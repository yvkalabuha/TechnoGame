using Singularity.Core.Data;
using Singularity.Core.Knowledge;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;
using Singularity.Session;
using System.Text.Json;

namespace Singularity.Lab.Models.Slice;

/// <summary>
/// Checks of the slice models (TASK-0044, T10), run by <c>Singularity.Lab selftest</c> until tests/ has a Lab test project: the calibration header,
/// L1 (two runs → the same CSV and hash), L2 (threads 1 and 4 → the same CSV and hash), the Session golden through the Lab path, the parameter errors,
/// and a small matrix in which every run writes every table. Each returns null or the problem.
/// </summary>
public static class SliceChecks
{
    // Seed 1, months 0…149, default config: the golden of the Session (SeriesSwitchTests.MainSeed1Month149, F17b YProdGolden[0]).
    // Re-recorded in TASK-0110 (DEBT-0054), reason: schema v10 (new columns prd.altReached.*, novelty memory per actor); the old columns are unchanged per the projection tests in this branch (T6dOff, FleetAblation, ToolingUnmet), so only the new columns change the hash.
    // Re-recorded in TASK-0115: new columns cmd.*.gen / pol.*.gen and SchemaVersion 11; old columns unchanged per the projection tests.
    // Re-recorded in TASK-0121 (#423): schema v12, new columns prd.d*.cohort.1978…2030 and prd.d*.q.1.
    private const string Golden150 = "a0b7ab02e5f089a2c7e344132eae6d298b8f2e1f3d3ab117cf7261841460869e";
    private const long Ticks150 = 4564;   // 1945-09-01 → 1958-03-01: the tick of month 150, so month 149 is the last one run
    private const long Ticks60 = 1826;    // 1945-09-01 → 1950-09-01

    private static string Manifest(string model, long ticks, string threads = "1", string seeds = "[3]", string pars = "{}", string series = "") =>
        $$"""
        {"schema": 1, "experiment": "SELFTEST-SLICE", "model": "{{model}}", "ticks": {{ticks}}, "chunk_size": 1, "threads": {{threads}},
         "seeds": {{seeds}}, "params": {{pars}}{{series}}}
        """;

    public static IEnumerable<(string Name, Func<string?> Body)> All(string tmp) =>
    [
        ("slice: JetSliceCalib tables have no forbidden file or column (spec §8.2 п. 2)", CalibrationHeader),
        ("slice: JetSliceCalib run writes only its 8 tables; forbidden parameters rejected", () => CalibrationRun(tmp)),
        ("slice: parameters — unknown key, bad value, LK/LC/LP without CONTROL_TABLES, tables without those layers rejected", Parameters),
        ("slice: Lab path gives the Session golden (seed 1, month 149)", () => Golden(tmp)),
        ("slice: L1 — two runs, same CSV and golden_hash", () => L1(tmp)),
        ("slice: L2 — threads 1 vs 4, full horizon, same CSV and golden_hash", () => L2(tmp)),
        ("slice: matrix 2 points x 2 seeds x threads 1,2 — every run has every table", () => Matrix(tmp)),
        ("slice: data_hash and data_files cover the mod files the slice loads", () => DataProvenance(tmp)),
        ("slice: data cache — a changed mod file is loaded again; stale data_files fail the run", () => DataCache(tmp)),
        ("slice: a40.force — the FORCE arm marks the forced series, the AI never", () => A40Force(tmp)),
        ("slice: COEXIST_MONEY needs left = 0 and no training money in every month, not Bind = Money alone", CoexistMoneyClass),
        ("slice: LIC_END — a lapse of LicenceView.Active gives a t_end and an event", () => LicenceEnd(tmp)),
        ("slice: eng_short_months counts months at one bureau, not at any bureau", EngShortOneBureau),
        ("slice: licence track — replace, lapse, resume without a purchase (DEBT-0032)", LicenceTrack),
        ("slice: control tables — CONTROL_TABLES file → LkTables, hash in model_info, bad files rejected (TASK-0054)", () => ControlTablesFile(tmp)),
        ("slice: stage manifests stay valid after freezing; matrix --point/--seeds select one point (TASK-0055)", () => StageManifests(tmp)),
        ("slice: C3 distance — a tiny positive d_C3 is reported positive at full precision (TASK-0055 Codex)", C3DistanceExact),
        ("slice: design capacity — AI design commands, commands without a design and slots used are counted (TASK-0054)", () => DesignCapacity(tmp)),
        ("slice: calibration points — one point of each stage C1, C3+C4, C2 and the L_C/L_P layers run; K keys change the run (TASK-0055)", () => CalibrationPoints(tmp)),
        ("slice: switches — FORCE, START PERT/SYM, CHANNELS, GRID, FLEET_ARM, SALE_ARM, change the run (DEBT-0032, HYP-0004)", () => Switches(tmp)),
        ("slice: calib-print — 1 seed, 60 months, JetSliceCalib and JetSlice: hash = golden_hash, G_r6 ≤ G_L, first entry equal; FORCE/CONTROL_TABLES rejected (review M1, M2, M5)", () => CalibPrintSmoke(tmp)),
        ("slice: calib-print r6 key — synthetic views: G and P over the r6 fielded set, T and N over mastered processes; tie rule and lead field (#268)", CalibPrintR6Key),
        ("slice: calib-print r6 leader — JetSlice 150 months: lead_l and field_l = summary lead and lead_field, lead_r6 = leader of the r6 keys, hash = golden (#268)", () => CalibPrintR6Leader(tmp)),
        ("slice: lk-draws — t_known of an L_K run = max(req, 0) of the exported draws, req ≥ own (spec v0.34 §6.1 self-check, #251)", () => LkDrawsExact(tmp)),
        ("slice: c5-stand — the output months of the C5 site equal the C1 DevSite FULL_T0 (same culture rule C-B + I-OC, 8 months)", C5DevSiteEquivalence),
        ("slice: c5-stand — smoke (horizon 6, threshold 0,25): idle and restart run, two runs give the same bytes, no statistic row (#248)", () => C5Smoke(tmp)),
        ("slice: H8 — a control layer (LK, LK0, LC0, LC, LP) past 384 months is rejected before any month runs; FULL is not (TASK-0121)", () => ControlHorizon(tmp)),
    ];

    // H8 of slice-horizon-2030.md §2.2 (TASK-0121): the control layers are calibration stands of 1945-1977. 384 months (ticks 11688) is the last
    // accepted run of a control layer; one more month fails loudly in the model factory and in calib-print; FULL is never limited by it.
    private static string? ControlHorizon(string tmp)
    {
        GameData data = SliceData.Load(tmp);
        ScenarioData sc = data.Scenario(SliceRunConfig.DefaultScenario);
        var calendar = new TickCalendar(new CivilDate(sc.StartYear, sc.StartMonth, 1));
        int h = JetSliceModelFactory.SliceHorizonMonths;
        long last = calendar.TickOfMonth(h), over = last + 1, y2030 = calendar.TickOfMonth(1020);
        if (last != 11688 || JetSliceModelFactory.MonthsOf(last, sc) != h || JetSliceModelFactory.MonthsOf(over, sc) != h + 1)
            return $"month count: ticks {last} → {JetSliceModelFactory.MonthsOf(last, sc)}, {over} → {JetSliceModelFactory.MonthsOf(over, sc)}";
        LayerControl[] control = [LayerControl.Lk, LayerControl.Lk0, LayerControl.Lc0, LayerControl.Lc, LayerControl.Lp];
        foreach (LayerControl layer in control)
        {
            try { JetSliceModelFactory.CheckControlHorizon(layer, last, sc); }
            catch (ParamException e) { return $"{layer} rejected at {h} months: {e.Message}"; }
            foreach (long t in new[] { over, y2030 })
            {
                try
                {
                    JetSliceModelFactory.CheckControlHorizon(layer, t, sc);
                    return $"{layer} accepted ticks {t}";
                }
                catch (ParamException e) when (e.Message.Contains("1945-1977", StringComparison.Ordinal)) { }
            }
        }
        JetSliceModelFactory.CheckControlHorizon(LayerControl.Full, y2030, sc);

        // End to end: the run fails with the parameter error, before any month (no CONTROL_TABLES file is even read: the check comes first).
        foreach (string pars in new[] { """{"LAYER": "LK0", "detail": "lite"}""", """{"LAYER": "LK", "CONTROL_TABLES": "absent.json", "detail": "lite"}""" })
        {
            var (r, _) = Run(tmp, Manifest("JetSlice", y2030, pars: pars), 1, 1, "h8");
            if (r.Ok || r.Error?.Contains("ParamException", StringComparison.Ordinal) != true || r.Error.Contains("1945-1977", StringComparison.Ordinal) != true)
                return $"run {pars} past the horizon: ok={r.Ok} {r.Error}";
        }
        string dir = Path.Combine(tmp, "h8-print");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "m.json");
        File.WriteAllText(path, Manifest("JetSliceCalib", over, seeds: "[1]", pars: """{"LAYER": "LC0"}"""));
        try
        {
            Smoke.CalibPrint.Run(path, null, null, Path.Combine(dir, "out"), 1, TextWriter.Null);
            return "calib-print accepted LC0 past the horizon";
        }
        catch (ParamException e) when (e.Message.Contains("1945-1977", StringComparison.Ordinal)) { }
        return null;
    }

    // Review M5 of 321d8c9: the print runs the matrix world (final hash = golden_hash) and its invariants hold on a short run.
    private static string? CalibPrintSmoke(string tmp)
    {
        string dir = Path.Combine(tmp, "calib-print");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "m.json");
        string json = Manifest("JetSliceCalib", Ticks60, seeds: "[10001]");
        File.WriteAllText(path, json);
        var (r, _) = Run(tmp, json, 10001, 1, "calib-print-golden");
        if (!r.Ok)
            return r.Error;
        string outDir = Path.Combine(dir, "out");
        if (Smoke.CalibPrint.Run(path, null, null, outDir, 1, TextWriter.Null) != 0)
            return "calib-print exit code";
        string[] runs = File.ReadAllLines(Path.Combine(outDir, "fielded_runs.csv"));
        if (runs.Length != 2 || runs[1].Split(',')[2] != r.GoldenHash)
            return $"hash {(runs.Length > 1 ? runs[1] : "-")} vs golden {r.GoldenHash}";
        string[] lines = File.ReadAllLines(Path.Combine(outDir, "fielded_actor.csv"));
        string[] head = lines[0].Split(',');
        int[] fl = [.. Enumerable.Range(0, head.Length).Where(i => head[i].StartsWith("first_l_g", StringComparison.Ordinal))];
        int[] fr = [.. Enumerable.Range(0, head.Length).Where(i => head[i].StartsWith("first_r6_g", StringComparison.Ordinal))];
        if (fl.Length == 0 || fl.Length != fr.Length)
            return $"generation columns {lines[0]}";
        int lt = Array.IndexOf(head, "months_r6_gt_l");
        foreach (string line in lines.Skip(1))
        {
            string[] c = line.Split(',');
            if (c[lt] != "0")
                return $"G_r6 > G_L in {line}";
            for (int g = 0; g < fl.Length; g++)
            {
                if (c[fl[g]] != c[fr[g]])
                    return $"first entry differs in {line}";
            }
        }
        // D3 (spec v0.34 §5.9, #250): the print also reads JetSlice (main); its final hash is the JetSlice golden of the same seed.
        string jsJson = Manifest("JetSlice", Ticks60, seeds: "[10001]", pars: """{"detail": "lite"}""");
        File.WriteAllText(path, jsJson);
        var (rj, _) = Run(tmp, jsJson, 10001, 1, "calib-print-js-golden");
        if (!rj.Ok)
            return rj.Error;
        string jsOut = Path.Combine(dir, "out-js");
        if (Smoke.CalibPrint.Run(path, null, null, jsOut, 1, TextWriter.Null) != 0)
            return "calib-print JetSlice exit code";
        string[] jsRuns = File.ReadAllLines(Path.Combine(jsOut, "fielded_runs.csv"));
        if (jsRuns.Length != 2 || jsRuns[1].Split(',')[2] != rj.GoldenHash)
            return $"JetSlice hash {(jsRuns.Length > 1 ? jsRuns[1] : "-")} vs golden {rj.GoldenHash}";
        foreach (string bad in new[] { """{"FORCE": "X1"}""", """{"LAYER": "LK", "CONTROL_TABLES": "t.json"}""" })
        {
            File.WriteAllText(path, Manifest("JetSliceCalib", Ticks60, seeds: "[10001]", pars: bad));
            try
            {
                Smoke.CalibPrint.Run(path, null, null, Path.Combine(dir, "bad"), 1, TextWriter.Null);
                return $"accepted {bad}";
            }
            catch (UsageException) { }   // any other exception fails the check with its message
        }
        return null;
    }

    // #268: the r6 key of calib-print on hand-made views. Processes 101 (tier 1), 102 and 103 (tier 2) mastered, 104 (tier 3) not; n_service_min 3.
    private static string? CalibPrintR6Key()
    {
        static ProcessView Pv(bool mastered) => new(Fx6.One, Fx6.Zero, true, Fx6.One, mastered, Fx6.Zero);
        static DesignView Dv(int d, DesignStage stage, int gen, long inServiceRaw, long perf, params int[] prc) =>
            new(d, stage, gen, gen, -1, -1, true, false, Fx6.Zero, Fx6.One, Fx6.One, Fx6.FromRaw(inServiceRaw), Fx6.One, Fx6.FromInt(perf),
                LineBind.None, true, [.. prc]);
        var layout = new SliceLayoutView(["USA"], [101, 102, 103, 104, 200], [1, 1, 1, 1, 1], [101, 102, 103, 104], [1, 2, 2, 3], [], [], 6, 0, 2, 0,
            Fx6.One, Fx6.One);
        NodeList nodes = NodeList.Create(
        [
            new NodeView(101, true, false, Pv(true), null), new NodeView(102, true, false, Pv(true), null), new NodeView(103, true, false, Pv(true), null),
            new NodeView(104, true, false, Pv(false), null), new NodeView(200, true, false, null, null),
        ]);
        var designs = System.Collections.Immutable.ImmutableArray.Create(
            Dv(0, DesignStage.InService, 1, 5 * Fx6.Scale, 100, 101),          // fielded by r6
            Dv(1, DesignStage.InService, 2, 10 * Fx6.Scale, 300, 101, 104),    // 104 not mastered now: out of r6 (the closed-line case)
            Dv(2, DesignStage.InService, 1, 3 * Fx6.Scale - 1, 500, 101),      // ⌊2.999999⌋ = 2 < 3: out
            Dv(3, DesignStage.InService, 1, 4 * Fx6.Scale, 200, 102),          // fielded by r6, the largest P of the set
            Dv(4, DesignStage.None, 2, 9 * Fx6.Scale, 900, 101),               // no stage: out
            Dv(5, DesignStage.Series, 1, 3 * Fx6.Scale, 150, 101, 102, 103));  // exactly n_service_min, all mastered: in
        var av = new ActorView(0, PolicyProfile.Quality, [], 2, new TechKey(2, 2, 2, Fx6.FromInt(300)), null, [], default,
            new LicenceView(0, -1, -1, 0, 0, Fx6.Zero, [], false), default, nodes, designs, new PolicyDecisionView([], [], [], [], []));
        TechKey k = Smoke.CalibPrint.KeyR6(av, layout, 3);
        if (k != new TechKey(1, 2, 2, Fx6.FromInt(200)))
            return $"K_r6 {k}, expected (1, 2, 2, 200)";
        // G and P are maxima over the set, not the fields of one design: a gen-2 design with a low perf next to a gen-1 design with a high perf.
        var mixed = av with
        {
            Designs = System.Collections.Immutable.ImmutableArray.Create(
                Dv(0, DesignStage.InService, 2, 5 * Fx6.Scale, 100, 101),
                Dv(1, DesignStage.InService, 1, 5 * Fx6.Scale, 400, 102)),
        };
        k = Smoke.CalibPrint.KeyR6(mixed, layout, 3);
        if (k != new TechKey(2, 2, 2, Fx6.FromInt(400)))
            return $"K_r6 {k} with G and P from different designs, expected (2, 2, 2, 400)";
        // Tie rule of the metric (contract §9.1 v2.2): the incumbent keeps a tie, a new leader is the first of the tied top, the field is where it leads.
        TechKey a = new(1, 2, 2, Fx6.FromInt(200)), b = new(1, 2, 2, Fx6.FromInt(100)), z = new(0, 2, 2, Fx6.Zero);
        TechKey[] keys = [a, a, z];
        if (SliceObserver.Leader(keys, 3, 1) != 1 || SliceObserver.LeadField(keys, 3, 1) != '=')
            return "a tie must be kept by the incumbent with field '='";
        if (SliceObserver.Leader(keys, 3, 2) != 0 || SliceObserver.LeadField(keys, 3, 0) != '=')
            return "a new leader among a tied top must be the first actor";
        keys = [b, a, z];
        if (SliceObserver.Leader(keys, 3, 0) != 1 || SliceObserver.LeadField(keys, 3, 1) != 'P')
            return "a strict lead on P must change the leader with field 'P'";
        keys = [b, a, new TechKey(2, 0, 0, Fx6.Zero)];
        return SliceObserver.Leader(keys, 3, 1) == 2 && SliceObserver.LeadField(keys, 3, 2) == 'G' ? null : "G must dominate T, N and P";
    }

    // #268: on the full views the leader columns of calib-print equal the observer's summary (the L leader), and lead_r6 is the leader of the printed r6 keys.
    private static string? CalibPrintR6Leader(string tmp)
    {
        string dir = Path.Combine(tmp, "calib-print-r6");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "m.json");
        string json = Manifest("JetSlice", Ticks150, seeds: "[3]", pars: """{"detail": "full"}""");
        File.WriteAllText(path, json);
        var (r, runDir) = Run(tmp, json, 3, 1, "calib-print-r6-golden");
        if (!r.Ok)
            return r.Error;
        string outDir = Path.Combine(dir, "out");
        if (Smoke.CalibPrint.Run(path, null, null, outDir, 1, TextWriter.Null) != 0)
            return "calib-print exit code";
        string[] runs = File.ReadAllLines(Path.Combine(outDir, "fielded_runs.csv"));
        if (runs.Length != 2 || runs[1].Split(',')[2] != r.GoldenHash)
            return $"hash {(runs.Length > 1 ? runs[1] : "-")} vs golden {r.GoldenHash}";
        string[] sum = File.ReadAllLines(Path.Combine(runDir, "summary.csv"));
        string[] sh = sum[0].Split(','), sc = sum[1].Split(',');
        string lead = sc[Array.IndexOf(sh, "lead")], field = sc[Array.IndexOf(sh, "lead_field")];
        string[] leads = File.ReadAllLines(Path.Combine(outDir, "fielded_lead.csv"));
        if (leads[0] != Smoke.CalibPrint.LeadHeader || leads.Length != 2)
            return $"fielded_lead.csv: {leads[0]}, {leads.Length} lines";
        string[] lc = leads[1].Split(',');
        if (lead.Length != 150 || lc[2] != lead || lc[4] != field)
            return $"lead_l/field_l differ from summary (length {lead.Length})";
        if (lc[3].Length != 150 || lc[5].Length != 150)
            return "lead_r6/field_r6 length";
        string[] keyLines = File.ReadAllLines(Path.Combine(outDir, "fielded_key.csv"));
        if (keyLines[0] != Smoke.CalibPrint.KeyHeader)
            return $"fielded_key.csv header {keyLines[0]}";
        // Rebuild both leaders from the printed keys of the great powers (rows in month order, actors in layout order).
        var byMonth = keyLines.Skip(1).Select(x => x.Split(',')).GroupBy(c => long.Parse(c[3], System.Globalization.CultureInfo.InvariantCulture)).ToList();
        int nGreat = SliceObserver.Great.Length, l6 = 0, ll = 0;
        var r6 = new System.Text.StringBuilder();
        var rl = new System.Text.StringBuilder();
        foreach (var g in byMonth)
        {
            string[][] rows = [.. g];
            if (rows.Length < nGreat || Enumerable.Range(0, nGreat).Any(i => rows[i][2] != SliceObserver.Great[i]))
                return $"month {g.Key}: actor rows";
            static int P(string v) => int.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
            TechKey[] k6 = [.. rows.Select(c => new TechKey(P(c[8]), P(c[5]), P(c[6]), Fx6.FromRaw(long.Parse(c[9], System.Globalization.CultureInfo.InvariantCulture))))];
            TechKey[] kl = [.. rows.Select(c => new TechKey(P(c[4]), P(c[5]), P(c[6]), Fx6.FromRaw(long.Parse(c[7], System.Globalization.CultureInfo.InvariantCulture))))];
            if (rows.Any(c => P(c[8]) > P(c[4]) || long.Parse(c[9], System.Globalization.CultureInfo.InvariantCulture) > long.Parse(c[7], System.Globalization.CultureInfo.InvariantCulture)))
                return $"month {g.Key}: K_r6 above K_L";
            l6 = SliceObserver.Leader(k6, nGreat, l6);
            ll = SliceObserver.Leader(kl, nGreat, ll);
            r6.Append((char)('0' + l6));
            rl.Append((char)('0' + ll));
        }
        if (rl.ToString() != lead)
            return "leader of the printed K_L differs from summary lead";
        return r6.ToString() == lc[3] ? null : "leader of the printed K_r6 differs from lead_r6";
    }

    // #251: the draws exported by lk-draws are those of the run — the self-check of the corrected L_K check, here on synthetic tables, 150 months.
    private static string? LkDrawsExact(string tmp)
    {
        GameData data = SliceData.Load(tmp);
        File.WriteAllText(Path.Combine(tmp, "lkd-tables.json"), SyntheticControlTables(data, n => n % 7 == 0 ? null : n % 5));
        string json = Manifest("JetSliceCalib", Ticks150, seeds: "[3]", pars: """{"LAYER": "LK", "CONTROL_TABLES": "lkd-tables.json"}""");
        string path = Path.Combine(tmp, "lkd-m.json");
        File.WriteAllText(path, json);
        var (r, dir) = Run(tmp, json, 3, 1, "lkd-run");
        if (!r.Ok)
            return r.Error;
        string outDir = Path.Combine(tmp, "lkd-out");
        Smoke.LkDraws.Run(path, null, null, outDir, TextWriter.Null);
        var known = new Dictionary<(string, string), string>();
        string[] nt = File.ReadAllLines(Path.Combine(dir, "node_times.csv"));
        string[] h = nt[0].Split(',');
        int ia = Array.IndexOf(h, "actor"), ino = Array.IndexOf(h, "node"), itk = Array.IndexOf(h, "t_known");
        foreach (string line in nt.Skip(1))
        {
            string[] c = line.Split(',');
            known[(c[ia], c[ino])] = c[itk];
        }
        string[] draws = File.ReadAllLines(Path.Combine(outDir, Smoke.LkDraws.File));
        if (draws[0] != Smoke.LkDraws.Header || draws.Length < 2)
            return "lk_draws.csv header or no rows";
        int seen = 0;
        foreach (string line in draws.Skip(1))
        {
            string[] c = line.Split(',');   // seed,actor,gen,app,never,clock,own,req
            string want = c[7].Length > 0 && long.Parse(c[7], System.Globalization.CultureInfo.InvariantCulture) <= 149
                ? Math.Max(0, long.Parse(c[7], System.Globalization.CultureInfo.InvariantCulture)).ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            string got = known.TryGetValue((c[1], c[3]), out string? v) ? v : "";
            if (got != want)
                return $"{c[1]}:{c[3]} t_known {got}, draws give {want} ({line})";
            if (c[6].Length > 0 && c[7].Length > 0 && long.Parse(c[7], System.Globalization.CultureInfo.InvariantCulture) < long.Parse(c[6], System.Globalization.CultureInfo.InvariantCulture))
                return $"req < own in {line}";
            seen += want.Length > 0 ? 1 : 0;
        }
        return seen > 0 ? null : "no APP became known in the run: the check compared nothing";
    }

    // Gate v0.33 (critic): the C5 site runs the build's culture rule like the C1 site; on the shared path (P3, t0, producing) the states are equal.
    private static string? C5DevSiteEquivalence()
    {
        string mods = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(SliceData.Files(AppContext.BaseDirectory)[0]))!, "core");
        GameData data = Singularity.Core.Data.ModLoader.Load(mods);
        ScenarioData sc = data.Scenario("jet_slice_1945");
        var ip = new Core.Industry.IndustryParams();
        var line = new Smoke.C1Stand.ReferenceLine(data, sc, ip);
        var dev = new Smoke.C1Stand.DevSite(data, sc, line, Core.Policy.PolicyProfile.Adaptive, false, false, ip);
        dev.Run(3);   // months 0…7
        var c5 = new Smoke.C5Stand.Site(data, sc, line, Core.Policy.PolicyProfile.Adaptive, false, ip, Smoke.C5Stand.Horizon);
        c5.Produce(8);
        Fx6[] a = dev.StateVector(), b = c5.StateVector();
        if (a.Length != b.Length)
            return $"state lengths {a.Length} vs {b.Length}";
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
                return $"state {i}: DevSite {a[i]} vs C5 {b[i]}";
        }
        return null;
    }

    // #248: the C5 stand is deterministic; a smoke run (not the statistic: horizon 6, threshold 0,25) passes through the idle and restart of arm ADMIT.
    private static string? C5Smoke(string tmp)
    {
        string mods = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(SliceData.Files(AppContext.BaseDirectory)[0]))!, "core");
        var thr = Fx6.FromRaw(250_000);
        string a = Path.Combine(tmp, "c5-a"), b = Path.Combine(tmp, "c5-b");
        var logA = new StringWriter();
        if (Smoke.C5Stand.Run(mods, a, logA, new Core.Industry.IndustryParams(), 6, thr) != 0)
            return "smoke exit code";
        string text = logA.ToString();
        if (text.Contains("NaN", StringComparison.Ordinal) || text.Contains("Infinity", StringComparison.Ordinal) || text.Contains('∞'))
            return "NaN or Infinity in the log";
        Smoke.C5Stand.Run(mods, b, TextWriter.Null, new Core.Industry.IndustryParams(), 6, thr);
        foreach (string f in new[] { "c5_stand.csv", "c5_trace.csv", "c5_process.csv", "c5_cells.csv", "c5_meta.csv" })
        {
            if (!File.ReadAllBytes(Path.Combine(a, f)).SequenceEqual(File.ReadAllBytes(Path.Combine(b, f))))
                return $"{f} differs between two runs";
        }
        string[] rows = File.ReadAllLines(Path.Combine(a, "c5_stand.csv"));
        if (rows[0] != Smoke.C5Stand.StandHeader)
            return "header";
        if (rows.Skip(1).Any(r => r.Split(',')[1] != "0"))
            return "a smoke run wrote a statistic row (in_stat = 1)";
        string? admit = rows.FirstOrDefault(r => r.StartsWith("ADMIT,", StringComparison.Ordinal));
        string[] c = admit?.Split(',') ?? [];
        int restart = Array.IndexOf(Smoke.C5Stand.StandHeader.Split(','), "restart_month");
        if (c.Length <= restart || c[restart] != "120")
            return $"ADMIT row {admit}: no restart at month 120";
        // #252: censored newcomers (T0 arms at horizon 6) and the newcomer admitted at t0 (ADMIT, n_new = 0) are both «undefined», without d.
        int io = Array.IndexOf(Smoke.C5Stand.StandHeader.Split(','), "outcome");
        if (rows.Skip(1).Any(r => r.Split(',')[io] != Smoke.C5Stand.Undefined || r.Split(',')[io + 1] != ""))
            return "smoke outcomes are not all undefined without d";
        return Smoke.C5Stand.OutcomeOf(10, 9) == Smoke.C5Stand.Pass && Smoke.C5Stand.OutcomeOf(10, 10) == Smoke.C5Stand.Fail
            && Smoke.C5Stand.OutcomeOf(10, null) == Smoke.C5Stand.Fail && Smoke.C5Stand.OutcomeOf(0, 0) == Smoke.C5Stand.Undefined
            && Smoke.C5Stand.OutcomeOf(null, null) == Smoke.C5Stand.Undefined ? null : "OutcomeOf codes";
    }

    // DEBT-0032: a licence that lapsed (Active 1 -> 0) and is Active again without a purchase was no longer tracked; now it resumes (LIC_RESUME).
    private static string? LicenceTrack()
    {
        var cases = new (bool Open, bool Lapsed, bool Bought, bool Active, SliceObserver.LicenceStep Want)[]
        {
            (true, false, false, true, SliceObserver.LicenceStep.None),
            (true, false, false, false, SliceObserver.LicenceStep.End),
            (true, false, true, true, SliceObserver.LicenceStep.Replace),
            (true, false, true, false, SliceObserver.LicenceStep.Replace),
            (false, true, false, true, SliceObserver.LicenceStep.Resume),
            (false, true, false, false, SliceObserver.LicenceStep.None),
            (false, true, true, true, SliceObserver.LicenceStep.None),     // a new purchase opens a new row, not a resume
            (false, false, false, true, SliceObserver.LicenceStep.None),
            (false, false, true, true, SliceObserver.LicenceStep.None),
        };
        foreach (var c in cases)
        {
            SliceObserver.LicenceStep got = SliceObserver.Track(c.Open, c.Lapsed, c.Bought, c.Active);
            if (got != c.Want)
                return $"open={c.Open} lapsed={c.Lapsed} bought={c.Bought} active={c.Active}: {got}, want {c.Want}";
        }
        return null;
    }

    // DEBT-0032: every switch of the format §3 that is not idle by construction changes the run of seed 1 (full horizon). LIC_KNOWS, LAYER and
    // the tooling keys are covered elsewhere (T6d, T8 tests); EFFORT_SHARE X1 and LINE_CLOSE YPROD are the defaults.
    private static string? Switches(string tmp)
    {
        var (main, _) = Run(tmp, Manifest("JetSlice", 11688, pars: """{"detail": "lite"}"""), 1, 1, "sw-main");
        if (!main.Ok)
            return main.Error;
        // A41_FORM and PILOT_PROFILES are not listed: a non-default value adds the run.a41 column, so the golden would differ whatever the AI does; their effect on the compositions is A41OptionsTests.
        string[] switches =
        [
            """{"FORCE": "X1"}""", """{"FORCE": "Q"}""", """{"START": "PERT"}""", """{"START": "SYM"}""", """{"CHANNELS": ["KCATCH"]}""",
            """{"CHANNELS": ["E0", "KTR"]}""", """{"GRID": "LO_MID_MID"}""", """{"GRID": "MID_HI_MID"}""", """{"GRID": "MID_MID_HI"}""",
            """{"FLEET_ARM": "NO_RETIRE"}""", """{"FLEET_ARM": "NO_SALE"}""", """{"FLEET_ARM": "PURCHASE_CAP"}""", """{"SALE_ARM": "SIGMA_LO"}""",
            """{"SALE_ARM": "QUOTA_LO"}"""
        ];
        var same = new List<string>();
        int k = 0;
        foreach (string sw in switches)
        {
            string pars = sw.Replace("{", "{\"detail\": \"lite\", ", StringComparison.Ordinal);
            var (r, _) = Run(tmp, Manifest("JetSlice", 11688, pars: pars), 1, 1, "sw-" + k++);
            if (!r.Ok)
                return $"{sw}: {r.Error}";
            if (r.GoldenHash == main.GoldenHash)
                same.Add(sw);
        }
        return same.Count == 0 ? null : "golden equals main (switch idle on seed 1): " + string.Join(" ", same);
    }

    // Codex r4 (TASK-0044): Bind = Money also holds with positive money short of one unit or with training money; the spec class needs neither.
    private static string? CoexistMoneyClass()
    {
        Fx6 zero = Fx6.Zero, some = Fx6.FromInt(5);
        SliceObserver.ActorMonth M(Fx6? left, Fx6? training, LineBind bind, SliceObserver.Role role = SliceObserver.Role.N, bool two = true) =>
            new(Fx6.One, false, Fx6.Zero, false, two, false, false, [role], [Fx6.One], [-1], [left], [training], [zero], [bind]);
        var ok = M(zero, zero, LineBind.Money);
        var cases = new (string Name, SliceObserver.ActorMonth Bad, bool Want)[]
        {
            ("spec case", ok, true),
            ("left > 0", M(some, zero, LineBind.Money), false),
            ("training > 0", M(zero, some, LineBind.Money), false),
            ("no step-7 money row", M(null, null, LineBind.Money), false),
            ("not admitted (Bind None)", M(zero, zero, LineBind.None), false),
            ("role W", M(zero, zero, LineBind.Money, SliceObserver.Role.W), false),
            ("one line", M(zero, zero, LineBind.Money, two: false), false),
        };
        foreach (var (name, bad, want) in cases)
        {
            // The bad month is the last of three: the class needs the condition in every month.
            if (SliceObserver.CoexistMoney([ok, ok, bad], 0) != want)
                return $"COEXIST_MONEY {(want ? "rejected" : "accepted")} the case '{name}'";
        }
        return SliceObserver.CoexistMoney([], 0) ? "COEXIST_MONEY of no months" : null;
    }

    // LIC_END is the 1 -> 0 change of LicenceView.Active (format §11 п. 7): every licence row with a t_end has its event, and a lapse exists in the run.
    // Codex r5 (TASK-0044): P4 (b) needs ≥ 6 shortage months at one bureau; 3 + 3 at two bureaus is not a case.
    private static string? EngShortOneBureau()
    {
        static BureauView Short(int b) => new(b, 0, 80, 100);       // granted/n = 0.8 < 0.9
        static BureauView Ok(int b) => new(b, 0, 95, 100);
        var split = new int[2];
        for (int m = 0; m < 6; m++)
            SliceObserver.CountShort(split, m < 3 ? [Short(0), Ok(1)] : [Ok(0), Short(1)]);
        if (SliceObserver.MaxShort(split) != 3)
            return $"3 + 3 months at two bureaus gave {SliceObserver.MaxShort(split)}, want 3";
        var one = new int[2];
        for (int m = 0; m < 6; m++)
            SliceObserver.CountShort(one, [Short(0), new BureauView(1, -1, 0, 100)]);
        if (SliceObserver.MaxShort(one) != 6)
            return $"6 months at one bureau gave {SliceObserver.MaxShort(one)}, want 6";
        var idle = new int[1];
        SliceObserver.CountShort(idle, [new BureauView(0, -1, 0, 100), new BureauView(0, 0, 0, 0)]);
        return SliceObserver.MaxShort(idle) == 0 ? null : "a bureau without a project or need counted";
    }

    private static string? LicenceEnd(string tmp)
    {
        var (r, dir) = Run(tmp, Manifest("JetSlice", 11688), 1, 1, "lic-end");
        if (!r.Ok)
            return r.Error;
        var licences = Rows(dir, "licences.csv");
        var ends = Rows(dir, "events.csv").Where(e => e["kind"] == "LIC_END").ToList();
        var ended = licences.Where(l => l["t_end"].Length > 0).ToList();
        if (ended.Count == 0)
            return "seed 1: no licence has a t_end";
        if (ended.Count != ends.Count)
            return $"{ended.Count} licences with t_end, {ends.Count} LIC_END events";
        return ended.All(l => ends.Any(e => e["t"] == l["t_end"] && e["actor"] == l["buyer"])) ? null : "a t_end without a LIC_END of the buyer in that month";
    }

    private static List<Dictionary<string, string>> Rows(string dir, string file)
    {
        string[] lines = File.ReadAllLines(Path.Combine(dir, file));
        string[] head = lines[0].Split(',');
        return [.. lines.Skip(1).Select(l => head.Zip(l.Split(',')).ToDictionary(p => p.First, p => p.Second, StringComparer.Ordinal))];
    }

    // Codex r3 (TASK-0044): SliceData cached by directory only gave old data next to a new data_hash after a mod file changed in the process.
    private static string? DataCache(string tmp)
    {
        string repo = Path.Combine(tmp, "slice-cache-repo");
        string mods = Path.Combine(repo, "mods", "core");
        Directory.CreateDirectory(mods);
        File.WriteAllText(Path.Combine(repo, "Singularity.sln"), "");
        string source = Path.GetDirectoryName(Path.GetDirectoryName(SliceData.Files(AppContext.BaseDirectory)[0]))!;
        foreach (string f in Directory.GetFiles(Path.Combine(source, "core")))
            File.Copy(f, Path.Combine(mods, Path.GetFileName(f)), overwrite: true);
        string path = Path.Combine(repo, "lab", "m.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Manifest("JetSlice", Ticks60));
        Manifest m = Lab.Manifest.Load(path);

        Provenance before = Provenance.Collect(m);           // fills the cache
        string d1 = SliceData.Load(m.BaseDirectory).DataHash;
        string nodes = Path.Combine(mods, "nodes.yaml");
        string text = File.ReadAllText(nodes);
        int at = text.IndexOf("weight: 0.6}", StringComparison.Ordinal);
        if (at < 0)
            return "nodes.yaml has no 'weight: 0.6}' to change";
        File.WriteAllText(nodes, text[..at] + "weight: 0.5}" + text[(at + "weight: 0.6}".Length)..]);
        string fresh = Singularity.Core.Data.ModLoader.Load(mods).DataHash;
        string d2 = SliceData.Load(m.BaseDirectory).DataHash;
        if (fresh == d1)
            return "the edit of nodes.yaml did not change GameData.DataHash";
        if (d2 != fresh)
            return $"SliceData.Load gave the cached data {d2} after nodes.yaml changed (fresh {fresh})";

        // A run whose recorded data_files predate the change fails instead of pairing new data with the old data_hash.
        RunOutcome stale = RunExecutor.Execute(new RunRequest(m, 0, 3, 1, Path.Combine(repo, "stale"), "r"), before, TextWriter.Null);
        if (stale.Ok || stale.Error?.Contains("changed after data_hash was recorded", StringComparison.Ordinal) != true)
            return $"a run with stale data_files was not refused: {stale.Error}";

        Provenance now = Provenance.Collect(m);
        string outDir = Path.Combine(repo, "out");
        RunOutcome r = RunExecutor.Execute(new RunRequest(m, 0, 3, 1, outDir, "r"), now, TextWriter.Null);
        if (!r.Ok)
            return $"run failed: {r.Error}";
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, RunExecutor.RunJsonFile)));
        JsonElement root = doc.RootElement;
        if (root.GetProperty("data_hash").GetString() != now.DataHash || now.DataHash == before.DataHash)
            return "run.json data_hash is not the hash of the changed files";
        string? model = root.GetProperty("model_info").GetProperty("data_hash").GetString();
        return model == fresh ? null : $"model_info.data_hash {model}, want the changed data {fresh}";
    }

    // Codex r3 (TASK-0044): a40.force was always 0, though the FORCE arms launch series with force (format §4.9).
    private static string? A40Force(string tmp)
    {
        // With I-OC (TASK-0058) main gives no project or series up (the a40 table is empty), so the check runs on the A40 stress arm: no effort money, no spill or catch-up.
        const string stress = "\"EFFORT_SHARE\": \"X0\", \"CHANNELS\": [\"SIGMA\", \"KCATCH\"]";
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var (f, df) = Run(tmp, Manifest("JetSlice", 11688, pars: "{\"FORCE\": \"X1\", " + stress + "}"), seed, 1, "force-x1-" + seed);
            if (!f.Ok)
                return f.Error;
            var rows = A40Rows(df);
            if (!rows.Any(r => r["branch"] == "C" && r["force"] == "1"))
                continue;
            if (rows.Any(r => r["force"] == "1" && (r["branch"] != "C" || long.Parse(r["t_launch"], System.Globalization.CultureInfo.InvariantCulture) <= 0)))
                return "force = 1 on a row that is not a series launched in the run";
            var (o, dOff) = Run(tmp, Manifest("JetSlice", 11688, pars: "{" + stress + "}"), seed, 1, "force-off-" + seed);
            if (!o.Ok)
                return o.Error;
            var off = A40Rows(dOff);
            return off.Count > 0 && off.All(r => r["force"] == "0") ? null : $"FORCE=OFF seed {seed}: an AI row with force = 1 or no a40 row";
        }
        return "FORCE=X1 seeds 1-8: no A40C row with force = 1";
    }

    private static List<Dictionary<string, string>> A40Rows(string dir)
    {
        string[] lines = File.ReadAllLines(Path.Combine(dir, "a40.csv"));
        string[] head = lines[0].Split(',');
        return [.. lines.Skip(1).Select(l => head.Zip(l.Split(',')).ToDictionary(p => p.First, p => p.Second, StringComparer.Ordinal))];
    }

    private static string? DataProvenance(string tmp)
    {
        // A repository of its own: Singularity.sln + a copy of mods/core, the manifest beside them.
        string repo = Path.Combine(tmp, "slice-data-repo");
        string mods = Path.Combine(repo, "mods", "core");
        Directory.CreateDirectory(mods);
        File.WriteAllText(Path.Combine(repo, "Singularity.sln"), "");
        string source = Path.GetDirectoryName(Path.GetDirectoryName(SliceData.Files(AppContext.BaseDirectory)[0]))!;
        foreach (string f in Directory.GetFiles(Path.Combine(source, "core")))
            File.Copy(f, Path.Combine(mods, Path.GetFileName(f)), overwrite: true);
        string path = Path.Combine(repo, "lab", "m.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Manifest("JetSlice", Ticks60));
        Manifest m = Lab.Manifest.Load(path);

        IReadOnlyList<string> loaded = SliceData.Files(m.BaseDirectory);
        string[] want = [.. loaded.Select(f => Path.GetRelativePath(m.BaseDirectory, f).Replace('\\', '/')).Order(StringComparer.Ordinal)];
        (string h1, var files) = Provenance.HashData(m);
        if (!files.Select(f => f.Path).SequenceEqual(want))
            return $"data_files {string.Join(",", files.Select(f => f.Path))} != loaded {string.Join(",", want)}";
        foreach (string name in new[] { "mod.yaml", "nodes.yaml", "product_classes.yaml", "scenario_1945.yaml" })
        {
            if (!want.Contains("../mods/core/" + name, StringComparer.Ordinal))
                return $"../mods/core/{name} is not in data_files";
        }
        File.AppendAllText(Path.Combine(mods, "nodes.yaml"), "\n# provenance check\n");
        (string h2, _) = Provenance.HashData(m);
        if (h1 == h2)
            return "data_hash did not change with nodes.yaml";

        // The run.json of a real run carries the same data_hash and data_files.
        string outDir = Path.Combine(repo, "out");
        RunOutcome r = RunExecutor.Execute(new RunRequest(m, 0, 3, 1, outDir, "r"), Provenance.Collect(m), TextWriter.Null);
        if (!r.Ok)
            return $"run failed: {r.Error}";
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, RunExecutor.RunJsonFile)));
        if (doc.RootElement.GetProperty("data_hash").GetString() != h2)
            return "run.json data_hash differs from the hash of the loaded files";
        int n = doc.RootElement.GetProperty("data_files").GetArrayLength();
        return n == want.Length ? null : $"run.json data_files has {n} entries, want {want.Length}";
    }

    private static string? CalibrationHeader()
    {
        var problems = new List<string>();
        foreach (MetricTableSpec t in SliceTables.Calibration)
        {
            if (SliceTables.CalibrationForbiddenFiles.Contains(t.Name + ".csv"))
                problems.Add(t.Name + ".csv");
            foreach (string c in t.Columns.Prepend("run").Prepend(t.Name))
            {
                if (SliceParameters.CalibrationForbidden.Any(s => c.Contains(s, StringComparison.OrdinalIgnoreCase)))
                    problems.Add($"{t.Name}:{c}");
            }
        }
        if (SliceTables.Calibration.Length != 8 || SliceTables.Full.Length != 14)
            problems.Add($"table counts {SliceTables.Calibration.Length}/{SliceTables.Full.Length}");
        return problems.Count == 0 ? null : string.Join(", ", problems);
    }

    private static string? CalibrationRun(string tmp)
    {
        var (r, dir) = Run(tmp, Manifest("JetSliceCalib", Ticks60, pars: """{"LINE_CLOSE": "YPROD", "TOOLING_TARGET": "V5", "POOL_ORDER": "CAN"}"""), 10001, 1, "calib");
        if (!r.Ok)
            return r.Error;
        string[] files = [.. Directory.GetFiles(dir, "*.csv").Select(Path.GetFileName).Order(StringComparer.Ordinal)!];
        string[] expected = [.. SliceTables.Calibration.Select(t => t.Name + ".csv").Append(RunExecutor.RunsFile).Order(StringComparer.Ordinal)];
        if (!files.SequenceEqual(expected))
            return $"files {string.Join(",", files)}";
        foreach (string f in files)
        {
            string header = File.ReadLines(Path.Combine(dir, f)).First();
            if (header.Split(',').Any(c => SliceParameters.CalibrationForbidden.Any(s => c.Contains(s, StringComparison.OrdinalIgnoreCase))))
                return $"{f}: forbidden column in '{header}'";
        }
        foreach (string bad in new[] { """{"TOOLING_M_LEAD": 0.2}""", """{"detail": "full"}""" })
        {
            var (b, _) = Run(tmp, Manifest("JetSliceCalib", Ticks60, pars: bad), 10001, 1, "calib-bad");
            if (b.Ok || b.Error?.Contains("ParamException", StringComparison.Ordinal) != true)
                return $"accepted {bad}: {b.Error}";
        }
        return null;
    }

    private static string? Parameters()
    {
        foreach (string bad in new[] { """{"FOO": 1}""", """{"START": "X"}""", """{"LAYER": "LK"}""", """{"GRID": "LO_MID"}""", """{"CHANNELS": ["B", "B"]}""",
                     """{"CONTROL_TABLES": "t.json"}""", """{"LAYER": "LC"}""", """{"LAYER": "LP"}""", """{"LAYER": "LC0", "CONTROL_TABLES": "t.json"}""",
                     """{"X_SCALE": 0}""", """{"K_DIV": "x"}""" })
        {
            Manifest m = Lab.Manifest.Parse(Manifest("JetSlice", Ticks60, pars: bad), "/x/m.json");
            try
            {
                SliceParameters.Parse(m.Points[0].CreateParamSet(), 1, 1, 1, calibration: false);
                return $"accepted {bad}";
            }
            catch (ParamException) { }
        }
        return null;
    }

    /// <summary>A synthetic CONTROL_TABLES file of the core scenario (format §3.1): canonical JSON, integers only.</summary>
    internal static string SyntheticControlTables(GameData data, Func<int, long?>? delta = null, string[]? actors = null)
    {
        ScenarioData sc = data.Scenario(SliceRunConfig.DefaultScenario);
        int h = JetSliceModelFactory.SliceHorizonMonths;
        var apps = new HashSet<int>(ControlTables.AppNodes(data.Nodes));
        var sb = new System.Text.StringBuilder();
        sb.Append("{\"format\":\"").Append(ControlTables.Format).Append("\",\"horizon\":").Append(h).Append(",\"max_node\":").Append(data.Nodes.Number.Max());
        string[] ids = actors ?? [.. sc.ActorId];
        sb.Append(",\"actors\":[").Append(string.Join(",", ids.Select(a => $"\"{a}\""))).Append(']');
        sb.Append(",\"lk\":{").Append(string.Join(",", ids.Select(a => $"\"{a}\":{{" + string.Join(",", Enumerable.Range(1, 4).Select(g => $"\"{g}\":{{\"p_cens\":0,\"mean\":{60 * g},\"half\":6}}")) + "}"))).Append('}');
        var nodes = Enumerable.Range(0, data.Nodes.Count).Where(r => data.Nodes.Gen[r] is >= 1 and <= 4).Select(r => data.Nodes.Number[r]).ToList();
        string D(int n) => apps.Contains(n) ? "0" : (delta?.Invoke(n) is long v ? v.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null");
        sb.Append(",\"delta\":{").Append(string.Join(",", ids.Select(a => $"\"{a}\":{{" + string.Join(",", nodes.Select(n => $"\"{n}\":{D(n)}")) + "}"))).Append('}');
        string Curve(long v) => "[" + string.Join(",", Enumerable.Repeat(v, 2 * h)) + "]";
        sb.Append(",\"lc\":{\"Y\":{").Append(string.Join(",", ControlTables.Tiers.Select(t => $"\"{t}\":{Curve(500_000)}"))).Append("},\"U\":{")
            .Append(string.Join(",", ControlTables.Tiers.Select(t => $"\"{t}\":{Curve(1_500_000)}"))).Append("}}");
        sb.Append(",\"lp\":{\"T_design\":{\"1\":24,\"2\":36,\"3\":40,\"4\":60},\"rho\":{\"1\":100000,\"2\":40000,\"3\":20000,\"4\":1000}}}");
        return CanonicalJson.Canonicalize(sb.ToString());
    }

    // CONTROL_TABLES (TASK-0054): the file is read into LkTables, the run is an L_K run, run.json carries the SHA-256 of the file; a file that is
    // not canonical, has other actors, or a non-zero δ of an APP node is a parameter error.
    private static string? ControlTablesFile(string tmp)
    {
        GameData data = SliceData.Load(tmp);
        ScenarioData sc = data.Scenario(SliceRunConfig.DefaultScenario);
        string good = SyntheticControlTables(data, n => n % 7 == 0 ? null : n % 5);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(good);
        ControlTables t = ControlTables.Parse(bytes, sc, data.Nodes, JetSliceModelFactory.SliceHorizonMonths);
        string sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
        if (t.Sha256 != sha || t.Lk.Mean[4 + 1] != 120 || t.Lc.Length != 3 || t.TDesign[3] != 60)
            return "parsed tables differ from the file";
        int app = ControlTables.AppNodes(data.Nodes).First();
        int never = Enumerable.Range(0, data.Nodes.Count).Select(r => data.Nodes.Number[r]).First(n => n % 7 == 0 && n != app
            && data.Nodes.Gen[data.Nodes.Number.IndexOf(n)] is >= 1 and <= 4 && !ControlTables.AppNodes(data.Nodes).Contains(n));
        if (t.Lk.Delta[t.Lk.DeltaIndex(0, never)] != LkTables.Never || t.Lk.Delta[t.Lk.DeltaIndex(0, app)] != 0)
            return "delta null/APP not read as Never/0";
        foreach ((string bad, string why) in new[]
        {
            (good + "\n", "trailing newline"),
            (SyntheticControlTables(data, actors: ["GBR", "USA", "SUN", "FRA", "SWE"]), "actor order"),
            (good.Replace("\"format\":\"control_tables/1\"", "\"format\":\"control_tables/0\"", StringComparison.Ordinal), "format"),
        })
        {
            try
            {
                ControlTables.Parse(System.Text.Encoding.UTF8.GetBytes(bad), sc, data.Nodes, JetSliceModelFactory.SliceHorizonMonths);
                return $"accepted a file with a wrong {why}";
            }
            catch (ParamException) { }
        }
        string badApp = good.Replace($"\"{app}\":0", $"\"{app}\":3", StringComparison.Ordinal);
        try
        {
            ControlTables.Parse(System.Text.Encoding.UTF8.GetBytes(badApp), sc, data.Nodes, JetSliceModelFactory.SliceHorizonMonths);
            return "accepted a non-zero delta of an APP node";
        }
        catch (ParamException) { }

        File.WriteAllBytes(Path.Combine(tmp, "tables.json"), bytes);
        var (r, dir) = Run(tmp, Manifest("JetSlice", Ticks60, pars: """{"LAYER": "LK", "CONTROL_TABLES": "tables.json", "detail": "lite"}"""), 1, 1, "lk-file");
        if (!r.Ok)
            return r.Error;
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, RunExecutor.RunJsonFile)));
        string? got = doc.RootElement.GetProperty("model_info").TryGetProperty("control_tables_hash", out JsonElement h) ? h.GetString() : null;
        if (got != sha)
            return $"model_info.control_tables_hash {got}, want {sha}";
        var (main, _) = Run(tmp, Manifest("JetSlice", Ticks60, pars: """{"detail": "lite"}"""), 1, 1, "lk-main");
        return main.Ok && main.GoldenHash != r.GoldenHash ? null : "the L_K run has the golden of main";
    }

    // TASK-0055: one point of each calibration stage (stage manifests EXP-0002-c1/c3c4/c2.json) and the L_C/L_P layers of the same CONTROL_TABLES file are executable;
    // each K key of the point moves the golden, the L_C/L_P run differs from main and from the other layer.
    private static string? CalibrationPoints(string tmp)
    {
        GameData data = SliceData.Load(tmp);
        File.WriteAllText(Path.Combine(tmp, "tables.json"), SyntheticControlTables(data, n => n % 7 == 0 ? null : n % 5));
        string main = Run(tmp, Manifest("JetSliceCalib", Ticks60), 1, 1, "cal-main").Outcome.GoldenHash ?? "";
        var seen = new HashSet<string> { main };
        foreach ((string name, string pars) in new[]
        {
            ("C1", """{"X_SCALE": 0.75, "ALPHA": 0.08}"""),
            ("C3+C4", """{"K_DIV": 1.5, "H_MAX": 0.12, "THETA_START": 0.3, "KAPPA_Q10": 512}"""),
            ("C3+C4 phase B", """{"K_DIV": 2.5, "H_MAX": 0.08, "THETA_START": 0.9, "KAPPA_Q10": 768}"""),
            ("C2", """{"C_M": 0.05, "DELTA_H": 0.0017}"""),
            ("L_C", """{"LAYER": "LC", "CONTROL_TABLES": "tables.json"}"""),
            ("L_P", """{"LAYER": "LP", "CONTROL_TABLES": "tables.json"}"""),
        })
        {
            var (r, _) = Run(tmp, Manifest("JetSliceCalib", Ticks60, pars: pars), 1, 1, "cal-" + name.Replace('+', '_').Replace(' ', '_'));
            if (!r.Ok)
                return $"{name}: {r.Error}";
            if (!seen.Add(r.GoldenHash ?? ""))
                return $"{name}: the golden equals that of main or of another point";
        }
        return null;
    }

    // TASK-0055 Codex r2: the stage manifests stay valid after a freeze — the frozen keys of the earlier stages written into the top-level params
    // never meet a sweep axis of the stage — and Select keeps one point with the full manifest's index, hash and run ids (matrix --point P --seeds N).
    private static string? StageManifests(string tmp)
    {
        string? repo = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null && repo is null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "Singularity.sln")))
                repo = d.FullName;
        }
        if (repo is null)
            return "repository root not found";
        var frozen = new Dictionary<string, string>
        {
            ["EXP-0002-c3c4"] = "\"ALPHA\": 0.08, \"X_SCALE\": 1.5",
            ["EXP-0002-c2"] = "\"ALPHA\": 0.08, \"X_SCALE\": 1.5, \"K_DIV\": 2, \"THETA_START\": 0.3, \"H_MAX\": 0.12, \"KAPPA_Q10\": 768",
        };
        foreach ((string name, string keys) in frozen.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            string path = Path.Combine(repo, "lab", "manifests", name + ".json");
            Manifest full = Lab.Manifest.Load(path);
            // TASK-0057: the committed manifests already carry earlier freezes (C1 since TASK-0056, C3 since #239), so a key
            // already in params gets the test value instead of a second copy (a duplicate key is a manifest error).
            var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            var extra = System.Text.Json.Nodes.JsonNode.Parse("{" + keys + "}")!.AsObject();
            var pobj = root["params"]!.AsObject();
            foreach (var kv in extra.ToList())
                pobj[kv.Key] = kv.Value!.DeepClone();
            string text = root.ToJsonString();
            Manifest m;
            try
            {
                m = Lab.Manifest.Parse(text, Path.Combine(tmp, name + "-frozen.json"));
            }
            catch (ManifestException e)
            {
                return $"{name} after freezing: {e.Message}";
            }
            if (m.Points.Count != full.Points.Count || m.Hash == full.Hash)
                return $"{name}: frozen manifest has {m.Points.Count} points (want {full.Points.Count}) or the same hash";
            Manifest one = full.Select(3, 2);
            if (one.Points.Count != 1 || one.Points[0].Index != 3 || one.Points[0].Seeds.Count != 2 || one.Hash != full.Hash
                || RunExecutor.MakeRunId(one, 3, one.Points[0].Seeds[0], 1) != RunExecutor.MakeRunId(full, 3, one.Points[0].Seeds[0], 1))
                return $"{name}: Select(3, 2) does not keep one point with the index, hash and run id of the full manifest";
        }
        return null;
    }

    // TASK-0055 Codex: d_C3 of a median CV just outside the corridor is tiny but positive; the reported text must parse back to the same value.
    private static string? C3DistanceExact()
    {
        foreach (double cv in new[] { 0.5 + 1e-9, 0.3 - 1e-12, 0.50000002 })
        {
            double d = Smoke.SmokeT4s.C3Distance(cv);
            string text = Smoke.SmokeT4s.Exact(d);
            if (d <= 0 || double.Parse(text, System.Globalization.CultureInfo.InvariantCulture) != d || double.Parse(text, System.Globalization.CultureInfo.InvariantCulture) <= 0)
                return $"cv {cv}: d {d} reported as '{text}'";
        }
        return Smoke.SmokeT4s.C3Distance(0.4) == 0 && Smoke.SmokeT4s.C3Distance(0.3) == 0 && Smoke.SmokeT4s.C3Distance(0.5) == 0 ? null : "inside the corridor d > 0";
    }

    // contract-proposals T3 (TASK-0054): model_info carries the design-capacity counters; on the full horizon of seed 1 the AI never fills
    // every slot, so no command can have met DESIGN_CAPACITY.
    private static string? DesignCapacity(string tmp)
    {
        var (r, dir) = Run(tmp, Manifest("JetSlice", 11688, pars: """{"detail": "lite"}"""), 1, 1, "capacity");
        if (!r.Ok)
            return r.Error;
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, RunExecutor.RunJsonFile)));
        JsonElement info = doc.RootElement.GetProperty("model_info");
        long Get(string k) => long.Parse(info.GetProperty(k).GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        long cmds = Get("design_commands"), slots = Get("design_slots_max"), cap = Get("design_slots_capacity");
        if (cmds <= 0 || slots <= 0 || cap != 12)
            return $"design_commands {cmds}, design_slots_max {slots}, capacity {cap}";
        return slots < cap && Get("design_commands_without_design_all_slots_used") == 0 ? null : $"slots {slots} of {cap}";
    }

    private static string? Golden(string tmp)
    {
        var (r, _) = Run(tmp, Manifest("JetSlice", Ticks150, pars: """{"detail": "lite"}"""), 1, 2, "golden");
        return r.Ok && r.GoldenHash == Golden150 ? null : $"{r.GoldenHash} {r.Error}";
    }

    private static string? L1(string tmp)
    {
        var (a, da) = Run(tmp, Manifest("JetSlice", 11688), 3, 1, "l1-a");
        var (b, db) = Run(tmp, Manifest("JetSlice", 11688), 3, 1, "l1-b");
        if (!a.Ok || !b.Ok || a.GoldenHash != b.GoldenHash)
            return $"{a.GoldenHash} vs {b.GoldenHash} ({a.Error}{b.Error})";
        return SameCsv(da, db, "", "");
    }

    private static string? L2(string tmp)
    {
        // Full horizon (DEBT-0032; was 150 months): the det series of EXP-0002 repeats it on 20 seeds at threads 1/4/8.
        var (a, da) = Run(tmp, Manifest("JetSlice", 11688), 5, 1, "l2-1");
        var (b, db) = Run(tmp, Manifest("JetSlice", 11688), 5, 4, "l2-4");
        if (!a.Ok || !b.Ok || a.GoldenHash != b.GoldenHash)
            return $"{a.GoldenHash} vs {b.GoldenHash} ({a.Error}{b.Error})";
        return SameCsv(da, db, "-t1,", "-t4,");
    }

    // The run id is the first column; it differs only in the thread count. Every other cell must be identical.
    private static string? SameCsv(string da, string db, string ta, string tb)
    {
        foreach (MetricTableSpec t in SliceTables.Full)
        {
            string fa = Path.Combine(da, t.Name + ".csv"), fb = Path.Combine(db, t.Name + ".csv");
            if (!File.Exists(fa) || !File.Exists(fb))
                return $"missing {t.Name}.csv";
            string ca = File.ReadAllText(fa), cb = File.ReadAllText(fb);
            if (ta.Length > 0)
            {
                ca = ca.Replace(ta, "-tX,");
                cb = cb.Replace(tb, "-tX,");
            }
            if (ca != cb)
                return $"{t.Name}.csv differs";
        }
        return null;
    }

    private static string? Matrix(string tmp)
    {
        string dir = Path.Combine(tmp, "slice-matrix");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "m.json");
        File.WriteAllText(path, Manifest("JetSlice", Ticks150, threads: "[1, 2]", seeds: "[1, 2]", pars: """{"detail": "full"}""",
            series: """, "series": [{"name": "main"}, {"name": "lite", "params": {"detail": "lite", "LIC_OFF": true}}]"""));
        Manifest m = Lab.Manifest.Load(path);
        string outDir = Path.Combine(dir, "out");
        int rc = MatrixRunner.Run(m, outDir, 4, TextWriter.Null);
        if (rc != 0)
            return $"matrix exit {rc}: {File.ReadAllText(Path.Combine(outDir, MatrixRunner.MatrixJsonFile))}";
        string[] expected = [.. SliceTables.Full.Select(t => t.Name + ".csv").Append(RunExecutor.RunsFile).Order(StringComparer.Ordinal)];
        foreach (string run in Directory.GetDirectories(Path.Combine(outDir, MatrixRunner.RunsDir)))
        {
            string[] files = [.. Directory.GetFiles(run, "*.csv").Select(Path.GetFileName).Order(StringComparer.Ordinal)!];
            if (!files.SequenceEqual(expected))
                return $"{Path.GetFileName(run)}: files {string.Join(",", files)}";
        }
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, MatrixRunner.MatrixJsonFile)));
        int merged = doc.RootElement.GetProperty("merged_csv").GetArrayLength();
        return merged == expected.Length ? null : $"merged {merged} files";
    }

    private static (RunOutcome Outcome, string Dir) Run(string tmp, string json, ulong seed, int threads, string tag)
    {
        Manifest m = Lab.Manifest.Parse(json, Path.Combine(tmp, "m.json"));
        string dir = Path.Combine(tmp, tag);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        var prov = new Provenance("selftest", null, "selftest", []);
        RunOutcome r = RunExecutor.Execute(new RunRequest(m, 0, seed, threads, dir, RunExecutor.MakeRunId(m, 0, seed, threads)), prov, TextWriter.Null);
        return (r, dir);
    }
}
