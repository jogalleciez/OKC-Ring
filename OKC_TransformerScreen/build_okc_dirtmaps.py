"""
Paints one PowerWash control map per wash-target object of the OKC screen (split build: OKC_Panel_01..14 and
OKC_Screen_Plinth), in each object's own DirtMask UV space (Unity TexCoord2 after import).

Channel meaning (docs/phase0-findings.md section 6): R, G, B = opacity of palette layers 0, 1, 2; alpha unused (0).
Only texels whose centre lies on the mesh get dirt, so every dirty texel can be washed (the game counts all of
them as start dirtiness); faces looking into the closed ring stay clean.

The dirt is layered the way the game's own levels are (the Tram's rust-flake doors): a tough bottom layer under easy
coats, nested masks, soft edges. Palettes are set in OkcPack.ImportRing and must list the layers in this order.
  Panels (TPL_OKC)       R  layer 0 rust flakes (Dirt_PW2_PaintedFlakesRust, Tier 6)  weathered patches, more of
                            them low down and near the panel seams (RUST_COVERAGE of each panel)
                         G  layer 1 grime       (Dirt_PW2_Grime, Tier 1)              every texel, varying opacity
                         B  layer 2 mud splats  (Dirt_PW2_MudSplats, Tier 1)          noise blobs, more low down
  Curb   (TPL_OKC_Curb)  R  layer 0 slimy grime (Dirt_PW2_GrimeGreenSlimy, Tier 1)    every texel, varying opacity
                         G  layer 1 sand        (Dirt_PW2_SandDarker, Tier 1)         patches, more low down
                         B  layer 2 cracked mud (Dirt_PW2_CrackedMud, Tier 3)         patches inside the sand
Every non-zero value is >= MIN_DIRT: the game counts a texel as dirty somewhere between 33 and 64, so with no values
below 64 its start dirtiness equals OkcPack.CountDirt (any channel > 0).

Writes export/dirt/CM_<object>.png (RGBA8, row 0 = top as PNG expects) and export/dirt/dirtmaps.json.

Run headless (after build_okc_screen.py):
  blender -b OKC_TransformerScreen.blend --python build_okc_dirtmaps.py
"""
import bpy
import json
import math
import os
import struct
import zlib
import numpy as np

RES = 1024
PLINTH_H = 0.15
MIN_DIRT = 64             # lowest non-zero opacity (see the docstring)
SOFT_WIDTH = 0.08         # noise units over which a mask edge ramps from MIN_DIRT to 255
RUST_COVERAGE = 0.35      # share of each panel's reachable texels under rust flakes
EDGE_BAND = 0.35          # metres from a panel seam where rust is more likely
MUD_THRESHOLD = 0.60      # higher = fewer mud blobs
SAND_THRESHOLD = 0.55     # curb: higher = less sand
CRACK_THRESHOLD = 0.62    # curb: higher = less cracked mud
SEED = 20260922


def object_triangles(ob):
    """(uv, world) arrays of shape (n, 3, 2) and (n, 3, 3) for the loop triangles of `ob` a player can reach.

    The ring is closed (panels stand shoulder to shoulder on the curb), so faces looking at the ring's centre
    -- the panels' inner skins and the curb's inner wall -- cannot be washed and stay clean.
    """
    me = ob.data
    me.calc_loop_triangles()
    uv_layer = me.uv_layers["DirtMask"].data
    mw = np.array(ob.matrix_world)
    co = np.array([v.co for v in me.vertices])
    co = co @ mw[:3, :3].T + mw[:3, 3]
    uvs, wps = [], []
    for lt in me.loop_triangles:
        w = co[list(lt.vertices)]
        c = w.mean(axis=0)
        nrm = mw[:3, :3] @ np.array(lt.normal)
        radial = np.array([c[0], c[1], 0.0])
        rl = np.linalg.norm(radial)
        if rl > 1e-6 and nrm @ (radial / rl) < -0.5:
            continue
        uvs.append([uv_layer[li].uv for li in lt.loops])
        wps.append(w)
    return np.array(uvs, dtype=np.float64), np.array(wps, dtype=np.float64)


def rasterize_positions(uv, wp, res):
    """World position of each texel centre covered by a triangle (NaN where uncovered)."""
    pos = np.full((res, res, 3), np.nan)
    px = uv * res
    for t, w in zip(px, wp):
        area2 = (t[1, 0] - t[0, 0]) * (t[2, 1] - t[0, 1]) - (t[1, 1] - t[0, 1]) * (t[2, 0] - t[0, 0])
        if abs(area2) < 1e-9:
            continue
        x0 = max(int(math.floor(t[:, 0].min())), 0)
        x1 = min(int(math.ceil(t[:, 0].max())), res - 1)
        y0 = max(int(math.floor(t[:, 1].min())), 0)
        y1 = min(int(math.ceil(t[:, 1].max())), res - 1)
        if x1 < x0 or y1 < y0:
            continue
        X, Y = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        # barycentric coordinates
        l0 = ((t[1, 0] - X) * (t[2, 1] - Y) - (t[1, 1] - Y) * (t[2, 0] - X)) / area2
        l1 = ((t[2, 0] - X) * (t[0, 1] - Y) - (t[2, 1] - Y) * (t[0, 0] - X)) / area2
        l2 = 1.0 - l0 - l1
        inside = (l0 >= 0) & (l1 >= 0) & (l2 >= 0)
        if not inside.any():
            continue
        p = l0[..., None] * w[0] + l1[..., None] * w[1] + l2[..., None] * w[2]
        block = pos[y0:y1 + 1, x0:x1 + 1]
        block[inside] = p[inside]
    return pos


def value_noise(p, scale, seed):
    """Smooth 3D value noise in 0..1 at points p (n, 3)."""
    q = p * scale
    i = np.floor(q).astype(np.int64)
    f = q - i
    f = f * f * (3 - 2 * f)

    def h(ix, iy, iz):
        v = (ix * 73856093) ^ (iy * 19349663) ^ (iz * 83492791) ^ seed
        v = (v ^ (v >> 13)) * 1274126177
        return ((v ^ (v >> 16)) & 0xFFFF) / 65535.0

    out = 0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                wx = f[:, 0] if dx else 1 - f[:, 0]
                wy = f[:, 1] if dy else 1 - f[:, 1]
                wz = f[:, 2] if dz else 1 - f[:, 2]
                out = out + wx * wy * wz * h(i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz)
    return out


def fbm(p, scale, seed):
    return (0.6 * value_noise(p, scale, seed) + 0.3 * value_noise(p, scale * 2.3, seed + 1)
            + 0.1 * value_noise(p, scale * 5.1, seed + 2))


def soft(x, edge, width=SOFT_WIDTH):
    """Mask with a soft edge: 0 where x <= edge, else MIN_DIRT..255 over `width` (smoothstep)."""
    t = np.clip((x - edge) / width, 0, 1)
    v = MIN_DIRT + (255 - MIN_DIRT) * t * t * (3 - 2 * t)
    return np.where(x > edge, np.round(v), 0).astype(np.uint8)


def coat(p, scale, seed, bias=0.0):
    """Opacity MIN_DIRT..255 on every point: a coat that covers the whole surface, thicker in places."""
    return np.clip(np.round(80 + 160 * fbm(p, scale, seed) + 60 * bias), MIN_DIRT, 255).astype(np.uint8)


def seam_distance(p):
    """Distance (m) along the ring to the nearer side seam of the panel the points `p` lie on."""
    ang = np.arctan2(p[:, 1], p[:, 0])
    mid = math.atan2(p[:, 1].mean(), p[:, 0].mean())
    rel = (ang - mid + math.pi) % (2 * math.pi) - math.pi      # angle about the panel's centre, no wrap
    radius = np.hypot(p[:, 0], p[:, 1])
    return np.minimum(rel - rel.min(), rel.max() - rel) * radius


def paint(pos, is_plinth):
    res = pos.shape[0]
    covered = ~np.isnan(pos[..., 0])
    rgba = np.zeros((res, res, 4), dtype=np.uint8)
    p = pos[covered]
    if is_plinth:
        z = p[:, 2]                                           # 0 at the ground, PLINTH_H at the curb top
        low = np.clip(1 - z / PLINTH_H, 0, 1)
        r = coat(p, 1.2, SEED + 50, low)
        g = soft(fbm(p, 2.0, SEED + 10) + 0.25 * low, SAND_THRESHOLD)
        b = soft(fbm(p, 1.4, SEED + 40) + 0.2 * low, CRACK_THRESHOLD)
        b[g == 0] = 0                                         # cracked mud only inside the sand
    else:
        h = p[:, 2] - PLINTH_H                                 # height above the curb top (Blender Z up)
        low = np.clip(1 - h / 1.2, 0, 1)
        near_seam = np.clip(1 - seam_distance(p) / EDGE_BAND, 0, 1)
        rust = fbm(p, 2.2, SEED + 20) + 0.30 * low + 0.25 * near_seam
        r = soft(rust, np.quantile(rust, 1 - RUST_COVERAGE))
        g = coat(p, 0.8, SEED + 30, low)
        b = soft(fbm(p, 1.6, SEED) + np.clip(0.5 - h, 0, 0.5) * 0.35, MUD_THRESHOLD)
    rgba[covered, 0] = r
    rgba[covered, 1] = g
    rgba[covered, 2] = b
    return rgba, covered


def check(name, rgba, covered, is_plinth):
    """Problems with one control map (empty list = fine)."""
    c = rgba[..., :3]
    dirty = c.max(axis=2) > 0
    r, g, b = (c[..., i] > 0 for i in range(3))
    bad = []
    if ((c > 0) & (c < MIN_DIRT)).any():
        bad.append(f"values in 1..{MIN_DIRT - 1}")
    if (dirty & ~covered).any():
        bad.append("dirt outside the mesh")
    base = r if is_plinth else g                              # the coat every other layer sits in
    if (covered & ~base).any():
        bad.append("base coat does not cover every texel")
    if (b & ~g).any():
        bad.append("layer 2 not inside layer 1")
    if not is_plinth:
        share = r.sum() / covered.sum()
        if abs(share - RUST_COVERAGE) > 0.03:
            bad.append(f"rust share {share:.3f}")
    return [f"{name}: {m}" for m in bad]


def write_png(path, rgba):
    """RGBA8 PNG; rgba[0] is the UV v=0 row, which is the bottom of the image."""
    h, w, _ = rgba.shape
    raw = b"".join(b"\x00" + rgba[y].tobytes() for y in range(h - 1, -1, -1))

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)))
        f.write(chunk(b"IDAT", zlib.compress(raw, 9)))
        f.write(chunk(b"IEND", b""))


def main():
    base = os.path.dirname(bpy.data.filepath)
    out_dir = os.path.join(base, "export", "dirt")
    os.makedirs(out_dir, exist_ok=True)
    targets = sorted((ob for ob in bpy.data.objects
                      if ob.type == 'MESH' and (ob.name.startswith("OKC_Panel_") or ob.name == "OKC_Screen_Plinth")),
                     key=lambda ob: ob.name)
    if len(targets) != 15:
        raise SystemExit(f"expected 14 panels + plinth (split build), found {[ob.name for ob in targets]}")
    info, problems = {}, []
    for ob in targets:
        uv, wp = object_triangles(ob)
        pos = rasterize_positions(uv, wp, RES)
        is_plinth = ob.name == "OKC_Screen_Plinth"
        rgba, covered = paint(pos, is_plinth)
        problems += check(ob.name, rgba, covered, is_plinth)
        dirty = int((rgba[..., :3].max(axis=2) > 0).sum())
        path = os.path.join(out_dir, f"CM_{ob.name}.png")
        write_png(path, rgba)
        layers = [int((rgba[..., c] > 0).sum()) for c in range(3)]
        info[ob.name] = {"file": os.path.basename(path), "resolution": RES, "startDirtiness": dirty,
                         "coveredTexels": int(covered.sum()), "layerTexels": layers,
                         "layerShare": [round(n / max(int(covered.sum()), 1), 4) for n in layers]}
        print(f"[dirt] {ob.name}: {dirty} dirty texels of {RES * RES} "
              f"(R {layers[0]}, G {layers[1]}, B {layers[2]}; shares {info[ob.name]['layerShare']})", flush=True)
    with open(os.path.join(out_dir, "dirtmaps.json"), "w", encoding="utf-8") as f:
        json.dump(info, f, indent=2)
    print(f"DIRTMAPS: {len(info)} written to {out_dir}")
    for m in problems:
        print(f"[dirt] FAIL {m}")
    print("DIRTMAPS: " + ("PASS" if not problems else f"FAIL ({len(problems)} problems)"))
    if problems:
        raise SystemExit(1)

if __name__ == "__main__":
    main()
