using System.Globalization;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Knowledge;
using Singularity.Kernel;

namespace Singularity.Lab.Smoke;

/// <summary>Candidate culture_H rules (docs/simulation/culture-rule-selection.md §1.1).</summary>
internal enum CultureRule { R0, R1, R2, R1u1, R1c }

/// <summary>
/// One variant of a rule: <c>u_p = min(1, v_p/v_full)^β</c>, <c>v_full = r_full·labour_p</c>, β = 1 or 0.5
/// (<see cref="Sqrt"/>), <c>u_m</c> of the smooth maintenance floor (R1 only).
/// </summary>
internal sealed record RuleVariant(CultureRule Rule, Fx6 RFull, bool Sqrt, Fx6 UM)
{
    public string Label => Rule switch
    {
        CultureRule.R0 => "R0",
        CultureRule.R1u1 => "R1u1",
        CultureRule.R1 => $"R1/rf{RFull}/b{(Sqrt ? "0.5" : "1")}/um{UM}",
        CultureRule.R1c => $"R1c/rf{RFull}/um{UM}",
        _ => $"R2/rf{RFull}/b{(Sqrt ? "0.5" : "1")}",
    };

    public int NewParams => Rule switch { CultureRule.R1 => 3, CultureRule.R2 => 2, _ => 0 };
}

/// <summary>A process of a cluster in the rule step: <c>v_p</c> of month t−1, <c>r_d</c>, world_lead, <c>v_full(p)</c>.</summary>
internal readonly record struct RuleProc(int Number, Fx6 V, Fx6 R, bool WorldLead, Fx6 VFull);

/// <summary>
/// The culture_H step of each candidate rule in the canonical Fx6 form of contract §4.0 (left to right, floor on
/// every · and /). Only <c>drive</c>, <c>catch</c>, <c>maint</c> differ from contract §4.1; R0 is the core formula itself.
/// </summary>
internal static class CultureRuleMath
{
    private static readonly Fx6 GapOffset = Fx6.FromRaw(250_000);

    private static readonly Fx6 Two = Fx6.FromInt(2), Three = Fx6.FromInt(3);

    public static Fx6 Max0(Fx6 x) => x > Fx6.Zero ? x : Fx6.Zero;

    /// <summary>Integer square root of a Fx6 in [0; 1]: floor(sqrt(raw·10⁶)) raw, exact and platform-independent.</summary>
    public static Fx6 Sqrt(Fx6 u)
    {
        long n = checked(u.Raw * Fx6.Scale);
        long r = (long)Math.Sqrt(n);
        while (r * r > n)
            r--;
        while ((r + 1) * (r + 1) <= n)
            r++;
        return Fx6.FromRaw(r);
    }

    public static Fx6 U(RuleVariant rv, Fx6 v, Fx6 vFull)
    {
        if (rv.Rule == CultureRule.R1u1)
            return Fx6.One;
        Fx6 u = v / vFull;
        if (u > Fx6.One)
            u = Fx6.One;
        if (rv.Rule == CultureRule.R1c)
            return u * u * (Three - Two * u); // smoothstep (decision #85): ≈ 0 for a token output, flat at saturation
        return rv.Sqrt ? Sqrt(u) : u;
    }

    public static CultureStep Step(RuleVariant rv, CultureCell cell, ReadOnlySpan<RuleProc> procs, ReadOnlySpan<CultureSpill> spill, IndustryParams p)
    {
        if (rv.Rule == CultureRule.R0)
        {
            Span<CultureProcess> cp = stackalloc CultureProcess[procs.Length];
            for (int i = 0; i < procs.Length; i++)
                cp[i] = new CultureProcess(procs[i].Number, procs[i].V, procs[i].R, procs[i].WorldLead);
            return IndustryMath.Culture(cell, cp, spill, p);
        }
        Fx6 h = cell.H;
        Fx6 vc = Fx6.Zero;
        for (int i = 0; i < procs.Length; i++)
        {
            if (i > 0 && procs[i].Number <= procs[i - 1].Number)
                throw new ArgumentException("Culture processes must be in strictly ascending number.", nameof(procs));
            vc += procs[i].V;
        }
        Fx6 hFloor = p.PhiFloor * cell.Hcap;
        Fx6 drive = Fx6.Zero, catchUp = Fx6.Zero, maint = Fx6.Zero;
        if (vc != Fx6.Zero)
        {
            Fx6 actRatio = cell.Act / p.ActNorm;
            if (actRatio > Fx6.One)
                actRatio = Fx6.One;
            foreach (RuleProc proc in procs)
            {
                Fx6 u = U(rv, proc.V, proc.VFull);
                Fx6 above = Max0(proc.R + GapOffset - h);
                Fx6 d = u * above;
                if (d > drive)
                    drive = d;
                if (proc.WorldLead && d > catchUp)
                    catchUp = d;
                if (rv.Rule == CultureRule.R2)
                {
                    Fx6 s = proc.V / vc;
                    maint += s * proc.R * actRatio;
                }
                else
                {
                    Fx6 share = u / rv.UM;
                    if (share > Fx6.One)
                        share = Fx6.One;
                    Fx6 mp = hFloor + (proc.R * actRatio - hFloor) * share;
                    if (mp > maint)
                        maint = mp;
                }
            }
        }
        Fx6 spillSum = Fx6.Zero;
        foreach (CultureSpill sp in spill)
            spillSum += sp.R * Max0(sp.HOther - h);
        Fx6 sat = Max0(Fx6.One - h / cell.Hcap);
        Fx6 alphaEff = cell.QualityBoost ? p.Alpha * (Fx6.One + p.KQp) : p.Alpha;
        Fx6 inner = alphaEff * cell.Act * cell.W * drive + p.KCatch * catchUp + p.Sigma * spillSum;
        Fx6 dPlus = sat * inner;
        Fx6 capped = dPlus < p.RhoMax ? dPlus : p.RhoMax;
        Fx6 dMinus = p.DeltaH * Max0(h - (maint > hFloor ? maint : hFloor));
        return new CultureStep(vc, drive, catchUp, maint, spillSum, hFloor, sat, alphaEff, dPlus, capped, dMinus, h + capped - dMinus);
    }
}

internal enum StartMod { None, Edge, Hold }

internal enum RunSet { Solo, Cluster, PrcSet, Mastered }

/// <summary>
/// One site of one actor, alone, with the T4s line-stand rules (smoke-t4s.md §2) but a pluggable culture rule.
/// Same tick order as <see cref="IndustrySystem"/>: compute of month t reads the snapshot (yields, culture from
/// <c>proc_v</c> of t−1, tooling with the stand's investment), then lines report and merge writes
/// <c>proc_X</c>, <c>proc_v</c>. With R0 the trajectory must equal the core stand bit for bit (checked by
/// <see cref="CultureRules.Check"/>). Fx6 only in the state.
/// </summary>
internal sealed class RuleStand
{
    private static readonly Fx6 CapShare = Fx6.FromRaw(250_000);
    private readonly IndustryLayout _l;
    private readonly IndustryParams _p;
    private readonly RuleVariant _rv;
    private readonly bool[] _known, _running;
    private readonly Fx6 _rate, _hcap, _w, _capBudget;
    private readonly Fx6[] _m0, _vFull, _labour;
    private readonly int _c, _n;
    private readonly RuleProc[] _procs;
    private readonly CultureSpill[] _spill;
    private readonly Fx6[] _invest;
    private readonly bool _freeTooling;

    /// <summary>Deviation D-1 (culture-rule-selection.md §2.0): tooling never limits, <c>M ≡ 6</c> in every cell.</summary>
    public static readonly ThreadLocal<bool> FreeTooling = new(() => false);
    private static readonly Fx6 FreeM = Fx6.FromInt(6);

    public Fx6[] H { get; }
    public Fx6[] M { get; }
    public Fx6[] X { get; }
    public Fx6[] V { get; }
    public ProcessYield[] Y { get; }
    public IndustryLayout Layout => _l;
    public int Target { get; } = -1;
    public List<string> Notes { get; } = new();

    public RuleStand(GameData data, ScenarioData sc, int actor, IndustryParams ip, RuleVariant rv, Fx6 rate, int target,
        RunSet run, StartMod mod, bool rhms, (int Number, long BaseRaw)[]? prcSet = null)
    {
        _p = ip;
        _freeTooling = FreeTooling.Value;
        _rv = rv;
        _rate = rate;
        IndustryLayout full = IndustryLayout.FromData(data.Nodes, sc, 2, 4);
        var specs = new IndustryProcessSpec[full.ProcessCount];
        for (int r = 0; r < full.ProcessCount; r++)
            specs[r] = new IndustryProcessSpec(full.Number[r], full.Clusters[full.ClusterIndex[r]], full.Discipline[r], full.Tier[r], full.Req[r]);
        _l = IndustryLayout.Create([.. full.Clusters], specs, [0], 2, IndustryLayout.SliceSkilled, 4, IndustryLayout.SliceAlloyGrades);
        _c = _l.Clusters.Length;
        _n = _l.ProcessCount;

        var kg = KnowledgeGraph.FromData(data.Nodes, sc.Disciplines, new KnowledgeParams());
        _known = new bool[_n];
        foreach (int row in sc.KnownOf(actor))
            MarkKnown(data.Nodes.Number[row]);
        if (target > 0)
        {
            foreach (int r in SmokeT4s.Closure(kg, kg.RowOf(target)))
                MarkKnown(kg.Number[r]);
            Target = _l.RowOf(target);
        }
        _running = new bool[_n];
        var mastered = new bool[_n];
        foreach (int row in sc.MasteredOf(actor))
            mastered[_l.RowOf(data.Nodes.Number[row])] = true;
        if (prcSet is not null)
        {
            foreach ((int num, long _) in prcSet)
            {
                _known[_l.RowOf(num)] = true;
                _running[_l.RowOf(num)] = true;
            }
        }
        for (int r = 0; r < _n; r++)
        {
            _running[r] |= run switch
            {
                RunSet.Solo => r == Target,
                RunSet.Cluster => r == Target || (_known[r] && _l.ClusterIndex[r] == _l.ClusterIndex[Target]),
                RunSet.Mastered => mastered[r],
                _ => false,
            };
        }

        IndustryStart start = IndustryStart.FromScenario(sc, data.Nodes, IndustryLayout.FromData(data.Nodes, sc, 2, 4), ip);
        H = new Fx6[_c * 3];
        M = new Fx6[_c * 3];
        for (int c = 0; c < _c; c++)
        {
            for (int d = 0; d < 3; d++)
            {
                H[c * 3 + d] = start.H[start.Cell(actor, c, d)];
                M[c * 3 + d] = start.M[start.Cell(actor, c, d)];
            }
        }
        _hcap = sc.Hcap[actor];
        if (rhms)
        {
            // R-H-MS (RR-0006 addendum §5): a process mastered at t0 is a fixed point: H ≥ r_d − 0.05, M ≥ H + 0.2.
            var raised = new bool[_c * 3];
            for (int r = 0; r < _n; r++)
            {
                if (!mastered[r])
                    continue;
                for (int d = 0; d < 3; d++)
                {
                    Fx6 req = _l.Req[r][d];
                    if (req <= Fx6.Zero)
                        continue;
                    int cell = _l.ClusterIndex[r] * 3 + d;
                    Fx6 want = req - Fx6.FromRaw(50_000);
                    if (H[cell] < want)
                        H[cell] = want;
                    raised[cell] = true;
                }
            }
            for (int cell = 0; cell < raised.Length; cell++)
            {
                if (!raised[cell])
                    continue;
                Fx6 mw = H[cell] + Fx6.FromRaw(200_000);
                if (M[cell] < mw)
                    M[cell] = mw;
                if (_hcap < H[cell] + Fx6.FromRaw(500_000))
                    Notes.Add($"Hcap {_hcap} < H+0.5 in {_l.Clusters[cell / 3]}.{(Dimension)(cell % 3)} (H {H[cell]})");
            }
        }
        if (_freeTooling)
            Array.Fill(M, FreeM);
        _m0 = (Fx6[])M.Clone();
        if (mod != StartMod.None)
        {
            int tc = _l.ClusterIndex[Target];
            for (int d = 0; d < 3; d++)
            {
                Fx6 req = _l.Req[Target][d];
                if (req <= Fx6.Zero)
                    continue;
                int cell = tc * 3 + d;
                if (mod == StartMod.Hold)
                    H[cell] = req;
                else if (H[cell] < req - Fx6.FromRaw(900_000))
                    H[cell] = req - Fx6.FromRaw(900_000);
            }
        }
        X = new Fx6[_n];
        for (int r = 0; r < _n; r++)
            X[r] = start.X[actor * full.ProcessCount + r];
        V = new Fx6[_n];
        Y = new ProcessYield[_n];
        _w = sc.W[actor];
        _capBudget = CapShare * sc.BudgetIndex[actor];
        _labour = new Fx6[_n];
        _vFull = new Fx6[_n];
        for (int r = 0; r < _n; r++)
        {
            _labour[r] = Fx6.FromInt(SmokeT4s.Labour.TryGetValue(_l.Number[r], out long lab) ? lab : 0);
            _vFull[r] = rv.RFull * _labour[r];
        }
        _procs = new RuleProc[_n];
        _spill = new CultureSpill[_c];
        _invest = new Fx6[_c * 3];
    }

    private void MarkKnown(int number)
    {
        int r = _l.RowOf(number);
        if (r >= 0)
            _known[r] = true;
    }

    public bool Known(int row) => _known[row];

    public bool Running(int row) => _running[row];

    /// <summary>One month: snapshot yields, culture and tooling, line reports, merge.</summary>
    public void Step()
    {
        int mechTier = 0;
        var admitted = new bool[_n];
        for (int r = 0; r < _n; r++)
        {
            int c = _l.ClusterIndex[r];
            Y[r] = IndustryMath.Yield(_l.Req[r], Cells(H, c), Cells(M, c), X[r], _p.XHalf(_l.Tier[r]), Fx6.Zero, _p.Lambda0);
            admitted[r] = _known[r] && Y[r].Yield > Fx6.Zero;
            if (admitted[r] && _l.Clusters[c] == Cluster.MECH && _l.Tier[r] > mechTier)
                mechTier = _l.Tier[r];
        }
        // Stand tooling rule (smoke-t4s.md §2): constant budget in canonical cell order, MECH held at start level.
        Fx6 cap = Fx6.FromInt(1 + Math.Max(3, mechTier));
        Fx6 left = _capBudget;
        for (int c = 0; c < _c; c++)
        {
            for (int d = 0; d < 3; d++)
            {
                Fx6 need = Need(c, d, cap);
                Fx6 give = need < left ? need : left;
                _invest[c * 3 + d] = give;
                left -= give;
            }
        }
        var newH = new Fx6[H.Length];
        var newM = new Fx6[M.Length];
        Fx6 act = IndustryMath.Act(0, 0);
        for (int c = 0; c < _c; c++)
        {
            for (int d = 0; d < 3; d++)
            {
                int np = 0;
                foreach (int r in _l.Order)
                {
                    if (_l.ClusterIndex[r] == c && V[r] > Fx6.Zero)
                        _procs[np++] = new RuleProc(_l.Number[r], V[r], _l.Req[r][d], false, _vFull[r]);
                }
                int ns = 0;
                for (int c2 = 0; c2 < _c; c2++)
                {
                    if (c2 != c)
                        _spill[ns++] = new CultureSpill(_p.Spill(_l.Clusters[c], _l.Clusters[c2]), H[c2 * 3 + d]);
                }
                Fx6 h = H[c * 3 + d];
                var cell = new CultureCell(h, _hcap, act, _w, false);
                CultureStep st = CultureRuleMath.Step(_rv, cell, _procs.AsSpan(0, np), _spill.AsSpan(0, ns), _p);
                if (st.HNew < Fx6.Zero || (st.HNew - h).Raw > _p.RhoMax.Raw + 1 || st.HNew < (h < st.HFloor ? h : st.HFloor))
                    throw new InvalidOperationException($"culture invariant I1/I2 broken in {_l.Clusters[c]}.{(Dimension)d}: {h} → {st.HNew}");
                newH[c * 3 + d] = st.HNew;
                newM[c * 3 + d] = _freeTooling ? FreeM : IndustryMath.Tooling(M[c * 3 + d], _invest[c * 3 + d], mechTier, _p).MNew;
            }
        }
        Array.Copy(newH, H, H.Length);
        Array.Copy(newM, M, M.Length);
        for (int r = 0; r < _n; r++)
        {
            if (_running[r] && admitted[r])
            {
                X[r] += _rate;
                V[r] = _rate * _labour[r];
            }
            else
            {
                X[r] = IndustryMath.Forget(X[r], _p.DeltaX);
                V[r] = Fx6.Zero;
            }
        }
    }

    private Fx6 Need(int c, int d, Fx6 cap)
    {
        Fx6 target = Fx6.Zero;
        for (int r = 0; r < _n; r++)
        {
            if (_running[r] && _l.ClusterIndex[r] == c && _l.Req[r][d] > target)
                target = _l.Req[r][d];
        }
        if (target > cap)
            target = cap;
        if (_l.Clusters[c] == Cluster.MECH && _m0[c * 3 + d] > target)
            target = _m0[c * 3 + d];
        Fx6 m = M[c * 3 + d];
        Fx6 dec = m - _p.DeltaM * m;
        if (target <= dec)
            return Fx6.Zero;
        return (target - dec) * _p.CostM(IndustryMath.CostTier(m));
    }

    /// <summary>Culture factor <c>g_H(p) = Π_d g(H_d − r_d)</c> over active dimensions, from the current state.</summary>
    public Fx6 GH(int row)
    {
        Fx6 acc = Fx6.One;
        int c = _l.ClusterIndex[row];
        for (int d = 0; d < 3; d++)
        {
            Fx6 req = _l.Req[row][d];
            if (req > Fx6.Zero)
                acc *= IndustryMath.G(H[c * 3 + d] - req);
        }
        return acc;
    }

    /// <summary>Σ over the active dimensions of the target of its cluster's H.</summary>
    public Fx6 TargetHSum()
    {
        Fx6 s = Fx6.Zero;
        int c = _l.ClusterIndex[Target];
        for (int d = 0; d < 3; d++)
        {
            if (_l.Req[Target][d] > Fx6.Zero)
                s += H[c * 3 + d];
        }
        return s;
    }

    public double MeanH() => H.Average(h => h.ToDouble());

    private static Dims3 Cells(Fx6[] a, int c) => new(a[c * 3], a[c * 3 + 1], a[c * 3 + 2]);
}

/// <summary>
/// culture_H rule selection (docs/simulation/culture-rule-selection.md): the pre-registered criteria K0–K6 and
/// diagnostics D1–D4 for R0, R1, R2 and the ablation R1u1. Doubles only in the analysis of finished runs.
/// </summary>
internal static class CultureRules
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly long[] Alphas = [20_000, 35_000, 50_000, 65_000, 80_000];
    private static readonly long[] Deltas = [1_700, 3_000, 4_300];
    private static readonly Fx6 RNorm = Fx6.FromInt(25), RTok = Fx6.FromRaw(1_250_000);
    private static readonly Fx6 G09 = Fx6.FromRaw(900_000);
    private const int Horizon = 384;

    public static List<RuleVariant> Variants()
    {
        var list = new List<RuleVariant> { new(CultureRule.R0, RNorm, false, Fx6.FromRaw(250_000)) };
        long[] rf = [6_250_000, 12_500_000, 25_000_000];
        foreach (long r in rf)
            foreach (bool sq in new[] { false, true })
                foreach (long um in new long[] { 250_000, 500_000 })
                    list.Add(new RuleVariant(CultureRule.R1, Fx6.FromRaw(r), sq, Fx6.FromRaw(um)));
        foreach (long r in rf)
            foreach (bool sq in new[] { false, true })
                list.Add(new RuleVariant(CultureRule.R2, Fx6.FromRaw(r), sq, Fx6.FromRaw(250_000)));
        list.Add(new RuleVariant(CultureRule.R1u1, RNorm, false, Fx6.FromRaw(250_000)));
        return list;
    }

    public static IndustryParams Params(long alpha, long delta, int xPct) => new()
    {
        Alpha = Fx6.FromRaw(alpha),
        KCatch = Fx6.FromRaw(alpha / 2),
        DeltaH = Fx6.FromRaw(delta),
        CM = Fx6.FromRaw(100_000),
        XHalfByTier = [Fx6.FromInt(2 * xPct), Fx6.FromInt(8 * xPct), Fx6.FromInt(25 * xPct)],
    };

    public static int Run(string modDir, string outDir, string? only, TextWriter log)
    {
        GameData data = ModLoader.Load(modDir);
        ScenarioData sc = data.Scenario("jet_slice_1945");
        Directory.CreateDirectory(outDir);
        log.WriteLine($"culture-rules scenario_hash={sc.Hash}");
        if (!Check(data, sc, log))
            return 1;
        if (only == "check")
            return 0;
        if (only is not null && only.StartsWith("trace:", StringComparison.Ordinal))
            return Trace(data, sc, only[6..], log);
        List<RuleVariant> variants = Variants();
        if (only == "r1c")
        {
            // Decision #85, §5: grid pre-declared in culture-rule-selection.md §5.1 before this run.
            variants = [];
            foreach (long rf in new long[] { 6_250_000, 8_000_000, 10_000_000, 12_500_000 })
                foreach (long um in new long[] { 250_000, 500_000 })
                    variants.Add(new RuleVariant(CultureRule.R1c, Fx6.FromRaw(rf), false, Fx6.FromRaw(um)));
            outDir = Path.Combine(outDir, "r1c");
        }
        if (only == "posthoc")
        {
            // Post-hoc probe (§3), NOT selectable under §1.5: linear u between the grid nodes 6.25 and 12.5.
            variants = [new RuleVariant(CultureRule.R1, Fx6.FromInt(8), false, Fx6.FromRaw(500_000)),
                new RuleVariant(CultureRule.R1, Fx6.FromInt(10), false, Fx6.FromRaw(500_000)),
                new RuleVariant(CultureRule.R1, Fx6.FromInt(10), false, Fx6.FromRaw(250_000))];
            outDir = Path.Combine(outDir, "posthoc");
        }
        foreach (bool dev in new[] { false, true })
        {
            var results = new VariantResult[variants.Count];
            Parallel.For(0, variants.Count, i => results[i] = Evaluate(data, sc, variants[i], dev));
            string dir = Path.Combine(outDir, dev ? "deviation-D1-D2" : "literal");
            Directory.CreateDirectory(dir);
            log.WriteLine(dev ? "--- deviation D-1 (tooling not limiting) + D-2 (I5 on effective floor)" : "--- literal §1");
            Write(dir, variants, results, log);
        }
        return 0;
    }

    /// <summary>Diagnostic trace "trace:ACTOR:rule-index:alpha:delta" of the K6 stand (mastered set, R-H-MS, rate 25).</summary>
    private static int Trace(GameData data, ScenarioData sc, string spec, TextWriter log)
    {
        string[] f = spec.Split(':');
        int actor = sc.ActorIndex(f[0]);
        RuleVariant rv = Variants()[int.Parse(f[1], Inv)];
        var s = new RuleStand(data, sc, actor, Params(long.Parse(f[2], Inv), long.Parse(f[3], Inv), 100), rv, RNorm, -1, RunSet.Mastered, StartMod.None, true);
        IndustryLayout l = s.Layout;
        log.WriteLine($"trace {rv.Label} {f[0]}; notes: {string.Join("; ", s.Notes)}");
        for (int t = 0; t < Horizon; t++)
        {
            s.Step();
            if (t % 12 != 0 && t > 24)
                continue;
            var parts = new List<string>();
            for (int r = 0; r < l.ProcessCount; r++)
            {
                if (s.Running(r))
                    parts.Add($"{l.Number[r]}:y{s.Y[r].Yield.ToDouble():0.000} gH{s.GH(r).ToDouble():0.000} v{(s.V[r] > Fx6.Zero ? 1 : 0)}");
            }
            string cells = string.Join(' ', Enumerable.Range(0, l.Clusters.Length).Select(c => $"{l.Clusters[c]}H{s.H[c * 3].ToDouble():0.00}/{s.H[c * 3 + 1].ToDouble():0.00}/{s.H[c * 3 + 2].ToDouble():0.00}M{s.M[c * 3].ToDouble():0.00}/{s.M[c * 3 + 1].ToDouble():0.00}/{s.M[c * 3 + 2].ToDouble():0.00}"));
            log.WriteLine($"m{t} {string.Join(' ', parts)} | {cells}");
        }
        for (int r = 0; r < l.ProcessCount; r++)
        {
            if (s.Running(r))
                log.WriteLine($"req {l.Number[r]} {l.Clusters[l.ClusterIndex[r]]} {l.Req[r]}");
        }
        return 0;
    }

    // ------------------------------------------------------------------ prototype check against the core stand

    /// <summary>R0 in <see cref="RuleStand"/> must reproduce the core <c>IndustrySystem</c> stand of T4s bit for bit.</summary>
    public static bool Check(GameData data, ScenarioData sc, TextWriter log)
    {
        int cases = 0, bad = 0;
        var r0 = new RuleVariant(CultureRule.R0, RNorm, false, Fx6.FromRaw(250_000));
        foreach (long a in new long[] { 20_000, 80_000 })
        {
            IndustryParams ip = Params(a, 3_000, 100);
            var configs = new List<(int Actor, int Target, string Variant)>();
            foreach ((int proc, int _) in SmokeT4s.KeyProcesses)
                for (int actor = 0; actor < sc.ActorCount; actor++)
                    foreach (string v in new[] { "solo", "cluster", "edge" })
                        configs.Add((actor, proc, v));
            configs.Add((sc.ActorIndex("GBR"), -1, "c1"));
            foreach ((int actor, int target, string v) in configs)
            {
                var core = v == "c1"
                    ? new SmokeT4s.LineStand(data, sc, actor, ip, -1, "c1", RNorm, SmokeT4s.G2Superior)
                    : new SmokeT4s.LineStand(data, sc, actor, ip, target, v, RNorm);
                var mine = v == "c1"
                    ? new RuleStand(data, sc, actor, ip, r0, RNorm, -1, RunSet.PrcSet, StartMod.None, false, SmokeT4s.G2Superior)
                    : new RuleStand(data, sc, actor, ip, r0, RNorm, target, v == "cluster" ? RunSet.Cluster : RunSet.Solo,
                        v == "edge" ? StartMod.Edge : StartMod.None, false);
                cases++;
                for (long t = 0; t < 240; t++)
                {
                    core.StepMonth(t);
                    mine.Step();
                    if (!Same(core.Industry, mine))
                    {
                        log.WriteLine($"CHECK MISMATCH actor {sc.ActorId[actor]} target {target} {v} α {a} month {t}");
                        bad++;
                        break;
                    }
                }
            }
        }
        log.WriteLine($"check: R0 prototype vs core stand, {cases} cases × 240 months, mismatches {bad}");
        return bad == 0;
    }

    private static bool Same(IndustrySystem ind, RuleStand s)
    {
        IndustryLayout l = s.Layout;
        for (int c = 0; c < l.Clusters.Length; c++)
        {
            for (int d = 0; d < 3; d++)
            {
                if (ind.CultureH(0, c, (Dimension)d) != s.H[c * 3 + d] || ind.ToolingM(0, c, (Dimension)d) != s.M[c * 3 + d])
                    return false;
            }
        }
        for (int r = 0; r < l.ProcessCount; r++)
        {
            if (ind.ProcX(0, r) != s.X[r] || ind.ProcV(0, r) != s.V[r])
                return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ evaluation of one variant

    internal sealed class VariantResult
    {
        public readonly Dictionary<long, double> C1MaxDy = new();
        public readonly List<long> Window = new();
        public readonly int[] K1Fail = new int[7]; // I1..I5 (raw maint), continuity (effective floor), I5 on the effective floor (D-2)
        public double K1MaxJump, K1MaxJumpEff;
        public int K2aN, K2aFail, K2bN, K2bFail;
        public double K2aWorstG = double.NaN, K2bWorstRatio = double.NaN;
        public string K2aWorstCase = "", K2bWorstCase = "";
        public int K3N, K3Fail;
        public double K3WorstExcess = double.NaN, K3MinRatio = double.NaN;
        public string K3WorstCase = "";
        public int K4N, K4aFail, K4bFail, K4cFail;
        public double K4WorstT5Ratio = double.NaN;
        public string K4WorstCase = "";
        public readonly List<string> K4Table = new();
        public int K6Fail, K6DimFail;
        public double K6MinGap = 10;
        public string K6GapCase = "";
        public double K6MinG = double.NaN;
        public string K6WorstCase = "";
        public double D2MinG = double.NaN;
        public readonly List<string> D1 = new(), D3 = new(), C2 = new(), Notes = new();
        public int Excluded;

        // Literal §1: raw I5 and K6 on the product g_H; deviations D-2, D-3: I5 on the effective floor, K6 per dimension.
        public bool Pass => Literal ? PassCore && K1Fail[4] == 0 && K6Fail == 0 : PassCore && K1Fail[6] == 0 && K6DimFail == 0;

        public bool Literal { get; init; } = true;

        private bool PassCore => Window.Count > 0 && K1Fail[0] == 0 && K1Fail[1] == 0 && K1Fail[2] == 0 && K1Fail[3] == 0 && K1Fail[5] == 0 && K2aFail == 0 && K2bFail == 0 && K3Fail == 0
            && K4aFail == 0 && K4bFail == 0 && K4cFail == 0;
    }

    private static VariantResult Evaluate(GameData data, ScenarioData sc, RuleVariant rv, bool deviation)
    {
        var res = new VariantResult { Literal = !deviation };
        RuleStand.FreeTooling.Value = false;
        K0(data, sc, rv, res); // the α window is always the literal §1 K0 (real tooling)
        K1(rv, res);
        RuleStand.FreeTooling.Value = deviation;
        foreach (long a in res.Window)
        {
            foreach (long dh in Deltas)
            {
                IndustryParams ip = Params(a, dh, 100);
                for (int actor = 0; actor < sc.ActorCount; actor++)
                {
                    foreach ((int proc, int _) in SmokeT4s.KeyProcesses)
                    {
                        string id = $"{sc.ActorId[actor]}-{proc} α{Fx6.FromRaw(a)} δ{Fx6.FromRaw(dh)}";
                        K2(data, sc, rv, ip, actor, proc, id, res);
                        K34(data, sc, rv, ip, actor, proc, id, res);
                    }
                    K6(data, sc, rv, ip, actor, $"{sc.ActorId[actor]} α{Fx6.FromRaw(a)} δ{Fx6.FromRaw(dh)}", dh == 3_000, res);
                }
            }
            C2Report(data, sc, rv, a, res);
            D3(data, sc, rv, a, res);
        }
        return res;
    }

    // K0: α window by C1 (first 3 doublings d_y ≤ 0.409 for all δ, x_scale, both starts).
    private static void K0(GameData data, ScenarioData sc, RuleVariant rv, VariantResult res)
    {
        int gbr = sc.ActorIndex("GBR");
        foreach (long a in Alphas)
        {
            double worst = 0;
            foreach (long dh in Deltas)
                foreach (int x in new[] { 50, 100, 200 })
                    foreach (bool rhms in new[] { false, true })
                    {
                        var s = new RuleStand(data, sc, gbr, Params(a, dh, x), rv, RNorm, -1, RunSet.PrcSet, StartMod.None, rhms, SmokeT4s.G2Superior);
                        double[] u = UnitCost(s, 8, out _, out _);
                        for (int i = 0; i < 3; i++)
                            worst = Math.Max(worst, Math.Log(u[(1 << i) - 1]) - Math.Log(u[(2 << i) - 1]));
                    }
            res.C1MaxDy[a] = worst;
            if (worst <= 0.409)
                res.Window.Add(a);
        }
    }

    /// <summary>unit_cost_mult per month from the snapshot (month t's yields), plus per-month maturity flags.</summary>
    private static double[] UnitCost(RuleStand s, int months, out bool[] matureA18, out bool[] matureF2b)
    {
        var u = new double[months];
        matureA18 = new bool[months];
        matureF2b = new bool[months];
        IndustryLayout l = s.Layout;
        var items = new ProfileItem[SmokeT4s.G2Superior.Length];
        Fx6 three = Fx6.FromInt(3);
        for (int t = 0; t < months; t++)
        {
            // Snapshot X before the step (F2-b uses the accumulated output in force in month t).
            var x0 = (Fx6[])s.X.Clone();
            s.Step();
            bool a18 = true, f2b = true;
            for (int i = 0; i < items.Length; i++)
            {
                (int num, long baseRaw) = SmokeT4s.G2Superior[i];
                int r = l.RowOf(num);
                ProcessYield y = s.Y[r];
                items[i] = new ProfileItem(num, y, true, Fx6.One, Fx6.FromRaw(baseRaw));
                a18 &= y.Yield >= G09;
                f2b &= y.Yield >= G09 * y.Ramp && x0[r] >= three * y.XHalfEff;
            }
            u[t] = IndustryMath.Profile(items, Fx6.FromRaw(10_000)).UnitCostMult.ToDouble();
            matureA18[t] = a18;
            matureF2b[t] = f2b;
        }
        return u;
    }

    // K1: invariants I1–I5 on deterministic random cells.
    private static void K1(RuleVariant rv, VariantResult res)
    {
        var rng = new SplitMix(79080);
        Cluster[] others = [Cluster.MET, Cluster.CHEM, Cluster.MECH];
        double maxJump = 0, maxJumpEff = 0;
        for (int k = 0; k < 20_000; k++)
        {
            IndustryParams p = Params(Alphas[rng.Next(5)], Deltas[rng.Next(3)], 100);
            Fx6 hcap = Fx6.FromRaw(3_000_000 + rng.Next(3_000_000));
            Fx6 h = Fx6.FromRaw(200_000 + rng.Next((int)(hcap.Raw - 200_000 + 300_000)));
            var cell = new CultureCell(h, hcap, Fx6.FromRaw(500_000 + rng.Next(500_001)), Fx6.FromRaw(500_000 + rng.Next(1_000_001)), false);
            int n = 1 + rng.Next(6);
            var nums = new SortedSet<int>();
            while (nums.Count < n)
                nums.Add(2 + 2 * rng.Next(30)); // even numbers; odd ones are free for inserted processes
            var procs = new List<RuleProc>();
            foreach (int num in nums)
                procs.Add(RandomProc(rng, rv, num));
            var spill = new CultureSpill[3];
            for (int i = 0; i < 3; i++)
                spill[i] = new CultureSpill(p.Spill(Cluster.AERO, others[i]), Fx6.FromRaw(rng.Next(5_500_000)));
            CultureStep st = CultureRuleMath.Step(rv, cell, procs.ToArray(), spill, p);
            Fx6 dh = st.HNew - h;

            // I1: an easy process (r + 0.25 ≤ H) leaves drive unchanged and does not lower ΔH.
            if (h >= Fx6.FromRaw(250_000))
            {
                RuleProc easy = RandomProc(rng, rv, 1 + 2 * rng.Next(30));
                easy = easy with { R = Fx6.FromRaw(rng.Next((int)(h.Raw - 250_000) + 1)) };
                CultureStep st1 = CultureRuleMath.Step(rv, cell, Insert(procs, easy), spill, p);
                if (st1.Drive != st.Drive || st1.HNew - h < dh)
                    res.K1Fail[0]++;
            }
            // I2: drive ≤ max gap.
            Fx6 maxGap = Fx6.Zero;
            foreach (RuleProc q in procs)
            {
                Fx6 g = CultureRuleMath.Max0(q.R + Fx6.FromRaw(250_000) - h);
                if (g > maxGap)
                    maxGap = g;
            }
            if (st.Drive > maxGap)
                res.K1Fail[1]++;
            // I3: duplicating a process changes ΔH by at most 2 raw.
            RuleProc src = procs[rng.Next(procs.Count)];
            CultureStep st3 = CultureRuleMath.Step(rv, cell, Insert(procs, src with { Number = 1 + 2 * rng.Next(30) }), spill, p);
            if (Math.Abs((st3.HNew - h - dh).Raw) > 2)
                res.K1Fail[2]++;
            // I4.
            if (st.DHPlusCapped > p.RhoMax || st.HNew < (h < st.HFloor ? h : st.HFloor) || st.HNew < Fx6.Zero)
                res.K1Fail[3]++;
            // I5 + continuity: sweep v of one process over [0; 2·v_full] in 2000 steps (v = 0: process absent).
            if (k % 40 == 0)
            {
                int j = rng.Next(procs.Count);
                Fx6 prevPlus = Fx6.Zero, prevMaint = Fx6.Zero, prevEff = Fx6.Zero;
                bool mono = true, monoEff = true;
                for (int i = 0; i <= 2000; i++)
                {
                    var list = new List<RuleProc>(procs);
                    if (i == 0)
                        list.RemoveAt(j);
                    else
                        list[j] = list[j] with { V = Fx6.FromRaw(list[j].VFull.Raw * 2 * i / 2000) };
                    if (i > 0 && list[j].V == Fx6.Zero)
                        list.RemoveAt(j);
                    CultureStep s5 = CultureRuleMath.Step(rv, cell, list.ToArray(), spill, p);
                    Fx6 eff = s5.Maint > s5.HFloor ? s5.Maint : s5.HFloor;
                    if (i > 0)
                    {
                        if (s5.DHPlus < prevPlus || s5.Maint < prevMaint)
                            mono = false;
                        if (s5.DHPlus < prevPlus || eff < prevEff)
                            monoEff = false;
                        maxJump = Math.Max(maxJump, Math.Abs((s5.Maint - prevMaint).ToDouble()));
                        maxJumpEff = Math.Max(maxJumpEff, Math.Abs((eff - prevEff).ToDouble()));
                    }
                    prevPlus = s5.DHPlus;
                    prevMaint = s5.Maint;
                    prevEff = eff;
                }
                if (!mono)
                    res.K1Fail[4]++;
                if (!monoEff)
                    res.K1Fail[6]++;
            }
        }
        res.K1MaxJump = maxJump;
        res.K1MaxJumpEff = maxJumpEff;
        if (maxJumpEff > 0.05)
            res.K1Fail[5]++;
    }

    private static RuleProc RandomProc(SplitMix rng, RuleVariant rv, int number)
    {
        Fx6 r = rng.Next(5) == 0 ? Fx6.Zero : Fx6.FromRaw(1_000_000 + 50_000 * rng.Next(91));
        Fx6 vFull = rv.RFull * Fx6.FromInt(100 + rng.Next(7401));
        Fx6 v = Fx6.FromRaw(1 + rng.NextLong(2 * vFull.Raw));
        return new RuleProc(number, v, r, false, vFull);
    }

    private static RuleProc[] Insert(List<RuleProc> procs, RuleProc q)
    {
        var list = new List<RuleProc>(procs.Where(x => x.Number != q.Number)) { q };
        list.Sort((a, b) => a.Number.CompareTo(b.Number));
        return [.. list];
    }

    // K2: no smouldering shop (hold from frontier; learn from the edge vs norm).
    private static void K2(GameData data, ScenarioData sc, RuleVariant rv, IndustryParams ip, int actor, int proc, string id, VariantResult res)
    {
        var hold = new RuleStand(data, sc, actor, ip, rv, RTok, proc, RunSet.Solo, StartMod.Hold, false);
        int tr = hold.Target;
        bool started = false;
        for (int t = 0; t < 1200; t++)
        {
            hold.Step();
            started |= hold.V[tr] > Fx6.Zero;
        }
        if (started)
        {
            res.K2aN++;
            double g = hold.GH(tr).ToDouble();
            if (double.IsNaN(res.K2aWorstG) || g > res.K2aWorstG)
            {
                res.K2aWorstG = g;
                res.K2aWorstCase = id;
            }
            if (hold.GH(tr) >= G09)
                res.K2aFail++;
        }
        else
        {
            res.Excluded++;
        }
        var tok = new RuleStand(data, sc, actor, ip, rv, RTok, proc, RunSet.Solo, StartMod.Edge, false);
        var norm = new RuleStand(data, sc, actor, ip, rv, RNorm, proc, RunSet.Solo, StartMod.Edge, false);
        Fx6 h0 = norm.TargetHSum();
        for (int t = 0; t < 120; t++)
        {
            tok.Step();
            norm.Step();
        }
        double gNorm = (norm.TargetHSum() - h0).ToDouble(), gTok = (tok.TargetHSum() - h0).ToDouble();
        if (gNorm >= 0.05)
        {
            res.K2bN++;
            double ratio = gTok / gNorm;
            if (double.IsNaN(res.K2bWorstRatio) || ratio > res.K2bWorstRatio)
            {
                res.K2bWorstRatio = ratio;
                res.K2bWorstCase = id;
            }
            if (ratio > 0.25)
                res.K2bFail++;
        }
    }

    /// <summary>Months until g_H(target) ≥ 0.9 (0 if already at start), −1 = censored; also whether the line ever ran.</summary>
    private static (int T, bool Started) TimeToG(RuleStand s, int horizon)
    {
        if (s.GH(s.Target) >= G09)
            return (0, true);
        bool started = false;
        for (int t = 0; t < horizon; t++)
        {
            s.Step();
            started |= s.V[s.Target] > Fx6.Zero;
            if (s.GH(s.Target) >= G09)
                return (t + 1, true);
        }
        return (-1, started);
    }

    private static double Inf(int t) => t < 0 ? double.PositiveInfinity : t;

    // K3 (solo ≈ cluster at rate ≥ norm) and K4 (monotone with saturation).
    private static void K34(GameData data, ScenarioData sc, RuleVariant rv, IndustryParams ip, int actor, int proc, string id, VariantResult res)
    {
        Fx6[] rates = [RTok, Fx6.FromInt(5), Fx6.FromInt(10), RNorm, Fx6.FromInt(50)];
        int horizon = res.Literal ? Horizon : 1200; // D-4
        var T = new double[rates.Length];
        bool started25 = false;
        for (int i = 0; i < rates.Length; i++)
        {
            (int t, bool st) = TimeToG(new RuleStand(data, sc, actor, ip, rv, rates[i], proc, RunSet.Solo, StartMod.Edge, false), horizon);
            T[i] = Inf(t);
            if (i == 3)
                started25 = st;
        }
        if (!started25)
            return;
        res.K4N++;
        for (int i = 1; i < rates.Length; i++)
        {
            if (T[i] > T[i - 1] + 1)
            {
                res.K4aFail++;
                break;
            }
        }
        if (Math.Abs(T[4] - T[3]) > 1 && !(double.IsInfinity(T[4]) && double.IsInfinity(T[3])))
            res.K4bFail++;
        if (!double.IsInfinity(T[3]))
        {
            double r5 = T[3] == 0 ? (T[1] == 0 ? 1 : double.PositiveInfinity) : T[1] / T[3];
            bool ok = !double.IsInfinity(T[1]) && T[1] <= 2 * T[3] && T[2] <= 1.5 * T[3];
            if (!ok)
            {
                res.K4cFail++;
                if (res.K4Table.Count < 400)
                    res.K4Table.Add($"FAIL {id}: {string.Join(' ', T.Select(D))}");
            }
            if (double.IsNaN(res.K4WorstT5Ratio) || r5 > res.K4WorstT5Ratio)
            {
                res.K4WorstT5Ratio = r5;
                res.K4WorstCase = id;
            }
        }
        if (id.EndsWith("α0.050000 δ0.003000", StringComparison.Ordinal))
            res.K4Table.Add($"{id}: {string.Join(' ', T.Select(D))}");

        foreach (Fx6 rate in new[] { RNorm, Fx6.FromInt(50) })
        {
            double solo = rate == RNorm ? T[3] : T[4];
            double cl = Inf(TimeToG(new RuleStand(data, sc, actor, ip, rv, rate, proc, RunSet.Cluster, StartMod.Edge, false), horizon).T);
            res.K3N++;
            bool ok = double.IsInfinity(solo) || cl <= solo + Math.Max(6, 0.1 * solo);
            if (!ok)
                res.K3Fail++;
            double excess = cl - solo;
            if (!double.IsNaN(excess) && (double.IsNaN(res.K3WorstExcess) || excess > res.K3WorstExcess))
            {
                res.K3WorstExcess = excess;
                res.K3WorstCase = $"{id} r{rate}: solo {D(solo)} cluster {D(cl)}";
            }
            if (!double.IsInfinity(solo) && solo > 0)
            {
                double ratio = cl / solo;
                if (double.IsNaN(res.K3MinRatio) || ratio < res.K3MinRatio)
                    res.K3MinRatio = ratio;
            }
        }
    }

    // K6: R-H-MS stability at rate 25 (D2: rate 5), D1 mean H at month 120.
    private static void K6(GameData data, ScenarioData sc, RuleVariant rv, IndustryParams ip, int actor, string id, bool d1, VariantResult res)
    {
        foreach (Fx6 rate in new[] { RNorm, Fx6.FromInt(5) })
        {
            var s = new RuleStand(data, sc, actor, ip, rv, rate, -1, RunSet.Mastered, StartMod.None, true);
            if (rate == RNorm)
            {
                foreach (string n in s.Notes)
                {
                    string note = $"{sc.ActorId[actor]}: {n}";
                    if (!res.Notes.Contains(note))
                        res.Notes.Add(note);
                }
            }
            double h0 = s.MeanH();
            double minG = 1, minGap = 10;
            int worstP = 0, worstGapP = 0;
            for (int t = 0; t < Horizon; t++)
            {
                s.Step();
                for (int r = 0; r < s.Layout.ProcessCount; r++)
                {
                    if (!s.Running(r))
                        continue;
                    double g = s.GH(r).ToDouble();
                    if (g < minG)
                    {
                        minG = g;
                        worstP = s.Layout.Number[r];
                    }
                    int c = s.Layout.ClusterIndex[r];
                    for (int d = 0; d < 3; d++)
                    {
                        Fx6 req = s.Layout.Req[r][d];
                        double gap = (s.H[c * 3 + d] - req).ToDouble();
                        if (req > Fx6.Zero && gap < minGap)
                        {
                            minGap = gap;
                            worstGapP = s.Layout.Number[r];
                        }
                    }
                }
                if (t == 119 && d1 && rate == RNorm)
                    res.D1.Add($"{id} mastered@25: mean H {h0:0.000} → {s.MeanH():0.000}");
            }
            if (rate == RNorm)
            {
                if (minG < 0.81)
                    res.K6Fail++;
                if (minGap < -0.1)
                    res.K6DimFail++;
                if (minGap < res.K6MinGap)
                {
                    res.K6MinGap = minGap;
                    res.K6GapCase = $"{id} p{worstGapP}";
                }
                if (double.IsNaN(res.K6MinG) || minG < res.K6MinG)
                {
                    res.K6MinG = minG;
                    res.K6WorstCase = $"{id} p{worstP}";
                }
            }
            else if (double.IsNaN(res.D2MinG) || minG < res.D2MinG)
            {
                res.D2MinG = minG;
            }
        }
    }

    // K5: C2 printed only (p50 t_first → t_07 by yield, tier 4 / tier 5, edge and cluster from the scenario start, δ_H 0.003).
    private static void C2Report(GameData data, ScenarioData sc, RuleVariant rv, long a, VariantResult res)
    {
        IndustryParams ip = Params(a, 3_000, 100);
        foreach ((string name, RunSet run, StartMod mod) in new[] { ("edge", RunSet.Solo, StartMod.Edge), ("cluster", RunSet.Cluster, StartMod.None) })
        {
            var byTier = new Dictionary<int, List<double>> { [4] = new(), [5] = new() };
            var never = new Dictionary<int, int> { [4] = 0, [5] = 0 };
            foreach ((int proc, int tier) in SmokeT4s.KeyProcesses)
            {
                for (int actor = 0; actor < sc.ActorCount; actor++)
                {
                    if (SmokeT4s.IsMastered(data, sc, actor, proc))
                        continue;
                    var s = new RuleStand(data, sc, actor, ip, rv, RNorm, proc, run, mod, false);
                    long first = -1, t07 = -1;
                    for (long t = 0; t < Horizon; t++)
                    {
                        s.Step();
                        if (first < 0 && s.V[s.Target] > Fx6.Zero)
                            first = t;
                        if (t07 < 0 && s.Y[s.Target].Yield >= Fx6.FromRaw(700_000))
                        {
                            t07 = t;
                            break;
                        }
                    }
                    if (first < 0)
                        never[tier]++;
                    else
                        byTier[tier].Add(t07 >= 0 ? (t07 - first) / 12.0 : double.PositiveInfinity);
                }
            }
            res.C2.Add($"α{Fx6.FromRaw(a)} {name}: t4 p50 {D(P50(byTier[4]))} (n {byTier[4].Count}, never {never[4]}), t5 p50 {D(P50(byTier[5]))} (n {byTier[5].Count}, never {never[5]})");
        }
    }

    // D3: maturity of the C1 reference line (A18 and F2-b definitions), δ_H 0.003, x 1, both starts.
    private static void D3(GameData data, ScenarioData sc, RuleVariant rv, long a, VariantResult res)
    {
        int gbr = sc.ActorIndex("GBR");
        foreach (bool rhms in new[] { false, true })
        {
            var s = new RuleStand(data, sc, gbr, Params(a, 3_000, 100), rv, RNorm, -1, RunSet.PrcSet, StartMod.None, rhms, SmokeT4s.G2Superior);
            double[] u = UnitCost(s, 1024, out bool[] a18, out bool[] f2b);
            int firstA = Array.IndexOf(a18, true), firstB = Array.IndexOf(f2b, true);
            double matureMax = double.NaN;
            for (int i = 0; (2 << i) <= 1024; i++)
            {
                int from = (1 << i) - 1, to = (2 << i) - 1;
                if (firstB >= 0 && from >= firstB)
                {
                    double dy = Math.Log(u[from]) - Math.Log(u[to]);
                    matureMax = double.IsNaN(matureMax) ? dy : Math.Max(matureMax, dy);
                }
            }
            res.D3.Add($"α{Fx6.FromRaw(a)} {(rhms ? "R-H-MS" : "scenario")}: A18 first month {firstA}, F2-b first month {firstB}, max d_y of doublings starting in F2-b maturity {D(matureMax)}, unit_cost_mult m0 {u[0]:0.00} m1023 {u[1023]:0.000}");
        }
    }

    private static double P50(List<double> xs)
    {
        if (xs.Count == 0)
            return double.NaN;
        double[] s = [.. xs.Order()];
        return s[(int)Math.Floor(0.5 * (s.Length - 1))];
    }

    private static string D(double x) => double.IsNaN(x) ? "" : double.IsPositiveInfinity(x) ? "inf" : x.ToString("0.###", Inv);

    // ------------------------------------------------------------------ output

    private static void Write(string outDir, List<RuleVariant> variants, VariantResult[] results, TextWriter log)
    {
        using var sum = new StreamWriter(Path.Combine(outDir, "summary.csv"));
        sum.WriteLine("variant,pass,window,c1_maxdy_by_alpha,K1_I1,K1_I2,K1_I3,K1_I4,K1_I5,K1_cont,K1_I5eff,maint_jump_raw,maint_jump_eff,K2a_fail,K2a_n,K2a_worst_g,K2b_fail,K2b_n,K2b_worst_ratio,K3_fail,K3_n,K3_worst_excess,K3_min_ratio,K4a,K4b,K4c,K4_n,K4_worst_T5_ratio,K6_fail,K6_min_g,D2_min_g_rate5,excluded");
        using var det = new StreamWriter(Path.Combine(outDir, "details.txt"));
        for (int i = 0; i < variants.Count; i++)
        {
            VariantResult r = results[i];
            string dy = string.Join(' ', r.C1MaxDy.Select(kv => $"{Fx6.FromRaw(kv.Key)}:{kv.Value:0.000}"));
            string win = string.Join(' ', r.Window.Select(a => Fx6.FromRaw(a).ToString()));
            sum.WriteLine(string.Join(',', variants[i].Label, r.Pass ? 1 : 0, win, dy, r.K1Fail[0], r.K1Fail[1], r.K1Fail[2], r.K1Fail[3], r.K1Fail[4], r.K1Fail[5], r.K1Fail[6],
                D(r.K1MaxJump), D(r.K1MaxJumpEff), r.K2aFail, r.K2aN, D(r.K2aWorstG), r.K2bFail, r.K2bN, D(r.K2bWorstRatio), r.K3Fail, r.K3N, D(r.K3WorstExcess), D(r.K3MinRatio),
                r.K4aFail, r.K4bFail, r.K4cFail, r.K4N, D(r.K4WorstT5Ratio), r.K6Fail, D(r.K6MinG), D(r.D2MinG), r.Excluded));
            log.WriteLine($"{variants[i].Label,-24} pass={(r.Pass ? 1 : 0)} window=[{win}] K1={string.Join('/', r.K1Fail)} K2a={r.K2aFail}/{r.K2aN} (g {D(r.K2aWorstG)}) K2b={r.K2bFail}/{r.K2bN} (ratio {D(r.K2bWorstRatio)}) K3={r.K3Fail}/{r.K3N} K4={r.K4aFail}/{r.K4bFail}/{r.K4cFail} of {r.K4N} (T5/T25 {D(r.K4WorstT5Ratio)}) K6={r.K6Fail} (min g {D(r.K6MinG)}) K6dim={r.K6DimFail} (min H−r {D(r.K6MinGap)}) D2 {D(r.D2MinG)}");
            det.WriteLine($"== {variants[i].Label} pass={r.Pass}");
            det.WriteLine($"C1 max d_y (first 3 doublings) by α: {dy}");
            det.WriteLine($"K1 maint jump raw {D(r.K1MaxJump)}, effective {D(r.K1MaxJumpEff)}");
            det.WriteLine($"K2a worst {r.K2aWorstCase} g {D(r.K2aWorstG)}; K2b worst {r.K2bWorstCase} ratio {D(r.K2bWorstRatio)}");
            det.WriteLine($"K3 worst {r.K3WorstCase}; min ratio cluster/solo {D(r.K3MinRatio)}");
            det.WriteLine($"K4 worst T5/T25 {r.K4WorstCase} {D(r.K4WorstT5Ratio)}");
            foreach (string s in r.K4Table) det.WriteLine($"  K4 T(1.25,5,10,25,50) {s}");
            det.WriteLine($"K6 min g {D(r.K6MinG)} at {r.K6WorstCase}; per-dim (D-3) fails {r.K6DimFail}, min H−r {D(r.K6MinGap)} at {r.K6GapCase}; D2 (rate 5) min g {D(r.D2MinG)}");
            foreach (string s in r.Notes) det.WriteLine($"  note {s}");
            foreach (string s in r.D1) det.WriteLine($"  D1 {s}");
            foreach (string s in r.D3) det.WriteLine($"  D3 {s}");
            foreach (string s in r.C2) det.WriteLine($"  C2 {s}");
        }
    }

    /// <summary>SplitMix64 for the property cases of K1 (analysis input, not world state).</summary>
    private sealed class SplitMix(ulong seed)
    {
        private ulong _s = seed;

        public ulong NextU()
        {
            ulong z = _s += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public int Next(int n) => (int)(NextU() % (ulong)n);

        public long NextLong(long n) => (long)(NextU() % (ulong)n);
    }
}
