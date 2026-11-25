"""Determinism check for the fighter generator: one view rendered twice must give identical bytes.

Run: nice -n 10 blender -b --factory-startup --python-exit-code 1 -P tools/models3d/test_determinism.py
Exit code 0 = pass. Also reports whether the render equals the committed asset (same machine and Blender).
"""
import hashlib
import os
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fighter as F  # noqa: E402


def sha(path):
    with open(path, "rb") as f:
        return hashlib.sha256(f.read()).hexdigest()


def main():
    F.check_slot_ids()
    styles = F.setup_scene()
    tmp = tempfile.mkdtemp(prefix="fighter_det_")
    paths = []
    for i in range(2):
        ob, _ = F.build_design(*F.PILOT["G3"])  # rebuilt from scratch each time
        p = os.path.join(tmp, "g3_top_%d.png" % i)
        F.render_view(ob, styles, "top", p, 1024)
        paths.append(p)
    h = [sha(p) for p in paths]
    committed = os.path.join(F.REPO, "assets", "models3d", "fighters", "g3_top.png")
    same_as_asset = os.path.exists(committed) and sha(committed) == h[0]
    # a different slot must give a different picture
    ob, _ = F.build_design("eng_g3", "af_titanium", "ab_on")
    p3 = os.path.join(tmp, "g3_titanium_top.png")
    F.render_view(ob, styles, "top", p3, 1024)
    h3 = sha(p3)
    for p in paths + [p3]:
        os.remove(p)
    os.rmdir(tmp)
    print("[determinism] run1=%s run2=%s equal=%s committed_asset_equal=%s slot_change_differs=%s"
          % (h[0][:16], h[1][:16], h[0] == h[1], same_as_asset, h3 != h[0]))
    if h[0] != h[1]:
        raise SystemExit("FAIL: two renders of the same parameters differ")
    if h3 == h[0]:
        raise SystemExit("FAIL: changing the airframe slot did not change the picture")
    print("[determinism] PASS")


if __name__ == "__main__":
    main()
