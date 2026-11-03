#!/usr/bin/env python3
"""Explicit expected values for ProductsFormulaTests (TASK-0018), computed from the spec formulas.

Not an independent reference model (lab/reference/jet_slice/ does not cover the product layer yet): a stdlib
transcription of contract v2.3 §4.0/§4.4 and spec v0.8 §3.3, §3.5 A28 in Fx6 raw integers, floor on every * and /,
with DetMath.PowFrac ported operation by operation (ADR-0012: acc = 1, acc *= x^(2^-i) for set bits, then x^int;
negative exponent = one IEEE division). simulation-engineer should confirm the numbers or replace them with D-tests.

Usage (from repo root): python3 tests/Singularity.Core.Tests/Reference/products_refs.py
"""
import math

S = 10**6


def _pow_abs(x, whole, frac):
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
    return acc * r


def pow_fx(raw, q10):
    """DetMath.PowFracFx: floor(PowFrac(raw/1e6, q10)·1e6)."""
    k = abs(q10)
    m = _pow_abs(raw / S, k >> 10, k & 1023)
    return math.floor((m if q10 >= 0 else 1.0 / m) * S)


def mul(a, b):
    return (a * b) // S


def div(a, b):
    return (a * S) // b


def assembly_hours(e, h_first, h_floor, q10=330):
    return h_floor + mul(h_first - h_floor, pow_fx(S + e, -q10))


def assembly_cost(hours, wage_kh):
    return (hours * wage_kh) // 10**9          # spec §3.5 A28: one rounding


# G1 «превосходящий» (r3 data): Σ n_p·base_cost_p = 928 raw, proc_scale 0.942188, materials 217, h 9320/2330.
H_FIRST, H_FLOOR, WAGE, MATERIALS = 9320 * S, 2330 * S, 125, 217
PROC = mul(mul(928, S), 942188)


def main():
    print("process part G1", PROC)
    for e in [0, 1, 10, 100, 1000, 10000]:
        h = assembly_hours(e * S, H_FIRST, H_FLOOR)
        print(f"E={e}: assembly_hours {h}, unit_cost {MATERIALS + assembly_cost(h, WAGE) + PROC}")
    floor_g1 = MATERIALS + assembly_cost(H_FLOOR, WAGE) + PROC
    print("unit_cost_floor G1", floor_g1)
    print("upkeep G1 ((floor·PowFrac(29, 512))·0.004)/1", div(mul(mul(floor_g1, pow_fx(29 * S, 512)), 4000), S))
    print("detected T=10000 h, D0=580:", min(580 * S, mul(50_000, pow_fx(10_000 * S, 512))))
    print("detected T=200 h:", mul(50_000, pow_fx(200 * S, 512)))


if __name__ == "__main__":
    main()
