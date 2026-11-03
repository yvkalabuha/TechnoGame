using System.Collections.Immutable;
using System.Text.Json;
using Singularity.Core.Data;
using Singularity.Core.Knowledge;
using Singularity.Core.Tests.Data;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Knowledge;

/// <summary>
/// lab/reference/jet_slice/out/*.json (TASK-0012) and a knowledge-only replay of its trajectory: the
/// layers that are not built yet (budget, AI decisions, industry) feed the knowledge system from the
/// reference values of the same month, so every knowledge number is compared, not re-derived.
/// </summary>
internal static class JetReference
{
    public static readonly string OutDir = Path.Combine(TestMods.RepoRoot, "lab", "reference", "jet_slice", "out");

    private static readonly Lazy<JsonDocument> FormulasDoc = new(() => Load("formulas.json"));
    private static readonly Lazy<JsonDocument> TrajectoryDoc = new(() => Load("trajectory.json"));

    private static readonly Lazy<JsonDocument> TrajectoryCbDoc = new(() => Load("trajectory_cb.json"));

    /// <summary>Same fixture with the culture rule C-B (knowledge, rates and budgets are identical to <see cref="Trajectory"/>).</summary>
    public static JsonElement TrajectoryCb => TrajectoryCbDoc.Value.RootElement;

    public static JsonElement RowCb(long month) => TrajectoryCb.GetProperty("rows")[(int)month];

    public static JsonElement Formulas => FormulasDoc.Value.RootElement;
    public static JsonElement Trajectory => TrajectoryDoc.Value.RootElement;

    public static readonly CivilDate StartDate = new(1945, 9, 1);

    private static JsonDocument Load(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(OutDir, name)));

    public static Fx6 Raw(JsonElement e) => Fx6.FromRaw(e.GetInt64());

    /// <summary>Exact decimal text ("1.5") → Fx6, as the reference's fx().</summary>
    public static Fx6 Dec(string s)
    {
        bool neg = s.StartsWith('-');
        string[] parts = (neg ? s[1..] : s).Split('.');
        string frac = parts.Length > 1 ? parts[1] : "";
        if (frac.Length > 6)
            throw new FormatException(s);
        long raw = long.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture) * Fx6.Scale
                   + long.Parse((frac + "000000")[..6], System.Globalization.CultureInfo.InvariantCulture);
        return Fx6.FromRaw(neg ? -raw : raw);
    }

    public static readonly string[] Disciplines = ["metal", "prop"];

    public static KnowledgeParams Params()
    {
        JsonElement p = Trajectory.GetProperty("params");
        Fx6 P(string name) => Dec(p.GetProperty(name).GetString()!);
        return new KnowledgeParams
        {
            SRef = P("S_ref"), KRef = P("K_ref"), KK = P("k_K"), KDiff = P("k_diff"), MuK = P("mu_K"), KNode = P("k_node"),
            ThetaStart = P("theta_start"), HMax = P("h_max"), YHalf = P("y_half"), ProgCap = P("prog_cap"),
            Eta = P("eta"), VRef = P("V_ref"), ThetaInd = P("theta_ind"), DeltaInd = P("delta_ind"),
        };
    }

    /// <summary>Reference node specs (numbers 1…6), optionally in a permuted storage order.</summary>
    public static KnowledgeNodeSpec[] NodeSpecs(int[]? storageOrder = null)
    {
        var specs = new List<KnowledgeNodeSpec>();
        foreach (JsonProperty node in Trajectory.GetProperty("nodes").EnumerateObject())
        {
            JsonElement v = node.Value;
            NodeType type = Enum.Parse<NodeType>(v.GetProperty("type").GetString()!);
            JsonElement disc = v.GetProperty("disc");
            int d = disc.ValueKind == JsonValueKind.Null ? -1 : Array.IndexOf(Disciplines, disc.GetString());
            int[][] requires = [.. v.GetProperty("requires").EnumerateArray().Select(x => new[] { x.GetInt32() })];
            specs.Add(new KnowledgeNodeSpec(int.Parse(node.Name, System.Globalization.CultureInfo.InvariantCulture), type, 1, d,
                v.GetProperty("hazard").GetBoolean(), Dec(v.GetProperty("c_n").GetString()!), requires));
        }
        specs.Sort((a, b) => a.Number.CompareTo(b.Number));
        return storageOrder is null ? [.. specs] : [.. storageOrder.Select(i => specs[i])];
    }

    public static bool IsStar(int number) =>
        Trajectory.GetProperty("nodes").GetProperty(number.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .TryGetProperty("star", out JsonElement s) && s.GetBoolean();

    public static JsonElement Row(long month) => Trajectory.GetProperty("rows")[(int)month];

    public static int RowCount => Trajectory.GetProperty("rows").GetArrayLength();

    /// <summary>
    /// Inputs of the reference fixture for any actor row (each row replays actor 0's inputs): <c>S_R</c> of
    /// the month, profile prio (spec §4), <c>j</c> of the year in force (A8, A15) drawn by
    /// <see cref="KnowledgeRng.J"/>, and <c>V_d</c> from <c>proc_v</c>, <c>proc_yield</c> of month t−1.
    /// </summary>
    public sealed class Inputs(KnowledgeGraph graph, CounterRng rng, ImmutableArray<int> actorNumbers) : IKnowledgeInputs
    {
        private readonly string _profile = Trajectory.GetProperty("setup").GetProperty("profile").GetString()!;

        public int[] Licences = [];
        public long LicenceMonth = -1;

        public Fx6 ResearchBudget(int actorRow, long month) => Raw(Row(month).GetProperty("S_R"));

        public Fx6 Priority(int actorRow, int nodeRow, long month)
        {
            NodeType type = graph.Type[nodeRow];
            bool two = _profile switch
            {
                "P1" => type == NodeType.ENG || IsStar(graph.Number[nodeRow]),
                "P2" => type == NodeType.PRC,
                _ => false,
            };
            return two ? Fx6.FromInt(2) : Fx6.One;
        }

        public Fx6 J(int actorRow, int nodeRow, long month) =>
            KnowledgeRng.J(rng, month == 0 ? 0 : (int)((month - 1) / 12), actorNumbers[actorRow], graph.Number[nodeRow]);

        public Fx6 DisciplineVolume(int actorRow, int discipline, long month)
        {
            if (month == 0)
                return Fx6.Zero; // fixture: no output in month −1
            JsonElement s = Row(month - 1).GetProperty("state_after");
            var v = new List<Fx6>();
            var y = new List<Fx6>();
            var d = new List<int>();
            foreach (JsonProperty p in s.GetProperty("proc_v").EnumerateObject())
            {
                v.Add(Raw(p.Value));
                y.Add(Raw(s.GetProperty("proc_yield").GetProperty(p.Name)));
                d.Add(graph.Discipline[graph.RowOf(int.Parse(p.Name, System.Globalization.CultureInfo.InvariantCulture))]);
            }
            return KnowledgeMath.DisciplineVolume([.. v], [.. y], [.. d], discipline);
        }

        public ReadOnlySpan<int> LicensedEngines(int actorRow, long month) => month == LicenceMonth ? Licences : [];
    }

    /// <summary>A knowledge-only run of the fixture on the Kernel runner with the daily calendar.</summary>
    public sealed class Run
    {
        public Run(int threads = 1, int chunkSize = 1, int[]? nodeOrder = null, int[]? actorNumbers = null,
            KnowledgeMode mode = KnowledgeMode.Full, Func<KnowledgeGraph, KnowledgeStart, ImmutableArray<long>>? schedule = null,
            bool licKnows = false, CivilDate? startDate = null)
        {
            actorNumbers ??= [0];
            Graph = KnowledgeGraph.Create(NodeSpecs(nodeOrder), Disciplines);
            Start = new KnowledgeStart([.. actorNumbers], Graph.Count, Disciplines.Length);
            JsonElement k0 = Trajectory.GetProperty("params").GetProperty("K_d0");
            for (int a = 0; a < actorNumbers.Length; a++)
            {
                foreach (JsonElement n in Trajectory.GetProperty("known_at_start").EnumerateArray())
                    Start.Known[a * Graph.Count + Graph.RowOf(n.GetInt32())] = true;
                for (int d = 0; d < Disciplines.Length; d++)
                    Start.KD[a * Disciplines.Length + d] = Dec(k0.GetProperty(Disciplines[d]).GetString()!);
            }
            var rng = new CounterRng(Trajectory.GetProperty("seed").GetUInt64());
            InputsImpl = new Inputs(Graph, rng, Start.ActorNumber);
            Table = new Table(actorNumbers.Length);
            Calendar = new TickCalendar(startDate ?? StartDate);
            int chunks = TickRunner.ChunkCountFor(Table.RowCount, chunkSize);
            System = new KnowledgeSystem(Table, Graph, Start, Params(), InputsImpl, rng, chunks, mode,
                schedule?.Invoke(Graph, Start), licKnows);
            Runner = new TickRunner(Table, Trajectory.GetProperty("seed").GetUInt64(), [System], chunkSize, threads, Calendar);
        }

        public KnowledgeGraph Graph { get; }
        public KnowledgeStart Start { get; }
        public Inputs InputsImpl { get; }
        public Table Table { get; }
        public TickCalendar Calendar { get; }
        public KnowledgeSystem System { get; }
        public TickRunner Runner { get; }

        /// <summary>Runs day ticks up to and including the monthly step of <paramref name="month"/>.</summary>
        public void RunThroughMonth(long month)
        {
            long last = Calendar.TickOfMonth(month);
            while (Runner.Tick <= last)
                Runner.RunTick();
        }
    }
}
