#!/usr/bin/env python3
"""Pixel contrast of named controls in the lever-window frames (TASK-0156, ui-art-director round 2, finding 1).

Reads the real 1920x1080 PNG frames, samples the ink (the pixel farthest in luminance from the background) and the background (median of the box)
inside each named control and prints the WCAG ratio. Text needs >= 4.5, icons >= 3.0. Exit 1 when any row fails.
Usage: python3 -I tools/ui/frame_contrast.py [FRAME_DIR]
"""
import sys
from statistics import median
from PIL import Image

DIR = sys.argv[1] if len(sys.argv) > 1 else "/var/www/gameDev/shots-out/TASK-0156-r2"
SUFFIX = "_1920x1080.png"

# (frame, control, kind, (x0, y0, x1, y1) inside the control, without its border); coordinates are canvas pixels at 100 % of the frames of the shot batch
BOXES = [
    ("lever_budget", "Сбросить (disabled)", "text", (1530, 936, 1596, 953)),
    ("lever_budget", "Применить (disabled)", "text", (1627, 936, 1725, 953)),
    ("lever_budget", "«+» (disabled)", "icon", (601, 264, 614, 279)),
    ("lever_budget", "▲ limit note", "icon", (183, 428, 197, 442)),
    ("lever_budget", "▲ reserve warning", "icon", (183, 670, 197, 684)),
    ("lever_market_buy", "Купить (disabled)", "text", (200, 467, 255, 485)),
    ("lever_market_buy", "Выставить (disabled)", "text", (203, 744, 278, 761)),
]

def lin(v):
    v /= 255
    return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4

def lum(c):
    r, g, b = (lin(x) for x in c[:3])
    return 0.2126 * r + 0.7152 * g + 0.0722 * b

def ratio(a, b):
    la, lb = lum(a), lum(b)
    return (max(la, lb) + 0.05) / (min(la, lb) + 0.05)

def measure(im, box):
    px = [im.getpixel((x, y)) for x in range(box[0], box[2]) for y in range(box[1], box[3])]
    bg = tuple(int(median(p[i] for p in px)) for i in range(3))
    ink = max(px, key=lambda p: abs(lum(p) - lum(bg)))
    return ink, bg, ratio(ink, bg)

def main():
    bad = 0
    print("| frame | control | ink | background | ratio | need |")
    print("|---|---|---|---|---|---|")
    for frame, name, kind, box in BOXES:
        im = Image.open(f"{DIR}/{frame}{SUFFIX}").convert("RGB")
        ink, bg, r = measure(im, box)
        need = 4.5 if kind == "text" else 3.0
        ok = r >= need
        bad += not ok
        print(f"| {frame} | {name} | #{ink[0]:02X}{ink[1]:02X}{ink[2]:02X} | #{bg[0]:02X}{bg[1]:02X}{bg[2]:02X} | {r:.2f} {'ok' if ok else 'FAIL'} | {need} |")
    return 1 if bad else 0

sys.exit(main())
