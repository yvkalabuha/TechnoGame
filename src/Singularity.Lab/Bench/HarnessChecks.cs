using System.Text;
using System.Text.Json;

namespace Singularity.Lab.Bench;

/// <summary>
/// Checks of the EXP-0008 harness (TASK-0106) run by <c>Singularity.Lab selftest</c>: /proc parsers on fixtures, load arithmetic, top-k
/// frequency, the load meter on a fixture /proc tree and without /proc, the series gate scenarios of ADR-0023 «Проверка», the manifest
/// gate defaults. Each returns null or the problem. No timing.
/// </summary>
public static class HarnessChecks
{
    public static IEnumerable<(string Name, Func<string?> Body)> All(string tmp) =>
    [
        ("bench: /proc/stat cpu line — busy = user+nice+system+irq+softirq+steal, total adds idle+iowait; short or bad line → null", ProcStat),
        ("bench: /proc/[pid]/stat — comm with spaces and ')' , utime+stime, starttime; loadavg; schedstat wait; nonvoluntary switches", PidStatFixtures),
        ("bench: foreign_cpu, top-k mean, top-5 foreign (own pid out, pid reuse by start time), run-delay share", LoadMathFixtures),
        ("bench: top-k frequency median in windows; /proc/cpuinfo «cpu MHz» lines; thermal flag only with both values", FreqFixtures),
        ("bench: LoadMeter on a fixture /proc tree writes the machine block; without /proc it is available:false and does not throw", () => MeterFixtures(tmp)),
        ("bench: series gate (ADR-0023 п. 1.3–1.5, 1.9) — н1/н2/м, exclusion only with н1|н2 on every attempt, window-end repeats, fixed med_point, #355 names/ancestors/idle", GateDecide),
        ("bench: series verdict — A 4/15 excluded INCONCLUSIVE, 3/15 not; seed 2/5 INCONCLUSIVE; series 23/114 INCONCLUSIVE, 22/114 not", GateVerdict),
        ("bench: EXP-0008 manifest — gate equals the П5 numbers, plan = EXP-0007 plan, reference modes parse; bad gate key rejected", Manifests),
    ];

    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    private static string? ProcStat()
    {
        const string stat = "cpu  100 5 50 1000 20 3 2 1 7 0\ncpu0 50 2 25 500 10 1 1 0 0 0\nintr 1 2 3\n";
        if (ProcParse.CpuLine(B(stat)) is not CpuTotals c || c.Busy != 100 + 5 + 50 + 3 + 2 + 1 || c.Total != c.Busy + 1000 + 20)
            return $"cpu line: {ProcParse.CpuLine(B(stat))}";
        if (ProcParse.CpuLine(B("cpu0 1 2 3 4\n")) is not null)
            return "per-CPU line taken as the aggregate";
        if (ProcParse.CpuLine(B("cpu  1 2 x\n")) is not null)
            return "malformed line parsed";
        // Old kernels: 4 fields only.
        return ProcParse.CpuLine(B("cpu  10 0 5 100\n")) is CpuTotals o && o.Busy == 15 && o.Total == 115 ? null : "4-field line";
    }

    private static string? PidStatFixtures()
    {
        // comm "(a b) c)" — spaces and a ')' inside; fields after it: state … utime(14)=111 stime(15)=22 … starttime(22)=999.
        string line = "4242 (a b) c)) S 1 4242 4242 0 -1 4194560 100 0 0 0 111 22 0 0 20 0 3 0 999 1000000 300 18446744073709551615";
        if (ProcParse.Stat(line) is not PidStat p || p.Pid != 4242 || p.Comm != "a b) c)" || p.CpuTicks != 133 || p.StartTime != 999)
            return $"stat: {ProcParse.Stat(line)}";
        if (ProcParse.StatCpuTicks(B(line)) != 133 || ProcParse.StatPid(B(line)) != 4242)
            return "span stat parser";
        if (ProcParse.Stat("4242 (x) S 1 2") is not null || ProcParse.StatCpuTicks(B("no paren")) is not null)
            return "short stat parsed";
        if (ProcParse.Loadavg("1.50 0.75 0.25 3/1234 5678\n") is not { } l || l.L1 != 1.5 || l.L15 != 0.25 || l.Running != 3 || l.Tasks != 1234)
            return "loadavg";
        if (ProcParse.SchedstatWaitNs("604888 1924 8\n") != 1924 || ProcParse.SchedstatWaitNs("1") is not null)
            return "schedstat";
        const string status = "Name:\tx\nvoluntary_ctxt_switches:\t10\nnonvoluntary_ctxt_switches:\t7\n";
        return ProcParse.NonvoluntarySwitches(status) == 7 && ProcParse.NonvoluntarySwitches("Name: x") is null ? null : "status";
    }

    private static string? LoadMathFixtures()
    {
        // 16 CPUs × 100 ticks: own process 300 ticks busy, machine 400 → foreign 100 / 1600.
        if (LoadMath.ForeignCpu(400, 1600, 300) is not double f || Math.Abs(f - 0.0625) > 1e-12 || LoadMath.ForeignCpu(1, 0, 0) is not null)
            return "foreign_cpu";
        if (LoadMath.TopKMean([1000, 4000, 3000, 800], 2) != 3500 || LoadMath.TopKMean([5], 3) != 5 || LoadMath.TopKMean([], 1) is not null)
            return "top-k mean";
        PidStat[] start = [new(1, "self", 10, 1), new(2, "ide", 100, 5), new(3, "old", 50, 6), new(5, "av", 0, 9)];
        // pid 3 was reused (other start time) → counts all its time; pid 4 started inside the window; pid 1 is self.
        PidStat[] end = [new(1, "self", 500, 1), new(2, "ide", 160, 5), new(3, "new", 7, 77), new(4, "browser", 40, 80), new(5, "av", 60, 9)];
        List<ForeignProcess> top = LoadMath.TopForeign(start, end, selfPid: 1, dTotal: 1000, n: 3);
        string got = string.Join(",", top.Select(t => $"{t.Pid}:{t.CpuTicks}"));
        if (got != "2:60,5:60,4:40" || Math.Abs(top[0].MachineShare - 0.06) > 1e-12)
            return $"top foreign {got}";
        var merged = LoadMath.MergeTop([top, [new ForeignProcess(4, "browser", 30, 0)]], 2000, 2);
        if (string.Join(",", merged.Select(t => $"{t.Pid}:{t.CpuTicks}")) != "4:70,2:60")
            return "merge top";
        var w0 = new Dictionary<int, long> { [10] = 100, [11] = 0 };
        var w1 = new Dictionary<int, long> { [10] = 600, [11] = 500, [12] = 1000 };
        // (500 + 500 + 1000) ns over 1000 ns × 2 threads.
        return LoadMath.RunDelayShare(w0, w1, 1000, 2) is double d && Math.Abs(d - 1.0) < 1e-12 && LoadMath.RunDelayShare(w0, w1, 0, 2) is null
            ? null : "run delay";
    }

    private static string? FreqFixtures()
    {
        // Powersave picture of EXP-0007: 2 busy cores at 4.5 GHz, 14 idle at 0.8 GHz. The all-CPU mean says 1.26 GHz; top-2 says 4.5 GHz.
        double[] busy = [.. Enumerable.Repeat(800.0, 14), 4500, 4500];
        double[] hot = [.. Enumerable.Repeat(800.0, 14), 3000, 3000];
        (long, double[])[] samples = [(10, busy), (20, busy), (30, hot), (99, hot)];
        (long, long)[] window = [(0, 35)];
        double? top2 = FreqSampler.MedianOf(samples, window, v => LoadMath.TopKMean(v, 2) ?? double.NaN);
        double? mean = FreqSampler.MedianOf(samples, window, v => v.Average());
        if (top2 != 4500 || mean is not double m || Math.Abs(m - (14 * 800 + 9000) / 16.0) > 1e-9)
            return $"top-k {top2}, mean {mean}";
        if (FreqSampler.MedianOf(samples, [(40, 50)], v => v.Average()) is not null)
            return "empty window not null";
        Span<double> row = stackalloc double[4];
        int n = FreqSampler.CpuinfoMhz(B("processor\t: 0\ncpu MHz\t\t: 3200.5\nprocessor\t: 1\ncpu MHz\t\t: 800.000\n"), row);
        if (n != 2 || row[0] != 3200.5 || row[1] != 800)
            return "cpuinfo";
        return LoadMath.PossibleThermalThrottle(3000, 4000, 0.85) == true && LoadMath.PossibleThermalThrottle(3500, 4000, 0.85) == false
            && LoadMath.PossibleThermalThrottle(null, 4000, 0.85) is null ? null : "thermal flag";
    }

    private static string? MeterFixtures(string tmp)
    {
        string root = Path.Combine(tmp, "bench-proc");
        Directory.CreateDirectory(Path.Combine(root, "self", "task", "7"));
        Directory.CreateDirectory(Path.Combine(root, "900"));
        File.WriteAllText(Path.Combine(root, "stat"), "cpu  100 0 50 1000 0 0 0 0 0 0\n");
        string self = "77 (lab) R 1 77 77 0 -1 0 0 0 0 0 30 10 0 0 20 0 9 0 5 0 0 0";
        File.WriteAllText(Path.Combine(root, "self", "stat"), self);
        File.WriteAllText(Path.Combine(root, "loadavg"), "0.50 0.40 0.30 1/100 200\n");
        File.WriteAllText(Path.Combine(root, "self", "task", "7", "schedstat"), "10 2000 3\n");
        File.WriteAllText(Path.Combine(root, "self", "task", "7", "status"), "nonvoluntary_ctxt_switches:\t4\n");
        File.WriteAllText(Path.Combine(root, "900", "stat"), "900 (ide) S 1 900 900 0 -1 0 0 0 0 0 5 5 0 0 20 0 1 0 3 0 0 0");
        using var meter = new LoadMeter(root, background: false);
        if (!meter.Available || meter.SelfPid != 77)
            return $"fixture meter: available {meter.Available}, pid {meter.SelfPid}, {meter.Reason}";
        MachineSnapshot s0 = meter.Snapshot();
        File.WriteAllText(Path.Combine(root, "stat"), "cpu  300 0 100 1200 0 0 0 0 0 0\n");          // busy +250, total +450
        File.WriteAllText(Path.Combine(root, "self", "stat"), self.Replace(" 30 10 ", " 130 60 "));  // self +150
        File.WriteAllText(Path.Combine(root, "900", "stat"), "900 (ide) S 1 900 900 0 -1 0 0 0 0 0 55 5 0 0 20 0 1 0 3 0 0 0");
        File.WriteAllText(Path.Combine(root, "self", "task", "7", "schedstat"), "10 5000 3\n");
        MachineSnapshot s1 = meter.Snapshot();
        var win = new MachineWindow("w", s0, s1);
        if (LoadMeter.ForeignCpu([win]) is not double f || Math.Abs(f - 100.0 / 450) > 1e-12)
            return $"fixture foreign_cpu {LoadMeter.ForeignCpu([win])}";
        using var ms = new MemoryStream();
        using (var j = new Utf8JsonWriter(ms))
        {
            j.WriteStartObject();
            meter.Write(j, [win], 1);
            j.WriteEndObject();
        }
        using JsonDocument doc = JsonDocument.Parse(ms.ToArray());
        JsonElement m = doc.RootElement.GetProperty("machine");
        JsonElement w0 = m.GetProperty("windows")[0];
        if (!m.GetProperty("available").GetBoolean() || w0.GetProperty("loadavg_end").GetString() != "0.50 0.40 0.30 1/100 200"
            || w0.GetProperty("run_delay_ns").GetInt64() != 3000 || w0.GetProperty("top_foreign")[0].GetProperty("comm").GetString() != "ide"
            || w0.GetProperty("top_foreign")[0].GetProperty("cpu_ticks").GetInt64() != 50)
            return "fixture machine block";

        using var none = new LoadMeter(Path.Combine(tmp, "no-such-proc"), background: false);
        if (none.Available || none.Reason is null)
            return "missing /proc reported available";
        MachineSnapshot n0 = none.Snapshot(), n1 = none.Snapshot();
        using var ms2 = new MemoryStream();
        using (var j = new Utf8JsonWriter(ms2))
        {
            j.WriteStartObject();
            none.Write(j, [new MachineWindow("w", n0, n1)], 1);
            j.WriteEndObject();
        }
        using JsonDocument d2 = JsonDocument.Parse(ms2.ToArray());
        JsonElement m2 = d2.RootElement.GetProperty("machine");
        return !m2.GetProperty("available").GetBoolean() && m2.GetProperty("foreign_cpu_window").ValueKind == JsonValueKind.Null
            ? null : "unavailable machine block";
    }

    private static readonly BenchGate G = new();

    private static int? PidStatOf(string line) => ProcParse.Stat(line)?.Ppid;

    private static string? GateDecide()
    {
        // Frequency and temperature are not inputs of GateAttempt at all; a clean first attempt is accepted with no repeat.
        var clean = SeriesGate.Decide([new GateAttempt(0, 300, 0.1, 0.0, 0.0)], 300, G);
        if (clean.Status != ProcessStatus.Accepted || clean.AcceptedAttempt != 0)
            return "clean attempt not accepted";
        // (н1) on all three attempts (mean CPUs, or seconds above 1 CPU): excluded under both rules; no 4th attempt.
        GateAttempt[] loaded = [new(0, 300, 2.0, 0, 0), new(1, 300, 0.1, 0.5, 0), new(2, 300, 1.5, 0, 0)];
        if (SeriesGate.Decide(loaded[..2], 300, G).Status != ProcessStatus.NeedsRepeat)
            return "2 flagged attempts must ask for a repeat";
        if (SeriesGate.Decide(loaded, 300, G).Status != ProcessStatus.Excluded || SeriesGate.Decide(loaded, 300, G with { ExclusionRule = ExclusionRule.AnyFlag }).Status != ProcessStatus.Excluded)
            return "3 (н1)-flagged attempts not excluded";
        try
        {
            SeriesGate.Decide([.. loaded, new GateAttempt(3, 300, 2.0, 0, 0)], 300, G);
            return "a 4th attempt was accepted as input";
        }
        catch (ArgumentException) { }
        // (м) only: any-flag excludes; independent-evidence (ADR-0023 п. 1.5) keeps the process with its first attempt.
        GateAttempt[] slow = [new(0, 400, 0.1, 0, 0), new(1, 380, 0.0, 0, 0), new(2, 390, 0.2, 0, 0)];
        if (SeriesGate.Decide(slow, 300, G with { ExclusionRule = ExclusionRule.AnyFlag }).Status != ProcessStatus.Excluded)
            return "any-flag: (м)-only not excluded";
        ProcessDecision ie = SeriesGate.Decide(slow, 300, G);
        if (ie.Status != ProcessStatus.Accepted || ie.AcceptedAttempt != 0 || ie.AcceptedBy != "no-evidence")
            return $"independent-evidence: {ie.Status} {ie.AcceptedAttempt} {ie.AcceptedBy}";
        // Mixed: (н1), then (м) only, then (н2): accepted with attempt 1, the first without independent evidence.
        GateAttempt[] mixed = [new(0, 300, 2.0, 0, 0), new(1, 400, 0.1, 0, 0), new(2, 300, 0.1, 0, 0.3)];
        ProcessDecision mx = SeriesGate.Decide(mixed, 300, G);
        if (mx.Status != ProcessStatus.Accepted || mx.AcceptedAttempt != 1)
            return $"mixed: {mx.Status} {mx.AcceptedAttempt}";
        // (н2) alone is a flag (п. 1.4) and evidence; on every attempt it excludes.
        if (SeriesGate.Decide([new GateAttempt(0, 300, 0.1, 0, 0.5)], 300, G).Status != ProcessStatus.NeedsRepeat)
            return "(н2) alone did not trigger a repeat";
        GateAttempt[] delayed = [new(0, 300, 0.1, 0, 0.2), new(1, 300, 0.0, 0, 0.3), new(2, 300, 0.2, 0, 0.1)];
        if (SeriesGate.Decide(delayed, 300, G).Status != ProcessStatus.Excluded)
            return "(н2) on every attempt not excluded";
        // The (м) criterion is off below median_gate_min_ms (small D points).
        if (SeriesGate.Decide([new GateAttempt(0, 40, 0.0, 0, 0)], 20, G).Status != ProcessStatus.Accepted)
            return "(м) applied below 50 ms";
        // #355 window end (п. 1.9): a virtual repeat is (м) without evidence → not excluded; first real attempt without evidence, else the first.
        GateAttempt[] cut = [new(0, 300, 2.0, 0, 0), new(1, 300, 0.1, 0, 0.9), new(2, null, null, null, null, Virtual: true)];
        ProcessDecision cd = SeriesGate.Decide(cut, 300, G);
        if (cd.Status != ProcessStatus.Accepted || cd.AcceptedAttempt != 0 || cd.AcceptedBy != "window-end")
            return $"window end: {cd.Status} {cd.AcceptedAttempt} {cd.AcceptedBy}";
        if (SeriesGate.Decide([new GateAttempt(0, 300, 2.0, 0, 0), new GateAttempt(1, null, null, null, null, Virtual: true)], 300, G).Status != ProcessStatus.Accepted)
            return "window end after one repeat must be final";
        // med_point is the median of first attempts and does not move with repeats.
        GateProcess[] ps = [new(0, "A", "g", 1, 1), new(1, "A", "g", 1, 2), new(2, "A", "g", 2, 1)];
        var first = new Dictionary<int, GateAttempt> { [0] = new(0, 100, 0, 0, 0), [1] = new(0, 200, 0, 0, 0), [2] = new(0, 900, 0, 0, 0) };
        var med = SeriesGate.MedPoints(ps, first);
        if (med["g"] != 200)
            return $"med_point {med["g"]}";
        ProcessDecision rep = SeriesGate.Decide([first[2], new GateAttempt(1, 150, 0, 0, 0)], med["g"], G);
        if (rep.Status != ProcessStatus.Accepted || rep.AcceptedAttempt != 1)
            return "repeat against fixed med_point";
        // #355 names: prefix, case-insensitive, the driver's own pid skipped.
        // #355: prefix, case-insensitive; the driver (99) and its ancestors (claude 50 ← bash 40) skipped; below forbidden_min_cpus recorded, not fatal.
        var stats = new Dictionary<int, PidStat> { [99] = new(99, "dotnet", 0, 0, 40), [40] = new(40, "bash", 0, 0, 50), [50] = new(50, "claude", 0, 0, 1), [1] = new(1, "systemd", 0, 0, 0) };
        List<PidStat> anc = LoadMath.Ancestors(99, x => stats.TryGetValue(x, out PidStat ps) ? ps : null);
        if (string.Join(",", anc.Select(x => x.Pid)) != "40,50,1")
            return $"ancestors {string.Join(",", anc.Select(x => x.Pid))}";
        var excluded = new HashSet<int>([99, .. anc.Select(x => x.Pid)]);
        List<ForbiddenHit> hits = SeriesGate.Forbidden([(10, "dotnet", 1.0), (11, "VBCSCompiler", 0.5), (12, "firefox", 2.0), (13, "Claude", 0.3),
            (14, "node", 0.01), (50, "claude", 3.0), (99, "dotnet", 4.0)], G, excluded, "t");
        string got = string.Join(",", hits.Select(h => $"{h.Pid}{(h.Fatal ? "!" : "")}"));
        if (PidStatOf("50 (claude) S 40 50 50 0 -1 0 0 0 0 0 1 1 0 0 20 0 1 0 7 0 0 0") != 40)
            return "ppid field";
        return got == "10!,11!,13!,14" ? null : $"#355 hits {got}";
    }

    private static string? GateVerdict()
    {
        static (List<GateProcess>, Dictionary<int, ProcessDecision>) Series(int aExcluded, int otherN, int otherExcluded, int[]? excludedSeeds = null)
        {
            var ps = new List<GateProcess>();
            var d = new Dictionary<int, ProcessDecision>();
            var acc = new ProcessDecision(ProcessStatus.Accepted, 0, "clean", []);
            var exc = new ProcessDecision(ProcessStatus.Excluded, null, null, []);
            int[] seedsOfExcluded = excludedSeeds ?? [1, 2, 3, 1, 2, 3];
            var left = seedsOfExcluded.Take(aExcluded).GroupBy(s => s).ToDictionary(x => x.Key, x => x.Count());
            for (ulong seed = 1; seed <= 3; seed++)
            {
                for (int r = 1; r <= 5; r++)
                {
                    int i = ps.Count;
                    ps.Add(new GateProcess(i, "A", "A", seed, r));
                    bool ex = left.TryGetValue((int)seed, out int k) && k > 0;
                    if (ex) left[(int)seed] = k - 1;
                    d[i] = ex ? exc : acc;
                }
            }
            for (int o = 0; o < otherN; o++)
            {
                int i = ps.Count;
                ps.Add(new GateProcess(i, "B", "B", 1, 1));
                d[i] = o < otherExcluded ? exc : acc;
            }
            return (ps, d);
        }
        var (p4, d4) = Series(4, 99, 0);
        if (!SeriesGate.Verdict(p4, d4, G).Inconclusive)
            return "4 of 15 in A not INCONCLUSIVE";
        var (p3, d3) = Series(3, 99, 0);
        if (SeriesGate.Verdict(p3, d3, G).Inconclusive)
            return "3 of 15 in A (one per seed) INCONCLUSIVE";
        var (ps, ds) = Series(3, 99, 0, [2, 2, 2]);
        SeriesVerdict vs = SeriesGate.Verdict(ps, ds, G);
        if (!vs.Inconclusive || vs.OutcomeSeeds[2] != (2, 5))
            return "seed with 2 of 5 accepted not INCONCLUSIVE";
        var (p23, d23) = Series(0, 99, 23);
        var (p22, d22) = Series(0, 99, 22);
        return SeriesGate.Verdict(p23, d23, G).Inconclusive && !SeriesGate.Verdict(p22, d22, G).Inconclusive && p23.Count == 114
            ? null : "series 23/114 vs 22/114";
    }

    private static string? Manifests()
    {
        string dir = Path.GetDirectoryName(Path.GetDirectoryName(SynthWorldGenerator.FindCoreMod())!)!;
        BenchManifest m8 = BenchManifest.Load(Path.Combine(dir, "lab", "manifests", "EXP-0008-bench.json"));
        BenchManifest m7 = BenchManifest.Load(Path.Combine(dir, "lab", "manifests", "EXP-0007-bench.json"));
        BenchGate g = m8.Gate;
        if (!m8.HasGate || g.ForeignCpusMax != 0.8 || g.ForeignOverShareMax != 0.10 || g.ForeignSecondCpus != 1.0 || g.RunDelayMax != 0.05
            || g.PreSeriesDeadlineSeconds != 1800 || g.WindowSeconds != 36_000 || g.ThermalMarkFrac != 0.20 || g.ExclusionRule != ExclusionRule.IndependentEvidence || g.MedianRatioMax != 1.10 || g.MedianGateMinMs != 50 || g.MaxRepeats != 2 || g.ExcludeMaxFrac != 0.20
            || g.ThermalRatio != 0.85 || g.QuietSampleSeconds != 10 || g.QuietStepSeconds != 30 || g.QuietMaxSeconds != 300 || g.PreSeriesSeconds != 60
            || g.PreSeriesForeignCpusMax != 0.8 || g.FRefSeconds != 300 || g.FRefTailSeconds != 180 || g.OutcomeBlock != "A")
            return "EXP-0008 gate differs from П5";
        if (g with { ForbiddenProcesses = [] } != new BenchGate() with { ForbiddenProcesses = [] }
            || !g.ForbiddenProcesses.SequenceEqual(new BenchGate().ForbiddenProcesses))
            return "EXP-0008 gate differs from the BenchGate defaults";
        var a = m8.Plan().Select(p => (p.Block, p.Point.Name, p.Seed, p.Threads, p.Replica, p.Background));
        var b = m7.Plan().Select(p => (p.Block, p.Point.Name, p.Seed, p.Threads, p.Replica, p.Background));
        if (!a.SequenceEqual(b))
            return "EXP-0008 plan differs from EXP-0007";
        if (m8.Reference.Variants.Count != 3 || m8.Reference.Modes.Count(x => x.Id.StartsWith("r2d", StringComparison.Ordinal)) != 3 || m8.Reference.Profile is null)
            return "EXP-0008 reference";
        if (m7.HasGate)
            return "EXP-0007 manifest has a gate";
        try
        {
            BenchManifest.Parse(File.ReadAllText(Path.Combine(dir, "lab", "manifests", "EXP-0008-bench.json")).Replace("\"foreign_cpus_max\"", "\"foreign_cpu_max\""), "/x.json");
            return "unknown gate key accepted";
        }
        catch (ManifestException) { }
        try
        {
            BenchManifest.Parse(File.ReadAllText(Path.Combine(dir, "lab", "manifests", "EXP-0008-bench.json")).Replace("\"independent-evidence\"", "\"maybe\""), "/x.json");
            return "unknown exclusion rule accepted";
        }
        catch (ManifestException) { }
        return null;
    }
}
