"""
Checks the generated OKC screen against the PowerWash dirt-mask rules and
basic mesh hygiene. Prints VERIFY: PASS or VERIFY: FAIL.

Run headless (after build_okc_screen.py):
  blender -b OKC_TransformerScreen.blend --python verify_okc_screen.py
"""
import bpy
import bmesh
import math
import os
import sys
import numpy as np
from mathutils import Vector

RES = 2048
ERODE_PX = 0.35
R_OUTER = 4.50
THICK = 0.008

failures = []


def check(cond, msg):
    status = "ok  " if cond else "FAIL"
    print(f"[verify] {status} {msg}", flush=True)
    if not cond:
        failures.append(msg)


def uv_triangles(me, layer_name):
    """All faces of `me` as UV-space triangles from the named UV layer."""
    uv = me.uv_layers[layer_name].data
    me.calc_loop_triangles()
    tris = np.empty((len(me.loop_triangles), 3, 2), dtype=np.float64)
    for i, lt in enumerate(me.loop_triangles):
        for j, li in enumerate(lt.loops):
            tris[i, j] = uv[li].uv
    return tris


def rasterize_coverage(tris, res):
    """Count how many triangles cover each texel centre (after a tiny erosion)."""
    cov = np.zeros((res, res), dtype=np.int32)
    px = tris * res
    for t in px:
        c = t.mean(axis=0)
        d = t - c
        n = np.linalg.norm(d, axis=1, keepdims=True)
        n[n < 1e-9] = 1e-9
        t2 = t - d / n * ERODE_PX
        area2 = ((t2[1, 0] - t2[0, 0]) * (t2[2, 1] - t2[0, 1])
                 - (t2[1, 1] - t2[0, 1]) * (t2[2, 0] - t2[0, 0]))
        if abs(area2) < 1e-6:
            continue
        if area2 < 0:
            t2 = t2[::-1]
        x0 = max(int(math.floor(t2[:, 0].min())), 0)
        x1 = min(int(math.ceil(t2[:, 0].max())), res - 1)
        y0 = max(int(math.floor(t2[:, 1].min())), 0)
        y1 = min(int(math.ceil(t2[:, 1].max())), res - 1)
        if x1 < x0 or y1 < y0:
            continue
        xs = np.arange(x0, x1 + 1) + 0.5
        ys = np.arange(y0, y1 + 1) + 0.5
        X, Y = np.meshgrid(xs, ys)
        inside = np.ones_like(X, dtype=bool)
        for k in range(3):
            ax, ay = t2[k]
            bx, by = t2[(k + 1) % 3]
            inside &= (bx - ax) * (Y - ay) - (by - ay) * (X - ax) >= 0
        cov[y0:y1 + 1, x0:x1 + 1] += inside.astype(np.int32)
    return cov


def save_coverage_png(cov, path):
    res = cov.shape[0]
    rgba = np.zeros((res, res, 4), dtype=np.float32)
    rgba[..., 3] = 1.0
    rgba[cov == 1, 1] = 0.8
    rgba[cov >= 2, 0] = 1.0
    img = bpy.data.images.new("uv1_coverage", res, res, alpha=True)
    img.pixels.foreach_set(rgba.ravel())
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)


def verify_mesh(ob, expect_closed, out_dir):
    me = ob.data
    print(f"[verify] --- {ob.name}: {len(me.vertices)} verts, {len(me.polygons)} faces")
    names = [l.name for l in me.uv_layers]
    check(names[:2] == ["UVMap", "DirtMask"], f"{ob.name}: UV layers are {names}")
    if "DirtMask" not in names:
        return

    tris = uv_triangles(me, "DirtMask")
    lo, hi = tris.min(), tris.max()
    check(lo >= -1e-6 and hi <= 1 + 1e-6,
          f"{ob.name}: DirtMask bounds [{lo:.4f}, {hi:.4f}] inside 0..1")

    cov = rasterize_coverage(tris, RES)
    covered = int((cov >= 1).sum())
    overlap = int((cov >= 2).sum())
    ratio = overlap / max(covered, 1)
    check(ratio < 1e-4, f"{ob.name}: DirtMask overlap {overlap} of {covered} covered texels "
                        f"({ratio:.5%}) at {RES}x{RES}; utilisation {covered / RES / RES:.1%}")
    save_coverage_png(cov, os.path.join(out_dir, f"uv_coverage_{ob.name}.png"))

    bm = bmesh.new()
    bm.from_mesh(me)
    bm.normal_update()
    non_manifold = sum(1 for e in bm.edges if len(e.link_faces) != 2)
    if expect_closed:
        check(non_manifold == 0, f"{ob.name}: closed shells, non-manifold edges = {non_manifold}")
    else:
        print(f"[verify] info {ob.name}: open edges = {non_manifold} (bottom left open on purpose)")
    degenerate = sum(1 for f in bm.faces if f.calc_area() < 1e-10)
    check(degenerate == 0, f"{ob.name}: degenerate faces = {degenerate}")
    loose = sum(1 for v in bm.verts if not v.link_faces)
    check(loose == 0, f"{ob.name}: loose vertices = {loose}")

    if expect_closed:
        bad = 0
        radial_faces = 0
        for f in bm.faces:
            c = f.calc_center_median()
            r = Vector((c.x, c.y, 0.0))
            rad = r.length
            if rad < 1e-6:
                continue
            d = f.normal.dot(r / rad)
            if abs(d) > 0.9:
                radial_faces += 1
                outer = rad > R_OUTER - THICK / 2
                if outer != (d > 0):
                    bad += 1
        check(bad == 0, f"{ob.name}: radial faces pointing the wrong way = {bad} of {radial_faces}")
    bm.free()

    sharp = sum(1 for e in me.edges if e.use_edge_sharp)
    smooth_faces = sum(1 for p in me.polygons if p.use_smooth)
    print(f"[verify] info {ob.name}: sharp edges {sharp}, smooth faces {smooth_faces}/{len(me.polygons)}")


def main():
    base = os.path.dirname(bpy.data.filepath)
    out_dir = os.path.join(base, "preview")
    os.makedirs(out_dir, exist_ok=True)
    # split build: OKC_Panel_01..14, each with its own 0..1 DirtMask; merged build: one OKC_Screen_Panels
    panels = sorted((ob for ob in bpy.data.objects
                     if ob.type == 'MESH' and (ob.name.startswith("OKC_Panel_") or ob.name == "OKC_Screen_Panels")),
                    key=lambda ob: ob.name)
    plinth = bpy.data.objects.get("OKC_Screen_Plinth")
    check(len(panels) in (1, 14), f"panel objects: {[ob.name for ob in panels]}")
    check(plinth is not None, "OKC_Screen_Plinth exists")
    if panels:
        pts = np.array([ob.matrix_world @ v.co for ob in panels for v in ob.data.vertices])
        d = pts.max(axis=0) - pts.min(axis=0)
        check(abs(d[0] - 2 * R_OUTER) < 0.01 and abs(d[1] - 2 * R_OUTER) < 0.01,
              f"screen footprint {d[0]:.3f} x {d[1]:.3f} m, height {d[2]:.3f} m")
        for ob in panels:
            verify_mesh(ob, True, out_dir)
    if plinth:
        verify_mesh(plinth, False, out_dir)
    for name in ("OKC_TransformerScreen.fbx", "OKC_TransformerScreen.glb"):
        p = os.path.join(base, "export", name)
        if os.path.isfile(p):
            check(os.path.getsize(p) > 10_000, f"export present: {name} ({os.path.getsize(p) / 1e6:.2f} MB)")
        else:
            check(False, f"export present: {name}")
    if failures:
        print(f"VERIFY: FAIL ({len(failures)} problems)")
        for f in failures:
            print(f"  - {f}")
        sys.exit(1)
    print("VERIFY: PASS")


if __name__ == "__main__":
    main()
