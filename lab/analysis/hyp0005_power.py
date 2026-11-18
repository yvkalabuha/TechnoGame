"""HYP-0005 rev 3 power simulation (owner decisions #356-#359).

Model: per-seed leader trajectory with parameters
  c  - true chg30 (P(at least one change of the record leader, t0+12..t1)), #357 'ever'
  r  - return share: among changers, share whose end leader is L(t0) again (A->B->A); chg_end = c*(1-r)
  f  - flip share among changers: P(>3 changes | changed); true flip = c*f
  ne - true Neff_c of the end-leader distribution among end-changers (7 contenders, geometric weights)
All seeds share L(t0) = actor 0 (HYP-0005 prints that share; worst case for P2 is handled by Neff_c).

Decision (HYP-0005 rev 3, 'Правило исхода' + 'Способ проверки'):
  step 300, level 97.1% (z 2.18): refutes if P1 Wilson CI fully outside [0.15;0.55] or P3 Wilson lower > 0.15.
  else step 600 by point (#359): P1 in [0.20;0.50], P3 <= 0.10, P2 (if >= 30 end-changers) in [1.6;4.0].
  caveat = refutes at 600 where no failing metric is a CI-fail (P1/P3 Wilson, P2 bootstrap outside [1.3;5.0]).
Usage: python3 lab/analysis/hyp0005_power.py [reps] [out_csv] [sets: main,flip0.3,neff1.3,neff4.5,oc]
Stdlib only; deterministic (fixed RNG seed per cell).
"""
import csv
import math
import os
import random
import sys

Z971 = 2.18          # Pocock 0.0294 two-sided per look
Z95 = 1.96
N_ACT = 8            # actor 0 = L(t0); 1..7 contenders
P2_MIN = 30
BOOT = 400           # bootstrap resamples for the P2 CI (only used for caveat classification)


def wilson(k, n, z):
    p = k / n
    d = 1 + z * z / n
    c = (p + z * z / (2 * n)) / d
    h = z * math.sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / d
    return c - h, c + h


def neff_of(q, m=N_ACT - 1):
    w = [q ** i for i in range(m)]
    s = sum(w)
    return s * s / sum(x * x for x in w)


def weights_for(ne):
    lo, hi = 1e-9, 1.0
    for _ in range(80):
        mid = (lo + hi) / 2
        if neff_of(mid) < ne:
            lo = mid
        else:
            hi = mid
    w = [lo ** i for i in range(N_ACT - 1)]
    s = sum(w)
    return [x / s for x in w]


def trajectory(rng, c, r, f, cum):
    """Return list of record leaders: seq[0] = L(t0), seq[-1] = L(t1)."""
    if rng.random() >= c:
        return [0]
    if rng.random() < f:
        k = 4
        while rng.random() < 0.5:
            k += 1
    else:
        k = rng.choices((1, 2, 3), weights=(0.6, 0.25, 0.15))[0]
    ret = rng.random() < r
    if ret:
        final = 0
        k = max(k, 2)
    else:
        u = rng.random()
        final = 1 + next(i for i, x in enumerate(cum) if u <= x)
    seq = [0]
    for i in range(1, k):
        while True:
            x = rng.randrange(N_ACT)
            if x != seq[-1] and (i < k - 1 or x != final):
                break
        seq.append(x)
    seq.append(final)
    return seq


def summarize(seqs):
    n = len(seqs)
    chg = sum(1 for s in seqs if len(s) > 1)
    flip = sum(1 for s in seqs if len(s) - 1 > 3)
    ends = [s[-1] for s in seqs if s[-1] != s[0]]
    return n, chg, flip, ends


def neff(ends):
    cnt = {}
    for e in ends:
        cnt[e] = cnt.get(e, 0) + 1
    m = len(ends)
    return 1.0 / sum((v / m) ** 2 for v in cnt.values())


def p2_ci(rng, seqs):
    cats = [s[-1] if s[-1] != s[0] else -1 for s in seqs]
    vals = []
    for _ in range(BOOT):
        b = [x for x in rng.choices(cats, k=len(cats)) if x != -1]
        if b:
            vals.append(neff(b))
    vals.sort()
    lo = vals[int(0.0145 * len(vals))]
    hi = vals[min(len(vals) - 1, int(0.9855 * len(vals)))]
    return lo, hi


def run_cell(c, r, f, ne, reps, seed):
    rng = random.Random(seed)
    cum, acc = [], 0.0
    for x in weights_for(ne):
        acc += x
        cum.append(acc)
    cum[-1] = 1.0
    o = dict.fromkeys(['sup', 'ref', 'ref300', 'cav', 'p1lo', 'p1hi', 'p3', 'p2', 'p2na300', 'p2na600',
                       'sup_p2na', 'r400sup', 'p1lo_ci300', 'p1hi_ci300', 'p3_ci300'], 0)
    for _ in range(reps):
        seqs = [trajectory(rng, c, r, f, cum) for _ in range(600)]
        # step 300
        n, chg, flip, ends = summarize(seqs[:300])
        if len(ends) < P2_MIN:
            o['p2na300'] += 1
        lo1, hi1 = wilson(chg, n, Z971)
        lo3, _ = wilson(flip, n, Z971)
        f1lo, f1hi, f3 = hi1 < 0.15, lo1 > 0.55, lo3 > 0.15
        o['p1lo_ci300'] += f1lo
        o['p1hi_ci300'] += f1hi
        o['p3_ci300'] += f3
        # step 600 quantities (also needed for the non-closure path)
        n, chg, flip, ends = summarize(seqs)
        p2na = len(ends) < P2_MIN
        o['p2na600'] += p2na
        if f1lo or f1hi or f3:
            o['ref'] += 1
            o['ref300'] += 1
        else:
            p1, p3 = chg / n, flip / n
            c1lo, c1hi = p1 < 0.20, p1 > 0.50
            c3 = p3 > 0.10
            c2 = (not p2na) and not (1.6 <= neff(ends) <= 4.0)
            o['p1lo'] += c1lo
            o['p1hi'] += c1hi
            o['p3'] += c3
            o['p2'] += c2
            if c1lo or c1hi or c3 or c2:
                o['ref'] += 1
                lo1, hi1 = wilson(chg, n, Z971)
                lo3, _ = wilson(flip, n, Z971)
                hard = (c1lo and hi1 < 0.15) or (c1hi and lo1 > 0.55) or (c3 and lo3 > 0.15)
                if c2 and not hard:
                    blo, bhi = p2_ci(rng, seqs)
                    hard = bhi < 1.3 or blo > 5.0
                o['cav'] += not hard
            else:
                o['sup'] += 1
                o['sup_p2na'] += p2na
        # independent replica n = 400 (#43), point rule
        rep = [trajectory(rng, c, r, f, cum) for _ in range(400)]
        n4, chg4, flip4, ends4 = summarize(rep)
        ok = 0.20 <= chg4 / n4 <= 0.50 and flip4 / n4 <= 0.10
        if ok and len(ends4) >= P2_MIN:
            ok = 1.6 <= neff(ends4) <= 4.0
        o['r400sup'] += ok
    return {k: v / reps for k, v in o.items()}


def main():
    reps = int(sys.argv[1]) if len(sys.argv) > 1 else 2000
    root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(root, 'lab/results/hyp0005_power/power.csv')
    os.makedirs(os.path.dirname(out), exist_ok=True)
    cells = []
    for c in (0.10, 0.20, 0.30, 0.40, 0.50, 0.60):
        for r in (0.0, 0.2, 0.4):
            cells.append(('main', c, r, 0.1, 2.5))
    for c in (0.10, 0.20, 0.30, 0.40, 0.50, 0.60):
        cells.append(('flip0.3', c, 0.2, 0.3, 2.5))
        cells.append(('neff1.3', c, 0.2, 0.1, 1.3))
        cells.append(('neff4.5', c, 0.2, 0.1, 4.5))
    for c in (0.15, 0.18, 0.22, 0.25, 0.45, 0.48, 0.52, 0.55):
        cells.append(('oc', c, 0.2, 0.1, 2.5))
    want = sys.argv[3].split(',') if len(sys.argv) > 3 else None
    rows = []
    for i, (tag, c, r, f, ne) in enumerate(cells):
        if want and tag not in want:
            continue
        res = run_cell(c, r, f, ne, reps, 50000 + i)
        row = dict(set=tag, chg30=c, ret=r, flip_share=f, neff=ne, chg_end=round(c * (1 - r), 3), **res)
        rows.append(row)
        print(' '.join(f'{k}={v:.3f}' if isinstance(v, float) else f'{k}={v}' for k, v in row.items()), flush=True)
    with open(out, 'w', newline='') as fh:
        w = csv.DictWriter(fh, fieldnames=list(rows[0].keys()))
        w.writeheader()
        w.writerows(rows)


if __name__ == '__main__':
    main()
