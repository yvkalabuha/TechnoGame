"""Toy science -> engineering -> production model (HYP-0001 / EXP-0001), reference prototype.

Spec: docs/simulation/toy-science-industry-model.md. Python stdlib only.

This prototype mirrors the planned C# Core bit for bit where that is cheap:
- stocks are integers in Fx6 raw units (value * 10^6), increments are floor(dx * 10^6);
- intermediate values are IEEE-754 doubles using only + - * / and sqrt, evaluated in the
  order written here (left to right); powers go through pow_dyadic (same as Kernel.DetMath);
- the RNG is the Kernel CounterRng (tools/reference/kernel_vectors.py).
So the C# port should reproduce yearly.csv exactly; any difference is a port bug.

Every right-hand side of a tick reads only the snapshot S_t (state at the start of month t
plus Kmax_t, Amax_t); nothing inside a tick reads t+1 values.
"""
import hashlib
import math
import struct

# ---------------------------------------------------------------- Kernel mirror

M64 = (1 << 64) - 1
M32 = (1 << 32) - 1
SALTS = (0x6A09E667F3BCC908, 0xBB67AE8584CAA73B, 0x3C6EF372FE94F82B,
         0xA54FF53A5F1D36F1, 0x510E527FADE682D1)
TWO_M53 = 2.0 ** -53
SCALE = 1_000_000

SYS_SETUP, SYS_SCIENCE, SYS_PRODUCTION = 0, 1, 2


def mix(x):
    z = (x + 0x9E3779B97F4A7C15) & M64
    z = ((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9) & M64
    z = ((z ^ (z >> 27)) * 0x94D049BB133111EB) & M64
    return z ^ (z >> 31)


def unit(seed, tick, system, entity, n):
    """CounterRng.NextUnit: uniform double in [0, 1)."""
    h = mix((seed & M64) ^ SALTS[0])
    h = mix(h ^ (tick & M64) ^ SALTS[1])
    h = mix(h ^ (system & M32) ^ SALTS[2])
    h = mix(h ^ (entity & M32) ^ SALTS[3])
    return (mix(h ^ (n & M32) ^ SALTS[4]) >> 11) * TWO_M53


def to_raw(x):
    """Fx6.FromDouble: floor(x * 10^6); non-finite values are an error."""
    if not math.isfinite(x):
        raise ArithmeticError("non-finite value %r" % x)
    return math.floor(x * SCALE)


def fx(raw):
    return raw / SCALE


def pow_int(x, n):
    """Kernel.DetMath.PowInt: square-and-multiply for n >= 0."""
    result = 1.0
    b = x
    while n > 0:
        if n & 1:
            result *= b
        b *= b
        n >>= 1
    return result


def pow_dyadic(x, eighths):
    """Kernel.DetMath.PowDyadic: x^(eighths/8) for a finite x >= 0 (ADR-0012 order).

    acc = 1; root_i = x^(2^-i) by repeated sqrt; acc *= root_i for the set fraction bits by
    ascending i (1/2, 1/4, 1/8), stopping after the lowest set bit; then acc * x^int.
    A negative exponent is one division 1 / x^|e|. Non-finite results are an error.
    """
    if not math.isfinite(x) or x < 0:
        raise ArithmeticError("pow_dyadic base %r" % x)
    if x == 0 and eighths < 0:
        raise ArithmeticError("pow_dyadic 0^%d/8" % eighths)
    k = abs(eighths)
    frac = (k & 7) << 7  # PowFrac q10 fraction bits
    acc = 1.0
    root = x
    bit = 512
    while frac != 0:
        root = math.sqrt(root)
        if frac & bit:
            acc *= root
            frac &= ~bit
        bit >>= 1
    mag = acc * pow_int(x, k >> 3)
    if not math.isfinite(mag):
        raise ArithmeticError("pow_dyadic overflow %r^(%d/8)" % (x, eighths))
    if eighths >= 0:
        return mag
    rec = 1.0 / mag
    if not math.isfinite(rec):
        raise ArithmeticError("pow_dyadic overflow %r^(%d/8)" % (x, eighths))
    return rec


def state_hash(schema_version, tick, n_rows, columns):
    data = struct.pack('<iqi', schema_version, tick, n_rows)
    for fmt, values in columns:
        data += b''.join(struct.pack('<' + fmt, v) for v in values)
    return hashlib.sha256(data).hexdigest()


# ---------------------------------------------------------------- parameters

LEVELS = {"l": 0.6, "m": 1.0, "h": 1.6}

# HYP-0001 rev 2 table. edu: (rho_R level, rho_E level); "sched" = 0.6 -> 1.6 over 1910-1940.
ARCHETYPES = [
    {"id": "A1", "S": "h", "E": "l", "Pl": "m", "Nl": "m", "edu": ["h", "l"], "iota": 0.6},
    {"id": "A2", "S": "l", "E": "m", "Pl": "h", "Nl": "m", "edu": ["m", "m"], "iota": 0.6},
    {"id": "A3", "S": "m", "E": "h", "Pl": "m", "Nl": "l", "edu": ["l", "h"], "iota": 0.6},
    {"id": "A4", "S": "l", "E": "m", "Pl": "m", "Nl": "h", "edu": ["l", "m"], "iota": 0.6},
    {"id": "A5", "S": "m", "E": "m", "Pl": "m", "Nl": "m", "edu": ["m", "m"], "iota": 0.6},
    {"id": "A6", "S": "l", "E": "l", "Pl": "l", "Nl": "m", "edu": ["sched", "sched"], "iota": 0.96},
]

PARAMS = {
    "n_ticks": 492, "dt": 1.0 / 12.0, "start_year": 1910,
    "delta_K": 0.25, "p0": 0.006, "g0": 0.04, "sigma0": 0.10, "mu": 0.03,
    "eta": 4.0, "theta": 0.25, "delta_A": 0.02,
    "beta": 0.625, "gamma": 1.0, "t_ref": 100.0, "z": 1.0,
    "kappa": 0.05, "delta_P": 0.05, "w": 0.5, "rho0": 0.12,
    "s_R0": 0.02, "s_E0": 0.02, "s_P0": 0.12,
    "setup_mult_lo": 0.85, "setup_mult_span": 0.30,
    "edu_sched": [0.6, 1.6, 360],           # from, to, months (1910-01 .. 1940-01)
    "milestone_q0": 100, "milestone_count": 24,  # q_j = 100*(5/4)^j, j = 0..23
    # control B (single tau): tau0 = c_tau0 * K0, science productivity delta_tau
    "B_c_tau0": 1.0, "B_delta_tau": 0.25,
    # control C (fixed lag): T(t) = psi * K(t - L), K(s < 0) = K0
    "C_lag_months": 120, "C_psi": 0.45,
}


def milestone_ladder(p):
    q = [p["milestone_q0"] * SCALE]
    for _ in range(p["milestone_count"] - 1):
        q.append(q[-1] * 5 // 4)
    return q


def eighths(x):
    k = x * 8.0
    if k != math.floor(k) or k < 0:
        raise ValueError("exponent %r is not a non-negative multiple of 1/8" % x)
    return int(k)


# ---------------------------------------------------------------- simulation

def simulate(model, seed, nu, p=None, archetypes=None, keep_milestones=True):
    """Run one 1910-1950 world. Returns dict with yearly rows, mults, milestones, hash."""
    p = dict(PARAMS if p is None else p)
    arch = ARCHETYPES if archetypes is None else archetypes
    n = len(arch)
    dt = p["dt"]
    b8, g8 = eighths(p["beta"]), eighths(p["gamma"])
    t_ref, z = p["t_ref"], p["z"]
    ladder = milestone_ladder(p)
    J = len(ladder)

    # ---- SETUP (system 0, tick 0, entity i, n = 0..3 -> S, E, Pl, Nl)
    mults, lv = [], []
    for i, a in enumerate(arch):
        m = [p["setup_mult_lo"] + p["setup_mult_span"] * unit(seed, 0, SYS_SETUP, i, k)
             for k in range(4)]
        mults.append(m)
        lv.append([LEVELS[a["S"]] * m[0], LEVELS[a["E"]] * m[1],
                   LEVELS[a["Pl"]] * m[2], LEVELS[a["Nl"]] * m[3]])
    s_R = [p["s_R0"] * L[0] for L in lv]
    s_E = [p["s_E0"] * L[1] for L in lv]
    s_P = [p["s_P0"] * (0.5 + 0.5 * L[2]) for L in lv]
    iota = [a["iota"] for a in arch]
    N = [to_raw(100.0 * L[3]) for L in lv]
    K = [to_raw(100.0 * L[0]) for L in lv]
    R = [to_raw(3.0 * L[0]) for L in lv]
    E = [to_raw(3.0 * L[1]) for L in lv]
    A = [min(to_raw(60.0 * L[1]), K[i]) for i, L in enumerate(lv)]
    T = [to_raw(fx(A[i]) * (0.5 + 0.25 * L[2])) for i, L in enumerate(lv)]
    P = [to_raw(100.0 * L[2]) for L in lv]
    B = [0] * n
    if model == "B":
        K = [to_raw(p["B_c_tau0"] * fx(k)) for k in K]
        A = list(K)
        T = list(K)
    elif model == "C":
        K_hist = [list(K) for _ in range(1)]  # K_hist[t] = K at start of month t
        A = [to_raw(p["C_psi"] * fx(k)) for k in K]
        T = list(A)
    elif model != "A":
        raise ValueError("unknown model %r" % model)
    delta_sci = p["B_delta_tau"] if model == "B" else p["delta_K"]

    def rho(i, which, t):
        e = arch[i]["edu"][which]
        if e == "sched":
            lo, hi, months = p["edu_sched"]
            lvl = lo + (hi - lo) * (min(t, months) / months)
        else:
            lvl = LEVELS[e]
        return p["rho0"] * lvl

    # milestones: first month index whose end-state reaches q_j; initial state -> 0
    mK = [[None] * J for _ in range(n)]
    mA = [[None] * J for _ in range(n)]
    mT = [[None] * J for _ in range(n)]

    def mark(t, i):
        for arr, x in ((mK, K[i]), (mA, A[i]), (mT, T[i])):
            row = arr[i]
            for j in range(J):
                if x < ladder[j]:
                    break
                if row[j] is None:
                    row[j] = t

    for i in range(n):
        mark(0, i)

    Kmax, Amax = max(K), max(A)
    yearly = []
    y_acc = [0] * n
    bind_acc = [0] * n
    shock_acc = [0.0] * n
    sqrt = math.sqrt

    for t in range(p["n_ticks"]):
        nK, nA, nT, nP, nR, nE = list(K), list(A), list(T), list(P), list(R), list(E)
        xKmax, xAmax = fx(Kmax), fx(Amax)
        for i in range(n):
            xK, xA, xT, xP, xR, xE, xN = fx(K[i]), fx(A[i]), fx(T[i]), fx(P[i]), fx(R[i]), fx(E[i]), fx(N[i])
            # 1. output
            u1 = unit(seed, t, SYS_PRODUCTION, i, 0)
            shock = nu * p["sigma0"] * (2.0 * u1 - 1.0)
            Y = z * pow_dyadic(1.0 + xT / t_ref, g8) * pow_dyadic(xP, b8) * pow_dyadic(xN, 8 - b8) * (1.0 + shock)
            Yr = to_raw(Y)
            # 2. budget (exact in raw units)
            spR = math.floor(s_R[i] * Yr)
            spE = math.floor(s_E[i] * Yr)
            I = math.floor(s_P[i] * Yr)
            C = Yr - spR - spE - I
            if C < 0:
                raise AssertionError("negative consumption")
            # 3. science (model B: staff H = R + E)
            xS = xR + xE if model == "B" else xR
            dK = dt * (delta_sci * sqrt(xS) * sqrt(xK) + p["mu"] * iota[i] * max(0.0, xKmax - xK))
            u2 = unit(seed, t, SYS_SCIENCE, i, 0)
            if nu > 0.0:
                prob = min(0.5, p["p0"] * sqrt(xS) / nu)
                if u2 < prob:
                    dK = dK + nu * p["g0"] * xK
                    B[i] += 1
            nK[i] = K[i] + to_raw(dK)
            # 4. engineering (model A only)
            binding = False
            if model == "A":
                F = max(K[i], to_raw(iota[i] * xAmax))
                G = fx(max(0, F - A[i]))
                cap_e = p["eta"] * xE
                cap_g = p["theta"] * G
                binding = cap_e < cap_g
                dA = dt * (min(cap_e, cap_g) - p["delta_A"] * xA)
                nA[i] = min(max(A[i] + to_raw(dA), 0), F)
            # 5. capital and machine park (vintage); new capital carries A_t
            inv = p["kappa"] * fx(I)
            dep = p["delta_P"] * dt * xP
            xP1 = xP + (inv - dep)
            nP[i] = P[i] + to_raw(inv - dep)
            if model == "A":
                nT[i] = T[i] + to_raw(inv * (xA - xT) / xP1)
            # 6. staff
            nR[i] = R[i] + to_raw(rho(i, 0, t) * dt * (fx(spR) / p["w"] - xR))
            nE[i] = E[i] + to_raw(rho(i, 1, t) * dt * (fx(spE) / p["w"] - xE))
            # accumulators
            y_acc[i] += Yr
            bind_acc[i] += 1 if binding else 0
            shock_acc[i] += shock

        if model == "B":
            nA, nT = list(nK), list(nK)
        elif model == "C":
            K_hist.append(nK)
            src = K_hist[t + 1 - p["C_lag_months"]] if t + 1 - p["C_lag_months"] >= 0 else K_hist[0]
            nA = [to_raw(p["C_psi"] * fx(k)) for k in src]
            nT = list(nA)

        # ---- post (sequential): invariants, aggregates, milestones, yearly metrics
        for i in range(n):
            if min(nK[i], nA[i], nT[i], nP[i], nR[i], nE[i]) < 0:
                raise AssertionError("negative stock t=%d i=%d" % (t, i))
            if nK[i] < K[i]:
                raise AssertionError("K decreased t=%d i=%d" % (t, i))
            if model == "A":
                if nA[i] > max(K[i], to_raw(iota[i] * xAmax)):
                    raise AssertionError("A > F t=%d i=%d" % (t, i))
                lo, hi = min(T[i], A[i]) - 1, max(T[i], A[i]) + 1
                if not lo <= nT[i] <= hi:
                    raise AssertionError("T' out of vintage range t=%d i=%d" % (t, i))
            for x in (nK[i], nA[i], nT[i], nP[i], nR[i], nE[i]):
                if abs(x) >= 1 << 53:
                    raise AssertionError("raw out of 2^53")
        K, A, T, P, R, E = nK, nA, nT, nP, nR, nE
        Kmax, Amax = max(K), max(A)
        for i in range(n):
            mark(t, i)
        if t % 12 == 11:
            year = p["start_year"] + t // 12
            for i in range(n):
                yearly.append((year, arch[i]["id"], K[i], A[i], T[i], P[i], R[i], E[i], N[i],
                               y_acc[i], B[i], bind_acc[i], shock_acc[i] / 12.0))
            y_acc = [0] * n
            bind_acc = [0] * n
            shock_acc = [0.0] * n

    h = state_hash(1, p["n_ticks"], n, [("q", K), ("q", A), ("q", T), ("q", P), ("q", R),
                                         ("q", E), ("q", N), ("i", B)])
    out = {"yearly": yearly, "mults": mults, "state_hash": h}
    if keep_milestones:
        out["milestones"] = [(arch[i]["id"], j, ladder[j], mK[i][j], mA[i][j], mT[i][j])
                             for i in range(n) for j in range(J)]
    return out
