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
    public ProductsHarness(int threads = 1, int chunkSize = 1, ProductsMode mode = ProductsMode.Full, ProductsParams? parameters = null)
    {
        Layout = MakeLayout(out _);
        Start = ProductsStart.FromScenario(Scenario, CoreData.Nodes, CoreData.ProductClasses, Layout);
        Table = new Table(Scenario.ActorCount);
        Calendar = new TickCalendar(new CivilDate(Scenario.StartYear, Scenario.StartMonth, 1));
        int chunks = TickRunner.ChunkCountFor(Table.RowCount, chunkSize);
        Mailbox = new IndustryMailbox(chunks);
        Inputs = new ScriptedInputs(this);
        Params = parameters ?? new ProductsParams();
        Products = new ProductsSystem(Table, Layout, Start, Params, Inputs, Mailbox, mode);
        Runner = new TickRunner(Table, 1, [Products], chunkSize, threads, Calendar);
        Grants = new long[Table.RowCount, Categories, Layout.Requesters];
    }

    public ProductsLayout Layout { get; }
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
    public Func<int, int, long, Fx6> Rate { get; set; } = (_, _, _) => Fx6.Zero;
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

    public string Hash() => StateHash.ComputeHex(Table, 1, Runner.Tick);

    private bool StartKnows(int actor, int number)
    {
        foreach (int row in Scenario.KnownOf(actor))
        {
            if (CoreData.Nodes.Number[row] == number)
                return true;
        }
        return false;
    }

    internal sealed class ScriptedInputs(ProductsHarness h) : IProductsInputs
    {
        public bool Knows(int actorRow, int nodeNumber, long month) =>
            h.Knows is null ? h.StartKnows(actorRow, nodeNumber) : h.Knows(actorRow, nodeNumber);

        public Fx6 AdmittedYield(int siteRow, int processNumber, long month) =>
            Knows(siteRow, processNumber, month) ? h.Yield(siteRow, processNumber) : Fx6.Zero;

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

        public Fx6 AlloySupply(int siteRow, long month) => h.Alloy(siteRow, month);

        public long Granted(int siteRow, int category, int requesterId, long month) => h.Grants[siteRow, category, requesterId];

        public Fx6 ProcurementBudget(int actorRow, long month) => h.Budget(actorRow, month);

        public DesignOrder? Order(int actorRow, long month) => h.Order(actorRow, month);

        public bool LaunchSeries(int actorRow, int design, long month) => h.Launch(actorRow, design, month);

        public bool CloseLine(int actorRow, int design, long month) => h.Close(actorRow, design, month);

        public Fx6 LineRate(int actorRow, int design, long month) => h.Rate(actorRow, design, month);

        public long AppKnownSince(int actorRow, int gen, long month) => h.AppKnownSince(actorRow, gen);
    }
}
