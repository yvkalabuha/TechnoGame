using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Singularity.Lab.Bench;

namespace Singularity.Lab.Tests;

/// <summary>TASK-0106 harness tests: SeriesGate, ProcParse, LoadMath, LoadMeter, FreqSampler top-k, manifests, CLI smokes. ADR-0023 rev 2.</summary>
[Collection("LabConsole")]
public sealed class BenchHarnessTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "sing-harness-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly BenchGate G = new();

    public BenchHarnessTests() => Directory.CreateDirectory(_tmp);

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch (IOException) { }
    }

    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    private static string LabFile(string name)
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "lab", "manifests", name)))
            dir = Path.GetDirectoryName(dir)!;
        return Path.Combine(dir!, "lab", "manifests", name);
    }

    private (int Code, string Out, string Err) Cli(params string[] args)
    {
        TextWriter o0 = Console.Out, e0 = Console.Error;
        var so = new StringWriter();
        var se = new StringWriter();
        Console.SetOut(so);
        Console.SetError(se);
        try { return (Program.Main(args), so.ToString(), se.ToString()); }
        finally { Console.SetOut(o0); Console.SetError(e0); }
    }

    // ---------- SeriesGate ----------

    private static GateAttempt A(int n, double t = 300, double? f = 0.1, double? over = 0, double? rd = 0) => new(n, t, f, over, rd);

    [Fact]
    public void Gate_NoForeignLoad_AcceptedAtAttempt0_NoRepeat()
    {
        for (int i = 0; i < 15; i++)
        {
            ProcessDecision d = SeriesGate.Decide([A(0, 300, 0.01)], 300, G);
            Assert.Equal(ProcessStatus.Accepted, d.Status);
            Assert.Equal(0, d.AcceptedAttempt);
            Assert.Equal("clean", d.AcceptedBy);
        }
        var ps = Enumerable.Range(0, 15).Select(i => new GateProcess(i, "A", "A", (ulong)(i / 5 + 1), i % 5 + 1)).ToList();
        var dec = ps.ToDictionary(p => p.Index, _ => SeriesGate.Decide([A(0, 300, 0.01)], 300, G));
        Assert.False(SeriesGate.Verdict(ps, dec, G).Inconclusive);
    }

    [Fact]
    public void Gate_ThreeFlaggedAttempts_Excluded_NoFourth()
    {
        GateAttempt[] x = [A(0, f: 0.2 + 1), A(1, f: 1.3), A(2, f: 1.1)];
        Assert.Equal(ProcessStatus.NeedsRepeat, SeriesGate.Decide(x[..1], 300, G).Status);
        Assert.Equal(ProcessStatus.NeedsRepeat, SeriesGate.Decide(x[..2], 300, G).Status);
        Assert.Equal(ProcessStatus.Excluded, SeriesGate.Decide(x, 300, G).Status);
        Assert.Equal(ProcessStatus.Excluded, SeriesGate.Decide(x, 300, G with { ExclusionRule = ExclusionRule.AnyFlag }).Status);
        Assert.Throws<ArgumentException>(() => SeriesGate.Decide([.. x, A(3, f: 2)], 300, G));
    }

    [Fact]
    public void Gate_OverShareAlone_IsN1Evidence()
    {
        GateAttempt[] x = [A(0, over: 0.5), A(1, over: 0.2), A(2, over: 0.11)];
        Assert.Equal(ProcessStatus.Excluded, SeriesGate.Decide(x, 300, G).Status);
    }

    [Fact]
    public void Gate_AttemptsMustBeNumberedInOrder_FirstNotVirtual_NotEmpty()
    {
        Assert.Throws<ArgumentException>(() => SeriesGate.Decide([], 300, G));
        Assert.Throws<ArgumentException>(() => SeriesGate.Decide([A(1, f: 2)], 300, G));
        Assert.Throws<ArgumentException>(() => SeriesGate.Decide([new GateAttempt(0, null, null, null, null, Virtual: true)], 300, G));
    }

    private static (List<GateProcess>, Dictionary<int, ProcessDecision>) Series(int[] excludedAOfSeed, int otherN, int otherExcluded)
    {
        var ps = new List<GateProcess>();
        var d = new Dictionary<int, ProcessDecision>();
        var acc = new ProcessDecision(ProcessStatus.Accepted, 0, "clean", []);
        var exc = new ProcessDecision(ProcessStatus.Excluded, null, null, []);
        var left = excludedAOfSeed.GroupBy(s => s).ToDictionary(x => x.Key, x => x.Count());
        for (int seed = 1; seed <= 3; seed++)
            for (int r = 1; r <= 5; r++)
            {
                int i = ps.Count;
                ps.Add(new GateProcess(i, "A", "A", (ulong)seed, r));
                bool ex = left.TryGetValue(seed, out int k) && k > 0;
                if (ex) left[seed] = k - 1;
                d[i] = ex ? exc : acc;
            }
        for (int o = 0; o < otherN; o++)
        {
            int i = ps.Count;
            ps.Add(new GateProcess(i, "B", "B", 1, 1));
            d[i] = o < otherExcluded ? exc : acc;
        }
        return (ps, d);
    }

    [Fact]
    public void Verdict_A_FourOf15Excluded_Inconclusive_ThreeNot()
    {
        var (p4, d4) = Series([1, 2, 3, 1], 99, 0);
        Assert.True(SeriesGate.Verdict(p4, d4, G).Inconclusive);
        var (p3, d3) = Series([1, 2, 3], 99, 0);
        Assert.False(SeriesGate.Verdict(p3, d3, G).Inconclusive);
    }

    [Fact]
    public void Verdict_SeedTwoOfFiveAccepted_Inconclusive()
    {
        var (p, d) = Series([2, 2, 2], 99, 0);
        SeriesVerdict v = SeriesGate.Verdict(p, d, G);
        Assert.True(v.Inconclusive);
        Assert.Equal((2, 5), v.OutcomeSeeds[2]);
    }

    [Fact]
    public void Verdict_Series23Of114_Inconclusive_22Not()
    {
        var (p23, d23) = Series([], 99, 23);
        var (p22, d22) = Series([], 99, 22);
        Assert.Equal(114, p23.Count);
        Assert.True(SeriesGate.Verdict(p23, d23, G).Inconclusive);
        Assert.False(SeriesGate.Verdict(p22, d22, G).Inconclusive);
    }

    [Fact]
    public void Verdict_UndecidedOrMissing_Throws()
    {
        GateProcess[] ps = [new(0, "A", "A", 1, 1)];
        Assert.Throws<ArgumentException>(() => SeriesGate.Verdict(ps, new Dictionary<int, ProcessDecision>(), G));
        var pend = new Dictionary<int, ProcessDecision> { [0] = new(ProcessStatus.NeedsRepeat, null, null, []) };
        Assert.Throws<ArgumentException>(() => SeriesGate.Verdict(ps, pend, G));
    }

    [Fact]
    public void MedPoint_FixedAcrossRepeats()
    {
        GateProcess[] ps = [new(0, "A", "g", 1, 1), new(1, "A", "g", 1, 2), new(2, "A", "g", 2, 1)];
        var first = new Dictionary<int, GateAttempt> { [0] = A(0, 100), [1] = A(0, 200), [2] = A(0, 900) };
        var med = SeriesGate.MedPoints(ps, first);
        Assert.Equal(200, med["g"]);
        ProcessDecision rep = SeriesGate.Decide([first[2], A(1, 150)], med["g"], G);
        Assert.Equal(ProcessStatus.Accepted, rep.Status);
        Assert.Equal(1, rep.AcceptedAttempt);
    }

    [Fact]
    public void MedPoint_GroupsAreSeparate_NullMedianSkipped()
    {
        GateProcess[] ps = [new(0, "A", "g1", 1, 1), new(1, "A", "g2", 1, 1), new(2, "A", "g1", 1, 2)];
        var first = new Dictionary<int, GateAttempt> { [0] = A(0, 100), [1] = A(0, 7), [2] = new GateAttempt(0, null, 0, 0, 0) };
        var med = SeriesGate.MedPoints(ps, first);
        Assert.Equal(100, med["g1"]);
        Assert.Equal(7, med["g2"]);
    }

    [Fact]
    public void IndependentEvidence_MedianOnly_AcceptedNoEvidence()
    {
        GateAttempt[] slow = [A(0, 400), A(1, 380, 0.0), A(2, 390, 0.2)];
        ProcessDecision d = SeriesGate.Decide(slow, 300, G);
        Assert.Equal(ProcessStatus.Accepted, d.Status);
        Assert.Equal(0, d.AcceptedAttempt);
        Assert.Equal("no-evidence", d.AcceptedBy);
    }

    [Fact]
    public void AnyFlag_MedianOnly_Excluded()
    {
        GateAttempt[] slow = [A(0, 400), A(1, 380), A(2, 390)];
        Assert.Equal(ProcessStatus.Excluded, SeriesGate.Decide(slow, 300, G with { ExclusionRule = ExclusionRule.AnyFlag }).Status);
    }

    [Fact]
    public void IndependentEvidence_MedianPlusRunDelayEveryAttempt_Excluded()
    {
        GateAttempt[] x = [A(0, 400, 0.1, 0, 0.2), A(1, 300, 0.1, 0, 0.3), A(2, 310, 0.0, 0, 0.1)];
        Assert.Equal(ProcessStatus.Excluded, SeriesGate.Decide(x, 300, G).Status);
    }

    [Fact]
    public void MedianOnlyFlag_KeepsFirstAttempt_EvenWhenOthersHaveEvidence()
    {
        // м only on attempt 0, then н1 and н2: the first attempt has no independent evidence -> kept.
        GateAttempt[] x = [A(0, 400), A(1, 300, 2.0), A(2, 300, 0.1, 0, 0.3)];
        ProcessDecision d = SeriesGate.Decide(x, 300, G);
        Assert.Equal(ProcessStatus.Accepted, d.Status);
        Assert.Equal(0, d.AcceptedAttempt);
    }

    [Fact]
    public void RunDelayAlone_Repeat()
    {
        Assert.Equal(ProcessStatus.NeedsRepeat, SeriesGate.Decide([A(0, 300, 0.1, 0, 0.5)], 300, G).Status);
    }

    [Fact]
    public void MedianBelowGateMin_NoFlag()
    {
        ProcessDecision d = SeriesGate.Decide([A(0, 40, 0.0)], 20, G);
        Assert.Equal(ProcessStatus.Accepted, d.Status);
        Assert.False(d.Evals[0].Flag);
    }

    [Fact]
    public void Evaluate_ThresholdsAreStrict()
    {
        Assert.False(SeriesGate.Evaluate(A(0, 330, G.ForeignCpusMax, G.ForeignOverShareMax, G.RunDelayMax), 300, G).Flag);
        Assert.True(SeriesGate.Evaluate(A(0, 300, G.ForeignCpusMax + 1e-9), 300, G).Flag);
        Assert.True(SeriesGate.Evaluate(A(0, 300, 0, G.ForeignOverShareMax + 1e-9), 300, G).Flag);
        Assert.True(SeriesGate.Evaluate(A(0, 300, 0, 0, G.RunDelayMax + 1e-9), 300, G).Flag);
        Assert.True(SeriesGate.Evaluate(A(0, 330.01), 300, G).Flag);
        // Unavailable meter (nulls) is not a flag.
        Assert.False(SeriesGate.Evaluate(new GateAttempt(0, 300, null, null, null), 300, G).Flag);
        Assert.True(SeriesGate.Evaluate(new GateAttempt(0, 300, null, null, null, Injected: true), 300, G).Evidence);
    }

    [Fact]
    public void WindowEnd_VirtualRepeat_AcceptedWindowEnd()
    {
        GateAttempt[] x = [A(0, 300, 2.0), A(1, 300, 0.1, 0, 0.9), new(2, null, null, null, null, Virtual: true)];
        ProcessDecision d = SeriesGate.Decide(x, 300, G);
        Assert.Equal(ProcessStatus.Accepted, d.Status);
        Assert.Equal(0, d.AcceptedAttempt);
        Assert.Equal("window-end", d.AcceptedBy);
        Assert.Equal(ProcessStatus.Accepted,
            SeriesGate.Decide([A(0, 300, 2.0), new GateAttempt(1, null, null, null, null, Virtual: true)], 300, G).Status);
    }

    [Fact]
    public void Mixed_FirstWithoutEvidenceAccepted()
    {
        GateAttempt[] x = [A(0, 300, 2.0), A(1, 400), A(2, 300, 0.1, 0, 0.3)];
        ProcessDecision d = SeriesGate.Decide(x, 300, G);
        Assert.Equal(ProcessStatus.Accepted, d.Status);
        Assert.Equal(1, d.AcceptedAttempt);
    }

    // ---------- #355, quiet deadline (CLI; Linux only) ----------

    private string SmokeManifest(Action<JsonObject> edit)
    {
        JsonObject root = JsonNode.Parse(File.ReadAllText(LabFile("EXP-0008-bench-smoke.json")))!.AsObject();
        edit(root["gate"]!.AsObject());
        string p = Path.Combine(_tmp, "m-" + Guid.NewGuid().ToString("N")[..6] + ".json");
        File.WriteAllText(p, root.ToJsonString());
        return p;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private Process StartBusy(string name)
    {
        string busy = Path.Combine(_tmp, name);
        File.Copy("/bin/sh", busy);
        File.SetUnixFileMode(busy, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return Process.Start(new ProcessStartInfo(busy, ["-c", "while :; do :; done"]) { UseShellExecute = false })!;
    }

    [Fact]
    public void Cli_Series_DeadlineNotStarted_Exit3()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/proc/stat") || !File.Exists("/bin/sh"))
            return;
        // A busy neighbour (about 1 CPU) against a limit of 0.5 CPU: never quiet, the 1 s deadline ends the wait.
        using Process busy = StartBusy("NeighbourLoad");
        try
        {
            string m = SmokeManifest(g =>
            {
                g["pre_series_foreign_cpus_max"] = 0.5;
                g["pre_series_s"] = 2;
                g["pre_series_deadline_s"] = 1;
            });
            string dir = Path.Combine(_tmp, "s3");
            var r = Cli("bench", "--mode", "series", "--manifest", m, "--out", dir, "--allow-debug", "true");
            Assert.Equal(3, r.Code);
            Assert.Contains("NOT STARTED", File.ReadAllText(Path.Combine(dir, "series.log")));
            // A series directory is never reused (no manual repeat or resume).
            Assert.Equal(2, Cli("bench", "--mode", "series", "--manifest", m, "--out", dir, "--allow-debug", "true").Code);
        }
        finally
        {
            busy.Kill();
            busy.WaitForExit();
        }
    }

    [Fact]
    public void Cli_Series_Forbidden355_PrefixCaseInsensitive_Invalid_Exit5()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/bin/sh"))
            return;
        using Process busy = StartBusy("SGBusyProc");
        try
        {
            string m = SmokeManifest(g =>
            {
                g["pre_series_s"] = 2;
                g["forbidden_processes"] = new JsonArray("sgbusy");   // lower case vs "SGBusyProc"
            });
            string dir = Path.Combine(_tmp, "s5");
            var r = Cli("bench", "--mode", "series", "--manifest", m, "--out", dir, "--allow-debug", "true");
            Assert.Equal(5, r.Code);
            string log = File.ReadAllText(Path.Combine(dir, "series.log"));
            Assert.Contains("#355", log);
            Assert.Contains("SGBusyProc", log);
        }
        finally
        {
            busy.Kill();
            busy.WaitForExit();
        }
    }

    // ---------- #355 ancestors, Forbidden, main_pass_only ----------

    private static PidStat PS(int pid, string comm, int ppid) => new(pid, comm, 0, 0, ppid);

    private static List<PidStat> Anc(int pid, params PidStat[] table)
    {
        var d = table.ToDictionary(x => x.Pid);
        return LoadMath.Ancestors(pid, x => d.TryGetValue(x, out PidStat v) ? v : null);
    }

    [Fact]
    public void Ancestors_Chain_StopsAtPid1()
    {
        var a = Anc(99, PS(99, "dotnet", 40), PS(40, "bash", 50), PS(50, "claude", 1), PS(1, "systemd", 0), PS(7, "other", 1));
        Assert.Equal([40, 50, 1], a.Select(x => x.Pid));
    }

    [Fact]
    public void Ancestors_Cycle_Stops()
    {
        Assert.Equal([3], Anc(2, PS(2, "a", 3), PS(3, "b", 2)).Select(x => x.Pid));
        Assert.Equal([3, 4], Anc(2, PS(2, "a", 3), PS(3, "b", 4), PS(4, "c", 3)).Select(x => x.Pid));
    }

    [Fact]
    public void Ancestors_MissingEntry_StopsAndKeepsKnown()
    {
        Assert.Empty(Anc(99));
        Assert.Equal([40], Anc(99, PS(99, "x", 40), PS(40, "bash", 50)).Select(x => x.Pid));
    }

    [Fact]
    public void Ancestors_Guard64Levels()
    {
        PidStat[] t = [.. Enumerable.Range(1000, 200).Select(i => PS(i, "p", i + 1)), PS(1200, "p", 0)];
        Assert.Equal(64, Anc(1000, t).Count);
    }

    [Fact]
    public void ExcludedFor_FixtureProc_SelfAndAncestors()
    {
        string root = Path.Combine(_tmp, "xproc");
        void Put(int pid, string comm, int ppid)
        {
            Directory.CreateDirectory(Path.Combine(root, pid.ToString()));
            // fields: pid (comm) state ppid ... utime(14) stime(15) ... starttime(22)
            File.WriteAllText(Path.Combine(root, pid.ToString(), "stat"), $"{pid} ({comm}) S {ppid} 1 1 0 -1 0 0 0 0 0 1 1 0 0 20 0 1 0 {1000 + pid} 0 0 0");
        }
        Put(99, "dotnet", 40);
        Put(40, "bash", 50);
        Put(50, "claude", 1);
        Put(1, "systemd", 0);
        Put(60, "sibling", 50);
        HashSet<int> ex = SeriesDriver.ExcludedFor(99, root);
        Assert.True(ex.SetEquals([99, 40, 50, 1]));
        Assert.True(SeriesDriver.ExcludedFor(5, Path.Combine(_tmp, "none")).SetEquals([5]));
    }

    [Fact]
    public void Forbidden_IdleBelowMin_NonFatal_BusyFatal()
    {
        List<ForbiddenHit> h = SeriesGate.Forbidden([(1, "claude", 0.049), (2, "node", 0.0), (3, "dotnet", 0.05), (4, "DotNetX", 1.5), (5, "firefox", 9)],
            G, new HashSet<int>(), "t");
        Assert.Equal([1, 2, 3, 4], h.Select(x => x.Pid));
        Assert.Equal([false, false, true, true], h.Select(x => x.Fatal));
    }

    [Fact]
    public void Forbidden_AncestorsSkipped_ChildrenOfAncestorsNot()
    {
        var excluded = new HashSet<int> { 99, 40, 50 };
        List<ForbiddenHit> h = SeriesGate.Forbidden([(50, "claude", 3.0), (40, "node", 2.0), (51, "node", 1.0), (99, "dotnet", 4.0)], G, excluded, "t");
        Assert.Equal([51], h.Select(x => x.Pid));
        Assert.True(h[0].Fatal);
        Assert.Equal(h, SeriesDriver.ForbiddenIn([(50, "claude", 3.0), (40, "node", 2.0), (51, "node", 1.0), (99, "dotnet", 4.0)], G, excluded, "t"));
    }

    [Fact]
    public void ExcludedAmong_PidCommStart()
    {
        PidStat self = new(99, "dotnet", 0, 500, 40);
        PidStat[] anc = [new(40, "bash", 0, 100, 50), new(50, "claude", 0, 0, 1)];
        // self always excluded (same comm/start); reused pid with other comm or start is not.
        Assert.Contains(99, SeriesDriver.ExcludedAmong(99, self, anc, [(99, "dotnet", 500)]));
        Assert.Contains(99, SeriesDriver.ExcludedAmong(99, null, anc, [(99, "whatever", 1)]));
        Assert.DoesNotContain(99, SeriesDriver.ExcludedAmong(99, self, anc, [(99, "node", 500)]));
        Assert.DoesNotContain(99, SeriesDriver.ExcludedAmong(99, self, anc, [(99, "dotnet", 501)]));
        // ancestor: pid+comm+start must agree; start null falls back to pid+comm; ancestor start 0 = unknown.
        Assert.Contains(40, SeriesDriver.ExcludedAmong(99, self, anc, [(40, "bash", 100)]));
        Assert.DoesNotContain(40, SeriesDriver.ExcludedAmong(99, self, anc, [(40, "bash", 101)]));
        Assert.DoesNotContain(40, SeriesDriver.ExcludedAmong(99, self, anc, [(40, "make", 100)]));
        Assert.Contains(40, SeriesDriver.ExcludedAmong(99, self, anc, [(40, "bash", null)]));
        Assert.DoesNotContain(40, SeriesDriver.ExcludedAmong(99, self, anc, [(40, "make", null)]));
        Assert.Contains(50, SeriesDriver.ExcludedAmong(99, self, anc, [(50, "claude", 12345)]));
        Assert.Empty(SeriesDriver.ExcludedAmong(99, self, anc, [(7, "bash", 100)]));
    }

    [Fact]
    public void PreSeries_SurvivesFatal355Stop()
    {
        var parts = new Dictionary<string, Action<Utf8JsonWriter>>();
        Assert.Throws<InvalidOperationException>(() => SeriesDriver.RegisterThenCheck(parts, "pre_series",
            j => { j.WriteStartObject("pre_series"); j.WriteNumber("seconds", 1); j.WriteEndObject(); },
            () => throw new InvalidOperationException("#355")));
        using var ms = new MemoryStream();
        using (var j = new Utf8JsonWriter(ms))
        {
            j.WriteStartObject();
            SeriesDriver.WriteParts(j, parts);
            j.WriteEndObject();
        }
        using JsonDocument d = JsonDocument.Parse(ms.ToArray());
        Assert.Equal(1, d.RootElement.GetProperty("pre_series").GetProperty("seconds").GetInt32());
        Assert.Equal("pre_series", SeriesDriver.PartOrder[0]);
    }

    [Fact]
    public void Manifest_MainPassOnly_Parsing()
    {
        string text = File.ReadAllText(LabFile("EXP-0008-bench-smoke.json"));
        Assert.False(BenchManifest.Parse(text, "/x.json").Gate.MainPassOnly);
        BenchManifest m = BenchManifest.Parse(text.Replace("\"window_s\": 0,", "\"window_s\": 0, \"main_pass_only\": true,"), "/x.json");
        Assert.True(m.Gate.MainPassOnly);
        Assert.Throws<ManifestException>(() => BenchManifest.Parse(text.Replace("\"window_s\": 0,", "\"window_s\": 0, \"main_pass_only\": 1,"), "/x.json"));
    }

    [Fact]
    public void Cli_NoFref_WithFref_Exit2()
    {
        string f = Path.Combine(_tmp, "nf.json");
        Assert.Equal(2, Cli("bench", "--mode", "process", "--actors", "6", "--nodes", "180", "--threads", "1", "--warmup", "1", "--measure", "1",
            "--no-fref", "true", "--fref", "3000", "--allow-debug", "true", "--out", f).Code);
    }

    private string MainOnlyManifest() => SmokeManifest(g =>
    {
        g["main_pass_only"] = true;
        g["pre_series_s"] = 1;
    });

    [Fact]
    public void Cli_Series_Smoke_Inject_Exit4()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/proc/stat"))
            return;
        string dir = Path.Combine(_tmp, "s4");
        // Two injected A processes out of 6 (> 20 % of the outcome block): INCONCLUSIVE.
        var r = Cli("bench", "--mode", "series", "--manifest", MainOnlyManifest(), "--out", dir, "--allow-debug", "true", "--inject-flag", "0:3,1:3");
        Assert.Equal(4, r.Code);
        using JsonDocument d = Json(Path.Combine(dir, "series.json"));
        Assert.Equal(4, d.RootElement.GetProperty("exit_code").GetInt32());
    }

    [Fact]
    public void Cli_Series_Calibration_NoRepeats()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/proc/stat"))
            return;
        string dir = Path.Combine(_tmp, "cal");
        var r = Cli("bench", "--mode", "series", "--manifest", MainOnlyManifest(), "--out", dir, "--allow-debug", "true", "--calibration", "true");
        Assert.Equal(0, r.Code);
        string[] files = [.. Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(dir, f))];
        Assert.Contains(files, f => f.EndsWith("-a0.json", StringComparison.Ordinal));
        Assert.DoesNotContain(files, f => f.EndsWith("-a1.json", StringComparison.Ordinal) || f.EndsWith("-a2.json", StringComparison.Ordinal));
    }

    // ---------- ProcParse ----------

    [Fact]
    public void ProcParse_CpuLine_BusyAndTotal()
    {
        const string stat = "cpu  100 5 50 1000 20 3 2 1 7 0\ncpu0 50 2 25 500 10 1 1 0 0 0\nintr 1 2 3\n";
        CpuTotals c = ProcParse.CpuLine(B(stat))!.Value;
        Assert.Equal(100 + 5 + 50 + 3 + 2 + 1, c.Busy);
        Assert.Equal(c.Busy + 1000 + 20, c.Total);
        CpuTotals o = ProcParse.CpuLine(B("cpu  10 0 5 100\n"))!.Value;
        Assert.Equal((15, 115), (o.Busy, o.Total));
    }

    [Theory]
    [InlineData("")]
    [InlineData("cpu0 1 2 3 4\n")]
    [InlineData("cpu  1 2 x\n")]
    [InlineData("cpu  1 2 3\n")]
    [InlineData("cpu  \n")]
    [InlineData("cpu  -1 2 3 4\n")]
    [InlineData("garbage\x00\xff")]
    public void ProcParse_CpuLine_Malformed_Null(string text) => Assert.Null(ProcParse.CpuLine(B(text)));

    private const string PidLine = "4242 (a b) c)) S 1 4242 4242 0 -1 4194560 100 0 0 0 111 22 0 0 20 0 3 0 999 1000000 300 18446744073709551615";

    [Fact]
    public void ProcParse_Stat_CommWithSpacesAndParen()
    {
        PidStat p = ProcParse.Stat(PidLine)!.Value;
        Assert.Equal(4242, p.Pid);
        Assert.Equal("a b) c)", p.Comm);
        Assert.Equal(133, p.CpuTicks);
        Assert.Equal(999, p.StartTime);
        Assert.Equal(133, ProcParse.StatCpuTicks(B(PidLine)));
        Assert.Equal(4242, ProcParse.StatPid(B(PidLine)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("4242 (x) S 1 2")]
    [InlineData("no paren at all")]
    [InlineData("(x) S 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21")]
    [InlineData("abc (x) S 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21")]
    [InlineData("1 (x) S 1 2 3 4 5 6 7 8 9 10 q 12 13 14 15 16 17 18 19 20 21")]
    public void ProcParse_Stat_Malformed_Null(string line)
    {
        Assert.Null(ProcParse.Stat(line));
        Assert.Null(ProcParse.StatCpuTicks(B(line)) is long && line.Contains(')') && line.Split(' ').Length > 20 ? null : ProcParse.StatCpuTicks(B(line)));
    }

    [Fact]
    public void ProcParse_StatSpan_NoParenOrShort_Null()
    {
        Assert.Null(ProcParse.StatCpuTicks(B("no paren")));
        Assert.Null(ProcParse.StatCpuTicks(B("1 (x) S 1 2")));
        Assert.Null(ProcParse.StatPid(B("xyz")));
        Assert.Null(ProcParse.StatPid([]));
    }

    [Fact]
    public void ProcParse_Loadavg()
    {
        var l = ProcParse.Loadavg("1.50 0.75 0.25 3/1234 5678\n")!.Value;
        Assert.Equal((1.5, 0.75, 0.25, 3, 1234), l);
        Assert.Null(ProcParse.Loadavg(""));
        Assert.Null(ProcParse.Loadavg("1.5 0.7 0.2"));
        Assert.Null(ProcParse.Loadavg("1.5 0.7 0.2 31234 5"));
        Assert.Null(ProcParse.Loadavg("a b c 1/2 3"));
        Assert.Null(ProcParse.Loadavg("1 2 3 x/2 3"));
    }

    [Fact]
    public void ProcParse_Schedstat_And_Status()
    {
        Assert.Equal(1924, ProcParse.SchedstatWaitNs("604888 1924 8\n"));
        Assert.Null(ProcParse.SchedstatWaitNs("1"));
        Assert.Null(ProcParse.SchedstatWaitNs(""));
        Assert.Null(ProcParse.SchedstatWaitNs("1 abc 3"));
        const string status = "Name:\tx\nvoluntary_ctxt_switches:\t10\nnonvoluntary_ctxt_switches:\t7\n";
        Assert.Equal(7, ProcParse.NonvoluntarySwitches(status));
        Assert.Null(ProcParse.NonvoluntarySwitches("Name: x"));
        Assert.Null(ProcParse.NonvoluntarySwitches("nonvoluntary_ctxt_switches:\tzz\n"));
        Assert.Null(ProcParse.NonvoluntarySwitches(""));
    }

    // ---------- LoadMath ----------

    [Fact]
    public void LoadMath_ForeignCpu()
    {
        Assert.Equal(0.0625, LoadMath.ForeignCpu(400, 1600, 300)!.Value, 12);
        Assert.Null(LoadMath.ForeignCpu(1, 0, 0));
        Assert.Null(LoadMath.ForeignCpu(1, -5, 0));
        Assert.True(LoadMath.ForeignCpu(10, 100, 12) < 0);   // not clamped
    }

    [Fact]
    public void LoadMath_TopKMean()
    {
        Assert.Equal(3500, LoadMath.TopKMean([1000, 4000, 3000, 800], 2));
        Assert.Equal(5, LoadMath.TopKMean([5], 3));
        Assert.Equal(800, LoadMath.TopKMean([1000, 4000, 3000, 800], 0) is double x ? 800 : 0);
        Assert.Null(LoadMath.TopKMean([], 1));
    }

    [Fact]
    public void LoadMath_TopForeign_PidReuse_OwnPidOut()
    {
        PidStat[] start = [new(1, "self", 10, 1), new(2, "ide", 100, 5), new(3, "old", 50, 6), new(5, "av", 0, 9)];
        PidStat[] end = [new(1, "self", 500, 1), new(2, "ide", 160, 5), new(3, "new", 7, 77), new(4, "browser", 40, 80), new(5, "av", 60, 9)];
        List<ForeignProcess> top = LoadMath.TopForeign(start, end, selfPid: 1, dTotal: 1000, n: 3);
        Assert.Equal("2:60,5:60,4:40", string.Join(",", top.Select(t => $"{t.Pid}:{t.CpuTicks}")));
        Assert.Equal(0.06, top[0].MachineShare, 12);
        Assert.Empty(LoadMath.TopForeign(start, start, 1, 1000, 5));
        Assert.Equal(0, LoadMath.TopForeign(start, end, 1, 0, 1)[0].MachineShare);
    }

    [Fact]
    public void LoadMath_MergeTop_And_RunDelay()
    {
        var a = new List<ForeignProcess> { new(2, "ide", 60, 0), new(4, "browser", 40, 0) };
        var merged = LoadMath.MergeTop([a, [new ForeignProcess(4, "browser", 30, 0)]], 2000, 2);
        Assert.Equal("4:70,2:60", string.Join(",", merged.Select(t => $"{t.Pid}:{t.CpuTicks}")));
        var w0 = new Dictionary<int, long> { [10] = 100, [11] = 0 };
        var w1 = new Dictionary<int, long> { [10] = 600, [11] = 500, [12] = 1000 };
        Assert.Equal(1.0, LoadMath.RunDelayShare(w0, w1, 1000, 2)!.Value, 12);
        Assert.Null(LoadMath.RunDelayShare(w0, w1, 0, 2));
        Assert.Null(LoadMath.RunDelayShare(w0, w1, 1000, 0));
        // A counter that went backwards (thread reuse) does not make a negative delta.
        Assert.Equal(0, LoadMath.SumDelta(new Dictionary<int, long> { [1] = 9 }, new Dictionary<int, long> { [1] = 3 }));
    }

    [Fact]
    public void LoadMath_PossibleThermalThrottle()
    {
        Assert.True(LoadMath.PossibleThermalThrottle(3000, 4000, 0.85));
        Assert.False(LoadMath.PossibleThermalThrottle(3500, 4000, 0.85));
        Assert.Null(LoadMath.PossibleThermalThrottle(null, 4000, 0.85));
        Assert.Null(LoadMath.PossibleThermalThrottle(3000, null, 0.85));
    }

    // ---------- FreqSampler ----------

    [Fact]
    public void FreqSampler_TopK_PowersavePicture()
    {
        double[] busy = [.. Enumerable.Repeat(800.0, 14), 4500, 4500];
        (long, double[])[] samples = [(10, busy), (20, busy), (30, busy), (99, busy)];
        (long, long)[] window = [(0, 35)];
        Assert.Equal(4500, FreqSampler.MedianOf(samples, window, v => LoadMath.TopKMean(v, 2) ?? double.NaN));
        Assert.Equal(1262.5, FreqSampler.MedianOf(samples, window, v => v.Average())!.Value, 9);
        Assert.Null(FreqSampler.MedianOf(samples, [(40, 50)], v => v.Average()));
        Assert.Null(FreqSampler.MedianOf([(10, [])], window, v => v.Average()));
    }

    [Fact]
    public void FreqSampler_CpuinfoMhz_Parse_Malformed()
    {
        Span<double> row = stackalloc double[4];
        Assert.Equal(2, FreqSampler.CpuinfoMhz(B("processor\t: 0\ncpu MHz\t\t: 3200.5\nprocessor\t: 1\ncpu MHz\t\t: 800.000\n"), row));
        Assert.Equal(3200.5, row[0]);
        Assert.Equal(0, FreqSampler.CpuinfoMhz(B("cpu MHz : abc\nmodel name : x\n"), row));
        Assert.Equal(0, FreqSampler.CpuinfoMhz([], row));
    }

    // ---------- LoadMeter ----------

    [Fact]
    public void LoadMeter_FixtureProc_MachineBlock()
    {
        string root = Path.Combine(_tmp, "proc");
        Directory.CreateDirectory(Path.Combine(root, "self", "task", "7"));
        Directory.CreateDirectory(Path.Combine(root, "900"));
        File.WriteAllText(Path.Combine(root, "stat"), "cpu  100 0 50 1000 0 0 0 0 0 0\n");
        const string self = "77 (lab) R 1 77 77 0 -1 0 0 0 0 0 30 10 0 0 20 0 9 0 5 0 0 0";
        File.WriteAllText(Path.Combine(root, "self", "stat"), self);
        File.WriteAllText(Path.Combine(root, "loadavg"), "0.50 0.40 0.30 1/100 200\n");
        File.WriteAllText(Path.Combine(root, "self", "task", "7", "schedstat"), "10 2000 3\n");
        File.WriteAllText(Path.Combine(root, "self", "task", "7", "status"), "nonvoluntary_ctxt_switches:\t4\n");
        File.WriteAllText(Path.Combine(root, "900", "stat"), "900 (ide) S 1 900 900 0 -1 0 0 0 0 0 5 5 0 0 20 0 1 0 3 0 0 0");
        using var meter = new LoadMeter(root, background: false);
        Assert.True(meter.Available);
        Assert.Equal(77, meter.SelfPid);
        MachineSnapshot s0 = meter.Snapshot();
        File.WriteAllText(Path.Combine(root, "stat"), "cpu  300 0 100 1200 0 0 0 0 0 0\n");
        File.WriteAllText(Path.Combine(root, "self", "stat"), self.Replace(" 30 10 ", " 130 60 "));
        File.WriteAllText(Path.Combine(root, "900", "stat"), "900 (ide) S 1 900 900 0 -1 0 0 0 0 0 55 5 0 0 20 0 1 0 3 0 0 0");
        File.WriteAllText(Path.Combine(root, "self", "task", "7", "schedstat"), "10 5000 3\n");
        var win = new MachineWindow("w", s0, meter.Snapshot());
        Assert.Equal(100.0 / 450, LoadMeter.ForeignCpu([win])!.Value, 12);
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
        Assert.True(m.GetProperty("available").GetBoolean());
        Assert.Equal("0.50 0.40 0.30 1/100 200", w0.GetProperty("loadavg_end").GetString());
        Assert.Equal(3000, w0.GetProperty("run_delay_ns").GetInt64());
        Assert.Equal("ide", w0.GetProperty("top_foreign")[0].GetProperty("comm").GetString());
        Assert.Equal(50, w0.GetProperty("top_foreign")[0].GetProperty("cpu_ticks").GetInt64());
        Assert.Equal(3, w0.GetProperty("top_foreign")[0].GetProperty("start_time").GetInt64());
    }

    [Fact]
    public void LoadMeter_NoProc_AvailableFalse_NoThrow()
    {
        using var none = new LoadMeter(Path.Combine(_tmp, "no-such-proc"), background: false);
        Assert.False(none.Available);
        Assert.NotNull(none.Reason);
        MachineSnapshot a = none.Snapshot(), b = none.Snapshot();
        Assert.Null(LoadMeter.ForeignCpu([new MachineWindow("w", a, b)]));
        using var ms = new MemoryStream();
        using (var j = new Utf8JsonWriter(ms))
        {
            j.WriteStartObject();
            none.Write(j, [new MachineWindow("w", a, b)], 1);
            j.WriteEndObject();
        }
        using JsonDocument d = JsonDocument.Parse(ms.ToArray());
        JsonElement m = d.RootElement.GetProperty("machine");
        Assert.False(m.GetProperty("available").GetBoolean());
        Assert.Equal(JsonValueKind.Null, m.GetProperty("foreign_cpu_window").ValueKind);
    }

    [Fact]
    public void LoadMeter_MalformedProcFiles_NoThrow()
    {
        string root = Path.Combine(_tmp, "bad");
        Directory.CreateDirectory(Path.Combine(root, "self"));
        File.WriteAllText(Path.Combine(root, "stat"), "garbage");
        File.WriteAllText(Path.Combine(root, "self", "stat"), "garbage");
        using var m = new LoadMeter(root, background: false);
        MachineSnapshot s = m.Snapshot();
        Assert.Null(LoadMeter.ForeignCpu([new MachineWindow("w", s, m.Snapshot())]));
    }

    // ---------- Manifests ----------

    [Fact]
    public void Manifest_Exp0008_GateEqualsP5_And_Defaults()
    {
        BenchManifest m8 = BenchManifest.Load(LabFile("EXP-0008-bench.json"));
        BenchGate g = m8.Gate;
        Assert.True(m8.HasGate);
        Assert.Equal(0.8, g.ForeignCpusMax);
        Assert.Equal(0.10, g.ForeignOverShareMax);
        Assert.Equal(1.0, g.ForeignSecondCpus);
        Assert.Equal(0.05, g.RunDelayMax);
        Assert.Equal(1800, g.PreSeriesDeadlineSeconds);
        Assert.Equal(36_000, g.WindowSeconds);
        Assert.Equal(0.05, g.ForbiddenMinCpus);
        Assert.False(g.MainPassOnly);
        Assert.Equal(0.20, g.ThermalMarkFrac);
        Assert.Equal(ExclusionRule.IndependentEvidence, g.ExclusionRule);
        Assert.Equal(1.10, g.MedianRatioMax);
        Assert.Equal(50, g.MedianGateMinMs);
        Assert.Equal(2, g.MaxRepeats);
        Assert.Equal(0.20, g.ExcludeMaxFrac);
        Assert.Equal(0.5, g.SeedAcceptMinFrac);
        Assert.Equal(0.85, g.ThermalRatio);
        Assert.Equal((10, 30, 300, 60), (g.QuietSampleSeconds, g.QuietStepSeconds, g.QuietMaxSeconds, g.PreSeriesSeconds));
        Assert.Equal(0.8, g.PreSeriesForeignCpusMax);
        Assert.Equal((300, 180), (g.FRefSeconds, g.FRefTailSeconds));
        Assert.Equal("A", g.OutcomeBlock);
        Assert.Equal(g with { ForbiddenProcesses = [] }, new BenchGate() with { ForbiddenProcesses = [] });
        Assert.True(g.ForbiddenProcesses.SequenceEqual(new BenchGate().ForbiddenProcesses));
    }

    [Fact]
    public void Manifest_Exp0008_PlanEqualsExp0007_And_Exp0007Unchanged()
    {
        BenchManifest m8 = BenchManifest.Load(LabFile("EXP-0008-bench.json"));
        BenchManifest m7 = BenchManifest.Load(LabFile("EXP-0007-bench.json"));
        Assert.True(m8.Plan().Select(p => (p.Block, p.Point.Name, p.Seed, p.Threads, p.Replica, p.Background))
            .SequenceEqual(m7.Plan().Select(p => (p.Block, p.Point.Name, p.Seed, p.Threads, p.Replica, p.Background))));
        Assert.False(m7.HasGate);
        Assert.Equal("6112abb435b9dc972edf72e97326da78fa75de3d1069f460520882e4a5ad21ac", m7.Hash);
    }

    [Fact]
    public void Manifest_Rejections()
    {
        string text = File.ReadAllText(LabFile("EXP-0008-bench.json"));
        Assert.Throws<ManifestException>(() => BenchManifest.Parse(text.Replace("\"foreign_cpus_max\"", "\"foreign_cpu_max\""), "/x.json"));
        Assert.Throws<ManifestException>(() => BenchManifest.Parse(text.Replace("\"independent-evidence\"", "\"maybe\""), "/x.json"));
        Assert.Throws<ManifestException>(() => BenchManifest.Parse(
            text.Replace("\"warmup\": \"100\", \"calls\": \"1000\"}", "\"warmup\": \"100\", \"calls\": \"1000\", \"out\": \"x\"}"), "/x.json"));
    }

    // ---------- CLI smokes ----------

    private JsonDocument Json(string file) => JsonDocument.Parse(File.ReadAllText(file));

    [Fact]
    public void Cli_Fref_TopK()
    {
        string f = Path.Combine(_tmp, "fref.json");
        int code = Cli("bench", "--mode", "fref", "--seconds", "2", "--tail", "1", "--threads", "1", "--k", "1", "--out", f, "--allow-debug", "true").Code;
        using JsonDocument d = Json(f);
        // Documented (BenchCommand.FRef): exit 1 = no frequency samples. Only Linux cpufreq gives samples; elsewhere the command
        // must not throw, must still write the JSON, and must exit 1 with f_ref_mhz null (TASK-0116).
        if (OperatingSystem.IsLinux() && code == 0)
            Assert.NotEqual(JsonValueKind.Null, d.RootElement.GetProperty("f_ref_mhz").ValueKind);
        else if (!OperatingSystem.IsLinux())
        {
            Assert.Equal(1, code);
            Assert.Equal(JsonValueKind.Null, d.RootElement.GetProperty("f_ref_mhz").ValueKind);
        }
        else
            Assert.Equal(1, code); // Linux without cpufreq (container): same documented exit 1
        Assert.Equal("topk", d.RootElement.GetProperty("stat").GetString());
        Assert.Equal(1, d.RootElement.GetProperty("k").GetInt32());
        Assert.True(d.RootElement.TryGetProperty("machine", out _));
    }

    [Fact]
    public void Cli_Process_Smoke_FrequencyAndMachine()
    {
        string f = Path.Combine(_tmp, "p.json");
        Assert.Equal(0, Cli("bench", "--mode", "process", "--actors", "6", "--nodes", "180", "--threads", "2", "--warmup", "1", "--measure", "2",
            "--fref", "3000", "--allow-debug", "true", "--out", f).Code);
        using JsonDocument d = Json(f);
        JsonElement fr = d.RootElement.GetProperty("frequency");
        Assert.Equal(2, fr.GetProperty("k").GetInt32());
        Assert.False(fr.TryGetProperty("throttled", out _));
        Assert.True(fr.TryGetProperty("possible_thermal_throttle", out _));
        Assert.Equal(2, d.RootElement.GetProperty("machine").GetProperty("windows").GetArrayLength());
        foreach (JsonElement run in d.RootElement.GetProperty("runs").EnumerateArray())
            Assert.True(run.TryGetProperty("allocated_bytes_net", out _));
    }

    [Fact]
    public void Cli_Alloc_Smoke()
    {
        string f = Path.Combine(_tmp, "a.json");
        Assert.Equal(0, Cli("bench", "--mode", "alloc", "--actors", "6", "--nodes", "180", "--months", "3", "--warmup", "1", "--allow-debug", "true", "--out", f).Code);
        using JsonDocument d = Json(f);
        JsonElement r = d.RootElement;
        foreach (string k in (string[])["provenance", "conditions", "params", "world", "machine"])
            Assert.True(r.TryGetProperty(k, out _), k);
        Assert.Equal(9, r.GetProperty("probes").GetArrayLength());
        Assert.True(r.GetProperty("probes_kept_hash").GetBoolean());
        Assert.Equal(r.GetProperty("state_hash_before_probes").GetString(), r.GetProperty("state_hash_after_probes").GetString());
        JsonElement p8 = r.GetProperty("p8_split");
        long without = p8.GetProperty("snapshot_without_nodes_bytes").GetInt64();
        Assert.InRange(without, 1, p8.GetProperty("snapshot_no_step_fresh_bytes").GetInt64() - 1);
        foreach (JsonElement m in r.GetProperty("months").EnumerateArray())
            Assert.True(m.GetProperty("snapshots_no_step").GetInt32() + m.GetProperty("snapshots_stepped").GetInt32() >= 1);
    }

    [Fact]
    public void R2d_WritesPairwiseRatios()
    {
        string f = Path.Combine(_tmp, "r2.json");
        Assert.Equal(0, Cli("bench", "--mode", "r2", "--variants", "d", "--actors", "6", "--nodes", "180", "--month-warmup", "1", "--months", "3",
            "--threads-list", "1", "--allow-debug", "true", "--out", f).Code);
        using JsonDocument d = Json(f);
        JsonElement t = d.RootElement.GetProperty("r2").GetProperty("d_month_cow_vs_full").GetProperty("by_threads")[0];
        Assert.Equal(3, t.GetProperty("ratio_cow_over_full_all").GetArrayLength());
    }
}
