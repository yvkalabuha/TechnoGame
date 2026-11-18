#!/usr/bin/env python3
"""Contrast check for TASK-0101 instrument layers. Same maths as src/Singularity.Client.Spike/ContrastValidator.cs
(sRGB -> linear -> CIE Lab D65, CIEDE2000, OKLCH chroma, WCAG ratio). Thresholds are [Предл.] from UI_INSTRUMENTS.md section 5.
Run: python3 -I contrast_check.py > contrast_result.txt"""
import math

def h2l(h):
    h = h.lstrip('#')
    out = []
    for i in (0, 2, 4):
        c = int(h[i:i + 2], 16) / 255
        out.append(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4)
    return out

def scale(l, k):
    return [min(1.0, x * k) for x in l]

def xyz(l):
    r, g, b = l
    return (0.4124564 * r + 0.3575761 * g + 0.1804375 * b, 0.2126729 * r + 0.7151522 * g + 0.0721750 * b, 0.0193339 * r + 0.1191920 * g + 0.9503041 * b)

def lab(l):
    X, Y, Z = xyz(l)
    wp = (0.95047, 1.0, 1.08883)
    f = lambda t: t ** (1 / 3) if t > 216 / 24389 else (24389 / 27 * t + 16) / 116
    fx, fy, fz = f(X / wp[0]), f(Y / wp[1]), f(Z / wp[2])
    return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz))

def de2000(a, b):
    L1, a1, b1 = a; L2, a2, b2 = b
    C1, C2 = math.hypot(a1, b1), math.hypot(a2, b2)
    Cm = (C1 + C2) / 2
    G = 0.5 * (1 - math.sqrt(Cm ** 7 / (Cm ** 7 + 25 ** 7)))
    a1p, a2p = (1 + G) * a1, (1 + G) * a2
    C1p, C2p = math.hypot(a1p, b1), math.hypot(a2p, b2)
    h1 = math.degrees(math.atan2(b1, a1p)) % 360
    h2 = math.degrees(math.atan2(b2, a2p)) % 360
    dL, dC = L2 - L1, C2p - C1p
    if C1p * C2p == 0: dh = 0
    else:
        dh = h2 - h1
        if dh > 180: dh -= 360
        elif dh < -180: dh += 360
    dH = 2 * math.sqrt(C1p * C2p) * math.sin(math.radians(dh / 2))
    Lm, Cmp = (L1 + L2) / 2, (C1p + C2p) / 2
    if C1p * C2p == 0: hm = h1 + h2
    elif abs(h1 - h2) <= 180: hm = (h1 + h2) / 2
    else: hm = (h1 + h2 + 360) / 2 if h1 + h2 < 360 else (h1 + h2 - 360) / 2
    T = 1 - 0.17 * math.cos(math.radians(hm - 30)) + 0.24 * math.cos(math.radians(2 * hm)) + 0.32 * math.cos(math.radians(3 * hm + 6)) - 0.20 * math.cos(math.radians(4 * hm - 63))
    dt = 30 * math.exp(-(((hm - 275) / 25) ** 2))
    Rc = 2 * math.sqrt(Cmp ** 7 / (Cmp ** 7 + 25 ** 7))
    Sl = 1 + 0.015 * (Lm - 50) ** 2 / math.sqrt(20 + (Lm - 50) ** 2)
    Sc, Sh = 1 + 0.045 * Cmp, 1 + 0.015 * Cmp * T
    Rt = -math.sin(math.radians(2 * dt)) * Rc
    return math.sqrt((dL / Sl) ** 2 + (dC / Sc) ** 2 + (dH / Sh) ** 2 + Rt * (dC / Sc) * (dH / Sh))

def lum(l): return xyz(l)[1]
def wcag(a, b):
    la, lb = lum(a), lum(b)
    if la < lb: la, lb = lb, la
    return (la + 0.05) / (lb + 0.05)

def oklch_c(l):
    r, g, b = l
    L_ = 0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b
    M_ = 0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b
    S_ = 0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b
    l_, m_, s_ = L_ ** (1 / 3), M_ ** (1 / 3), S_ ** (1 / 3)
    a = 1.9779984951 * l_ - 2.4285922050 * m_ + 0.4505937099 * s_
    b2 = 0.0259040371 * l_ + 0.7827717662 * m_ - 0.8086757660 * s_
    return math.hypot(a, b2)

# Palette C tokens (src/Singularity.Client.Spike/Palette.cs) and the desk of client-v05c.html
P = dict(ink='1D1E20', ink2='55575A', ink3='64625C', paper='F3EEDF', sheet='FBF8EF', rule='BDB59F', desk='D9D3C0')
INK = ['sand', 'teal', 'violet', 'olive', 'plum', 'slate']
LINE = ['8F7A2E', '168A92', '7A4AA8', '4F8A1E', 'A03E82', '4A7088']
WASH = ['EAD9A3', 'BFE6E5', 'D8CCEE', 'CFE6AA', 'F0C9E0', 'C3D8EA']
TEXT = ['635420', '1F5F63', '53407A', '4A6020', '75335F', '3F5666']
MOSS = ('3A8A5C', 'C6E6D2', '2A5E3F')
# Layer 1 candidates (new tokens, proposed): stroke and hatch, muted
L1 = dict(desk_line='B8B094', desk_hatch='C6BEA4')
L1_MAX_C = 0.06          # OKLCH chroma ceiling of layer 1 [Предл.]
L1_DE = (6.0, 14.0)      # dE2000 line vs desk: visible but quiet [Предл.]
L2_GFX = 3.0             # WCAG ratio for graphic parts of controls (WCAG 1.4.11)
KS = (0.94, 0.955, 1.0, 1.02)

res = []
def rec(name, ok, detail): res.append((name, ok, detail))

# L1: line against the desk, desk shaded by the texture multiplier (desk takes the same paper texture, T1..T6)
for key in ('desk_line', 'desk_hatch'):
    worst = []
    for k in KS:
        d = de2000(lab(scale(h2l(P['desk']), k)), lab(scale(h2l(L1[key]), k)))
        worst.append(d)
    lo, hi = min(worst), max(worst)
    lo_ref = de2000(lab(h2l(P['desk'])), lab(h2l(L1[key])))
    # both surfaces get the same multiplier, so the line-vs-desk dE is also evaluated with desk only shaded (texture under a drawn line)
    cross = [de2000(lab(scale(h2l(P['desk']), k)), lab(h2l(L1[key]))) for k in KS]
    lo = min(lo, min(cross)); hi = max(hi, max(cross))
    rec(f'L1 {key} vs desk dE00 in [{L1_DE[0]:.0f},{L1_DE[1]:.0f}]', L1_DE[0] <= lo and hi <= L1_DE[1] if key == 'desk_line' else lo >= 3.0 and hi <= L1_DE[1],
        f'min {lo:.1f} max {hi:.1f} (nominal {lo_ref:.1f})' + ('' if key == 'desk_line' else ' (hatch: floor 3)'))
    c = oklch_c(h2l(L1[key]))
    rec(f'L1 {key} OKLCH chroma <= {L1_MAX_C}', c <= L1_MAX_C, f'C {c:.3f}')
# L1 must stay quieter than the quietest functional text: WCAG(line, desk) well below 3 (cannot be mistaken for a control)
w = wcag(h2l(L1['desk_line']), h2l(P['desk']))
rec('L1 line vs desk WCAG < 1.6 (never reads as a control)', w < 1.6, f'{w:.2f}')
# The only text on the desk is the era caption: ink
r = wcag(scale(h2l(P['desk']), 0.94), h2l(P['ink']))
rec('ink caption on desk x0.94 WCAG >= 4.5', r >= 4.5, f'{r:.2f}')
# L2: neutral instrument parts on sheet (worst k)
for t, nm, need in ((P['ink'], 'ink strokes', 7.0), (P['ink2'], 'ink-2 scale ticks/labels', 4.5), (P['ink3'], 'ink-3 minor ticks (decor, caption >= 4.5)', 4.5)):
    r = min(wcag(scale(h2l(bg), k), h2l(t)) for bg in (P['sheet'], P['paper']) for k in (0.94, 1.0))
    rec(f'L2 {nm} on sheet/paper WCAG >= {need}', r >= need, f'{r:.2f}')
# L2: ink lines (gauge cells, flask outlines, caliper jaw) as graphic parts >= 3:1 against sheet at k 0.955
for i, n in enumerate(INK):
    r = min(wcag(scale(h2l(P['sheet']), k), h2l(LINE[i])) for k in (0.955, 1.0))
    rec(f'L2 line.{n} on sheet WCAG >= {L2_GFX}', r >= L2_GFX, f'{r:.2f}')
r = min(wcag(scale(h2l(P['sheet']), k), h2l(MOSS[0])) for k in (0.955, 1.0))
rec(f'L2 line.moss on sheet WCAG >= {L2_GFX}', r >= L2_GFX, f'{r:.2f}')
# L2: liquid (wash) against the sheet: dE00 >= 6 (decor fill; the level is carried by the ink meniscus line and the number), ink on wash >= 7
for i, n in enumerate(INK):
    d = min(de2000(lab(scale(h2l(P['sheet']), k)), lab(scale(h2l(WASH[i]), k))) for k in (0.955, 1.0, 1.02))
    rec(f'L2 wash.{n} vs sheet dE00 >= 6', d >= 6, f'{d:.1f}')
    r = min(wcag(scale(h2l(WASH[i]), k), h2l(P['ink'])) for k in (0.94, 1.0))
    rec(f'L2 ink on wash.{n} WCAG >= 7', r >= 7, f'{r:.2f}')
    c = oklch_c(h2l(WASH[i])); rec(f'L2 wash.{n} OKLCH chroma <= 0.13', c <= 0.13, f'C {c:.3f}')
    c = oklch_c(h2l(LINE[i])); rec(f'L2 line.{n} OKLCH chroma <= 0.16', c <= 0.16, f'C {c:.3f}')
# L2: number / label text colours on sheet
for i, n in enumerate(INK):
    r = min(wcag(scale(h2l(P['sheet']), k), h2l(TEXT[i])) for k in (0.94, 1.0))
    rec(f'L2 text.{n} on sheet x0.94 WCAG >= 4.5', r >= 4.5, f'{r:.2f}')
# L2 vs L1 separation: the same colour family must not be shared, a control must be unmistakable on the desk where it overhangs the panel
d = de2000(lab(h2l(L1['desk_line'])), lab(h2l(P['ink'])))
rec('L1 line vs L2 ink stroke dE00 >= 40 (two layers never confusable)', d >= 40, f'{d:.1f}')

fails = [r for r in res if not r[1]]
for n, ok, dt in res:
    print(('PASS ' if ok else 'FAIL ') + n + ' | ' + dt)
print(f'\nTOTAL {len(res)} pairs, PASS {len(res) - len(fails)}, FAIL {len(fails)}')
