using Singularity.Core.Simulation;
using Singularity.Kernel;

namespace Singularity.Core.Science.ToyFunnel;

/// <summary>Toy "science → engineering → production" model (HYP-0001 / EXP-0001).</summary>
public sealed class ToyFunnelModelFactory : ILabModelFactory
{
    public string Id => "toy-funnel";

    public bool IsTestModel => false;

    public string Description =>
        "Toy science -> engineering -> production funnel, 1910-1950 monthly (docs/simulation/toy-science-industry-model.md); param model = A | B | C.";

    public ILabModel Create(ModelSetup setup) =>
        new ToyFunnelModel(ToyFunnelConfig.FromParams(setup.Parameters), setup.Seed, setup.Ticks);
}

/// <summary>A broken model invariant (spec §7), detected after the compute phase of a tick.</summary>
public sealed class ToyFunnelInvariantException : Exception
{
    public ToyFunnelInvariantException(long tick, int country, string message)
        : base($"toy-funnel invariant violated at tick {tick}, country {country}: {message}")
    {
        Tick = tick;
        Country = country;
    }

    public long Tick { get; }
    public int Country { get; }
}

/// <summary>
/// One run of the toy model on Kernel tables (spec: docs/simulation/toy-science-industry-model.md,
/// reference: lab/proto/model.py). The order of floating-point operations follows the prototype
/// exactly, so state and metrics match it bit for bit.
/// <para>
/// Table rows = countries. Columns K, A, T, P, R, E, N (Fx6 raw) and B, in this order: for A and B
/// the Table hash is the prototype's <c>state_hash</c> (schema 1). Control C also needs the K history
/// for its lag, kept as a ring of <c>C_lag_months</c> columns after B (schema 2); its prototype-layout
/// hash is <see cref="PrototypeStateHash"/>.
/// </para>
/// Fields outside the Table: Kmax/Amax (recomputed from the Table every tick) and metric
/// accumulators (yearly sums, milestones) that never feed back into the dynamics.
/// </summary>
public sealed class ToyFunnelModel : ILabModel
{
    /// <summary>RNG "system" keys (spec §4).</summary>
    public const int RngSetup = 0, RngScience = 1, RngProduction = 2;

    public const int SchemaFunnel = 1, SchemaLagControl = 2;

    private readonly ToyFunnelConfig _c;
    private readonly long _ticks;
    private readonly int _n;
    private readonly LongColumn _k, _a, _t, _p, _r, _e, _nRes;
    private readonly IntColumn _b;
    private readonly LongColumn[] _kHistory;

    // Derived inputs (spec §1), fixed after SETUP.
    private readonly double[] _sR, _sE, _sP, _iota;
    private readonly EducationLevel[] _eduR, _eduE;
    private readonly double[][] _mults;
    private readonly string[] _countryIds;

    // Snapshot maxima of the current state; Compute reads them, Post refreshes them.
    private long _kMax, _aMax;

    // Per-row outputs of the current tick (scratch: rewritten every tick, row i only by its chunk).
    private readonly long[] _yRaw;
    private readonly bool[] _binding;
    private readonly double[] _shock;

    // Metric accumulators.
    private readonly long[] _yAcc, _yYear;
    private readonly int[] _bindAcc, _bindYear;
    private readonly double[] _shockAcc, _shockYear;
    private readonly long[] _ladder;
    private readonly int[] _milestones; // [(country * 3 + X) * J + j], X = K, A, T; -1 = not reached

    public ToyFunnelModel(ToyFunnelConfig config, ulong seed, long ticks)
    {
        config.Validate();
        _c = config;
        _ticks = ticks;
        _n = config.CountryCount;

        Table = new Table(_n);
        _k = Table.AddLong("K");
        _a = Table.AddLong("A");
        _t = Table.AddLong("T");
        _p = Table.AddLong("P");
        _r = Table.AddLong("R");
        _e = Table.AddLong("E");
        _nRes = Table.AddLong("N");
        _b = Table.AddInt("B");
        _kHistory = config.Variant == ToyFunnelVariant.C ? new LongColumn[config.CLagMonths] : [];
        for (int s = 0; s < _kHistory.Length; s++)
            _kHistory[s] = Table.AddLong($"K_lag_{s:D3}");

        _sR = new double[_n];
        _sE = new double[_n];
        _sP = new double[_n];
        _iota = new double[_n];
        _eduR = new EducationLevel[_n];
        _eduE = new EducationLevel[_n];
        _mults = new double[_n][];
        _countryIds = new string[_n];
        _yRaw = new long[_n];
        _binding = new bool[_n];
        _shock = new double[_n];
        _yAcc = new long[_n];
        _yYear = new long[_n];
        _bindAcc = new int[_n];
        _bindYear = new int[_n];
        _shockAcc = new double[_n];
        _shockYear = new double[_n];

        Setup(new CounterRng(seed));

        _ladder = new long[config.MilestoneCount];
        _ladder[0] = config.MilestoneQ0Raw;
        for (int j = 1; j < _ladder.Length; j++)
            _ladder[j] = checked(_ladder[j - 1] * 5) / 4;
        _milestones = new int[_n * 3 * _ladder.Length];
        Array.Fill(_milestones, -1);
        MarkMilestones(0);
        RefreshMaxima();

        Systems = [new FunnelSystem(this)];
        MetricTables =
        [
            new MetricTableSpec("yearly", ["year", "country", "K", "A", "T", "P", "R", "E", "N", "Y_year", "B", "eng_binding_months", "shock"]),
            new MetricTableSpec("countries", ["country", "mult_S", "mult_E", "mult_Pl", "mult_Nl"]),
            new MetricTableSpec("milestones", ["country", "j", "q_j", "m_K", "m_A", "m_T"]),
        ];
    }

    public const int YearlyTable = 0, CountriesTable = 1, MilestonesTable = 2;

    public ToyFunnelConfig Config => _c;

    public int SchemaVersion => _c.Variant == ToyFunnelVariant.C ? SchemaLagControl : SchemaFunnel;

    public Table Table { get; }

    public IReadOnlyList<ITickSystem> Systems { get; }

    public IReadOnlyList<MetricTableSpec> MetricTables { get; }

    public string CountryId(int country) => _countryIds[country];

    /// <summary>
    /// SHA-256 in the prototype's runs.csv layout: schema 1, tick, n, then K, A, T, P, R, E, N, B
    /// (spec §5). Equals <c>StateHash(Table, 1, tick)</c> for A and B; for C it leaves out the K history.
    /// </summary>
    public string PrototypeStateHash(long tick)
    {
        var view = new Table(_n);
        LongColumn[] stocks = [_k, _a, _t, _p, _r, _e, _nRes];
        for (int s = 0; s < stocks.Length; s++)
            view.AddLong(stocks[s].Name).Load(stocks[s].Current);
        view.AddInt(_b.Name).Load(_b.Current);
        return StateHash.ComputeHex(view, SchemaFunnel, tick);
    }

    /// <summary>
    /// Tick 0: one "countries" row per country. After each December: one "yearly" row per country
    /// (stocks after the tick, year accumulators). After the last tick: all milestones.
    /// </summary>
    public void Observe(long tick, IMetricSink sink)
    {
        if (tick == 0)
        {
            for (int i = 0; i < _n; i++)
                sink.Row(CountriesTable, _countryIds[i], MetricFormat.Real(_mults[i][0]), MetricFormat.Real(_mults[i][1]),
                    MetricFormat.Real(_mults[i][2]), MetricFormat.Real(_mults[i][3]));
        }
        if (tick > 0 && tick % 12 == 0)
        {
            string year = MetricFormat.Int(_c.StartYear + (tick - 1) / 12);
            for (int i = 0; i < _n; i++)
            {
                sink.Row(YearlyTable, year, _countryIds[i],
                    MetricFormat.Fx6(_k.Current[i]), MetricFormat.Fx6(_a.Current[i]), MetricFormat.Fx6(_t.Current[i]),
                    MetricFormat.Fx6(_p.Current[i]), MetricFormat.Fx6(_r.Current[i]), MetricFormat.Fx6(_e.Current[i]),
                    MetricFormat.Fx6(_nRes.Current[i]), MetricFormat.Fx6(_yYear[i]), MetricFormat.Int(_b.Current[i]),
                    MetricFormat.Int(_bindYear[i]), MetricFormat.Real(_shockYear[i]));
            }
        }
        if (tick == _ticks)
        {
            int jCount = _ladder.Length;
            for (int i = 0; i < _n; i++)
            {
                for (int j = 0; j < jCount; j++)
                {
                    sink.Row(MilestonesTable, _countryIds[i], MetricFormat.Int(j), MetricFormat.Fx6(_ladder[j]),
                        Milestone(i, 0, j), Milestone(i, 1, j), Milestone(i, 2, j));
                }
            }
        }
    }

    private string Milestone(int country, int x, int j)
    {
        int m = _milestones[(country * 3 + x) * _ladder.Length + j];
        return m < 0 ? "" : MetricFormat.Int(m);
    }

    private static double Fx(long raw) => (double)raw / Fx6.Scale;

    private static long Raw(double x) => Fx6.FromDouble(x).Raw;

    /// <summary>SETUP (spec §1): multipliers, derived inputs and the initial state, in prototype order.</summary>
    private void Setup(CounterRng rng)
    {
        var k = new long[_n];
        var a = new long[_n];
        var t = new long[_n];
        var p = new long[_n];
        var r = new long[_n];
        var e = new long[_n];
        var nRes = new long[_n];
        int archetypeCount = _c.Archetypes.Count;
        for (int i = 0; i < _n; i++)
        {
            ToyArchetype arch = _c.Archetypes[i % archetypeCount];
            _countryIds[i] = i < archetypeCount ? arch.Id : $"{arch.Id}.{i / archetypeCount}";
            var m = new double[4];
            for (int f = 0; f < 4; f++)
                m[f] = _c.SetupMultLo + _c.SetupMultSpan * rng.NextUnit(0, RngSetup, i, f);
            _mults[i] = m;
            double lS = arch.S * m[0], lE = arch.E * m[1], lPl = arch.Pl * m[2], lNl = arch.Nl * m[3];

            _sR[i] = _c.SR0 * lS;
            _sE[i] = _c.SE0 * lE;
            _sP[i] = _c.SP0 * (0.5 + 0.5 * lPl);
            _iota[i] = arch.Iota;
            _eduR[i] = arch.EduR;
            _eduE[i] = arch.EduE;

            nRes[i] = Raw(100.0 * lNl);
            k[i] = Raw(100.0 * lS);
            r[i] = Raw(3.0 * lS);
            e[i] = Raw(3.0 * lE);
            a[i] = Math.Min(Raw(60.0 * lE), k[i]);
            t[i] = Raw(Fx(a[i]) * (0.5 + 0.25 * lPl));
            p[i] = Raw(100.0 * lPl);

            if (_c.Variant == ToyFunnelVariant.B)
            {
                k[i] = Raw(_c.BCTau0 * Fx(k[i]));
                a[i] = k[i];
                t[i] = k[i];
            }
            else if (_c.Variant == ToyFunnelVariant.C)
            {
                a[i] = Raw(_c.CPsi * Fx(k[i]));
                t[i] = a[i];
            }
        }
        _k.Load(k);
        _a.Load(a);
        _t.Load(t);
        _p.Load(p);
        _r.Load(r);
        _e.Load(e);
        _nRes.Load(nRes);
        // K_s for s < 0 is K0, so the whole history starts at K0.
        for (int s = 0; s < _kHistory.Length; s++)
            _kHistory[s].Load(k);
    }

    private double Rho(EducationLevel edu, long tick)
    {
        double level = edu.Level;
        if (edu.Scheduled)
        {
            double from = _c.EduScheduleFrom, to = _c.EduScheduleTo;
            int months = _c.EduScheduleMonths;
            level = from + (to - from) * ((double)Math.Min(tick, months) / months);
        }
        return _c.Rho0 * level;
    }

    /// <summary>One country, one month (spec §2): reads only S_t, writes only row i of Next and scratch.</summary>
    private void ComputeRow(in TickContext ctx, int i)
    {
        long t = ctx.Tick;
        bool funnel = _c.Variant == ToyFunnelVariant.A;
        long kRaw = _k.Current[i], aRaw = _a.Current[i], tRaw = _t.Current[i], pRaw = _p.Current[i];
        long rRaw = _r.Current[i], eRaw = _e.Current[i];
        double xK = Fx(kRaw), xA = Fx(aRaw), xT = Fx(tRaw), xP = Fx(pRaw), xR = Fx(rRaw), xE = Fx(eRaw), xN = Fx(_nRes.Current[i]);
        double dt = _c.Dt;

        // 1. Output.
        double u1 = ctx.Rng.NextUnit(t, RngProduction, i, 0);
        double shock = _c.Nu * _c.Sigma0 * (2.0 * u1 - 1.0);
        double y = _c.Z * DetMath.PowDyadic(1.0 + xT / _c.TRef, _c.GammaEighths) * DetMath.PowDyadic(xP, _c.BetaEighths)
                   * DetMath.PowDyadic(xN, 8 - _c.BetaEighths) * (1.0 + shock);
        long yRaw = Raw(y);

        // 2. Budget, exact in raw units.
        long spR = (long)Math.Floor(_sR[i] * yRaw);
        long spE = (long)Math.Floor(_sE[i] * yRaw);
        long inv0 = (long)Math.Floor(_sP[i] * yRaw);
        long consumption = yRaw - spR - spE - inv0;
        if (consumption < 0)
            throw new ToyFunnelInvariantException(t, i, "negative consumption");

        // 3. Science (control B: staff H = R + E, productivity δ_τ).
        double xS = _c.Variant == ToyFunnelVariant.B ? xR + xE : xR;
        double deltaSci = _c.Variant == ToyFunnelVariant.B ? _c.BDeltaTau : _c.DeltaK;
        double dK = dt * (deltaSci * Math.Sqrt(xS) * Math.Sqrt(xK) + _c.Mu * _iota[i] * Math.Max(0.0, Fx(_kMax) - xK));
        double u2 = ctx.Rng.NextUnit(t, RngScience, i, 0);
        int bNext = _b.Current[i];
        if (_c.Nu > 0.0)
        {
            double prob = Math.Min(0.5, _c.P0 * Math.Sqrt(xS) / _c.Nu);
            if (u2 < prob)
            {
                dK = dK + _c.Nu * _c.G0 * xK;
                bNext = checked(bNext + 1);
            }
        }
        long kNext = kRaw + Raw(dK);

        // 4. Engineering (model A only).
        bool binding = false;
        long aNext = aRaw;
        if (funnel)
        {
            long f = Math.Max(kRaw, Raw(_iota[i] * Fx(_aMax)));
            double g = Fx(Math.Max(0, f - aRaw));
            double capE = _c.Eta * xE;
            double capG = _c.Theta * g;
            binding = capE < capG;
            double dA = dt * (Math.Min(capE, capG) - _c.DeltaA * xA);
            aNext = Math.Min(Math.Max(aRaw + Raw(dA), 0), f);
        }

        // 5. Capital and machine park (vintage); new capital carries A_t.
        double inv = _c.Kappa * Fx(inv0);
        double dep = _c.DeltaP * dt * xP;
        double xP1 = xP + (inv - dep);
        long pNext = pRaw + Raw(inv - dep);
        long tNext = funnel ? tRaw + Raw(inv * (xA - xT) / xP1) : tRaw;

        // 6. Staff.
        long rNext = rRaw + Raw(Rho(_eduR[i], t) * dt * (Fx(spR) / _c.W - xR));
        long eNext = eRaw + Raw(Rho(_eduE[i], t) * dt * (Fx(spE) / _c.W - xE));

        if (_c.Variant == ToyFunnelVariant.B)
        {
            aNext = kNext;
            tNext = kNext;
        }
        else if (_c.Variant == ToyFunnelVariant.C)
        {
            // Slot (t+1) mod L holds K_{t+1-L}; after reading it, it takes K_{t+1}.
            LongColumn slot = _kHistory[(int)((t + 1) % _kHistory.Length)];
            aNext = Raw(_c.CPsi * Fx(slot.Current[i]));
            tNext = aNext;
            slot.Next[i] = kNext;
        }

        _k.Next[i] = kNext;
        _a.Next[i] = aNext;
        _t.Next[i] = tNext;
        _p.Next[i] = pNext;
        _r.Next[i] = rNext;
        _e.Next[i] = eNext;
        _b.Next[i] = bNext;
        _yRaw[i] = yRaw;
        _binding[i] = binding;
        _shock[i] = shock;
    }

    private const long RawLimit = 1L << 53;

    /// <summary>Spec §7, comparing S_{t+1} (Next) with S_t (Current) before the swap.</summary>
    private void CheckInvariants(long t)
    {
        bool funnel = _c.Variant == ToyFunnelVariant.A;
        for (int i = 0; i < _n; i++)
        {
            long k0 = _k.Current[i], a0 = _a.Current[i], t0 = _t.Current[i];
            long k1 = _k.Next[i], a1 = _a.Next[i], t1 = _t.Next[i];
            long p1 = _p.Next[i], r1 = _r.Next[i], e1 = _e.Next[i];
            if (Math.Min(Math.Min(Math.Min(k1, a1), Math.Min(t1, p1)), Math.Min(r1, e1)) < 0)
                throw new ToyFunnelInvariantException(t, i, "negative stock");
            if (k1 < k0)
                throw new ToyFunnelInvariantException(t, i, "K decreased");
            if (funnel)
            {
                if (a1 > Math.Max(k0, Raw(_iota[i] * Fx(_aMax))))
                    throw new ToyFunnelInvariantException(t, i, "A > F");
                if (t1 < Math.Min(t0, a0) - 1 || t1 > Math.Max(t0, a0) + 1)
                    throw new ToyFunnelInvariantException(t, i, "T' out of the vintage range");
            }
            if (k1 >= RawLimit || a1 >= RawLimit || t1 >= RawLimit || p1 >= RawLimit || r1 >= RawLimit || e1 >= RawLimit)
                throw new ToyFunnelInvariantException(t, i, "raw value out of 2^53");
        }
    }

    /// <summary>Post of tick t (Current = S_{t+1}): maxima, milestones, year accumulators.</summary>
    private void AfterTick(long t)
    {
        RefreshMaxima();
        MarkMilestones(t);
        for (int i = 0; i < _n; i++)
        {
            _yAcc[i] = checked(_yAcc[i] + _yRaw[i]);
            _bindAcc[i] += _binding[i] ? 1 : 0;
            _shockAcc[i] += _shock[i];
        }
        if (t % 12 == 11)
        {
            for (int i = 0; i < _n; i++)
            {
                _yYear[i] = _yAcc[i];
                _bindYear[i] = _bindAcc[i];
                _shockYear[i] = _shockAcc[i] / 12.0;
            }
            Array.Clear(_yAcc);
            Array.Clear(_bindAcc);
            Array.Clear(_shockAcc);
        }
    }

    private void RefreshMaxima()
    {
        ReadOnlySpan<long> k = _k.Current, a = _a.Current;
        long kMax = long.MinValue, aMax = long.MinValue;
        for (int i = 0; i < _n; i++)
        {
            kMax = Math.Max(kMax, k[i]);
            aMax = Math.Max(aMax, a[i]);
        }
        _kMax = kMax;
        _aMax = aMax;
    }

    /// <summary>m_X(j) = first month index t whose end state has X ≥ q_j (initial state → 0).</summary>
    private void MarkMilestones(long t)
    {
        int jCount = _ladder.Length;
        for (int i = 0; i < _n; i++)
        {
            for (int x = 0; x < 3; x++)
            {
                long value = (x == 0 ? _k : x == 1 ? _a : _t).Current[i];
                int offset = (i * 3 + x) * jCount;
                for (int j = 0; j < jCount && value >= _ladder[j]; j++)
                {
                    if (_milestones[offset + j] < 0)
                        _milestones[offset + j] = (int)t;
                }
            }
        }
    }

    /// <summary>
    /// The whole model is one system: a country's step reads only S_t, so splitting it into
    /// subsystems would change nothing (spec §5) but add buffers. RNG keys use <see cref="RngScience"/>
    /// and <see cref="RngProduction"/> (spec §4), not <see cref="SystemId"/>.
    /// </summary>
    private sealed class FunnelSystem(ToyFunnelModel model) : ITickSystem
    {
        public int SystemId => 1;

        public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd)
        {
            for (int i = rowStart; i < rowEnd; i++)
                model.ComputeRow(context, i);
        }

        public void Merge(in TickContext context, int chunkCount) => model.CheckInvariants(context.Tick);

        public void Post(in TickContext context) => model.AfterTick(context.Tick - 1);
    }
}
