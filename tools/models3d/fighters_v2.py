"""Fighters v2 (owner #544/#557): export Gemini-built slot kits G1-G6 to GLB and render a paper-style sheet.

Kits are built by Gemini through the Blender MCP (brief: assets/models3d/fighters_v2/BRIEF.md); this script only
exports and renders. Two modes, both headless (Blender 5.3):

  # 1. one GLB per part from the saved scene file (each part root moved to the origin)
  nice -n 10 blender -b assets/models3d/fighters_v2/fighters_v2.blend -P tools/models3d/fighters_v2.py -- export
  # slot test: part roots, ATTACH empties, assembled kit == saved scene (exit 1 on failure)
  nice -n 10 blender -b assets/models3d/fighters_v2/fighters_v2.blend -P tools/models3d/fighters_v2.py -- test
  # 2. assemble every kit from its GLBs on the ATTACH empties; render the sheet, its greyscale copy and pair_g2_g5.png
  nice -n 10 blender -b --factory-startup -P tools/models3d/fighters_v2.py -- render [--out DIR]

Deterministic: no random numbers; Cycles CPU, fixed seed/samples/threads, emission-only materials, PNG re-encoded
without metadata. Slot ids per generation come from PILOT and are checked against mods/core/product_classes.yaml.
"""

import math
import os
import re
import struct
import sys
import zlib

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
KITS = os.path.join(REPO, "assets", "models3d", "fighters_v2")
PRODUCT_CLASSES = os.path.join(REPO, "mods", "core", "product_classes.yaml")

PAPER, INK = "#F2EFE6", "#1D1E20"          # color.paper, color.ink (UI_DESIGN_SYSTEM.md 2.1)
FILL, SHADE, DARK = "#D2CAB6", "#ABA28E", "#5E5A53"   # remark 6 + v2 review 4.3: grey CA/A2 against paper EE
PARTS = ("fuselage", "wing", "tail", "engine", "afterburner")
ATTACH = {"wing": ("fuselage", "ATTACH_wing"), "tail": ("fuselage", "ATTACH_tail"),
          "engine": ("fuselage", "ATTACH_engine"), "afterburner": ("engine", "ATTACH_afterburner")}
# generation -> design slots (engine, airframe, afterburner) of mods/core/product_classes.yaml
PILOT = {
    "G1": ("eng_g1", "af_straight", "ab_none"),
    "G2": ("eng_g2", "af_swept", "ab_none"),
    "G3": ("eng_g3", "af_milled", "ab_on"),
    "G4": ("eng_g4", "af_mach2", "ab_on"),
    "G5": ("eng_g5", "af_composite", "ab_on"),
    "G6": ("eng_g6", "af_lo_composite", "ab_on"),
}
# sheet: 3x2 cells of 640x540; label row, top view (nose left), side view at the same scale
CELL_W, CELL_H, TOP_H, SIDE_H, MARGIN = 640, 540, 330, 150, 14
OUTLINE_PX, DETAIL_PX = 2.5, 1.5   # remark 4: outline >= 2 px, inner lines >= 1.5 px, both ink
SS = 4                             # supersampling factor of the passes
CREASE_DEG = 50.0                  # a normal change above this inside one part is drawn as an inner line


def check_slot_ids():
    with open(PRODUCT_CLASSES, encoding="utf-8") as f:
        ids = set(re.findall(r"\{id:\s*([a-z0-9_]+)", f.read()))
    missing = sorted({s for v in PILOT.values() for s in v} - ids)
    if missing:
        raise ValueError("slot ids not in product_classes.yaml: %s" % missing)


def hex_rgb(h):
    return [int(h[i:i + 2], 16) for i in (1, 3, 5)]


def hex_lin(h):
    c = [v / 255.0 for v in hex_rgb(h)]
    return tuple(v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4 for v in c)


# ---------------------------------------------------------------- export
def export(out):
    """One GLB per part: reload the saved file, keep only scene Gn and the part's objects, root to origin, export."""
    blend = bpy.data.filepath
    gens = sorted(s.name for s in bpy.data.scenes if s.name in PILOT)
    for g in gens:
        d = os.path.join(out, g.lower())
        os.makedirs(d, exist_ok=True)
        for part in PARTS:
            bpy.ops.wm.open_mainfile(filepath=blend)
            sc = bpy.data.scenes[g]
            for other in [s for s in bpy.data.scenes if s != sc]:
                bpy.data.scenes.remove(other)
            root = bpy.data.objects.get("KIT_%s_%s_root" % (g, part))
            if root is None:
                raise ValueError("%s: missing root for %s" % (g, part))
            keep = {root} | set(root.children_recursive)
            for o in [o for o in bpy.data.objects if o not in keep]:
                bpy.data.objects.remove(o, do_unlink=True)
            root.parent = None
            root.matrix_world = Matrix.Identity(4)   # each part GLB has its root at the origin
            bpy.context.view_layer.update()
            path = os.path.join(d, "%s_%s.glb" % (g.lower(), part))
            bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=False,
                                      export_extras=False, export_cameras=False, export_lights=False,
                                      export_apply=True)
            print("[export] %s %d objects %d bytes" % (path, len(keep), os.path.getsize(path)))


# ---------------------------------------------------------------- render
def make_materials():
    mats = {}
    for name, col in (("fill", FILL), ("dark", DARK)):
        m = bpy.data.materials.new("paper_" + name)
        m.use_nodes = True
        nt = m.node_tree
        for n in list(nt.nodes):
            nt.nodes.remove(n)
        o = nt.nodes.new("ShaderNodeOutputMaterial")
        em = nt.nodes.new("ShaderNodeEmission")
        if name == "fill":  # flat two-step tone: faces whose true normal points down take SHADE
            geo = nt.nodes.new("ShaderNodeNewGeometry")
            sep = nt.nodes.new("ShaderNodeSeparateXYZ")
            lt = nt.nodes.new("ShaderNodeMath")
            lt.operation = "LESS_THAN"
            lt.inputs[1].default_value = -0.25
            mix = nt.nodes.new("ShaderNodeMix")
            mix.data_type = "RGBA"
            mix.inputs[6].default_value = (*hex_lin(FILL), 1)
            mix.inputs[7].default_value = (*hex_lin(SHADE), 1)
            nt.links.new(geo.outputs["True Normal"], sep.inputs[0])
            nt.links.new(sep.outputs["Z"], lt.inputs[0])
            nt.links.new(lt.outputs[0], mix.inputs[0])
            nt.links.new(mix.outputs[2], em.inputs["Color"])
        else:
            em.inputs["Color"].default_value = (*hex_lin(col), 1)
        em.inputs["Strength"].default_value = 1.0
        nt.links.new(em.outputs[0], o.inputs["Surface"])
        mats[name] = m
    return mats


def setup_scene():
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    sc = bpy.context.scene
    r = sc.render
    r.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 16
    sc.cycles.use_adaptive_sampling = False
    sc.cycles.use_denoising = False
    sc.cycles.seed = 0
    sc.cycles.use_animated_seed = False
    sc.cycles.max_bounces = 0
    r.threads_mode = "FIXED"
    r.threads = 4
    r.film_transparent = False
    r.resolution_percentage = 100
    r.dither_intensity = 0.0
    for a in dir(r):
        if a.startswith("use_stamp"):
            setattr(r, a, False)
    r.image_settings.file_format = "PNG"
    r.image_settings.color_mode = "RGB"
    r.image_settings.color_depth = "8"
    sc.view_settings.view_transform = "Standard"
    sc.view_settings.look = "None"
    w = bpy.data.worlds.new("paper")
    w.use_nodes = True
    bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs["Color"].default_value = (*hex_lin(PAPER), 1)
    bg.inputs["Strength"].default_value = 1.0
    sc.world = w
    r.use_freestyle = False   # ink is drawn from ID and normal passes (remark 5: no Freestyle specks)
    r.filter_size = 0.01      # no pixel filter: passes are rendered at SS x size and averaged down
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    cam.data.type = "ORTHO"
    sc.collection.objects.link(cam)
    sc.camera = cam


def import_part(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    return [o for o in bpy.data.objects if o not in before]


def assemble(g, kits, mats):
    """Imports the five part GLBs and puts each part root on its ATTACH empty."""
    for ob in list(bpy.data.objects):
        if ob.type != "CAMERA":
            bpy.data.objects.remove(ob, do_unlink=True)
    objs = {}
    for part in PARTS:
        objs[part] = import_part(os.path.join(kits, g.lower(), "%s_%s.glb" % (g.lower(), part)))
    bpy.context.view_layer.update()

    def find(part, name):
        for o in objs[part]:
            if o.name.split(".")[0] == name:
                return o
        raise ValueError("%s: %s not in %s" % (g, name, part))
    for part in ("engine", "wing", "tail", "afterburner"):
        host, att = ATTACH[part]
        root = find(part, "KIT_%s_%s_root" % (g, part))
        root.matrix_world = find(host, att).matrix_world.copy()
        bpy.context.view_layer.update()
    meshes = [o for p in PARTS for o in objs[p] if o.type == "MESH"]
    for o in meshes:
        if not o.material_slots and mats["fill"] is not None:
            o.data.materials.append(mats["fill"])
        o["_dark"] = [bool(s.material and s.material.name.startswith("M_dark")) for s in o.material_slots]
        for slot in o.material_slots:   # object-linked slots, so passes can be switched per object
            slot.link = "OBJECT"
    meshes.sort(key=lambda o: o.name)
    return meshes


def bounds(meshes):
    vs = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
    lo = Vector([min(v[i] for v in vs) for i in range(3)])
    hi = Vector([max(v[i] for v in vs) for i in range(3)])
    return lo, hi


def id_material(k):
    """Flat emission colour that encodes part id k (8-bit sRGB code, read back from the PNG)."""
    m = bpy.data.materials.new("id_%d" % k)
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    o = nt.nodes.new("ShaderNodeOutputMaterial")
    em = nt.nodes.new("ShaderNodeEmission")
    code = "#%02X%02X%02X" % (20 + 20 * (k % 11), 20 + 20 * ((k // 11) % 11), 100)
    em.inputs["Color"].default_value = (*hex_lin(code), 1)
    nt.links.new(em.outputs[0], o.inputs["Surface"])
    return m


def normal_material():
    """Emission = true normal * 0.5 + 0.5, used to find creases."""
    m = bpy.data.materials.new("normal")
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    o = nt.nodes.new("ShaderNodeOutputMaterial")
    em = nt.nodes.new("ShaderNodeEmission")
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    vm = nt.nodes.new("ShaderNodeVectorMath")
    vm.operation = "MULTIPLY_ADD"
    vm.inputs[1].default_value = (0.5, 0.5, 0.5)
    vm.inputs[2].default_value = (0.5, 0.5, 0.5)
    nt.links.new(geo.outputs["True Normal"], vm.inputs[0])
    nt.links.new(vm.outputs[0], em.inputs["Color"])
    nt.links.new(em.outputs[0], o.inputs["Surface"])
    return m


def set_pass(meshes, mode, mats, id_mats, nrm):
    """mode 'shade': paper fills; 'id': one flat code per (object, dark/body); 'normal': true normals."""
    for i, o in enumerate(meshes):
        for j, slot in enumerate(o.material_slots):
            dark = o["_dark"][j]
            if mode == "shade":
                slot.material = mats["dark"] if dark else mats["fill"]
            elif mode == "id":
                slot.material = id_mats[2 * i + (1 if dark else 0)]
            else:
                slot.material = nrm


def camera_for(view, ctr, scale_px, w, h):
    sc = bpy.context.scene
    cam = sc.camera
    cam.data.ortho_scale = max(w, h) / scale_px
    cam.data.clip_start, cam.data.clip_end = 0.1, 200.0
    if view == "top":
        cam.location = ctr + Vector((0, 0, 60))
        cam.rotation_euler = (0.0, 0.0, math.pi)          # from +Z; camera x axis = world -X: nose left
    else:
        cam.location = ctr + Vector((0, 60, 0))
        cam.rotation_euler = (math.pi / 2, 0.0, math.pi)  # from +Y looking -Y; nose left
    sc.render.resolution_x, sc.render.resolution_y = w * SS, h * SS


def render_png(tmp, samples):
    sc = bpy.context.scene
    sc.cycles.samples = samples
    sc.render.filepath = tmp
    bpy.ops.render.render(write_still=True)
    a = _png_read(tmp)
    os.remove(tmp)
    return a


def disc(r):
    import numpy as np
    n = int(math.ceil(r))
    yy, xx = np.mgrid[-n:n + 1, -n:n + 1]
    return [(dy, dx) for dy, dx in zip(yy.ravel(), xx.ravel()) if dy * dy + dx * dx <= r * r]


def dilate(m, r):
    import numpy as np
    out = np.zeros_like(m)
    n = int(math.ceil(r))
    p = np.pad(m, n)
    h, w = m.shape
    for dy, dx in disc(r):
        out |= p[n + dy:n + dy + h, n + dx:n + dx + w]
    return out


def down(a):
    """Average SS x SS blocks (float)."""
    h, w = a.shape[0] // SS, a.shape[1] // SS
    return a.reshape(h, SS, w, SS, *a.shape[2:]).mean(axis=(1, 3))


def render_tile(view, ctr, scale_px, w, h, meshes, mats, id_mats, nrm, tmp):
    """Fills from the shade pass; ink from ID edges (outline vs paper, part and material seams) and creases."""
    import numpy as np
    camera_for(view, ctr, scale_px, w, h)
    set_pass(meshes, "shade", mats, id_mats, nrm)
    shade = render_png(tmp, 1).astype(np.float64)
    set_pass(meshes, "id", mats, id_mats, nrm)
    ids = render_png(tmp, 1).astype(np.int32)
    set_pass(meshes, "normal", mats, id_mats, nrm)
    nr = render_png(tmp, 1).astype(np.float64) / 255.0
    paper = np.array(hex_rgb(PAPER))
    bg = np.abs(ids - paper).sum(axis=2) < 6
    code = ids[:, :, 0] * 65536 + ids[:, :, 1] * 256 + ids[:, :, 2]
    lin = np.where(nr <= 0.04045, nr / 12.92, ((nr + 0.055) / 1.055) ** 2.4) * 2 - 1
    lin /= np.maximum(np.linalg.norm(lin, axis=2, keepdims=True), 1e-6)
    cos_lim = math.cos(math.radians(CREASE_DEG))
    outer = np.zeros(bg.shape, bool)
    inner = np.zeros(bg.shape, bool)
    for ax in (0, 1):
        a_sl = (slice(None, -1), slice(None)) if ax == 0 else (slice(None), slice(None, -1))
        b_sl = (slice(1, None), slice(None)) if ax == 0 else (slice(None), slice(1, None))
        bgx = bg[a_sl] != bg[b_sl]
        idx = (code[a_sl] != code[b_sl]) & ~bg[a_sl] & ~bg[b_sl]
        crs = ((lin[a_sl] * lin[b_sl]).sum(axis=2) < cos_lim) & ~bg[a_sl] & ~bg[b_sl] & ~idx
        for m, e in ((outer, bgx), (inner, idx | crs)):
            m[a_sl] |= e
            m[b_sl] |= e
    outer = dilate(outer, OUTLINE_PX * SS / 2)
    inner = dilate(inner, DETAIL_PX * SS / 2)
    alpha = down((outer | inner).astype(np.float64))[:, :, None]
    base = down(shade)
    ink = np.array(hex_rgb(INK), np.float64)
    return np.round(base * (1 - alpha) + ink * alpha).astype(np.uint8)


def _png_write(path, arr):
    import numpy as np
    if arr.ndim == 2:
        arr = np.repeat(arr[:, :, None], 3, axis=2)
    h, w, _ = arr.shape
    up = arr.astype(np.int16)
    diff = np.empty_like(up)
    diff[0] = up[0]
    diff[1:] = up[1:] - up[:-1]
    raw = np.concatenate([np.full((h, 1), 2, np.uint8), (diff % 256).astype(np.uint8).reshape(h, w * 3)], axis=1)

    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)
    data = (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw.tobytes(), 9)) + chunk(b"IEND", b""))
    with open(path, "wb") as f:
        f.write(data)


def _png_read(path):
    import numpy as np
    img = bpy.data.images.load(path)
    w, h = img.size
    px = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    return (np.round(px.reshape(h, w, 4)[::-1, :, :3] * 255)).astype(np.uint8)


# 5x7 bitmap glyphs for the generation label (drawn as pixels; the client draws its own Label)
GLYPHS = {
    "G": ["01110", "10001", "10000", "10111", "10001", "10001", "01111"],
    "1": ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
    "2": ["01110", "10001", "00001", "00010", "00100", "01000", "11111"],
    "3": ["11110", "00001", "00001", "01110", "00001", "00001", "11110"],
    "4": ["00010", "00110", "01010", "10010", "11111", "00010", "00010"],
    "5": ["11111", "10000", "11110", "00001", "00001", "10001", "01110"],
    "6": ["00110", "01000", "10000", "11110", "10001", "10001", "01110"],
    "П": ["11111", "10001", "10001", "10001", "10001", "10001", "10001"],
    "о": ["00000", "00000", "01110", "10001", "10001", "10001", "01110"],
    "к": ["00000", "00000", "10010", "10100", "11000", "10100", "10010"],
    ".": ["00000", "00000", "00000", "00000", "00000", "01100", "01100"],
    " ": ["00000"] * 7,
}


def draw_text(sheet, x, y, text, k, col):
    for ch in text:
        for r, row in enumerate(GLYPHS[ch]):
            for c, bit in enumerate(row):
                if bit == "1":
                    sheet[y + r * k:y + (r + 1) * k, x + c * k:x + (c + 1) * k] = col
        x += 6 * k


def label(sheet, x, y, g, ink):
    """'G4' (28 px), 'Пок. 4' (21 px) and one 14 px square per generation, grouped by three."""
    n = int(g[1:])
    draw_text(sheet, x + 4, y + 12, g, 4, ink)
    draw_text(sheet, x + 68, y + 19, "Пок. %d" % n, 3, ink)
    for i in range(n):
        x0 = x + 196 + i * 20 + (12 if i >= 3 else 0)
        sheet[y + 23:y + 37, x0:x0 + 14] = ink


def draw_cell(sheet, g, x, y, w, top_h, side_h, ctx, scale=None):
    """Label row, top view (nose left) and side view at one scale; returns (top tile, scale)."""
    kits, out, mats, nrm, ink = ctx
    meshes = assemble(g, kits, mats)
    lo, hi = bounds(meshes)
    ext, ctr = hi - lo, (lo + hi) / 2
    tw = w - 2 * MARGIN
    s = scale or min((tw - 20) / ext.x, (top_h - 16) / ext.y, (side_h - 16) / ext.z)
    id_mats = [id_material(i) for i in range(2 * len(meshes))]
    tmp = os.path.join(out, ".pass.png")
    top = render_tile("top", ctr, s, tw, top_h, meshes, mats, id_mats, nrm, tmp)
    side = render_tile("side", ctr, s, tw, side_h, meshes, mats, id_mats, nrm, tmp)
    sheet[y + 48:y + 48 + top_h, x + MARGIN:x + MARGIN + tw] = top
    sheet[y + 52 + top_h:y + 52 + top_h + side_h, x + MARGIN:x + MARGIN + tw] = side
    label(sheet, x + MARGIN, y, g, ink)
    print("[render] %s slots=%s L=%.2f span=%.2f h=%.2f scale=%.1f px/m tris=%d" % (
        g, ",".join(PILOT[g]), ext.x, ext.y, ext.z, s,
        sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in meshes)))
    return top, s, ext


def grey_of(sheet):
    import numpy as np
    return np.round(sheet.astype(np.float64) @ np.array([0.2126, 0.7152, 0.0722])).astype(np.uint8)


def render_all(kits, out):
    import numpy as np
    check_slot_ids()
    setup_scene()
    ctx = (kits, out, make_materials(), normal_material(), hex_rgb(INK))
    sheet = np.empty((1080, 1920, 3), np.uint8)
    sheet[:] = hex_rgb(PAPER)
    tops = {}
    for k, g in enumerate(sorted(PILOT)):
        top, s, _ = draw_cell(sheet, g, (k % 3) * CELL_W, (k // 3) * CELL_H, CELL_W, TOP_H, SIDE_H, ctx)
        tops[g] = (top, s)
    rule = [0xC9, 0xC4, 0xB6]
    sheet[539:541, :] = rule
    sheet[:, 639:641] = rule
    sheet[:, 1279:1281] = rule
    _png_write(os.path.join(out, "sheet_1920x1080.png"), sheet)
    _png_write(os.path.join(out, "sheet_1920x1080_grey.png"), grey_of(sheet))
    print("[render] G4 vs G6 top-view area difference: %.1f %%" % (100 * planform_diff(tops["G4"], tops["G6"])))
    # plan frame 3: G2 next to G5, one common scale (true relative size)
    pair = np.empty((1080, 1920, 3), np.uint8)
    pair[:] = hex_rgb(PAPER)
    s = min(PAIR_SCALE(g) for g in ("G2", "G5"))
    for k, g in enumerate(("G2", "G5")):
        draw_cell(pair, g, k * 960, 0, 960, 620, 300, ctx, scale=s)
    pair[:, 959:961] = rule
    _png_write(os.path.join(out, "pair_g2_g5.png"), pair)


def PAIR_SCALE(g):
    """Largest scale at which aircraft g fits a 960 x (620 + 300) pair cell; measured from its kit."""
    lo, hi = bounds(assemble(g, KITS, {"fill": None, "dark": None}))
    ext = hi - lo
    return min((960 - 2 * MARGIN - 80) / ext.x, (620 - 40) / ext.y, (300 - 40) / ext.z)


def test_kits(kits):
    """Slot test: every part GLB has exactly its root (unparented, at the origin); the fuselage carries
    ATTACH_wing/tail/engine and the engine ATTACH_afterburner; the kit assembled from GLBs on the ATTACH
    empties has the same bounding box as the saved scene (1 mm)."""
    def world(o):
        m = o.matrix_parent_inverse @ o.matrix_basis
        return world(o.parent) @ m if o.parent else o.matrix_basis.copy()
    ref = {}
    for g in PILOT:
        sc = bpy.data.scenes[g]
        vs = [world(o) @ v.co for o in sc.objects if o.type == "MESH" for v in o.data.vertices]
        ref[g] = (Vector([min(v[i] for v in vs) for i in range(3)]), Vector([max(v[i] for v in vs) for i in range(3)]))
    bpy.ops.wm.read_factory_settings(use_empty=True)
    fails = []
    for g in sorted(PILOT):
        for part in PARTS:
            for o in list(bpy.data.objects):
                bpy.data.objects.remove(o, do_unlink=True)
            objs = import_part(os.path.join(kits, g.lower(), "%s_%s.glb" % (g.lower(), part)))
            roots = [o for o in objs if o.parent is None]
            names = {o.name.split(".")[0] for o in objs}
            if [r.name.split(".")[0] for r in roots] != ["KIT_%s_%s_root" % (g, part)]:
                fails.append("%s %s: roots %s" % (g, part, [r.name for r in roots]))
            elif roots[0].matrix_world.translation.length > 1e-6:
                fails.append("%s %s: root not at origin" % (g, part))
            need = {"fuselage": {"ATTACH_wing", "ATTACH_tail", "ATTACH_engine"},
                    "engine": {"ATTACH_afterburner"}}.get(part, set())
            if need - names:
                fails.append("%s %s: missing %s" % (g, part, sorted(need - names)))
        for o in list(bpy.data.objects):
            bpy.data.objects.remove(o, do_unlink=True)
        meshes = assemble(g, kits, {"fill": None, "dark": None})
        lo, hi = bounds_exact(meshes)
        d = max((lo - ref[g][0]).length, (hi - ref[g][1]).length)
        print("[test] %s assembled bbox vs scene: %.4f m" % (g, d))
        if d > 1e-3:
            fails.append("%s: assembled bbox differs by %.4f m" % (g, d))
    print("[test] %s" % ("PASS" if not fails else "FAIL " + "; ".join(fails)))
    if fails:
        sys.exit(1)


def bounds_exact(meshes):
    vs = [o.matrix_world @ v.co for o in meshes for v in o.data.vertices]
    return (Vector([min(v[i] for v in vs) for i in range(3)]), Vector([max(v[i] for v in vs) for i in range(3)]))


def planform_diff(a, b):
    """Share of the union area where the two top silhouettes differ, both scaled to the same length,
    nose and centreline aligned (XOR / union of non-paper pixels)."""
    import numpy as np
    paper = np.array(hex_rgb(PAPER))

    def mask(img_s):
        img, s = img_s
        m = np.abs(img.astype(int) - paper).sum(axis=2) > 30
        ys, xs = np.nonzero(m)
        m = m[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
        n = 400  # resample to 400 px length, keep the aspect (nearest)
        h = max(1, int(round(m.shape[0] * n / m.shape[1])))
        yi = (np.arange(h) * m.shape[0] / h).astype(int)
        xi = (np.arange(n) * m.shape[1] / n).astype(int)
        return m[yi][:, xi]
    ma, mb = mask(a), mask(b)
    h = max(ma.shape[0], mb.shape[0])

    def pad(m):
        p = np.zeros((h, 400), bool)
        o = (h - m.shape[0]) // 2
        p[o:o + m.shape[0]] = m
        return p
    ma, mb = pad(ma), pad(mb)
    return (ma ^ mb).sum() / (ma | mb).sum()


def main(argv):
    import argparse
    ap = argparse.ArgumentParser(prog="fighters_v2.py")
    ap.add_argument("mode", choices=("export", "render", "test"))
    ap.add_argument("--kits", default=KITS)
    ap.add_argument("--out", default=KITS)
    a = ap.parse_args(argv)
    os.makedirs(a.out, exist_ok=True)
    if a.mode == "export":
        export(a.kits)
    elif a.mode == "test":
        test_kits(a.kits)
    else:
        render_all(a.kits, a.out)


if __name__ == "__main__":
    main(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
