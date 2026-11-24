using System.Globalization;
using System.Text;
using Singularity.Core.Data;
using Singularity.Core.Products;
using Singularity.Kernel;
using Singularity.Lab.Models.Slice;
using Singularity.Session;

namespace Singularity.Lab.Smoke;

/// <summary>
/// Print of decision #247 and RISK-0022 (contract r13a §9.1) on the calibration build: per actor-month, <c>G</c> of the fleet in service by
/// <c>fielded_L</c> (<see cref="ActorView.FieldedGen"/>, the one Core function) against <c>G</c> of the r6 rule <c>fielded</c>, rebuilt from the read
/// views of the same snapshot: a project with a stage, ⌊in service⌋ ≥ <c>n_service_min</c> and every process of its <c>prc_set</c> mastered now
/// (<see cref="ProcessView.Mastered"/>). The rebuild is a diagnostic of the print only: no decision and no metric reads it.
/// Checks of §9.1 visible from the views: <c>G_r6 ≤ G_L</c> every month (from <c>fielded ⊆ fielded_L</c>) and, per generation g, the first month
/// with <c>G_L ≥ g</c> equals the first month with <c>G_r6 ≥ g</c> (from «first entry of a project is the same»; the minimum over projects keeps it).
/// Also <c>designs.csv</c>: every design slot that got a stage, with its generation, <c>prc_set</c> and the first months of its stages (K-classes of
/// the C2 pool, «process abandoned»; the former Lab host dump_host, read views only).
/// The run uses the point's parameters as the matrix does, so its final state hash is the run's golden_hash (checked by the analysis).
/// #268 (outside the pre-registration of EXP-0002): with the full views (<c>JetSlice</c>, <see cref="ActorView.Key"/> set) also the monthly leader by
/// the r6 key <c>K_r6 = (G_r6, T, N, P_r6)</c> — <c>G</c> and <c>P</c> over the r6 <c>fielded</c> set, <c>T</c> and <c>N</c> as in <c>KeyOf</c> — next to
/// the leader by <see cref="ActorView.Key"/> (<c>fielded_L</c>), both with the tie rule of the observer (<see cref="SliceObserver.Leader"/>): files
/// <c>fielded_lead.csv</c> and <c>fielded_key.csv</c>. Print only: no decision reads it, the world and its hash are the same.
/// </summary>
internal static class CalibPrint
{
    public static int Run(string manifestPath, int? point, int? seedCount, string outDir, int jobs, TextWriter log)
    {
        Manifest m = Manifest.Load(manifestPath);
        // Spec v0.34 §5.9 D3 (#250): the same print on main (JetSlice) for EXP-0002; both models run the same world from SliceRunConfig.
        if (m.Model is not ("JetSliceCalib" or "JetSlice"))
            throw new UsageException($"calib-print reads JetSliceCalib or JetSlice; the manifest model is {m.Model}.");
        bool calibration = m.Model == "JetSliceCalib";
        GameData data = SliceData.Load(m.BaseDirectory);
        // n_service_min of the r6 rebuild (review M4): SliceWorld builds ProductsParams with the default and no manifest key, calibration
        // arm, channel or sale arm sets NServiceMin (SliceWorld.ApplyCalibration/ApplyChannels/ApplySaleArm write other fields only), so the
        // world's value is the default. A Session key for it would have to come here too; the selftest check «calib-print» guards the
        // consequence (first entry by both rules equal, G_r6 ≤ G_L), which a different value would break.
        int nServiceMin = new ProductsParams().NServiceMin;
        var work = new List<(MatrixPoint P, ulong Seed, SliceRunConfig Config)>();
        foreach (MatrixPoint p in m.Points)
        {
            if (point is int i && p.Index != i)
                continue;
            foreach (ulong s in seedCount is int n ? p.Seeds.Take(n) : p.Seeds)
            {
                SliceParameters sp = SliceParameters.Parse(p.CreateParamSet(), s, 1, m.ChunkSize, calibration);
                // Review M1: the model adds a ForceProxy (FORCE) and loads the control tables (CONTROL_TABLES); this print builds the
                // session from the config alone, so such a point would run another world than the matrix and its hash would not be golden.
                if (sp.ForceDivisor != 0 || sp.ControlTablesPath is not null)
                    throw new UsageException($"calib-print does not support FORCE or CONTROL_TABLES (point {p.Index}): the print would not run the matrix world.");
                // H8 (TASK-0121): LK0 and LC0 need no CONTROL_TABLES, so the horizon of the control layers is checked here as in the model.
                JetSliceModelFactory.CheckControlHorizon(sp.Config.Layer, m.Ticks, data.Scenario(sp.Config.ScenarioId));
                // TASK-0131 (O-038, H9): LK0 needs no CONTROL_TABLES either, so the generation guard of the model runs here too (an early
                // parameter error instead of a late failure inside KnowledgeSchedule.BuildLk0 on G5/G6 data).
                JetSliceModelFactory.CheckControlGenerations(sp.Config.Layer, data);
                work.Add((p, s, sp.Config));
            }
        }
        if (work.Count == 0)
            throw new UsageException($"No point {point} in {manifestPath}.");
        var results = new Result[work.Count];
        Parallel.For(0, work.Count, new ParallelOptions { MaxDegreeOfParallelism = jobs }, k =>
        {
            (MatrixPoint p, ulong seed, SliceRunConfig config) = work[k];
            results[k] = One(data, config, m.Ticks, nServiceMin, p.Index, seed);
        });
        // Review M2: generation columns from the layout of the runs, not a fixed g1..g4.
        int nG = results[0].Generations;
        if (results.Any(r => r.Generations != nG))
            throw new InvalidOperationException("Runs of one manifest have different generation counts.");
        Directory.CreateDirectory(outDir);
        using (var w = new StreamWriter(Path.Combine(outDir, "fielded_actor.csv")))
        {
            IEnumerable<string> gens(string prefix) => Enumerable.Range(1, nG).Select(g => $"{prefix}{g}");
            w.WriteLine(string.Join(",", ["point", "seed", "actor", "months", "months_l_gt_r6", "months_r6_gt_l", .. gens("first_l_g"), .. gens("first_r6_g")]));
            foreach (Result r in results)
            {
                foreach (ActorRow a in r.Actors)
                    w.WriteLine(string.Join(",", [I(r.Point), r.Seed.ToString(CultureInfo.InvariantCulture), a.Actor, I(a.Months), I(a.LGtR6), I(a.R6GtL),
                        .. a.FirstL.Select(O), .. a.FirstR6.Select(O)]));
            }
        }
        using (var w = new StreamWriter(Path.Combine(outDir, "fielded_spells.csv")))
        {
            w.WriteLine("point,seed,actor,start,end,g_l_max,g_r6_min");
            foreach (Result r in results)
            {
                foreach (Spell s in r.Spells)
                    w.WriteLine(string.Join(",", I(r.Point), r.Seed.ToString(CultureInfo.InvariantCulture), s.Actor, I(s.Start), O(s.End), I(s.GL), I(s.GR6)));
            }
        }
        using (var w = new StreamWriter(Path.Combine(outDir, "designs.csv")))
        {
            // t_* = the first month (post view) in that stage, −1 = at the start; a stage that was skipped stays empty.
            w.WriteLine("point,seed,actor,design,gen,prc,t_created,t_proto,t_series,t_inservice,t_withdrawn,cancelled");
            foreach (Result r in results)
            {
                foreach (string d in r.Designs)
                    w.WriteLine($"{I(r.Point)},{r.Seed.ToString(CultureInfo.InvariantCulture)},{d}");
            }
        }
        using (var w = new StreamWriter(Path.Combine(outDir, "fielded_runs.csv")))
        {
            w.WriteLine("point,seed,hash_final");
            foreach (Result r in results)
                w.WriteLine($"{I(r.Point)},{r.Seed.ToString(CultureInfo.InvariantCulture)},{r.Hash}");
        }
        using (var w = new StreamWriter(Path.Combine(outDir, "fielded_lead.csv")))
        {
            // #268: one character per month 0…months−1; lead_* = index among the great powers, field_* = the field of the strict lead or '='.
            // Empty without the full views (JetSliceCalib).
            w.WriteLine(LeadHeader);
            foreach (Result r in results)
                w.WriteLine($"{I(r.Point)},{r.Seed.ToString(CultureInfo.InvariantCulture)},{r.LeadL},{r.LeadR6},{r.FieldL},{r.FieldR6}");
        }
        using (var w = new StreamWriter(Path.Combine(outDir, "fielded_key.csv")))
        {
            // #268: the key of every actor-month by both rules; T and N are one rule (checked equal), P as Fx6 raw. Rows only with the full views.
            w.WriteLine(KeyHeader);
            foreach (Result r in results)
            {
                foreach (string k in r.Keys)
                    w.WriteLine($"{I(r.Point)},{r.Seed.ToString(CultureInfo.InvariantCulture)},{k}");
            }
        }
        log.WriteLine($"calib-print: {results.Length} runs, n_service_min {nServiceMin}");
        return 0;
    }

    internal const string LeadHeader = "point,seed,lead_l,lead_r6,field_l,field_r6";
    internal const string KeyHeader = "point,seed,actor,month,g_l,t,n,p_l,g_r6,p_r6";

    private sealed record ActorRow(string Actor, long Months, long LGtR6, long R6GtL, long?[] FirstL, long?[] FirstR6);

    private sealed record Spell(string Actor, long Start, long? End, int GL, int GR6);

    private sealed record Result(int Point, ulong Seed, string Hash, int Generations, List<ActorRow> Actors, List<Spell> Spells, List<string> Designs,
        string LeadL, string LeadR6, string FieldL, string FieldR6, List<string> Keys);

    private static Result One(GameData data, SliceRunConfig config, long ticks, int nServiceMin, int point, ulong seed)
    {
        SliceSession s = SliceSession.Create(data, config);
        SliceLayoutView l = s.Layout;
        int nA = l.ActorIds.Length, nG = l.Generations;
        var calendar = new TickCalendar(s.Date);
        long months = 0;
        while (calendar.TickOfMonth(months) < ticks)
            months++;
        var firstL = new long?[nA, nG];
        var firstR6 = new long?[nA, nG];
        var gt = new long[nA];
        var lt = new long[nA];
        var open = new (long Start, int GL, int GR6)?[nA];
        var spells = new List<Spell>();
        int nD = l.Designs;
        var stageFirst = new long?[nA, nD, 6];
        var prc = new string?[nA, nD];
        var gen = new int[nA, nD];
        var cancelled = new bool[nA, nD];
        int nGreat = SliceObserver.Great.Length;
        var leadL = new StringBuilder();
        var leadR6 = new StringBuilder();
        var fieldL = new StringBuilder();
        var fieldR6 = new StringBuilder();
        var keyRows = new List<string>();
        int leaderL = 0, leaderR6 = 0;   // the observer's start (SliceObserver.Initialize)
        var kl = new TechKey[nA];
        var kr = new TechKey[nA];
        void Observe(SliceView v, long t)
        {
            for (int a = 0; a < nA; a++)
            {
                foreach (DesignView d in v.Actors[a].Designs)
                {
                    if (d.Stage == DesignStage.None)
                        continue;
                    stageFirst[a, d.Design, (int)d.Stage] ??= t;
                    prc[a, d.Design] ??= string.Join(' ', d.PrcSet);
                    gen[a, d.Design] = d.Gen;
                    cancelled[a, d.Design] = d.Cancelled;
                }
            }
            // With the full views the r6 key of this snapshot is computed once here and its G reused by the generation print below.
            bool haveKeys = t >= 0 && v.Actors.All(x => x.Key is not null);
            if (haveKeys)
            {
                for (int a = 0; a < nA; a++)
                {
                    ActorView av = v.Actors[a];
                    TechKey kL = av.Key!.Value, kR = KeyR6(av, l, nServiceMin);
                    // Checks of the rebuild against the Core key: T and N are one rule. G_r6 ≤ G_L and P_r6 ≤ P_L (from fielded ⊆ fielded_L) no longer
                    // hold since fleet ageing (#435, TASK-0129): Core counts ⌊n_eff⌋, weighed by age, and the view gives the plain InService the rebuild
                    // reads, so an old fleet can be r6-fielded and not fielded_L (TASK-0134 probe, B0 seed 5). Such months are counted
                    // (months_r6_gt_l of fielded_actor.csv), not refused.
                    if (kR.T != kL.T || kR.N != kL.N || kL.G != av.FieldedGen)
                        throw new InvalidOperationException($"seed {seed} month {t} actor {l.ActorIds[a]}: K_L {kL} vs K_r6 {kR}, FieldedGen {av.FieldedGen}.");
                    kl[a] = kL;
                    kr[a] = kR;
                    keyRows.Add(string.Join(",", l.ActorIds[a], I(t), I(kL.G), I(kL.T), I(kL.N), I(kL.P.Raw), I(kR.G), I(kR.P.Raw)));
                }
                leaderL = SliceObserver.Leader(kl, nGreat, leaderL);
                leaderR6 = SliceObserver.Leader(kr, nGreat, leaderR6);
                leadL.Append((char)('0' + leaderL));
                leadR6.Append((char)('0' + leaderR6));
                fieldL.Append(SliceObserver.LeadField(kl, nGreat, leaderL));
                fieldR6.Append(SliceObserver.LeadField(kr, nGreat, leaderR6));
            }
            for (int a = 0; a < nA; a++)
            {
                ActorView av = v.Actors[a];
                int gl = av.FieldedGen, gr6 = haveKeys ? kr[a].G : KeyR6(av, l, nServiceMin).G;
                for (int g = 1; g <= nG; g++)
                {
                    if (gl >= g) firstL[a, g - 1] ??= t;
                    if (gr6 >= g) firstR6[a, g - 1] ??= t;
                }
                if (t < 0)
                    continue;
                if (gr6 > gl) lt[a]++;
                if (gl > gr6)
                {
                    gt[a]++;
                    open[a] = open[a] is { } o ? (o.Start, Math.Max(o.GL, gl), Math.Min(o.GR6, gr6)) : (t, gl, gr6);
                }
                else if (open[a] is { } o)
                {
                    spells.Add(new Spell(l.ActorIds[a], o.Start, t - 1, o.GL, o.GR6));
                    open[a] = null;
                }
            }
        }
        Observe(s.View, -1);
        for (long t = 0; t < months; t++)
        {
            s.StepTo(calendar.TickOfMonth(t));
            s.Step(1);
            Observe(s.View, t);
        }
        for (int a = 0; a < nA; a++)
        {
            if (open[a] is { } o)
                spells.Add(new Spell(l.ActorIds[a], o.Start, null, o.GL, o.GR6));   // still open at the horizon: right-censored
        }
        s.StepTo(ticks);
        var rows = new List<ActorRow>();
        for (int a = 0; a < nA; a++)
        {
            rows.Add(new ActorRow(l.ActorIds[a], months, gt[a], lt[a],
                [.. Enumerable.Range(0, nG).Select(g => firstL[a, g])], [.. Enumerable.Range(0, nG).Select(g => firstR6[a, g])]));
        }
        var designs = new List<string>();
        for (int a = 0; a < nA; a++)
        {
            for (int d = 0; d < nD; d++)
            {
                if (prc[a, d] is not string set)
                    continue;
                long? created = Enumerable.Range(1, 5).Select(k => stageFirst[a, d, k]).Where(x => x is not null).Min();
                designs.Add(string.Join(",", l.ActorIds[a], I(d), I(gen[a, d]), set, O(created), O(stageFirst[a, d, 2]), O(stageFirst[a, d, 3]),
                    O(stageFirst[a, d, 4]), O(stageFirst[a, d, 5]), cancelled[a, d] ? "1" : "0"));
            }
        }
        return new Result(point, seed, s.LastHash, nG, rows, spells, designs, leadL.ToString(), leadR6.ToString(), fieldL.ToString(), fieldR6.ToString(), keyRows);
    }

    /// <summary>
    /// <c>K_r6 = (G, T, N, P)</c> of contract §9.1 r6 on the views of one snapshot (#268): <c>G</c> and <c>P</c> over the designs with a stage,
    /// ⌊in service⌋ ≥ <c>n_service_min</c> and every process of their <c>prc_set</c> mastered now; <c>T</c>, <c>N</c> — the highest tier among the
    /// mastered processes and their count, as in <c>ProductsSystem.KeyOf</c>. The view's Mastered is taken at the month after the snapshot, as the Key.
    /// </summary>
    internal static TechKey KeyR6(ActorView av, SliceLayoutView l, int nServiceMin)
    {
        bool Mastered(int number)
        {
            int r = l.NodeNumbers.IndexOf(number);
            return r >= 0 && av.Nodes[r].Process is ProcessView pv && pv.Mastered;
        }
        int g = 0;
        Fx6 perf = Fx6.Zero;
        foreach (DesignView d in av.Designs)
        {
            if (d.Stage == DesignStage.None || d.InService.Raw / Fx6.Scale < nServiceMin)
                continue;
            bool all = true;
            foreach (int p in d.PrcSet)
                all &= Mastered(p);
            if (!all)
                continue;
            g = Math.Max(g, d.Gen);
            if (d.Perf > perf)
                perf = d.Perf;
        }
        int tier = 0, count = 0;
        for (int k = 0; k < l.ProcessNumbers.Length; k++)
        {
            if (!Mastered(l.ProcessNumbers[k]))
                continue;
            if (l.ProcessTier[k] > tier)
            {
                tier = l.ProcessTier[k];
                count = 1;
            }
            else if (l.ProcessTier[k] == tier)
            {
                count++;
            }
        }
        return new TechKey(g, tier, count, perf);
    }

    private static string I(long v) => v.ToString(CultureInfo.InvariantCulture);

    private static string O(long? v) => v is long x ? I(x) : "";
}
