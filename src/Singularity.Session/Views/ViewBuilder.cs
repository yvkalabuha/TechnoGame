using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Singularity.Core.Data;
using Singularity.Core.Industry;
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
    private static readonly BadLicence[] Reasons = [BadLicence.Self, BadLicence.Empty, BadLicence.Gen, BadLicence.Money];

    public static SliceLayoutView Layout(SliceWorld w) => new(
        [.. w.Scenario.ActorId], w.Graph.Number, w.Graph.Gen, w.IndustryLayout.Number, w.IndustryLayout.Tier,
        [.. w.IndustryLayout.Clusters.Select(c => c.ToString())], w.ProductsLayout.GenApp,
        w.ProductsLayout.Designs, w.ProductsLayout.Bureaus, w.ProductsLayout.Generations, w.PolicyParameters.TAb,
        w.IndustryParameters.MasteredYield, w.PolicyParameters.FvBudget);

    /// <summary>The post state of <paramref name="month"/> (−1 before the first step); read for the compute of <c>month + 1</c>.</summary>
    public static SliceView Snapshot(SliceWorld w, long tick, string? hash, long month, MonthInputsView? inputs, MonthStepView? step)
    {
        var actors = ImmutableArray.CreateBuilder<ActorView>(w.Scenario.ActorCount);
        w.NodeBlocks.Begin(month + 1, w.NodeStamp(), w.Scenario.ActorCount, w.ReuseNodeBlocks);
        for (int a = 0; a < w.Scenario.ActorCount; a++)
            actors.Add(Actor(w, a, month, step is not null));
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
    private static ActorView Actor(SliceWorld w, int a, long month, bool stepped)
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
            full && month >= 0 && month % 12 == 0 ? w.Products.MaxFleetValue(a, Fx6.One, w.PolicyParameters.FvBudget, next) : null,
            bureaus.MoveToImmutable(),
            new QualityView(w.Policy.QualityProgrammeActive(a), w.Policy.QualityProgrammeStart(a), w.Policy.QualityTriggerCount(a), w.Policy.TrainingTarget(a)),
            new LicenceView(w.Policy.LicenceGen(a), w.Policy.LicenceSeller(a), w.Policy.LicenceStart(a), w.Policy.LicenceMask(a), w.Policy.OfferMask(a),
                w.Policy.LicenceFlow(a), refusals.MoveToImmutable(), w.LicenceActive(a)),
            new DisruptionView(disStart, disEnd), Nodes(w, a, next), designs.MoveToImmutable(), Decisions(w, a, next, stepped));
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
        return new DesignView(d, stage, p.PlatformGen(a, d), exists ? p.Gen(a, d) : 0, p.Parent(a, d), p.StageOMonth(a, d),
            p.BuiltAny(a, d), p.Cancelled(a, d), p.Rate(a, d), p.UnitCost(a, d), exists ? p.UnitCostFloor(a, d) : Fx6.Zero,
            p.InService(a, d), exists ? p.Readiness(a, d, next) : Fx6.Zero, exists ? p.Perf(a, d) : Fx6.Zero, p.Bind(a, d), w.Policy.AiLine(a, d),
            [.. p.PrcSet(a, d)], p.Generation(a, d));
    }

    private static PolicyDecisionView Decisions(SliceWorld w, int a, long next, bool stepped)
    {
        PolicySystem pol = w.Policy;
        var commands = ImmutableArray.CreateBuilder<DesignCommandView>();
        foreach (DesignCommand c in pol.AiDesignCommands(a, next))
            commands.Add(new DesignCommandView(c.Kind, c.Target, [.. c.Alts]));
        var starts = ImmutableArray.CreateBuilder<StartView>();
        var closes = ImmutableArray.CreateBuilder<CloseView>();
        for (int d = 0; d < w.ProductsLayout.Designs; d++)
        {
            if (pol.AiStart(a, d, next) is { } start)
                starts.Add(new StartView(d, start.Rate));
            if (pol.AiClose(a, d, next) is not null)
                closes.Add(new CloseView(d, stepped ? pol.LastCloseReason(a, d) : LineCloseReason.None));
        }
        var repeats = ImmutableArray.CreateBuilder<int>(w.ProductsLayout.Generations);
        for (int g = 1; g <= w.ProductsLayout.Generations; g++)
            repeats.Add(pol.Repeats(a, g));
        return new PolicyDecisionView(commands.ToImmutable(), starts.ToImmutable(), closes.ToImmutable(), [.. pol.AiFleetCommands(a, next)], repeats.MoveToImmutable());
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
            new CapabilityView(w.Industry.Alloy(a, month).Supply, w.Policy.AlloyDeficit(a, month)), designs.MoveToImmutable(), Requirement(w, a, month));
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
