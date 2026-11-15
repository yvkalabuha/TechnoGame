#!/usr/bin/env python3
"""Float sketch of the jet-slice knowledge layer for one actor (USA): a PRIOR for knowledge-pacing-proposal.md v2, not Core.

Simplifications (declared): one actor, no rivals except known_elsewhere for node 19 (GBR knows it at t0), no K_ind, no licences,
float arithmetic, Python random.Random(seed) for profile, PRIO_J and hazard draws (deterministic for a given seed and CPython).
Spec references: slice-jet-aviation.md v0.28 §3.1 (c_eff, prog, hazard, K_d), §4 (profiles), §8.1 (numbers).
Usage: python3 lab/analysis/pacing/pacing_sketch.py [validate|ablate|r2|cand|rush|all]
"""
import csv, os, random, statistics as st, sys
from collections import defaultdict

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..'))
D = os.path.join(ROOT, 'docs', 'research', 'data', 'jet-slice')
N = list(csv.DictReader(open(os.path.join(D, 'nodes.csv'), encoding='utf-8')))
IDX = {r['id']: int(r['idx']) for r in N}
NODE = {int(r['idx']): r for r in N}
REQ = {i: [[IDX[x] for x in g.split('|')] for g in r['requires'].split(';') if g] for i, r in NODE.items()}
INF = defaultdict(list)
for r in csv.DictReader(open(os.path.join(D, 'informs.csv'), encoding='utf-8')):
    INF[IDX[r['to']]].append((IDX[r['from']], float(r['weight'])))
TYPEC = {'FND': 1, 'PRN': 1.5, 'ENG': 3, 'PRC': 2, 'CMP': 1.5, 'APP': 1}
GEN = {'G0': 1, 'G1': 1, 'G2': 1.6, 'G3': 2.5, 'G4': 4.0}          # §8.1
GEN_X2 = {'G0': 1, 'G1': 1, 'G2': 2, 'G3': 4, 'G4': 8}            # sensitivity only: x2 per generation (upper edge of sources)
GNUM = {'G0': 0, 'G1': 1, 'G2': 2, 'G3': 3, 'G4': 4}
SREF, MU_K, K_NODE, K_DIFF = 0.1, 0.01, 0.05, 0.5
K0_USA = {20: 0.5, 59: 0.5}                                         # pre-registered rule §2 (levels 2, 2)
APP = {2: 59, 3: 60, 4: 61}
LAG = {2: 44, 3: 48, 4: 66}                                         # APP known -> first unit, p00/p23 USA medians
WIN = {2: (16, 87), 3: (100, 195), 4: (256, 363)}                   # C4 windows in months (exp0002 year_of, inclusive)


def closure(target, known):
    out, stack = set(), [target]
    while stack:
        n = stack.pop()
        if n in known or n in out:
            continue
        out.add(n)
        for gr in REQ[n]:
            if not any(x in known for x in gr):
                stack.append(gr[0])
    return out


def run(seed, k=1.0, theta=0.6, hmax=0.08, yhalf=0.5, cap=3.0, gm=GEN, w_out=0.0, start_prog=None, beta=0.0,
        service=False, pin=None, pin_mult=4.0, g=0.0299, kgrow=True, use_inf=True, T=384):
    rnd = random.Random(seed)
    prof = rnd.randrange(3)
    share_r = [0.35, 0.20, 0.28][prof]
    K = dict(aero=.30, prop=.30, metal=.25, chem=.10, mech=.15)        # USA K_d(t0), scenario_1945
    known = {i: -1 for i, r in NODE.items() if r['known_at_start'] == 'ALL' or 'USA' in r['known_at_start'].split(';')}
    prog = defaultdict(float)
    prog.update(start_prog or {})
    budget, j, off = 1.0, {}, 0.0
    for t in range(T):
        if t % 12 == 0:
            j = {i: rnd.uniform(0.5, 1.5) for i in NODE}
        F = []
        for n in NODE:
            if n in known or not all(any(x in known for x in gr) for gr in REQ[n]):
                continue
            r = NODE[n]
            if service and r['type'] == 'APP' and GNUM[r['gen']] >= 3:
                prev = APP[GNUM[r['gen']] - 1]
                if prev not in known or t < known[prev] + LAG[GNUM[r['gen']] - 1]:
                    continue
            F.append(n)
        path = closure(pin, known) if pin is not None else set()
        W = {}
        for n in F:
            r, w = NODE[n], 1.0
            if prof == 0 and (r['type'] == 'ENG' or r['hazard'] == 'key'):
                w = 2.0
            if prof == 1 and r['type'] == 'PRC':
                w = 2.0
            if n in path:
                w *= pin_mult
            W[n] = w * j[n]
        tot = sum(W.values()) + w_out
        S = share_r * budget
        if tot > 0:
            off += S * w_out / tot
        opened, amt = [], defaultdict(float)
        for n in F:
            r = NODE[n]
            c = TYPEC[r['type']] * gm[r['gen']] / k * 12 * SREF
            disc = r['discipline']
            km = 0.0 if r['type'] == 'APP' else K.get(disc, 0.0) / (gm[r['gen']] ** beta)
            sw = sum(w for f, w in INF[n] if f in known) if use_inf else 0.0
            ke = 1 if n == 19 else 0
            ce = c * (1 - K_DIFF * ke) / ((1 + km) * (1 + sw))
            a = S * W[n] / tot
            prog[n] = min(cap, prog[n] + a / ce)
            if disc:
                amt[disc] += a
            if r['hazard'] in ('key', 'hz'):
                if prog[n] >= theta:
                    y = prog[n] - theta
                    if rnd.random() < hmax * y / (y + yhalf):
                        opened.append(n)
            elif prog[n] >= 1:
                opened.append(n)
        if kgrow:
            for d, a in amt.items():
                K[d] += MU_K * a / SREF
        for n in opened:
            known[n] = t
            if NODE[n]['discipline'] and kgrow:
                K[NODE[n]['discipline']] += K_NODE
        budget *= 1 + g / 12
    known['_off'] = off
    return known


def series(n=100, **kw):
    return [run(s, **kw) for s in range(n)]


def med(res, x):
    return st.median(kn.get(x, 999) for kn in res)


def summary(res):
    m = {g: med(res, APP[g]) for g in (2, 3, 4)}
    w = {g: sum(1 for kn in res if WIN[g][0] <= kn.get(APP[g], 999) + LAG[g] <= WIN[g][1]) / len(res) for g in (2, 3, 4)}
    return m, w


def line(name, res):
    m, w = summary(res)
    return (f"{name:46s} APP59/60/61 {m[2]:5.0f} {m[3]:5.0f} {m[4]:5.0f} | G2->G3 {m[3]-m[2]:4.0f} G3->G4 {m[4]-m[3]:4.0f}"
            f" | proxy win {w[2]:.2f}/{w[3]:.2f}/{w[4]:.2f}")


def validate():
    print('# validate vs Core T12 (USA medians APP59/60/61): p00 139/202/233, p23 84/133/166, p42 56/98/116')
    print(line('sketch p00 k1 t.6 h.08', series(k=1, theta=.6, hmax=.08)))
    print(line('sketch p23 k2 t.9 h.12', series(k=2, theta=.9, hmax=.12)))
    print(line('sketch p42 k3 t.6 h.16', series(k=3, theta=.6, hmax=.16)))


def ablate():
    print('# ablations at k1 t.6 h.08')
    b = dict(k=1, theta=.6, hmax=.08)
    print(line('base', series(**b)))
    print(line('no budget growth', series(g=0, **b)))
    print(line('no K_d growth', series(kgrow=False, **b)))
    print(line('no informs', series(use_inf=False, **b)))
    print(line('K2b beta=1', series(beta=1, **b)))
    print(line('W_out 4', series(w_out=4, **b)))


def r2():
    print('# R2 predictions (k only, current model)')
    for k, t, h in ((4, .9, .16), (3.5, .9, .16), (4, .6, .12)):
        print(line(f'k{k} t{t} h{h}', series(k=k, theta=t, hmax=h)))


CELLS = ((.9, .16), (.9, .12), (.6, .12))
KS = (2.5, 3, 3.5, 4)


def cand():
    print('# candidates (K0 rule, W_out grid, K2b beta 1); k <= k_max = 4; cells', CELLS)
    variants = [('K0', dict(start_prog=K0_USA)),
                ('K0+K2b', dict(start_prog=K0_USA, beta=1)),
                ('K0+service', dict(start_prog=K0_USA, service=True)),
                ('K0+K2b+service', dict(start_prog=K0_USA, beta=1, service=True))]
    for w in (2, 4, 8):
        variants += [(f'K0+K1 W{w}', dict(start_prog=K0_USA, w_out=w)),
                     (f'K0+K1 W{w}+K2b', dict(start_prog=K0_USA, w_out=w, beta=1)),
                     (f'K0+K1 W{w}+K2b+service', dict(start_prog=K0_USA, w_out=w, beta=1, service=True))]
    variants += [('sens: K0+K1 W4+K2b+K2a x2', dict(start_prog=K0_USA, w_out=4, beta=1, gm=GEN_X2))]
    for name, kw in variants:
        for t, h in CELLS:
            for k in KS:
                print(line(f'{name} k{k} t{t} h{h}', series(k=k, theta=t, hmax=h, **kw)))


def rush(specs=None):
    print('# rush: pin APP61 x4 from t0 vs AI of the same world (W_out equal, #235)')
    specs = specs or [('current k2 t.9 h.12', dict(k=2, theta=.9, hmax=.12))]
    for name, kw in specs:
        ai, pl = series(**kw), series(pin=61, pin_mult=4, **kw)
        gain = med(ai, 61) - med(pl, 61)
        delays = {n: med(pl, n) - med(ai, n) for n in NODE if med(ai, n) < 999 and med(ai, n) >= 0}
        worst = sorted(delays.items(), key=lambda x: -x[1])[:5]
        print(f'{name:40s} G4 AI {med(ai,61):.0f} -> pin {med(pl,61):.0f} gain {gain:.0f} | G3 APP60 {med(ai,60):.0f}->{med(pl,60):.0f}'
              f' | most delayed: ' + ' '.join(f'{n}:{d:+.0f}' for n, d in worst))


if __name__ == '__main__':
    what = sys.argv[1] if len(sys.argv) > 1 else 'all'
    for name, f in (('validate', validate), ('ablate', ablate), ('r2', r2), ('cand', cand), ('rush', rush)):
        if what in (name, 'all'):
            f()
