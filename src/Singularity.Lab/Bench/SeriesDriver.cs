using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Singularity.Lab.Bench;

/// <summary>
/// <c>lab bench --mode series</c> (TASK-0106, EXP-0008 П10 п. 4, ADR-0023 п. 1): runs a whole bench series from its manifest, one child
/// process at a time on the machine, and writes every decision into <c>series.json</c>. Order:
/// <list type="number">
/// <item>pre-series stop: foreign_cpu over <c>pre_series_s</c>; above the limit the series does not start (exit 3);</item>
/// <item>f_ref(k) for every k = threads + background of the plan (<c>--mode fref --stat topk</c>);</item>
/// <item>validity (reference.validity), INVALID → exit 1;</item>
/// <item>main pass in plan order, each attempt after the wait for quiet;</item>
/// <item>med_point per group, fixed; repeats 1 in plan order, then repeats 2 (<see cref="SeriesGate"/>); exclusions; the exclusion verdict;</item>
/// <item>reference variants (П9 п. 1) with the same gate, R modes with the foreign-only gate, the profile;</item>
/// <item>post-series machine record.</item>
/// </list>
/// Exit codes: 0 series done; 1 a child failed (INVALID (а) or a crash); 2 usage error; 3 not started (no quiet before the deadline);
/// 4 INCONCLUSIVE by exclusions; 5 INVALID (б) by #355 (a forbidden process at work on the machine). A series is never
/// resumed or repeated by hand: the output directory must be new (the repeat plan is the driver's alone).
/// </summary>
internal static class SeriesDriver
{
    private sealed class Ctx
    {
        public required BenchManifest M { get; init; }
        public required string Dir { get; init; }
        public required StreamWriter Log { get; init; }
        public required bool AllowDebug { get; init; }
        public required Dictionary<int, int> Injected { get; init; }
        public Dictionary<int, double> FRef { get; } = [];
        public required bool Calibration { get; init; }
        public Stopwatch Clock { get; } = Stopwatch.StartNew();
        public string? Invalid355 { get; set; }
        public int VirtualRepeats { get; set; }
        /// <summary>The driver itself (own stat) and its ancestor chain: the launching session is never a #355 violation.</summary>
        public List<PidStat> Ancestors { get; } = [];
        public PidStat? Self { get; set; }

        public IReadOnlySet<int> ExcludedAmong(IEnumerable<(int Pid, string Comm, long? Start)> procs) =>
            SeriesDriver.ExcludedAmong(Environment.ProcessId, Self, Ancestors, procs);
        public List<ForbiddenHit> Seen355 { get; } = [];

        /// <summary>Records the hits; a fatal one stops the series with INVALID (б).</summary>
        public void Check355(List<ForbiddenHit> hits)
        {
            Seen355.AddRange(hits);
            foreach (ForbiddenHit h in hits.Where(h => !h.Fatal))
                Say($"#355 recorded (idle): {h}");
            List<ForbiddenHit> fatal = [.. hits.Where(h => h.Fatal)];
            if (fatal.Count > 0)
                throw new Invalid355(string.Join("; ", fatal));
        }

        public void Say(string line)
        {
            string s = $"{DateTime.UtcNow:HH:mm:ss} {line}";
            Console.WriteLine(s);
            Log.WriteLine(s);
            Log.Flush();
        }
    }

    /// <summary>One attempt as recorded: the gate input plus where it came from.</summary>
    private sealed record AttemptRec(string Key, int Number, string File, int Exit, double WaitQuietSeconds, double? QuietForeign, GateAttempt Gate,
        JsonElement? TopForeign, double? TopkMhz, bool? ThermalFlag, JsonElement? Thermal, DateTime Started, DateTime Ended, JsonElement? Gc = null);

    /// <summary>Keys of the child's <c>gc</c> block copied into the attempt (decision #416: the pause criterion's inputs; the pause list stays in the child file).</summary>
    internal static readonly string[] GcKeys = ["pause_max_ms", "pause_p50_ms", "pause_p99_ms", "pause_count", "gen2_blocking_after_warmup",
        "gen2_background_after_warmup", "gen2", "pause_share", "pause_share_events", "events_complete"];

    /// <summary>The attempt's copy of the child's <c>gc</c> block (pure): only <see cref="GcKeys"/>, absent keys skipped; null without the block.</summary>
    internal static JsonElement? GcOf(JsonElement child)
    {
        if (!child.TryGetProperty("gc", out JsonElement gc) || gc.ValueKind != JsonValueKind.Object)
            return null;
        using var ms = new MemoryStream();
        using (var j = new Utf8JsonWriter(ms))
        {
            j.WriteStartObject();
            foreach (string k in GcKeys)
            {
                if (gc.TryGetProperty(k, out JsonElement v))
                {
                    j.WritePropertyName(k);
                    v.WriteTo(j);
                }
            }
            j.WriteEndObject();
        }
        using JsonDocument d = JsonDocument.Parse(ms.ToArray());
        return d.RootElement.Clone();
    }

    /// <summary>The thermal record of an attempt (ADR-0023 п. 1.2): Δthrottle of the union, temperature at the start of the first and end of the last window, EPP, no_turbo.</summary>
    private static JsonElement ThermalOf(JsonElement machine)
    {
        using var ms = new MemoryStream();
        using (var j = new Utf8JsonWriter(ms))
        {
            j.WriteStartObject();
            LoadMeter.WriteNum(j, "core_throttle_delta", Num(machine, "core_throttle_delta_window"));
            LoadMeter.WriteNum(j, "package_throttle_delta", Num(machine, "package_throttle_delta_window"));
            JsonElement ws = machine.GetProperty("windows");
            int n = ws.GetArrayLength();
            JsonElement? first = n > 0 && ws[0].TryGetProperty("thermal", out JsonElement t0) ? t0 : null;
            JsonElement? last = n > 0 && ws[n - 1].TryGetProperty("thermal", out JsonElement t1) ? t1 : null;
            LoadMeter.WriteNum(j, "package_temp_c_start", first is { } f ? Num(f, "package_temp_c_start") : null);
            LoadMeter.WriteNum(j, "package_temp_c_end", last is { } l ? Num(l, "package_temp_c_end") : null);
            string? epp = first is { } f2 && f2.TryGetProperty("energy_performance_preference", out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            string? nt = first is { } f3 && f3.TryGetProperty("no_turbo", out JsonElement t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            if (epp is null) j.WriteNull("energy_performance_preference"); else j.WriteString("energy_performance_preference", epp);
            if (nt is null) j.WriteNull("no_turbo"); else j.WriteString("no_turbo", nt);
            j.WriteEndObject();
        }
        using JsonDocument d = JsonDocument.Parse(ms.ToArray());
        return d.RootElement.Clone();
    }

    public static int Run(Options o)
    {
        string manifestPath = o.Required("manifest");
        string dir = Path.GetFullPath(o.Required("out"));
        string? inject = o.Optional("inject-flag");
        bool allowDebug = o.Optional("allow-debug") == "true";
        bool calibration = o.Optional("calibration") == "true";
        o.CheckAllUsed();
        if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any())
            throw new UsageException($"{dir} is not empty: a series is never resumed or repeated by hand (EXP-0008 П10 п. 4); use a new directory.");
        BenchManifest m = BenchManifest.Load(manifestPath);
        if (!m.HasGate)
            throw new UsageException("--mode series needs a manifest with a \"gate\" block (EXP-0008 П10 п. 5).");
        Directory.CreateDirectory(dir);
        using var log = new StreamWriter(Path.Combine(dir, "series.log"), append: false);
        var ctx = new Ctx { M = m, Dir = dir, Log = log, AllowDebug = allowDebug, Injected = ParseInject(inject), Calibration = calibration };
        // One walk (shared with ExcludedFor); the chain with comm and start time is kept for the matching and for series.json.
        ctx.Ancestors.AddRange(AncestorChain(Environment.ProcessId));
        ctx.Self = StatOf(Environment.ProcessId, "/proc");
        ctx.Say($"#355: driver pid {Environment.ProcessId}, ancestors excluded: {string.Join(" ← ", ctx.Ancestors.Select(a => $"{a.Comm}({a.Pid})"))}");
        return Execute(ctx);
    }

    /// <summary>"K:N,K2:N2" → plan process K gets an artificial load flag on its first N attempts (smoke of П10 п. 7 only).</summary>
    internal static Dictionary<int, int> ParseInject(string? s)
    {
        var r = new Dictionary<int, int>();
        if (string.IsNullOrWhiteSpace(s))
            return r;
        foreach (string part in s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string[] kn = part.Split(':');
            if (kn.Length != 2 || !int.TryParse(kn[0], NumberStyles.None, CultureInfo.InvariantCulture, out int k)
                || !int.TryParse(kn[1], NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < 1)
                throw new UsageException("--inject-flag is a list of PROCESS:ATTEMPTS, e.g. 2:3,4:1.");
            r[k] = n;
        }
        return r;
    }

    private static int Execute(Ctx c)
    {
        BenchManifest m = c.M;
        BenchGate g = m.Gate;
        IReadOnlyList<BenchProcess> plan = m.Plan();
        bool smoke = c.Injected.Count > 0 || g.MainPassOnly;
        c.Say($"series {m.Experiment}: manifest_hash {m.Hash}, {plan.Count} processes, exclusion rule {g.ExclusionRule}{(smoke ? ", smoke (injected flags or main_pass_only)" : "")}");
        var outJson = new Dictionary<string, Action<Utf8JsonWriter>>();
        int exit = 0;
        string stage = "pre_series";
        try
        {
            // 1. Wait for quiet before the series (ADR-0023 п. 1.8): windows of pre_series_s until (н1) is below the limit or the deadline.
            var preWait = Stopwatch.StartNew();
            var preTries = new List<double?>();
            double? pre;
            MachineWindow preWin;
            LoadMeter preMeter;
            while (true)
            {
                (pre, preWin, preMeter) = Measure(c, g.PreSeriesSeconds, "pre_series");
                preTries.Add(pre);
                // The block is (re)registered before the check, so series.json keeps it when the check stops the series.
                // Every window of the wait is checked, not only the last one.
                (MachineWindow w0, LoadMeter m0) = (preWin, preMeter);
                RegisterThenCheck(outJson, "pre_series", PreSeriesBlock(g, preWait.Elapsed.TotalSeconds, preTries, pre, w0, m0),
                    () => c.Check355(Forbidden(c, m0, w0, $"pre_series#{preTries.Count}")));
                c.Say($"pre-series foreign {Fmt(pre)} CPUs (limit {g.PreSeriesForeignCpusMax}), waited {preWait.Elapsed.TotalSeconds:F0} s");
                if (pre is not double pv || pv <= g.PreSeriesForeignCpusMax || preWait.Elapsed.TotalSeconds + g.PreSeriesSeconds > g.PreSeriesDeadlineSeconds)
                    break;
            }
            if (pre is double p0 && p0 > g.PreSeriesForeignCpusMax)
            {
                c.Say($"NOT STARTED: no quiet within {g.PreSeriesDeadlineSeconds} s; the question goes to the owner's queue (ADR-0023 п. 1.8). Top foreign: "
                    + string.Join(", ", LoadMath.TopForeign(preWin.Start.Pids ?? [], preWin.End.Pids ?? [], preMeter.SelfPid,
                        preWin.End.Cpu?.Total - preWin.Start.Cpu?.Total ?? 0, 5).Select(t => $"{t.Comm}({t.Pid}) {t.MachineShare * Environment.ProcessorCount:F2} CPU")));
                exit = 3;
                return exit;
            }

            // 2. f_ref(k).
            stage = "fref";
            int[] ks = g.MainPassOnly ? [] : [.. plan.Select(p => p.Threads + p.Background).Distinct().Order()];
            foreach (int k in ks)
            {
                string f = Path.Combine(c.Dir, "fref", $"fref-k{k}.json");
                int code = Child(c, ["bench", "--mode", "fref", "--stat", "topk", "--k", I(k), "--threads", I(k),
                    "--seconds", I(g.FRefSeconds), "--tail", I(g.FRefTailSeconds), "--out", f], $"fref k{k}");
                if (code != 0 || ReadDouble(f, "f_ref_mhz") is not double mhz)
                    throw new ChildFailed($"fref k{k} exited {code} or gave no f_ref");
                c.FRef[k] = mhz;
                c.Say($"f_ref(k={k}) = {mhz:F0} MHz");
            }
            outJson["fref"] = j =>
            {
                j.WriteStartObject("f_ref_mhz");
                foreach ((int k, double v) in c.FRef.OrderBy(x => x.Key))
                    j.WriteNumber(I(k), v);
                j.WriteEndObject();
            };

            // 3. Validity.
            stage = "validity";
            if (!g.MainPassOnly && m.Reference.Validity is { } va)
            {
                string f = Path.Combine(c.Dir, "validity.json");
                int code = Child(c, ["bench", "--mode", "validity", .. Flat(va), "--out", f], "validity");
                outJson["validity"] = j => { j.WriteStartObject("validity"); j.WriteNumber("exit", code); j.WriteString("file", Rel(c, f)); j.WriteEndObject(); };
                if (code != 0)
                    throw new ChildFailed($"validity exited {code}: INVALID (a)");
            }

            // 4–5. Main pass, med_point, repeats, exclusions.
            stage = "main";
            var gp = plan.Select(p => new GateProcess(p.Index, p.Block, Group(p), p.Seed, p.Replica)).ToList();
            var main = Gated(c, gp, (p, n) => ProcessArgs(c, plan[p.Index], n, null, []), $"p", medianGate: true);
            if (c.Calibration)
            {
                c.Say("calibration run (ADR-0023 п. 1.10): no repeats, no reference; thresholds are chosen by lab/analysis/exp0008.py --calibrate");
                outJson["main"] = j => WriteGated(j, "main", gp, main, c, plan);
                stage = "calibration_done";
                return exit;
            }
            SeriesVerdict verdict = SeriesGate.Verdict(gp, main.Decisions, g);
            outJson["main"] = j => WriteGated(j, "main", gp, main, c, plan);
            outJson["verdict"] = j => WriteVerdict(j, verdict);
            c.Say($"main: {verdict.Excluded} of {verdict.Processes} excluded; outcome block {g.OutcomeBlock}: {verdict.OutcomeExcluded} of {verdict.OutcomeProcesses}; "
                + (verdict.Inconclusive ? "INCONCLUSIVE: " + string.Join("; ", verdict.Reasons) : "no exclusion verdict"));
            if (verdict.Inconclusive)
                exit = 4;

            if (!g.MainPassOnly)
            {
                // 6. Reference: variants, modes, profile (the #355 window does not stop them; only repeats are cut, п. 1.9).
                stage = "reference";
                var variantResults = new List<(BenchVariant V, List<GateProcess> Gp, GatedResult R)>();
                foreach (BenchVariant v in m.Reference.Variants)
                {
                    var vp = new List<GateProcess>();
                    var map = new Dictionary<int, BenchProcess>();
                    int id = 0;
                    foreach (ulong seed in v.Seeds)
                    {
                        BenchProcess bp = plan.Where(p => p.Block == v.Block && p.Seed == seed).OrderBy(p => p.Replica).ThenBy(p => p.Index).FirstOrDefault()
                            ?? throw new ManifestException($"variant {v.Id}: block {v.Block} has no process with seed {seed}");
                        map[id] = bp;
                        vp.Add(new GateProcess(id++, v.Block, $"{v.Id}|{Group(bp)}", seed, 1));
                    }
                    GatedResult r = Gated(c, vp, (p, n) => ProcessArgs(c, map[p.Index], n, v.Id, v.Switches), $"{v.Id}-", medianGate: true);
                    variantResults.Add((v, vp, r));
                }
                var modeResults = new List<(BenchModeRun Mode, GatedResult R)>();
                foreach (BenchModeRun mr in m.Reference.Modes)
                {
                    var one = new List<GateProcess> { new(0, mr.Id, mr.Id, 0, 1) };
                    GatedResult r = Gated(c, one, (_, n) => (["bench", "--mode", mr.Mode, .. Flat(mr.Args)], Path.Combine(c.Dir, "reference", $"{mr.Id}-a{n}.json")),
                        mr.Id + "#", medianGate: false);
                    modeResults.Add((mr, r));
                }
                outJson["reference"] = j =>
                {
                    j.WriteStartObject("reference");
                    j.WriteStartArray("variants");
                    foreach (var (v, vp, r) in variantResults)
                    {
                        j.WriteStartObject();
                        j.WriteString("id", v.Id);
                        WriteGated(j, "gate", vp, r, c, null);
                        j.WriteEndObject();
                    }
                    j.WriteEndArray();
                    j.WriteStartArray("modes");
                    foreach (var (mr, r) in modeResults)
                    {
                        j.WriteStartObject();
                        j.WriteString("id", mr.Id);
                        j.WriteString("mode", mr.Mode);
                        ProcessDecision d = r.Decisions[0];
                        // An excluded R mode leaves its ADR-0017 threshold «not evaluated» (EXP-0008 П9 п. 6).
                        j.WriteBoolean("evaluated", d.Status == ProcessStatus.Accepted);
                        WriteGated(j, "gate", [new GateProcess(0, mr.Id, mr.Id, 0, 1)], r, c, null);
                        j.WriteEndObject();
                    }
                    j.WriteEndArray();
                    j.WriteEndObject();
                };
                if (m.Reference.Profile is { } prof)
                {
                    (string status, string? file) = Profile(c, prof, plan);
                    outJson["profile"] = j =>
                    {
                        j.WriteStartObject("profile");
                        j.WriteString("status", status);
                        if (file is null) j.WriteNull("file"); else j.WriteString("file", Rel(c, file));
                        j.WriteEndObject();
                    };
                }
            }

            // 7. Post-series record.
            stage = "post_series";
            (double? post, MachineWindow postWin, LoadMeter postMeter) = Measure(c, g.PreSeriesSeconds, "post_series");
            c.Check355(Forbidden(c, postMeter, postWin, "post_series"));
            outJson["post_series"] = j =>
            {
                j.WriteStartObject("post_series");
                LoadMeter.WriteNum(j, "foreign_cpus", post);
                postMeter.Write(j, [postWin], 0);
                j.WriteEndObject();
            };
            c.Say($"post-series foreign {Fmt(post)} CPUs; series done, exit {exit}");
            return exit;
        }
        catch (Invalid355 e)
        {
            c.Say($"INVALID (б) #355 at {stage}: {e.Message}");
            c.Invalid355 = e.Message;
            exit = 5;
            return exit;
        }
        catch (ChildFailed e)
        {
            c.Say($"FAILED at {stage}: {e.Message}");
            exit = 1;
            return exit;
        }
        finally
        {
            WriteSeries(c, outJson, exit, stage, smoke);
        }
    }

    private sealed class ChildFailed(string message) : Exception(message);

    private sealed class Invalid355(string message) : Exception(message);

    /// <summary>#355 check as the driver applies it (see <see cref="SeriesGate.Forbidden"/>); internal for Singularity.Lab.Tests.</summary>
    internal static List<ForbiddenHit> ForbiddenIn(IEnumerable<(int Pid, string Comm, double Cpus)> procs, BenchGate g, IReadOnlySet<int> excluded, string where) =>
        SeriesGate.Forbidden(procs, g, excluded, where);

    /// <summary>The driver's #355 exclusion set: <paramref name="pid"/> and its ancestor chain read from <paramref name="procRoot"/>.</summary>
    internal static HashSet<int> ExcludedFor(int pid, string procRoot = "/proc")
    {
        var set = new HashSet<int> { pid };
        foreach (PidStat a in AncestorChain(pid, procRoot))
            set.Add(a.Pid);
        return set;
    }

    /// <summary>The ancestor chain of <paramref name="pid"/> with comm and start time (the one walk behind <see cref="ExcludedFor"/> and the driver).</summary>
    internal static List<PidStat> AncestorChain(int pid, string procRoot = "/proc") => LoadMath.Ancestors(pid, x => StatOf(x, procRoot));

    private static PidStat? StatOf(int pid, string procRoot) =>
        BenchEnv.ReadTrim(Path.Combine(procRoot, pid.ToString(CultureInfo.InvariantCulture), "stat")) is string l ? ProcParse.Stat(l) : null;

    /// <summary>
    /// #355 exclusion matching (pure): the pids of <paramref name="procs"/> that are the driver (<paramref name="selfPid"/>, matched by comm
    /// and start time when <paramref name="self"/> is known) or one of <paramref name="ancestors"/>: pid AND comm must match, and the start
    /// time where both sides have it (an ancestor start time 0 = unknown), so a reused pid cannot hide a new build.
    /// </summary>
    internal static IReadOnlySet<int> ExcludedAmong(int selfPid, PidStat? self, IReadOnlyList<PidStat> ancestors, IEnumerable<(int Pid, string Comm, long? Start)> procs)
    {
        var set = new HashSet<int>();
        foreach ((int pid, string comm, long? start) in procs)
        {
            bool isSelf = pid == selfPid && (self is not PidStat me || me.Comm == comm && (start is null || start == me.StartTime));
            bool isAnc = ancestors.Any(a => a.Pid == pid && a.Comm == comm && (start is null || a.StartTime == 0 || start == a.StartTime));
            if (isSelf || isAnc)
                set.Add(pid);
        }
        return set;
    }

    /// <summary>
    /// The pre-series step order (pure): the block is registered in <paramref name="parts"/> before <paramref name="check"/> runs, so a
    /// check that throws (#355, exit 5) leaves the block for series.json.
    /// </summary>
    internal static void RegisterThenCheck(IDictionary<string, Action<Utf8JsonWriter>> parts, string key, Action<Utf8JsonWriter> block, Action check)
    {
        parts[key] = block;
        check();
    }

    /// <summary>Writes the registered blocks of series.json in their fixed order (pure; absent blocks are skipped).</summary>
    internal static void WriteParts(Utf8JsonWriter j, IReadOnlyDictionary<string, Action<Utf8JsonWriter>> parts)
    {
        foreach (string key in PartOrder)
        {
            if (parts.TryGetValue(key, out Action<Utf8JsonWriter>? write))
                write(j);
        }
    }

    internal static readonly string[] PartOrder = ["pre_series", "fref", "validity", "main", "verdict", "reference", "profile", "post_series"];

    /// <summary>The <c>pre_series</c> block of series.json (pure given its inputs).</summary>
    internal static Action<Utf8JsonWriter> PreSeriesBlock(BenchGate g, double waitedSeconds, IReadOnlyList<double?> preTries, double? pre, MachineWindow w, LoadMeter meter)
    {
        double waited = waitedSeconds;
        double?[] tries = [.. preTries];
        return j =>
        {
            j.WriteStartObject("pre_series");
            j.WriteNumber("seconds", g.PreSeriesSeconds);
            j.WriteNumber("waited_s", Math.Round(waited, 1));
            j.WriteNumber("deadline_s", g.PreSeriesDeadlineSeconds);
            LoadMeter.WriteNum(j, "foreign_cpus", pre);
            j.WriteStartArray("foreign_cpus_tries");
            foreach (double? t in tries) { if (t is double tv) j.WriteNumberValue(tv); else j.WriteNullValue(); }
            j.WriteEndArray();
            j.WriteNumber("limit_cpus", g.PreSeriesForeignCpusMax);
            meter.Write(j, [w], 0);
            j.WriteEndObject();
        };
    }

    /// <summary>#355 hits of a driver window (CPU-active foreign processes with their mean CPUs). Off Linux there are no pids and no hits.</summary>
    private static List<ForbiddenHit> Forbidden(Ctx c, LoadMeter meter, MachineWindow w, string where)
    {
        if (w.Start.Pids is null || w.End.Pids is null)
            return [];
        long dTotal = w.End.Cpu?.Total - w.Start.Cpu?.Total ?? 0;
        List<ForeignProcess> procs = LoadMath.TopForeign(w.Start.Pids, w.End.Pids, meter.SelfPid, dTotal, int.MaxValue);
        IReadOnlySet<int> excluded = c.ExcludedAmong(procs.Select(t => (t.Pid, t.Comm, t.StartTime)));
        return SeriesGate.Forbidden(procs.Select(t => (t.Pid, t.Comm, t.MachineShare * Environment.ProcessorCount)), c.M.Gate, excluded, where);
    }

    /// <summary>Attempts, med_points and decisions of one gated set (main pass, a variant, an R mode).</summary>
    private sealed record GatedResult(Dictionary<int, List<AttemptRec>> Attempts, Dictionary<string, double> MedPoints,
        Dictionary<int, ProcessDecision> Decisions, List<(int Round, List<int> Processes)> RepeatPlan);

    private delegate (string[] Args, string File) ArgsFor(GateProcess p, int attempt);

    /// <summary>
    /// Main pass over <paramref name="ps"/> in order, then med_point (fixed), then repeat rounds 1…max_repeats in the same order (EXP-0008
    /// П5.4: repeats 1 at the end of the queue, repeats 2 after all repeats 1), each decided by <see cref="SeriesGate.Decide"/>.
    /// <paramref name="medianGate"/> false: foreign_cpu only (R modes, П9 п. 6).
    /// </summary>
    private static GatedResult Gated(Ctx c, IReadOnlyList<GateProcess> ps, ArgsFor argsFor, string tag, bool medianGate)
    {
        BenchGate g = c.M.Gate;
        var attempts = ps.ToDictionary(p => p.Index, _ => new List<AttemptRec>());
        foreach (GateProcess p in ps)
            attempts[p.Index].Add(RunAttempt(c, p, 0, argsFor, tag, medianGate));
        Dictionary<string, double> med = medianGate
            ? SeriesGate.MedPoints(ps, attempts.ToDictionary(kv => kv.Key, kv => kv.Value[0].Gate))
            : [];
        foreach ((string grp, double v) in med.OrderBy(x => x.Key, StringComparer.Ordinal))
            c.Say($"med_point {grp} = {v:F2} ms");
        var plan = new List<(int, List<int>)>();
        for (int round = 1; round <= (c.Calibration ? 0 : g.MaxRepeats); round++)
        {
            var todo = ps.Where(p => attempts[p.Index].Count == round
                && SeriesGate.Decide([.. attempts[p.Index].Select(a => a.Gate)], MedOf(med, p), g).Status == ProcessStatus.NeedsRepeat).ToList();
            plan.Add((round, [.. todo.Select(p => p.Index)]));
            c.Say($"repeat round {round}: {todo.Count} process(es) {string.Join(" ", todo.Select(p => p.Index))}");
            foreach (GateProcess p in todo)
            {
                if (g.WindowSeconds > 0 && c.Clock.Elapsed.TotalSeconds >= g.WindowSeconds)
                {
                    // ADR-0023 п. 1.9: after the #355 window no repeat is started; the missing repeat counts as an attempt flagged by (м).
                    c.VirtualRepeats++;
                    c.Say($"{tag}{p.Index}-a{round}: not started, the #355 window of {g.WindowSeconds} s ended");
                    attempts[p.Index].Add(new AttemptRec($"{tag}{p.Index}-a{round}", round, "", -1, 0, null,
                        new GateAttempt(round, null, null, null, null, Virtual: true), null, null, null, null, DateTime.UtcNow, DateTime.UtcNow));
                    continue;
                }
                attempts[p.Index].Add(RunAttempt(c, p, round, argsFor, tag, medianGate));
            }
        }
        // Calibration: one attempt per process, decided as if final (no repeats asked).
        BenchGate gd = c.Calibration ? g with { MaxRepeats = 0 } : g;
        var decisions = ps.ToDictionary(p => p.Index, p => SeriesGate.Decide([.. attempts[p.Index].Select(a => a.Gate)], MedOf(med, p), gd));
        foreach (GateProcess p in ps)
        {
            ProcessDecision d = decisions[p.Index];
            if (d.Status != ProcessStatus.Accepted || d.AcceptedAttempt != 0)
                c.Say($"{tag}{p.Index}: {d.Status}{(d.AcceptedBy is null ? "" : $" ({d.AcceptedBy}, attempt {d.AcceptedAttempt})")} after {attempts[p.Index].Count} attempt(s)");
        }
        return new GatedResult(attempts, med, decisions, plan);
    }

    private static double? MedOf(Dictionary<string, double> med, GateProcess p) => med.TryGetValue(p.Group, out double v) ? v : null;

    private static AttemptRec RunAttempt(Ctx c, GateProcess p, int n, ArgsFor argsFor, string tag, bool medianGate)
    {
        BenchGate g = c.M.Gate;
        // Wait for quiet (EXP-0008 П5.4 [Предл.]): sample, then wait in steps up to the maximum, then start in any case.
        var sw = Stopwatch.StartNew();
        string qkey = $"{tag}{p.Index}-a{n}";
        int qi = 0;
        (double? q, MachineWindow qw, LoadMeter qm) = Measure(c, g.QuietSampleSeconds, null);
        c.Check355(Forbidden(c, qm, qw, $"{qkey} quiet#{++qi}"));
        while (q is double f && f > g.ForeignCpusMax && sw.Elapsed.TotalSeconds + g.QuietStepSeconds <= g.QuietMaxSeconds + g.QuietSampleSeconds)
        {
            Thread.Sleep(TimeSpan.FromSeconds(g.QuietStepSeconds));
            (q, qw, qm) = Measure(c, g.QuietSampleSeconds, null);
            c.Check355(Forbidden(c, qm, qw, $"{qkey} quiet#{++qi}"));
        }
        double waited = sw.Elapsed.TotalSeconds;
        (string[] args, string file) = argsFor(p, n);
        string key = $"{tag}{p.Index}-a{n}";
        DateTime t0 = DateTime.UtcNow;
        int code = Child(c, [.. args, "--out", file], key);
        DateTime t1 = DateTime.UtcNow;
        if (code != 0)
            throw new ChildFailed($"{key} exited {code} (see {Rel(c, file)})");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
        JsonElement r = doc.RootElement;
        double? medT = null;
        if (medianGate && r.TryGetProperty("runs", out JsonElement runs))
        {
            foreach (JsonElement run in runs.EnumerateArray())
            {
                if (run.GetProperty("mode").GetString() == "daily")
                    medT = run.GetProperty("month_ms").GetProperty("p50").GetDouble();
            }
        }
        JsonElement? machine = r.TryGetProperty("machine", out JsonElement mm) ? mm : null;
        double? foreign = null, over = null, delay = null;
        JsonElement? thermal = null;
        if (machine is { } mx)
        {
            int cpus = mx.TryGetProperty("logical_cpus", out JsonElement lc) ? lc.GetInt32() : Environment.ProcessorCount;
            foreign = Num(mx, "foreign_cpu_window") * cpus;
            delay = Num(mx, "run_delay_share_window");
            // (н1) seconds share recomputed with the manifest's per-second level from the raw series (fraction of the machine).
            if (mx.TryGetProperty("foreign_cpu_series", out JsonElement fs) && fs.GetArrayLength() > 0)
                over = fs.EnumerateArray().Count(x => x.GetDouble() * cpus > g.ForeignSecondCpus) / (double)fs.GetArrayLength();
            thermal = ThermalOf(mx);
            if (mx.TryGetProperty("top_foreign_window", out JsonElement tf) && tf.ValueKind == JsonValueKind.Array)
            {
                // The child's top-5 over its windows; "cores" = mean CPUs of the process over the child's union window.
                var procs = tf.EnumerateArray().Select(x => (Pid: x.GetProperty("pid").GetInt32(), Comm: x.GetProperty("comm").GetString() ?? "",
                    Start: x.TryGetProperty("start_time", out JsonElement stt) && stt.ValueKind == JsonValueKind.Number ? stt.GetInt64() : (long?)null,
                    Cpus: x.TryGetProperty("cores", out JsonElement cr) ? cr.GetDouble() : 0)).ToList();
                IReadOnlySet<int> excluded = c.ExcludedAmong(procs.Select(x => (x.Pid, x.Comm, x.Start)));
                c.Check355(SeriesGate.Forbidden(procs.Select(x => (x.Pid, x.Comm, x.Cpus)), g, excluded, $"{tag}{p.Index}-a{n}"));
            }
        }
        JsonElement? top = machine is { } m3 && m3.TryGetProperty("top_foreign_window", out JsonElement t) ? t.Clone() : null;
        double? topk = null;
        bool? thermalFlag = null;
        if (r.TryGetProperty("frequency", out JsonElement fr))
        {
            topk = Num(fr, "topk_median_mhz");
            thermalFlag = fr.TryGetProperty("possible_thermal_throttle", out JsonElement th) && th.ValueKind is JsonValueKind.True or JsonValueKind.False ? th.GetBoolean() : null;
        }
        bool injected = tag == "p" && c.Injected.TryGetValue(p.Index, out int upTo) && n < upTo;
        var ga = new GateAttempt(n, medT, foreign, over, delay, injected);
        JsonElement? gcRec = GcOf(r);
        string gcSay = gcRec is { } gx ? $", GC pause max {Fmt(Num(gx, "pause_max_ms"))} ms, blocking gen2 {(Num(gx, "gen2_blocking_after_warmup") is double bg ? I((int)bg) : "n/a")}" : "";
        c.Say($"{key}: T p50 {Fmt(medT)} ms, н1 {Fmt(foreign)} CPUs / {Fmt(over)} s>1, н2 {Fmt(delay)}, waited {waited:F0} s{(injected ? ", injected flag" : "")}{gcSay}");
        return new AttemptRec(key, n, file, code, waited, q, ga, top, topk, thermalFlag, thermal, t0, t1, gcRec);
    }

    private static (string[] Args, string File) ProcessArgs(Ctx c, BenchProcess p, int attempt, string? reference, IReadOnlyList<(string Key, string Value)> switches)
    {
        int k = p.Threads + p.Background;
        var args = new List<string> { "bench", "--mode", "process", "--manifest", c.M.Path, "--process", I(p.Index), "--k", I(k),
            "--thermal-ratio", D(c.M.Gate.ThermalRatio), "--attempt", I(attempt) };
        args.AddRange(c.FRef.TryGetValue(k, out double fr) ? ["--fref", D(fr)] : ["--no-fref", "true"]);
        if (reference is not null)
            args.AddRange(["--reference", reference]);
        args.AddRange(Flat(switches));
        string name = reference is null ? $"p{p.Index:000}-a{attempt}.json" : $"{reference}-s{p.Seed}-a{attempt}.json";
        return ([.. args], Path.Combine(c.Dir, reference is null ? "attempts" : "reference", name));
    }

    /// <summary>The П6 profile process under the sampling tool; "missing_tool" when the tool is not found (the series does not fail).</summary>
    private static (string Status, string? File) Profile(Ctx c, BenchProfile prof, IReadOnlyList<BenchProcess> plan)
    {
        string? tool = FindTool(prof.Tool);
        if (tool is null)
        {
            c.Say($"profile: {prof.Tool} not found on PATH or in ~/.dotnet/tools — not run");
            return ("missing_tool", null);
        }
        BenchProcess bp = plan.Where(p => p.Block == prof.Block && p.Seed == prof.Seed).OrderBy(p => p.Replica).First();
        int k = bp.Threads + bp.Background;
        string file = Path.Combine(c.Dir, "reference", "profile.json");
        string trace = Path.Combine(c.Dir, "reference", "profile.nettrace");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        (string exe, string[] pre) = SelfCommand();
        string[] lab = [exe, .. pre, "bench", "--mode", "process", "--manifest", c.M.Path, "--process", I(bp.Index), "--fref", D(c.FRef[k]), "--k", I(k),
            "--warmup", I(prof.Warmup), "--measure", I(prof.Measure), "--runs", prof.Runs, "--reference", "profile", "--out", file, .. AllowDebug(c)];
        int code = Exec(c, tool, [.. prof.ToolArgs, "-o", trace, "--", .. lab], "profile");
        c.Say($"profile: {prof.Tool} exit {code}, trace {Rel(c, trace)}");
        return (code == 0 ? "done" : $"exit {code}", file);
    }

    private static string? FindTool(string name)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        IEnumerable<string> dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Append(Path.Combine(home, ".dotnet", "tools"));
        foreach (string d in dirs.Where(d => d.Length > 0))
        {
            foreach (string n in (string[])[name, name + ".exe"])
            {
                string p = Path.Combine(d, n);
                if (File.Exists(p))
                    return p;
            }
        }
        return null;
    }

    // ---------------- machine windows of the driver ----------------

    /// <summary>
    /// foreign_cpu of the next <paramref name="seconds"/> seen from the driver (its own process idles meanwhile). The meter is stopped before
    /// it is returned (its records stay readable for <see cref="LoadMeter.Write"/>), so no sampler thread of the driver runs during a child.
    /// </summary>
    private static (double?, MachineWindow, LoadMeter) Measure(Ctx c, int seconds, string? name)
    {
        // Quiet windows (name null) need no per-second series, but they read the pids: the #355 check covers every quiet window.
        var meter = new LoadMeter(background: name is not null);
        MachineSnapshot s0 = meter.Snapshot();
        Thread.Sleep(TimeSpan.FromSeconds(seconds));
        var w = new MachineWindow(name ?? "quiet", s0, meter.Snapshot());
        meter.Dispose();
        if (name is not null && !meter.Available)
            c.Say($"load meter unavailable: {meter.Reason}");
        return (LoadMeter.ForeignCpu([w]) * Environment.ProcessorCount, w, meter);
    }

    // ---------------- children ----------------

    private static (string Exe, string[] Prefix) SelfCommand() => SelfInvocation.Current();

    private static string[] AllowDebug(Ctx c) => c.AllowDebug ? ["--allow-debug", "true"] : [];

    private static int Child(Ctx c, string[] args, string label)
    {
        (string exe, string[] pre) = SelfCommand();
        return Exec(c, exe, [.. pre, .. args, .. AllowDebug(c)], label);
    }

    private static int Exec(Ctx c, string exe, string[] args, string label)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string a in args)
            psi.ArgumentList.Add(a);
        string logFile = Path.Combine(c.Dir, "logs", label + ".log");
        Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
        using var p = Process.Start(psi) ?? throw new ChildFailed($"{label}: could not start {exe}");
        // Both pipes are drained concurrently so a full stderr cannot block the child.
        Task<string> err = p.StandardError.ReadToEndAsync();
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        File.WriteAllText(logFile, $"$ {exe} {string.Join(' ', args)}\n{output}{err.Result}");
        return p.ExitCode;
    }

    private static IEnumerable<string> Flat(IReadOnlyList<(string Key, string Value)> args) => args.SelectMany(a => (string[])["--" + a.Key, a.Value]);

    // ---------------- output ----------------

    /// <summary>The med_point group: (block, point, threads, background) — see <see cref="GateProcess.Group"/>.</summary>
    internal static string Group(BenchProcess p) => $"{p.Block}|{p.Point.Name}|t{p.Threads}|bg{p.Background}";

    private static void WriteSeries(Ctx c, Dictionary<string, Action<Utf8JsonWriter>> parts, int exit, string stage, bool smoke)
    {
        using var json = BenchJson.Open(Path.Combine(c.Dir, "series.json"));
        Utf8JsonWriter j = json.Writer;
        j.WriteStartObject();
        j.WriteString("kind", "lab-bench/series");
        j.WriteNumber("format", 1);
        BenchEnv.Write(j, BenchCommand.RepoDir());
        j.WriteString("experiment", c.M.Experiment);
        j.WriteString("manifest", c.M.Path);
        j.WriteString("manifest_hash", c.M.Hash);
        j.WriteBoolean("smoke", smoke);
        j.WriteStartObject("injected_flags");
        foreach ((int k, int n) in c.Injected.OrderBy(x => x.Key))
            j.WriteNumber(I(k), n);
        j.WriteEndObject();
        c.M.Gate.Write(j);
        j.WriteBoolean("calibration", c.Calibration);
        j.WriteNumber("elapsed_s", Math.Round(c.Clock.Elapsed.TotalSeconds, 1));
        j.WriteNumber("repeats_not_started_window_end", c.VirtualRepeats);
        if (c.Invalid355 is null) j.WriteNull("invalid_355"); else j.WriteString("invalid_355", c.Invalid355);
        j.WriteStartObject("check_355");
        j.WriteNumber("driver_pid", Environment.ProcessId);
        j.WriteStartArray("excluded_ancestors");
        foreach (PidStat a in c.Ancestors)
        {
            j.WriteStartObject();
            j.WriteNumber("pid", a.Pid);
            j.WriteString("comm", a.Comm);
            j.WriteNumber("ppid", a.Ppid);
            j.WriteNumber("start_time", a.StartTime);
            j.WriteEndObject();
        }
        j.WriteEndArray();
        j.WriteNumber("forbidden_min_cpus", c.M.Gate.ForbiddenMinCpus);
        j.WriteStartArray("seen");
        foreach (ForbiddenHit h in c.Seen355)
        {
            j.WriteStartObject();
            j.WriteString("where", h.Where);
            j.WriteNumber("pid", h.Pid);
            j.WriteString("comm", h.Comm);
            j.WriteNumber("cpus", Math.Round(h.Cpus, 4));
            j.WriteBoolean("fatal", h.Fatal);
            j.WriteEndObject();
        }
        j.WriteEndArray();
        j.WriteEndObject();
        j.WriteNumber("exit_code", exit);
        j.WriteString("exit_meaning", exit switch
        {
            0 => "series done", 1 => "process failure", 3 => "not started (no quiet before the deadline)", 4 => "INCONCLUSIVE by exclusions",
            5 => "INVALID (б): #355 (another build or agent on the machine)", _ => "?",
        });
        j.WriteString("last_stage", stage);
        WriteParts(j, parts);
        j.WriteEndObject();
    }

    private static void WriteGated(Utf8JsonWriter j, string name, IReadOnlyList<GateProcess> ps, GatedResult r, Ctx c, IReadOnlyList<BenchProcess>? plan)
    {
        j.WriteStartObject(name);
        j.WriteStartObject("med_point_ms");
        foreach ((string grp, double v) in r.MedPoints.OrderBy(x => x.Key, StringComparer.Ordinal))
            j.WriteNumber(grp, v);
        j.WriteEndObject();
        j.WriteStartArray("repeat_plan");
        foreach ((int round, List<int> procs) in r.RepeatPlan)
        {
            j.WriteStartObject();
            j.WriteNumber("round", round);
            j.WriteStartArray("processes");
            foreach (int p in procs) j.WriteNumberValue(p);
            j.WriteEndArray();
            j.WriteEndObject();
        }
        j.WriteEndArray();
        j.WriteStartArray("processes");
        foreach (GateProcess p in ps)
        {
            ProcessDecision d = r.Decisions[p.Index];
            j.WriteStartObject();
            j.WriteNumber("index", p.Index);
            j.WriteString("block", p.Block);
            j.WriteString("group", p.Group);
            j.WriteNumber("seed", p.Seed);
            j.WriteNumber("replica", p.Replica);
            if (plan is not null)
                j.WriteString("point", plan[p.Index].Point.Name);
            j.WriteString("status", d.Status.ToString());
            if (d.AcceptedAttempt is int aa) j.WriteNumber("accepted_attempt", aa); else j.WriteNull("accepted_attempt");
            if (d.AcceptedBy is null) j.WriteNull("accepted_by"); else j.WriteString("accepted_by", d.AcceptedBy);
            j.WriteStartArray("attempts");
            List<AttemptRec> atts = r.Attempts[p.Index];
            for (int i = 0; i < atts.Count; i++)
            {
                AttemptRec a = atts[i];
                AttemptEval e = d.Evals.Count > i ? d.Evals[i] : SeriesGate.Evaluate(a.Gate, MedOf(r.MedPoints, p), c.M.Gate);
                j.WriteStartObject();
                j.WriteNumber("attempt", a.Number);
                j.WriteString("file", a.File.Length == 0 ? "" : Rel(c, a.File));
                j.WriteString("started_utc", a.Started.ToString("O", CultureInfo.InvariantCulture));
                j.WriteString("ended_utc", a.Ended.ToString("O", CultureInfo.InvariantCulture));
                j.WriteNumber("wait_quiet_s", Math.Round(a.WaitQuietSeconds, 1));
                LoadMeter.WriteNum(j, "quiet_foreign_cpu_last", a.QuietForeign);
                LoadMeter.WriteNum(j, "median_T_ms", a.Gate.MedianTMs);
                LoadMeter.WriteNum(j, "foreign_cpus", a.Gate.ForeignCpus);
                LoadMeter.WriteNum(j, "foreign_over_share", a.Gate.ForeignOverShare);
                LoadMeter.WriteNum(j, "run_delay_share", a.Gate.RunDelay);
                j.WriteBoolean("injected", a.Gate.Injected);
                j.WriteBoolean("virtual", a.Gate.Virtual);
                if (a.Thermal is JsonElement th) { j.WritePropertyName("thermal"); th.WriteTo(j); } else j.WriteNull("thermal");
                LoadMeter.WriteNum(j, "topk_median_mhz", a.TopkMhz);
                if (a.ThermalFlag is bool tf) j.WriteBoolean("possible_thermal_throttle", tf); else j.WriteNull("possible_thermal_throttle");
                j.WriteBoolean("flag", e.Flag);
                j.WriteBoolean("evidence", e.Evidence);
                j.WriteStartArray("reasons");
                foreach (string s in e.Reasons) j.WriteStringValue(s);
                j.WriteEndArray();
                if (a.TopForeign is JsonElement top) { j.WritePropertyName("top_foreign"); top.WriteTo(j); }
                else j.WriteNull("top_foreign");
                // Decision #416: the child's GC pause summary (record only; the gate does not use it).
                if (a.Gc is JsonElement gce) { j.WritePropertyName("gc"); gce.WriteTo(j); } else j.WriteNull("gc");
                j.WriteEndObject();
            }
            j.WriteEndArray();
            j.WriteEndObject();
        }
        j.WriteEndArray();
        j.WriteEndObject();
    }

    private static void WriteVerdict(Utf8JsonWriter j, SeriesVerdict v)
    {
        j.WriteStartObject("exclusion_verdict");
        j.WriteBoolean("inconclusive", v.Inconclusive);
        j.WriteStartArray("reasons");
        foreach (string s in v.Reasons) j.WriteStringValue(s);
        j.WriteEndArray();
        j.WriteNumber("excluded", v.Excluded);
        j.WriteNumber("processes", v.Processes);
        j.WriteNumber("outcome_excluded", v.OutcomeExcluded);
        j.WriteNumber("outcome_processes", v.OutcomeProcesses);
        j.WriteStartObject("outcome_seeds");
        foreach ((ulong seed, (int acc, int tot)) in v.OutcomeSeeds)
        {
            j.WriteStartObject(seed.ToString(CultureInfo.InvariantCulture));
            j.WriteNumber("accepted", acc);
            j.WriteNumber("total", tot);
            j.WriteEndObject();
        }
        j.WriteEndObject();
        j.WriteString("note", "noise (ADR-0023 п. 1.7, first condition) is computed by lab/analysis/exp0008.py, not here");
        j.WriteEndObject();
    }

    private static string Rel(Ctx c, string f) => Path.GetRelativePath(c.Dir, f);

    private static double? Num(JsonElement e, string key) =>
        e.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static double? ReadDouble(string file, string key)
    {
        if (!File.Exists(file))
            return null;
        using JsonDocument d = JsonDocument.Parse(File.ReadAllText(file));
        return Num(d.RootElement, key);
    }

    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);
    private static string D(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static string Fmt(double? v) => v is double d ? d.ToString("F3", CultureInfo.InvariantCulture) : "n/a";
}
