"""
Procedurally builds the plaza around the OKC transformer screen: a downtown square of concrete pavers on a
street corner, with a sidewalk, street curb and road on the south side, raised planters with small trees,
benches, bollards, lamp posts, a grass verge and simple building blocks on the skyline. Scenery only: nothing
here is a wash target. Also writes invisible boundary boxes (OKC_Plaza_COL_*) that Unity puts on the game's
NavigationCollision layer.

Blender Z is up; the ring (build_okc_screen.py) is centred on the origin with its curb (r = 4.58 m) standing on
the paving at z = 0. "South" is -Y here; the FBX export converts to Unity Y-up exactly like the ring's export.

Outputs in export/:
  OKC_Plaza.fbx         one empty OKC_Plaza with every piece as a child (one material per object)
  OKC_Plaza.json        object -> material / collider / layer, and material -> texture / colour / tiling
  plaza_tex/*.png       tileable textures generated here (pavers, concrete, asphalt, grass, facades)

Run headless:
  blender -b --python build_okc_plaza.py
"""
import bpy
import bmesh
import json
import math
import os
import random
import struct
import zlib
import numpy as np
from mathutils import Matrix, Vector

SEED = 20260923

PLAZA_HALF = 12.0          # paved square is 24 x 24 m
SIDEWALK_W = 3.0           # sidewalk south of the square
CURB_H = 0.15              # street curb drop
ROAD_W = 12.0              # road beyond the curb
GROUND_HALF = 120.0        # grass/dirt ground plane
BOUNDARY_H = 3.0
BOUNDARY_T = 0.5

TEX_RES = 512

# material -> (texture file or None, colour multiplier, metallic, smoothness, metres per texture repeat (u, v))
MATERIALS = {
    "OKC_Pavers":    ("pavers.png",   (1.0, 1.0, 1.0), 0.0, 0.25, (1.2, 1.2)),
    "OKC_Sidewalk":  ("concrete.png", (1.0, 1.0, 1.0), 0.0, 0.20, (1.5, 1.5)),
    "OKC_CurbStone": ("concrete.png", (0.92, 0.92, 0.9), 0.0, 0.15, (1.5, 1.5)),
    "OKC_Asphalt":   ("asphalt.png",  (1.0, 1.0, 1.0), 0.0, 0.30, (4.0, 4.0)),
    "OKC_RoadPaint": (None, (0.85, 0.78, 0.35), 0.0, 0.35, (1.0, 1.0)),
    "OKC_Grass":     ("grass.png",    (1.0, 1.0, 1.0), 0.0, 0.10, (2.0, 2.0)),
    "OKC_Planter":   ("concrete.png", (0.80, 0.78, 0.74), 0.0, 0.15, (1.5, 1.5)),
    "OKC_Soil":      (None, (0.22, 0.16, 0.11), 0.0, 0.05, (1.0, 1.0)),
    "OKC_Bark":      (None, (0.30, 0.22, 0.16), 0.0, 0.10, (1.0, 1.0)),
    "OKC_Foliage":   (None, (0.24, 0.42, 0.16), 0.0, 0.15, (1.0, 1.0)),
    "OKC_BenchWood": (None, (0.52, 0.36, 0.22), 0.0, 0.35, (1.0, 1.0)),
    "OKC_DarkMetal": (None, (0.10, 0.11, 0.12), 0.8, 0.45, (1.0, 1.0)),
    "OKC_LampGlass": (None, (1.00, 0.95, 0.85), 0.0, 0.80, (1.0, 1.0)),
    "OKC_Facade_A":  ("facade.png",   (0.80, 0.62, 0.50), 0.0, 0.30, (4.0, 3.5)),
    "OKC_Facade_B":  ("facade.png",   (0.70, 0.72, 0.75), 0.2, 0.45, (4.0, 3.5)),
    "OKC_Facade_C":  ("facade.png",   (0.86, 0.82, 0.72), 0.0, 0.30, (4.0, 3.5)),
    "OKC_Roof":      (None, (0.30, 0.30, 0.31), 0.0, 0.10, (1.0, 1.0)),
}

objects_info = []


def log(msg):
    print(f"[plaza] {msg}", flush=True)


# ------------------------------------------------------------------ textures (tileable, numpy)
def tile_noise(res, cells, rng):
    """Tileable smooth value noise in 0..1, `cells` lattice cells across the tile."""
    lat = rng.random((cells, cells))
    x = np.arange(res) * cells / res
    i0 = np.floor(x).astype(int)
    f = x - i0
    f = f * f * (3 - 2 * f)
    i1 = (i0 + 1) % cells
    a = lat[np.ix_(i0, i0)]
    b = lat[np.ix_(i0, i1)]
    c = lat[np.ix_(i1, i0)]
    d = lat[np.ix_(i1, i1)]
    fy, fx = f[:, None], f[None, :]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def fbm(res, rng, base=4, octaves=4):
    out = np.zeros((res, res))
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        out += amp * tile_noise(res, base * 2 ** o, rng)
        tot += amp
        amp *= 0.5
    return out / tot


def write_png(path, rgb):
    """RGB float 0..1 array (row 0 = top) -> 8-bit PNG."""
    h, w, _ = rgb.shape
    data = (np.clip(rgb, 0, 1) * 255 + 0.5).astype(np.uint8)
    raw = b"".join(b"\x00" + data[y].tobytes() for y in range(h))

    def chunk(tag, payload):
        return struct.pack(">I", len(payload)) + tag + payload + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF)

    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)))
        f.write(chunk(b"IDAT", zlib.compress(raw, 9)))
        f.write(chunk(b"IEND", b""))


def make_textures(out_dir):
    rng = np.random.default_rng(SEED)
    os.makedirs(out_dir, exist_ok=True)
    r = TEX_RES
    yy, xx = np.mgrid[0:r, 0:r] / r

    # pavers: 2 x 2 pavers of 0.6 m per 1.2 m tile, light grout joints, per-paver tone
    n = fbm(r, rng, 8)
    tone = np.where((xx < 0.5) == (yy < 0.5), 0.03, -0.02) + np.where(xx < 0.5, 0.015, -0.01)
    joint = (np.minimum.reduce([xx % 0.5, 0.5 - xx % 0.5, yy % 0.5, 0.5 - yy % 0.5]) < 0.006)
    base = 0.70 + tone + (n - 0.5) * 0.10
    pav = np.stack([base * 1.00, base * 0.97, base * 0.92], axis=-1)
    pav[joint] = pav[joint] * 0.62
    write_png(os.path.join(out_dir, "pavers.png"), pav)

    # concrete: 1.5 m slabs with a saw joint on two edges
    n = fbm(r, rng, 6) * 0.7 + fbm(r, rng, 32, 2) * 0.3
    base = 0.66 + (n - 0.5) * 0.14
    con = np.stack([base, base * 0.985, base * 0.95], axis=-1)
    j = (xx < 0.004) | (yy < 0.004)
    con[j] *= 0.6
    write_png(os.path.join(out_dir, "concrete.png"), con)

    # asphalt: dark, speckled
    n = fbm(r, rng, 16, 3)
    speck = rng.random((r, r)) ** 18
    base = 0.17 + (n - 0.5) * 0.06 + speck * 0.25
    write_png(os.path.join(out_dir, "asphalt.png"), np.stack([base, base, base * 1.03], axis=-1))

    # grass: mottled green with dry patches
    n = fbm(r, rng, 6)
    m = fbm(r, rng, 24, 2)
    g = np.stack([0.20 + n * 0.12 + m * 0.05, 0.33 + n * 0.14, 0.12 + n * 0.05], axis=-1)
    write_png(os.path.join(out_dir, "grass.png"), g)

    # facade: one 4 m x 3.5 m bay, window band with frames, light wall (tinted per material)
    wall = 0.82 + (fbm(r, rng, 8, 2) - 0.5) * 0.06
    fac = np.stack([wall, wall, wall], axis=-1)
    win = (xx > 0.12) & (xx < 0.88) & (yy > 0.25) & (yy < 0.80)
    glass = np.stack([0.16 + yy * 0.10, 0.22 + yy * 0.12, 0.30 + yy * 0.14], axis=-1)
    fac[win] = glass[win]
    mull = win & ((np.abs(xx - 0.5) < 0.01) | (np.abs(yy - 0.52) < 0.008))
    fac[mull] = 0.25
    write_png(os.path.join(out_dir, "facade.png"), fac)
    log(f"textures written to {out_dir}")


# ------------------------------------------------------------------ mesh helpers
def box_uv(bm, uv):
    """World-space box mapping, 1 UV unit = 1 m (materials scale it to their texture period)."""
    bm.normal_update()
    for f in bm.faces:
        n = f.normal
        ax = max(range(3), key=lambda k: abs(n[k]))
        for lp in f.loops:
            co = lp.vert.co
            if ax == 2:
                lp[uv].uv = (co.x, co.y)
            elif ax == 0:
                lp[uv].uv = (co.y * (1 if n.x > 0 else -1), co.z)
            else:
                lp[uv].uv = (co.x * (-1 if n.y > 0 else 1), co.z)


def new_object(name, material, collection, bm, collider="mesh", layer="Default"):
    uv = bm.loops.layers.uv.get("UVMap") or bm.loops.layers.uv.new("UVMap")
    box_uv(bm, uv)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.validate(verbose=False)
    for p in me.polygons:
        p.use_smooth = False
    ob = bpy.data.objects.new(name, me)
    collection.objects.link(ob)
    objects_info.append({"name": name, "material": material or "", "collider": collider, "layer": layer})
    return ob


def add_box(bm, cx, cy, z0, sx, sy, sz, rot=0.0):
    m = Matrix.Translation((cx, cy, z0 + sz / 2)) @ Matrix.Rotation(rot, 4, 'Z') @ Matrix.Diagonal((sx, sy, sz, 1))
    bmesh.ops.create_cube(bm, size=1.0, matrix=m)


def add_cylinder(bm, cx, cy, z0, r, h, seg=12):
    m = Matrix.Translation((cx, cy, z0 + h / 2))
    bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r, radius2=r, depth=h, matrix=m)


def add_blob(bm, cx, cy, cz, r, squash, rng):
    m = Matrix.Translation((cx, cy, cz)) @ Matrix.Diagonal((r, r, r * squash, 1))
    res = bmesh.ops.create_icosphere(bm, subdivisions=2, radius=1.0, matrix=m)
    for v in res["verts"]:
        v.co += (v.co - Vector((cx, cy, cz))).normalized() * rng.uniform(-0.12, 0.12) * r


def box_object(name, material, coll, cx, cy, z0, sx, sy, sz, **kw):
    bm = bmesh.new()
    add_box(bm, cx, cy, z0, sx, sy, sz)
    return new_object(name, material, coll, bm, **kw)


# ------------------------------------------------------------------ plaza pieces
def build(coll, rng):
    H, S = PLAZA_HALF, SIDEWALK_W
    y_curb = -H - S                                   # street-side edge of the sidewalk

    # paving: a 0.3 m slab whose top is z = 0 (the ring's curb stands on it)
    box_object("OKC_Plaza_Paving", "OKC_Pavers", coll, 0, 0, -0.3, 2 * H, 2 * H, 0.3)
    box_object("OKC_Plaza_Sidewalk", "OKC_Sidewalk", coll, 0, -H - S / 2, -0.3, 2 * H + 2 * S, S, 0.3)
    # street curb: a 0.2 m kerb stone whose top is flush with the sidewalk, face dropping to the road
    box_object("OKC_Plaza_StreetCurb", "OKC_CurbStone", coll, 0, y_curb - 0.1, -0.35, 2 * H + 2 * S + 60, 0.2, 0.35)
    box_object("OKC_Plaza_Road", "OKC_Asphalt", coll, 0, y_curb - 0.2 - ROAD_W / 2, -CURB_H - 0.3, 2 * GROUND_HALF, ROAD_W, 0.3)
    # road markings: dashed centre line (paint slightly above the asphalt)
    bm = bmesh.new()
    yc = y_curb - 0.2 - ROAD_W / 2
    x = -GROUND_HALF + 1.5
    while x < GROUND_HALF - 1.5:
        add_box(bm, x, yc, -CURB_H - 0.0005, 3.0, 0.15, 0.003)
        x += 7.5
    new_object("OKC_Plaza_RoadMarkings", "OKC_RoadPaint", coll, bm, collider="none")
    # far sidewalk across the road
    box_object("OKC_Plaza_FarSidewalk", "OKC_Sidewalk", coll, 0, y_curb - 0.2 - ROAD_W - 2.0, -0.3, 2 * GROUND_HALF, 4.0, 0.3)
    # grass everywhere else, just below the paving
    box_object("OKC_Plaza_Ground", "OKC_Grass", coll, 0, 0, -0.35, 2 * GROUND_HALF, 2 * GROUND_HALF, 0.3)

    # raised planters with a small tree each, on the diagonals
    trunks, canopies, soils, planters = bmesh.new(), bmesh.new(), bmesh.new(), bmesh.new()
    for sx in (-1, 1):
        for sy in (-1, 1):
            cx, cy = sx * 8.6, sy * 8.6
            for dx, dy, lx, ly in ((0, 1.1, 2.4, 0.2), (0, -1.1, 2.4, 0.2), (1.1, 0, 0.2, 2.0), (-1.1, 0, 0.2, 2.0)):
                add_box(planters, cx + dx, cy + dy, 0, lx, ly, 0.55)
            add_box(soils, cx, cy, 0, 2.0, 2.0, 0.45)
            h = rng.uniform(2.6, 3.2)
            add_cylinder(trunks, cx, cy, 0.45, 0.11, h, 8)
            for k in range(3):
                add_blob(canopies, cx + rng.uniform(-0.4, 0.4), cy + rng.uniform(-0.4, 0.4),
                         0.45 + h + rng.uniform(-0.2, 0.5), rng.uniform(1.0, 1.35), 0.8, rng)
    new_object("OKC_Plaza_Planters", "OKC_Planter", coll, planters)
    new_object("OKC_Plaza_PlanterSoil", "OKC_Soil", coll, soils, collider="none")
    new_object("OKC_Plaza_TreeTrunks", "OKC_Bark", coll, trunks)
    new_object("OKC_Plaza_TreeCanopies", "OKC_Foliage", coll, canopies, collider="none")

    # benches facing the ring on the north, east and west axes (seat + back in wood, legs in metal)
    wood, legs = bmesh.new(), bmesh.new()
    for ang in (math.pi / 2, 0.0, math.pi):
        d = 8.4
        cx, cy = d * math.cos(ang), d * math.sin(ang)
        rot = ang + math.pi / 2                    # bench length runs tangentially
        out = Vector((math.cos(ang), math.sin(ang), 0))
        add_box(wood, cx, cy, 0.42, 1.9, 0.45, 0.06, rot)
        bx, by = cx + out.x * 0.24, cy + out.y * 0.24
        add_box(wood, bx, by, 0.55, 1.9, 0.05, 0.38, rot)
        t = Vector((-math.sin(ang), math.cos(ang), 0))
        for s in (-0.8, 0.8):
            add_box(legs, cx + t.x * s, cy + t.y * s, 0, 0.06, 0.45, 0.42, rot)
    new_object("OKC_Plaza_BenchWood", "OKC_BenchWood", coll, wood)
    new_object("OKC_Plaza_BenchLegs", "OKC_DarkMetal", coll, legs)

    # bollards along the street edge of the sidewalk, lamp posts at the square's corners
    metal, glass = bmesh.new(), bmesh.new()
    x = -H - S + 1.5
    while x <= H + S - 1.5 + 1e-6:
        add_cylinder(metal, x, y_curb + 0.6, 0, 0.1, 0.95, 10)
        x += 3.0
    for sx in (-1, 1):
        for sy in (-1, 1):
            cx, cy = sx * (H - 0.7), sy * (H - 0.7)
            add_cylinder(metal, cx, cy, 0, 0.09, 5.0, 10)
            arm = Vector((-sx, -sy, 0)).normalized() * 0.45
            add_box(metal, cx + arm.x, cy + arm.y, 4.9, 0.9, 0.08, 0.08, math.atan2(arm.y, arm.x))
            add_box(glass, cx + arm.x * 2, cy + arm.y * 2, 4.62, 0.35, 0.35, 0.28)
    new_object("OKC_Plaza_Metal", "OKC_DarkMetal", coll, metal)
    new_object("OKC_Plaza_LampHeads", "OKC_LampGlass", coll, glass, collider="none")

    # skyline: building blocks beyond the grass (north, east, west) and across the road (south)
    fac = {"OKC_Facade_A": bmesh.new(), "OKC_Facade_B": bmesh.new(), "OKC_Facade_C": bmesh.new()}
    roofs = bmesh.new()
    names = list(fac)
    spots = []
    for k in range(-3, 4):
        spots.append((k * 16.0 + rng.uniform(-2, 2), 34 + rng.uniform(0, 8)))            # north
        spots.append((34 + rng.uniform(0, 8), k * 16.0 + rng.uniform(-2, 2)))            # east
        spots.append((-34 - rng.uniform(0, 8), k * 16.0 + rng.uniform(-2, 2)))           # west
        spots.append((k * 16.0 + rng.uniform(-2, 2), y_curb - ROAD_W - 14 - rng.uniform(0, 6)))  # south
    for i, (cx, cy) in enumerate(spots):
        w, dpt = rng.uniform(11, 15), rng.uniform(11, 15)
        h = rng.choice((10.5, 14.0, 17.5, 24.5, 31.5, 42.0))
        add_box(fac[names[i % 3]], cx, cy, -0.3, w, dpt, h + 0.3)
        add_box(roofs, cx, cy, h, w - 0.2, dpt - 0.2, 0.25)
    for n, bm in fac.items():
        # roof caps get the roof material; facade top faces stay but are hidden by the cap
        new_object(f"OKC_Plaza_Buildings_{n[-1]}", n, coll, bm, collider="none")
    new_object("OKC_Plaza_Roofs", "OKC_Roof", coll, roofs, collider="none")

    # boundary: invisible walls around square + sidewalk (road side blocked at the street curb)
    t, h = BOUNDARY_T, BOUNDARY_H
    x0, x1, y0, y1 = -H - S, H + S, y_curb, H
    box_object("OKC_Plaza_COL_North", None, coll, 0, y1 + t / 2, 0, x1 - x0 + 2 * t, t, h, collider="box", layer="NavigationCollision")
    box_object("OKC_Plaza_COL_South", None, coll, 0, y0 - t / 2, 0, x1 - x0 + 2 * t, t, h, collider="box", layer="NavigationCollision")
    box_object("OKC_Plaza_COL_East", None, coll, x1 + t / 2, (y0 + y1) / 2, 0, t, y1 - y0, h, collider="box", layer="NavigationCollision")
    box_object("OKC_Plaza_COL_West", None, coll, x0 - t / 2, (y0 + y1) / 2, 0, t, y1 - y0, h, collider="box", layer="NavigationCollision")


def export(out_dir, objects):
    os.makedirs(out_dir, exist_ok=True)
    for ob in bpy.data.objects:
        ob.select_set(ob in objects)
    path = os.path.join(out_dir, "OKC_Plaza.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'EMPTY', 'MESH'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', global_scale=1.0,
                             axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                             use_mesh_modifiers=True, mesh_smooth_type='FACE', use_tspace=False,
                             add_leaf_bones=False, path_mode='AUTO', colors_type='NONE', embed_textures=False)
    log(f"exported {path}")
    info = {"objects": objects_info,
            "materials": [{"name": k, "texture": v[0] or "", "color": list(v[1]), "metallic": v[2], "smoothness": v[3],
                           "metresPerRepeat": list(v[4])} for k, v in MATERIALS.items()]}
    with open(os.path.join(out_dir, "OKC_Plaza.json"), "w", encoding="utf-8") as f:
        json.dump(info, f, indent=2)


def main():
    base = os.path.dirname(os.path.abspath(__file__))
    out_dir = os.path.join(base, "export")
    bpy.ops.wm.read_factory_settings(use_empty=True)
    coll = bpy.data.collections.new("OKC_Plaza")
    bpy.context.scene.collection.children.link(coll)
    root = bpy.data.objects.new("OKC_Plaza", None)
    coll.objects.link(root)
    rng = random.Random(SEED)
    make_textures(os.path.join(out_dir, "plaza_tex"))
    build(coll, rng)
    obs = [o for o in coll.objects if o.type == 'MESH']
    for o in obs:
        o.parent = root
    tris = 0
    for o in obs:
        o.data.calc_loop_triangles()
        tris += len(o.data.loop_triangles)
        bad = [p for p in o.data.polygons if p.area < 1e-8]
        if bad:
            raise SystemExit(f"{o.name}: {len(bad)} degenerate faces")
    export(out_dir, set(obs) | {root})
    log(f"SUMMARY objects={len(obs)} tris={tris}")
    print("PLAZA: PASS")


if __name__ == "__main__":
    main()
