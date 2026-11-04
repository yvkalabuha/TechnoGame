"""EXP-0006 (pool order) analysis: pass-2 pool allocation of the A39 tooling plan under V5 (CAN, REV, PROP, SEV) vs V0 and VINF.

Pre-registration: docs/simulation/exp-pool-order.md r1 (criteria П1–П5, Г1–Г3, selection rule); definitions of D, R, π̂, K1, K2, K4, K5 and
V-0 — docs/simulation/exp-t6e2-tooling-target.md r1 with О1–О7 of EXP-0005; clarifications decided before the run — EXP-0006 «Уточнения».
Usage: python3 lab/analysis/pool_order.py [out_dir] [manifest]
"""
import csv
import json
import math
import os
import random
import sys
from collections import defaultdict
from fractions import Fraction

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, 'lab/results/pool_order/out')
MAN = json.load(open(sys.argv[2] if len(sys.argv) > 2 else os.path.join(ROOT, 'lab/manifests/EXP-0006-pool-order.json')))
AN = MAN['analysis']
ARMS = list(MAN['arms'].keys())                     # V0, VINF, CAN, REV, PROP, SEV
V5ARMS = ['CAN', 'REV', 'PROP', 'SEV']
CAND = AN['candidates']                             # PROP, SEV
RULES = ['REV'] + CAND                              # judged by П1–П5; REV not selectable
GREAT = AN['judged']                                # USA, GBR
ACTORS = ['USA', 'GBR', 'SUN', 'FRA', 'SWE']
CONFIGS = list(MAN['configs'].keys())
FACT = ['allP1', 'allP2', 'allP3']
SETS = ['main', 'stress']
SEEDS = {c: MAN['configs'][c]['seeds'] for c in CONFIGS}
MONTHS = MAN['months']
WIN = AN['window_months']
NW = MONTHS // WIN
S = 1e6
INF = float('inf')
NAN = float('nan')
REPS = AN['bootstrap_reps']
CLUSTERS = ['MET', 'CHEM', 'MECH', 'AERO']


def med(xs):
    xs = sorted(xs)
    n = len(xs)
    if n == 0:
        return NAN
    if n % 2:
        return xs[n // 2]
    a, b = xs[n // 2 - 1], xs[n // 2]
    return INF if (math.isinf(a) or math.isinf(b)) else (a + b) / 2


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
    if gap < -1:
        return 0.0
    if gap < 0:
        return (1 + gap) ** 2
    return 1.0


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
    m = {}
    for p in procs:
        c, r, _ = REQ[p]
        for d in range(3):
            if r[d] > 0 and r[d] > m.get((c, d), 0):
                m[(c, d)] = r[d]
    return m


def deficit(cfg, arm, seed, estar):
    """D (T6e2 r1, O4 EXP-0004), culture part DH, weighted D_g (print), and the tooling part by cluster — per actor."""
    cap = defaultdict(dict)
    with open(os.path.join(rd(cfg, arm, seed), 'home.csv')) as fh:
        for row in csv.DictReader(fh):
            cap[(int(row['t']), row['actor'])][(row['c'], int(row['d']))] = (min(int(row['H']), int(row['M'])), int(row['H']))
    D, DH, Dw = defaultdict(int), defaultdict(int), defaultdict(float)
    Dtc = defaultdict(int)   # (actor, cluster) -> tooling part of D
    for t in range(MONTHS):
        for a in ACTORS:
            ps = estar.get((t, a), set())
            if not ps:
                continue
            cc = cap[(t, a)]
            for key, r in rmax_of(ps).items():
                c, hh = cc[key]
                culture = max(0, r - hh)
                if r > c:
                    D[a] += r - c
                    Dw[a] += 1 - g_fx((c - r) / S)
                    Dtc[(a, key[0])] += (r - c) - culture
                DH[a] += culture
    return D, DH, Dw, Dtc


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
            procs[(row['actor'], int(row['p']))] = {k: int(row[k]) for k in ('tier', 'mastered_t0', 'first_mastered')}
    return procs


def load_pool(cfg, arm, seed):
    """pool.csv (V5 arms): per (t, actor) exh, u by cluster, g1 mismatch, PROP remainder; participation months from cells.csv (part = 1)."""
    part = set()
    with open(os.path.join(rd(cfg, arm, seed), 'cells.csv')) as fh:
        for row in csv.DictReader(fh):
            if row['part'] == '1':
                part.add((int(row['t']), row['actor']))
    rows = {}
    with open(os.path.join(rd(cfg, arm, seed), 'pool.csv')) as fh:
        for row in csv.DictReader(fh):
            rows[(int(row['t']), row['actor'])] = row
    out = {}
    for a in ACTORS:
        pm = em = 0
        u = {c: 0 for c in CLUSTERS}
        for t in range(MONTHS):
            r = rows[(t, a)]
            for c in CLUSTERS:
                u[c] += int(r[f'u_{c}'])
            if (t, a) in part:
                pm += 1
                em += r['exh'] == '1'
        out[a] = {'pm': pm, 'em': em, 'u': u}
    g1 = sum(int(r['g1_mismatch']) for r in rows.values())
    rem = max(int(r['prop_rem']) for r in rows.values())
    return out, g1, rem


print('loading ...', file=sys.stderr)
RUN, M, PROCS, POOL = {}, {}, {}, {}
G1, REM = defaultdict(int), defaultdict(int)
for cfg in CONFIGS:
    for s in SEEDS[cfg]:
        estar = load_elig(cfg, 'V0', s)
        for k, v in load_elig(cfg, 'VINF', s).items():
            estar[k] |= v
        for arm in ARMS:
            RUN[(cfg, arm, s)] = json.load(open(os.path.join(rd(cfg, arm, s), 'run.json')))
            D, DH, Dw, Dtc = deficit(cfg, arm, s, estar)
            acts = load_acts(cfg, arm, s)
            PROCS[(cfg, arm, s)] = load_procs(cfg, arm, s)
            if arm in V5ARMS:
                pl, g1, rem = load_pool(cfg, arm, s)
                G1[(cfg, arm)] += g1
                REM[(cfg, arm)] = max(REM[(cfg, arm)], rem)
            for a in ACTORS:
                ac = acts[a]
                m = {'D': D[a], 'DH': DH[a], 'Dw': Dw[a], 'Dtc': {c: Dtc[(a, c)] for c in CLUSTERS},
                     'C': sum(x['notionalTotal'] if arm == 'VINF' else x['p2total'] for x in ac),
                     'out': [sum(x['output'] for x in ac[w * WIN:(w + 1) * WIN]) for w in range(NW)],
                     'first_out': next((x['t'] for x in ac if x['output'] > 0), None),
                     'ta': sum(x['ta'] for x in ac), 'ret': sum(x['restRet'] for x in ac),
                     'G1': next((x['t'] for x in ac if x['G'] >= 1), 384)}
                if arm in V5ARMS:
                    m['pool'] = pl[a]
                M[(cfg, arm, s, a)] = m

bad = [k for k, v in RUN.items() if v['mismatch_gain'] or v['mismatch_rest'] or v['mismatch_mcap']]
print('# EXP-0006 pool order analysis')
print(f'runs {len(RUN)}; invalid (plan vs Core trace) {len(bad)}; max lines per actor-month {max(v["max_lines"] for v in RUN.values())}; '
      f'A40 guard firings {sum(v["guard_firings"] for v in RUN.values())}')
print('Г1 plan equality when N ≤ P (cells, all V5 runs): ' + '; '.join(f'{c}/{a} {G1[(c, a)]}' for c in CONFIGS for a in V5ARMS)
      + f' -> {"ok" if not any(G1.values()) else "FAIL"}')
print('max PROP remainder P − Σx (raw, observer PROP plan in exhausted months): ' + '; '.join(f'{c}/{a} {REM[(c, a)]}' for c in CONFIGS for a in V5ARMS))
VALID = not bad and not any(G1.values())


def boot(cfg, fn):
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


def sD(cfg, arm, a, seeds):
    return sum(M[(cfg, arm, s, a)]['D'] for s in seeds)


def sdD(cfg, arm, a, seeds):   # ΔD against V0
    return sD(cfg, 'V0', a, seeds) - sD(cfg, arm, a, seeds)


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


def gap(cfg, a, seeds):   # D(CAN) − D(VINF)
    return sD(cfg, 'CAN', a, seeds) - sD(cfg, 'VINF', a, seeds)


def Q_of(cfg, arm, a, seeds):
    den = gap(cfg, a, seeds)
    if den <= 0 or den < AN['g3_min_gap_share_of_D_CAN'] * sD(cfg, 'CAN', a, seeds):
        return None
    return Fraction(sD(cfg, 'CAN', a, seeds) - sD(cfg, arm, a, seeds), den)


# ---- V-0 (main)
print('\n## V-0 (main): ΣΔD(VINF) > 0 and CI lower > 0')
V0OK = {}
for a in GREAT:
    p, ci, _ = boot('main', lambda ss, a=a: sdD('main', 'VINF', a, ss))
    V0OK[a] = p > 0 and ci[0] > 0
    print(f'{a}: {f(p / S, 1)} [{f(ci[0] / S, 1)}; {f(ci[1] / S, 1)}] -> {"pass" if V0OK[a] else "FAIL"}')
JUDGED = [a for a in GREAT if V0OK[a]]

# ---- D, M-E, U, P-dec (print)
print('\n## ΣD by set, arm, actor (культура·месяц; sums over seeds) | culture part | ΣD_g')
for cfg in SETS:
    for a in ACTORS:
        print(f'{cfg} {a}: ' + ' | '.join(f'{arm} {f(sD(cfg, arm, a, SEEDS[cfg]) / S, 1)} (c {f(sum(M[(cfg, arm, s, a)]["DH"] for s in SEEDS[cfg]) / S, 1)}, '
                                         f'g {f(sum(M[(cfg, arm, s, a)]["Dw"] for s in SEEDS[cfg]), 1)})' for arm in ARMS))
print('\n## tooling part of D by cluster (MET/CHEM/MECH/AERO), sums over seeds')
for cfg in SETS:
    for a in ['USA', 'GBR', 'FRA', 'SUN']:
        print(f'{cfg} {a}: ' + ' | '.join(f'{arm} ' + '/'.join(f(sum(M[(cfg, arm, s, a)]["Dtc"][c] for s in SEEDS[cfg]) / S, 0) for c in CLUSTERS)
                                         for arm in ['CAN', 'REV', 'PROP', 'SEV', 'VINF']))
print('\n## M-E: share of months with participation where N > P before allocation (rule-independent); U by cluster (Σ unmet, budget units); U_max/ΣU')
ME = {}
for cfg in CONFIGS:
    for a in ACTORS:
        parts = []
        for arm in V5ARMS:
            pm = sum(M[(cfg, arm, s, a)]['pool']['pm'] for s in SEEDS[cfg])
            em = sum(M[(cfg, arm, s, a)]['pool']['em'] for s in SEEDS[cfg])
            ME[(cfg, arm, a)] = em / pm if pm else NAN
            u = {c: sum(M[(cfg, arm, s, a)]['pool']['u'][c] for s in SEEDS[cfg]) / S for c in CLUSTERS}
            tu = sum(u.values())
            parts.append(f'{arm} {f(ME[(cfg, arm, a)])} ({em}/{pm}) U ' + '/'.join(f(u[c], 1) for c in CLUSTERS) + f' max {f(max(u.values()) / tu if tu else NAN, 2)}')
        print(f'{cfg} {a}: ' + ' | '.join(parts))

# ---- П1: K1, K2, K4, K5 per V5 arm (T6e2 r1)
print('\n## П1 — K1, K2, K4, K5 (T6e2 r1) for every V5 arm')
K = {arm: {} for arm in V5ARMS}
for arm in V5ARMS:
    k1 = True
    for cfg in ['main'] + FACT:
        for a in GREAT:
            p, ci, und = boot(cfg, lambda ss, a=a, cfg=cfg: R_of(cfg, arm, a, ss))
            if cfg == 'main':
                judged = a in JUDGED
                ok = p is not None and p >= Fraction(3, 4)
            else:
                _, cid, _ = boot(cfg, lambda ss, a=a, cfg=cfg: sdD(cfg, 'VINF', a, ss))
                judged = a in JUDGED and cid[0] > 0
                ok = not (ci[1] < 0.75)
            if judged:
                k1 &= ok
            print(f'{arm} K1 {cfg} {a}: R {f(p)} [{f(ci[0])}; {f(ci[1])}] und {und} -> {("ok" if ok else "FAIL") if judged else "n/j"}')
    K[arm]['K1'] = k1
    k2 = True
    for a in JUDGED:
        p, ci, und = boot('main', lambda ss, a=a: pihat_of('main', arm, a, ss))
        ok = p is not None and p <= 1
        k2 &= ok
        print(f'{arm} K2 {a}: π̂ {f(p)} [{f(ci[0])}; {f(ci[1])}] -> {"ok" if ok else "FAIL"}')
    K[arm]['K2'] = k2


def balance(cfg, arm, ref):
    """Median over seeds of (out_USA/out_GBR)(arm) / (…)(ref) per window; seeds with ref output of both > 0; judged with ≥ k4_min seeds."""
    lo, hi = AN['k4_band']
    res, allok = [], True
    for w in range(NW):
        bs = []
        for s in SEEDS[cfg]:
            oi = (M[(cfg, ref, s, 'USA')]['out'][w], M[(cfg, ref, s, 'GBR')]['out'][w])
            if min(oi) <= 0:
                continue
            ou, og = M[(cfg, arm, s, 'USA')]['out'][w], M[(cfg, arm, s, 'GBR')]['out'][w]
            bs.append(INF if og == 0 else 0.0 if ou == 0 else (ou / og) / (oi[0] / oi[1]))
        judged = len(bs) >= AN['k4_min_judged_seeds']
        m = med(bs)
        ok = (lo <= m <= hi) if judged else True
        allok &= ok
        res.append(f'w{w} {f(m, 2)} n{len(bs)}{"" if judged else " n/j"}{"" if ok else " FAIL"}')
    return allok, ' '.join(res)


k4_on = len(JUDGED) == 2
for arm in V5ARMS:
    ok, txt = balance('main', arm, 'VINF')
    K[arm]['K4'] = ok if k4_on else None
    print(f'{arm} K4 (vs VINF, main): {txt}')

pairs = []
for s in SEEDS['main']:
    for (a, p), x in PROCS[('main', 'VINF', s)].items():
        if a in [g for g in GREAT if g not in JUDGED] or x['tier'] < 4 or x['mastered_t0'] or x['first_mastered'] < 0:
            continue
        pairs.append((s, a, p))


def T_of(arm):
    return sum(min(384, PROCS[('main', arm, s)][(a, p)]['first_mastered']) if PROCS[('main', arm, s)][(a, p)]['first_mastered'] >= 0 else 384
               for (s, a, p) in pairs)


T0, TI = T_of('V0'), T_of('VINF')
print(f'K5 pairs {len(pairs)}; T(V0) {T0}, T(VINF) {TI}')
for arm in V5ARMS:
    if T0 - TI <= 0:
        K[arm]['K5'] = None
    else:
        r5 = Fraction(T0 - T_of(arm), T0 - TI)
        K[arm]['K5'] = r5 >= Fraction(3, 4)
        print(f'{arm} K5: T {T_of(arm)} R5 {f(r5)} -> {"ok" if K[arm]["K5"] else "FAIL"}')
P1 = {arm: bool(JUDGED) and K[arm]['K1'] and K[arm]['K2'] and K[arm]['K4'] in (True, None) and K[arm]['K5'] in (True, None) for arm in V5ARMS}
print('П1: ' + '; '.join(f'{arm} {"+" if P1[arm] else "−"}' for arm in V5ARMS))

# ---- Г2, Г3
print('\n## Г2 (stress: M-E(CAN) ≥ 0.20) and Г3 (gap D(CAN) − D(VINF) ≥ 5% ΣD(CAN), CI lower > 0)')
G3 = {}
for cfg in SETS:
    for a in GREAT + ['FRA', 'SUN']:
        p, ci, _ = boot(cfg, lambda ss, a=a, cfg=cfg: gap(cfg, a, ss))
        share = p / sD(cfg, 'CAN', a, SEEDS[cfg]) if sD(cfg, 'CAN', a, SEEDS[cfg]) else NAN
        G3[(cfg, a)] = p > 0 and ci[0] > 0 and share >= AN['g3_min_gap_share_of_D_CAN']
        print(f'{cfg} {a}: gap {f(p / S, 1)} [{f(ci[0] / S, 1)}; {f(ci[1] / S, 1)}] share {f(share)} -> Г3 {"+" if G3[(cfg, a)] else "−"}'
              + (f'; Г2 M-E {f(ME[(cfg, "CAN", a)])} -> {"+" if ME[(cfg, "CAN", a)] >= AN["g2_min_exhaustion_share"] else "−"}' if cfg == 'stress' else ''))
STRESS_J = [a for a in GREAT if G3[('stress', a)] and ME[('stress', 'CAN', a)] >= AN['g2_min_exhaustion_share']]
print(f'judged in stress (П3): {STRESS_J or "none"}')

# ---- Q
print('\n## Q = (D(CAN) − D(O)) / (D(CAN) − D(VINF)) [CI95] (undefined if gap < 5% ΣD(CAN))')
Q = {}
for cfg in SETS:
    for arm in RULES:
        for a in ACTORS:
            p, ci, und = boot(cfg, lambda ss, a=a, cfg=cfg: Q_of(cfg, arm, a, ss))
            Q[(cfg, arm, a)] = (p, ci)
            print(f'{cfg} {arm} {a}: Q {f(p)} [{f(ci[0])}; {f(ci[1])}] und {und}; ΣD(O)/ΣD(CAN) {f(sD(cfg, arm, a, SEEDS[cfg]) / sD(cfg, "CAN", a, SEEDS[cfg]) if sD(cfg, "CAN", a, SEEDS[cfg]) else NAN, 4)}')

# ---- П2–П5
print('\n## П2–П5')
VERD = {}
for arm in RULES:
    p2 = True
    for a in JUDGED:
        okd = sD('main', arm, a, SEEDS['main']) <= AN['p2_D_ratio_max'] * sD('main', 'CAN', a, SEEDS['main'])
        p, ci = Q[('main', arm, a)]
        okq = p is None or (p >= AN['p2_q_main_min_point'] and ci[1] >= 0)
        p2 &= okd and okq
    p3 = bool(STRESS_J)
    for a in STRESS_J:
        p, ci = Q[('stress', arm, a)]
        p3 &= p is not None and p >= AN['p3_q_stress_min'] and ci[0] > 0
    qm, qs = Q[('main', arm, 'GBR')][0], Q[('stress', arm, 'GBR')][0]
    sign_ok = qm is None or qs is None or qm == 0 or qs == 0 or (qm > 0) == (qs > 0)
    p3 &= sign_ok
    p4m, txtm = balance('main', arm, 'CAN')
    p4s, txts = balance('stress', arm, 'CAN')
    p5 = all(sD(cfg, arm, 'FRA', SEEDS[cfg]) <= AN['fra_veto_D_ratio_max'] * sD(cfg, 'CAN', 'FRA', SEEDS[cfg]) for cfg in SETS)
    VERD[arm] = {'П1': P1[arm], 'П2': p2, 'П3': p3, 'П4': p4m and p4s, 'П5': p5}
    print(f'{arm}: П4 main {txtm}\n      П4 stress {txts}')
    print(f'{arm}: П1 {"+" if P1[arm] else "−"} П2 {"+" if p2 else "−"} П3 {"+" if p3 else "−"} (GBR sign {"ok" if sign_ok else "FAIL"}) '
          f'П4 {"+" if p4m and p4s else "−"} П5 {"+" if p5 else "−"} (FRA ΣD/CAN main {f(sD("main", arm, "FRA", SEEDS["main"]) / sD("main", "CAN", "FRA", SEEDS["main"]), 4)}, '
          f'stress {f(sD("stress", arm, "FRA", SEEDS["stress"]) / sD("stress", "CAN", "FRA", SEEDS["stress"]), 4)}) -> '
          f'{"ГОДЕН" if all(VERD[arm].values()) else "не годен"}')

print('\n## Selection')
fit = [(float(sum(Q[('stress', arm, a)][0] for a in STRESS_J) / len(STRESS_J)), arm) for arm in CAND if all(VERD[arm].values())] if STRESS_J else []
if not VALID:
    print('SELECTION: run invalid (Г1 or trace) -> no verdict')
elif not STRESS_J:
    print('SELECTION: Г2/Г3 not met in stress for USA and GBR -> inconclusive; CAN stays')
elif not fit:
    print('SELECTION: no fit candidate -> CAN stays (refutes)' + (' [REV fit: print to owner only]' if all(VERD['REV'].values()) else ''))
else:
    fit.sort(reverse=True)
    close = [x for x in fit if fit[0][0] - x[0] <= AN['selection_tie_abs_q']]
    close.sort(key=lambda x: AN['simplicity_S'][x[1]])
    print(f'SELECTION: fit {[(v, round(q, 4)) for q, v in fit]}; within 0.05: {[v for _, v in close]}; selected {close[0][1]}')

# ---- prints: FRA, SUN, T_a
print('\n## Print: FRA first month with output (p50, None -> 384), SUN first G1 (p50), T_a and unspent rest (Σ, budget units)')
for cfg in SETS:
    for arm in ARMS:
        fo = [M[(cfg, arm, s, 'FRA')]['first_out'] if M[(cfg, arm, s, 'FRA')]['first_out'] is not None else 384 for s in SEEDS[cfg]]
        sg = [M[(cfg, arm, s, 'SUN')]['G1'] for s in SEEDS[cfg]]
        print(f'{cfg} {arm}: FRA first out p50 {f(med(fo), 0)} (none {sum(1 for x in fo if x >= 384)}/{len(fo)}); SUN G1 p50 {f(med(sg), 0)}; '
              + ' '.join(f'{a} T_a {f(sum(M[(cfg, arm, s, a)]["ta"] for s in SEEDS[cfg]) / S, 0)} ret {f(sum(M[(cfg, arm, s, a)]["ret"] for s in SEEDS[cfg]) / S, 0)}' for a in ['USA', 'GBR', 'FRA']))
print('\nQ per seed (stress, GBR): ' + '; '.join(
    f'{arm} p10/50/90 ' + '/'.join(f(x) for x in (lambda v: (pct(v, .1), med(v), pct(v, .9)))(
        [(M[('stress', 'CAN', s, 'GBR')]['D'] - M[('stress', arm, s, 'GBR')]['D']) / max(1, M[('stress', 'CAN', s, 'GBR')]['D'] - M[('stress', 'VINF', s, 'GBR')]['D']) for s in SEEDS['stress']]))
    for arm in RULES))
