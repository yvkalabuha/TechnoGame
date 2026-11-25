using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Institutes;
using Singularity.Core.Intelligence;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session;

/// <summary>
/// Collects the read views from the public pure functions of Core (SLICE_COMPOSITION §12). No formula and no state here, no RNG: a view is a copy
/// of what the adapters and the AI already read, so building one never changes the hash. Mutable arrays of Core are copied.
/// </summary>
internal static class ViewBuilder
{
    // Refusal reasons in BadLicence order, without None.
    private static readonly ImmutableArray<BadLicence> Reasons = [BadLicence.Self, BadLicence.Empty, BadLicence.Gen, BadLicence.Money];

    public static SliceLayoutView Layout(SliceWorld w) => new(
        [.. w.Scenario.ActorId], w.Graph.Number, w.Graph.Gen, w.IndustryLayout.Number, w.IndustryLayout.Tier,
        [.. w.IndustryLayout.Clusters.Select(c => c.ToString())], w.ProductsLayout.GenApp,
        w.ProductsLayout.Designs, w.ProductsLayout.Bureaus, w.ProductsLayout.Generations, w.PolicyParameters.TAb,
        w.IndustryParameters.MasteredYield, w.PolicyParameters.FvBudget);

    /// <summary>The post state of <paramref name="month"/> (−1 before the first step); read for the compute of <c>month + 1</c>.</summary>
    /// <param name="subscribed">Actors whose node blocks the view carries (ADR-0025); null = all.</param>
    public static SliceView Snapshot(SliceWorld w, long tick, string? hash, long month, MonthInputsView? inputs, MonthStepView? step, bool[]? subscribed = null)
    {
        var actors = ImmutableArray.CreateBuilder<ActorView>(w.Scenario.ActorCount);
        w.NodeBlocks.Begin(month + 1, w.NodeStamp(), w.Scenario.ActorCount, w.ReuseNodeBlocks);
        for (int a = 0; a < w.Scenario.ActorCount; a++)
            actors.Add(Actor(w, a, month, step is not null, subscribed is null || subscribed[a]));
        return new SliceView(tick, w.Calendar.DateOf(tick), hash, month, actors.MoveToImmutable(), inputs, step);
    }

    /// <summary>The actors of a view as a fresh snapshot of a tick without a monthly step would build them: the close reasons are not read (F7a).</summary>
    public static ImmutableArray<ActorView> WithoutCloseReasons(ImmutableArray<ActorView> actors)
    {
        var result = ImmutableArray.CreateBuilder<ActorView>(actors.Length);
        foreach (ActorView a in actors)
        {
            var closes = ImmutableArray.CreateBuilder<CloseView>(a.Decisions.Closes.Length);
            foreach (CloseView c in a.Decisions.Closes)
                closes.Add(c with { Reason = LineCloseReason.None });
            result.Add(a with { Decisions = a.Decisions with { Closes = closes.MoveToImmutable() } });
        }
        return result.MoveToImmutable();
    }

    // `stepped`: the tick ran a monthly step, so the step traces are of month `month`; otherwise they belong to an earlier step and are not read (§13.7).
    private static ActorView Actor(SliceWorld w, int a, long month, bool stepped, bool withNodes)
    {
        long next = month + 1;
        bool full = w.Config.ViewMode == SliceViewMode.Full;
        var kd = ImmutableArray.CreateBuilder<Fx6>(w.Graph.DisciplineNames.Length);
        for (int d = 0; d < w.Graph.DisciplineNames.Length; d++)
            kd.Add(w.Knowledge.KD(a, d));
        var bureaus = ImmutableArray.CreateBuilder<BureauView>(w.ProductsLayout.Bureaus);
        for (int b = 0; b < w.ProductsLayout.Bureaus; b++)
        {
            int design = w.Products.BureauDesign(a, b);
            long required = design < 0 ? 0 : w.Products.BureauReq(w.ProductsLayout.AltAfClass[w.Products.AltAt(a, design, w.ProductsLayout.AirframeSlot)]);
            bureaus.Add(new BureauView(b, design, w.Industry.Granted(a, ProductsLayout.EngineerCategory, w.ProductsLayout.BureauRequester(b)), required));
        }
        var refusals = ImmutableArray.CreateBuilder<long>(Reasons.Length);
        foreach (BadLicence reason in Reasons)
            refusals.Add(w.Policy.BadLicenceCount(a, reason));
        (int disStart, int disEnd) = w.Policy.Disruption(a);
        var designs = ImmutableArray.CreateBuilder<DesignView>(w.ProductsLayout.Designs);
        for (int d = 0; d < w.ProductsLayout.Designs; d++)
            designs.Add(Design(w, a, d, next));
        return new ActorView(a, w.Policy.Profile(a), kd.MoveToImmutable(), w.Products.FieldedGen(a, next),
            full ? w.Products.KeyOf(a, next) : null,
            full && month >= 0 && (month % 12 == 0 || w.SlotsOn) ? w.Products.MaxFleetValue(a, Fx6.One, w.PolicyParameters.FvBudget, next) : null,
            bureaus.MoveToImmutable(),
            new QualityView(w.Policy.QualityProgrammeActive(a), w.Policy.QualityProgrammeStart(a), w.Policy.QualityTriggerCount(a), w.Policy.TrainingTarget(a)),
            LicenceOf(w, a, refusals.MoveToImmutable()),
            new DisruptionView(disStart, disEnd), withNodes && !HiddenFromPlayer(w, a) ? Nodes(w, a, next) : Unsubscribed(w, a), designs.MoveToImmutable(), Decisions(w, a, next, stepped),
            Alloy(w, a, month), Research(w, a), HiddenFromPlayer(w, a) ? Math.Min(1, UnknownNodes(w, a)) : UnknownNodes(w, a),
            w.Policy.MarketOn && !HiddenFromPlayer(w, a) ? Market(w, a, next) : null,
            Institutes(w, a, next), Slots(w, a, next));
    }

    // The slot model's numbers of the actor: what the institutes did in the last step (columns) and the air power of the month.
    private static SlotScienceView? Slots(SliceWorld w, int a, long month)
    {
        if (!w.SlotsOn || w.Institutes is not { } inst)
            return null;
        Span<int> node = stackalloc int[inst.Layout.SlotCount(a)];
        Span<byte> focus = stackalloc byte[node.Length];
        inst.Resolve(a, month, node, focus);
        int standing = 0, free = 0;
        for (int k = 0; k < node.Length; k++)
        {
            standing += node[k] >= 0 ? 1 : 0;
            free += node[k] == 0 ? 1 : 0;
        }
        return new SlotScienceView(standing, free, w.Knowledge.Coverage(a), w.Knowledge.ReturnedToBudget(a), w.AirPower(a, month), w.AirPowerRatio(a, month), inst.SlotParameters.SpeedCap);
    }

    // The market as the actor sees it at the month of the next step: pure reads of the policy, built only when the market is on.
    private static LicenceMarketView Market(SliceWorld w, int a, long month)
    {
        PolicySystem pol = w.Policy;
        Fx6 funds = w.LicenceFunds(a, month);
        var held = ImmutableArray.CreateBuilder<LicenceHeldView>();
        foreach (LicenceRecord r in pol.Licences(a))
            held.Add(new LicenceHeldView(r.Seller, r.Gen, r.Start));
        var own = ImmutableArray.CreateBuilder<CatalogueLineView>();
        var offers = ImmutableArray.CreateBuilder<CatalogueLineView>();
        int gens = w.ProductsLayout.Generations;
        var world = ImmutableArray.CreateBuilder<WorldPriceView>(gens);
        for (int g = 1; g <= gens; g++)
        {
            (Fx6? average, int sellers) = pol.WorldPrice(g, month);
            world.Add(new WorldPriceView(g, average, sellers));
            for (int s = 0; s < w.Scenario.ActorCount; s++)
            {
                if (pol.Offer(s, g, month) is not { } o)
                    continue;
                if (s == a)
                    own.Add(new CatalogueLineView(s, g, o.Reference, o.M, o.Price, o.Circle, BadLicence.None));
                else
                    offers.Add(new CatalogueLineView(s, g, o.Reference, o.M, o.Price, o.Circle, pol.CheckBuy(a, s, g, month, funds, out _)));
            }
        }
        (BadLicence reason, int seller, int gen, int lastMonth) = pol.LastRefusal(a);
        return new LicenceMarketView(held.ToImmutable(), own.ToImmutable(), offers.ToImmutable(), world.MoveToImmutable(), reason, seller, gen, lastMonth,
            pol.LicenceIncome(a, month), pol.IsExternal(a) ? pol.IncomeCap(a) : Fx6.Zero, pol.IsExternal(a));
    }

    private static ImmutableArray<InstituteView> Institutes(SliceWorld w, int a, long month)
    {
        if (w.Institutes is not { } inst)
            return [];
        var list = ImmutableArray.CreateBuilder<InstituteView>(inst.Layout.SlotCount(a));
        Span<int> goals = stackalloc int[InstituteSystem.MaxSlotsLimit];
        Span<bool> builds = stackalloc bool[InstituteSystem.MaxSlotsLimit];
        bool advise = w.Config.PlayerActor == a;
        // A scheduled knowledge run refuses every priority order (PriorityNoEffect): propose builds only.
        bool scheduled = w.Config.Layer is LayerControl.Lk or LayerControl.Lk0;
        if (advise)
            inst.Propose(a, month, goals, builds);
        Span<int> slotNode = stackalloc int[inst.Layout.SlotCount(a)];
        Span<byte> slotFocus = stackalloc byte[slotNode.Length];
        Span<int> entries = stackalloc int[inst.SlotsOn ? inst.ListLength : 0];
        if (inst.SlotsOn)
            inst.Resolve(a, month, slotNode, slotFocus);
        for (int k = 0; k < inst.Layout.SlotCount(a); k++)
        {
            var h = new InstituteHandle(a, k);
            var levels = ImmutableArray.CreateBuilder<int>(inst.Layout.DisciplineNames.Length);
            for (int d = 0; d < inst.Layout.DisciplineNames.Length; d++)
                levels.Add(inst.Level(h, d));
            long start = inst.BuildStart(h);
            int active = 0, focus = 0;
            ImmutableArray<int> queue = [], dirLevels = [];
            Fx6 progress = Fx6.Zero, monthsLeft = Fx6.Zero;
            if (inst.SlotsOn && inst.Standing(h) && !HiddenFromPlayer(w, a))
            {
                active = slotNode[k];
                focus = slotFocus[k];
                int n = w.SlotOrders!.Entries(h, entries);
                var q = ImmutableArray.CreateBuilder<int>(n);
                for (int j = 0; j < n; j++)
                {
                    if (entries[j] != active)
                        q.Add(entries[j]);
                }
                queue = q.ToImmutable();
                var dl = ImmutableArray.CreateBuilder<int>(inst.Layout.DirectionNames.Length);
                for (int d = 0; d < inst.Layout.DirectionNames.Length; d++)
                    dl.Add(inst.DirLevel(h, d));
                dirLevels = dl.MoveToImmutable();
                if (active > 0 && w.Graph.RowOf(active) is >= 0 and int row)
                {
                    progress = w.Knowledge.Prog(a, row);
                    Fx6 rate = w.Knowledge.ProgressRate(a, row, month);
                    monthsLeft = rate > Fx6.Zero && progress < Fx6.One ? (Fx6.One - progress) / rate : Fx6.Zero;
                }
            }
            // A candidate shows the price it would pay if started now (progress 0).
            list.Add(new InstituteView(k, inst.Layout.IdOf(h), inst.Standing(h), HiddenFromPlayer(w, a) ? 0 : inst.GoalNode(h), levels.MoveToImmutable(), start, inst.BuildMonths(h),
                inst.Standing(h) ? Fx6.Zero : inst.BuildRate(h, month), inst.Layout.RefYear(h), inst.Layout.TotalLevels(h),
                advise && !scheduled ? goals[k] : 0, advise && builds[k], active, queue, focus, progress, monthsLeft, dirLevels));
        }
        return list.MoveToImmutable();
    }

    // Supply and ask are both from the step-7 trace of the same monthly step; nothing is recomputed from the post-step state. No trace (restored snapshot, before the first step) = not known.
    private static AlloyCoverage Alloy(SliceWorld w, int a, long month) =>
        w.Products.HasAlloyTrace(a)
            ? new AlloyCoverage(w.Products.LastAlloySupply(a), w.Products.LastAlloyAsk(a), w.Policy.AlloyDeficit(a, month))
            : default;

    // Nodes the actor does not know yet, whether or not its node block is carried: a reader asks it for «is there anything left to research» without reading the nodes.
    private static int UnknownNodes(SliceWorld w, int a)
    {
        int n = 0;
        for (int r = 0; r < w.Graph.Count; r++)
            if (!w.Knowledge.IsKnown(a, r))
                n++;
        return n;
    }

    /// <summary>Most research entries a view carries per actor.</summary>
    internal const int MaxResearch = 5;

    private static ImmutableArray<ResearchView> Research(SliceWorld w, int a)
    {
        Span<int> rows = stackalloc int[MaxResearch];
        int n = w.Knowledge.ResearchInProgress(a, rows);
        if (n == 0)
            return ImmutableArray<ResearchView>.Empty;
        // Entries are built on the stack; the published array is the cached one when nothing changed (progress moves once a month), so a rebuild between steps allocates nothing.
        Span<ResearchView> fresh = stackalloc ResearchView[MaxResearch];
        for (int i = 0; i < n; i++)
        {
            Fx6 have = w.Knowledge.Prog(a, rows[i]), need = w.Knowledge.NeedOf(rows[i]);
            fresh[i] = new ResearchView(w.Graph.Number[rows[i]], have, need, w.Graph.IsHazard[rows[i]] && have >= need);
        }
        ImmutableArray<ResearchView> last = w.ResearchViews[a];
        if (!last.IsDefault && last.AsSpan().SequenceEqual(fresh[..n]))
            return last;
        return w.ResearchViews[a] = ImmutableArray.Create(fresh[..n]);
    }

    // An actor the view does not carry the nodes of: no block, and the cache keeps none of it (ADR-0025).
    private static NodeList Unsubscribed(SliceWorld w, int a)
    {
        w.NodeBlocks.Drop(a);
        return default;
    }

    // One compact block per actor (R1). On a tick that wrote none of the columns the blocks read, the block of the last snapshot of the same month is
    // handed out again (R2): the command tick after a monthly step is such a tick.
    private static NodeList Nodes(SliceWorld w, int a, long next)
    {
        NodeBlockCache cache = w.NodeBlocks;
        if (cache.TryGet(a, out NodeList reused))
            return reused;
        NodeShape shape = w.NodeShape;
        var block = new NodeBlock(shape, w.Knowledge.TracksKnownSince);
        // One pass over the grants per actor instead of one per node.
        foreach ((int actor, int node) in w.Knowledge.LastLicenceGrants)
        {
            int row = actor == a ? w.Graph.RowOf(node) : -1;
            if (row >= 0)
                block.ByLicence[row >> 6] |= 1UL << (row & 63);
        }
        for (int r = 0; r < shape.Numbers.Length; r++)
        {
            if (w.Knowledge.IsKnown(a, r))
                block.Known[r >> 6] |= 1UL << (r & 63);
            if (block.KnownSince is { } since)
                since[r] = w.Knowledge.KnownSince(a, r);
            int p = shape.LayoutRow[r];
            if (p >= 0)
                block.Processes[shape.ProcessIndex[r]] = Process(w, a, p, shape.Numbers[r], next);
        }
        var list = new NodeList(block);
        cache.Put(a, list);
        return list;
    }

    private static ProcessView Process(SliceWorld w, int a, int row, int number, long next)
    {
        // ucm of a process = the unit-cost multiplier of the set made of it alone (weights N = base cost = 1 cancel out).
        ProductionProfile alone = w.Industry.Profile(a, [new PrcSetItem(number, Fx6.One, Fx6.One)], next);
        return new ProcessView(w.Industry.Yield(a, row, next).Yield, w.Industry.ProcV(a, row), alone.Admitted, alone.UnitCostMult,
            w.ProductsInputs.Mastered(a, number, next), w.IndustryInputs.LicenceShare(a, row, next));
    }

    private static DesignView Design(SliceWorld w, int a, int d, long next)
    {
        ProductsSystem p = w.Products;
        DesignStage stage = p.Stage(a, d);
        // An empty slot has no alternatives, so the functions of the design are not defined: only the columns are copied.
        bool exists = stage != DesignStage.None;
        return new DesignView(d, stage, p.PlatformGen(a, d), exists ? p.Gen(a, d) : 0, p.LiveParent(a, d), p.StageOMonth(a, d),
            p.BuiltAny(a, d), p.Cancelled(a, d), p.Rate(a, d), p.UnitCost(a, d), exists ? p.UnitCostFloor(a, d) : Fx6.Zero,
            p.InService(a, d), exists ? p.Readiness(a, d, next) : Fx6.Zero, exists ? p.Perf(a, d) : Fx6.Zero, p.Bind(a, d), w.Policy.AiLine(a, d, p.Generation(a, d)),
            [.. p.PrcSet(a, d)], p.Generation(a, d), p.LicensedFrom(a, d));
    }

    private static PolicyDecisionView Decisions(SliceWorld w, int a, long next, bool stepped)
    {
        PolicySystem pol = w.Policy;
        var commands = ImmutableArray.CreateBuilder<DesignCommandView>();
        foreach (DesignCommand c in pol.AiDesignCommands(a, next))
            commands.Add(new DesignCommandView(c.Kind, c.Target, [.. c.Alts], c.Design));
        var starts = ImmutableArray.CreateBuilder<StartView>();
        var closes = ImmutableArray.CreateBuilder<CloseView>();
        for (int d = 0; d < w.ProductsLayout.Designs; d++)
        {
            if (pol.AiStart(a, d, next) is { } start)
                starts.Add(new StartView(d, start.Rate, start.Design));
            if (pol.AiClose(a, d, next) is { } close)
                closes.Add(new CloseView(d, stepped ? pol.LastCloseReason(a, d) : LineCloseReason.None, close));
        }
        var repeats = ImmutableArray.CreateBuilder<int>(w.ProductsLayout.Generations);
        for (int g = 1; g <= w.ProductsLayout.Generations; g++)
            repeats.Add(pol.Repeats(a, g));
        var proposals = ImmutableArray.CreateBuilder<ProposalView>();
        foreach (AdviserProposal p in pol.AdviserProposals(a, next))
        {
            // The proposal was decided on the snapshot before the last step: drop what the step (the player's own command) has already done.
            DesignStage stage = w.Products.Stage(a, p.Design);
            if (p.Kind == ProposalKind.Open
                ? stage is not (DesignStage.Prototype or DesignStage.InService) || w.Commands.Series(a, p.Design, next) is not null
                : stage != DesignStage.Series || w.Commands.CloseLine(a, p.Design, next) is not null)
                continue;
            proposals.Add(new ProposalView(p.Kind, p.Design, p.Handle, p.Rate, p.Reason));
        }
        return new PolicyDecisionView(commands.ToImmutable(), starts.ToImmutable(), closes.ToImmutable(), [.. pol.AiFleetCommands(a, next)], repeats.MoveToImmutable())
        {
            Proposals = proposals.ToImmutable(),
        };
    }

    /// <summary>
    /// The inputs of the monthly step <paramref name="month"/>, read before its compute (after the deals and the commands of the tick).
    /// Actors are independent pure reads of the snapshot (nothing is written, the memo of <c>PrcSet</c> is built only in the constructor and Post),
    /// so they run in parallel; each result goes to its own slot <c>rows[a]</c>, so the order does not depend on the threads (tick-profile-m0 §4 F4-A).
    /// </summary>
    public static MonthInputsView Inputs(SliceWorld w, long month) => Inputs(w, month, w.Config.Threads);

    internal static MonthInputsView Inputs(SliceWorld w, long month, int maxThreads)
    {
        var rows = new ActorInputsView[w.Scenario.ActorCount];
        if (maxThreads <= 1)
        {
            for (int a = 0; a < rows.Length; a++)
                rows[a] = ActorInputs(w, a, month);
        }
        else
        {
            // Failures are recorded, not thrown: every actor runs, and the lowest failed actor is rethrown as is,
            // so the exception does not depend on the thread count or on scheduling (as in TickRunner).
            var errors = new Exception?[rows.Length];
            Parallel.For(0, rows.Length, new ParallelOptions { MaxDegreeOfParallelism = maxThreads }, a =>
            {
                try
                {
                    rows[a] = ActorInputs(w, a, month);
                }
                catch (Exception e)
                {
                    errors[a] = e;
                }
            });
            foreach (Exception? e in errors)
            {
                if (e is not null)
                    ExceptionDispatchInfo.Capture(e).Throw();
            }
        }
        // The adapter of the policy asks GFront(t − 1); the fielded set does not depend on the month, but the call is the same.
        return new MonthInputsView(month, w.Products.GFront(month - 1), ImmutableCollectionsMarshal.AsImmutableArray(rows));
    }

    private static ActorInputsView ActorInputs(SliceWorld w, int a, long month)
    {
        // One allocation and one plan per actor and month: the compute of the step reads them from the memo on the same snapshot.
        if (w.MemoizeInputs)
            w.Industry.MemoizeActor(a, month);
        Fx6 rest = w.Industry.RestActor(a, month);
        ToolingPlanResult plan = w.MemoizeInputs ? w.IndustryInputs.Memoize(a, month, rest) : w.IndustryInputs.Plan(a, month, rest);
        int participating = 0;
        var unmet = new Fx6[w.IndustryLayout.Clusters.Length];
        for (int c = 0; c < unmet.Length; c++)
        {
            for (int dim = 0; dim < Dims3.Count; dim++)
            {
                participating += plan.Participates(a, c, (Dimension)dim) ? 1 : 0;
                unmet[c] += plan.Unmet(a, c, (Dimension)dim);
            }
        }
        var tooling = new ToolingLedgerView(plan.Rest, plan.CapBudget, plan.Total, plan.TakenRest, plan.TakenCap, plan.RestReturned,
            plan.TrainingBudget, participating, [.. unmet]);
        var designs = ImmutableArray.CreateBuilder<DesignInputsView>(w.ProductsLayout.Designs);
        for (int d = 0; d < w.ProductsLayout.Designs; d++)
            designs.Add(DesignInputs(w, a, d, month));
        return new ActorInputsView(a, w.Policy.Budget(a), w.SharesOf(a), w.ProcurementBudget(a), tooling,
            new CapabilityView(w.Industry.Alloy(a, month).Supply, w.Policy.AlloyDeficit(a, month)), designs.MoveToImmutable(), Requirement(w, a, month),
            w.Reserve is null ? null : new ReserveView(w.Reserve.Balance(a), w.ReserveShare(a) * w.Policy.Budget(a), w.Reserve.LeakOf(w.Reserve.Balance(a))),
            w.Intel is null || HiddenFromPlayer(w, a) ? null : IntelOf(w, w.Intel, a));
    }

    // With intelligence and a named player, what another actor knows is not the player's to read: its node block is not carried, the count of its unknown nodes
    // shrinks to "some left or none" (the exact count is what the estimate of the intelligence card gives as a range), and its own intelligence card is not published.
    private static bool HiddenFromPlayer(SliceWorld w, int a) => w.Intel is not null && w.Config.PlayerActor is int viewer && viewer != a;

    // The licence of an actor. For the player's own eyes another actor's deal is a hidden event: it is shown only when intelligence sees it (#508), else empty.
    private static LicenceView LicenceOf(SliceWorld w, int a, ImmutableArray<long> refusals)
    {
        if (w.Intel is { } intel && w.Config.PlayerActor is int viewer && viewer != a && HiddenEvents(w, intel, a, intel.Estimate(viewer, a)).IsEmpty)
            return new LicenceView(0, -1, 0, default, default, Fx6.Zero, default, false);
        return new LicenceView(w.Policy.LicenceGen(a), w.Policy.LicenceSeller(a), w.Policy.LicenceStart(a), w.Policy.LicenceMask(a), w.Policy.OfferMask(a),
            w.Policy.LicenceFlow(a), refusals, w.LicenceActive(a));
    }

    // The intelligence card of an actor: words and ranges only. The estimate of a rival is a range from IntelSystem.Estimate, never the exact count.
    private static IntelView IntelOf(SliceWorld w, IntelSystem intel, int a)
    {
        int rows = w.Table.RowCount;
        var targets = ImmutableArray.CreateBuilder<IntelTargetView>(IntelParams.MaxTargets);
        int mine = w.Knowledge.KnownCount(a);
        for (int k = 0; k < IntelParams.MaxTargets; k++)
        {
            if (!intel.Plan(a, k, out int t, out IntelMode mode))
                continue;
            IntelEstimate e = intel.Estimate(a, t);
            targets.Add(new IntelTargetView(t, mode, intel.NetworkOf(a, t), intel.Risk(a, t), intel.TraceOf(a, t), e.Visible,
                e.Visible ? e.Low - mine : 0, e.Visible ? e.High - mine : 0, e.Confidence, e.Sources, e.Distorted, HiddenEvents(w, intel, t, e)));
        }
        int lost = 0, caught = 0;
        TraceWord foreign = TraceWord.None;
        var exposures = ImmutableArray.CreateBuilder<ExposureReportView>();
        var caughtAt = ImmutableArray.CreateBuilder<CaughtView>();
        for (int o = 0; o < rows; o++)
        {
            if (o == a)
                continue;
            caught += intel.CaughtOf(o, a);
            TraceWord tw = intel.TraceOf(o, a);
            foreign = tw > foreign ? tw : foreign;
            lost += intel.Lost(a, o);
            if (intel.LastExposure(o, a) is { } theirs)
                caughtAt.Add(new CaughtView(o, theirs.Month, theirs.Outcome));
            // A turned network is not reported to its owner; one found out later is (the record then says Lost).
            if (intel.LastExposure(a, o) is { Outcome: ExposureOutcome.Lost } own)
                exposures.Add(new ExposureReportView(o, own.Month, own.RiskPerYear, own.Outcome));
        }
        return new IntelView(w.SharesOf(a).Security, intel.SecurityWordOf(w.SharesOf(a).Security), targets.ToImmutable(), lost, caught, foreign,
            exposures.ToImmutable(), caughtAt.ToImmutable());
    }

    // Hidden events of a target through intelligence (#508): only above the visibility of the layer, each with intel_conf·sens of its own.
    private static ImmutableArray<HiddenEventView> HiddenEvents(SliceWorld w, IntelSystem intel, int target, IntelEstimate e)
    {
        Fx6 conf = e.Confidence * intel.Parameters.SensLicence;
        if (!e.Visible || conf < intel.Parameters.ConfVisible || !w.LicenceActive(target))
            return [];
        return [new HiddenEventView(HiddenEventKind.Licence, w.Policy.LicenceSeller(target), w.Policy.LicenceGen(target), w.Policy.LicenceStart(target), conf)];
    }

    // The variants of the yearly Requirement; FV is not read in the calibration mode (spec §8.2 п. 2). Initialize writes only pol.* columns,
    // which Variants does not read, so month 0 is the same as after it.
    private static RequirementView? Requirement(SliceWorld w, int a, long month)
    {
        if (!w.Config.ProductAi || w.Config.ViewMode == SliceViewMode.Calibration || month % 12 != 0)
            return null;
        PresetVariants v = w.Policy.RequirementVariants(a, month);
        return new RequirementView(v.Gen, v.Count, v.Superior, v.Producible, v.FvSuperior, v.FvProducible,
            v.PilotSlots.IsDefault ? [] : v.PilotSlots);
    }

    /// <summary>The results of the monthly step <paramref name="month"/>, read from the step traces right after the tick that ran it.</summary>
    public static MonthStepView Step(SliceWorld w, long month)
    {
        var actors = ImmutableArray.CreateBuilder<ActorStepView>(w.Scenario.ActorCount);
        for (int a = 0; a < w.Scenario.ActorCount; a++)
        {
            MoneyStepView? money = null;
            if (w.Products.LastLeftStart(a) is Fx6 leftStart)
            {
                var lines = ImmutableArray.CreateBuilder<LineMoneyView>();
                foreach (LineMoneyStep l in w.Products.LastLineMoney(a))
                    lines.Add(new LineMoneyView(l.Design, l.LeftIn, l.FromLeft, l.Training, l.FromTraining));
                money = new MoneyStepView(leftStart, lines.ToImmutable());
            }
            actors.Add(new ActorStepView(a, money,
                [.. w.Policy.LastMoneyRefusals(a).Select(r => new LicenceRefusalView(r.Seller, r.Gen, r.Price, r.Room))]));
        }
        return new MonthStepView(month, actors.MoveToImmutable());
    }

    private static DesignInputsView DesignInputs(SliceWorld w, int a, int d, long month)
    {
        ProductsSystem p = w.Products;
        DesignStage stage = p.Stage(a, d);
        bool admitted = false;
        Fx6 yMin = Fx6.Zero;
        int argP = -1;
        int[] prc = p.PrcSet(a, d);
        if (prc.Length > 0)
        {
            admitted = w.Industry.Profile(a, p.PrcItems(a, d), month).Admitted;
            // The first of the minima in prc_set order, as in the print of T6e2.
            yMin = w.ProductsInputs.AdmittedYield(a, prc[0], month);
            argP = prc[0];
            for (int i = 1; i < prc.Length; i++)
            {
                Fx6 y = w.ProductsInputs.AdmittedYield(a, prc[i], month);
                if (y < yMin)
                {
                    yMin = y;
                    argP = prc[i];
                }
            }
        }
        return new DesignInputsView(d, stage, p.IdleMonths(a, d), stage == DesignStage.Prototype && p.GateOpen(a, d, month), p.DefectsHidden(a, d), admitted,
            yMin, argP, // The class is the AI's: without the product AI (a scripted player) there is none.
            stage == DesignStage.Prototype && w.Config.ProductAi ? w.Policy.ClassifyGiveUp(a, d, month) : null);
    }
}
