using System.Globalization;
using Singularity.Core.Data;
using Singularity.Core.Knowledge;
using Singularity.Kernel;
using Singularity.Lab.Models.Slice;
using Singularity.Session;

namespace Singularity.Lab.Smoke;

/// <summary>
/// The draws of the L_K schedule (spec v0.34 §6.1, #249/#251) for the check of the control: per seed of a <c>LAYER = LK</c> point and per
/// (actor, g) whose APP is unknown at the start — the «never» draw, the generation clock, the own due of APP <c>T + δ</c> and the due after
/// <c>requires</c>. The clock and «never» come from the same public Core calls as <see cref="KnowledgeSchedule.BuildLk"/>
/// (<see cref="KnowledgeRng.ScheduleNever"/>, <see cref="KnowledgeRng.ScheduleClock"/>, <see cref="CounterRng"/>), the due after requires is
/// <see cref="KnowledgeSchedule.BuildLk"/> itself, on the stream KNOW of the run (<see cref="SliceStreams.Create"/>). Session is not changed;
/// the world is not built. Output <c>lk_draws.csv</c>: <c>seed,actor,gen,app,never,clock,own,req</c> (empty = «never»).
/// </summary>
internal static class LkDraws
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public const string File = "lk_draws.csv";
    public const string Header = "seed,actor,gen,app,never,clock,own,req";

    public static int Run(string manifestPath, int? point, int? seedCount, string outDir, TextWriter log)
    {
        Manifest m = Manifest.Load(manifestPath);
        GameData data = SliceData.Load(m.BaseDirectory);
        var rows = new List<string>();
        int points = 0;
        foreach (MatrixPoint p in m.Points)
        {
            if (point is int i && p.Index != i)
                continue;
            points++;
            foreach (ulong seed in seedCount is int n ? p.Seeds.Take(n) : p.Seeds)
            {
                SliceParameters sp = SliceParameters.Parse(p.CreateParamSet(), seed, 1, m.ChunkSize, calibration: m.Model == "JetSliceCalib");
                if (sp.Config.Layer != LayerControl.Lk || sp.ControlTablesPath is null)
                    throw new UsageException($"lk-draws needs a point with LAYER = LK and CONTROL_TABLES (point {p.Index}).");
                if (sp.Config.Start != StartVariant.Scenario)
                    throw new UsageException("lk-draws reads the scenario start only (START is not the default).");
                // TASK-0131 (O-038, H9): L_K knows generations 1…4 only; lk-draws builds no model, so the guard of the factory runs here
                // (an early parameter error instead of a late failure inside ControlTables.Load or KnowledgeSchedule.BuildLk on G5/G6 data).
                JetSliceModelFactory.CheckControlGenerations(sp.Config.Layer, data);
                ScenarioData sc = data.Scenario(sp.Config.ScenarioId);
                ControlTables tables = ControlTables.Load(Path.GetFullPath(Path.Combine(m.BaseDirectory, sp.ControlTablesPath)), sc, data.Nodes,
                    JetSliceModelFactory.SliceHorizonMonths);
                rows.AddRange(Seed(data, sc, sp.Config, tables.Lk, seed));
            }
        }
        if (points == 0)
            throw new UsageException($"No point {point} in {manifestPath}.");
        Directory.CreateDirectory(outDir);
        using var w = new StreamWriter(Path.Combine(outDir, File));
        w.WriteLine(Header);
        foreach (string r in rows)
            w.WriteLine(r);
        log.WriteLine($"lk-draws: {rows.Count} rows");
        return 0;
    }

    private static IEnumerable<string> Seed(GameData data, ScenarioData sc, SliceRunConfig config, LkTables t, ulong seed)
    {
        // Structure only (numbers, generations, requires): the knowledge parameters do not change it.
        KnowledgeGraph graph = KnowledgeGraph.FromData(data.Nodes, sc.Disciplines, new KnowledgeParams());
        KnowledgeStart start = KnowledgeStart.FromScenario(sc, graph);
        CounterRng know = SliceStreams.Create(config).Know;
        var due = KnowledgeSchedule.BuildLk(graph, start, t, know);
        int nNodes = graph.Count;
        int[] app = [.. ControlTables.AppNodes(data.Nodes)];
        for (int a = 0; a < start.ActorCount; a++)
        {
            int actor = start.ActorNumber[a];
            foreach (int number in app)
            {
                int r = graph.RowOf(number);
                if (r < 0 || start.IsKnown(a, r))
                    continue;
                int g = graph.Gen[r];
                int k = actor * 4 + g - 1;
                bool never = CounterRng.Chance(KnowledgeRng.ScheduleNever(know, actor, g), t.PCens[k]);
                long? clock = never ? null
                    : CounterRng.UniformInt(KnowledgeRng.ScheduleClock(know, actor, g), t.Mean[k] - t.HalfWidth[k], t.Mean[k] + t.HalfWidth[k]);
                int delta = t.Delta[t.DeltaIndex(actor, number)];
                long? own = clock is long c && delta != LkTables.Never ? c + delta : null;
                long d = due[a * nNodes + r];
                long? req = d == KnowledgeSchedule.Never ? null : d;
                yield return string.Join(',', seed.ToString(Inv), sc.ActorId[actor], I(g), I(number), never ? "1" : "0", O(clock), O(own), O(req));
            }
        }
    }

    private static string I(long v) => v.ToString(Inv);

    private static string O(long? v) => v is long x ? I(x) : "";
}
