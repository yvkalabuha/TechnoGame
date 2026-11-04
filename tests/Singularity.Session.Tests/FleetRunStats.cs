using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>
/// One 384-month run of the slice watched month by month (TASK-0028): the invariants of the fleet rule that no single fixture can show
/// (spec §6.10 P0 checks R1, R4, R5, R-gen, FL6, FL7, no refused AI command, <c>mastered_run</c> consistency) and the counters of the
/// information print (E-gen, sales, windows, u, «парк на keep», leader changes).
/// </summary>
internal sealed class FleetRunStats
{
    public const int Months = 384, Window = 48, Windows = Months / Window;
    private const int KeepYears = 24;

    public required ulong Seed { get; init; }
    public required SliceRunConfig Config { get; init; }
    public string[] ActorIds { get; private set; } = [];
    public PolicyProfile[] Profiles { get; private set; } = [];

    /// <summary>Violations of the checks, as text; empty when the run is clean.</summary>
    public List<string> Violations { get; } = [];

    public long[,] Output { get; private set; } = new long[0, 0];                    // actor, window: units
    public List<long>[,] UPermille { get; private set; } = new List<long>[0, 0];     // actor, window: u of months with a fleet
    public long[] Sold { get; private set; } = [];                                    // actor: units sold by the AI
    public long[] Retired { get; private set; } = [];
    public long[] SaleIncomeTotal { get; private set; } = [];                          // actor: raw
    public long[,] CommandsByPair { get; private set; } = new long[0, 0];             // (gen L, gen d), all actors
    public long[] SoldByGen { get; private set; } = new long[6];                      // units by gen(d)
    public long[] Armed { get; private set; } = [];
    public long[] SilentByBind { get; private set; } = [];
    public long[,] ArmedByWindow { get; private set; } = new long[0, 0];
    public long[,] SilentByWindow { get; private set; } = new long[0, 0];
    public int LaggardSales { get; private set; }                                      // sales with gen(d) ≥ G(seller) at gap ≥ 1
    public List<(int Actor, long Month)> LaggardSalesList { get; } = [];
    public int[] KeepActorYears { get; private set; } = [];                            // per actor: years in windows 5…7 «at keep»
    public int[] ActorYears { get; private set; } = [];
    public int LeaderChanges { get; private set; }
    public int[] ChangesByComponent { get; } = new int[4];                             // G, T, N, P
    public int FrontMonths { get; private set; }                                       // months with G_front ≥ 1
    public int FrontMax { get; private set; }

    public static FleetRunStats Run(ulong seed, SliceRunConfig? template = null)
    {
        SliceRunConfig config = (template ?? new SliceRunConfig()) with { Seed = seed, Threads = 4, ChunkSize = 2 };
        var stats = new FleetRunStats { Seed = seed, Config = config };
        stats.Execute();
        return stats;
    }

    private void Execute()
    {
        SliceSession s = SliceTestData.Create(Config);
        Session = s;
        SliceWorld w = s.World;
        ProductsSystem p = w.Products;
        PolicySystem pol = w.Policy;
        int n = w.Scenario.ActorCount, designs = w.ProductsLayout.Designs, gens = w.ProductsLayout.Generations;
        ActorIds = [.. w.Scenario.ActorId];
        Profiles = [.. Enumerable.Range(0, n).Select(a => pol.Profile(a))];
        Output = new long[n, Windows];
        UPermille = new List<long>[n, Windows];
        ArmedByWindow = new long[n, Windows];
        SilentByWindow = new long[n, Windows];
        CommandsByPair = new long[gens + 1, gens + 1];
        for (int a = 0; a < n; a++)
        {
            for (int k = 0; k < Windows; k++)
                UPermille[a, k] = [];
        }
        KeepActorYears = new int[n];
        ActorYears = new int[n];
        var lastArmed = new long[n];
        var lastSilent = new long[n];
        var emptySince = new int[n];
        var previous = new Pending[n];
        Fx6[] expectedIncome = new Fx6[n];
        var soldBefore = new Fx6[n, designs];
        var leader = w.Scenario.TechLead;
        int candidate = -1, candidateSince = -1;
        TechKey[] keyAtCandidate = new TechKey[n];
        var sumIncome = new long[n];
        SaleIncomeTotal = new long[n];
        var saleOnly = new long[n];   // the revenue of the sales alone: ext_total also holds the licence flows since T6d
        var mrPrev = new int[w.IndustryLayout.SiteCount, w.IndustryLayout.ProcessCount];
        for (int site = 0; site < mrPrev.GetLength(0); site++)
        {
            for (int r = 0; r < mrPrev.GetLength(1); r++)
                mrPrev[site, r] = w.Industry.MasteredRun(site, r);
        }

        for (long m = 0; m < Months; m++)
        {
            // State after month m−1: the snapshot the decisions of month m read.
            var def = new bool[n][];
            var stageSnap = new DesignStage[n, designs];
            for (int a = 0; a < n; a++)
            {
                def[a] = p.DefK(a, m);
                for (int d = 0; d < designs; d++)
                    stageSnap[a, d] = p.Stage(a, d);
            }
            s.StepThroughMonth(m);

            // Month m has run: the commands decided in m−1 have executed. Check them against what they were decided on.
            for (int a = 0; a < n; a++)
            {
                if (p.SaleIncome(a) != expectedIncome[a])
                    Violations.Add($"FL7 actor {a} month {m}: sale_income {p.SaleIncome(a)} != inc_exp {expectedIncome[a]}");
                // T6d: ext_total also holds the licence flow of the month (contract §3.1 r6/r7); the flows net out over the actors (L-M1).
                sumIncome[a] += p.SaleIncome(a).Raw + w.Policy.LicenceFlow(a).Raw;
                saleOnly[a] += p.SaleIncome(a).Raw;
                if (p.ExternalTotal(a).Raw != sumIncome[a])
                    Violations.Add($"FL6 actor {a} month {m}: ext_total {p.ExternalTotal(a)} != Σ (sale_income + LicenceFlow)");
                CheckExecuted(a, m, previous[a], gens);
            }

            for (int a = 0; a < n; a++)
            {
                long rate = 0;
                for (int d = 0; d < designs; d++)
                    rate += p.Rate(a, d).Raw;
                Output[a, (int)(m / Window)] += rate;
                Fx6 basis = w.ProcurementBase(a);
                if (basis > Fx6.Zero && Enumerable.Range(0, designs).Any(d => p.InService(a, d) > Fx6.Zero))
                    UPermille[a, (int)(m / Window)].Add(p.UpkeepNeeded(a).Raw * 1000 / basis.Raw);
                ArmedByWindow[a, (int)(m / Window)] += pol.FleetArmedMonths(a) - lastArmed[a];
                SilentByWindow[a, (int)(m / Window)] += pol.FleetSilentByBindMonths(a) - lastSilent[a];
                lastArmed[a] = pol.FleetArmedMonths(a);
                lastSilent[a] = pol.FleetSilentByBindMonths(a);
            }

            // Sales of this month by a laggard of its own front (#146 print): gen(d) ≥ G(seller) with gap ≥ 1.
            int gFront = p.GFront(m);
            if (gFront >= 1)
                FrontMonths++;
            FrontMax = Math.Max(FrontMax, gFront);
            for (int a = 0; a < n; a++)
            {
                for (int d = 0; d < designs; d++)
                {
                    if (p.Sold(a, d) > soldBefore[a, d] && Fx6.Zero < p.SaleIncome(a))
                    {
                        // G of the seller in the snapshot the layer used (after m−1) is not kept; the state after m−1 differs only by one month.
                        if (p.Gen(a, d) >= p.FieldedGen(a, m - 1) && gFront - p.Gen(a, d) >= 1)
                        {
                            LaggardSales++;
                            LaggardSalesList.Add((a, m));
                        }
                    }
                    soldBefore[a, d] = p.Sold(a, d);
                }
            }

            // mastered_run is the saturating counter of the two conditions (contract r6 §9.1).
            for (int site = 0; site < mrPrev.GetLength(0); site++)
            {
                for (int r = 0; r < mrPrev.GetLength(1); r++)
                {
                    bool ok = w.Industry.ProcYield(site, r) >= w.IndustryParameters.MasteredYield && w.Industry.ProcV(site, r) >= w.IndustryParameters.VMin;
                    int expected = ok ? Math.Min(6, mrPrev[site, r] + 1) : 0;
                    int actual = w.Industry.MasteredRun(site, r);
                    if (actual != expected)
                        Violations.Add($"mastered_run site {site} process row {r} month {m}: {actual} != {expected}");
                    mrPrev[site, r] = actual;
                }
            }

            // Decisions of month m (they run in m+1): remember what they were decided on.
            for (int a = 0; a < n; a++)
            {
                var cmds = pol.AiFleetCommands(a, m + 1);
                expectedIncome[a] = pol.ExpectedSaleIncome(a);
                var cohorts = new Fx6[designs][];
                for (int d = 0; d < designs; d++)
                    cohorts[d] = p.Cohorts(a, d);
                previous[a] = new Pending(cmds, def[a], stageSnap, a, cohorts);
            }

            // «Парк на keep»: every project that is not a line and not in Def(K) has been empty for KeepYears months (spec §6.10).
            for (int a = 0; a < n; a++)
            {
                bool empty = true;
                bool[] now = p.DefK(a, m);
                bool any = false;
                for (int d = 0; d < designs; d++)
                {
                    if (p.Stage(a, d) is DesignStage.None or DesignStage.Series)
                        continue;
                    if (!now[d] && p.InService(a, d) > Fx6.Zero)
                        empty = false;
                    any |= now[d];
                }
                emptySince[a] = empty ? emptySince[a] + 1 : 0;
                if (m % 12 == 11 && m / Window >= 5)
                {
                    ActorYears[a]++;
                    if (any && emptySince[a] >= KeepYears)
                        KeepActorYears[a]++;
                }
            }

            // The leader of the great powers (USA, GBR, SUN) by K (contract §9.1): ties keep the previous leader; a change counts after 24 months.
            if (m < 360)
            {
                int[] great = [w.Scenario.ActorIndex("USA"), w.Scenario.ActorIndex("GBR"), w.Scenario.ActorIndex("SUN")];
                TechKey best = default;
                var bestSet = new List<int>();
                foreach (int a in great)
                {
                    TechKey k = p.KeyOf(a, m);
                    int c = bestSet.Count == 0 ? 1 : k.CompareTo(best);
                    if (c > 0)
                    {
                        best = k;
                        bestSet.Clear();
                        bestSet.Add(a);
                    }
                    else if (c == 0)
                    {
                        bestSet.Add(a);
                    }
                    keyAtCandidate[a] = k;
                }
                int now2 = bestSet.Contains(leader) ? leader : bestSet.Count == 1 ? bestSet[0] : leader;
                if (now2 == leader)
                {
                    candidate = -1;
                }
                else if (candidate != now2)
                {
                    candidate = now2;
                    candidateSince = (int)m;
                }
                else if (m - candidateSince + 1 >= 24)
                {
                    TechKey from = p.KeyOf(leader, candidateSince), to = p.KeyOf(candidate, candidateSince);
                    LeaderChanges++;
                    ChangesByComponent[from.G != to.G ? 0 : from.T != to.T ? 1 : from.N != to.N ? 2 : 3]++;
                    leader = candidate;
                    candidate = -1;
                }
            }
        }

        Sold = [.. Enumerable.Range(0, n).Select(a => pol.FleetUnitsSold(a))];
        Retired = [.. Enumerable.Range(0, n).Select(a => pol.FleetUnitsRetired(a))];
        Armed = [.. Enumerable.Range(0, n).Select(a => pol.FleetArmedMonths(a))];
        SilentByBind = [.. Enumerable.Range(0, n).Select(a => pol.FleetSilentByBindMonths(a))];
        for (int a = 0; a < n; a++)
        {
            SaleIncomeTotal[a] = saleOnly[a];
            long rejected = 0;
            foreach (CommandRejection r in Enum.GetValues<CommandRejection>().Where(r => (int)r >= (int)CommandRejection.BadRetireSlot))
                rejected += p.RejectionCount(a, r);
            if (rejected != 0)
                Violations.Add($"actor {a}: {rejected} fleet commands of the AI were refused");
            for (int d = 0; d < designs; d++)
            {
                if (p.Stage(a, d) != DesignStage.None)
                    SoldByGen[Math.Min(5, p.Gen(a, d))] += p.Sold(a, d).Raw / Fx6.Scale;
            }
            for (int gl = 1; gl <= gens; gl++)
            {
                for (int gd = 1; gd <= gens; gd++)
                    CommandsByPair[gl, gd] += pol.FleetCommandsByPair(a, gl, gd);
            }
        }
    }

    private sealed record Pending(IReadOnlyList<FleetCommand> Commands, bool[] DefK, DesignStage[,] StageSnapshot, int Actor, Fx6[][] CohortsAfterDecision);

    // R1, R4, R-gen on the commands executed this month, against the state they were decided on.
    private void CheckExecuted(int a, long m, Pending? was, int gens)
    {
        if (was is null || was.Commands.Count == 0)
            return;
        ProductsSystem p = Session.World.Products;
        if (Config.FleetLineCond != FleetLineCond.Gen)
            return;
        int line = -1;
        for (int d = 0; d < was.StageSnapshot.GetLength(1); d++)
        {
            if (p.Stage(a, d) == DesignStage.Series)
                line = d;
        }
        foreach (FleetCommand c in was.Commands)
        {
            if (was.StageSnapshot[a, c.Design] == DesignStage.Series || p.Stage(a, c.Design) == DesignStage.Series)
                Violations.Add($"R4 actor {a} month {m}: design {c.Design} in С was commanded");
            if (was.DefK[c.Design] && p.InService(a, c.Design).Raw / Fx6.Scale < p.Parameters.NServiceMin)
                Violations.Add($"R1 actor {a} month {m}: design {c.Design} in Def(K) fell below keep");
            if (line >= 0 && p.Gen(a, line) <= p.Gen(a, c.Design))
                Violations.Add($"R-gen actor {a} month {m}: gen(L) {p.Gen(a, line)} <= gen(d) {p.Gen(a, c.Design)}");
            // R4 (age): the cohorts younger than A_PROT are as they were after the decision month.
            Fx6[] before = was.CohortsAfterDecision[c.Design], after = p.Cohorts(a, c.Design);
            for (int y = 0; y < before.Length; y++)
            {
                if (m - p.CohortMidyear(y) < 60 && before[y] != after[y] && !(after[y] == Fx6.Zero && m - p.CohortMidyear(y) >= p.Parameters.ServiceLife))
                    Violations.Add($"R4 actor {a} month {m}: a cohort of year index {y} younger than A_PROT changed");
            }
        }
    }

    internal SliceSession Session { get; set; } = null!;
}
