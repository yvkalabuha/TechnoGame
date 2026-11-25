"""Headless fixes of the Gemini fighter kits after art-director review 3d-v2-review-2026-11-25.md section 4.

  nice -n 10 blender -b assets/models3d/fighters_v2/fighters_v2.blend -P tools/models3d/fighters_v2_fix.py

1. G6: profile 1.8x thicker (world z about z = 0, mount points move with it); the aft deck shield and the flat
   nozzles become slabs of even thickness (no wedge tapering to a needle in the side view; planform unchanged).
2. G3, G5: fin trailing edge made one straight line from root to tip (the Gemini fin had a step at the tip station).
Idempotent: each scene records the fixes it got in scene["fixes"]; the file is saved in place.
"""

import bpy
from mathutils import Matrix, Vector

G6_Z = 1.8
G6_AFT_HALF = 0.16   # half thickness (m) of the aft deck and flat nozzle slabs after the fix
FINS = {"G3": "G3_fin", "G5": "G5_fin"}


def world(o):
    """World matrix from the parent chain (matrix_world of a non-active scene is not refreshed in -b mode)."""
    m = o.matrix_parent_inverse @ o.matrix_basis
    return world(o.parent) @ m if o.parent else o.matrix_basis.copy()


def depth(o):
    return 1 + depth(o.parent) if o.parent else 0


def scale_scene_z(sc, k):
    """Scales every object of the scene in world z by k about z = 0, keeping parenting and part roots."""
    S = Matrix.Diagonal((1.0, 1.0, k, 1.0))
    objs = list(sc.objects)
    old = {o: world(o) for o in objs}
    for o in sorted(objs, key=depth):  # parents first
        want = old[o].copy()
        want.translation = Vector((want.translation.x, want.translation.y, want.translation.z * k))
        pw = world(o.parent) @ o.matrix_parent_inverse if o.parent else Matrix.Identity(4)
        o.matrix_basis = pw.inverted() @ want
    for o in objs:
        if o.type != "MESH":
            continue
        if o.data.users > 1:
            o.data = o.data.copy()
        o.data.transform(world(o).inverted() @ S @ old[o])
        o.data.update()


def slab(o, half):
    """Rebuilds the mesh as a slab of even thickness 2*half: its upward faces, flattened onto the top plane,
    extruded straight down. Planform kept; a wedge trailing edge becomes a blunt vertical edge."""
    import bmesh
    mw = world(o)
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bm.transform(mw)
    zc = (min(v.co.z for v in bm.verts) + max(v.co.z for v in bm.verts)) / 2
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.normal.z <= 0.05], context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    for v in bm.verts:
        v.co.z = zc + half
    ext = bmesh.ops.extrude_face_region(bm, geom=list(bm.faces))
    down = [e for e in ext["geom"] if isinstance(e, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, verts=down, vec=(0, 0, -2 * half))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.transform(mw.inverted())
    bm.to_mesh(o.data)
    bm.free()
    o.data.update()


def straighten_fin_te(o):
    """Each z station of the fin: the chord is stretched about its leading edge (max x, nose +X) so that the
    trailing edge lies on the straight line from the root trailing edge to the tip trailing edge."""
    mw = world(o)
    mi = mw.inverted()
    st = {}
    for v in o.data.vertices:
        w = mw @ v.co
        st.setdefault(round(w.z, 3), []).append(v)
    zs = sorted(st)
    te = {z: min((mw @ v.co).x for v in st[z]) for z in zs}
    le = {z: max((mw @ v.co).x for v in st[z]) for z in zs}
    z0, z1 = zs[0], zs[-1]
    for z in zs[1:-1]:
        want = te[z0] + (te[z1] - te[z0]) * (z - z0) / (z1 - z0)
        f = (le[z] - want) / (le[z] - te[z])
        for v in st[z]:
            w = mw @ v.co
            w.x = le[z] - (le[z] - w.x) * f
            v.co = mi @ w
        print("[fix] %s z=%.2f te %.3f -> %.3f" % (o.name, z, te[z], want))
    o.data.update()


def main():
    sc6 = bpy.data.scenes["G6"]
    if "g6_profile" not in sc6.get("fixes", ""):
        scale_scene_z(sc6, G6_Z)
        for n in ("G6_aft_deck_shield", "G6_flat_nozzles"):
            slab(bpy.data.objects[n], G6_AFT_HALF)
        sc6["fixes"] = sc6.get("fixes", "") + " g6_profile"
    for g, n in FINS.items():
        sc = bpy.data.scenes[g]
        if "fin_te" not in sc.get("fixes", ""):
            straighten_fin_te(bpy.data.objects[n])
            sc["fixes"] = sc.get("fixes", "") + " fin_te"
    bpy.ops.wm.save_mainfile()
    print("[fix] saved", bpy.data.filepath)


main()
