#!/usr/bin/env python3
"""Reference values for ToyFunnelParityTests, produced by the prototype lab/proto/model.py.

Usage (from repo root):
    python3 tests/Singularity.Core.Tests/Reference/toy_funnel_refs.py lab/proto

Prints one xUnit InlineData line per case. Digests (SHA-256 of UTF-8 lines, see below) cover
every yearly row, SETUP multiplier and milestone bit for bit:
  yearly:     "year,i,K,A,T,P,R,E,N,Y_year,B,eng_binding_months,shock_bits\n" (raw Fx6 integers,
              i = country index, shock_bits = IEEE-754 bits of the yearly mean shock as int64)
  mults:      "i,bits(mult_S),bits(mult_E),bits(mult_Pl),bits(mult_Nl)\n"
  milestones: "i,j,q_j,m_K,m_A,m_T\n" (empty field = not reached)
Countries beyond 6 cycle through the archetypes (country i uses archetype i mod 6).
"""
import hashlib
import struct
import sys

sys.path.insert(0, sys.argv[1] if len(sys.argv) > 1 else "lab/proto")
import model as M  # noqa: E402

CASES = [
    # model, seed, nu, beta, gamma, B_c_tau0, B_delta_tau, countries
    ("A", 1, 0.25, 0.625, 1.0, 1.0, 0.25, 6),
    ("A", 2, 1.0, 0.625, 1.0, 1.0, 0.25, 6),
    ("A", 7, 0.25, 0.625, 1.0, 1.0, 0.25, 6),
    ("A", 42, 1.0, 0.625, 1.0, 1.0, 0.25, 6),
    ("A", 200, 0.25, 0.625, 1.0, 1.0, 0.25, 6),
    ("A", 123456789, 1.0, 0.625, 1.0, 1.0, 0.25, 6),
    ("A", 0, 0.0, 0.625, 1.0, 1.0, 0.25, 6),
    ("A", 18446744073709551615, 1.0, 0.625, 1.0, 1.0, 0.25, 6),
    ("A", 11, 0.25, 0.5, 0.5, 1.0, 0.25, 6),
    ("A", 12, 1.0, 0.75, 0.75, 1.0, 0.25, 6),
    # B with the per-nu calibration values of the draft manifest (uncalibrated B overflows 2^53).
    ("B", 1, 0.25, 0.625, 1.0, 0.45605548915918925, 0.1080050200689584, 6),
    ("B", 3, 1.0, 0.625, 1.0, 0.4562846855958923, 0.10798051790334284, 6),
    ("B", 5, 0.25, 0.625, 1.0, 0.45605548915918925, 0.1080050200689584, 6),
    ("B", 10001, 1.0, 0.625, 1.0, 0.4562846855958923, 0.10798051790334284, 6),
    ("C", 1, 0.25, 0.625, 1.0, 1.0, 0.25, 6),
    ("C", 4, 1.0, 0.625, 1.0, 1.0, 0.25, 6),
    ("C", 99, 0.25, 0.625, 1.0, 1.0, 0.25, 6),
    ("C", 150, 1.0, 0.625, 1.0, 1.0, 0.25, 6),
    ("C", 0, 0.0, 0.625, 1.0, 1.0, 0.25, 6),
    ("A", 3, 1.0, 0.625, 1.0, 1.0, 0.25, 64),
    ("B", 9, 0.25, 0.625, 1.0, 0.45605548915918925, 0.1080050200689584, 64),
    ("C", 8, 0.25, 0.625, 1.0, 1.0, 0.25, 64),
]


def bits(x):
    return struct.unpack('<q', struct.pack('<d', x))[0]


def sha(lines):
    return hashlib.sha256("".join(lines).encode()).hexdigest()


def main():
    for model, seed, nu, beta, gamma, c0, dtau, n in CASES:
        p = dict(M.PARAMS, beta=beta, gamma=gamma, B_c_tau0=c0, B_delta_tau=dtau)
        arch = [M.ARCHETYPES[i % 6] for i in range(n)]
        r = M.simulate(model, seed, nu, p, archetypes=arch)
        yearly = []
        for k, (y, _c, K, A, T, P, R, E, N, Y, B, bind, shock) in enumerate(r["yearly"]):
            yearly.append("%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d\n"
                          % (y, k % n, K, A, T, P, R, E, N, Y, B, bind, bits(shock)))
        mults = ["%d,%d,%d,%d,%d\n" % ((i,) + tuple(bits(v) for v in m)) for i, m in enumerate(r["mults"])]
        ms = []
        for k, (_c, j, q, mk, ma, mt) in enumerate(r["milestones"]):
            ms.append("%d,%d,%d,%s,%s,%s\n" % (k // p["milestone_count"], j, q, "" if mk is None else mk,
                                               "" if ma is None else ma, "" if mt is None else mt))
        print('[InlineData("%s", %duL, %r, %r, %r, %r, %r, %d, "%s", "%s", "%s", "%s")]' % (
            model, seed, nu, beta, gamma, c0, dtau, n, r["state_hash"], sha(yearly), sha(mults), sha(ms)))


if __name__ == "__main__":
    main()
