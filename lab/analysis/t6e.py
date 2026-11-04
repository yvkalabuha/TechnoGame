#!/usr/bin/env python3
"""EXP-0003 (T6e) analysis: metrics M1-M4, criteria K1-K5, selection rule (spec slice-jet-aviation.md v0.19 r1 §11 T6e,
deviations O1-O13 in development/experiments/EXP-0003-t6e-tooling-target.md). Reads lab/results/t6e/out, prints a report.
Usage: python3 lab/analysis/t6e.py [out_dir] [manifest]"""
import csv
import json
import math
import os
import random
import sys
from collections import defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, 'lab/results/t6e/out')
MAN = json.load(open(sys.argv[2] if len(sys.argv) > 2 else os.path.join(ROOT, 'lab/manifests/EXP-0003-t6e.json')))
AN = MAN['analysis']
ARMS = ['V0', 'V1', 'V2', 'V3', 'V3b', 'V4', 'VINF']
CAND = ['V1', 'V2', 'V3', 'V4']
GREAT = ['USA', 'GBR']
ACTORS = ['USA', 'GBR', 'SUN', 'FRA', 'SWE']
CONFIGS = list(MAN['configs'].keys())
SEEDS = {c: MAN['configs'][c]['seeds'] for c in CONFIGS}
MONTHS = MAN['months']
WIN = AN['window_months']
NW = MONTHS // WIN
S = 1e6
INF = float('inf')


def med(xs):
    xs = sorted(xs)
    n = len(xs)
    if n == 0:
        return float('nan')
    return xs[n // 2] if n % 2 else (xs[n // 2 - 1] + xs[n // 2]) / 2


def lower_med(xs):
    xs = sorted(xs)
    return xs[(len(xs) - 1) // 2] if xs else float('nan')


def pct(xs, q):
    xs = sorted(xs)
    if not xs:
        return float('nan')
    k = (len(xs) - 1) * q
    f = math.floor(k)
    c = min(f + 1, len(xs) - 1)
    if xs[f] == xs[c] or math.isinf(xs[f]) or math.isinf(xs[c]):
        return xs[f] if k - f < 0.5 else xs[c]
    return xs[f] + (xs[c] - xs[f]) * (k - f)


def ratio(a, b):
    if math.isinf(a) and math.isinf(b):
        return None
    if b == 0 or math.isnan(a) or math.isnan(b):
        return None
    if math.isinf(b):
        return 0.0
    return a / b


def run_dir(cfg, arm, seed):
    return os.path.join(OUT, cfg, arm, f'seed{seed}')


def load(cfg, arm, seed):
    d = run_dir(cfg, arm, seed)
    r = {'run': json.load(open(os.path.join(d, 'run.json')))}
    cells = defaultdict(list)  # (actor, c, d) -> list of rows (t ordered)
    with open(os.path.join(d, 'cells.csv')) as f:
        for row in csv.DictReader(f):
            if row['actor'] not in GREAT:
                continue
            cells[(row['actor'], row['c'], row['d'])].append((int(row['t']), int(row['pool']), int(row['p2']), int(row['notional']),
                                                             int(row['gotE']), int(row['lab']), int(row['H']), int(row['M'])))
    r['cells'] = cells
    acts = defaultdict(list)
    with open(os.path.join(d, 'actors.csv')) as f:
        for row in csv.DictReader(f):
            acts[row['actor']].append({k: (row[k] if k in ('actor', 'profile') else int(row[k])) for k in row})
    r['acts'] = acts
    hend = {}
    with open(os.path.join(d, 'hend.csv')) as f:
        for row in csv.DictReader(f):
            hend[(row['actor'], row['c'], row['d'])] = (int(row['H0']), int(row['Hend']))
    r['hend'] = hend
    procs = {}
    with open(os.path.join(d, 'procs.csv')) as f:
        for row in csv.DictReader(f):
            procs[(row['actor'], int(row['p']))] = {k: int(row[k]) for k in ('tier', 'mastered_t0', 'first_mastered', 't_first', 't_07')}
    r['procs'] = procs
    lines = defaultdict(list)
    with open(os.path.join(d, 'lines.csv')) as f:
        for row in csv.DictReader(f):
            lines[(row['actor'], row['design'], row['gen'])].append((int(row['t']), int(row['rate']), int(row['unitcost']), int(row['minyield'])))
    r['lines'] = lines
    return r


print('loading ...', file=sys.stderr)
DATA = {(c, a, s): load(c, a, s) for c in CONFIGS for a in ARMS for s in SEEDS[c]}

# ---- validity (O1)
bad = [k for k, v in DATA.items() if v['run']['mismatch_gain'] or v['run']['mismatch_rest'] or v['run']['mismatch_mcap']]
print(f'# EXP-0003 T6e analysis\nruns {len(DATA)}, invalid (plan reconstruction mismatch) {len(bad)}')

# ---- pool (common for all arms of a seed: union of V0 and VINF)
POOL = {}
for c in CONFIGS:
    for s in SEEDS[c]:
        for a in GREAT:
            p = set()
            for arm in ('V0', 'VINF'):
                for key, rows in DATA[(c, arm, s)]['cells'].items():
                    if key[0] == a and any(x[1] for x in rows):
                        p.add(key)
            POOL[(c, s, a)] = p

# ---- per (config, arm, seed, actor) M1 metrics
M = {}
for (c, arm, s), r in DATA.items():
    for a in GREAT:
        pool = POOL[(c, s, a)]
        dh = sum(r['hend'][k][1] - r['hend'][k][0] for k in pool) / S
        cost = cost_pm = 0
        pm = got = tool = sw = 0
        for k in pool:
            rows = r['cells'].get(k, [])
            prev = None
            for (t, pl, p2, notl, ge, lab, h, m) in rows:
                money = notl if arm == 'VINF' else p2
                cost += money
                if pl:
                    cost_pm += money
                    pm += 1
                    got += ge
                    tool += lab == 2
                    if prev is not None and prev[0] == t - 1 and {prev[1], lab} == {1, 2}:
                        sw += 1
                    prev = (t, lab)
                else:
                    prev = None
        cost_all = sum((x[3] if arm == 'VINF' else x[2]) for k, rows in r['cells'].items() if k[0] == a for x in rows)
        acts = r['acts'][a]
        pool_money = sum(x['rest'] + x['bcap'] for x in acts)
        M[(c, arm, s, a)] = {
            'dH': dh, 'C': cost / S, 'C_pm': cost_pm / S, 'C_all': cost_all / S,
            'c': (cost / S) / dh if dh > 0 else INF,
            'm1a': got / pm if pm else float('nan'), 'm1b': 100 * sw / pm if pm else float('nan'), 'm1c': tool / pm if pm else float('nan'),
            'pool_cells': len(pool), 'pool_months': pm,
            'rest_res': sum(x['restRet'] for x in acts) / pool_money if pool_money else float('nan'),
            'ta_res': sum(x['ta'] for x in acts) / pool_money if pool_money else float('nan'),
            'total': sum(x['total'] for x in acts) / S, 'bcap': sum(x['bcap'] for x in acts) / S,
            'out': [sum(x['output'] for x in acts[w * WIN:(w + 1) * WIN]) / S for w in range(NW)],
        }

rng_seed = AN['bootstrap_seed']


def boot_median_ratio(cfg, num_arm, den_arm, actor, key, invert=False):
    """Per-seed ratio key(num)/key(den); median over seeds; paired percentile bootstrap CI; excluded count."""
    seeds = SEEDS[cfg]
    rs = []
    for s in seeds:
        x = ratio(M[(cfg, num_arm, s, actor)][key], M[(cfg, den_arm, s, actor)][key])
        rs.append(x)
    valid = [x for x in rs if x is not None]
    excl = len(rs) - len(valid)
    if not valid:
        return float('nan'), (float('nan'), float('nan')), excl, rs
    rnd = random.Random(rng_seed)
    bs = []
    for _ in range(AN['bootstrap_reps']):
        smp = [rs[rnd.randrange(len(rs))] for _ in rs]
        smp = [x for x in smp if x is not None]
        if smp:
            bs.append(med(smp))
    return med(valid), (pct(bs, 0.025), pct(bs, 0.975)), excl, rs


def f(x, n=3):
    if x is None:
        return '—'
    if isinstance(x, float) and math.isinf(x):
        return '+∞'
    if isinstance(x, float) and math.isnan(x):
        return 'nan'
    return f'{x:.{n}f}'


# ---- M1 print
print('\n## M1 (main): p50 over seeds [p10; p90]')
print('arm actor | dH | C | c=C/dH | C pool-months | C all cells | M1a E>0 | M1b sw/100 | M1c TOOLING | rest residue | T_a residue | pool cells')
for arm in ARMS:
    for a in GREAT:
        vals = [M[('main', arm, s, a)] for s in SEEDS['main']]
        def q(k):
            xs = [v[k] for v in vals]
            return f'{f(med(xs))} [{f(pct(xs, .1))}; {f(pct(xs, .9))}]'
        print(f'{arm} {a} | {q("dH")} | {q("C")} | {q("c")} | {f(med([v["C_pm"] for v in vals]))} | {f(med([v["C_all"] for v in vals]))} | '
              f'{q("m1a")} | {q("m1b")} | {q("m1c")} | {f(med([v["rest_res"] for v in vals]))} | {f(med([v["ta_res"] for v in vals]))} | '
              f'{med([v["pool_cells"] for v in vals])}')
for a in GREAT:
    cs = sorted(((M[('main', 'V0', s, a)]['c'], s) for s in SEEDS['main']))
    print(f'V0 {a} c extremes: min seed {cs[0][1]} {f(cs[0][0])}, max seed {cs[-1][1]} {f(cs[-1][0])}')

# ---- K1, K2
RES = {v: {} for v in CAND + ['V3b']}
print('\n## K1: dH(V)/dH(VINF) p50 over seeds [CI95], threshold >= 0.75')
for v in CAND + ['V3b', 'V0']:
    for cfg in CONFIGS:
        for a in GREAT:
            m, ci, ex, _ = boot_median_ratio(cfg, v, 'VINF', a, 'dH')
            ok = (m >= AN['k1_min_ratio_to_vinf']) and ex <= len(SEEDS[cfg]) / 2
            edge = ci[0] < AN['k1_min_ratio_to_vinf'] <= ci[1] or ci[0] <= AN['k1_min_ratio_to_vinf'] < ci[1]
            if v in RES:
                RES[v].setdefault('K1', []).append((cfg, a, m, ci, ok, edge, ex))
            print(f'{v} {cfg} {a}: {f(m)} [{f(ci[0])}; {f(ci[1])}] excl {ex} -> {"ok" if ok else "FAIL"}{" (на грани)" if edge else ""}')
print('\n## K2: c(V)/c(VINF) p50 over seeds [CI95], threshold <= 1 (main; factorial printed)')
for v in CAND + ['V3b', 'V0']:
    for cfg in CONFIGS:
        for a in GREAT:
            m, ci, ex, _ = boot_median_ratio(cfg, v, 'VINF', a, 'c')
            ok = (m <= AN['k2_max_ratio_to_vinf']) and ex <= len(SEEDS[cfg]) / 2
            edge = ci[0] <= AN['k2_max_ratio_to_vinf'] <= ci[1]
            if v in RES and cfg == 'main':
                RES[v].setdefault('K2', []).append((cfg, a, m, ci, ok, edge, ex))
            print(f'{v} {cfg} {a}: {f(m)} [{f(ci[0])}; {f(ci[1])}] excl {ex} -> {"ok" if ok else "FAIL"}{" (на грани)" if edge else ""}'
                  + ('' if cfg == 'main' else ' (print)'))

# ---- K4
print('\n## K4: r_w = out_USA/out_GBR; p50 over seeds of r_w(V)/r_w(V0), band [0.67; 1.5] (main)')
lo, hi = AN['k4_band']
for v in CAND + ['V3b', 'VINF']:
    parts = []
    allok = True
    edges = []
    for w in range(NW):
        rs = []
        zero_v0 = 0
        for s in SEEDS['main']:
            o0 = (M[('main', 'V0', s, 'USA')]['out'][w], M[('main', 'V0', s, 'GBR')]['out'][w])
            ov = (M[('main', v, s, 'USA')]['out'][w], M[('main', v, s, 'GBR')]['out'][w])
            if min(o0) > 0 and min(ov) > 0:
                rs.append((ov[0] / ov[1]) / (o0[0] / o0[1]))
            elif min(o0) == 0 and min(ov) > 0:
                zero_v0 += 1
        if not rs:
            parts.append(f'w{w}: —(V0=0,V>0: {zero_v0})')
            continue
        m = med(rs)
        rnd = random.Random(rng_seed)
        bs = [med([rs[rnd.randrange(len(rs))] for _ in rs]) for _ in range(AN['bootstrap_reps'])]
        ci = (pct(bs, .025), pct(bs, .975))
        ok = lo <= m <= hi
        edge = ci[0] < lo or ci[1] > hi
        allok &= ok
        if edge:
            edges.append(w)
        parts.append(f'w{w}: {f(m, 2)} [{f(ci[0], 2)}; {f(ci[1], 2)}] n{len(rs)}{"" if ok else " FAIL"}')
    if v in RES:
        RES[v]['K4'] = (allok, edges)
    print(f'{v}: ' + ' | '.join(parts))

print('\n## M2: output by 48-month windows, p50 over seeds (main), units/month summed over the window')
for arm in ARMS:
    for a in GREAT:
        print(f'{arm} {a}: ' + ' '.join(f(med([M[("main", arm, s, a)]["out"][w] for s in SEEDS["main"]]), 0) for w in range(NW))
              + f' | windows with output>0 (p50): {med([sum(1 for x in M[("main", arm, s, a)]["out"] if x > 0) for s in SEEDS["main"]])}')


# ---- K5, M3
def first_m(cfg, arm, s, a, p):
    x = DATA[(cfg, arm, s)]['procs'][(a, p)]['first_mastered']
    return 384 if x < 0 else x


print('\n## K5: p50 of first mastered month, pairs (seed, actor, PRC tier>=4 not mastered at t0) with mastered in V0 or V; censor 384 (main)')
procs_t4 = sorted({p for (a, p), v in DATA[('main', 'V0', 1)]['procs'].items() if v['tier'] >= 4})
for v in CAND + ['V3b', 'VINF']:
    xs0, xsv, n = [], [], 0
    for s in SEEDS['main']:
        for a in ACTORS:
            for p in procs_t4:
                pr0 = DATA[('main', 'V0', s)]['procs'][(a, p)]
                if pr0['mastered_t0']:
                    continue
                f0, fv = first_m('main', 'V0', s, a, p), first_m('main', v, s, a, p)
                if f0 < 384 or fv < 384:
                    xs0.append(f0)
                    xsv.append(fv)
    m0, mv = med(xs0), med(xsv)
    ok = mv <= m0
    if v in RES:
        RES[v]['K5'] = (ok, m0, mv, len(xs0))
    print(f'{v}: pairs {len(xs0)}, p50 V0 {f(m0, 1)}, p50 V {f(mv, 1)} -> {"ok" if ok else "FAIL"}')
print('pairs by (actor, PRC) with first mastered in any arm (main), count of seeds per arm:')
for a in ACTORS:
    for p in procs_t4:
        cnt = {arm: sum(1 for s in SEEDS['main'] if not DATA[('main', 'V0', s)]['procs'][(a, p)]['mastered_t0'] and first_m('main', arm, s, a, p) < 384) for arm in ARMS}
        if any(cnt.values()):
            print(f'  {a} {p}: ' + ' '.join(f'{arm}={cnt[arm]}' for arm in ARMS))

print('\n## M3: first month G(a) >= g (p50, censor 384) and G_front (main)')
for arm in ARMS:
    parts = []
    for a in GREAT + ['SUN']:
        for g in (1, 2, 3):
            xs = []
            for s in SEEDS['main']:
                acts = DATA[('main', arm, s)]['acts'][a]
                xs.append(next((x['t'] for x in acts if x['G'] >= g), 384))
            parts.append(f'{a} G>={g}: {f(med(xs), 0)}')
    gf = []
    mx = []
    for s in SEEDS['main']:
        acts = DATA[('main', arm, s)]['acts']['USA']
        gf.append(sum(1 for x in acts if x['Gfront'] >= 2))
        mx.append(max(x['Gfront'] for x in acts))
    parts.append(f'G_front>=2 months p50 {f(med(gf), 0)}; maxG_front p50 {f(med(mx), 0)} max {max(mx)}')
    print(f'{arm}: ' + '; '.join(parts))


# ---- K3: C1, C2 (O8, O9)
def c1_stats(arm):
    dev, mature = [], []
    for s in SEEDS['main']:
        for key, rows in DATA[('main', arm, s)]['lines'].items():
            q1 = rows[0][1]
            cum = 0
            marks = [(rows[0][0], rows[0][2], rows[0][3])]
            k = 1
            for (t, rate, uc, my) in rows:
                cum += rate
                while cum >= (2 ** k) * q1 and k <= 20:
                    marks.append((t, uc, my))
                    k += 1
            dls = []
            for i in range(1, len(marks)):
                u0, u1 = marks[i - 1][1], marks[i][1]
                if u0 > 0 and u1 > 0:
                    dls.append((i, math.log(u0 / u1), marks[i - 1][2]))
            d13 = [x[1] for x in dls if x[0] <= 3]
            if d13:
                dev.append(max(d13))
            mature += [x[1] for x in dls if x[2] >= 900000]
    return dev, mature


def dk(sv, lo_, hi_):
    if sv is None or (isinstance(sv, float) and math.isnan(sv)):
        return 1.0
    return max(0.0, lo_ - sv, sv - hi_) / (hi_ - lo_)


def c2_stats(arm):
    out = {}
    for tier, plist in AN['c2_star_prc'].items():
        durs, no_first, n = [], 0, 0
        for s in SEEDS['main']:
            for a in ACTORS:
                for p in plist:
                    pr = DATA[('main', arm, s)]['procs'][(a, p)]
                    if pr['mastered_t0']:
                        continue
                    n += 1
                    if pr['t_first'] < 0:
                        no_first += 1
                        continue
                    durs.append(INF if pr['t_07'] < 0 else max(0, pr['t_07'] - pr['t_first']))
        out[tier] = (lower_med(durs) if durs else float('nan'), n, no_first, len(durs))
    return out


K3 = {}
c1lo, c1hi = AN['c1_corridors_dln_per_doubling']['development_1_3']
c1mlo, c1mhi = AN['c1_corridors_dln_per_doubling']['mature_yield_ge_0_9']
c2lo, c2hi = AN['c2_corridor_months']
print('\n## M4/K3: anchors (main seeds 1..20; not calibration)')
for arm in ARMS:
    dev, mat = c1_stats(arm)
    s_dev = med(dev) if dev else float('nan')
    s_mat = med(mat) if mat else float('nan')
    c2 = c2_stats(arm)
    t4, t5 = c2['4'], c2['5']
    d_c2_4 = 1.0 if (t4[3] == 0 or t4[2] > t4[1] / 2) else dk(t4[0], c2lo, c2hi) if not math.isinf(t4[0]) else 1.0
    order_ok = t4[3] > 0 and t5[3] > 0 and t5[2] <= t5[1] / 2 and t5[0] > t4[0]
    d = {'C1_dev': dk(s_dev, c1lo, c1hi), 'C1_mature': dk(s_mat, c1mlo, c1mhi), 'C2_t4': d_c2_4, 'C2_order': 0.0 if order_ok else 1.0}
    K3[arm] = d
    print(f'{arm}: C1 dev p50 {f(s_dev)} (lines {len(dev)}), mature p50 {f(s_mat)} (doublings {len(mat)}); '
          f'C2 t4 p50 {f(t4[0], 0)} mo (pairs {t4[1]}, no t_first {t4[2]}), t5 p50 {f(t5[0], 0)} (pairs {t5[1]}, no t_first {t5[2]}); '
          f'd: ' + ', '.join(f'{k}={f(v, 3)}' for k, v in d.items()))
for v in CAND + ['V3b']:
    RES[v]['K3'] = all(K3[v][k] <= K3['V0'][k] + 1e-12 for k in K3['V0'])

# C4 print
print('\n## C4 (print): share of seeds with first G_front>=2 in months 16-87, >=3 in 100-195, >=4 in 256-363 (main)')
for arm in ARMS:
    sh = []
    for g, (a0, a1) in ((2, (16, 87)), (3, (100, 195)), (4, (256, 363))):
        hits = 0
        for s in SEEDS['main']:
            acts = DATA[('main', arm, s)]['acts']['USA']
            first = next((x['t'] for x in acts if x['Gfront'] >= g), None)
            hits += first is not None and a0 <= first <= a1
        sh.append(f'G>={g}: {hits}/{len(SEEDS["main"])}')
    print(f'{arm}: ' + ', '.join(sh))

# ---- v0.13 print
print('\n## v0.13 thresholds (print): M1a p50 >= 0.9 and p50 (ΣTotal(V) − ΣTotal(V0))/ΣB_cap <= 0.02, USA and GBR, main')
V013 = {}
for v in CAND + ['V3b']:
    ok = True
    parts = []
    for a in GREAT:
        m1a = med([M[('main', v, s, a)]['m1a'] for s in SEEDS['main']])
        inc = med([(M[('main', v, s, a)]['total'] - M[('main', 'V0', s, a)]['total']) / M[('main', v, s, a)]['bcap'] for s in SEEDS['main']])
        ok &= m1a >= AN['v013_print']['m1a_min'] and inc <= AN['v013_print']['invest_increment_max_share_cap']
        parts.append(f'{a} M1a {f(m1a)} inc {f(inc, 4)}')
    V013[v] = ok
    print(f'{v}: {"; ".join(parts)} -> {"да" if ok else "нет"}')

# ---- verdict and selection
print('\n## Verdict (candidates V1-V4; V3b printed as control)')
fit = []
for v in CAND + ['V3b']:
    r = RES[v]
    k1 = all(x[4] for x in r['K1'])
    k2 = all(x[4] for x in r['K2'])
    k3 = r['K3']
    k4 = r['K4'][0]
    k5 = r['K5'][0]
    edge = [f'K1 {x[0]} {x[1]}' for x in r['K1'] if x[5]] + [f'K2 {x[1]}' for x in r['K2'] if x[5]] + [f'K4 w{w}' for w in r['K4'][1]]
    ok = k1 and k2 and k3 and k4 and k5
    cbar = (med([M[('main', v, s, 'USA')]['c'] for s in SEEDS['main']]) + med([M[('main', v, s, 'GBR')]['c'] for s in SEEDS['main']])) / 2
    print(f'{v}: K1 {"+" if k1 else "−"} K2 {"+" if k2 else "−"} K3 {"+" if k3 else "−"} K4 {"+" if k4 else "−"} K5 {"+" if k5 else "−"} '
          f'-> {"ГОДЕН" if ok else "не годен"}; c (mean of USA/GBR p50) {f(cbar)}; v0.13 {"да" if V013[v] else "нет"}; на грани: {", ".join(edge) or "—"}'
          + (' [на грани по переобъявлению]' if ok and not V013[v] else ''))
    if ok and v in CAND:
        fit.append((cbar, v))
fit.sort()
if not fit:
    print('SELECTION: no fit candidate -> A39 stays V0; report to the owner')
else:
    best = fit[0]
    close = [x for x in fit if x[0] <= best[0] * (1 + AN['selection_tie_rel'])]
    close.sort(key=lambda x: (AN['simplicity_S'][x[1]], AN['new_literals'][x[1]], CAND.index(x[1])))
    print(f'SELECTION: fit {[(v, round(c, 3)) for c, v in fit]}; within 5% of min: {[v for _, v in close]}; selected {close[0][1]}')

# ---- USA 49 diagnostic (seed 2, V0; all arms by window)
print('\n## Diagnostic: USA process 49, main seed 2, by 48-month windows (mean yield; mean v_exp; modal limiter; mean M−H per active dim; months E>0)')
for arm in ARMS:
    rows = list(csv.DictReader(open(os.path.join(run_dir('main', arm, 2), 'diag49.csv'))))
    rows = [r for r in rows if r['actor'] == 'USA']
    parts = []
    for w in range(NW):
        ws = rows[w * WIN:(w + 1) * WIN]
        y = sum(int(r['yield']) for r in ws) / len(ws) / S
        ve = sum(int(r['vexp']) for r in ws) / len(ws) / S
        lims = defaultdict(int)
        for r in ws:
            lims[r['lim']] += 1
        lim = max(lims.items(), key=lambda x: x[1])
        mh = '/'.join(f(sum(int(r[k]) for r in ws) / len(ws) / S, 3) for k in ('MminusH_prec', 'MminusH_pur', 'MminusH_qc') if ws[0][k] != '')
        e = sum(1 for r in ws if int(r['E']) > 0)
        parts.append(f'w{w}: y {f(y, 2)} v {f(ve, 0)} {lim[0]}({lim[1]}) M−H {mh} E>0 {e}')
    print(f'{arm}: ' + ' | '.join(parts))
