using Singularity.Kernel;

namespace Singularity.Core.Intelligence;

/// <summary>What an actor knows about a target's knowledge, with the honesty of the source (INTELLIGENCE.md §3): a range, never the exact value.</summary>
/// <param name="Visible">The layer is shown: <c>intel_conf ≥ ConfVisible</c>.</param>
/// <param name="Low">Lower end of the range of nodes the target knows.</param>
/// <param name="High">Upper end.</param>
/// <param name="Confidence"><c>conf = 1 − (1 − q)^s</c>, the share the help text shows (#260), derived from the number of sources.</param>
/// <param name="Sources">Independent sources left after false trails.</param>
/// <param name="Distorted">The target's false trails corrupted some of the sources and the estimate is biased: "false confidence".</param>
public readonly record struct IntelEstimate(bool Visible, int Low, int High, Fx6 Confidence, int Sources, bool Distorted);

/// <summary>
/// Intelligence of the slice (INTELLIGENCE.md rev 3, #515): networks <c>N[o,t]</c> with maturity <c>A[o,t]</c>, the trace <c>trace[o,t]</c>, exposure with the RNG
/// keyed by the network, counter-intelligence and the theft channel that the knowledge layer reads as <see cref="StealShare"/> (only in <c>c_eff</c>, O-051).
/// Columns <c>int.*</c>: row = the actor <c>o</c>, one column per target <c>t</c>; this system is the only writer. Plans: the player's plan (a command) or the AI plan,
/// decided each January from the snapshot with the same rules for every actor and kept in <c>int.ai.*</c>. Monthly; yearly norms (ADR-0027).
/// </summary>
public sealed class IntelSystem : ITickSystem
{
    public const int DefaultSystemId = 600;

    /// <summary>The RNG system of the exposure draw (ADR-0021 stream <c>intel.expose</c>), on the events stream; key <c>(month, 7, o·2^16 + t, 0)</c>.</summary>
    public const int SystemExpose = 7;

    private readonly IntelParams _p;
    private readonly IIntelInputs _in;
    private readonly CounterRng _events;
    private readonly int _rows;
    private readonly LongColumn[] _n, _a, _trace;
    private readonly IntColumn[] _lost;
    private readonly IntColumn[] _aiTarget, _aiMode;
    private readonly Fx6 _growthM, _decayM, _matM, _traceDecayM;

    public IntelSystem(Table table, IntelParams parameters, IIntelInputs inputs, CounterRng events, int systemId = DefaultSystemId)
    {
        _p = parameters;
        _in = inputs;
        _events = events;
        _rows = table.RowCount;
        if (_rows > (1 << 16))
            throw new ArgumentOutOfRangeException(nameof(table), _rows, "The exposure key holds 16 bits per actor.");
        _growthM = IntelMath.PerMonth(parameters.GrowthPerYear);
        _decayM = IntelMath.PerMonth(parameters.DecayPerYear);
        _matM = IntelMath.PerMonth(parameters.MaturityPerYear);
        _traceDecayM = IntelMath.PerMonth(parameters.TraceDecayPerYear);
        _n = new LongColumn[_rows];
        _a = new LongColumn[_rows];
        _trace = new LongColumn[_rows];
        _lost = new IntColumn[_rows];
        for (int t = 0; t < _rows; t++)
        {
            _n[t] = table.AddLong($"int.n.{t}");
            _a[t] = table.AddLong($"int.a.{t}");
            _trace[t] = table.AddLong($"int.tr.{t}");
            _lost[t] = table.AddInt($"int.lost.{t}");
        }
        _aiTarget = new IntColumn[IntelParams.MaxTargets];
        _aiMode = new IntColumn[IntelParams.MaxTargets];
        for (int k = 0; k < IntelParams.MaxTargets; k++)
        {
            _aiTarget[k] = table.AddInt($"int.ai.tgt.{k}");
            _aiMode[k] = table.AddInt($"int.ai.mode.{k}");
        }
        SystemId = systemId;
    }

    public int SystemId { get; }

    public Cadence Cadence => Cadence.Monthly;

    public IntelParams Parameters => _p;

    // ---- snapshot reads (pure functions of Current)

    public Fx6 Network(int o, int t) => Fx6.FromRaw(_n[t].Current[o]);

    public Fx6 Maturity(int o, int t) => Fx6.FromRaw(_a[t].Current[o]);

    public Fx6 Trace(int o, int t) => Fx6.FromRaw(_trace[t].Current[o]);

    /// <summary>How many times the network of <c>o</c> in <c>t</c> was exposed.</summary>
    public int Lost(int o, int t) => _lost[t].Current[o];

    /// <summary>The plan of the actor in force: the player's when set, else the AI's. False for an empty slot.</summary>
    public bool Plan(int actorRow, int slot, out int target, out IntelMode mode)
    {
        if (_in.PlayerPlan(actorRow, slot, out target, out mode))
            return target >= 0 && mode != IntelMode.None && target < _rows && target != actorRow;
        int tg = _aiTarget[slot].Current[actorRow] - 1;
        target = tg;
        mode = (IntelMode)_aiMode[slot].Current[actorRow];
        return tg >= 0 && mode != IntelMode.None;
    }

    /// <summary>The mode of the actor against a target (None when it is not a target).</summary>
    public IntelMode ModeAgainst(int actorRow, int target)
    {
        for (int k = 0; k < IntelParams.MaxTargets; k++)
        {
            if (Plan(actorRow, k, out int t, out IntelMode m) && t == target)
                return m;
        }
        return IntelMode.None;
    }

    private int TargetCount(int actorRow)
    {
        int c = 0;
        for (int k = 0; k < IntelParams.MaxTargets; k++)
            c += Plan(actorRow, k, out _, out _) ? 1 : 0;
        return c;
    }

    /// <summary><c>ci_eff = sat(ci_budget / ref)</c> of the actor: the counter-intelligence part of its security item.</summary>
    public Fx6 CiEff(int actorRow) => IntelMath.Sat(_in.SecurityShare(actorRow) * _p.CiShare / _p.CiShareRef);

    /// <summary>
    /// The discount of the theft channel on node <paramref name="nodeRow"/> for the actor, a share of the node cost: the largest <c>s_steal</c> over its targets
    /// in the mode <see cref="IntelMode.Steal"/> that know the node (the target must know it, O-051). Zero when nothing is stolen.
    /// </summary>
    public Fx6 StealShare(int actorRow, int nodeRow)
    {
        Fx6 best = Fx6.Zero;
        for (int k = 0; k < IntelParams.MaxTargets; k++)
        {
            if (!Plan(actorRow, k, out int t, out IntelMode m) || m != IntelMode.Steal || !_in.Knows(t, nodeRow))
                continue;
            Fx6 s = IntelMath.StealShare(_p, Network(actorRow, t), Maturity(actorRow, t));
            if (s > best)
                best = s;
        }
        return best;
    }

    /// <summary>The yearly exposure hazard of the network now (the number behind the risk word).</summary>
    public Fx6 HazardPerYear(int o, int t) =>
        IntelMath.HazardPerYear(_p, Network(o, t), IntelMath.Act(_p, ModeAgainst(o, t)), CiEff(t));

    public RiskWord Risk(int o, int t)
    {
        Fx6 h = HazardPerYear(o, t);
        return h >= _p.RiskHigh ? RiskWord.High : h >= _p.RiskNoticeable ? RiskWord.Noticeable : RiskWord.Low;
    }

    /// <summary>The trace of the network of <c>o</c> in <c>t</c> as the target sees it: a word.</summary>
    public TraceWord TraceOf(int o, int t)
    {
        Fx6 tr = Trace(o, t);
        return tr >= _p.TraceMany ? TraceWord.Many : tr >= _p.TraceSome ? TraceWord.Some : TraceWord.None;
    }

    /// <summary>The security item as one of the three preset words, or <see cref="SecurityWord.Custom"/>.</summary>
    public SecurityWord SecurityWordOf(Fx6 share) =>
        share == Fx6.Zero ? SecurityWord.None : share == _p.PresetLow ? SecurityWord.Low : share == _p.PresetNormal ? SecurityWord.Normal
        : share == _p.PresetStrong ? SecurityWord.Strong : SecurityWord.Custom;

    public NetworkWord NetworkOf(int o, int t)
    {
        Fx6 n = Network(o, t);
        int s = Sources(o, t);
        return n <= Fx6.Zero || s == 0 ? NetworkWord.None : IntelMath.Confidence(Maturity(o, t), s) >= _p.ConfVisible ? NetworkWord.Reliable : NetworkWord.Weak;
    }

    /// <summary><c>s = ⌊S_max·N⌋</c>: independent sources the network of <c>o</c> in <c>t</c> has.</summary>
    public int Sources(int o, int t) => IntelMath.Floor(Fx6.FromInt(_p.SourcesMax) * Network(o, t));

    /// <summary>
    /// The estimate of the number of nodes <c>t</c> knows, as <c>o</c> sees it (INTELLIGENCE.md §3). False trails of <c>t</c> against <c>o</c> corrupt
    /// <c>c = ⌊φ·s + ½⌋</c> of the sources, <c>φ = min(1, κ·N[t,o])</c>: if at least two honest sources remain the corrupt ones are dropped, otherwise the
    /// centre is shifted by <c>b·c/s</c> and the range stays as narrow as before ("false confidence").
    /// </summary>
    public IntelEstimate Estimate(int o, int t)
    {
        int s = Sources(o, t);
        int c = 0;
        if (s > 0 && ModeAgainst(t, o) == IntelMode.FalseTrails)
        {
            Fx6 phi = IntelMath.Min(Fx6.One, _p.Kappa * Network(t, o));
            c = IntelMath.Floor(phi * Fx6.FromInt(s) + Fx6.FromRaw(Fx6.Scale / 2));
        }
        bool distorted = false;
        Fx6 bias = Fx6.Zero;
        int used = s;
        if (c > 0)
        {
            if (s - c >= 2)
                used = s - c;
            else
            {
                distorted = true;
                bias = _p.BiasMax * Fx6.FromInt(c) / Fx6.FromInt(s);
            }
        }
        Fx6 conf = IntelMath.Confidence(Maturity(o, t), used);
        if (used == 0 || conf < _p.ConfVisible)
            return new IntelEstimate(false, 0, 0, conf, used, distorted);
        Fx6 w = IntelMath.Max(_p.RangeMin, Fx6.One - conf);
        Fx6 centre = Fx6.FromInt(_in.KnownCount(t)) * (Fx6.One - bias);
        return new IntelEstimate(true, IntelMath.Floor(centre * (Fx6.One - w)), IntelMath.Ceiling(centre * (Fx6.One + w)), conf, used, distorted);
    }

    // ---- step

    public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd)
    {
        long month = (context.Calendar ?? throw new InvalidOperationException("Intelligence is monthly and needs a runner calendar.")).MonthIndex(context.Tick);
        for (int o = rowStart; o < rowEnd; o++)
        {
            StepNetworks(o, month);
            if (month % 12 == 0)
                DecidePlan(o);
        }
    }

    private void StepNetworks(int o, long month)
    {
        int targets = TargetCount(o);
        Fx6 netShare = _in.SecurityShare(o) * (Fx6.One - _p.CiShare);
        Fx6 perTarget = targets == 0 ? Fx6.Zero : netShare / Fx6.FromInt(targets);
        Fx6 full = IntelMath.Sat(perTarget / _p.NetShareRef);
        for (int t = 0; t < _rows; t++)
        {
            if (t == o)
                continue;
            Fx6 n = Network(o, t), a = Maturity(o, t), tr = Trace(o, t);
            IntelMode mode = ModeAgainst(o, t);
            Fx6 ci = CiEff(t);
            Fx6 nStar = mode == IntelMode.None ? Fx6.Zero : full * (Fx6.One - _p.CiGrowthCut * ci);
            Fx6 n2 = n + (nStar > n ? _growthM : _decayM) * (nStar - n);
            Fx6 a2 = a + _matM * (n - a);
            Fx6 tr2 = tr - tr * _traceDecayM;
            bool exposed = false;
            if (mode != IntelMode.None && n > Fx6.Zero)
            {
                Fx6 hm = IntelMath.MonthlyFromYearly(IntelMath.HazardPerYear(_p, n, IntelMath.Act(_p, mode), ci));
                tr2 += hm;
                exposed = CounterRng.Chance(_events.NextU64(month, SystemExpose, o * (1 << 16) + t, 0), hm);
            }
            if (exposed)
            {
                n2 *= _p.LossKeepN;
                a2 *= _p.LossKeepA;
                tr2 = Fx6.Zero;
                _lost[t].Next[o] = _lost[t].Current[o] + 1;
            }
            _n[t].Next[o] = n2.Raw;
            _a[t].Next[o] = a2.Raw;
            _trace[t].Next[o] = tr2.Raw;
        }
    }

    // The AI plan of the actor, the same rule for every actor: the leaders (more known nodes) in order of lead, at most three, to steal from; with no
    // leader the strongest rival is only observed. The player's own plan, when set, replaces it (see Plan).
    private void DecidePlan(int o)
    {
        int mine = _in.KnownCount(o);
        Span<int> chosen = stackalloc int[IntelParams.MaxTargets];
        Span<int> known = stackalloc int[IntelParams.MaxTargets];
        int count = 0;
        for (int k = 0; k < IntelParams.MaxTargets; k++)
        {
            int best = -1, bestKnown = -1;
            for (int t = 0; t < _rows; t++)
            {
                if (t == o || chosen[..count].Contains(t))
                    continue;
                int kt = _in.KnownCount(t);
                if (kt > bestKnown)
                {
                    best = t;
                    bestKnown = kt;
                }
            }
            if (best < 0 || (k > 0 && bestKnown <= mine))
                break;
            chosen[count] = best;
            known[count++] = bestKnown;
        }
        for (int k = 0; k < IntelParams.MaxTargets; k++)
        {
            bool has = k < count;
            _aiTarget[k].Next[o] = has ? chosen[k] + 1 : 0;
            _aiMode[k].Next[o] = has ? (int)(known[k] > mine ? IntelMode.Steal : IntelMode.Observe) : 0;
        }
    }

    public void Merge(in TickContext context, int chunkCount)
    {
    }

    public void Post(in TickContext context)
    {
        long month = context.Calendar!.MonthIndex(context.Tick - 1);
        for (int t = 0; t < _rows; t++)
        {
            ReadOnlySpan<long> n = _n[t].Current, a = _a[t].Current;
            for (int o = 0; o < _rows; o++)
            {
                if (n[o] < 0 || n[o] > Fx6.Scale || a[o] < 0 || a[o] > Fx6.Scale)
                    throw new IntelInvariantException(month, o, $"network or maturity of the network against {t} is outside [0, 1]");
            }
        }
    }
}

/// <summary>A broken intelligence invariant: a network size or maturity left [0, 1].</summary>
public sealed class IntelInvariantException(long month, int actorRow, string message)
    : Exception($"intelligence invariant violated at month {month}, actor row {actorRow}: {message}");
