using Singularity.Core.Institutes;
using Singularity.Kernel;

namespace Singularity.Core.Knowledge;

/// <summary>
/// Slot science model (SCIENCE_SLOTS.md): a node moves only in the institute that works on it (S1). A slot makes progress <c>u · M / T_eff</c> a month and asks
/// <c>C_eff · M · s^0.6 / T_eff</c> of money; <c>u = min(1, S / Σ asks)</c> is one factor for all slots. What the slots do not ask is returned to the budget
/// (O-089b). A hazard node leaves its slot at the progress cap and keeps a reduced chance (O-092). State per slot (columns): the progress the slot added last
/// month and its node, read by the institutes for the experience; per actor: the coverage `u` and the money returned.
/// </summary>
public sealed partial class KnowledgeSystem
{
    private readonly SlotParams? _slots;
    private readonly int _slotCount;
    private Fx6[] _tn = [];
    private LongColumn[] _sprog = [];
    private IntColumn[] _snode = [];
    private LongColumn? _cover, _returned;

    private void InitSlots(Table table)
    {
        _tn = new Fx6[_n];
        for (int r = 0; r < _n; r++)
            _tn[r] = SlotMath.NodeMonths(_slots!, _p.CostByType[(int)_g.Type[r]], _g.Gen[r]);
        _sprog = new LongColumn[_slotCount];
        _snode = new IntColumn[_slotCount];
        for (int k = 0; k < _slotCount; k++)
        {
            _sprog[k] = table.AddLong($"know.sprog.{k}");
            _snode[k] = table.AddInt($"know.snode.{k}");
        }
        _cover = table.AddLong("know.cover");
        _returned = table.AddLong("know.returned");
        var one = new long[_actors];
        Array.Fill(one, Fx6.One.Raw);
        _cover.Load(one);
    }

    /// <summary>The slot model runs.</summary>
    public bool SlotsOn => _slots is not null;

    /// <summary><c>T_n</c> of a node in months (a constant of the data and the parameters).</summary>
    public Fx6 NodeMonths(int nodeRow) => _tn[nodeRow];

    /// <summary>Coverage <c>u</c> of the last step of the actor (1 = every slot ran at full speed).</summary>
    public Fx6 Coverage(int actorRow) => _cover is null ? Fx6.One : Fx6.FromRaw(_cover.Current[actorRow]);

    /// <summary>Research money the slots did not ask in the last step; it returned to the budget (O-089b).</summary>
    public Fx6 ReturnedToBudget(int actorRow) => _returned is null ? Fx6.Zero : Fx6.FromRaw(_returned.Current[actorRow]);

    /// <summary>The node number the slot worked on in the last step (0 = none) and the progress it added.</summary>
    public int SlotProgress(int actorRow, int slot, out Fx6 added)
    {
        added = Fx6.FromRaw(_sprog[slot].Current[actorRow]);
        return _snode[slot].Current[actorRow];
    }

    /// <summary>A hazard node at its progress cap: it left its slot (O-092).</summary>
    public bool Released(int actorRow, int nodeRow) => _g.IsHazard[nodeRow] && Prog(actorRow, nodeRow) >= _p.ProgCap;

    /// <summary>
    /// <c>R = 1 + Σ bonus</c> of the research_speed nodes the actor knows, weighted like the overlap level (main direction 0.5, the rest share 0.5, O-090): an effect for all directions counts for every node. Bonuses add up, the sum stops at <see cref="SlotParams.ResearchSpeedCap"/>. Exactly 1 while the data has no such node.
    /// </summary>
    public Fx6 ResearchFactor(int a, int nodeRow)
    {
        if (_g.SpeedNodes.IsEmpty)
            return Fx6.One;
        var dirs = _g.Directions[nodeRow];
        int allDirections = (1 << _g.DirectionNames.Length) - 1;
        Fx6 allBonus = Fx6.Zero;
        Span<Fx6> perDir = stackalloc Fx6[dirs.Length];
        perDir.Clear();
        foreach (int s in _g.SpeedNodes)
        {
            if (!IsKnown(a, s))
                continue;
            foreach ((int mask, Fx6 bonus) in _g.SpeedEffects[s])
            {
                if (mask == allDirections)
                {
                    allBonus += bonus;
                    continue;
                }
                for (int i = 0; i < dirs.Length; i++)
                    perDir[i] += (mask & (1 << dirs[i])) != 0 ? bonus : Fx6.Zero;
            }
        }
        Fx6 sum = allBonus;
        for (int i = 0; i < dirs.Length; i++)
            sum += SlotMath.DirectionWeight(i, dirs.Length, _slots!.MainWeight) * perDir[i];
        return Fx6.One + (sum > _slots!.ResearchSpeedCap ? _slots.ResearchSpeedCap : sum);
    }

    /// <summary><c>T_eff</c> of the node for the actor in month t: the months at speed 1 (the quote of a card).</summary>
    public Fx6 EffectiveMonths(int actorRow, int nodeRow, long month) => TimeEff(actorRow, nodeRow, month);

    // T_eff of the node for the actor: T_n with the discount of the price (diffusion or theft, the larger).
    private Fx6 TimeEff(int a, int r, long t)
    {
        Fx6 diffusion = KnownElsewhere(a, r) ? _p.KDiff : Fx6.Zero;
        Fx6 steal = _inputs.StealShare(a, r, t);
        return SlotMath.EffectiveMonths(_tn[r], steal > diffusion ? steal : diffusion);
    }

    // The asks of the working slots on the research money; returns their sum. Same numbers for the step, the why_not read and the reserve.
    // A focus bought from the reserve (O-088) moves its surcharge over the plain ask to the reserve, as far as the reserve can pay it (`reserveAsk` = what the
    // reserve is asked for before the coverage u; the rest of the surcharge stays on the research money).
    private Fx6 PlanSlots(int a, long t, ReadOnlySpan<SlotWork> work, Span<Fx6> speed, Span<Fx6> teff, Span<Fx6> ask, out Fx6 reserveAsk)
    {
        Span<Fx6> surcharge = stackalloc Fx6[work.Length];
        Fx6 wanted = Fx6.Zero;
        for (int i = 0; i < work.Length; i++)
        {
            int r = work[i].NodeRow;
            Fx6 research = ResearchFactor(a, r);
            speed[i] = SlotMath.Speed(_slots!, work[i].InstituteMultiplier, work[i].Focus, research);
            teff[i] = TimeEff(a, r, t);
            Fx6 c = CEffOf(a, r, KMix(a, r), t);
            ask[i] = SlotMath.Rate(_slots!, c, speed[i], work[i].Focus, teff[i]);
            surcharge[i] = Fx6.Zero;
            if (work[i].FromReserve && work[i].Focus > 0)
            {
                Fx6 plain = SlotMath.Rate(_slots!, c, SlotMath.Speed(_slots!, work[i].InstituteMultiplier, 0, research), 0, teff[i]);
                surcharge[i] = ask[i] > plain ? ask[i] - plain : Fx6.Zero;
                wanted += surcharge[i];
            }
        }
        Fx6 share = Fx6.Zero;
        reserveAsk = Fx6.Zero;
        if (wanted > Fx6.Zero)
        {
            Fx6 available = _inputs.ReserveAvailable(a, t);
            Fx6 paid = available < wanted ? available : wanted;
            share = paid / wanted;
            reserveAsk = paid;
        }
        Fx6 total = Fx6.Zero;
        for (int i = 0; i < work.Length; i++)
        {
            ask[i] -= surcharge[i] * share;
            total += ask[i];
        }
        return total;
    }

    /// <summary>The money the player's focus takes from the reserve in month t (O-088): the surcharges it can pay, times the coverage. A pure read of the snapshot.</summary>
    private Fx6 SlotReserveSpend(int a, long t)
    {
        Span<SlotWork> work = stackalloc SlotWork[InstituteSystem.MaxSlotsLimit];
        int n = _inputs.SlotWork(a, t, work);
        Span<Fx6> speed = stackalloc Fx6[n], teff = stackalloc Fx6[n], ask = stackalloc Fx6[n];
        Fx6 total = PlanSlots(a, t, work[..n], speed, teff, ask, out Fx6 reserveAsk);
        return reserveAsk * SlotMath.Coverage(_inputs.ResearchBudget(a, t), total);
    }

    /// <summary>
    /// The coverage <c>u</c> the actor's slots would have in month t if one more slot worked on <paramref name="extra"/> (null: as they are; the check before a start the money does not cover, §1.2).
    /// </summary>
    public Fx6 CoverageWith(int a, long t, SlotWork? extra)
    {
        Span<SlotWork> work = stackalloc SlotWork[InstituteSystem.MaxSlotsLimit + 1];
        int n = _inputs.SlotWork(a, t, work[..InstituteSystem.MaxSlotsLimit]);
        if (extra is { } more)
            work[n++] = more;
        Span<Fx6> speed = stackalloc Fx6[n], teff = stackalloc Fx6[n], ask = stackalloc Fx6[n];
        Fx6 total = PlanSlots(a, t, work[..n], speed, teff, ask, out _);
        return SlotMath.Coverage(_inputs.ResearchBudget(a, t), total);
    }

    private Fx6 SlotProgressRate(int a, int nodeRow, long t)
    {
        Span<SlotWork> work = stackalloc SlotWork[Singularity.Core.Institutes.InstituteSystem.MaxSlotsLimit];
        int n = _inputs.SlotWork(a, t, work);
        Span<Fx6> speed = stackalloc Fx6[n], teff = stackalloc Fx6[n], ask = stackalloc Fx6[n];
        Fx6 total = PlanSlots(a, t, work[..n], speed, teff, ask, out _);
        Fx6 u = SlotMath.Coverage(_inputs.ResearchBudget(a, t), total);
        for (int i = 0; i < n; i++)
        {
            if (work[i].NodeRow == nodeRow)
                return u * speed[i] / teff[i];
        }
        return Fx6.Zero;
    }

    private void ComputeSlots(int a, long t, List<(int, int, bool)> opened)
    {
        int actor = _start.ActorNumber[a];
        Fx6 s = _inputs.ResearchBudget(a, t);
        foreach (int r in _g.Order)
        {
            if (!IsKnown(a, r) && RequiresKnown(a, r))
                _trace[a * _n + r] = default(KnowledgeNodeStep) with { InFrontier = true };
        }
        Span<SlotWork> work = stackalloc SlotWork[InstituteSystem.MaxSlotsLimit];
        int n = _inputs.SlotWork(a, t, work);
        Span<Fx6> speed = stackalloc Fx6[n], teff = stackalloc Fx6[n], ask = stackalloc Fx6[n];
        Fx6 total = PlanSlots(a, t, work[..n], speed, teff, ask, out _);
        Fx6 u = SlotMath.Coverage(s, total);

        for (int k = 0; k < _slotCount; k++)
        {
            _sprog[k].Next[a] = 0;
            _snode[k].Next[a] = 0;
        }
        Fx6 spent = Fx6.Zero;
        for (int i = 0; i < n; i++)
        {
            int r = work[i].NodeRow;
            Fx6 alloc = ask[i] * u;
            spent += alloc;
            int disc = _g.Discipline[r];
            Fx6 kmix = KMix(a, r);
            Fx6 ceff = CEffOf(a, r, kmix, t);
            Fx6 dprog = u * speed[i] / teff[i];
            Fx6 prog = Prog(a, r) + dprog;
            bool open;
            HazardStep hz = default;
            ulong draw = 0;
            if (_g.IsHazard[r])
            {
                if (prog > _p.ProgCap)
                    prog = _p.ProgCap;
                hz = KnowledgeMath.Hazard(prog, _p.ThetaStart, _p.HMax, _p.YHalf);
                if (hz.Active)
                    draw = KnowledgeRng.Hazard(_know, t, actor, _g.Number[r]);
                open = hz.Active && CounterRng.Chance(draw, hz.H);
            }
            else
            {
                open = prog >= Fx6.One;
            }
            _prog[r].Next[a] = prog.Raw;
            if (disc >= 0)
                _amount[a * _d + disc] += alloc.Raw;
            if (open)
                opened.Add((a, r, true));
            _trace[a * _n + r] = new KnowledgeNodeStep(true, Fx6.Zero, alloc, kmix, ceff, dprog, prog, hz.Active, hz.H, draw, open);
            _sprog[work[i].Slot].Next[a] = dprog.Raw;
            _snode[work[i].Slot].Next[a] = _g.Number[r];
        }
        if (spent > s && s >= Fx6.Zero)
            throw new KnowledgeInvariantException(t, actor, $"R&D spent {spent} exceeds S_a {s} (I4)");
        _spent[a] = spent.Raw;
        _cover!.Next[a] = u.Raw;
        _returned!.Next[a] = (s > spent ? s - spent : Fx6.Zero).Raw;

        // Hazard nodes that left their slot at the cap: the chance goes on at a reduced rate, with no progress and no money (O-092).
        foreach (int r in _g.Order)
        {
            if (!_trace[a * _n + r].InFrontier || !Released(a, r) || Worked(work[..n], r))
                continue;
            HazardStep hz = KnowledgeMath.Hazard(Prog(a, r), _p.ThetaStart, _p.HMax, _p.YHalf);
            if (!hz.Active)
                continue;
            ulong draw = KnowledgeRng.Hazard(_know, t, actor, _g.Number[r]);
            bool open = CounterRng.Chance(draw, hz.H * _slots!.ReleasedHazardShare);
            if (open)
                opened.Add((a, r, true));
            _trace[a * _n + r] = _trace[a * _n + r] with { HazardActive = true, H = hz.H, U = draw, Opened = open, ProgNew = Prog(a, r) };
        }

        for (int d = 0; d < _d; d++)
        {
            Fx6 v = _inputs.DisciplineVolume(a, d, t);
            _kind[d].Next[a] = KnowledgeMath.KIndStep(KInd(a, d), KD(a, d), v, _p.Eta, _p.VRef, _p.ThetaInd, _p.DeltaInd).Raw;
        }
    }

    private static bool Worked(ReadOnlySpan<SlotWork> work, int row)
    {
        for (int i = 0; i < work.Length; i++)
        {
            if (work[i].NodeRow == row)
                return true;
        }
        return false;
    }
}
