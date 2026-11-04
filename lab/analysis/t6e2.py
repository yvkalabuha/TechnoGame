#!/usr/bin/env python3
"""EXP-0004 (T6e2) analysis: capability deficit D, validity V-0, criteria K1, K2, K4, K5, selection rule, prints M1-M6
(pre-registration docs/simulation/exp-t6e2-tooling-target.md r1; deviations O1-O14 in
development/experiments/EXP-0004-t6e2-tooling-target.md). Reads lab/results/t6e2/out, prints a report.
Usage: python3 lab/analysis/t6e2.py [out_dir] [manifest]"""
import csv
import json
import math
import os
import random
import sys
from collections import defaultdict
from fractions import Fraction

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, 'lab/results/t6e2/out')
MAN = json.load(open(sys.argv[2] if len(sys.argv) > 2 else os.path.join(ROOT, 'lab/manifests/EXP-0004-t6e2.json')))
AN = MAN['analysis']
ARMS = list(MAN['arms'].keys())
CAND = ['V1', 'V3', 'V4']
GREAT = ['USA', 'GBR']
ACTORS = ['USA', 'GBR', 'SUN', 'FRA', 'SWE']
CONFIGS = list(MAN['configs'].keys())
FACT = ['allP1', 'allP2', 'allP3']
SEEDS = {c: MAN['configs'][c]['seeds'] for c in CONFIGS}
CARMS = {c: MAN['configs'][c].get('arms', ARMS) for c in CONFIGS}
MONTHS = MAN['months']
WIN = AN['window_months']
NW = MONTHS // WIN
S = 1e6
INF = float('inf')
NAN = float('nan')
TAB = AN['t_ab']
YPROD = int(round(AN['y_prod'] * S))
REPS = AN['bootstrap_reps']


def med(xs):
    xs = sorted(xs)
    n = len(xs)
    if n == 0:
        return NAN
    if n % 2:
        return xs[n // 2]
    a, b = xs[n // 2 - 1], xs[n // 2]
    return INF if (math.isinf(a) or math.isinf(b)) else (a + b) / 2


def lower_med(xs):
    xs = sorted(xs)
    return xs[(len(xs) - 1) // 2] if xs else NAN


def pct(xs, q):
    xs = sorted(xs)
    if not xs:
        return NAN
    k = (len(xs) - 1) * q
    lo = math.floor(k)
    hi = min(lo + 1, len(xs) - 1)
    if xs[lo] == xs[hi] or math.isinf(xs[lo]) or math.isinf(xs[hi]):
        return xs[lo] if k - lo < 0.5 else xs[hi]
    return xs[lo] + (xs[hi] - xs[lo]) * (k - lo)


def f(x, n=3):
    if x is None:
        return '—'
    if isinstance(x, Fraction):
        x = float(x)
    if isinstance(x, float) and math.isinf(x):
        return '+∞' if x > 0 else '−∞'
    if isinstance(x, float) and math.isnan(x):
        return 'nan'
    return f'{x:.{n}f}'


def rd(cfg, arm, seed):
    return os.path.join(OUT, cfg, arm, f'seed{seed}')


def g_fx(gap):
    """IndustryMath.G in float (print weight only)."""
    if gap < -1:
        return 0.0
    if gap < 0:
        return (1 + gap) ** 2
    return 1.0


# ---- static process table
REQ = {}
with open(os.path.join(rd('main', 'V0', SEEDS['main'][0]), 'req.csv')) as fh:
    for row in csv.DictReader(fh):
        REQ[int(row['p'])] = (row['c'], [int(row['r0']), int(row['r1']), int(row['r2'])], int(row['tier']))


def load_elig(cfg, arm, seed):
    e = defaultdict(set)
    with open(os.path.join(rd(cfg, arm, seed), 'elig.csv')) as fh:
        for row in csv.DictReader(fh):
            e[(int(row['t']), row['actor'])].add(int(row['p']))
    return e


def rmax_of(procs):
    """(c, d) -> max r_d over the processes with r_d > 0."""
    m = {}
    for p in procs:
        c, r, _ = REQ[p]
        for d in range(3):
            if r[d] > 0 and r[d] > m.get((c, d), 0):
                m[(c, d)] = r[d]
    return m


def deficit(cfg, arm, seed, estar):
    """D, D_in (E* ∩ E(arm)), D weighted by 1 − g_d, per actor (O4, O11)."""
    e_arm = load_elig(cfg, arm, seed)
    cap = defaultdict(dict)
    with open(os.path.join(rd(cfg, arm, seed), 'home.csv')) as fh:
        for row in csv.DictReader(fh):
            cap[(int(row['t']), row['actor'])][(row['c'], int(row['d']))] = (min(int(row['H']), int(row['M'])), int(row['H']))
    D = defaultdict(int)
    Din = defaultdict(int)
    Dw = defaultdict(float)
    DH = defaultdict(int)   # print only: the part of D that culture alone gives, Σ max(0, r − H)
    for t in range(MONTHS):
        for a in ACTORS:
            ps = estar.get((t, a), set())
            if not ps:
                continue
            rm = rmax_of(ps)
            rin = rmax_of(ps & e_arm.get((t, a), set()))
            cc = cap[(t, a)]
            for key, r in rm.items():
                c, hh = cc[key]
                if r > hh:
                    DH[a] += r - hh
                if r > c:
                    D[a] += r - c
                    Dw[a] += 1 - g_fx((c - r) / S)
                ri = rin.get(key, 0)
                if ri > c:
                    Din[a] += ri - c
    return D, Din, Dw, DH


def load_acts(cfg, arm, seed):
    acts = defaultdict(list)
    with open(os.path.join(rd(cfg, arm, seed), 'actors.csv')) as fh:
        for row in csv.DictReader(fh):
            acts[row['actor']].append({k: (row[k] if k in ('actor', 'profile') else int(row[k])) for k in row})
    return acts


def load_procs(cfg, arm, seed):
    procs = {}
    with open(os.path.join(rd(cfg, arm, seed), 'procs.csv')) as fh:
        for row in csv.DictReader(fh):
            procs[(row['actor'], int(row['p']))] = {k: int(row[k]) for k in ('tier', 'mastered_t0', 'first_mastered', 't_first', 't_07')}
    return procs


def load_cells(cfg, arm, seed):
    cells = defaultdict(list)
    with open(os.path.join(rd(cfg, arm, seed), 'cells.csv')) as fh:
        for row in csv.DictReader(fh):
            if row['actor'] not in GREAT:
                continue
            cells[(row['actor'], row['c'], row['d'])].append((int(row['t']), int(row['pool']), int(row['p2']), int(row['notional']),
                                                             int(row['gotE']), int(row['lab'])))
    return cells


def load_hend(cfg, arm, seed):
    h = {}
    with open(os.path.join(rd(cfg, arm, seed), 'hend.csv')) as fh:
        for row in csv.DictReader(fh):
            h[(row['actor'], row['c'], row['d'])] = (int(row['H0']), int(row['Hend']))
    return h


def load_lines(cfg, arm, seed):
    lines = defaultdict(list)
    with open(os.path.join(rd(cfg, arm, seed), 'lines.csv')) as fh:
        for row in csv.DictReader(fh):
            lines[(row['actor'], row['design'], row['gen'])].append((int(row['t']), int(row['rate']), int(row['unitcost']), int(row['minyield'])))
    return lines


def load_lines2(cfg, arm, seed):
    rows = []
    with open(os.path.join(rd(cfg, arm, seed), 'lines2.csv')) as fh:
        for row in csv.DictReader(fh):
            rows.append(row)
    return rows


# ---- load everything
print('loading ...', file=sys.stderr)
RUN = {}
M = {}          # (cfg, arm, seed, actor) -> metrics
PROCS = {}
ACTS = {}
LINES = {}
L2 = {}
EVENTS = {}
for cfg in CONFIGS:
    for s in SEEDS[cfg]:
        estar = load_elig(cfg, 'V0', s)
        for k, v in load_elig(cfg, 'VINF', s).items():
            estar[k] |= v
        cells0 = load_cells(cfg, 'V0', s)
        cellsI = load_cells(cfg, 'VINF', s)
        pool = {a: {k for src in (cells0, cellsI) for k, rows in src.items() if k[0] == a and any(x[1] for x in rows)} for a in GREAT}
        for arm in CARMS[cfg]:
            RUN[(cfg, arm, s)] = json.load(open(os.path.join(rd(cfg, arm, s), 'run.json')))
            D, Din, Dw, DH = deficit(cfg, arm, s, estar)
            acts = load_acts(cfg, arm, s)
            ACTS[(cfg, arm, s)] = acts
            PROCS[(cfg, arm, s)] = load_procs(cfg, arm, s)
            if cfg in ('main', 'admit'):
                LINES[(cfg, arm, s)] = load_lines(cfg, arm, s)
            L2[(cfg, arm, s)] = load_lines2(cfg, arm, s)
            with open(os.path.join(rd(cfg, arm, s), 'events.csv')) as fh:
                EVENTS[(cfg, arm, s)] = list(csv.DictReader(fh))
            cells = cells0 if arm == 'V0' else cellsI if arm == 'VINF' else load_cells(cfg, arm, s)
            hend = load_hend(cfg, arm, s)
            for a in ACTORS:
                ac = acts[a]
                C = sum(x['notionalTotal'] if arm == 'VINF' else x['p2total'] for x in ac)
                m = {'D': D[a], 'Din': Din[a], 'Dw': Dw[a], 'DH': DH[a], 'C': C,
                     'out': [sum(x['output'] for x in ac[w * WIN:(w + 1) * WIN]) for w in range(NW)],
                     'p2w': [sum(x['notionalTotal'] if arm == 'VINF' else x['p2total'] for x in ac[w * WIN:(w + 1) * WIN]) for w in range(NW)],
                     'taw': [sum(x['ta'] for x in ac[w * WIN:(w + 1) * WIN]) for w in range(NW)],
                     'retw': [sum(x['restRet'] for x in ac[w * WIN:(w + 1) * WIN]) for w in range(NW)],
                     'poolw': [sum(x['rest'] + x['bcap'] for x in ac[w * WIN:(w + 1) * WIN]) for w in range(NW)],
                     'total': sum(x['total'] for x in ac), 'bcap': sum(x['bcap'] for x in ac)}
                if a in GREAT:
                    pl = pool[a]
                    m['dH'] = sum(hend[k][1] - hend[k][0] for k in pl) / S
                    pm = got = tool = sw = 0
                    for k in pl:
                        prev = None
                        for (t, p_, p2, nt, ge, lab) in cells.get(k, []):
                            if p_:
                                pm += 1
                                got += ge
                                tool += lab == 2
                                if prev is not None and prev[0] == t - 1 and {prev[1], lab} == {1, 2}:
                                    sw += 1
                                prev = (t, lab)
                            else:
                                prev = None
                    m['m1a'] = got / pm if pm else NAN
                    m['m1b'] = 100 * sw / pm if pm else NAN
                    m['m1c'] = tool / pm if pm else NAN
                M[(cfg, arm, s, a)] = m

bad = [k for k, v in RUN.items() if v['mismatch_gain'] or v['mismatch_rest'] or v['mismatch_mcap']]
print(f'# EXP-0004 T6e2 analysis\nruns {len(RUN)}, invalid (plan reconstruction mismatch) {len(bad)}, max lines per actor-month {max(v["max_lines"] for v in RUN.values())}')


# ---- bootstrap helpers (O7)
def boot(cfg, fn):
    """fn(list of seeds) -> value or None; returns (point, (lo, hi), n_undefined)."""
    seeds = SEEDS[cfg]
    point = fn(seeds)
    rnd = random.Random(AN['bootstrap_seed'])
    vals, und = [], 0
    for _ in range(REPS):
        smp = [seeds[rnd.randrange(len(seeds))] for _ in seeds]
        v = fn(smp)
        if v is None:
            und += 1
        else:
            vals.append(float(v))
    return point, (pct(vals, .025), pct(vals, .975)), und


def sdD(cfg, arm, a, seeds):
    return sum(M[(cfg, 'V0', s, a)]['D'] - M[(cfg, arm, s, a)]['D'] for s in seeds)


def sdC(cfg, arm, a, seeds):
    return sum(M[(cfg, arm, s, a)]['C'] - M[(cfg, 'V0', s, a)]['C'] for s in seeds)


def R_of(cfg, arm, a, seeds):
    den = sdD(cfg, 'VINF', a, seeds)
    return None if den <= 0 else Fraction(sdD(cfg, arm, a, seeds), den)


def pi_of(cfg, arm, a, seeds):
    dd = sdD(cfg, arm, a, seeds)
    if dd <= 0:
        return None
    dc = sdC(cfg, arm, a, seeds)
    return Fraction(0) if dc <= 0 else Fraction(dc, dd)


def pihat_of(cfg, arm, a, seeds):
    pv, pi = pi_of(cfg, arm, a, seeds), pi_of(cfg, 'VINF', a, seeds)
    if pv is None or pi is None or pi == 0:
        return None
    return pv / pi


# ---- D print
print('\n## D by actor (main; sum over seeds, units культура·месяц; p10/p50/p90 per seed; ΔD = D(V0) − D(V))')
for a in ACTORS:
    for arm in ARMS:
        ds = [M[('main', arm, s, a)]['D'] / S for s in SEEDS['main']]
        dd = [(M[('main', 'V0', s, a)]['D'] - M[('main', arm, s, a)]['D']) / S for s in SEEDS['main']]
        din = sum(M[('main', 'V0', s, a)]['Din'] - M[('main', arm, s, a)]['Din'] for s in SEEDS['main']) / S
        dw = sum(M[('main', 'V0', s, a)]['Dw'] - M[('main', arm, s, a)]['Dw'] for s in SEEDS['main'])
        mn = min(range(len(dd)), key=lambda i: dd[i])
        mx = max(range(len(dd)), key=lambda i: dd[i])
        print(f'{a} {arm}: ΣD {f(sum(ds), 1)} | D p10/50/90 {f(pct(ds, .1), 1)}/{f(med(ds), 1)}/{f(pct(ds, .9), 1)} | ΣΔD {f(sum(dd), 1)} '
              f'(in-arm-eligible {f(din, 1)}, rest {f(sum(dd) - din, 1)}) | ΔD p10/50/90 {f(pct(dd, .1), 1)}/{f(med(dd), 1)}/{f(pct(dd, .9), 1)} '
              f'min s{SEEDS["main"][mn]} {f(dd[mn], 1)} max s{SEEDS["main"][mx]} {f(dd[mx], 1)} | ΣΔD·(1−g) {f(dw, 2)}')

print('\nD decomposition (print): ΣD = Σ max(0, r − H) [culture] + rest [tooling M below H]; main, sums over seeds')
for a in GREAT + ['SUN']:
    print(f'  {a}: ' + ' | '.join(f'{arm} {f(sum(M[("main", arm, s, a)]["DH"] for s in SEEDS["main"]) / S, 1)} + '
                                 f'{f(sum(M[("main", arm, s, a)]["D"] - M[("main", arm, s, a)]["DH"] for s in SEEDS["main"]) / S, 1)}' for arm in ARMS))
print('weighted (1 − g) share of VINF improvement (print): ' + '; '.join(
    f'{v} {a} {f(sum(M[("main", "V0", s, a)]["Dw"] - M[("main", v, s, a)]["Dw"] for s in SEEDS["main"]) / sum(M[("main", "V0", s, a)]["Dw"] - M[("main", "VINF", s, a)]["Dw"] for s in SEEDS["main"]))}'
    for v in CAND for a in GREAT))

# ---- V-0
print('\n## V-0: ΣΔD(VINF) > 0 and CI lower > 0 (main); factorial and ADMIT printed')
V0OK = {}
for cfg in CONFIGS:
    for a in GREAT:
        p, ci, _ = boot(cfg, lambda ss, a=a, cfg=cfg: sdD(cfg, 'VINF', a, ss))
        le0 = sum(1 for s in SEEDS[cfg] if M[(cfg, 'V0', s, a)]['D'] - M[(cfg, 'VINF', s, a)]['D'] <= 0)
        ok = p > 0 and ci[0] > 0
        if cfg == 'main':
            V0OK[a] = ok
        print(f'{cfg} {a}: ΣΔD(VINF) {f(p / S, 1)} CI [{f(ci[0] / S, 1)}; {f(ci[1] / S, 1)}]; seeds ΔD(VINF) ≤ 0: {le0}/{len(SEEDS[cfg])} -> '
              f'{"pass" if ok else "FAIL"}')
JUDGED = [a for a in GREAT if V0OK[a]]
print(f'judged powers: {JUDGED or "none"}')

RES = {v: {} for v in CAND}
# ---- K1
print('\n## K1: R = ΣΔD(V)/ΣΔD(VINF) [CI95]; main point ≥ 0.75; factorial: fail only if CI upper < 0.75 (judged cells)')
for v in CAND + ['V0']:
    for cfg in ['main'] + FACT:
        for a in GREAT:
            p, ci, und = boot(cfg, lambda ss, a=a, cfg=cfg, v=v: R_of(cfg, v, a, ss))
            if cfg == 'main':
                judged = a in JUDGED
                ok = p is not None and p >= Fraction(3, 4)
            else:
                _, cid, _ = boot(cfg, lambda ss, a=a, cfg=cfg: sdD(cfg, 'VINF', a, ss))
                judged = a in JUDGED and cid[0] > 0
                ok = not (ci[1] < 0.75)
            edge = ci[0] < 0.75 <= ci[1] if not math.isnan(ci[0]) else False
            if v in RES and judged:
                RES[v].setdefault('K1', []).append((cfg, a, ok))
            print(f'{v} {cfg} {a}: R {f(p)} [{f(ci[0])}; {f(ci[1])}] undefined reps {und} -> '
                  f'{("ok" if ok else "FAIL") if judged else "not judged"}{" (на грани)" if edge and judged else ""}')

# ---- K2
print('\n## K2: π̂ = π(V)/π(VINF), π = ΣΔC/ΣΔD (main), threshold ≤ 1; π in budget units per культура·месяц')
PIH = {}
for v in CAND + ['V0', 'VINF']:
    for a in GREAT:
        pv = pi_of('main', v, a, SEEDS['main'])
        p, ci, und = boot('main', lambda ss, a=a, v=v: pihat_of('main', v, a, ss))
        ok = p is not None and p <= 1
        PIH[(v, a)] = p
        if v in RES and a in JUDGED:
            RES[v].setdefault('K2', []).append((a, ok))
        print(f'{v} {a}: ΣΔC {f(sdC("main", v, a, SEEDS["main"]) / S, 2)} π {f(pv, 3)} π̂ {f(p)} [{f(ci[0])}; {f(ci[1])}] undefined reps {und}'
              + (f' -> {"ok" if ok else "FAIL"}' if v in RES and a in JUDGED else ''))
print('factorial (print): π̂ ' + '; '.join(f'{cfg} {v} {a} {f(pihat_of(cfg, v, a, SEEDS[cfg]))}' for cfg in FACT for v in CAND for a in GREAT))

# ---- K4
print('\n## K4: b_w = r_w(V)/r_w(VINF), seeds with VINF out USA, GBR > 0; judged if ≥ 10; median in [0.67; 1.5]')
lo, hi = AN['k4_band']
k4_on = len(JUDGED) == 2
for v in CAND + ['V0']:
    parts, allok = [], True
    for w in range(NW):
        bs = []
        for s in SEEDS['main']:
            oi = (M[('main', 'VINF', s, 'USA')]['out'][w], M[('main', 'VINF', s, 'GBR')]['out'][w])
            if min(oi) <= 0:
                continue
            ou, og = M[('main', v, s, 'USA')]['out'][w], M[('main', v, s, 'GBR')]['out'][w]
            if og == 0:
                bs.append(INF)
            elif ou == 0:
                bs.append(0.0)
            else:
                bs.append((ou / og) / (oi[0] / oi[1]))
        n = len(bs)
        judged = n >= AN['k4_min_judged_seeds']
        m = med(bs)
        ok = lo <= m <= hi if judged else True
        if judged and k4_on:
            allok &= ok
        parts.append(f'w{w}: {f(m, 2)} n{n}{"" if judged else " (not judged)"}{"" if ok else " FAIL"}')
    if v in RES:
        RES[v]['K4'] = allok if k4_on else None
    print(f'{v}: ' + ' | '.join(parts) + ('' if k4_on else ' [K4 not judged: a power failed V-0]'))
print('per actor out(V)/out(VINF) by window, ratio of sums over seeds (main):')
for v in CAND + ['V0']:
    for a in GREAT:
        print(f'  {v} {a}: ' + ' '.join(f(sum(M[("main", v, s, a)]["out"][w] for s in SEEDS["main"]) / max(1, sum(M[("main", "VINF", s, a)]["out"][w] for s in SEEDS["main"])), 2) for w in range(NW)))
print('free budget of VINF (main, sums over seeds, per window; units of budget): pass-2 money (V: from pool; VINF: notional, not from pool) | T_a | unspent rest')
for arm in ARMS:
    for a in GREAT:
        ser = lambda key: ' '.join(f(sum(M[('main', arm, s, a)][key][w] for s in SEEDS['main']) / S, 0) for w in range(NW))
        print(f'  {arm} {a}: p2 {ser("p2w")} | T_a {ser("taw")} | rest returned {ser("retw")}')

# ---- K5
print('\n## K5: pairs (s, a, p) tier ≥ 4 not mastered at t0, mastered in VINF; T = Σ min(t, 384); R5 = (T(V0)−T(V))/(T(V0)−T(VINF)) ≥ 0.75')
excl = [a for a in GREAT if a not in JUDGED]
pairs = []
for s in SEEDS['main']:
    pi_ = PROCS[('main', 'VINF', s)]
    for (a, p), x in pi_.items():
        if a in excl or x['tier'] < 4 or x['mastered_t0'] or x['first_mastered'] < 0:
            continue
        pairs.append((s, a, p))


def T_of(arm):
    return sum(min(384, PROCS[('main', arm, s)][(a, p)]['first_mastered']) if PROCS[('main', arm, s)][(a, p)]['first_mastered'] >= 0 else 384
               for (s, a, p) in pairs)


T0, TI = T_of('V0'), T_of('VINF')
print(f'pairs {len(pairs)} (by actor: ' + ', '.join(f'{a} {sum(1 for x in pairs if x[1] == a)}' for a in ACTORS) + f'); T(V0) {T0}, T(VINF) {TI}')
for v in CAND:
    if T0 - TI <= 0:
        RES[v]['K5'] = None
        print(f'{v}: not judged (T(V0) − T(VINF) ≤ 0)')
        continue
    r5 = Fraction(T0 - T_of(v), T0 - TI)
    RES[v]['K5'] = r5 >= Fraction(3, 4)
    print(f'{v}: T {T_of(v)} R5 {f(r5)} -> {"ok" if RES[v]["K5"] else "FAIL"}')

# ---- verdict and selection
print('\n## Verdict and selection')
fit = []
for v in CAND:
    r = RES[v]
    k1 = all(x[2] for x in r.get('K1', []))
    k2 = all(x[1] for x in r.get('K2', []))
    k4 = r['K4'] in (True, None)
    k5 = r['K5'] in (True, None)
    ok = bool(JUDGED) and k1 and k2 and k4 and k5
    ph = [PIH[(v, a)] for a in JUDGED]
    score = None if any(x is None for x in ph) or not ph else sum(float(x) for x in ph) / len(ph)
    print(f'{v}: K1 {"+" if k1 else "−"} K2 {"+" if k2 else "−"} K4 {"+" if r["K4"] else ("n/j" if r["K4"] is None else "−")} '
          f'K5 {"+" if r["K5"] else ("n/j" if r["K5"] is None else "−")} -> {"ГОДЕН" if ok else "не годен"}; mean π̂ {f(score)}')
    if ok:
        fit.append((score, v))
if not JUDGED:
    print('SELECTION: V-0 failed for both powers -> inconclusive, A39 stays V0')
elif not fit:
    print('SELECTION: no fit candidate -> A39 stays V0 (refutes)')
else:
    fit.sort()
    best = fit[0][0]
    close = [x for x in fit if x[0] <= best * (1 + AN['selection_tie_rel'])]
    close.sort(key=lambda x: (AN['simplicity_S'][x[1]], AN['new_literals'][x[1]], CAND.index(x[1])))
    print(f'SELECTION: fit {[(v, round(c, 4)) for c, v in fit]}; within 5% of min: {[v for _, v in close]}; selected {close[0][1]}')

# ---- M1 (EXP-0003 definitions, print)
print('\n## M1 (main; p50 over seeds; USA / GBR): TOOLING share | E>0 share | switches/100 | ΔH pool (EXP-0003) | C pass 2')
for arm in ARMS:
    q = lambda k, a: f(med([M[('main', arm, s, a)][k] for s in SEEDS['main']]), 3)
    print(f'{arm}: ' + ' | '.join(f'{q("m1c", a)}/{q("m1a", a)}/{q("m1b", a)}/{q("dH", a)}/{f(med([M[("main", arm, s, a)]["C"] / S for s in SEEDS["main"]]), 2)}' for a in GREAT))
print('v0.13 thresholds (print): M1a p50 ≥ 0.9 and p50 (ΣTotal(V) − ΣTotal(V0))/ΣB_cap ≤ 0.02')
for v in CAND:
    parts = []
    for a in GREAT:
        m1a = med([M[('main', v, s, a)]['m1a'] for s in SEEDS['main']])
        inc = med([(M[('main', v, s, a)]['total'] - M[('main', 'V0', s, a)]['total']) / M[('main', v, s, a)]['bcap'] for s in SEEDS['main']])
        parts.append(f'{a} M1a {f(m1a)} inc {f(inc, 4)}')
    print(f'  {v}: ' + '; '.join(parts))
print('EXP-0003-style K1 by ΔH (print): p50 over seeds of ΔH(V)/ΔH(VINF) and seeds with ΔH(VINF) ≤ 0')
for v in CAND:
    for a in GREAT:
        rs = [M[('main', v, s, a)]['dH'] / M[('main', 'VINF', s, a)]['dH'] for s in SEEDS['main'] if M[('main', 'VINF', s, a)]['dH'] != 0]
        print(f'  {v} {a}: {f(med(rs))}; ΔH(VINF) ≤ 0 in {sum(1 for s in SEEDS["main"] if M[("main", "VINF", s, a)]["dH"] <= 0)}/20')

# ---- M2, M3
print('\n## M2: output by window, p50 over seeds (main), units summed over the window')
for arm in ARMS:
    print(f'{arm}: ' + ' || '.join(f'{a} ' + ' '.join(f(med([M[("main", arm, s, a)]["out"][w] / S for s in SEEDS["main"]]), 0) for w in range(NW)) for a in ['USA', 'GBR', 'SUN']))
print('\n## M3: first month G(a) ≥ g (p50, censor 384); first mastered tier ≥ 4 (p50 over pairs); G_front (main)')
for arm in ARMS:
    parts = []
    for a in ['USA', 'GBR', 'SUN']:
        for g in (1, 2, 3):
            xs = [next((x['t'] for x in ACTS[('main', arm, s)][a] if x['G'] >= g), 384) for s in SEEDS['main']]
            parts.append(f'{a} G≥{g} {f(med(xs), 0)}')
    gf = [sum(1 for x in ACTS[('main', arm, s)]['USA'] if x['Gfront'] >= 2) for s in SEEDS['main']]
    mx = [max(x['Gfront'] for x in ACTS[('main', arm, s)]['USA']) for s in SEEDS['main']]
    fm = [min(384, PROCS[('main', arm, s)][(a, p)]['first_mastered']) if PROCS[('main', arm, s)][(a, p)]['first_mastered'] >= 0 else 384 for (s, a, p) in pairs]
    parts.append(f'G_front≥2 months p50 {f(med(gf), 0)}; maxG_front p50 {f(med(mx), 0)} max {max(mx)}; K5 pairs first mastered p50 {f(med(fm), 0)}')
    print(f'{arm}: ' + '; '.join(parts))


# ---- M4 anchors (EXP-0003 definitions O8, O9)
def c1_stats(cfg, arm):
    dev, mature = [], []
    for s in SEEDS[cfg]:
        for key, rows in LINES[(cfg, arm, s)].items():
            q1 = rows[0][1]
            if q1 <= 0:
                continue
            cum, marks, k = 0, [(rows[0][0], rows[0][2], rows[0][3])], 1
            for (t, rate, uc, my) in rows:
                cum += rate
                while cum >= (2 ** k) * q1 and k <= 20:
                    marks.append((t, uc, my))
                    k += 1
            dls = [(i, math.log(marks[i - 1][1] / marks[i][1]), marks[i - 1][2]) for i in range(1, len(marks)) if marks[i - 1][1] > 0 and marks[i][1] > 0]
            d13 = [x[1] for x in dls if x[0] <= 3]
            if d13:
                dev.append(max(d13))
            mature += [x[1] for x in dls if x[2] >= 900000]
    return dev, mature


print('\n## M4 anchors (print; not calibration): C1 dev / mature p50; C2 tier 4 p50 months (pairs, no t_first); C4 USA G_front windows')
for arm in ARMS:
    dev, mat = c1_stats('main', arm)
    durs, nf, n = [], 0, 0
    for s in SEEDS['main']:
        for a in ACTORS:
            for p in AN['c2_star_prc']['4']:
                x = PROCS[('main', arm, s)][(a, p)]
                if x['mastered_t0']:
                    continue
                n += 1
                if x['t_first'] < 0:
                    nf += 1
                    continue
                durs.append(INF if x['t_07'] < 0 else max(0, x['t_07'] - x['t_first']))
    c4 = []
    for g, (a0, a1) in ((2, (16, 87)), (3, (100, 195)), (4, (256, 363))):
        hits = sum(1 for s in SEEDS['main'] if (lambda ft: ft is not None and a0 <= ft <= a1)(next((x['t'] for x in ACTS[('main', arm, s)]['USA'] if x['Gfront'] >= g), None)))
        c4.append(f'G≥{g} {hits}/20')
    print(f'{arm}: C1 dev {f(med(dev) if dev else NAN)} ({len(dev)}), mature {f(med(mat) if mat else NAN)} ({len(mat)}); '
          f'C2 t4 {f(lower_med(durs) if durs else NAN, 0)} (pairs {n}, no t_first {nf}); C4 {", ".join(c4)}')


# ---- M5 F17b print
def episodes(cfg, arm, s):
    """Coexistence episodes per actor (O12)."""
    by = defaultdict(lambda: defaultdict(list))
    for r in L2[(cfg, arm, s)]:
        if r['ymin'] == '':
            continue
        by[r['actor']][int(r['t'])].append(r)
    eps = []
    for a, months in by.items():
        cur = None
        for t in range(MONTHS):
            ls = months.get(t, [])
            if len(ls) == 2:
                x, y = ls
                gx, gy = int(x['gp']), int(y['gp'])
                dx, dy = int(x['design']), int(y['design'])
                n = x if (gx > gy or (gx == gy and dx > dy)) else y
                wv = y if n is x else x
                key = (wv['design'], n['design'])
                if cur is None or cur['key'] != key or cur['end'] != t - 1:
                    if cur is not None:
                        eps.append(cur)
                    cur = {'actor': a, 'key': key, 'start': t, 'end': t, 'rows': [], 'gpW': int(wv['gp']), 'gpN': int(n['gp'])}
                cur['end'] = t
                cur['rows'].append((t, wv, n))
            elif cur is not None:
                eps.append(cur)
                cur = None
        if cur is not None:
            eps.append(cur)
    for e in eps:
        # A close decided in compute t is applied in step t+1: the snapshot t+1 still shows two lines (EXP-0004 O12a).
        dec = None
        for (t, wv, n) in e['rows'][-2:]:
            if wv['close'] == '1' or n['close'] == '1':
                dec = (t, wv, n)
                break
        if dec is None:
            e['reason'] = 'horizon' if e['end'] == MONTHS - 1 else 'other'
            e['len'] = e['end'] - e['start'] + 1
            continue
        t, wv, n = dec
        if wv['close'] == '1' and int(n['ymin']) >= YPROD:
            e['reason'] = 'YPROD'
        elif n['close'] == '1' and any(r['start'] == '1' for r in L2_by_t(cfg, arm, s, e['actor'], t)):
            e['reason'] = 'REPLACE'
        elif n['close'] == '1' and int(n['idle']) >= TAB:
            e['reason'] = 'A40C_N'
        elif wv['close'] == '1' and int(wv['idle']) >= TAB:
            e['reason'] = 'A40C_W'
        else:
            e['reason'] = 'other'
        e['len'] = t - e['start'] + 1
    return eps


_L2IDX = {}


def L2_by_t(cfg, arm, s, a, t):
    k = (cfg, arm, s)
    if k not in _L2IDX:
        idx = defaultdict(list)
        for r in L2[k]:
            idx[(r['actor'], int(r['t']))].append(r)
        _L2IDX[k] = idx
    return _L2IDX[k].get((a, t), [])


def gprod(r):
    gs = [int(r[k]) / S for k in ('g0', 'g1', 'g2') if r[k] != '']
    p = 1.0
    for g in gs:
        p *= g
    return p


print('\n## M5 F17b (YPROD configs): coexistences per (actor, seed), duration p50/p90/max (censored at 384), end reasons')
EPS = {}
for cfg in CONFIGS:
    for arm in CARMS[cfg]:
        for s in SEEDS[cfg]:
            EPS[(cfg, arm, s)] = episodes(cfg, arm, s)
for cfg in CONFIGS:
    for arm in CARMS[cfg]:
        allep = [e for s in SEEDS[cfg] for e in EPS[(cfg, arm, s)]]
        reasons = defaultdict(int)
        for e in allep:
            reasons[e['reason']] += 1
        lens = [e['len'] for e in allep]
        per = defaultdict(int)
        for e in allep:
            per[e['actor']] += 1
        share = []
        for e in allep:
            for (t, wv, n) in e['rows']:
                sw, sn = int(wv['rate']) * int(wv['unitcost']), int(n['rate']) * int(n['unitcost'])
                if sw + sn > 0:
                    share.append(sn / (sw + sn))
        print(f'{cfg} {arm}: episodes {len(allep)} by actor {dict(per)}; len p50 {f(med(lens), 0) if lens else "—"} p90 {f(pct(lens, .9), 0) if lens else "—"} '
              f'max {max(lens) if lens else "—"}; reasons {dict(reasons)}; N share of line money p50 {f(med(share), 2) if share else "—"}; '
              f'launches with g_p(X) ≤ g_p(W): {sum(1 for e in allep if e["gpN"] <= e["gpW"])}')

# branch C classes, L-C0, L-O0
print('\nA40 classes (all configs): branch С culture / coexistence money / other (L-C0 must be 0); branch О defects (L-O0 must be 0)')
for cfg in CONFIGS:
    for arm in CARMS[cfg]:
        cc = defaultdict(int)
        od = 0
        for s in SEEDS[cfg]:
            od += sum(1 for e in EVENTS[(cfg, arm, s)] if e['cls'] != 'Processes')
            hist = defaultdict(list)
            for r in sorted(L2[(cfg, arm, s)], key=lambda r: int(r['t'])):
                if r['ymin'] == '':
                    continue
                hist[(r['actor'], r['design'])].append(r)
            for (a, d), rows in hist.items():
                for i, r in enumerate(rows):
                    if r['close'] == '1' and int(r['idle']) >= TAB:
                        cult = len(rows[:i + 1]) >= TAB and all(x['cultural'] == '1' for x in rows[i + 1 - TAB:i + 1])
                        two = len(L2_by_t(cfg, arm, s, a, int(r['t']))) - sum(1 for x in L2_by_t(cfg, arm, s, a, int(r['t'])) if x['ymin'] == '') == 2
                        cc['culture' if cult else ('coexistence money' if two else 'other')] += 1
        print(f'  {cfg} {arm}: С {dict(cc)}; О defects {od}')

# reviewer diagnostic: y_min(N) = Π g_d · ramp, N output, GBR and SUN
print('\n## Reviewer diagnostic: coexistence episodes of GBR and SUN (main): y_min(N) start→end, p50 Π g_d, p50 ramp (arg-min process), N output mean')
for arm in ARMS:
    for a in ('GBR', 'SUN'):
        es = [(s, e) for s in SEEDS['main'] for e in EPS[('main', arm, s)] if e['actor'] == a]
        rows = []
        for s, e in es:
            ns = [n for (_, _, n) in e['rows']]
            gp = [gprod(n) for n in ns]
            rp = [int(n['ramp']) / S for n in ns if n['ramp'] != '']
            out = sum(int(n['rate']) for n in ns) / len(ns) / S
            rows.append(f's{s} t{e["start"]}+{e["len"]} g{e["gpW"]}→{e["gpN"]} y {f(int(ns[0]["ymin"]) / S, 2)}→{f(int(ns[-1]["ymin"]) / S, 2)} '
                        f'Πg {f(med(gp), 2)} ramp {f(med(rp), 2) if rp else "—"} p{ns[-1]["argp"]} outN {f(out, 2)} {e["reason"]}')
        print(f'{arm} {a}: {len(es)} episodes' + ('' if not rows else ':\n    ' + '\n    '.join(rows)))
print('\nmonthly trajectory (every 6th month) of the first episode of GBR and SUN, main V0 and VINF: t: y_min = Πg × ramp (process), out_N, out_W')
for arm in ('V0', 'VINF'):
    for a in ('GBR', 'SUN'):
        es = [(s, e) for s in SEEDS['main'] for e in EPS[('main', arm, s)] if e['actor'] == a]
        if not es:
            print(f'{arm} {a}: none')
            continue
        s, e = es[0]
        pts = [f't{t}: {f(int(n["ymin"]) / S, 3)}={f(gprod(n), 3)}×{f(int(n["ramp"]) / S, 3) if n["ramp"] != "" else "—"} (p{n["argp"]}) '
               f'N {f(int(n["rate"]) / S, 2)} W {f(int(wv["rate"]) / S, 2)}' for i, (t, wv, n) in enumerate(e['rows']) if i % 6 == 0 or i == len(e['rows']) - 1]
        print(f'{arm} {a} seed {s} episode t{e["start"]}..{e["end"]} ({e["reason"]}):\n    ' + '\n    '.join(pts))

# ---- M6 ADMIT diagnostic
print('\n## M6: ΔD(VINF) under YPROD (main) and ADMIT; seeds with ΔD(VINF) ≤ 0; ΔH pool USA p50 (EXP-0003 metric)')
for cfg in ('main', 'admit'):
    for a in GREAT:
        dd = [M[(cfg, 'V0', s, a)]['D'] - M[(cfg, 'VINF', s, a)]['D'] for s in SEEDS[cfg]]
        print(f'{cfg} {a}: ΣΔD(VINF) {f(sum(dd) / S, 1)}; ≤ 0 in {sum(1 for x in dd if x <= 0)}/20; ΔH V0 p50 {f(med([M[(cfg, "V0", s, a)]["dH"] for s in SEEDS[cfg]]))} '
              f'VINF p50 {f(med([M[(cfg, "VINF", s, a)]["dH"] for s in SEEDS[cfg]]))}; ΔH(VINF) ≤ 0 in {sum(1 for s in SEEDS[cfg] if M[(cfg, "VINF", s, a)]["dH"] <= 0)}/20; '
              f'D(V0) Σ {f(sum(M[(cfg, "V0", s, a)]["D"] for s in SEEDS[cfg]) / S, 1)}')

# ---- check: goal vs spec formula (main seeds 21-23, all participating cell-months)
print('\n## Check: pass-2 goal vs spec formula (main seeds 21-23, every participating cell-month)')
off = {k: int(round(v * S)) for k, v in AN['goal_offsets'].items()}
for arm in ARMS:
    n = bad_ = 0
    for s in SEEDS['main'][:3]:
        with open(os.path.join(rd('main', arm, s), 'cells.csv')) as fh:
            for row in csv.DictReader(fh):
                if row['part'] != '1':
                    continue
                h, mc, goal = int(row['H']), int(row['Mcap']), int(row['goal'])
                want = mc if arm == 'VINF' else min(h + off[arm], mc)
                n += 1
                bad_ += goal != want
    print(f'{arm}: participating cell-months {n}, mismatches {bad_}')

# ---- check: D by hand on one seed, three cells (independent path)
print('\n## Check: δ by hand, main seed 21 V0 USA, three cell-months')
s0 = SEEDS['main'][0]
el = defaultdict(set)
for arm in ('V0', 'VINF'):
    with open(os.path.join(rd('main', arm, s0), 'elig.csv')) as fh:
        for row in csv.DictReader(fh):
            if row['actor'] == 'USA':
                el[int(row['t'])].add(int(row['p']))
home = {}
with open(os.path.join(rd('main', 'V0', s0), 'home.csv')) as fh:
    for row in csv.DictReader(fh):
        if row['actor'] == 'USA':
            home[(int(row['t']), row['c'], int(row['d']))] = (int(row['H']), int(row['M']))
shown = 0
for (t, c, d), (h, m) in sorted(home.items()):
    if t not in (100, 250, 380) or shown >= 9:
        continue
    reqs = [(p, REQ[p][1][d]) for p in sorted(el[t]) if REQ[p][0] == c and REQ[p][1][d] > 0]
    if not reqs:
        continue
    rmax = max(r for _, r in reqs)
    delta = max(0, rmax - min(h, m))
    if delta > 0:
        print(f't{t} {c} d{d}: H {h / S} M {m / S} min {min(h, m) / S}; E* procs {reqs}; δ = max(0, {rmax / S} − {min(h, m) / S}) = {delta / S}')
        shown += 1
total = 0
for (t, c, d), (h, m) in home.items():
    rs = [REQ[p][1][d] for p in el[t] if REQ[p][0] == c and REQ[p][1][d] > 0]
    if rs:
        total += max(0, max(rs) - min(h, m))
print(f'independent ΣD(seed {s0}, V0, USA) = {total}; main path = {M[("main", "V0", s0, "USA")]["D"]} -> {"match" if total == M[("main", "V0", s0, "USA")]["D"] else "MISMATCH"}')
