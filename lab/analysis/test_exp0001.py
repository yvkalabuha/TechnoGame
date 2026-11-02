"""Tests for exp0001.py on synthetic CSV with known answers.

Run: python3 -m unittest discover -s lab/analysis -p 'test_*.py' -v
"""
import csv
import io
import math
import os
import sys
import tempfile
import unittest
from contextlib import redirect_stdout

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import exp0001 as X  # noqa: E402

CFG = X.DEFAULTS
C = CFG["countries"]
YEARS = list(range(1910, 1951))
BASE = {"A1": 100.0, "A2": 50.0, "A3": 40.0, "A4": 30.0, "A5": 20.0, "A6": 10.0}
STATE = {"K": 100.0, "A": 100.0, "T": 100.0, "P": 100.0, "R": 3.0, "E": 3.0, "N": 100.0,
         "B": 0.0, "eng_binding_months": 0.0, "shock": 0.0}


def order_power(order):
    """Static power: first in order gets 100, then 90, 80, ..."""
    return {c: 100.0 - 10.0 * i for i, c in enumerate(order)}


class Spec:
    def __init__(self, rid, seed, series="main", model="A", nu=1.0, beta=0.625, gamma=1.0,
                 power=None, overrides=None, state=None, mult=None, milestones=None):
        self.rid, self.seed, self.series, self.model = rid, seed, series, model
        self.nu, self.beta, self.gamma = nu, beta, gamma
        self.power = dict(power or BASE)
        self.overrides = overrides or {}          # (year, country) -> Y_year
        self.state = state or {}                  # country -> dict of state overrides (all years)
        self.mult = mult or {}                    # country -> dict mult_*
        self.milestones = milestones or []        # (country, j, m_K, m_T)


def write_dataset(d, specs):
    with open(os.path.join(d, "runs.csv"), "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(X.RUNS_COLS)
        for s in specs:
            w.writerow([s.rid, s.series, s.model, s.nu, s.beta, s.gamma, 100, s.seed])
    with open(os.path.join(d, "yearly.csv"), "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(X.YEARLY_COLS)
        for s in specs:
            for y in YEARS:
                for c in C:
                    st = dict(STATE)
                    st.update(s.state.get(c, {}))
                    yv = s.overrides.get((y, c), s.power[c])
                    w.writerow([s.rid, y, c, st["K"], st["A"], st["T"], st["P"], st["R"],
                                st["E"], st["N"], repr(yv), st["B"], st["eng_binding_months"],
                                st["shock"]])
    with open(os.path.join(d, "countries.csv"), "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(X.COUNTRIES_COLS)
        for s in specs:
            for c in C:
                m = {"mult_S": 1.0, "mult_E": 1.0, "mult_Pl": 1.0, "mult_Nl": 1.0}
                m.update(s.mult.get(c, {}))
                w.writerow([s.rid, c, m["mult_S"], m["mult_E"], m["mult_Pl"], m["mult_Nl"]])
    with open(os.path.join(d, "milestones.csv"), "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(X.MILESTONES_COLS)
        for s in specs:
            for (c, j, mk, mt) in s.milestones:
                w.writerow([s.rid, c, j, 100 * 1.25 ** j, "" if mk is None else mk, "",
                            "" if mt is None else mt])


def analyze(specs, cfg=CFG):
    with tempfile.TemporaryDirectory() as d:
        write_dataset(d, specs)
        return X.analyze(X.load(d, cfg), cfg)


def only_group(res):
    gs = res["groups"]
    assert len(gs) == 1, list(gs)
    return next(iter(gs.values()))


class TestStats(unittest.TestCase):
    def test_wilson(self):
        lo, hi = X.wilson(5, 10, 1.96)
        self.assertAlmostEqual(lo, 0.236593, places=5)
        self.assertAlmostEqual(hi, 0.763407, places=5)
        self.assertEqual(X.wilson(0, 10, 1.96)[0], 0.0)

    def test_ks(self):
        d, p = X.ks_2samp([1, 2, 3], [4, 5, 6])
        self.assertEqual(d, 1.0)
        lam = (math.sqrt(1.5) + 0.12 + 0.11 / math.sqrt(1.5))
        exp_p = 2 * sum((-1) ** (k - 1) * math.exp(-2 * k * k * lam * lam) for k in range(1, 50))
        self.assertAlmostEqual(p, exp_p, places=6)
        d, _ = X.ks_2samp([1, 1, 2], [1, 2, 2])          # ties
        self.assertAlmostEqual(d, 1 / 3)
        self.assertEqual(X.ks_2samp([1, 2], [1, 2])[0], 0.0)

    def test_quantile(self):
        self.assertEqual(X.quantile([1, 2, 3, 4], 0.5), 2.5)
        self.assertAlmostEqual(X.quantile(list(range(101)), 0.05), 5.0)

    def test_entropy(self):
        self.assertAlmostEqual(X.norm_entropy([1 / 6] * 6, 6), 1.0)
        self.assertEqual(X.norm_entropy([1.0, 0, 0, 0, 0, 0], 6), 0.0)


class TestCriterion1(unittest.TestCase):
    def specs(self):
        out = []
        for i in range(20):
            if i < 8:
                o = ["A1", "A2", "A3", "A4", "A5", "A6"]
            elif i < 16:
                o = ["A2", "A3", "A4", "A5", "A1", "A6"]
            else:
                o = ["A5", "A4", "A3", "A1", "A2", "A6"]
            out.append(Spec("r%02d" % i, i + 1, power=order_power(o)))
        return out

    def test_known_shares(self):
        g = only_group(analyze(self.specs()))
        r = g["1"]["rows"]
        self.assertEqual(r["A1"]["win"], 0.4)
        self.assertEqual(r["A2"]["win"], 0.4)
        self.assertEqual(r["A5"]["win"], 0.2)
        self.assertEqual(r["A4"]["top2"], 0.2)
        self.assertEqual(r["A6"]["top2"], 0.0)
        self.assertEqual(g["1"]["min_top2"][0], "A4")  # A6 excluded; A4/A5 tie -> index
        self.assertTrue(g["1"]["pass"])                # 0.40 <= 0.40
        self.assertTrue(g["1"]["borderline"])          # Wilson CI of 0.40 straddles 0.40

    def test_a6_not_excluded_fails(self):
        cfg = X.merge_config(CFG, {"c1": {"exclude_from_min_top2": []}})
        g = only_group(analyze(self.specs(), cfg))
        self.assertFalse(g["1"]["pass"])
        self.assertEqual(g["1"]["min_top2"], ("A6", 0.0))


class TestCriterion2Hysteresis(unittest.TestCase):
    def specs(self):
        big = {"A1": 100.0, "A2": 50.0}
        # r1: A2 leads 1920 by 2% only one year -> no change
        r1 = Spec("r1", 1, overrides={(1920, "A2"): 102.0})
        # r2: A2 leads 1920 by 10% (change), A1 leads again 1921+ by ~1% (holds >=2y: change)
        ov2 = {(1920, "A2"): 110.0}
        for y in range(1921, 1951):
            ov2[(y, "A2")] = 99.0
        r2 = Spec("r2", 2, overrides=ov2)
        # r3: A2 leads 1949-1950 by 1% -> one change (2-year hold), final leader A2
        r3 = Spec("r3", 3, overrides={(1949, "A2"): 101.0, (1950, "A2"): 101.0})
        # r4: A2 leads only 1950 by 1% -> no change, but raw final leader differs from 1910
        r4 = Spec("r4", 4, overrides={(1950, "A2"): 101.0})
        del big
        return [r1, r2, r3, r4]

    def test_changes(self):
        g = only_group(analyze(self.specs()))
        self.assertEqual([d["changes"] for d in g["der"]], [0, 2, 1, 0])
        self.assertEqual(g["2"]["median_changes"], 0.5)
        self.assertEqual(g["2"]["new_leader_share"], 0.5)   # r3, r4
        self.assertFalse(g["2"]["pass"])                    # median 0.5 < 1

    def test_margin_param(self):
        cfg = X.merge_config(CFG, {"c2": {"hysteresis_margin": 0.0, "hysteresis_years": 3}})
        g = only_group(analyze(self.specs(), cfg))
        # margin 0: every single-year lead counts
        self.assertEqual([d["changes"] for d in g["der"]], [2, 2, 1, 1])

    def test_hysteresis_final_leader(self):
        cfg = X.merge_config(CFG, {"c2": {"final_leader": "hysteresis"}})
        g = only_group(analyze(self.specs(), cfg))
        self.assertEqual(g["2"]["new_leader_share"], 0.25)  # only r3


class TestCriterion3Lag(unittest.TestCase):
    def ms(self, extra_left=False):
        m = [("A1", 0, 0, 5),        # left-censored (passed at t=0)
             ("A1", 1, 12, 72),      # lag 60
             ("A1", 2, 24, 84),      # lag 60
             ("A1", 3, 400, None),   # right-censored: 492-400 = 92
             ("A2", 9, 50, 20),      # import, excluded
             ("A3", 9, None, None)]  # never discovered
        for c in C[1:]:
            m.append((c, 1, 10, 40))  # lag 30
            if extra_left:
                m.append((c, 0, 0, 0))
        return m

    def test_ratio_and_censoring(self):
        specs = [Spec("r%d" % i, i, milestones=self.ms()) for i in range(10)]
        g = only_group(analyze(specs))["3"]
        self.assertEqual(g["median_a1"], 60)
        self.assertEqual(g["median_others"], 30)
        self.assertEqual(g["ratio"], 2.0)
        self.assertEqual(g["ci"], (2.0, 2.0))
        self.assertEqual(g["stats"], {"total": 110, "undiscovered": 10, "left": 10,
                                      "right": 10, "import": 10})
        self.assertAlmostEqual(g["left_share"], 10 / 110)
        self.assertTrue(g["pass"])
        self.assertFalse(g["borderline"])

    def test_left_censor_flag(self):
        specs = [Spec("r%d" % i, i, milestones=self.ms(extra_left=True)) for i in range(10)]
        g = only_group(analyze(specs))["3"]
        self.assertAlmostEqual(g["left_share"], 6 / 16)
        self.assertTrue(g["pass"])
        self.assertTrue(g["borderline"])       # left censoring 37.5% > 30%

    def test_bootstrap_deterministic_and_borderline(self):
        specs = []
        for i in range(30):
            lag = 30 + (i % 5) * 10              # A1 lags vary 30..70 by seed
            specs.append(Spec("r%02d" % i, i, milestones=[("A1", 1, 10, 10 + lag)] +
                              [(c, 1, 10, 40) for c in C[1:]]))
        a = only_group(analyze(specs))["3"]
        b = only_group(analyze(specs))["3"]
        self.assertEqual(a["ci"], b["ci"])
        self.assertEqual(a["ratio"], 50 / 30)
        self.assertTrue(a["pass"])
        self.assertTrue(a["ci"][0] <= 1.5 < a["ci"][1])
        self.assertTrue(a["borderline"])


class TestCriterion4(unittest.TestCase):
    def specs(self, separate=True):
        out = []
        for i in range(20):
            inside = i < 4
            o = ["A2", "A1", "A3", "A4", "A5", "A6"] if inside else \
                ["A2", "A3", "A1", "A4", "A5", "A6"]
            me = 1.15 if (inside and separate) else 0.9
            out.append(Spec("r%02d" % i, i, power=order_power(o),
                            mult={"A1": {"mult_E": me}}))
        return out

    def test_pass(self):
        g = only_group(analyze(self.specs()))["4"]
        self.assertEqual(g["share"], 0.2)
        self.assertEqual(g["features"][0]["feature"], "mult_E")
        self.assertEqual(g["features"][0]["D"], 1.0)
        self.assertLess(g["features"][0]["p"], 0.01)
        self.assertEqual(g["significant"], ["mult_E"])
        self.assertTrue(g["pass"])

    def test_no_feature(self):
        g = only_group(analyze(self.specs(separate=False)))["4"]
        self.assertEqual(g["significant"], [])
        self.assertFalse(g["pass"])


class TestCriterion5Bottlenecks(unittest.TestCase):
    B, G = 0.5, 1.0
    LN4 = math.log(4.0)

    def specs(self):
        h = 0.5 * self.LN4                                   # beta*ln4 = (1-beta)*ln4
        tech = math.log(4.0 / 3.0)                           # gamma*ln[(1+1)/(1+0.5)]
        q = 100.0 * 0.75 ** 2                                # 0.5*ln(100/q) == tech
        state = {
            "A2": {"P": 25.0, "N": 25.0},                    # CAPITAL h + RESOURCES h
            "A3": {},                                        # pure NOISE
            "A4": {"T": 50.0, "A": 50.0, "P": 25.0},         # ENGINEERS tech + CAPITAL h
            "A5": {"T": 50.0, "A": 50.0, "P": q, "N": q},    # 3 equal factors: share 2/3
            "A6": {},
        }
        lead = 1000.0
        power = {"A1": lead,
                 "A2": lead / math.exp(2 * h),
                 "A3": lead / 2.0,
                 "A4": lead / math.exp(tech + h),
                 "A5": lead / math.exp(3 * tech),
                 "A6": lead * 0.9}                           # not lagging
        return [Spec("r1", 1, beta=self.B, gamma=self.G, power=power, state=state)]

    def test_decompose_values(self):
        lead = dict(STATE, P=100.0, Y_year=1000.0)
        lag = dict(STATE, T=50.0, A=50.0, P=25.0, Y_year=1000.0 / math.exp(math.log(4 / 3) + 0.5 * self.LN4))
        gap, comp = X.decompose(lead, lag, self.B, self.G, 100.0)
        self.assertAlmostEqual(comp["ENGINEERS"], math.log(4 / 3))
        self.assertAlmostEqual(comp["CAPITAL"], 0.5 * self.LN4)
        self.assertEqual(comp["SCIENCE"], 0.0)
        self.assertEqual(comp["MACHINE_PARK"], 0.0)
        self.assertAlmostEqual(comp["NOISE"], 0.0, places=9)

    def test_negative_zeroed_and_noise(self):
        lead = dict(STATE, P=400.0, Y_year=1000.0)
        lag = dict(STATE, N=400.0, Y_year=500.0)             # resources favour the laggard
        gap, comp = X.decompose(lead, lag, 0.5, 1.0, 100.0)
        self.assertEqual(comp["RESOURCES"], 0.0)
        self.assertAlmostEqual(comp["CAPITAL"], math.log(2.0))
        self.assertAlmostEqual(comp["NOISE"], math.log(2.0))  # gap - (cap + res_signed)
        # tie CAPITAL/NOISE -> CAPITAL first, NOISE second -> unexplained
        self.assertEqual(X.rank_components(comp)[:2], ["CAPITAL", "NOISE"])

    def test_fraction_and_labels(self):
        g = only_group(analyze(self.specs()))["5"]
        n_years = 1950 - 1915 + 1
        self.assertEqual(g["n_cases"], 4 * n_years)
        self.assertEqual(g["fraction"], 0.5)                 # A2, A4 explained
        self.assertEqual(g["per"]["A2"]["explained"], 1.0)
        self.assertEqual(g["per"]["A3"]["explained"], 0.0)
        self.assertEqual(g["per"]["A3"]["labels"]["NOISE"], 1.0)
        self.assertEqual(g["per"]["A4"]["explained"], 1.0)
        self.assertEqual(g["per"]["A5"]["explained"], 0.0)
        self.assertAlmostEqual(g["per"]["A5"]["p50"], 2 / 3)
        self.assertEqual(g["per"]["A2"]["p50"], 1.0)
        self.assertNotIn("A6", g["per"])
        self.assertFalse(g["pass"])
        cfg = X.merge_config(CFG, {"c5": {"min_explained_fraction": 0.5}})
        self.assertTrue(only_group(analyze(self.specs(), cfg))["5"]["pass"])

    def test_noise_rule_param(self):
        # pure-noise case A3: share = 1.0 when NOISE allowed in top-2
        cfg = X.merge_config(CFG, {"c5": {"noise_in_top2_unexplained": False}})
        g = only_group(analyze(self.specs(), cfg))["5"]
        self.assertEqual(g["per"]["A3"]["explained"], 1.0)


class TestCriterion6Controls(unittest.TestCase):
    def diverse(self, model, n=12):
        out = []
        for i in range(n):
            o = C[i % 6:] + C[:i % 6]
            ov = {(1930, o[1]): 200.0, (1931, o[1]): 200.0}  # one hysteresis change per run
            out.append(Spec("%s%02d" % (model, i), i, model=model, power=order_power(o),
                            overrides=ov))
        return out

    def flat(self, model, n=12):
        return [Spec("%s%02d" % (model, i), i, model=model) for i in range(n)]

    def test_pass_and_missing(self):
        res = analyze(self.diverse("A") + self.flat("B"))
        g = res["groups"][("main", "A", 1.0, 0.625, 1.0)]
        self.assertAlmostEqual(g["diversity"][0], 1.0)
        self.assertEqual(g["diversity"][1], 2)              # up 1930 (2-y hold), back 1932 (>5%)
        self.assertIsNone(g["6"]["pass"])                   # C missing
        cfg = X.merge_config(CFG, {"c6": {"control_models": ["B"]}})
        g = analyze(self.diverse("A") + self.flat("B"), cfg)["groups"][("main", "A", 1.0, 0.625, 1.0)]
        self.assertTrue(g["6"]["pass"])
        self.assertEqual(g["6"]["controls"]["B"]["D"], 0.0)

    def test_c_fails(self):
        res = analyze(self.diverse("A") + self.flat("B") + self.diverse("C"))
        g = res["groups"][("main", "A", 1.0, 0.625, 1.0)]
        self.assertTrue(g["6"]["controls"]["B"]["pass"])
        self.assertFalse(g["6"]["controls"]["C"]["pass"])
        self.assertFalse(g["6"]["pass"])

    def test_grid_point_uses_main_control(self):
        specs = self.diverse("A") + self.flat("B") + self.flat("C")
        for s in self.diverse("A"):
            s.rid, s.series, s.beta = "g" + s.rid, "grid", 0.5
            specs.append(s)
        g = analyze(specs)["groups"][("grid", "A", 1.0, 0.5, 1.0)]
        self.assertTrue(g["6"]["pass"])
        self.assertEqual(g["6"]["controls"]["B"]["group"], ("main", "B", 1.0, 0.625, 1.0))


class TestCriterion7CatchUp(unittest.TestCase):
    def specs(self):
        def a6(vals):
            return {(y, "A6"): v for y, v in vals.items()}
        return [
            Spec("r1", 1, overrides=a6({1948: 45.0, 1949: 45.0, 1950: 45.0})),   # 6 -> 3: +3
            Spec("r2", 2, overrides=a6({1950: 200.0})),                          # mean 73.3 -> 2: +4
            Spec("r3", 3, overrides=a6({1930: 60.0, 1948: 25.0, 1949: 25.0, 1950: 25.0})),  # mean 26.7: 5 -> 5: 0
            Spec("r4", 4, overrides=a6({1948: 25.0, 1949: 25.0, 1950: 25.0})),   # 6 -> 5: +1
        ]

    def test_gains(self):
        g = only_group(analyze(self.specs()))
        d = g["der"]
        self.assertEqual([x["rank_from"]["A6"] - x["rank_to"]["A6"] for x in d], [3, 4, 0, 1])
        self.assertEqual(d[2]["rank_from"]["A6"], 5)          # window mean, not single 1930
        c = g["7"]
        self.assertEqual(c["share_gain1"], 0.75)
        self.assertEqual(c["share_gain2"], 0.5)
        self.assertEqual(c["median_gain"], 2.0)
        self.assertTrue(c["pass"])
        cfg = X.merge_config(CFG, {"c7": {"min_share_gain2": 0.6}})
        self.assertFalse(only_group(analyze(self.specs(), cfg))["7"]["pass"])


def fake_group(series, nu, beta, gamma, passes=None, border=(), a1_top2=0.2):
    passes = passes if passes is not None else {c: True for c in X.CRITERIA}
    g = {"key": (series, "A", nu, beta, gamma)}
    for c in X.CRITERIA:
        g[c] = {"pass": passes.get(c, True), "borderline": c in border}
    g["1"]["rows"] = {"A1": {"top2": a1_top2}}
    return g


def fake_res(nu_specs):
    """nu_specs: nu -> (main_kwargs, [grid kwargs x 9])."""
    res = {}
    for nu, (mk, grid) in nu_specs.items():
        m = fake_group("main", nu, 0.625, 1.0, **mk)
        res[m["key"]] = m
        pts = [(b, gm) for b in (0.5, 0.625, 0.75) for gm in (0.5, 0.75, 1.0)]
        for (b, gm), kw in zip(pts, grid):
            g = fake_group("grid", nu, b, gm, **kw)
            res[g["key"]] = g
    return res


class TestGridDecision(unittest.TestCase):
    FAIL3 = {"passes": {"3": False}}

    def grid(self, n_pass, extra=None):
        return [extra or {} for _ in range(n_pass)] + [self.FAIL3] * (9 - n_pass)

    def test_supports(self):
        res = fake_res({0.25: ({}, self.grid(5)), 1.0: ({}, self.grid(9))})
        d = X.decide(res, CFG)
        self.assertEqual(d["per_nu"][0.25]["n_pass"], 5)
        self.assertEqual(d["per_nu"][0.25]["outcome"], "supports")
        self.assertEqual(d["overall"], "supports")
        self.assertFalse(d["d_trigger"])

    def test_four_points_rework(self):
        res = fake_res({0.25: ({}, self.grid(4)), 1.0: ({}, self.grid(9))})
        d = X.decide(res, CFG)
        self.assertEqual(d["per_nu"][0.25]["outcome"], "rework")
        self.assertEqual(d["overall"], "rework")

    def test_borderline_majority_inconclusive(self):
        grid = [{}] * 4 + [{"border": ("4",)}] + [self.FAIL3] * 4
        res = fake_res({0.25: ({}, grid), 1.0: ({}, self.grid(9))})
        d = X.decide(res, CFG)
        self.assertEqual(d["per_nu"][0.25]["n_pass"], 5)
        self.assertEqual(d["per_nu"][0.25]["n_clean"], 4)
        self.assertEqual(d["overall"], "inconclusive")

    def test_main_borderline_inconclusive(self):
        res = fake_res({0.25: ({"border": ("1",)}, self.grid(9)), 1.0: ({}, self.grid(9))})
        self.assertEqual(X.decide(res, CFG)["overall"], "inconclusive")

    def test_refute_main_and_grid(self):
        res = fake_res({0.25: ({"passes": {"6": False}}, self.grid(9)), 1.0: ({}, self.grid(9))})
        self.assertEqual(X.decide(res, CFG)["overall"], "refutes")
        grid = [{}] * 4 + [{"passes": {"1": False}}] * 5
        res = fake_res({0.25: ({}, self.grid(9)), 1.0: ({}, grid)})
        self.assertEqual(X.decide(res, CFG)["per_nu"][1.0]["outcome"], "refutes")

    def test_main_fail_rework(self):
        res = fake_res({0.25: ({"passes": {"7": False}}, self.grid(9)), 1.0: ({}, self.grid(9))})
        d = X.decide(res, CFG)
        self.assertEqual(d["per_nu"][0.25]["main_failed"], ["7"])
        self.assertEqual(d["overall"], "rework")

    def test_d_trigger(self):
        low = {"a1_top2": 0.05}
        res = fake_res({0.25: ({}, [low] * 5 + [{}] * 4), 1.0: ({}, [low] * 4 + [{}] * 5)})
        d = X.decide(res, CFG)
        self.assertTrue(d["per_nu"][0.25]["d_trigger"])
        self.assertFalse(d["per_nu"][1.0]["d_trigger"])
        self.assertTrue(d["d_trigger"])
        self.assertEqual(d["overall"], "supports")           # flag, not a criterion

    def test_grid_min_points_param(self):
        cfg = X.merge_config(CFG, {"decision": {"grid_min_points": 6}})
        res = fake_res({0.25: ({}, self.grid(5)), 1.0: ({}, self.grid(9))})
        self.assertEqual(X.decide(res, cfg)["overall"], "rework")


class TestEndToEnd(unittest.TestCase):
    def specs(self):
        out = []
        for model in ("A", "B", "C"):
            for i in range(8):
                o = C[i % 6:] + C[:i % 6] if model == "A" else C
                out.append(Spec("%s%d" % (model, i), i, model=model, power=order_power(o),
                                milestones=[("A1", 1, 10, 100), ("A2", 1, 10, 40)]))
        return out

    def test_byte_identical_and_config(self):
        with tempfile.TemporaryDirectory() as d:
            write_dataset(d, self.specs())
            o1, o2 = os.path.join(d, "o1.md"), os.path.join(d, "o2.md")
            self.assertEqual(X.main([d, "--out", o1]), 0)
            self.assertEqual(X.main([d, "--out", o2]), 0)
            with open(o1, "rb") as f1, open(o2, "rb") as f2:
                b1 = f1.read()
                self.assertEqual(b1, f2.read())
            self.assertIn(b"| 3 | ratio=", b1)
            cfgp = os.path.join(d, "cfg.json")
            with open(cfgp, "w") as f:
                f.write('{"c1": {"max_win_share": 0.9}}')
            o3 = os.path.join(d, "o3.md")
            self.assertEqual(X.main([d, "--config", cfgp, "--out", o3]), 0)
            with open(o3, "rb") as f:
                self.assertNotEqual(f.read(), b1)              # config hash/thresholds differ

    def test_unknown_config_key(self):
        with self.assertRaises(X.InputError):
            X.merge_config(CFG, {"c1": {"max_win": 0.4}})

    def test_missing_rows(self):
        with tempfile.TemporaryDirectory() as d:
            write_dataset(d, self.specs()[:1])
            with open(os.path.join(d, "yearly.csv")) as f:
                lines = f.readlines()
            with open(os.path.join(d, "yearly.csv"), "w") as f:
                f.writelines(lines[:-1])
            with self.assertRaises(X.InputError):
                X.load(d, CFG)

    def test_dump_config(self):
        buf = io.StringIO()
        with redirect_stdout(buf):
            X.main(["--dump-config"])
        self.assertIn('"max_win_share": 0.4', buf.getvalue())


if __name__ == "__main__":
    unittest.main()
