#!/usr/bin/env python3
"""Independent expected values for the product layer of the jet slice (D-tests for TASK-0018).

Spec: docs/simulation/slice-jet-aviation.md v0.10 §1.4, §3.3, §3.5 (A28, A31-A33), §6.1 L_P, §8.1;
contract docs/architecture/LAYER_CONTRACT.md v2.3 §4.0, §4.4. Data: docs/research/data/jet-slice/
(nodes.csv read at run time; product_class.md §1 and product_inputs.md §4, §6 transcribed below).
Written from the spec, not from the C# code (#42). Python stdlib only.

Arithmetic: Fx6 raw integers, floor on every * and / (contract §4.0). PowFrac is DetMath.PowFrac
(ADR-0012): x = raw/1e6 as double, acc *= x^(2^-i) for the set bits of the q10 fraction (sqrt chain),
times x^whole by squaring, negative exponent = one IEEE division, result floor(v*1e6). IEEE sqrt,
*, / are correctly rounded, so Python doubles give the same bits as .NET.

Usage (from repo root):
    python3 lab/reference/jet_slice/products_ref.py           # writes out/products.json
    python3 lab/reference/jet_slice/products_ref.py --check   # exit 1 if out/products.json differs
"""
import argparse
import csv
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
NODES_CSV = os.path.join(ROOT, "docs", "research", "data", "jet-slice", "nodes.csv")
OUT = os.path.join(HERE, "out", "products.json")

S = 1_000_000


def fx(text):
    """Decimal string -> Fx6 raw (exact)."""
    whole, _, frac = text.partition(".")
    neg = whole.startswith("-")
    raw = abs(int(whole or "0")) * S + int((frac + "000000")[:6])
    return -raw if neg else raw


def fi(n):
    return n * S


def mul(a, b):
    return (a * b) // S


def div(a, b):
    return (a * S) // b


def pow_frac(raw, q10):
    """DetMath.PowFracFx(Fx6.FromRaw(raw), q10)."""
    x = raw / S
    k = abs(q10)
    whole, frac = k >> 10, k & 1023
    acc, root, bit = 1.0, x, 512
    while frac:
        root = math.sqrt(root)
        if frac & bit:
            acc *= root
            frac &= ~bit
        bit >>= 1
    r, b, n = 1.0, x, whole
    while n > 0:
        if n & 1:
            r *= b
        b *= b
        n >>= 1
    m = acc * r
    if q10 < 0:
        m = 1.0 / m
    return math.floor(m * 1000000.0)


# ---------------------------------------------------------------------------------------------- data
def load_nodes():
    gen, tier = {}, {}
    with open(NODES_CSV, newline="", encoding="utf-8") as f:
        for row in csv.DictReader(f):
            i = int(row["idx"])
            gen[i] = int(row["gen"][1:])
            tier[i] = int(row["tier"]) if row["tier"] else 0
    return gen, tier


# product_class.md §1: slot -> [(alt_id, rank, perf_mult, prc_set, requires groups)]; a group is a tuple of
# alternatives (any_of); slot order = data order, engine first.
SLOTS = [
    ("engine", [
        ("eng_g1", 1, "1.0", [54, 44], [(17,)]),
        ("eng_g2", 2, "1.3", [54, 44], [(19,)]),
        ("eng_g3", 3, "1.7", [54, 44], [(23,)]),
        ("eng_g4", 4, "2.2", [54, 44], [(28,)]),
    ]),
    ("hot_section", [
        ("hot_nicr_forged", 1, "1.0", [39, 45, 49], [(30,), (33,)]),
        ("hot_sa2_forged", 2, "1.1", [40, 45, 49], [(30,), (33,)]),
        ("hot_vim_cast", 3, "1.25", [41, 46, 51], [(34,)]),
        ("hot_vim_ds_cooled", 4, "1.45", [41, 43, 52], [(35,)]),
    ]),
    ("compressor", [
        ("comp_centrifugal", 1, "1.0", [48], []),
        ("comp_axial", 2, "1.15", [50], [(36,)]),
    ]),
    ("airframe", [
        ("af_straight", 1, "1.0", [38, 53], [(18,)]),
        ("af_swept", 2, "1.3", [38, 53, 55], [(20,)]),
        ("af_milled", 3, "1.5", [38, 53, 55, 56], [(24, 25)]),
        ("af_titanium", 4, "1.6", [38, 53, 55, 56, 42], [(24, 25), (32,)]),
    ]),
    ("afterburner", [
        ("ab_none", 1, "1.0", [], []),
        ("ab_on", 2, "1.15", [57], [(21,), (37,)]),
    ]),
]
LIGHTEN_ORDER = ["hot_section", "compressor", "afterburner", "airframe"]  # F-PC2

# product_inputs.md §4 (r3): base_cost_p raw, labour_p h; n_p = 1 (R-NP).
BASE_COST = {38: 75, 53: 462, 55: 431, 56: 937, 42: 843, 44: 18, 54: 168, 39: 28, 40: 75, 41: 225,
             45: 28, 46: 150, 49: 93, 51: 281, 43: 468, 52: 656, 48: 56, 50: 150, 57: 112}
# product_inputs.md §6 (r3), per generation g = 1..4.
GEN = {
    1: dict(h_first=9320, h_floor=2330, materials="0.000217", proc_scale="0.942188", design_scale="1.00"),
    2: dict(h_first=14760, h_floor=3690, materials="0.000329", proc_scale="0.858987", design_scale="1.11"),
    3: dict(h_first=41960, h_floor=10490, materials="0.000919", proc_scale="1.021118", design_scale="1.82"),
    4: dict(h_first=125920, h_floor=31480, materials="0.002098", proc_scale="2.597030", design_scale="3.12"),
}
WAGE_KH = fx("0.000125")          # A28
MAINTAINABILITY = fx("1.0")
APP = {1: 58, 2: 59, 3: 60, 4: 61}

# §8.1 (v0.10)
P = dict(
    lr_q10=330, d0=fi(20), lambda_d=fx("0.0087"), beta_q10=512, psi_d=fx("0.5"), fix_hours=fi(2000),
    upkeep_share=fx("0.004"), alpha_u_q10=512, service_life=180, W0=fi(400), E_B=fi(3), test_rate=fi(200),
    y_prod=fx("0.5"), B0=500, h_pm=fi(175),
)


# ---------------------------------------------------------------------------------- design selection
def alt_ok(alt, gen_cap, knows, node_gen):
    """Available for a design of generation gen_cap (§1.4 v0.10, A31): all PRC and one member of every requires
    group known and of node generation <= gen_cap."""
    _, rank, _, prc, groups = alt
    ok = lambda n: knows(n) and node_gen[n] <= gen_cap
    return all(ok(p) for p in prc) and all(any(ok(n) for n in g) for g in groups)


def engine_ok(alt, g):
    return alt[1] == g


def superior(g, knows, node_gen):
    alts = []
    for slot, options in SLOTS:
        best = None
        for alt in options:
            if slot == "engine" and not engine_ok(alt, g):
                continue
            if alt_ok(alt, g, knows, node_gen):
                best = alt
        if best is None:
            return None
        alts.append(best)
    return alts


def bottleneck_yield(alt, yields):
    return min([yields(p) for p in alt[3]] + [S])


def producible(g, knows, node_gen, yields, tier):
    sup = superior(g, knows, node_gen)
    if sup is None:
        return None, "none"
    alts = []
    for i, (slot, options) in enumerate(SLOTS):
        if slot == "engine":
            alts.append(sup[i])
            continue
        avail = [a for a in options if alt_ok(a, g, knows, node_gen)]
        passing = [a for a in avail if bottleneck_yield(a, yields) >= P["y_prod"]]
        alts.append(passing[-1] if passing else avail[0])   # A32: fallback = lightest available
    if prc_set(alts) != prc_set(sup):
        return alts, "own"
    best_i, best_tier, best_lower = None, -1, None
    for slot in LIGHTEN_ORDER:
        i = [s for s, _ in SLOTS].index(slot)
        options = SLOTS[i][1]
        cur = options.index(alts[i])
        lower = [a for a in options[:cur] if alt_ok(a, g, knows, node_gen)]
        t = max([tier[p] for p in alts[i][3]] + [0])
        if lower and t > best_tier:
            best_i, best_tier, best_lower = i, t, lower[-1]
    if best_i is None:
        return sup, "single"    # F-PC3: one variant
    alts = list(alts)
    alts[best_i] = best_lower
    return alts, "lightened"


def canonical(g, knows, node_gen, yields):
    """L_P (§6.1 v0.10): best alternative of generation <= g whose PRC are all admitted (yield > 0), else lower."""
    alts = []
    for slot, options in SLOTS:
        pick = None
        for alt in options:
            if slot == "engine" and not engine_ok(alt, g):
                continue
            if alt_ok(alt, g, knows, node_gen) and all(yields(p) > 0 for p in alt[3]):
                pick = alt
        if pick is None:
            return None
        alts.append(pick)
    return alts


def prc_set(alts):
    return sorted({p for a in alts for p in a[3]})


def complexity(prc, tier):
    return sum(tier[p] for p in prc)


def perf(alts):
    v = fx(alts[0][2])
    for a in alts[1:]:
        v = mul(v, fx(a[2]))
    return v


def base_sum(prc):
    return sum(BASE_COST[p] for p in prc)   # n_p = 1


# ---------------------------------------------------------------------------------------- formulas
def assembly_hours(e, g):
    hf, hl = fi(GEN[g]["h_first"]), fi(GEN[g]["h_floor"])
    return hl + mul(hf - hl, pow_frac(S + e, -P["lr_q10"]))


def assembly_cost(hours):
    return (hours * WAGE_KH) // (S * 1000)       # A28: one rounding


def process_part(bsum, ucm, g):
    return mul(mul(bsum, ucm), fx(GEN[g]["proc_scale"]))       # A33: ((Σ n·base)·unit_cost_mult)·proc_scale


def unit_cost(e, g, bsum, ucm=S):
    return fx(GEN[g]["materials"]) + assembly_cost(assembly_hours(e, g)) + process_part(bsum, ucm, g)


def unit_cost_floor(g, bsum):
    return fx(GEN[g]["materials"]) + assembly_cost(fi(GEN[g]["h_floor"])) + process_part(bsum, S, g)


def upkeep(floor, cx):
    # A33: ((unit_cost_floor·PowFrac(complexity, q10_αu))·share)/maintainability
    return div(mul(mul(floor, pow_frac(fi(cx), P["alpha_u_q10"])), P["upkeep_share"]), MAINTAINABILITY)


def design_work(cx, g, exp):
    return div(mul(mul(P["W0"], fi(cx)), fx(GEN[g]["design_scale"])), S + div(exp, P["E_B"]))


def initial_defects(novelty, cx, exp):
    return div(mul(mul(P["d0"], novelty), fi(cx)), S + div(exp, P["E_B"]))


def detected(t_hours, d0):
    # §3.5 A31: detected = D0·min(1, λ_D·PowFrac(T, q10_β)), T cumulative
    frac = mul(P["lambda_d"], pow_frac(t_hours, P["beta_q10"]))
    return mul(d0, min(S, frac))


def readiness(paid_ratio, q, d, d0):
    r = mul(min(S, paid_ratio), q)
    return r if d0 <= 0 else mul(r, S - div(mul(P["psi_d"], d), d0))


def bureau_req(g, kappa_q10):
    # §3.3: ⌊B0·design_scale^κ⌋, computed once at manifest build (params, not mod data)
    return (P["B0"] * pow_frac(fx(GEN[g]["design_scale"]), kappa_q10)) // S


def cohort_retire_month(year):
    # A32: a (design, year) cohort ages from July of its year: m0 = 12·(Y − 1945) − 2 (1945-09 = month 0)
    return 12 * (year - 1945) - 2 + P["service_life"]


# ------------------------------------------------------------------------------------------- cases
def build():
    node_gen, tier = load_nodes()
    all_upto = lambda g: (lambda n: node_gen[n] <= g)
    know_all = lambda n: True
    yield_all = lambda p: S
    out = {"spec": "slice-jet-aviation.md v0.10", "params": {k: v for k, v in P.items()}}

    designs = {}
    for g in range(1, 5):
        sup = superior(g, all_upto(g), node_gen)
        tech, kind = producible(g, all_upto(g), node_gen, yield_all, tier)
        rows = {}
        for name, alts in (("superior", sup), ("producible", tech)):
            prc = prc_set(alts)
            cx = complexity(prc, tier)
            bs = base_sum(prc)
            fl = unit_cost_floor(g, bs)
            rows[name] = {
                "alts": [a[0] for a in alts], "prc_set": prc, "complexity": cx, "perf": perf(alts),
                "base_cost_sum": bs, "process_part": process_part(bs, S, g),
                "unit_cost_by_E": {str(e): unit_cost(fi(e), g, bs) for e in (0, 1, 10, 100, 1000, 10000)},
                "assembly_hours_by_E": {str(e): assembly_hours(fi(e), g) for e in (0, 1, 10, 100, 1000)},
                "unit_cost_floor": fl, "upkeep": upkeep(fl, cx),
                "W_exp0": design_work(cx, g, 0), "W_exp_g_minus_1": design_work(cx, g, fi(g - 1)),
                "D0_novelty1_exp0": initial_defects(S, cx, 0),
            }
        rows["producible_kind"] = kind
        rows["bureau_req"] = {"kappa0": bureau_req(g, 0), "kappa0.5": bureau_req(g, 512),
                              "kappa1": bureau_req(g, 1024)}
        designs["G%d" % g] = rows
    out["designs_know_all_up_to_g"] = designs

    # F2 / A31: generation cap — an actor knowing every node orders G1 and G2; the design must equal the
    # reference of that generation (no eng_g3/g4, no hot_vim_*), and the L_P canonical project the same.
    cap = {}
    for g in (1, 2, 3):
        sup = superior(g, know_all, node_gen)
        can = canonical(g, know_all, node_gen, yield_all)
        cap["G%d" % g] = {"superior": [a[0] for a in sup], "canonical": [a[0] for a in can],
                          "equals_reference": [a[0] for a in sup] == designs["G%d" % g]["superior"]["alts"]}
    # A32 fallback: G2 producible when no hot-section and compressor alternative reaches y_prod
    low = {40: fx("0.3"), 39: fx("0.2"), 45: fx("0.9"), 49: fx("0.9"), 50: fx("0.1"), 48: fx("0.4")}
    tech, kind = producible(2, all_upto(2), node_gen, lambda p: low.get(p, S), tier)
    cap["G2_producible_fallback"] = {"alts": [a[0] for a in tech], "kind": kind}
    # L_P: G2 with superalloy melt (40) not admitted -> hot section falls to nicr
    can = canonical(2, all_upto(2), node_gen, lambda p: 0 if p == 40 else S)
    cap["G2_canonical_40_not_admitted"] = [a[0] for a in can]
    out["generation_cap"] = cap

    # L_P price (A33): ((Σ n·base)·unit_cost_mult)·proc_scale, unit_cost_mult = 1.25
    bs2 = designs["G2"]["superior"]["base_cost_sum"]
    out["lp_price_G2_ucm_1.25"] = process_part(bs2, fx("1.25"), 2)

    # Detection (A31): months of tests only (test_rate h/month) until found ≥ share·D0, D0 = 580 (G1, novelty 1)
    d0 = designs["G1"]["superior"]["D0_novelty1_exp0"]
    months = {}
    for share in ("0.30", "0.45", "0.60", "1.00"):
        t, m = 0, 0
        while detected(t, d0) < mul(d0, fx(share)) and m < 1000:
            m += 1
            t += P["test_rate"]
        months[share] = m
    det = {"D0": d0, "months_tests_only_to_share": months,
           "by_hours": {str(h): detected(fi(h), d0) for h in (0, 200, 1200, 2400, 4800, 10000, 13300)}}
    # tests 24 months, then a series of 20 units/month, 20 flight h per unit and month, k = 1
    t, units, trace = fi(24 * 200), 0, []
    for m in range(1, 13):
        units += 20
        t += P["test_rate"] + units * fi(20)
        trace.append(detected(t, d0))
    det["series_20_per_month_after_24m"] = trace
    out["detection"] = det

    out["readiness"] = {
        "new_design_D_eq_D0": readiness(S, S, d0, d0),
        "half_open": readiness(S, fx("0.9"), d0 // 2, d0),
        "unpaid_0.8_no_defects": readiness(fx("0.8"), S, 0, d0),
        "start_fleet_D0_0": readiness(S, S, 0, 0),
    }
    out["cohort_retire_month"] = {str(y): cohort_retire_month(y) for y in (1945, 1946, 1960)}
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    args = ap.parse_args()
    text = json.dumps(build(), indent=1, sort_keys=True, ensure_ascii=False) + "\n"
    if args.check:
        try:
            with open(OUT, encoding="utf-8") as f:
                old = f.read()
        except FileNotFoundError:
            print("products.json missing", file=sys.stderr)
            return 1
        if old != text:
            print("out/products.json differs from a fresh run", file=sys.stderr)
            return 1
        print("products: OK")
        return 0
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        f.write(text)
    print("wrote", OUT)
    return 0


if __name__ == "__main__":
    sys.exit(main())
