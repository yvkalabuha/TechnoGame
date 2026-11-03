using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Products;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>
/// The product layer alone on a Kernel runner with the slice calendar and core data; other layers and the AI are scripted
/// inputs. After every monthly step the harness drains the capability mailbox (as the capability merge would) and grants
/// the requests by <see cref="GrantShare"/>; the grants are visible to the next month (contract §4.6).
/// </summary>
internal sealed class ProductsHarness
{
    /// <param name="actorPerm">Storage order of the actors: table row r holds scenario actor <c>actorPerm[r]</c>; scripts get the stable actor index.</param>
    /// <param name="years">Cohort years of the layout (default <see cref="ProductTestData.Years"/>).</param>
    public ProductsHarness(int threads = 1, int chunkSize = 1, ProductsMode mode = ProductsMode.Full, ProductsParams? parameters = null,
        int[]? actorPerm = null, int years = Years, int? maxDesigns = null, Action<Table>? corruptInMerge = null)
    {
        Layout = MakeLayout(out _, years, maxDesigns ?? Scenario.MaxDesigns);
        ProductsStart baseStart = ProductsStart.FromScenario(Scenario, CoreData.Nodes, CoreData.ProductClasses, Layout);
        ActorOf = actorPerm ?? [.. Enumerable.Range(0, Scenario.ActorCount)];
        Start = actorPerm is null ? baseStart : Permute(baseStart, ActorOf);
        Table = new Table(Scenario.ActorCount);
        Calendar = new TickCalendar(new CivilDate(Scenario.StartYear, Scenario.StartMonth, 1));
        int chunks = TickRunner.ChunkCountFor(Table.RowCount, chunkSize);
        Mailbox = new IndustryMailbox(chunks);
        Inputs = new ScriptedInputs(this);
        Params = parameters ?? new ProductsParams();
        Products = new ProductsSystem(Table, Layout, Start, Params, Inputs, Mailbox, mode);
        Runner = new TickRunner(Table, 1, corruptInMerge is null ? [Products] : [Products, new Saboteur(corruptInMerge, Table)], chunkSize, threads, Calendar);
        Grants = new long[Table.RowCount, Categories, Layout.Requesters];
    }

    public ProductsLayout Layout { get; }

    /// <summary>Scenario actor (stable index) of each table row.</summary>
    public int[] ActorOf { get; }

    /// <summary>Table row of a scenario actor.</summary>
    public int RowOf(int actor) => Array.IndexOf(ActorOf, actor);
    public ProductsStart Start { get; }
    public Table Table { get; }
    public TickCalendar Calendar { get; }
    public IndustryMailbox Mailbox { get; }
    public ScriptedInputs Inputs { get; }
    public ProductsParams Params { get; }
    public ProductsSystem Products { get; }
    public TickRunner Runner { get; }

    /// <summary>granted[actor, category, requester] of the last month's requests.</summary>
    public long[,,] Grants { get; private set; }

    public Fx6 GrantShare { get; set; } = Fx6.One;

    public List<OutputReport> Reports { get; } = new();
    public List<WorkforceRequest> Requests { get; } = new();

    // Script (AI and other layers).
    public Func<int, int, bool>? Knows { get; set; }
    public Func<int, int, Fx6> Yield { get; set; } = (_, _) => Fx6.One;
    public Func<int, long, Fx6> Budget { get; set; } = (_, _) => Fx6.FromRaw(440_000);
    public Func<int, long, Fx6> Alloy { get; set; } = (_, _) => Fx6.FromInt(1_000_000);
    public Func<int, long, DesignOrder?> Order { get; set; } = (_, _) => null;
    public Func<int, int, long, bool> Launch { get; set; } = (_, _, _) => false;
    public Func<int, int, long, bool> Close { get; set; } = (_, _, _) => false;
    public Func<int, int, long, Fx6?> Rate { get; set; } = (_, _, _) => null;

    /// <summary>Explicit commands of the script, applied after the one derived from <see cref="Order"/>.</summary>
    public Func<int, long, IReadOnlyList<DesignCommand>> Commands { get; set; } = (_, _) => [];

    /// <summary>Explicit <c>start_series</c> of the script; when it returns null, <see cref="Launch"/> and <see cref="Rate"/> decide.</summary>
    public Func<int, int, long, SeriesStart?> Series { get; set; } = (_, _, _) => null;
    public Func<int, int, long> AppKnownSince { get; set; } = (_, _) => -1;

    public long Month => Calendar.MonthIndex(Runner.Tick);

    /// <summary>Runs whole days until the month step <paramref name="month"/> is done.</summary>
    public void RunThroughMonth(long month)
    {
        long last = Calendar.TickOfMonth(month);
        while (Runner.Tick <= last)
        {
            bool monthly = Calendar.IsDue(Cadence.Monthly, Runner.Tick);
            Runner.RunTick();
            if (!monthly)
                continue;
            Reports.Clear();
            Requests.Clear();
            Mailbox.Drain(Reports, Requests);
            var grants = new long[Table.RowCount, Categories, Layout.Requesters];
            foreach (WorkforceRequest q in Requests)
                grants[q.Site, q.Category, q.RequesterId] = (Fx6.FromInt(q.N) * GrantShare).Raw / Fx6.Scale;
            Grants = grants;
        }
    }

    public string Hash() => StateHash.ComputeHex(Table, ProductsSystem.LayoutVersion, Runner.Tick);

    private static ProductsStart Permute(ProductsStart b, int[] perm)
    {
        ProductsLayout l = b.Layout;
        var s = new ProductsStart(l, b.Actors);
        int d = l.Designs;
        for (int r = 0; r < perm.Length; r++)
        {
            int a = perm[r];
            Array.Copy(b.Stage, a * d, s.Stage, r * d, d);
            Array.Copy(b.PlatformGen, a * d, s.PlatformGen, r * d, d);
            Array.Copy(b.Alts, a * d * l.SlotCount, s.Alts, r * d * l.SlotCount, d * l.SlotCount);
            Array.Copy(b.Cohort, a * d * l.Years, s.Cohort, r * d * l.Years, d * l.Years);
            Array.Copy(b.BureauExp, a * l.Bureaus, s.BureauExp, r * l.Bureaus, l.Bureaus);
        }
        return s;
    }

    private bool StartKnows(int actor, int number)
    {
        foreach (int row in Scenario.KnownOf(actor))
        {
            if (CoreData.Nodes.Number[row] == number)
                return true;
        }
        return false;
    }

    /// <summary>Test-only system that rewrites columns of the product table in its merge, so the product Post sees a corrupted state.</summary>
    private sealed class Saboteur(Action<Table> corrupt, Table table) : ITickSystem
    {
        public int SystemId => 999;
        public Cadence Cadence => Cadence.Monthly;
        public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd) { }
        public void Merge(in TickContext context, int chunkCount) => corrupt(table);
        public void Post(in TickContext context) { }
    }

    internal sealed class ScriptedInputs(ProductsHarness h) : IProductsInputs
    {
        private int A(int row) => h.ActorOf[row];

        public bool Knows(int actorRow, int nodeNumber, long month) =>
            h.Knows is null ? h.StartKnows(A(actorRow), nodeNumber) : h.Knows(A(actorRow), nodeNumber);

        public Fx6 AdmittedYield(int siteRow, int processNumber, long month) =>
            Knows(siteRow, processNumber, month) ? h.Yield(A(siteRow), processNumber) : Fx6.Zero;

        public ProductionProfile Profile(int siteRow, ReadOnlySpan<PrcSetItem> prcSet, long month)
        {
            var items = new ProfileItem[prcSet.Length];
            for (int i = 0; i < prcSet.Length; i++)
            {
                int p = prcSet[i].ProcessNumber;
                items[i] = new ProfileItem(p, ProcessYield.Control(AdmittedYield(siteRow, p, month)), Knows(siteRow, p, month),
                    prcSet[i].N, prcSet[i].BaseCost);
            }
            return IndustryMath.Profile(items, Fx6.FromRaw(10_000));
        }

        public Fx6 AlloySupply(int siteRow, long month) => h.Alloy(A(siteRow), month);

        public long Granted(int siteRow, int category, int requesterId, long month) => h.Grants[siteRow, category, requesterId];

        public Fx6 ProcurementBudget(int actorRow, long month) => h.Budget(A(actorRow), month);

        // A scripted order becomes a compose_design for the newest APP the actor knows (the validator's g*), with the preset
        // composition of the order's variant; the order's generation is only a trigger.
        public IReadOnlyList<DesignCommand> DesignCommands(int actorRow, long month)
        {
            var list = new List<DesignCommand>();
            if (h.Order(A(actorRow), month) is { } order)
            {
                Func<int, bool> knows = n => Knows(actorRow, n, month);
                int target = 0;
                for (int g = 1; g <= h.Layout.Generations; g++)
                {
                    if (knows(h.Layout.GenApp[g - 1]))
                        target = g;
                }
                var alts = new int[h.Layout.SlotCount];
                // F-PC3: a «producible» that equals «superior» is not a separate design; the order falls back to «superior».
                if (target > 0 && ((order.Variant == DesignVariant.Producible
                        && DesignRules.Producible(h.Layout, target, knows, p => AdmittedYield(actorRow, p, month), h.Params.YProd, null, alts))
                        || DesignRules.Superior(h.Layout, target, knows, alts)))
                    list.Add(new DesignCommand(DesignCommandKind.Compose, target, [.. alts]));
            }
            list.AddRange(h.Commands(A(actorRow), month));
            return list;
        }

        public SeriesStart? StartSeries(int actorRow, int design, long month) =>
            h.Series(A(actorRow), design, month) ?? (h.Launch(A(actorRow), design, month)
                ? new SeriesStart(h.Rate(A(actorRow), design, month) ?? Fx6.Zero, false) : null);

        public bool CloseLine(int actorRow, int design, long month) => h.Close(A(actorRow), design, month);

        public Fx6? LineRate(int actorRow, int design, long month) => h.Rate(A(actorRow), design, month);

        public long AppKnownSince(int actorRow, int gen, long month) => h.AppKnownSince(A(actorRow), gen);
    }
}
