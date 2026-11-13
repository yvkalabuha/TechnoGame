"""RISK-0019 (TASK-0041): price fall per doubling by channel on the T8 reference line, in doubles.

Mirrors tests/Singularity.Core.Tests/Products/ProductsChannelBudgetTests.cs (G2 'superior', GBR-like site g = 1,
constant pace, E = X = cum) with numbers dumped from the core mod (ProductsLayout/IndustryParams, 2026-11-13).
Formulas: ramp (spec §3.5, IndustryMath.Ramp), unit_cost_mult = Σ n·base/yc / Σ n·base (contract §4.2),
unit_cost = materials + assembly_hours·wage + process part (contract §4.4), assembly_hours (contract §4.4).
Python stdlib only. Usage: python3 -I lab/analysis/risk19_yield_channel.py
"""
import math

LAMBDA0, EPS, B = 0.3, 0.01, 330 / 1024          # IndustryParams.Lambda0, Eps; ProductsParams.LearningQ10 (q10)
H_FIRST, H_FLOOR, WAGE_KH, MAT = 125920.0, 31480.0, 0.000125, 0.002098   # af_class 4 card
SCALE_PU, SCALE_AF = 0.827491, 3.754003
XH = {3: 200.0, 4: 800.0, 5: 2500.0}
PU = [(5, .000225), (5, .000468), (3, .000018), (4, .000150), (5, .000656), (4, .000168), (4, .000112)]  # (tier, n·base)
AF = [(3, .000075), (5, .000843), (3, .000462), (4, .000431), (4, .000937)]


def ramp(x, xh):
    return LAMBDA0 + (1 - LAMBDA0) * x / (x + xh)


def u(block, x, xs):
    return sum(w / max(ramp(x, xs * XH[t]), EPS) for t, w in block) / sum(w for _, w in block)


def cost(n, yc, ac, xs=1.0, b=B, hfl=H_FLOOR):
    x = n if yc else 0.0
    bb = b if ac else 0.0
    hours = hfl + (H_FIRST - hfl) * (1 + n) ** (-bb)
    proc = sum(w for _, w in PU) * u(PU, x, xs) * SCALE_PU + sum(w for _, w in AF) * u(AF, x, xs) * SCALE_AF
    return MAT + hours * WAGE_KH / 1000 + proc


def channels(n, **kw):
    d = lambda y, a: math.log(cost(n, y, a, **kw) / cost(2 * n, y, a, **kw))
    full, none = d(True, True), d(False, False)
    yc = .5 * ((full - d(False, True)) + (d(True, False) - none))
    ac = .5 * ((full - d(True, False)) + (d(False, True) - none))
    return full, yc, ac


def mature_min(xs):        # stand: min process yield >= 0.9 (on the doubling grid)
    n = 1
    while min(ramp(n, xs * XH[t]) for t, _ in PU + AF) < 0.9:
        n *= 2
    return n


def mature_profile(xs):    # alternative: 1/unit_cost_mult of the whole prc_set >= 0.9
    n = 1
    w = PU + AF
    while sum(c for _, c in w) / sum(c / ramp(n, xs * XH[t]) for t, c in w) < 0.9:
        n *= 2
    return n


def row(n, **kw):
    full, yc, ac = channels(n, **kw)
    return f"N={n:>6}: fall {1 - math.exp(-full):6.1%}  yield {yc / (yc + ac):4.0%}  assembly {ac / (yc + ac):4.0%}"


if __name__ == "__main__":
    print("A. Stand reproduction (x_scale 1)")
    for n in (1, 2, 4, 25, 50, 100, mature_min(1), 2 * mature_min(1)):
        print("  " + row(n))
    print("B. Mature points: min-process vs profile definition, x_scale 0.5/1/2")
    for xs in (0.5, 1.0, 2.0):
        for name, n in (("min", mature_min(xs)), ("profile", mature_profile(xs))):
            print(f"  xs={xs} {name:7} " + row(n, xs=xs))
    print("C. Cost composition at N = 16384 (x_scale 1)")
    n = 16384
    asm_ex = (H_FIRST - H_FLOOR) * (1 + n) ** -B * WAGE_KH / 1000
    print(f"  unit_cost {cost(n, True, True):.6f}; materials {MAT:.6f}; assembly floor {H_FLOOR * WAGE_KH / 1000:.6f};"
          f" assembly above floor {asm_ex:.6f}; process part {cost(n, True, True) - MAT - H_FLOOR * WAGE_KH / 1000 - asm_ex:.6f}")
    print("D. Largest N with yield share <= 20% (x_scale 1, doubling grid), and yield share where min ramp >= 0.9")
    last = max((2 ** k for k in range(0, 21) if (lambda f, y, a: y / (y + a))(*channels(2 ** k)) <= .2), default=None)
    print(f"  last N with share<=20%: {last}; min ramp there {min(ramp(last, XH[t]) for t, _ in PU + AF):.3f}" if last else "  none")
    print("E. Sensitivity at the stand's mature point: h_floor -> 0 (pure Wright), b x1.5")
    m = mature_min(1)
    print("  h_floor=0:    " + row(m, hfl=0.0))
    print("  b=0.48:       " + row(m, b=1.5 * B))
    print("  both:         " + row(m, hfl=0.0, b=1.5 * B))
    print("F. Upper bound of the total fall per doubling: pure Wright on all of unit_cost = 1 - 2^-b =", f"{1 - 2 ** -B:.1%}")


def cost_x0(n, yc, ac, k):
    """Variant G: processes start at X0 = k·X½(tier) (inherited from earlier lines), line_E starts at 0; X = X0 + n."""
    def ub(block, x_add):
        return sum(w / max(ramp(k * XH[t] + x_add, XH[t]), EPS) for t, w in block) / sum(w for _, w in block)
    xa = n if yc else 0.0
    hours = H_FLOOR + (H_FIRST - H_FLOOR) * (1 + n) ** (-(B if ac else 0.0))
    proc = sum(w for _, w in PU) * ub(PU, xa) * SCALE_PU + sum(w for _, w in AF) * ub(AF, xa) * SCALE_AF
    return MAT + hours * WAGE_KH / 1000 + proc


def row_x0(n, k):
    d = lambda y, a: math.log(cost_x0(n, y, a, k) / cost_x0(2 * n, y, a, k))
    full, none = d(True, True), d(False, False)
    yc = .5 * ((full - d(False, True)) + (d(True, False) - none))
    ac = .5 * ((full - d(True, False)) + (d(False, True) - none))
    return f"N={n:>5}: fall {1 - math.exp(-full):6.1%}  yield {yc / (yc + ac):4.0%}  assembly {ac / (yc + ac):4.0%}"


if __name__ == "__main__":
    print("G. Line on already mature processes: X0 = k·X½ (k = 6 => every ramp = 0.90 at line start; k = 3 = scenario mastered_history)")
    for k in (3, 6):
        for n in (1, 4, 25, 100, 400, 1000):
            print(f"  k={k} " + row_x0(n, k))
