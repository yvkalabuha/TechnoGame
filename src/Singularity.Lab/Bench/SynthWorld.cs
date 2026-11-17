using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Singularity.Core.Data;
using Singularity.Kernel;
using Singularity.Lab.Models.Slice;

namespace Singularity.Lab.Bench;

/// <summary>Shape of the synthetic knowledge graph (tick-benchmark-m0.md §2).</summary>
public enum SynthGraph
{
    /// <summary>Disjoint copies of the core subgraph: edges only inside a copy.</summary>
    Clones,

    /// <summary>The same copies plus random informs and requires edges between copies (seeded).</summary>
    CrossEdges,
}

/// <summary>
/// One point of the synthetic world: <paramref name="Actors"/> actors and <paramref name="Nodes"/> knowledge nodes. The generator seed is not
/// the simulation seed: it fixes the actor perturbation and the cross edges, so one world can be run with many simulation seeds.
/// </summary>
public sealed record SynthWorldSpec(int Actors, int Nodes, SynthGraph Graph, ulong GeneratorSeed = SynthWorldSpec.DefaultGeneratorSeed)
{
    public const ulong DefaultGeneratorSeed = 0x5_1A7_B0B;

    /// <summary>The ADR-0016 grid (EXP-0007 pre-registration): actors 6/24/96/200 × nodes 180/500/2000 × both graph variants.</summary>
    public static IReadOnlyList<SynthWorldSpec> BenchmarkGrid(ulong generatorSeed = DefaultGeneratorSeed) =>
    [
        .. from g in (SynthGraph[])[SynthGraph.Clones, SynthGraph.CrossEdges]
           from a in (int[])[6, 24, 96, 200]
           from n in (int[])[180, 500, 2000]
           select new SynthWorldSpec(a, n, g, generatorSeed),
    ];

    /// <summary>Directory-safe name, e.g. <c>a200-n2000-cross-s85698315</c>.</summary>
    public string Name => string.Create(CultureInfo.InvariantCulture,
        $"a{Actors}-n{Nodes}-{(Graph == SynthGraph.Clones ? "clones" : "cross")}-s{GeneratorSeed}");
}

/// <summary>What the generator built: the loaded data (mod core + the overlay), the scenario to run and the overlay's identity.</summary>
/// <param name="OverlaySha256">SHA-256 over the overlay files (name and bytes, in load order): the same spec gives the same value.</param>
public sealed record SynthWorld(SynthWorldSpec Spec, GameData Data, string ScenarioId, string OverlayDirectory, string OverlaySha256,
    SynthWorldStats Stats);

/// <summary>Counts of the generated world, for the EXP record and the unit costs.</summary>
public sealed record SynthWorldStats(int Actors, int Nodes, int Copies, int RequiresMembers, int InformsEdges, int CrossRequires, int CrossInforms,
    int KnownAtStart, int MasteredAtStart);

/// <summary>
/// Synthetic full-scale world for the tick benchmark (ADR-0016, tick-benchmark-m0.md §2, TASK-0065): a Lab-only mod overlay (ADR-0010) on top of
/// <c>mods/core</c>. No Core or Session change: the overlay is plain mod data, loaded by <see cref="ModLoader"/> and validated like any mod.
/// <list type="bullet">
/// <item>Nodes: copy 0 is the core graph itself; copies 1… are renamed clones (<c>id_kC</c>, number <c>C·100 + n</c>); the last copy may be
/// partial — a prefix of a topological order, so it is closed under requires.</item>
/// <item>Actors: the core actors stay first and unchanged (USA, GBR, SUN keep their names: Session looks them up); the clones cycle through
/// them with ids XAA, XAB, … and a seeded ±10% perturbation of GDP, GDP per capita and budget index. Every actor knows and masters the clone of
/// each node its source knows and masters, in every copy.</item>
/// <item>Cross edges (variant <see cref="SynthGraph.CrossEdges"/>): every clone FND informs 2 random ENG/PRC nodes of its discipline in other
/// copies (weight 0.3); every clone node of G ≥ 1 requires, with probability 1/4, one random node of a strictly earlier generation in another
/// copy that every source actor knowing the child also knows (so the graph stays acyclic and the start knowledge stays closed).</item>
/// </list>
/// The output is a function of the spec and of the core data only (no clock, no hash-set order, Kernel <see cref="CounterRng"/>).
/// </summary>
public static class SynthWorldGenerator
{
    public const string ModId = "synth_bench";
    public const string ScenarioId = "synth_bench";
    public const int NumberStride = 100;
    public const int CrossInformsPerFnd = 2;
    public static readonly Fx6 CrossInformsWeight = Fx6.FromRaw(300_000);
    public static readonly Fx6 CrossRequiresChance = Fx6.FromRaw(250_000);
    public const long PerturbationRaw = 100_000;   // ±10% in millionths

    private const int RngSystemPerturb = 1, RngSystemInforms = 2, RngSystemRequires = 3;

    /// <summary><c>mods/core</c> of the repository above <paramref name="start"/> or above the Lab binary.</summary>
    public static string FindCoreMod(string? start = null) =>
        (start is null ? null : SliceData.FindMods(start)) ?? SliceData.FindMods(Directory.GetCurrentDirectory()) ?? SliceData.FindMods(AppContext.BaseDirectory)
        ?? throw new InvalidOperationException("mods/core not found above the working directory or the Lab binary.");

    /// <summary>
    /// Writes the overlay mod into <paramref name="outDirectory"/> (created if missing; its own three files are replaced) and loads core + overlay.
    /// This is the entry point of <c>lab bench</c> (TASK-0064): run the session with <c>ScenarioId = </c><see cref="ScenarioId"/>.
    /// </summary>
    public static SynthWorld Build(SynthWorldSpec spec, string outDirectory, string? coreModDirectory = null) =>
        Build(spec, outDirectory, coreModDirectory, overrideCoreScenario: false);

    /// <param name="overrideCoreScenario">Self-check only: write the scenario as an <c>override</c> of the core scenario (same id, so the same
    /// scenario hash and <c>run.scenario</c> column), to prove that the emitted data reproduce the core run.</param>
    internal static SynthWorld Build(SynthWorldSpec spec, string outDirectory, string? coreModDirectory, bool overrideCoreScenario)
    {
        string core = coreModDirectory ?? FindCoreMod();
        GameData baseData = ModLoader.Load(core);
        ScenarioData sc = baseData.Scenario(Singularity.Session.SliceRunConfig.DefaultScenario);
        Validate(spec, baseData.Nodes, sc);

        var plan = new NodePlan(baseData.Nodes, spec);
        CrossEdges cross = spec.Graph == SynthGraph.CrossEdges ? CrossEdges.Draw(plan, sc, spec.GeneratorSeed) : CrossEdges.None(plan);

        Directory.CreateDirectory(outDirectory);
        var files = new List<(string Name, string Text)>
        {
            ("mod.yaml", ModYaml()),
            ("nodes.yaml", NodesYaml(plan, cross)),
            ("scenario.yaml", ScenarioYaml(baseData, sc, plan, spec, overrideCoreScenario)),
        };
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach ((string name, string text) in files)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            File.WriteAllBytes(Path.Combine(outDirectory, name), bytes);
            sha.AppendData(Encoding.UTF8.GetBytes(name + "\n"));
            sha.AppendData(bytes);
        }
        string overlaySha = Convert.ToHexStringLower(sha.GetHashAndReset());

        GameData data = ModLoader.Load(core, outDirectory);
        string scenarioId = overrideCoreScenario ? sc.Id : ScenarioId;
        ScenarioData synth = data.Scenario(scenarioId);
        var stats = new SynthWorldStats(synth.ActorCount, data.Nodes.Count, plan.Copies, data.Nodes.Requires.Members.Length,
            data.Nodes.InformsTo.Length, cross.RequiresCount, cross.InformsCount, synth.Known.Length, synth.Mastered.Length);
        if (stats.Actors != spec.Actors || stats.Nodes != spec.Nodes)
            throw new InvalidOperationException($"The overlay loaded {stats.Actors} actors and {stats.Nodes} nodes, the spec asks {spec.Actors} and {spec.Nodes}.");
        return new SynthWorld(spec, data, scenarioId, Path.GetFullPath(outDirectory), overlaySha, stats);
    }

    private static void Validate(SynthWorldSpec spec, NodeTable nodes, ScenarioData sc)
    {
        if (spec.Actors < sc.ActorCount || spec.Actors > sc.ActorCount + 26 * 26)
            throw new ArgumentOutOfRangeException(nameof(spec), spec.Actors, $"Actors must be in {sc.ActorCount}…{sc.ActorCount + 676} (the core actors stay).");
        int maxNumber = nodes.Number.Max();
        if (maxNumber >= NumberStride)
            throw new InvalidOperationException($"Core node numbers reach {maxNumber}; the clone numbering needs them below {NumberStride}.");
        int maxCopies = (Core.Knowledge.KnowledgeRng.MaxNodeNumber - maxNumber) / NumberStride + 1;
        if (spec.Nodes < nodes.Count || spec.Nodes > nodes.Count * maxCopies)
            throw new ArgumentOutOfRangeException(nameof(spec), spec.Nodes, $"Nodes must be in {nodes.Count}…{nodes.Count * maxCopies}.");
    }

    /// <summary>Which core rows each copy holds. Copy 0 is the core itself (not written); copies 1… are clones, the last one maybe partial.</summary>
    internal sealed class NodePlan
    {
        public readonly NodeTable Core;
        public readonly int Copies;
        /// <summary>Core rows of copy c: all rows for a full copy, a prefix of <see cref="Topo"/> for the partial last one.</summary>
        public readonly int[][] Rows;
        public readonly int[] Topo;

        public NodePlan(NodeTable core, SynthWorldSpec spec)
        {
            Core = core;
            Topo = TopologicalOrder(core);
            int full = spec.Nodes / core.Count, rest = spec.Nodes % core.Count;
            Copies = full + (rest > 0 ? 1 : 0);
            Rows = new int[Copies][];
            for (int c = 0; c < Copies; c++)
            {
                int take = c < full ? core.Count : rest;
                int[] rows = Topo[..take];
                Array.Sort(rows);
                Rows[c] = rows;
            }
        }

        public bool Has(int copy, int row) => Array.BinarySearch(Rows[copy], row) >= 0;

        public string Id(int copy, int row) => copy == 0 ? Core.Id[row] : string.Create(CultureInfo.InvariantCulture, $"{Core.Id[row]}_k{copy}");

        public int Number(int copy, int row) => copy * NumberStride + Core.Number[row];

        // Kahn's order; among ready nodes the smallest node number first. A prefix of it is closed under requires.
        private static int[] TopologicalOrder(NodeTable n)
        {
            int count = n.Count;
            var indegree = new int[count];
            var children = new List<int>[count];
            for (int r = 0; r < count; r++)
                children[r] = [];
            for (int r = 0; r < count; r++)
            {
                var parents = new SortedSet<int>();
                for (int g = n.Requires.GroupStart[r]; g < n.Requires.GroupStart[r + 1]; g++)
                {
                    foreach (int p in n.Requires.GroupMembers(g))
                        parents.Add(p);
                }
                foreach (int p in parents)
                {
                    children[p].Add(r);
                    indegree[r]++;
                }
            }
            var ready = new SortedSet<(int Number, int Row)>();
            for (int r = 0; r < count; r++)
            {
                if (indegree[r] == 0)
                    ready.Add((n.Number[r], r));
            }
            var order = new List<int>(count);
            while (ready.Count > 0)
            {
                (int _, int row) = ready.Min;
                ready.Remove(ready.Min);
                order.Add(row);
                foreach (int ch in children[row])
                {
                    if (--indegree[ch] == 0)
                        ready.Add((n.Number[ch], ch));
                }
            }
            if (order.Count != count)
                throw new InvalidOperationException("The core requires graph has a cycle.");
            return [.. order];
        }
    }

    /// <summary>Extra edges between copies, keyed by the clone node (copy ≥ 1, core row).</summary>
    internal sealed class CrossEdges
    {
        public readonly List<(int Copy, int Row)>[][] Informs;
        public readonly List<(int Copy, int Row)>[][] Requires;
        public int InformsCount, RequiresCount;

        private CrossEdges(NodePlan plan)
        {
            Informs = new List<(int, int)>[plan.Copies][];
            Requires = new List<(int, int)>[plan.Copies][];
            for (int c = 0; c < plan.Copies; c++)
            {
                Informs[c] = new List<(int, int)>[plan.Core.Count];
                Requires[c] = new List<(int, int)>[plan.Core.Count];
                for (int r = 0; r < plan.Core.Count; r++)
                {
                    Informs[c][r] = [];
                    Requires[c][r] = [];
                }
            }
        }

        public static CrossEdges None(NodePlan plan) => new(plan);

        public static CrossEdges Draw(NodePlan plan, ScenarioData sc, ulong seed)
        {
            var e = new CrossEdges(plan);
            if (plan.Copies < 2)
                return e;
            NodeTable n = plan.Core;
            var rng = new CounterRng(seed);
            // All (copy, row) nodes in a fixed order: the candidates of each draw are a filter of this list.
            var all = new List<(int Copy, int Row)>();
            for (int c = 0; c < plan.Copies; c++)
            {
                foreach (int r in plan.Rows[c])
                    all.Add((c, r));
            }
            bool[,] known = KnownMatrix(sc, n);
            for (int c = 1; c < plan.Copies; c++)
            {
                foreach (int r in plan.Rows[c])
                {
                    int entity = c * NumberStride + n.Number[r];
                    if (n.Type[r] == NodeType.FND)
                    {
                        var targets = all.Where(t => t.Copy != c && n.Type[t.Row] is NodeType.ENG or NodeType.PRC
                                                     && n.Discipline[t.Row] == n.Discipline[r]).ToList();
                        for (int k = 0; k < CrossInformsPerFnd && targets.Count > 0; k++)
                        {
                            int i = CounterRng.Index(rng.NextU64(0, RngSystemInforms, entity, k), targets.Count);
                            e.Informs[c][r].Add(targets[i]);
                            targets.RemoveAt(i);
                            e.InformsCount++;
                        }
                    }
                    if (n.Gen[r] >= 1 && CounterRng.Chance(rng.NextU64(0, RngSystemRequires, entity, 0), CrossRequiresChance))
                    {
                        var parents = all.Where(t => t.Copy != c && n.Gen[t.Row] < n.Gen[r] && KnownImplies(known, r, t.Row)).ToList();
                        if (parents.Count > 0)
                        {
                            e.Requires[c][r].Add(parents[CounterRng.Index(rng.NextU64(0, RngSystemRequires, entity, 1), parents.Count)]);
                            e.RequiresCount++;
                        }
                    }
                }
            }
            return e;
        }

        private static bool[,] KnownMatrix(ScenarioData sc, NodeTable n)
        {
            var k = new bool[sc.ActorCount, n.Count];
            for (int a = 0; a < sc.ActorCount; a++)
            {
                foreach (int r in sc.KnownOf(a))
                    k[a, r] = true;
            }
            return k;
        }

        // A source actor that knows the child at the start must know the parent too (start knowledge closed under requires).
        private static bool KnownImplies(bool[,] known, int child, int parent)
        {
            for (int a = 0; a < known.GetLength(0); a++)
            {
                if (known[a, child] && !known[a, parent])
                    return false;
            }
            return true;
        }
    }

    private static string ModYaml() =>
        """
        # Synthetic benchmark overlay (TASK-0065, ADR-0016). Generated by Singularity.Lab SynthWorldGenerator; never edit by hand.
        id: synth_bench
        version: "0.1.0"
        name: "Synthetic full-scale world for the tick benchmark (Lab only)"
        dependencies:
          - {id: core, min: "0.1.0"}
        files: [nodes.yaml, scenario.yaml]

        """;

    private static string NodesYaml(NodePlan plan, CrossEdges cross)
    {
        NodeTable n = plan.Core;
        var w = new StringBuilder();
        w.Append("# Generated clone nodes (TASK-0065). Copy 0 is mods/core itself.\nkind: node\n");
        if (plan.Copies < 2)
            return w.Append("define: []\n").ToString();
        w.Append("define:\n");
        for (int c = 1; c < plan.Copies; c++)
        {
            foreach (int r in plan.Rows[c])
            {
                w.Append("  - id: ").Append(Q(plan.Id(c, r))).Append('\n');
                Line(w, "number", I(plan.Number(c, r)));
                Line(w, "type", Q(DataNames.NameOf(DataNames.NodeTypes, n.Type[r])));
                Line(w, "gen", I(n.Gen[r]));
                Line(w, "discipline", Q(DataNames.NameOf(DataNames.Disciplines, n.Discipline[r])));
                Line(w, "cluster", Q(DataNames.NameOf(DataNames.Clusters, n.Cluster[r])));
                if (n.Type[r] == NodeType.PRC)
                {
                    Line(w, "tier", I(n.Tier[r]));
                    var req = new List<string>();
                    foreach ((string name, Dimension dim) in DataNames.Dimensions)
                    {
                        if (n.Requirement(r, dim) != Fx6.Zero)
                            req.Add($"{name}: {n.Requirement(r, dim)}");
                    }
                    Line(w, "capability_req", "{" + string.Join(", ", req) + "}");
                    Line(w, "labour_p", n.LabourP[r].ToString());
                }
                var requires = new List<string>();
                for (int g = n.Requires.GroupStart[r]; g < n.Requires.GroupStart[r + 1]; g++)
                {
                    ReadOnlySpan<int> members = n.Requires.GroupMembers(g);
                    var ids = new List<string>();
                    foreach (int m in members)
                        ids.Add(Q(plan.Id(c, m)));
                    requires.Add(members.Length == 1 ? ids[0] : "{any_of: [" + string.Join(", ", ids) + "]}");
                }
                foreach ((int pc, int pr) in cross.Requires[c][r])
                    requires.Add(Q(plan.Id(pc, pr)));
                Line(w, "requires", "[" + string.Join(", ", requires) + "]");
                Line(w, "hazard", Q(DataNames.NameOf(DataNames.Hazards, n.Hazard[r])));
                var informs = new List<string>();
                for (int e = n.InformsStart[r]; e < n.InformsStart[r + 1]; e++)
                {
                    // An edge to a node the partial copy does not hold is dropped.
                    if (plan.Has(c, n.InformsTo[e]))
                        informs.Add($"{{to: {Q(plan.Id(c, n.InformsTo[e]))}, weight: {n.InformsWeight[e]}}}");
                }
                foreach ((int tc, int tr) in cross.Informs[c][r])
                    informs.Add($"{{to: {Q(plan.Id(tc, tr))}, weight: {CrossInformsWeight}}}");
                if (informs.Count > 0)
                    Line(w, "informs", "[" + string.Join(", ", informs) + "]");
                string before = n.AnchorBefore[r] ? "before: true, " : "";
                Line(w, "anchor", $"{{year: {I(n.AnchorYear[r])}, {before}event: {Q(n.AnchorEvent[r])}, source: {Q(n.AnchorSource[r])}, " +
                                  $"estimated: {B(n.AnchorEstimated[r])}}}");
            }
        }
        return w.ToString();
    }

    private static string ScenarioYaml(GameData data, ScenarioData sc, NodePlan plan, SynthWorldSpec spec, bool overrideCore)
    {
        var w = new StringBuilder();
        w.Append("# Generated synthetic scenario (TASK-0065): the core actors, then perturbed clones.\nkind: scenario\n");
        w.Append(overrideCore ? "override:\n" : "define:\n");
        w.Append("  - id: ").Append(Q(overrideCore ? sc.Id : ScenarioId)).Append('\n');
        Line(w, "start", $"{{year: {I(sc.StartYear)}, month: {I(sc.StartMonth)}}}");
        Line(w, "clusters", "[" + string.Join(", ", sc.Clusters.Select(c => Q(DataNames.NameOf(DataNames.Clusters, c)))) + "]");
        Line(w, "disciplines", "[" + string.Join(", ", sc.Disciplines.Select(d => Q(DataNames.NameOf(DataNames.Disciplines, d)))) + "]");
        Line(w, "tech_lead", Q(sc.ActorId[sc.TechLead]));
        Line(w, "mastered_history", $"{{since_month: {I(sc.MasteredSinceMonth)}, proc_x_half_lives: {I(sc.ProcXHalfLives)}}}");
        Line(w, "alloy_ref_kg", sc.AlloyRefKg.ToString());
        Line(w, "max_designs", I(sc.MaxDesigns));
        Line(w, "provenance", "");
        for (int p = 0; p < sc.ProvField.Length; p++)
        {
            IEnumerable<string> sources = Enumerable.Range(sc.ProvSourcesStart[p], sc.ProvSourcesStart[p + 1] - sc.ProvSourcesStart[p])
                .Select(i => Q(sc.ProvSources[i]));
            w.Append($"      - {{field: {Q(sc.ProvField[p])}, rule: {Q(sc.ProvRule[p])}, sources: [{string.Join(", ", sources)}], " +
                     $"estimated: {B(sc.ProvEstimated[p])}}}\n");
        }
        Line(w, "actors", "");
        var rng = new CounterRng(spec.GeneratorSeed);
        int names = 0;
        for (int i = 0; i < spec.Actors; i++)
        {
            int src = i < sc.ActorCount ? i : (i - sc.ActorCount) % sc.ActorCount;
            string id;
            long factor = Fx6.Scale;
            if (i < sc.ActorCount)
            {
                id = sc.ActorId[i];
            }
            else
            {
                do
                {
                    id = string.Create(CultureInfo.InvariantCulture, $"X{(char)('A' + names / 26)}{(char)('A' + names % 26)}");
                    names++;
                }
                while (sc.ActorIndex(id) >= 0);
                factor += CounterRng.UniformInt(rng.NextU64(0, RngSystemPerturb, i, 0), -PerturbationRaw, PerturbationRaw);
            }
            Actor(w, data, sc, plan, src, id, factor);
        }
        return w.ToString();
    }

    private static void Actor(StringBuilder w, GameData data, ScenarioData sc, NodePlan plan, int a, string id, long factor)
    {
        const string P = "        ";
        Fx6 Scale(Fx6 v) => Fx6.FromRaw(checked(v.Raw * factor) / Fx6.Scale);
        w.Append("      - id: ").Append(Q(id)).Append('\n');
        w.Append(P).Append("class: ").Append(Q(DataNames.NameOf(DataNames.ActorClasses, sc.ActorClass[a]))).Append('\n');
        w.Append(P).Append("exposure: ").Append(sc.Exposure[a]).Append('\n');
        w.Append(P).Append("gdp_1945: ").Append(Scale(sc.Gdp[a])).Append('\n');
        w.Append(P).Append("gdp_year: ").Append(I(sc.GdpYear[a])).Append('\n');
        w.Append(P).Append("gdppc_1945: ").Append(Scale(sc.GdpPerCapita[a])).Append('\n');
        w.Append(P).Append("budget_index: ").Append(Scale(sc.BudgetIndex[a])).Append('\n');
        w.Append(P).Append("growth_pa: ").Append(sc.GrowthPa[a]).Append('\n');
        w.Append(P).Append("culture_h: ").Append(ByDims(sc, sc.CultureOf, a)).Append('\n');
        w.Append(P).Append("tooling_m: ").Append(ByDims(sc, sc.ToolingOf, a)).Append('\n');
        w.Append(P).Append("hcap: ").Append(sc.Hcap[a]).Append('\n');
        w.Append(P).Append("w: ").Append(sc.W[a]).Append('\n');
        w.Append(P).Append("known: [").Append(string.Join(", ", Cloned(plan, sc.KnownOf(a).ToArray()))).Append("]\n");
        w.Append(P).Append("k_d: {").Append(string.Join(", ", sc.Disciplines.Select(d =>
            $"{DataNames.NameOf(DataNames.Disciplines, d)}: {sc.KDOf(a, d)}"))).Append("}\n");
        w.Append(P).Append("k_ind: ").Append(sc.KInd[a]).Append('\n');
        w.Append(P).Append("mastered: [").Append(string.Join(", ", Cloned(plan, sc.MasteredOf(a).ToArray()))).Append("]\n");
        var fleet = new List<string>();
        for (int f = sc.FleetStart[a]; f < sc.FleetStart[a + 1]; f++)
        {
            fleet.Add($"{{product_class: {Q(data.ProductClasses.ClassId[sc.FleetClass[f]])}, gen: {I(sc.FleetGen[f])}, " +
                      $"variant: {Q(DataNames.NameOf(DataNames.Variants, sc.FleetVariant[f]))}, count: {I(sc.FleetCount[f])}, " +
                      $"cohort_year: {I(sc.FleetCohortYear[f])}}}");
        }
        w.Append(P).Append("fleet: [").Append(string.Join(", ", fleet)).Append("]\n");
        w.Append(P).Append("trophy_engineers: ").Append(I(sc.TrophyEngineers[a])).Append('\n');
        w.Append(P).Append("estimated: [").Append(string.Join(", ",
            Enumerable.Range(sc.EstimatedStart[a], sc.EstimatedStart[a + 1] - sc.EstimatedStart[a]).Select(i => Q(sc.Estimated[i])))).Append("]\n");
    }

    // The node set of a core actor, repeated in every copy that holds the node.
    private static IEnumerable<string> Cloned(NodePlan plan, int[] rows)
    {
        for (int c = 0; c < plan.Copies; c++)
        {
            foreach (int r in rows)
            {
                if (plan.Has(c, r))
                    yield return Q(plan.Id(c, r));
            }
        }
    }

    private static string ByDims(ScenarioData sc, Func<int, Cluster, Dimension, Fx6> value, int a) =>
        "{" + string.Join(", ", sc.Clusters.Select(c =>
            $"{DataNames.NameOf(DataNames.Clusters, c)}: {{" +
            string.Join(", ", DataNames.Dimensions.Select(d => $"{d.Name}: {value(a, c, d.Value)}")) + "}")) + "}";

    private static void Line(StringBuilder w, string key, string value) =>
        w.Append("    ").Append(key).Append(':').Append(value.Length > 0 ? " " + value : "").Append('\n');

    // JSON string literals are valid YAML double-quoted scalars (non-ASCII as \u escapes).
    private static string Q(string s) => JsonSerializer.Serialize(s);

    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

    private static string B(bool v) => v ? "true" : "false";
}
