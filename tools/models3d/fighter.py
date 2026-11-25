"""Parametric jet fighter generator (3D pilot, O-075, docs/ui/specs/3d-models-plan.md variant a).

Builds a generic (not a replica of any real aircraft) fighter mesh from the design slots of
mods/core/product_classes.yaml (engine, airframe, afterburner) plus an engine count, and renders
blueprint / engraving style orthographic views (top, side, front) on paper with Freestyle ink lines.

Run (headless, Blender 5.3):
  nice -n 10 blender -b --factory-startup -P tools/models3d/fighter.py -- \
      [--out assets/models3d/fighters] [--gens G1,G2,...] [--no-sheet]
  nice -n 10 blender -b --factory-startup -P tools/models3d/fighter.py -- \
      --design eng_g4,af_mach2,ab_on --engines 2 --name custom

Deterministic: no random numbers; Cycles CPU with a fixed seed, sample count and thread count;
render metadata stamps are off, so the same parameters give the same PNG bytes on one machine.
Own procedural geometry only (ADR-0009); no external models or textures.
"""

import math
import os
import re
import struct
import sys
import zlib

import bpy
import bmesh
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
PRODUCT_CLASSES = os.path.join(REPO, "mods", "core", "product_classes.yaml")

# ---------------------------------------------------------------- style (UI_DESIGN_SYSTEM.md §2.1)
PAPER = "#F2EFE6"   # color.paper
INK = "#1D1E20"     # color.ink
FILL = "#E4DFD2"    # upper / lit surfaces (paper darkened, flat)
SHADE = "#CBC4B4"   # downward-facing surfaces (flat step, no gradient)
DARK = "#7E7A71"    # glazing, intake and nozzle openings

# ---------------------------------------------------------------- slot -> geometry tables
# Engine generation: overall size, intake layout and nozzle style. Lengths in metres (generic).
ENGINE = {
    "eng_g1":          dict(gen=1, length=10.5, radius=0.78, intake="nose",       nozzle="round", noz_r=0.42),
    "eng_g2":          dict(gen=2, length=11.5, radius=0.80, intake="nose",       nozzle="round", noz_r=0.44),
    "eng_g3":          dict(gen=3, length=14.5, radius=0.82, intake="side_round", nozzle="round", noz_r=0.50),
    "eng_g4":          dict(gen=4, length=18.5, radius=0.92, intake="side_rect",  nozzle="round", noz_r=0.52),
    "eng_g5":          dict(gen=5, length=15.0, radius=0.90, intake="side_rect",  nozzle="round", noz_r=0.56),
    "eng_g6":          dict(gen=6, length=19.0, radius=1.05, intake="caret",      nozzle="flat",  noz_r=0.58),
    "eng_g6_adaptive": dict(gen=6, length=19.5, radius=1.08, intake="caret",      nozzle="flat",  noz_r=0.62),
}
# Default engine count per generation archetype (O-075): G1-G3 and "4+" one engine, G4 and "F-22 class" two.
ENGINE_COUNT_BY_GEN = {1: 1, 2: 1, 3: 1, 4: 2, 5: 1, 6: 2}

# Airframe: planform and layout. Fractions are of fuselage length L (x) or fuselage radius R (z).
# wing: root chord, semi-span, LE sweep (deg), taper, root LE x, z (of R), dihedral (deg), t/c, profile.
AIRFRAME = {
    "af_straight": dict(af_class=1, section="round", waist=0.0,
                        wing=dict(c=0.22, b=0.50, sweep=6, taper=0.55, x=0.36, z=-0.30, dih=4, tc=0.12, prof="naca"),
                        tail="tailplane", tp=dict(c=0.12, b=0.19, sweep=12, taper=0.6, z=0.15),
                        fin=dict(c=0.20, h=0.20, sweep=32, taper=0.5), canard=None, lerx=0.0, tip_tanks=True),
    "af_swept":    dict(af_class=2, section="round", waist=0.0,
                        wing=dict(c=0.26, b=0.43, sweep=40, taper=0.45, x=0.30, z=0.05, dih=0, tc=0.09, prof="naca"),
                        tail="tailplane", tp=dict(c=0.12, b=0.17, sweep=40, taper=0.45, z=0.35),
                        fin=dict(c=0.22, h=0.21, sweep=48, taper=0.40), canard=None, lerx=0.0, tip_tanks=False),
    "af_milled":   dict(af_class=3, section="round", waist=0.16,
                        wing=dict(c=0.56, b=0.30, sweep=58, taper=0.06, x=0.30, z=-0.20, dih=0, tc=0.05, prof="naca"),
                        tail="none", tp=None,
                        fin=dict(c=0.26, h=0.22, sweep=55, taper=0.25), canard=None, lerx=0.0, tip_tanks=False),
    "af_titanium": dict(af_class=3, section="round", waist=0.12,
                        wing=dict(c=0.40, b=0.28, sweep=52, taper=0.16, x=0.35, z=-0.10, dih=-2, tc=0.045, prof="naca"),
                        tail="tailplane", tp=dict(c=0.12, b=0.15, sweep=50, taper=0.3, z=0.0),
                        fin=dict(c=0.24, h=0.20, sweep=52, taper=0.30), canard=None, lerx=0.0, tip_tanks=False),
    "af_mach2":    dict(af_class=4, section="boxy", waist=0.06,
                        wing=dict(c=0.30, b=0.30, sweep=40, taper=0.30, x=0.40, z=0.10, dih=0, tc=0.045, prof="naca"),
                        tail="tailplane", tp=dict(c=0.15, b=0.20, sweep=45, taper=0.30, z=0.0),
                        fin=dict(c=0.20, h=0.17, sweep=45, taper=0.35), canard=None, lerx=0.10, tip_tanks=False),
    "af_fbw":      dict(af_class=4, section="blend", waist=0.0, ventral=True,
                        wing=dict(c=0.32, b=0.30, sweep=40, taper=0.28, x=0.43, z=0.05, dih=0, tc=0.045, prof="naca"),
                        tail="tailplane", tp=dict(c=0.14, b=0.18, sweep=40, taper=0.30, z=0.0),
                        fin=dict(c=0.22, h=0.20, sweep=45, taper=0.35), canard=None, lerx=0.16, tip_tanks=False),
    "af_composite": dict(af_class=5, section="blend", waist=0.0,
                        wing=dict(c=0.46, b=0.28, sweep=52, taper=0.14, x=0.40, z=-0.05, dih=0, tc=0.04, prof="naca"),
                        tail="none", tp=None,
                        fin=dict(c=0.24, h=0.20, sweep=50, taper=0.30),
                        canard=dict(c=0.10, b=0.15, sweep=45, taper=0.35, x=0.25, z=0.35), lerx=0.0, tip_tanks=False),
    "af_lo_composite": dict(af_class=6, section="chine", waist=0.0,
                        wing=dict(c=0.40, b=0.27, sweep=42, taper=0.18, x=0.38, z=0.10, dih=0, tc=0.035, prof="diamond"),
                        tail="tailplane", tp=dict(c=0.16, b=0.16, sweep=42, taper=0.25, z=0.0),
                        fin=dict(c=0.20, h=0.15, sweep=42, taper=0.30, cant=27), canard=None, lerx=0.0,
                        tip_tanks=False),
}
AFTERBURNER = {"ab_none": False, "ab_on": True}

# The six pilot designs (plan §4); each one is a reachable slot combination.
PILOT = {
    "G1": ("eng_g1", "af_straight", "ab_none"),
    "G2": ("eng_g2", "af_swept", "ab_none"),
    "G3": ("eng_g3", "af_milled", "ab_on"),
    "G4": ("eng_g4", "af_mach2", "ab_on"),
    "G5": ("eng_g5", "af_composite", "ab_on"),
    "G6": ("eng_g6", "af_lo_composite", "ab_on"),
}


def check_slot_ids(path=PRODUCT_CLASSES):
    """Every slot id used by the tables must exist in product_classes.yaml (text scan, no PyYAML in Blender)."""
    with open(path, encoding="utf-8") as f:
        ids = set(re.findall(r"\{id:\s*([a-z0-9_]+)", f.read()))
    missing = [k for k in list(ENGINE) + list(AIRFRAME) + list(AFTERBURNER) if k not in ids]
    if missing:
        raise ValueError("slot ids not in %s: %s" % (path, missing))


def design_params(engine, airframe, afterburner, engines=None):
    """Slots -> geometry parameter dict (pure function; the only input of build())."""
    e, a = ENGINE[engine], AIRFRAME[airframe]
    n = engines if engines is not None else ENGINE_COUNT_BY_GEN[e["gen"]]
    intake = "ventral" if a.get("ventral") else e["intake"]
    if intake == "caret" and a["section"] != "chine":
        intake = "side_rect"
    return dict(slots=(engine, airframe, afterburner), gen=e["gen"], L=e["length"], R=e["radius"],
                engines=n, intake=intake, nozzle=e["nozzle"], noz_r=e["noz_r"], ab=AFTERBURNER[afterburner],
                fins=2 if (n == 2 and a["af_class"] >= 4) else 1, bubble=e["gen"] >= 4, **{
                    k: a[k] for k in ("af_class", "section", "waist", "wing", "tail", "tp", "fin", "canard",
                                      "lerx", "tip_tanks")})


# ---------------------------------------------------------------- mesh helpers
def hex_lin(h):
    c = [int(h[i:i + 2], 16) / 255.0 for i in (1, 3, 5)]
    return tuple(v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4 for v in c)


class Builder:
    def __init__(self):
        self.bm = bmesh.new()

    def _ring(self, pts):
        return [self.bm.verts.new(p) for p in pts]

    def loft(self, rings, start="point", end="point", mat=0, cap_mat=2, inset=0.78, depth=0.15):
        """rings: list of equal-length lists of Vector. caps: 'point' (fan), 'flat', 'inset' (lip + dark face)."""
        vr = [self._ring(r) for r in rings]
        n = len(rings[0])
        for a, b in zip(vr, vr[1:]):
            for i in range(n):
                f = self.bm.faces.new((a[i], a[(i + 1) % n], b[(i + 1) % n], b[i]))
                f.material_index = mat
        self._cap(vr[0], rings[0], start, mat, cap_mat, inset, depth, rings[1])
        self._cap(vr[-1], rings[-1], end, mat, cap_mat, inset, depth, rings[-2])

    def _cap(self, verts, pts, kind, mat, cap_mat, inset, depth, nxt):
        if kind is None:
            return
        c = sum(pts, Vector()) / len(pts)
        cn = sum(nxt, Vector()) / len(nxt)
        n = len(verts)
        if kind == "inset":
            axis = (cn - c).normalized()
            inner = self._ring([c + (p - c) * inset + axis * depth for p in pts])
            for i in range(n):
                f = self.bm.faces.new((verts[i], verts[(i + 1) % n], inner[(i + 1) % n], inner[i]))
                f.material_index = mat
            verts, c = inner, c + axis * depth
            mat = cap_mat
        if kind == "flat" or kind == "inset":
            f = self.bm.faces.new(verts)
            f.material_index = mat
            return
        tip = self.bm.verts.new(c)
        for i in range(n):
            f = self.bm.faces.new((verts[i], verts[(i + 1) % n], tip))
            f.material_index = mat

    def surface(self, stations, span_dir, thick_dir, tc, prof, mirror, mat=0):
        """Lifting surface: stations = [(le Vector, chord)], chord along +x, lofted airfoils, closed."""
        fr = [0.0, 0.03, 0.10, 0.22, 0.36, 0.52, 0.68, 0.84, 1.0] if prof == "naca" else [0.0, 0.45, 1.0]

        def half(f):
            if prof == "diamond":
                return tc * (f / 0.45 if f <= 0.45 else (1 - f) / 0.55) * 0.5
            return 5 * tc * (0.2969 * math.sqrt(f) - 0.1260 * f - 0.3516 * f * f + 0.2843 * f ** 3 - 0.1036 * f ** 4)

        sides = (1, -1) if mirror else (1,)
        for s in sides:
            rings = []
            for le, ch in stations:
                le = Vector((le.x, le.y * s, le.z))
                td = Vector((thick_dir.x, thick_dir.y * s, thick_dir.z))
                up = [le + Vector((f * ch, 0, 0)) + td * (half(f) * ch) for f in fr]
                lo = [le + Vector((f * ch, 0, 0)) - td * (half(f) * ch) for f in fr[1:-1]]
                rings.append(up + lo[::-1])
            self.loft(rings, "flat", "flat", mat=mat)

    def to_object(self, name, mats):
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        me = bpy.data.meshes.new(name)
        self.bm.to_mesh(me)
        self.bm.free()
        for m in mats:
            me.materials.append(m)
        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        return ob


def section(kind, w, h, yc, zc, x, n=24):
    """Cross-section ring at x: round/boxy/blend superellipse, or faceted 'chine' for low-observable."""
    if kind == "chine":
        prof = [(0, 1.0), (0.45, 0.85), (1.0, 0.02), (0.62, -0.62), (0.0, -0.80),
                (-0.62, -0.62), (-1.0, 0.02), (-0.45, 0.85)]
        return [Vector((x, yc + w * py, zc + h * pz)) for py, pz in prof]
    e = {"round": 2.0, "blend": 2.4, "boxy": 2.8, "rect": 5.0, "flat": 6.0, "rhomb": 1.3}[kind]
    out = []
    for i in range(n):
        t = 2 * math.pi * i / n
        c, s = math.cos(t), math.sin(t)
        out.append(Vector((x, yc + w * math.copysign(abs(c) ** (2 / e), c),
                           zc + h * math.copysign(abs(s) ** (2 / e), s))))
    return out


def smooth(t):
    t = min(1.0, max(0.0, t))
    return t * t * (3 - 2 * t)


# ---------------------------------------------------------------- aircraft
def build(p, mats):
    """Builds the aircraft as one object at the origin (nose at x=0 pointing -x... flipped below)."""
    L, R = p["L"], p["R"]
    nr = p["noz_r"]
    twin = p["engines"] == 2
    noz_sep = nr * 1.12 if twin else 0.0
    w_aft = (noz_sep + nr * 1.08) if twin else nr * 1.08
    h_aft = nr * (0.62 if p["nozzle"] == "flat" else 1.06)
    w_mid = max(R, w_aft) if twin else R
    wing = p["wing"]
    x_w = (wing["x"] + wing["c"] * 0.5) * L
    nose_intake = p["intake"] == "nose"
    fn, ft = 0.30, 0.74
    kind = p["section"]
    B = Builder()

    def fus(f):
        """width, height, z-centre of the fuselage at fraction f."""
        x = f * L
        if f < fn:
            t = f / fn
            k = (0.70 + 0.30 * math.sin(t * math.pi / 2)) if nose_intake else max(0.02, math.sin(t * math.pi / 2) ** 0.9)
            w, h = R * k, R * k
            zc = 0.0 if nose_intake else -0.18 * R * (1 - t) ** 2
        elif f <= ft:
            t = smooth((f - fn) / 0.2)
            w, h, zc = R + (w_mid - R) * t, R, 0.0
        else:
            t = smooth((f - ft) / (1 - ft))
            w, h, zc = w_mid + (w_aft - w_mid) * t, R + (h_aft - R) * t, 0.0
        if p["waist"]:
            k = 1 - p["waist"] * math.exp(-((x - x_w) / (0.09 * L)) ** 2)
            w *= k
        return w, h, zc

    N = 36
    rings = []
    for i in range(N + 1):
        f = i / N
        w, h, zc = fus(f)
        rings.append(section(kind, w, h, 0.0, zc, f * L))
    B.loft(rings, start="inset" if nose_intake else "point", end="flat", inset=0.80, depth=0.3 * R)

    # canopy
    xc0 = (0.13 if nose_intake else 0.16) * L
    clen = (0.20 if p["bubble"] else 0.15) * L
    crings = []
    for i in range(13):
        t = i / 12
        x = xc0 + t * clen
        w, h, zc = fus(x / L)
        k = max(0.03, math.sin(math.pi * t) ** (0.55 if p["bubble"] else 0.8))
        if t > 0.5 and not p["bubble"]:
            k = max(0.03, k ** 0.6)
        ck = "chine" if kind == "chine" else "round"
        crings.append(section(ck, 0.46 * R * k, 0.55 * R * k, 0.0, zc + h * 0.72, x, n=16))
    B.loft(crings, "point", "point", mat=2)

    # wings (with optional leading-edge root extension)
    b, c0 = wing["b"] * L, wing["c"] * L
    xle, z = wing["x"] * L, wing["z"] * R
    tan = math.tan(math.radians(wing["sweep"]))
    c_tip = c0 * wing["taper"]
    x_te_root, x_te_tip = xle + c0, xle + b * tan + c_tip
    dz = math.tan(math.radians(wing["dih"]))
    st = []
    if p["lerx"]:
        yl = w_mid * 1.15
        st.append((Vector((xle - p["lerx"] * L, 0, z)), x_te_root - (xle - p["lerx"] * L)))
        xl = xle + yl * tan
        st.append((Vector((xl, yl, z + yl * dz)), x_te_root + (x_te_tip - x_te_root) * yl / b - xl))
    else:
        st.append((Vector((xle, 0, z)), c0))
    st.append((Vector((xle + b * tan, b, z + b * dz)), c_tip))
    B.surface(st, Vector((0, 1, 0)), Vector((0, 0, 1)), wing["tc"], wing["prof"], True)

    if p["tip_tanks"]:
        xt, yt, zt, tl = xle + b * tan - 0.25 * c_tip, b, z + b * dz, 1.9 * c_tip
        tr = 0.30 * R
        for s in (1, -1):
            B.loft([section("round", tr * max(0.04, math.sin(math.pi * i / 10) ** 0.6),
                            tr * max(0.04, math.sin(math.pi * i / 10) ** 0.6), s * yt, zt, xt + tl * i / 10, n=16)
                    for i in range(11)], "point", "point")

    def small_surface(d, x0, z0, y0=0.0):
        bb, cc = d["b"] * L, d["c"] * L
        tn = math.tan(math.radians(d["sweep"]))
        B.surface([(Vector((x0, y0, z0)), cc), (Vector((x0 + bb * tn, y0 + bb, z0)), cc * d["taper"])],
                  Vector((0, 1, 0)), Vector((0, 0, 1)), 0.05, wing["prof"], True)

    if p["tail"] == "tailplane":
        tp = p["tp"]
        small_surface(tp, L * 0.985 - tp["c"] * L - tp["b"] * L * math.tan(math.radians(tp["sweep"])) * 0.35,
                      tp["z"] * R)
    if p["canard"]:
        cn = p["canard"]
        small_surface(cn, cn["x"] * L, cn["z"] * R)

    # fins
    fin = p["fin"]
    fc, fh = fin["c"] * L, fin["h"] * L
    ftan = math.tan(math.radians(fin["sweep"]))
    fx = L * 0.97 - fc - fh * ftan * 0.45
    cant = math.radians(fin.get("cant", 0 if p["fins"] == 1 else 12))
    ys = [0.0] if p["fins"] == 1 else [w_aft * 0.72]
    for y0 in ys:
        _, hh, _ = fus((fx + fc * 0.5) / L)
        root = Vector((fx, y0, hh * 0.55))
        span = Vector((0, math.sin(cant), math.cos(cant))) if p["fins"] == 2 else Vector((0, 0, 1))
        thick = Vector((0, math.cos(cant), -math.sin(cant))) if p["fins"] == 2 else Vector((0, 1, 0))
        tip = root + span * fh + Vector((fh * ftan, 0, 0))
        B.surface([(root, fc), (tip, fc * fin["taper"])], span, thick, 0.06, wing["prof"], p["fins"] == 2)

    # intakes
    it = p["intake"]
    if it != "nose":
        x0 = (0.27 if it != "ventral" else 0.30) * L
        x1 = max(x0 + 0.12 * L, xle + 0.05 * L)
        if it == "ventral":
            places = [(0.0, -1)]
        else:
            places = [(1, 0), (-1, 0)]
        for side, below in places:
            irings = []
            for i in range(9):
                t = i / 8
                x = x0 + (x1 - x0) * t
                w, h, zc = fus(x / L)
                k = 1.0 - 0.55 * smooth((t - 0.5) / 0.5)
                if it == "side_round":
                    iw, ih, ik = 0.42 * R, 0.55 * R, "round"
                    yc, zz = side * (w + iw * 0.35), zc - 0.15 * R
                elif it == "side_rect":
                    iw, ih, ik = 0.36 * R, 0.72 * R, "rect"
                    yc, zz = side * (w + iw * 0.55 + 0.05 * R), zc - 0.12 * R
                elif it == "caret":
                    iw, ih, ik = 0.42 * R, 0.62 * R, "rhomb"
                    yc, zz = side * (w + iw * 0.25), zc - 0.08 * R
                else:  # ventral
                    iw, ih, ik = 0.60 * R, 0.42 * R, "rect"
                    yc, zz = 0.0, zc - h * 0.85
                ring = section(ik, iw * (k if it != "ventral" else 1.0), ih * k, yc, zz, x, n=24)
                if i == 0:  # raked lip
                    for v in ring:
                        dzl, dyl = v.z - zz, (v.y - yc) * (side if side else 1)
                        if it == "side_rect":
                            v.x += -0.45 * dzl
                        elif it == "caret":
                            v.x += -0.50 * dzl + 0.55 * dyl
                        elif it == "ventral":
                            v.x += 0.55 * dzl
                irings.append(ring)
            B.loft(irings, "inset", "point", inset=0.72, depth=0.25 * R)

    # nozzles
    ys = [-noz_sep, noz_sep] if twin else [0.0]
    flat = p["nozzle"] == "flat"
    for y0 in ys:
        nk = "flat" if flat else "round"
        wq, hq = (nr * 1.0, nr * 0.55) if flat else (nr, nr)
        if p["ab"]:
            prof = [(-0.05, 1.0), (0.0, 1.0), (0.006, 0.93), (0.055, 1.02)]
        else:
            prof = [(-0.05, 1.0), (0.0, 1.0), (0.025, 0.88)]
        nrings = [section(nk, wq * k, hq * k, y0, 0.0, L * (1 + dx), n=24) for dx, k in prof]
        B.loft(nrings, "flat", "inset", inset=0.80, depth=-0.02 * L)

    ob = B.to_object("fighter", mats)
    for poly in ob.data.polygons:
        poly.use_smooth = False
    return ob


# ---------------------------------------------------------------- scene and render
def make_materials():
    out = []
    for name, col in (("fill", FILL), ("shade", SHADE), ("dark", DARK)):
        m = bpy.data.materials.new(name)
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
        out.append(m)
    return out[0], out[1], out[2]


def setup_scene():
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    sc = bpy.context.scene
    r = sc.render
    r.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 24
    sc.cycles.use_adaptive_sampling = False
    sc.cycles.use_denoising = False
    sc.cycles.seed = 0
    sc.cycles.use_animated_seed = False
    sc.cycles.max_bounces = 0
    r.threads_mode = "FIXED"
    r.threads = 4
    r.film_transparent = False
    r.resolution_percentage = 100
    r.dither_intensity = 0.0  # dither noise would break flat fills and PNG size
    for a in dir(r):
        if a.startswith("use_stamp"):
            setattr(r, a, False)
    r.image_settings.file_format = "PNG"
    r.image_settings.color_mode = "RGB"
    r.image_settings.color_depth = "8"
    r.image_settings.compression = 100
    sc.view_settings.view_transform = "Standard"
    sc.view_settings.look = "None"
    sc.view_settings.exposure = 0.0
    sc.view_settings.gamma = 1.0
    w = bpy.data.worlds.new("paper")
    w.use_nodes = True
    bg = w.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (*hex_lin(PAPER), 1)
    bg.inputs["Strength"].default_value = 1.0
    sc.world = w
    # Freestyle ink: thick outer contour + thin silhouettes, borders and creases
    r.use_freestyle = True
    r.line_thickness_mode = "ABSOLUTE"
    vl = sc.view_layers[0]
    vl.use_freestyle = True
    fs = vl.freestyle_settings
    fs.crease_angle = math.radians(140)
    for ls in list(fs.linesets):
        fs.linesets.remove(ls)
    styles = {}
    for name, edges in (("outline", ("external_contour",)), ("detail", ("silhouette", "border", "crease"))):
        ls = fs.linesets.new(name)
        ls.select_by_visibility = True
        ls.visibility = "VISIBLE"
        ls.select_by_edge_types = True
        for e in ("silhouette", "border", "crease", "external_contour", "contour", "ridge_valley",
                  "suggestive_contour", "material_boundary", "edge_mark"):
            setattr(ls, "select_" + e, e in edges)
        st = bpy.data.linestyles.new(name)
        st.color = hex_lin(INK)
        st.caps = "ROUND"
        st.thickness_position = "CENTER"
        ls.linestyle = st
        styles[name] = st
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    cam.data.type = "ORTHO"
    sc.collection.objects.link(cam)
    sc.camera = cam
    return styles


VIEWS = {  # camera direction (unit, from target towards camera) and euler
    "top": (Vector((0, 0, 1)), (0.0, 0.0, -math.pi / 2)),
    "side": (Vector((0, 1, 0)), (math.pi / 2, 0.0, math.pi)),
    "front": (Vector((-1, 0, 0)), (math.pi / 2, 0.0, -math.pi / 2)),
}


def render_view(ob, styles, view, path, size, outline=3.0, detail=1.4):
    sc = bpy.context.scene
    cam = sc.camera
    vs = [ob.matrix_world @ v.co for v in ob.data.vertices]
    lo = Vector([min(v[i] for v in vs) for i in range(3)])
    hi = Vector([max(v[i] for v in vs) for i in range(3)])
    ctr, ext = (lo + hi) / 2, hi - lo
    d, eul = VIEWS[view]
    cam.data.ortho_scale = max(ext.x, ext.y, ext.z) * 1.10
    cam.data.clip_start, cam.data.clip_end = 0.1, 200.0
    cam.location = ctr + d * 60.0
    cam.rotation_euler = eul
    sc.render.resolution_x = sc.render.resolution_y = size
    styles["outline"].thickness = outline
    styles["detail"].thickness = detail
    tmp = path + ".raw.png"
    sc.render.filepath = tmp
    bpy.ops.render.render(write_still=True)
    # Cycles writes render-time text chunks into the PNG; re-encode pixels only so bytes are reproducible.
    _png_write(path, _png_read(tmp))
    os.remove(tmp)


def build_design(engine, airframe, afterburner, engines=None):
    p = design_params(engine, airframe, afterburner, engines)
    for ob in [o for o in bpy.data.objects if o.type == "MESH"]:
        bpy.data.objects.remove(ob, do_unlink=True)
    mats = [bpy.data.materials.get(n) for n in ("fill", "shade", "dark")]
    if mats[0] is None:
        mats = list(make_materials())
    ob = build(p, mats)
    # nose at x=0 pointing -x after a flip: views expect the nose towards -x? keep nose at +x
    ob.scale.x = -1.0
    bpy.context.view_layer.update()
    return ob, p


# ---------------------------------------------------------------- contact sheet
def _png_write(path, arr):
    """arr: uint8 HxWx3 numpy array, top row first. Plain RGB PNG with no metadata, zlib level 9, filter 'Up'."""
    import numpy as np
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


def make_sheet(tiles, path, tmp):
    """tiles: list of (gen, {view: tile png}). 3x2 cells 640x540; top view 400, side/front 210; gen ticks."""
    import numpy as np
    sheet = np.empty((1080, 1920, 3), np.uint8)
    sheet[:] = [int(PAPER[i:i + 2], 16) for i in (1, 3, 5)]
    ink = [int(INK[i:i + 2], 16) for i in (1, 3, 5)]
    rule = [0xC9, 0xC4, 0xB6]
    for k, (gen, views) in enumerate(tiles):
        cx, cy = (k % 3) * 640, (k // 3) * 540
        for view, (ox, oy) in (("top", (12, 100)), ("side", (420, 50)), ("front", (420, 290))):
            a = _png_read(views[view])
            sheet[cy + oy:cy + oy + a.shape[0], cx + ox:cx + ox + a.shape[1]] = a
        for g in range(gen):  # generation shown as a count of ink squares, no text
            x0 = cx + 20 + g * 16
            sheet[cy + 20:cy + 30, x0:x0 + 10] = ink
    sheet[539:541, :] = rule
    sheet[:, 639:641] = rule
    sheet[:, 1279:1281] = rule
    _png_write(path, sheet)


# ---------------------------------------------------------------- main
def main(argv):
    import argparse
    ap = argparse.ArgumentParser(prog="fighter.py")
    ap.add_argument("--out", default=os.path.join(REPO, "assets", "models3d", "fighters"))
    ap.add_argument("--gens", default=",".join(PILOT))
    ap.add_argument("--design", help="engine,airframe,afterburner slot ids")
    ap.add_argument("--engines", type=int)
    ap.add_argument("--name", default="custom")
    ap.add_argument("--size", type=int, default=1024)
    ap.add_argument("--no-sheet", action="store_true")
    a = ap.parse_args(argv)
    check_slot_ids()
    os.makedirs(a.out, exist_ok=True)
    styles = setup_scene()
    jobs = [(a.name, tuple(a.design.split(",")))] if a.design else [(g, PILOT[g]) for g in a.gens.split(",")]
    tiles = []
    tmp = os.path.join(a.out, ".tiles")
    for name, slots in jobs:
        ob, p = build_design(*slots, engines=a.engines)
        print("[fighter] %s %s engines=%d intake=%s faces=%d" % (name, slots, p["engines"], p["intake"],
                                                                 len(ob.data.polygons)))
        views = {}
        for v in VIEWS:
            render_view(ob, styles, v, os.path.join(a.out, "%s_%s.png" % (name.lower(), v)), a.size)
            if not a.no_sheet and not a.design:
                os.makedirs(tmp, exist_ok=True)
                tp = os.path.join(tmp, "%s_%s.png" % (name.lower(), v))
                render_view(ob, styles, v, tp, 400 if v == "top" else 210,
                            outline=2.0 if v == "top" else 1.5, detail=1.0 if v == "top" else 0.8)
                views[v] = tp
        tiles.append((p["gen"], views))
    if not a.no_sheet and not a.design:
        make_sheet(tiles, os.path.join(a.out, "sheet_1920x1080.png"), tmp)
        for f in sorted(os.listdir(tmp)):
            os.remove(os.path.join(tmp, f))
        os.rmdir(tmp)


if __name__ == "__main__":
    main(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
