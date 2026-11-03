using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Knowledge;
using Singularity.Core.Tests.Knowledge;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Industry;

/// <summary>
/// The reference fixture (lab/reference/jet_slice/out/trajectory.json) run with the knowledge and capability
/// layers coupled through their snapshot APIs on one Kernel runner with the daily calendar: knowledge reads
/// <c>discipline_volume</c> from the industry snapshot, industry reads <c>known_processes</c> from the knowledge
/// snapshot. Layers that do not exist yet feed reference inputs of the same month: budget, prio and <c>j</c>
/// (<see cref="JetReference.Inputs"/>) and the line rate in force (AI command, <c>rate_in_force</c>). One site
/// per actor, site row = actor row; every actor replays actor 0's inputs.
/// </summary>
internal sealed class JetIndustryRun
{
    public static readonly Cluster[] Clusters = [Cluster.MET, Cluster.CHEM];

    public JetIndustryRun(int threads = 1, int chunkSize = 1, int[]? actorNumbers = null, int[]? processOrder = null,
        IndustryMode mode = IndustryMode.Full, ImmutableArray<CapabilityCurve> curves = default, IIndustryInputs? inputs = null, bool cb = false)
    {
        actorNumbers ??= [0];
        JsonElement tp = JetReference.Trajectory.GetProperty("params");
        Graph = KnowledgeGraph.Create(JetReference.NodeSpecs(), JetReference.Disciplines);
        KStart = new KnowledgeStart([.. actorNumbers], Graph.Count, JetReference.Disciplines.Length);
        for (int a = 0; a < actorNumbers.Length; a++)
        {
            foreach (JsonElement n in JetReference.Trajectory.GetProperty("known_at_start").EnumerateArray())
                KStart.Known[a * Graph.Count + Graph.RowOf(n.GetInt32())] = true;
            for (int d = 0; d < JetReference.Disciplines.Length; d++)
                KStart.KD[a * JetReference.Disciplines.Length + d] = P(tp.GetProperty("K_d0"), JetReference.Disciplines[d]);
        }
        ulong seed = JetReference.Trajectory.GetProperty("seed").GetUInt64();
        var rng = new CounterRng(seed);
        Table = new Table(actorNumbers.Length);
        Calendar = new TickCalendar(JetReference.StartDate);
        int chunks = TickRunner.ChunkCountFor(Table.RowCount, chunkSize);
        Knowledge = new KnowledgeSystem(Table, Graph, KStart, JetReference.Params(),
            new CoupledKnowledgeInputs(new JetReference.Inputs(Graph, rng, KStart.ActorNumber), this), rng, chunks);

        Layout = IndustryLayout.Create(Clusters, ProcessSpecs(processOrder), [.. Enumerable.Range(0, actorNumbers.Length)],
            workforceCategories: 2, skilledCategory: IndustryLayout.SliceSkilled, requesters: 3);
        Params = ReferenceParams();
        IStart = new IndustryStart(Layout);
        for (int s = 0; s < Layout.SiteCount; s++)
        {
            for (int c = 0; c < Clusters.Length; c++)
            {
                Fx6 hc = P(tp, Clusters[c] == Cluster.MET ? "H_MET" : "H_CHEM");
                // Reference site_dims: precision = H_c, purity = H_c − 0.2, quality_control = H_c − 0.3; M = H + 0.2.
                Fx6[] h = [hc, hc - Fx6.FromRaw(200_000), hc - Fx6.FromRaw(300_000)];
                for (int d = 0; d < 3; d++)
                {
                    IStart.H[IStart.Cell(s, c, d)] = h[d];
                    IStart.M[IStart.Cell(s, c, d)] = h[d] + Fx6.FromRaw(200_000);
                }
            }
            foreach (JsonProperty x in tp.GetProperty("X0").EnumerateObject())
                IStart.X[s * Layout.ProcessCount + Layout.RowOf(int.Parse(x.Name, CultureInfo.InvariantCulture))] = JetReference.Dec(x.Value.GetString()!);
        }
        Industry = new IndustrySystem(Table, Layout, IStart, Params, inputs ?? (cb ? new CbInputs(new ReferenceIndustryInputs(this)) : new ReferenceIndustryInputs(this)), chunks,
            cb ? new CbCultureRule() : new V23CultureRule(), mode, curves);
        Lines = new ReferenceLines(this, Industry.CreateMailbox(chunks, Cadence.Monthly));
        Runner = new TickRunner(Table, seed, [Knowledge, Industry, Lines], chunkSize, threads, Calendar);
    }

    public KnowledgeGraph Graph { get; }
    public KnowledgeStart KStart { get; }
    public Table Table { get; }
    public TickCalendar Calendar { get; }
    public KnowledgeSystem Knowledge { get; }
    public IndustryLayout Layout { get; }
    public IndustryParams Params { get; }
    public IndustryStart IStart { get; }
    public IndustrySystem Industry { get; }
    public ReferenceLines Lines { get; }
    public TickRunner Runner { get; }

    public void RunThroughMonth(long month)
    {
        long last = Calendar.TickOfMonth(month);
        while (Runner.Tick <= last)
            Runner.RunTick();
    }

    public static Fx6 P(JsonElement obj, string name) => JetReference.Dec(obj.GetProperty(name).GetString()!);

    public static JsonElement Node(int number) =>
        JetReference.Trajectory.GetProperty("nodes").GetProperty(number.ToString(CultureInfo.InvariantCulture));

    /// <summary>PRC nodes of the fixture (4, 6), optionally in a permuted storage order.</summary>
    public static IndustryProcessSpec[] ProcessSpecs(int[]? order = null)
    {
        var specs = new List<IndustryProcessSpec>();
        foreach (JsonProperty n in JetReference.Trajectory.GetProperty("nodes").EnumerateObject())
        {
            JsonElement v = n.Value;
            if (v.GetProperty("type").GetString() != "PRC")
                continue;
            Fx6[] req = [.. v.GetProperty("req").EnumerateArray().Select(x => JetReference.Dec(x.GetString()!))];
            specs.Add(new IndustryProcessSpec(int.Parse(n.Name, CultureInfo.InvariantCulture),
                Enum.Parse<Cluster>(v.GetProperty("cluster").GetString()!),
                Array.IndexOf(JetReference.Disciplines, v.GetProperty("disc").GetString()), v.GetProperty("tier").GetInt32(),
                new Dims3(req[0], req[1], req[2]), P(v, "labour")));
        }
        specs.Sort((a, b) => a.Number.CompareTo(b.Number));
        return order is null ? [.. specs] : [.. order.Select(i => specs[i])];
    }

    public static IndustryParams ReferenceParams()
    {
        JsonElement tp = JetReference.Trajectory.GetProperty("params");
        return new IndustryParams
        {
            Alpha = P(tp, "alpha"), DeltaH = P(tp, "delta_H"), RhoMax = P(tp, "rho_max"), KCatch = P(tp, "k_catch"),
            Sigma = P(tp, "sigma"), PhiFloor = P(tp, "phi_floor"), Lambda0 = P(tp, "lam0"), DeltaX = P(tp, "delta_X"),
            DeltaM = P(tp, "delta_M"),
            XHalfByTier = [P(tp.GetProperty("Xh"), "3"), P(tp.GetProperty("Xh"), "4"), Fx6.FromInt(2500)],
            CM = P(tp, "c_M"),
            SpillPairs = [(Cluster.MET, Cluster.CHEM, P(tp, "R_MET_CHEM"))],
        };
    }

    /// <summary>Knowledge inputs: reference budget, prio, j; <c>V_d</c> from the industry snapshot (contract §5).</summary>
    private sealed class CoupledKnowledgeInputs(JetReference.Inputs reference, JetIndustryRun run) : IKnowledgeInputs
    {
        public Fx6 ResearchBudget(int actorRow, long month) => reference.ResearchBudget(actorRow, month);

        public Fx6 Priority(int actorRow, int nodeRow, long month) => reference.Priority(actorRow, nodeRow, month);

        public Fx6 J(int actorRow, int nodeRow, long month) => reference.J(actorRow, nodeRow, month);

        public Fx6 DisciplineVolume(int actorRow, int discipline, long month) => run.Industry.DisciplineVolume(actorRow, discipline);
    }

    /// <summary>
    /// Industry inputs of the fixture: knowledge snapshot, constant w/Hcap, invest in money
    /// <c>(δ_M·M0)·cost_M(k(M0))</c> so that M stays at M0 (spec §3.5 A21), no QP, licence, alloy or skilled requests.
    /// </summary>
    internal sealed class ReferenceIndustryInputs(JetIndustryRun run) : IIndustryInputs
    {
        public int RunNumber(int processRow) => run.Layout.Number[processRow];

        private readonly JsonElement _tp = JetReference.Trajectory.GetProperty("params");

        public bool KnowsProcess(int siteRow, int processRow, long month) =>
            run.Knowledge.IsKnown(run.Layout.SiteOwner[siteRow], run.Graph.RowOf(run.Layout.Number[processRow]));

        public Fx6 W(int siteRow, int cluster, long month) => P(_tp, "w");

        public Fx6 Hcap(int siteRow, Dimension dim, long month) => P(_tp, "Hcap");

        public bool QualityProgramme(int siteRow, long month) => false;

        public Fx6 ToolingInvest(int siteRow, int cluster, Dimension dim, long month)
        {
            Fx6 m0 = run.IStart.M[run.IStart.Cell(siteRow, cluster, (int)dim)];
            return run.Params.DeltaM * m0 * run.Params.CostM(IndustryMath.CostTier(m0));
        }

        public Fx6 LicenceShare(int siteRow, int processRow, long month) => Fx6.Zero;

        public long WorkforceAvailable(int siteRow, int category, long month) => 10;

        public Fx6 AlloyCap(int siteRow, long month) => Fx6.Zero;

        public Fx6 AlloyDeficit(int siteRow, long month) => Fx6.Zero;

        public long KnownSince(int siteRow, int processRow, long month) => -1;
    }

    /// <summary>
    /// Inputs of the C-B fixture (jet_ref.py, trajectory_cb): <c>D_a</c> of the month from the reference row, PRC 4 is
    /// <c>planned</c> once known, <c>v_exp</c> = target rate · labour of a known process.
    /// </summary>
    private sealed class CbInputs(ReferenceIndustryInputs b) : IIndustryInputs
    {
        public bool KnowsProcess(int siteRow, int processRow, long month) => b.KnowsProcess(siteRow, processRow, month);

        public Fx6 W(int siteRow, int cluster, long month) => b.W(siteRow, cluster, month);

        public Fx6 Hcap(int siteRow, Dimension dim, long month) => b.Hcap(siteRow, dim, month);

        public bool QualityProgramme(int siteRow, long month) => false;

        public Fx6 ToolingInvest(int siteRow, int cluster, Dimension dim, long month) => b.ToolingInvest(siteRow, cluster, dim, month);

        public Fx6 LicenceShare(int siteRow, int processRow, long month) => Fx6.Zero;

        public long WorkforceAvailable(int siteRow, int category, long month) => b.WorkforceAvailable(siteRow, category, month);

        public Fx6 AlloyCap(int siteRow, long month) => Fx6.Zero;

        public Fx6 AlloyDeficit(int siteRow, long month) => Fx6.Zero;

        public long KnownSince(int siteRow, int processRow, long month) => -1;

        public Fx6 EffortBudget(int actorRow, long month) => JetReference.Raw(JetReference.RowCb(month).GetProperty("effort").GetProperty("D_a"));

        public bool Planned(int siteRow, int processRow, long month) =>
            b.KnowsProcess(siteRow, processRow, month) && b.RunNumber(processRow) == 4;

        public Fx6 PlannedVolume(int siteRow, int processRow, long month)
        {
            if (!b.KnowsProcess(siteRow, processRow, month))
                return Fx6.Zero;
            JsonElement n = Node(b.RunNumber(processRow));
            return P(n, "rate") * P(n, "labour");
        }
    }

    /// <summary>
    /// Stand-in for the product layer: in its compute of month t it runs every known process with the reference
    /// rate in force and <c>yield &gt; 0</c> by the snapshot, and posts <c>report_output(units = rate,
    /// labour_hours = rate·labour)</c>. Optional extra workforce requests for tests.
    /// </summary>
    internal sealed class ReferenceLines(JetIndustryRun run, IndustryMailbox box) : ITickSystem
    {
        public int SystemId => 300;

        public Cadence Cadence => Cadence.Monthly;

        public Func<long, int, IEnumerable<WorkforceRequest>>? Requests { get; set; }

        public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd)
        {
            long t = context.Calendar!.MonthIndex(context.Tick);
            JsonElement rate = JetReference.Row(t).GetProperty("rate_in_force");
            IndustryLayout l = run.Layout;
            for (int s = rowStart; s < rowEnd; s++)
            {
                foreach (int r in l.Order)
                {
                    int number = l.Number[r];
                    Fx6 rt = JetReference.Raw(rate.GetProperty(number.ToString(CultureInfo.InvariantCulture)));
                    bool known = run.Knowledge.IsKnown(l.SiteOwner[s], run.Graph.RowOf(number));
                    if (known && rt > Fx6.Zero && run.Industry.Yield(s, r, t).Yield > Fx6.Zero)
                        box.ReportOutput(chunk, new OutputReport(s, number, rt, rt * P(Node(number), "labour")));
                }
                foreach (WorkforceRequest q in Requests?.Invoke(t, s) ?? [])
                    box.ReserveWorkforce(chunk, q);
            }
        }

        public void Merge(in TickContext context, int chunkCount) { }

        public void Post(in TickContext context) { }
    }
}
