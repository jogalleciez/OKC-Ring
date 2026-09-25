"""
Procedurally builds the OKC transformer screen: a cylindrical perforated metal
wall (stencil O / K / C cut-outs on 14 panels) standing on a concrete curb.
Intended as a static prop for a PowerWash Simulator 2 mod.

The mesh carries two UV maps:
  UVMap     - tiling base-texture UVs, 1 m per UV unit
  DirtMask  - lightmap-style UVs for the PowerWash dirt mask:
              non-overlapping islands, all inside 0..1, with margins

By default (SPLIT_PANELS) every panel is its own object OKC_Panel_01..14 with its
own 0..1 DirtMask layout, so each can be a separate PowerWash wash target; pass
--merged for the single OKC_Screen_Panels object with one shared atlas.

Run headless:
  blender -b --python build_okc_screen.py -- [--out DIR] [--no-render] [--merged]
"""
import bpy
import bmesh
import math
import os
import sys
import random
import time
from mathutils import Vector
from mathutils.geometry import delaunay_2d_cdt

# ------------------------------------------------------------------ parameters
SEED        = 20260527
R_OUTER     = 4.50        # outer face radius of the screen (m)
THICK       = 0.008       # sheet thickness (m)
N_PANELS    = 14
PANEL_GAP   = 0.015       # seam between neighbouring panels (m)
PANEL_H     = 2.30        # panel height (m)
PLINTH_H    = 0.15        # concrete curb height; panels stand on top of it
PLINTH_OUT  = R_OUTER + 0.08
PLINTH_IN   = R_OUTER - THICK - 0.12
PLINTH_SEG  = 144
PLINTH_PIECES = 6         # dirt-UV islands for the curb

GLYPH_ROWS  = 4
ROW_PITCH   = 0.46
ROW0_V      = 0.40        # centre of the lowest glyph row above the panel bottom
ROW_V_JITTER = 0.08       # each letter bobs up/down by up to this much
GLYPH_R     = 0.16        # outer radius of a big O / C
SMALL_SCALE = 0.65        # small o / c relative to a big ring
STROKE      = 0.055       # stroke width of the letters (same for all sizes)
BRIDGE_HALF = 0.014       # half width of the stencil bridges that split the rings
GAP_TARGET  = 0.07        # nominal gap between neighbouring letters in a row
GAP_JITTER  = (-0.02, 0.04)
GLYPH_CLEAR = 0.04        # minimum clearance between any two letters
EDGE_CLEAR  = 0.03        # minimum clearance from a letter to the panel side edge
BOTTOM_BAND = 0.16        # solid band under the lowest letters
TOP_BAND    = 0.26        # solid band above the highest letters
LETTER_WEIGHTS = (("O", 0.34), ("C", 0.20), ("K", 0.24), ("o", 0.14), ("c", 0.08))

STEINER_DU  = 0.08        # interior support points so the flat panel bends smoothly
STEINER_DV  = 0.20
EDGE_DU     = 0.05        # subdivision of the top/bottom panel edges
EDGE_DV     = 0.25        # subdivision of the left/right panel edges

SPLIT_PANELS = True       # one object (and one dirt atlas) per panel; --merged turns it off
UV_COLS     = 6           # merged dirt atlas: 6 islands per row
UV_COLS_SPLIT = 2         # per-panel dirt atlas: outer and inner face side by side
UV_MARGIN   = 0.005
UV_GAP_RIM  = 0.0025
SHARP_ANGLE = math.radians(40)

CIRC        = 2 * math.pi * R_OUTER
PANEL_PITCH = CIRC / N_PANELS
PANEL_W     = PANEL_PITCH - PANEL_GAP

METAL_COLOR    = (0.33, 0.34, 0.36)
CONCRETE_COLOR = (0.62, 0.60, 0.56)


def log(msg):
    print(f"[okc] {msg}", flush=True)


# ------------------------------------------------------------------ 2D glyphs
def seg_count(r, a0, a1, step=0.022):
    return max(3, int(math.ceil(abs(a1 - a0) * r / step)))


def arc_piece(r_in, r_out, a0, a1):
    """Closed CCW polygon of an annular sector."""
    seg_out = seg_count(r_out, a0, a1)
    seg_in = seg_count(r_in, a0, a1)
    pts = [(r_out * math.cos(a0 + (a1 - a0) * i / seg_out),
            r_out * math.sin(a0 + (a1 - a0) * i / seg_out)) for i in range(seg_out + 1)]
    pts += [(r_in * math.cos(a0 + (a1 - a0) * i / seg_in),
             r_in * math.sin(a0 + (a1 - a0) * i / seg_in)) for i in range(seg_in, -1, -1)]
    return pts


def bar(p0, p1, w):
    """Closed CCW rectangle of width w along the segment p0 -> p1."""
    dx, dy = p1[0] - p0[0], p1[1] - p0[1]
    L = math.hypot(dx, dy)
    nx, ny = -dy / L * w / 2, dx / L * w / 2
    return [(p0[0] - nx, p0[1] - ny), (p1[0] - nx, p1[1] - ny),
            (p1[0] + nx, p1[1] + ny), (p0[0] + nx, p0[1] + ny)]


def rotate(poly, ang):
    c, s = math.cos(ang), math.sin(ang)
    return [(x * c - y * s, x * s + y * c) for x, y in poly]


def ring_split(r_out, cuts):
    """Ring of width STROKE split into arcs by stencil bridges centred on `cuts`."""
    r_in = r_out - STROKE
    gh = BRIDGE_HALF / ((r_in + r_out) / 2)
    cuts = sorted(cuts)
    pieces = []
    for k, c0 in enumerate(cuts):
        c1 = cuts[(k + 1) % len(cuts)] + (2 * math.pi if k == len(cuts) - 1 else 0.0)
        pieces.append(arc_piece(r_in, r_out, c0 + gh, c1 - gh))
    return pieces


def glyph_ring(rng, scale):
    """O (big, 2 or 3 bridges) or o (small, 2 bridges), at a random angle."""
    r_out = GLYPH_R * scale
    n = 3 if (scale >= 1.0 and rng.random() < 0.6) else 2
    base = rng.uniform(0, 2 * math.pi)
    return ring_split(r_out, [base + k * 2 * math.pi / n for k in range(n)])


def glyph_c(rng, scale):
    """C (big, one bridge opposite the opening) or c (small, no bridge)."""
    r_out = GLYPH_R * scale
    r_in = r_out - STROKE
    opening = rng.uniform(0, 2 * math.pi)
    half_open = math.radians(40)
    a0 = opening + half_open
    a1 = opening + 2 * math.pi - half_open
    if scale < 1.0:
        return [arc_piece(r_in, r_out, a0, a1)]
    gh = BRIDGE_HALF / ((r_in + r_out) / 2)
    mid = opening + math.pi
    return [arc_piece(r_in, r_out, a0, mid - gh), arc_piece(r_in, r_out, mid + gh, a1)]


def glyph_K(rng, scale=1.0):
    rot = rng.uniform(0, 2 * math.pi)
    stem_x = -0.085
    half_h = 0.15
    x0 = stem_x + STROKE / 2 + 0.045
    L = 0.16
    c45 = math.cos(math.pi / 4)
    pieces = [bar((stem_x, -half_h), (stem_x, half_h), STROKE),
              bar((x0, 0.035), (x0 + L * c45, 0.035 + L * c45), STROKE),
              bar((x0, -0.035), (x0 + L * c45, -0.035 - L * c45), STROKE)]
    return [rotate(p, rot) for p in pieces]


GLYPHS = {
    "O": (glyph_ring, 1.0),
    "o": (glyph_ring, SMALL_SCALE),
    "C": (glyph_c, 1.0),
    "c": (glyph_c, SMALL_SCALE),
    "K": (glyph_K, 1.0),
}
SMALL_LETTERS = tuple(l for l, (_, s) in GLYPHS.items() if s < 1.0)


def pick_letter(rng):
    r = rng.random()
    acc = 0.0
    for letter, w in LETTER_WEIGHTS:
        acc += w
        if r <= acc:
            return letter
    return LETTER_WEIGHTS[-1][0]


def make_glyph(rng, force_small=False):
    """Returns (letter, pieces in glyph-local metres, bounding radius)."""
    letter = rng.choice(SMALL_LETTERS) if force_small else pick_letter(rng)
    fn, scale = GLYPHS[letter]
    pieces = fn(rng, scale)
    r_b = max(math.hypot(x, y) for piece in pieces for x, y in piece)
    return letter, pieces, r_b


def layout_row(rng, v_base, W, H, prev_row):
    """Pack one row of letters across the panel width.

    Letters are drawn at random (mixed sizes), spaced with jittered gaps, and the
    leftover width is spread evenly so the row still reads as evenly spaced. Each
    letter then gets a vertical wobble that is kept clear of the row below.
    Returns a list of (cu, cv, r_b, pieces, letter).
    """
    u_min, u_max = EDGE_CLEAR, W - EDGE_CLEAR
    avail = u_max - u_min
    items, gaps = [], []
    used = 0.0
    while True:
        gap = GAP_TARGET + rng.uniform(*GAP_JITTER) if items else 0.0
        letter, pieces, r_b = make_glyph(rng)
        if used + gap + 2 * r_b > avail:
            letter, pieces, r_b = make_glyph(rng, force_small=True)
            if used + gap + 2 * r_b > avail:
                break
        items.append((letter, pieces, r_b))
        gaps.append(gap)
        used += gap + 2 * r_b
    extra = (avail - used) / (len(items) + 1)

    placed = []
    cursor = u_min + extra
    for (letter, pieces, r_b), gap in zip(items, gaps):
        cursor += gap + (extra if placed else 0.0)
        cu = cursor + r_b
        cursor = cu + r_b
        v_lo = BOTTOM_BAND + r_b
        v_hi = H - TOP_BAND - r_b
        cv = min(max(v_base, v_lo), v_hi)
        for _ in range(12):
            cand = min(max(v_base + rng.uniform(-ROW_V_JITTER, ROW_V_JITTER), v_lo), v_hi)
            if all(math.hypot(cu - pu, cand - pv) >= r_b + pr + GLYPH_CLEAR
                   for pu, pv, pr, _, _ in prev_row):
                cv = cand
                break
        placed.append((cu, cv, r_b, pieces, letter))
    return placed


# ------------------------------------------------------------------ flat panel
def build_panel_2d(rng):
    """Triangulate one flat panel (u along the wall, v up) with the glyph holes.

    Returns (verts2d, tris, letters) where tris index verts2d and are CCW.
    """
    W, H = PANEL_W, PANEL_H
    n_u = max(1, round(W / EDGE_DU))
    n_v = max(1, round(H / EDGE_DV))
    outer = [(W * i / n_u, 0.0) for i in range(n_u + 1)]             # bottom, left -> right
    outer += [(W, H * j / n_v) for j in range(1, n_v)]                 # right edge, up
    outer += [(W * i / n_u, H) for i in range(n_u, -1, -1)]            # top, right -> left
    outer += [(0.0, H * j / n_v) for j in range(n_v - 1, 0, -1)]       # left edge, down

    verts = list(outer)
    faces = [list(range(len(outer)))]
    glyphs = []
    prev_row = []
    for row in range(GLYPH_ROWS):
        placed = layout_row(rng, ROW0_V + row * ROW_PITCH, W, H, prev_row)
        glyphs += placed
        prev_row = placed
    letters = [g[4] for g in glyphs]
    for cu, cv, _, pieces, _ in glyphs:
        for piece in pieces:
            idx0 = len(verts)
            verts += [(cu + x, cv + y) for x, y in piece]
            faces.append(list(range(idx0, len(verts))))

    # Steiner points keep the fill triangles short so the bend is smooth.
    v = STEINER_DV / 2
    while v < H:
        u = STEINER_DU / 2
        while u < W:
            if all((u - cu) ** 2 + (v - cv) ** 2 > (r_b + 0.04) ** 2
                   for cu, cv, r_b, _, _ in glyphs):
                verts.append((u, v))
            u += STEINER_DU
        v += STEINER_DV

    vo, eo, fo, _, _, _ = delaunay_2d_cdt([Vector(p) for p in verts], [], faces, 2, 1e-6)
    verts2d = [(p.x, p.y) for p in vo]
    tris = []
    for f in fo:
        for k in range(1, len(f) - 1):          # fan, in case a non-triangle sneaks through
            a, b, c = f[0], f[k], f[k + 1]
            ax, ay = verts2d[a]
            bx, by = verts2d[b]
            cx, cy = verts2d[c]
            area2 = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax)
            if abs(area2) < 1e-12:
                continue
            tris.append((a, b, c) if area2 > 0 else (a, c, b))
    return verts2d, tris, letters


def boundary_loops(tris):
    """Directed boundary edges of a 2D triangulation, chained into closed loops.

    Each directed edge (a, b) has its face on the left.
    """
    count = {}
    for t in tris:
        for i in range(3):
            a, b = t[i], t[(i + 1) % 3]
            key = (a, b) if a < b else (b, a)
            count[key] = count.get(key, 0) + 1
    nxt = {}
    for t in tris:
        for i in range(3):
            a, b = t[i], t[(i + 1) % 3]
            key = (a, b) if a < b else (b, a)
            if count[key] == 1:
                nxt.setdefault(a, []).append(b)
    loops = []
    while nxt:
        start = next(iter(nxt))
        loop = []
        a = start
        while True:
            outs = nxt[a]
            b = outs.pop()
            if not outs:
                del nxt[a]
            loop.append((a, b))
            a = b
            if a == start or a not in nxt:
                break
        loops.append(loop)
    return loops


# ------------------------------------------------------------------ 3D assembly
class Atlas:
    """Dirt-mask atlas: a grid of near-square cells (one per panel face), plus a spare strip on top."""

    def __init__(self, cols=UV_COLS, cells=2 * N_PANELS):
        self.cols = cols
        self.cells = cells
        self.cell_w = 1.0 / cols
        self.s = (self.cell_w - 2 * UV_MARGIN) / PANEL_W          # UV units per metre
        self.cell_h = PANEL_H * self.s + 2 * UV_MARGIN
        self.rows = int(1.0 // self.cell_h)
        if self.rows * cols < cells:
            raise RuntimeError("dirt atlas too small for the panel count")

    def cell_origin(self, k):
        col, row = k % self.cols, k // self.cols
        return (col * self.cell_w + UV_MARGIN, row * self.cell_h + UV_MARGIN)

    def spare_rects(self):
        rects = []
        for k in range(self.cells, self.rows * self.cols):
            col, row = k % self.cols, k // self.cols
            rects.append((col * self.cell_w, row * self.cell_h,
                          (col + 1) * self.cell_w, (row + 1) * self.cell_h))
        top = self.rows * self.cell_h
        if 1.0 - top > 3 * UV_MARGIN:
            rects.append((0.0, top, 1.0, 1.0))
        return rects


def add_panel(bm, uv0, uv1, p, verts2d, tris, atlas, rim_loops_out, ex_k, in_k):
    theta0 = p * (2 * math.pi / N_PANELS) + (PANEL_GAP / 2) / R_OUTER
    used = sorted({i for t in tris for i in t})
    v_out, v_in = {}, {}
    for i in used:
        u, v = verts2d[i]
        th = theta0 + u / R_OUTER
        c, s = math.cos(th), math.sin(th)
        z = PLINTH_H + v
        v_out[i] = bm.verts.new((R_OUTER * c, R_OUTER * s, z))
        v_in[i] = bm.verts.new(((R_OUTER - THICK) * c, (R_OUTER - THICK) * s, z))

    u_off = p * PANEL_PITCH
    ex_o = atlas.cell_origin(ex_k)
    in_o = atlas.cell_origin(in_k)
    sc = atlas.s
    for t in tris:
        f = bm.faces.new([v_out[i] for i in t])
        f.smooth = True
        for lp, i in zip(f.loops, t):
            u, v = verts2d[i]
            lp[uv0].uv = (u_off + u, v)
            lp[uv1].uv = (ex_o[0] + u * sc, ex_o[1] + v * sc)
        t_rev = (t[2], t[1], t[0])
        f = bm.faces.new([v_in[i] for i in t_rev])
        f.smooth = True
        for lp, i in zip(f.loops, t_rev):
            u, v = verts2d[i]
            lp[uv0].uv = (u_off + u, v)
            lp[uv1].uv = (in_o[0] + u * sc, in_o[1] + v * sc)

    for loop in boundary_loops(tris):
        recs = []
        along = 0.0
        for a, b in loop:
            ua, va = verts2d[a]
            ub, vb = verts2d[b]
            L = math.hypot(ub - ua, vb - va)
            f = bm.faces.new((v_out[a], v_in[a], v_in[b], v_out[b]))
            f.smooth = True
            f.loops[0][uv0].uv = (along, THICK)
            f.loops[1][uv0].uv = (along, 0.0)
            f.loops[2][uv0].uv = (along + L, 0.0)
            f.loops[3][uv0].uv = (along + L, THICK)
            along += L
            recs.append((f, L))
        rim_loops_out.append(recs)


def pack_rims(rim_loops, rects, uv1, s_r):
    """Lay rim quads into the spare atlas rectangles as thin strips.

    Returns False if they do not fit at this density.
    """
    M = UV_MARGIN
    ri = 0
    x0, y0, x1, y1 = rects[0]
    cx, cy = x0 + M, y0 + M
    rowh = THICK * s_r
    row_w = x1 - x0 - 2 * M
    for loop in rim_loops:
        cx += UV_GAP_RIM                       # small gap between separate loops
        for f, L in loop:
            w = min(L * s_r, row_w)
            if cx + w > x1 - M:
                cy += rowh + UV_GAP_RIM
                cx = x0 + M
                if cy + rowh > y1 - M:
                    ri += 1
                    if ri >= len(rects):
                        return False
                    x0, y0, x1, y1 = rects[ri]
                    cx, cy = x0 + M, y0 + M
                    row_w = x1 - x0 - 2 * M
            f.loops[0][uv1].uv = (cx, cy + rowh)
            f.loops[1][uv1].uv = (cx, cy)
            f.loops[2][uv1].uv = (cx + w, cy)
            f.loops[3][uv1].uv = (cx + w, cy + rowh)
            cx += w
    return True


def mark_sharp_by_angle(bm):
    bm.normal_update()
    n = 0
    for e in bm.edges:
        if len(e.link_faces) == 2 and e.calc_face_angle(0.0) > SHARP_ANGLE:
            e.smooth = False
            n += 1
    return n


def make_material(name, color, metallic, roughness):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*color, 1.0)
        bsdf.inputs["Metallic"].default_value = metallic
        bsdf.inputs["Roughness"].default_value = roughness
    mat.diffuse_color = (*color, 1.0)
    mat.metallic = metallic
    mat.roughness = roughness
    return mat


def bm_to_object(bm, name, material, collection):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(material)
    me.update()
    me.validate(verbose=False)
    ob = bpy.data.objects.new(name, me)
    collection.objects.link(ob)
    return ob


def finish_rims(rim_loops, atlas, uv1):
    """Pack the rim quads into the atlas' spare space, shrinking their density until they fit."""
    rects = atlas.spare_rects()
    s_r = atlas.s
    while not pack_rims(rim_loops, rects, uv1, s_r):
        s_r *= 0.85
        if s_r < atlas.s * 0.1:
            raise RuntimeError("rim UVs do not fit in the dirt atlas")
    return s_r


def build_screen(collection, material, rng, split=SPLIT_PANELS):
    """Returns (objects, info): one OKC_Screen_Panels object, or OKC_Panel_01..14 when split."""
    letters_all = []
    objects = []
    t0 = time.time()
    if split:
        rim_len = 0.0
        s_r_min = None
        for p in range(N_PANELS):
            bm = bmesh.new()
            uv0 = bm.loops.layers.uv.new("UVMap")
            uv1 = bm.loops.layers.uv.new("DirtMask")
            atlas = Atlas(UV_COLS_SPLIT, 2)
            rim_loops = []
            verts2d, tris, letters = build_panel_2d(rng)
            letters_all += letters
            add_panel(bm, uv0, uv1, p, verts2d, tris, atlas, rim_loops, 0, 1)
            s_r = finish_rims(rim_loops, atlas, uv1)
            s_r_min = s_r if s_r_min is None else min(s_r_min, s_r)
            rim_len += sum(L for loop in rim_loops for _, L in loop)
            sharp = mark_sharp_by_angle(bm)
            name = f"OKC_Panel_{p + 1:02d}"
            log(f"{name}: {len(tris)} tris per side, {len(bm.faces)} faces, {sharp} sharp edges, "
                f"rims at {s_r / atlas.s:.0%} density ({time.time() - t0:.1f}s)")
            objects.append(bm_to_object(bm, name, material, collection))
        panel_s = Atlas(UV_COLS_SPLIT, 2).s
        log(f"dirt atlas per panel: {panel_s:.4f} UV/m on faces, {s_r_min:.4f} UV/m on rims (worst panel)")
    else:
        bm = bmesh.new()
        uv0 = bm.loops.layers.uv.new("UVMap")
        uv1 = bm.loops.layers.uv.new("DirtMask")
        atlas = Atlas()
        rim_loops = []
        for p in range(N_PANELS):
            verts2d, tris, letters = build_panel_2d(rng)
            letters_all += letters
            add_panel(bm, uv0, uv1, p, verts2d, tris, atlas, rim_loops, p, N_PANELS + p)
            log(f"panel {p + 1}/{N_PANELS}: {len(tris)} tris per side, "
                f"{sum(len(l) for l in rim_loops)} rim quads so far ({time.time() - t0:.1f}s)")
        s_r_min = finish_rims(rim_loops, atlas, uv1)
        panel_s = atlas.s
        rim_len = sum(L for loop in rim_loops for _, L in loop)
        log(f"dirt atlas: {atlas.s:.4f} UV/m on faces, {s_r_min:.4f} UV/m on rims "
            f"({s_r_min / atlas.s:.0%}); rim length {rim_len:.1f} m in {len(rim_loops)} loops")
        sharp = mark_sharp_by_angle(bm)
        log(f"screen: {len(bm.verts)} verts, {len(bm.faces)} faces, {sharp} sharp edges")
        objects.append(bm_to_object(bm, "OKC_Screen_Panels", material, collection))
    counts = {l: letters_all.count(l) for l in GLYPHS}
    return objects, {"panel_uv_per_m": panel_s, "rim_uv_per_m": s_r_min,
                     "rim_len_m": rim_len, "letters": counts}


def build_plinth(collection, material):
    bm = bmesh.new()
    uv0 = bm.loops.layers.uv.new("UVMap")
    uv1 = bm.loops.layers.uv.new("DirtMask")
    seg = PLINTH_SEG
    spp = seg // PLINTH_PIECES
    seg = spp * PLINTH_PIECES
    ring_w = PLINTH_OUT - PLINTH_IN
    r_mid = (PLINTH_OUT + PLINTH_IN) / 2
    M = UV_MARGIN
    len_out = 2 * math.pi * PLINTH_OUT / PLINTH_PIECES
    len_mid = 2 * math.pi * r_mid / PLINTH_PIECES
    len_in = 2 * math.pi * PLINTH_IN / PLINTH_PIECES
    s_p = (1.0 - 2 * M - 0.01) / len_out
    piece_h = (PLINTH_H + ring_w + PLINTH_H) * s_p + 2 * UV_GAP_RIM
    if PLINTH_PIECES * (piece_h + M) + M > 1.0:
        raise RuntimeError("plinth dirt UVs do not fit")

    vo_b, vo_t, vi_b, vi_t = [], [], [], []
    for k in range(seg):
        th = 2 * math.pi * k / seg
        c, s = math.cos(th), math.sin(th)
        vo_b.append(bm.verts.new((PLINTH_OUT * c, PLINTH_OUT * s, 0.0)))
        vo_t.append(bm.verts.new((PLINTH_OUT * c, PLINTH_OUT * s, PLINTH_H)))
        vi_b.append(bm.verts.new((PLINTH_IN * c, PLINTH_IN * s, 0.0)))
        vi_t.append(bm.verts.new((PLINTH_IN * c, PLINTH_IN * s, PLINTH_H)))

    for k in range(seg):
        k1 = (k + 1) % seg
        q = k // spp
        a0 = (k - q * spp) / spp
        a1 = (k + 1 - q * spp) / spp
        base_y = M + q * (piece_h + M)
        y_out = base_y
        y_top = y_out + PLINTH_H * s_p + UV_GAP_RIM
        y_in = y_top + ring_w * s_p + UV_GAP_RIM
        arc0 = 2 * math.pi * k / seg
        arc1 = 2 * math.pi * (k + 1) / seg

        f = bm.faces.new((vo_b[k], vo_b[k1], vo_t[k1], vo_t[k]))
        f.smooth = True
        for lp, (a, arc, h) in zip(f.loops, ((a0, arc0, 0.0), (a1, arc1, 0.0),
                                             (a1, arc1, PLINTH_H), (a0, arc0, PLINTH_H))):
            lp[uv0].uv = (arc * PLINTH_OUT, h)
            lp[uv1].uv = (M + a * len_out * s_p, y_out + h * s_p)

        f = bm.faces.new((vo_t[k], vo_t[k1], vi_t[k1], vi_t[k]))
        f.smooth = True
        for lp, (a, arc, r) in zip(f.loops, ((a0, arc0, PLINTH_OUT), (a1, arc1, PLINTH_OUT),
                                             (a1, arc1, PLINTH_IN), (a0, arc0, PLINTH_IN))):
            lp[uv0].uv = (arc * r_mid, r - PLINTH_IN)
            lp[uv1].uv = (M + a * len_mid * s_p, y_top + (r - PLINTH_IN) * s_p)

        f = bm.faces.new((vi_b[k], vi_t[k], vi_t[k1], vi_b[k1]))
        f.smooth = True
        for lp, (a, arc, h) in zip(f.loops, ((a0, arc0, 0.0), (a0, arc0, PLINTH_H),
                                             (a1, arc1, PLINTH_H), (a1, arc1, 0.0))):
            lp[uv0].uv = (arc * PLINTH_IN, h)
            lp[uv1].uv = (M + a * len_in * s_p, y_in + h * s_p)

    sharp = mark_sharp_by_angle(bm)
    log(f"plinth: {len(bm.verts)} verts, {len(bm.faces)} faces, {sharp} sharp edges, "
        f"dirt UV {s_p:.4f} UV/m")
    ob = bm_to_object(bm, "OKC_Screen_Plinth", material, collection)
    return ob, {"plinth_uv_per_m": s_p}


# ------------------------------------------------------------------ export
def filtered_kwargs(op, kwargs):
    props = op.get_rna_type().properties.keys()
    return {k: v for k, v in kwargs.items() if k in props}


def export_all(out_dir, objects):
    exp_dir = os.path.join(out_dir, "export")
    os.makedirs(exp_dir, exist_ok=True)
    for ob in bpy.data.objects:
        ob.select_set(ob in objects)
    results = {}

    fbx_path = os.path.join(exp_dir, "OKC_TransformerScreen.fbx")
    try:
        kwargs = dict(filepath=fbx_path, use_selection=True, object_types={'EMPTY', 'MESH'},
                      apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', global_scale=1.0,
                      axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                      use_mesh_modifiers=True, mesh_smooth_type='OFF', use_tspace=True,
                      use_triangles=False, add_leaf_bones=False, path_mode='AUTO',
                      use_custom_props=False, colors_type='NONE', embed_textures=False)
        bpy.ops.export_scene.fbx(**filtered_kwargs(bpy.ops.export_scene.fbx, kwargs))
        results["fbx"] = fbx_path
        log(f"exported {fbx_path}")
    except Exception as e:  # noqa: BLE001
        log(f"FBX export failed: {e!r}")

    glb_path = os.path.join(exp_dir, "OKC_TransformerScreen.glb")
    try:
        kwargs = dict(filepath=glb_path, export_format='GLB', use_selection=True,
                      export_apply=True, export_yup=True, export_texcoords=True,
                      export_normals=True, export_tangents=True, export_materials='EXPORT',
                      export_cameras=False, export_lights=False, export_extras=False,
                      export_image_format='NONE')
        bpy.ops.export_scene.gltf(**filtered_kwargs(bpy.ops.export_scene.gltf, kwargs))
        results["glb"] = glb_path
        log(f"exported {glb_path}")
    except Exception as e:  # noqa: BLE001
        log(f"GLB export failed: {e!r}")
    return results


# ------------------------------------------------------------------ preview
def look_at(ob, target):
    d = Vector(target) - ob.location
    ob.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()


def add_preview_rig(scene, collection):
    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_data.lens = 35
    cam = bpy.data.objects.new("PreviewCam", cam_data)
    collection.objects.link(cam)
    scene.camera = cam
    sun_data = bpy.data.lights.new("Sun", 'SUN')
    sun_data.energy = 4.0
    sun = bpy.data.objects.new("Sun", sun_data)
    sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(-40))
    collection.objects.link(sun)
    return cam


def render_previews(scene, cam, out_dir):
    prev_dir = os.path.join(out_dir, "preview")
    os.makedirs(prev_dir, exist_ok=True)
    views = [
        ("preview_exterior", (10.5, -10.5, 2.2), (0.0, 0.0, 1.2)),
        ("preview_closeup", (0.0, -7.3, 1.5), (0.0, -4.5, 1.3)),
        ("preview_interior", (0.0, 0.3, 1.5), (0.0, -4.5, 1.3)),
        ("preview_top", (6.0, -11.0, 14.0), (0.0, 0.0, 0.8)),
    ]
    world = bpy.data.worlds.new("PreviewWorld")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.55, 0.65, 0.8, 1.0)
        bg.inputs[1].default_value = 1.0
    scene.render.resolution_x = 1600
    scene.render.resolution_y = 900
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.film_transparent = False

    def do_render(engine):
        scene.render.engine = engine
        if engine == 'BLENDER_WORKBENCH':
            sh = scene.display.shading
            sh.light = 'STUDIO'
            sh.color_type = 'MATERIAL'
            sh.show_cavity = True
            for attr, val in (("show_shadow", True), ("show_object_outline", False)):
                if hasattr(sh, attr):
                    setattr(sh, attr, val)
            scene.display.render_aa = '8'
        else:
            scene.cycles.samples = 24
            scene.cycles.device = 'CPU'
            scene.cycles.use_denoising = True
            scene.render.resolution_percentage = 60
        done = []
        for name, loc, tgt in views:
            cam.location = loc
            look_at(cam, tgt)
            path = os.path.join(prev_dir, name + ".png")
            scene.render.filepath = path
            bpy.ops.render.render(write_still=True)
            done.append(path)
            log(f"rendered {path}")
        return done

    try:
        return do_render('BLENDER_WORKBENCH')
    except Exception as e:  # noqa: BLE001
        log(f"Workbench render failed ({e!r}); falling back to Cycles CPU")
        try:
            return do_render('CYCLES')
        except Exception as e2:  # noqa: BLE001
            log(f"Cycles render failed too: {e2!r}")
            return []


# ------------------------------------------------------------------ main
def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out_dir = os.path.dirname(os.path.abspath(__file__))
    do_render = True
    split = SPLIT_PANELS
    i = 0
    while i < len(argv):
        if argv[i] == "--out" and i + 1 < len(argv):
            out_dir = os.path.abspath(argv[i + 1])
            i += 1
        elif argv[i] == "--no-render":
            do_render = False
        elif argv[i] == "--merged":
            split = False
        i += 1
    os.makedirs(out_dir, exist_ok=True)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1.0
    coll = bpy.data.collections.new("OKC_TransformerScreen")
    scene.collection.children.link(coll)

    rng = random.Random(SEED)
    metal = make_material("OKC_Screen_Metal", METAL_COLOR, 0.9, 0.45)
    concrete = make_material("OKC_Concrete", CONCRETE_COLOR, 0.0, 0.9)

    root = bpy.data.objects.new("OKC_TransformerScreen", None)
    root.empty_display_type = 'PLAIN_AXES'
    coll.objects.link(root)

    panels, info = build_screen(coll, metal, rng, split)
    plinth, info2 = build_plinth(coll, concrete)
    info.update(info2)
    for ob in panels + [plinth]:
        ob.parent = root

    log(f"screen: {len(panels)} panel object(s), top of wall at z={PLINTH_H + PANEL_H:.2f} m")
    log(f"letters used: {info['letters']}")

    exports = export_all(out_dir, {root, plinth, *panels})
    cam = add_preview_rig(scene, coll)

    blend_path = os.path.join(out_dir, "OKC_TransformerScreen.blend")
    bpy.ops.wm.save_as_mainfile(filepath=blend_path, compress=True)
    log(f"saved {blend_path}")

    if do_render:
        render_previews(scene, cam, out_dir)

    tri_count = sum(len(p.vertices) - 2 for ob in panels for p in ob.data.polygons)
    tri_count_pl = sum(len(p.vertices) - 2 for p in plinth.data.polygons)
    log("SUMMARY "
        f"panel_objects={len(panels)} panels_tris={tri_count} "
        f"panels_verts={sum(len(ob.data.vertices) for ob in panels)} "
        f"plinth_tris={tri_count_pl} plinth_verts={len(plinth.data.vertices)} "
        f"panel_uv_per_m={info['panel_uv_per_m']:.4f} rim_uv_per_m={info['rim_uv_per_m']:.4f} "
        f"plinth_uv_per_m={info['plinth_uv_per_m']:.4f} rim_len_m={info['rim_len_m']:.1f} "
        f"exports={sorted(exports)}")


if __name__ == "__main__":
    main()
