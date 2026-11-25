using Singularity.Core.Commands;
using Singularity.Core.Industry;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session;

/// <summary>
/// Inputs of the product layer. Reads of the capability and knowledge layers are their public pure functions; the design commands,
/// <c>start_series</c> and <c>close_line</c> of month t are the external commands in <c>cmd.*</c> whose stamp is t. [Extension point,
/// T6c] AI decisions will live in <c>pol.*</c> and join the external ones here in command-key order; <c>force</c> is taken only from
/// <c>cmd.*</c>, and an AI decision never has it. No RNG here.
/// </summary>
internal sealed class ProductsInputsAdapter(SliceWorld world) : IProductsInputs
{
    public bool Knows(int actorRow, int nodeNumber, long month) => KnowsNode(actorRow, nodeNumber);

    public Fx6 AdmittedYield(int siteRow, int processNumber, long month) =>
        KnowsNode(siteRow, processNumber) ? world.Industry.Yield(siteRow, world.IndustryLayout.RowOf(processNumber), month).Yield : Fx6.Zero;

    public Fx6 CultureYield(int siteRow, int processNumber, long month) =>
        KnowsNode(siteRow, processNumber) ? world.Industry.Yield(siteRow, world.IndustryLayout.RowOf(processNumber), month).CultureYield : Fx6.Zero;

    public ProductionProfile Profile(int siteRow, ReadOnlySpan<PrcSetItem> prcSet, long month) => world.Industry.Profile(siteRow, prcSet, month);

    public Fx6 AlloySupply(int siteRow, long month) => world.Industry.Alloy(siteRow, month).Supply;

    public long Granted(int siteRow, int category, int requesterId, long month) => world.Industry.Granted(siteRow, category, requesterId);

    public Fx6 ProcurementBudget(int actorRow, long month) => world.ProcurementBudget(actorRow);

    public Fx6 ProcurementBase(int actorRow, long month) => world.ProcurementBase(actorRow);

    // mastered(a, p): mastered_run ≥ 6 at some site of the actor (contract r6 §9.1; in the slice a site is an actor).
    public bool Mastered(int actorRow, int processNumber, long month)
    {
        int row = world.IndustryLayout.RowOf(processNumber);
        if (row < 0)
            return false;
        for (int s = 0; s < world.IndustryLayout.SiteCount; s++)
        {
            if (world.IndustryLayout.SiteOwner[s] == actorRow && world.Industry.MasteredRun(s, row) >= IndustryParams.MasteredMonths)
                return true;
        }
        return false;
    }

    // External commands first, then the AI's (key order: SourceClass.Ai is last).
    public IReadOnlyList<FleetCommand> FleetCommands(int actorRow, long month)
    {
        var list = new List<FleetCommand>();
        foreach ((CommandKind kind, DesignHandle design, int n) in world.Commands.FleetCommands(actorRow, month))
            list.Add(new FleetCommand(kind == CommandKind.SellCohort ? FleetCommandKind.Sell : FleetCommandKind.Retire, design, n));
        list.AddRange(world.Policy.AiFleetCommands(actorRow, month));
        return list;
    }

    public IReadOnlyList<DesignCommand> DesignCommands(int actorRow, long month)
    {
        var list = new List<DesignCommand>();
        // Key order: the external sources (player, harness) first, then the AI (SourceClass.Ai is last).
        foreach ((CommandKind kind, DesignHandle target, var alts) in world.Commands.DesignCommands(actorRow, month))
        {
            list.Add(kind switch
            {
                CommandKind.ComposeDesign => DesignCommand.Compose(target.Slot, alts),
                CommandKind.DeriveDesign => DesignCommand.Derive(target, alts),
                _ => DesignCommand.Cancel(target),
            });
        }
        list.AddRange(world.Policy.AiDesignCommands(actorRow, month));
        return list;
    }

    // Key order: the external command, then the AI's; the layer applies the first whose handle resolves (SLICE_COMPOSITION §7.5). The AI's start never
    // carries force (spec A36). Nothing is checked here: resolution of a handle is the layer's alone.
    public int SeriesStarts(int actorRow, int design, long month, Span<SeriesStart> into)
    {
        int n = 0;
        if (world.Commands.Series(actorRow, design, month) is { } s)
            into[n++] = new SeriesStart(s.Design, s.Rate, s.Force);
        if (world.Policy.AiStart(actorRow, design, month) is { } ai)
            into[n++] = new SeriesStart(ai.Design, ai.Rate, false);
        return n;
    }

    public int LineCloses(int actorRow, int design, long month, Span<DesignHandle> into)
    {
        int n = 0;
        if (world.Commands.CloseLine(actorRow, design, month) is { } c)
            into[n++] = c;
        if (world.Policy.AiClose(actorRow, design, month) is { } ai)
            into[n++] = ai;
        return n;
    }

    // Spec A39, A41 (F15): T_a of this month's plan on the line the policy chose as the training series, else 0.
    public Fx6 TrainingBudget(int actorRow, int design, long month) =>
        world.Policy.TrainingTarget(actorRow) == design
            ? world.IndustryInputs.Plan(actorRow, month, world.Industry.RestActor(actorRow, month)).TrainingBudget
            : Fx6.Zero;

    // Spec A41: a line the AI launched runs at r_AI = ⌊B_proc(t)/unit_cost_floor⌋, recomputed from the budget of the month; the training series adds
    // the capability item (B_proc + B_cap). A line started by a command keeps the rate of that command.
    public bool PlayerRateCommands(int actorRow) => world.Policy.IsAdviser(actorRow);

    public Fx6? LineRate(int actorRow, int design, long month)
    {
        // The player's start on the line of an advised actor takes the rate from the AI at once (the policy drops the AI flag from the next month).
        if (world.Policy.IsAdviser(actorRow) && world.Commands.Series(actorRow, design, month) is { } series && world.Products.Resolves(actorRow, series.Design))
            return null;
        if (!world.Policy.AiLine(actorRow, design, world.Products.Generation(actorRow, design)))
            return null;
        BudgetShares shares = world.SharesOf(actorRow);
        Fx6 budget = shares.Procurement * world.Policy.Budget(actorRow);
        if (world.Policy.TrainingTarget(actorRow) == design)
            budget += shares.Capability * world.Policy.Budget(actorRow);
        // Spec A42.5: the expected sale revenue of the month joins the base; PURCHASE_CAP keeps the upkeep at U_LO.
        Fx6 need = Fx6.Zero;
        for (int d = 0; d < world.ProductsLayout.Designs; d++)
        {
            if (world.Products.Stage(actorRow, d) is DesignStage.Series or DesignStage.InService)
                need += Fx6.FromInt(world.Products.InService(actorRow, d).Raw / Fx6.Scale) * world.Products.Upkeep(actorRow, d);
        }
        Fx6 rate = world.Policy.AiLineRate(actorRow, budget, shares.Procurement * world.Policy.Budget(actorRow),
            world.Products.UnitCostFloor(actorRow, design), need, world.Products.Upkeep(actorRow, design));
        // Spec A41 F17b: with two lines the working one (smaller g_p, a tie to the smaller id) runs at min(r_AI, r_keep), the rest of the money goes to the candidate.
        return IsWorkingOfPair(actorRow, design) && world.IndustryParameters.RKeep < rate ? world.IndustryParameters.RKeep : rate;
    }

    private bool IsWorkingOfPair(int actorRow, int design)
    {
        if (world.Policy.LineClose != LineCloseRule.YProd || !world.Config.ProductAi)
            return false;
        int other = -1;
        for (int d = 0; d < world.ProductsLayout.Designs; d++)
        {
            if (!world.Products.IsLineNow(actorRow, d))
                continue;
            if (d != design && other >= 0)
                return false;   // three lines: not a pair (the policy invariant rejects it)
            if (d != design)
                other = d;
        }
        if (other < 0)
            return false;
        // The working line is the one the shared ordering does not pick as the candidate (the same rule as training and the policy).
        ProductsSystem p = world.Products;
        return PolicySystem.Candidate(design, p.PlatformGen(actorRow, design), p.StageOMonth(actorRow, design),
            other, p.PlatformGen(actorRow, other), p.StageOMonth(actorRow, other)) == other;
    }

    // The player's programme commands, then the AI's (key order: SourceClass.Ai is last).
    public int ProgrammeCommands(int actorRow, long month, Span<ProgrammeCommand> into)
    {
        int n = world.Commands.ProgrammeCommands(actorRow, month, into);
        return n + world.Policy.AiProgrammeCommands(actorRow, month, into[n..]);
    }

    public ProgrammeVerdict ProgrammeVerdict(int actorRow, int processNumber, bool abroad, long month) =>
        MasteryChecks.Verdict(world, actorRow, processNumber, abroad, month);

    // Only the L_P control reads it (spec §6.1): t_known of the APP node of the generation.
    public long AppKnownSince(int actorRow, int gen, long month) =>
        world.Knowledge.KnownSince(actorRow, world.Graph.RowOf(world.ProductsLayout.GenApp[gen - 1]));

    private bool KnowsNode(int actorRow, int nodeNumber)
    {
        int row = world.Graph.RowOf(nodeNumber);
        return row >= 0 && world.Knowledge.IsKnown(actorRow, row);
    }
}
