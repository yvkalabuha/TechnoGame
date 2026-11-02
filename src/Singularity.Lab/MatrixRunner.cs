using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Singularity.Lab;

/// <summary>
/// Runs every (point, seed, threads) combination as a separate `run` process (ADR-0008),
/// up to <c>jobs</c> at a time, then merges outputs in run order (points, seeds, threads),
/// which does not depend on completion order.
/// </summary>
public static class MatrixRunner
{
    public const string RunsDir = "runs";
    public const string MatrixJsonFile = "matrix.json";

    private sealed record Job(int Index, string RunId, MatrixPoint Point, ulong Seed, int Threads, string Dir);

    private sealed record JobResult(int ExitCode, string Status, string? Golden, string? Error, string? CoreCommit, string? ManifestHash);

    /// <summary>Default per-run wall-clock limit for a child `run` process.</summary>
    public const int DefaultRunTimeoutSeconds = 3600;

    public static int Run(Manifest m, string outDir, int jobs, TextWriter log, int timeoutSeconds = DefaultRunTimeoutSeconds)
    {
        Provenance prov = Provenance.Collect(m);
        ModelRegistry.Find(m.Model); // fail fast on an unknown model
        string runsRoot = Path.Combine(outDir, RunsDir);
        Directory.CreateDirectory(runsRoot);

        var jobList = new List<Job>();
        foreach (MatrixPoint p in m.Points)
        {
            foreach (ulong seed in p.Seeds)
            {
                foreach (int t in m.Threads)
                {
                    string id = RunExecutor.MakeRunId(m, p.Index, seed, t);
                    jobList.Add(new Job(jobList.Count, id, p, seed, t, Path.Combine(runsRoot, id)));
                }
            }
        }

        log.WriteLine($"matrix: {jobList.Count} runs ({m.Points.Count} points, {m.Points.Sum(p => p.Seeds.Count)} point-seeds x {m.Threads.Count} thread counts), jobs={jobs}, timeout_s={timeoutSeconds}");
        DateTime startedUtc = DateTime.UtcNow;
        var clock = Stopwatch.StartNew();
        var results = new JobResult[jobList.Count];
        int done = 0;
        Parallel.For(0, jobList.Count, new ParallelOptions { MaxDegreeOfParallelism = jobs }, i =>
        {
            results[i] = RunChild(m, jobList[i], timeoutSeconds);
            int n = Interlocked.Increment(ref done);
            if (n % 25 == 0 || n == jobList.Count)
                log.WriteLine($"  {n}/{jobList.Count}");
        });
        clock.Stop();

        var problems = new List<string>();
        for (int i = 0; i < jobList.Count; i++)
        {
            JobResult r = results[i];
            if (r.Status != "ok")
                problems.Add($"{jobList[i].RunId}: {r.Status} (exit {r.ExitCode}) {r.Error}");
            else if (r.ManifestHash != m.Hash || r.CoreCommit != prov.CoreCommit)
                problems.Add($"{jobList[i].RunId}: provenance differs from matrix (manifest/commit).");
        }

        // L2 check for free: the same (point, seed) must give the same golden_hash for every thread count.
        var threadMismatch = new List<string>();
        for (int i = 0; i < jobList.Count; i += m.Threads.Count)
        {
            string? first = results[i].Golden;
            for (int k = 1; k < m.Threads.Count; k++)
            {
                if (results[i + k].Golden != first && results[i + k].Status == "ok" && results[i].Status == "ok")
                    threadMismatch.Add($"{jobList[i].RunId} vs {jobList[i + k].RunId}");
            }
        }

        RunExecutor.WriteRunsCsv(Path.Combine(outDir, RunExecutor.RunsFile),
            jobList.Select(j => (j.RunId, j.Point, j.Seed, j.Threads, results[j.Index].Golden, results[j.Index].Status == "ok")).ToList());
        List<string> merged = MergeCsvs(outDir, jobList, results, problems);

        // A thread mismatch means the golden hashes are not reproducible: no matrix-level hash then.
        string? goldenSet = problems.Count == 0 && threadMismatch.Count == 0 ? GoldenSetHash(jobList, results) : null;
        WriteMatrixJson(outDir, m, prov, jobList, results, problems, threadMismatch, merged, goldenSet, startedUtc, clock.ElapsedMilliseconds, jobs);

        log.WriteLine($"matrix: ok={results.Count(r => r.Status == "ok")} failed={results.Count(r => r.Status != "ok")} thread_mismatch={threadMismatch.Count} elapsed_ms={clock.ElapsedMilliseconds}");
        foreach (string p in problems.Take(20)) log.WriteLine($"  problem: {p}");
        foreach (string p in threadMismatch.Take(20)) log.WriteLine($"  thread mismatch: {p}");
        if (goldenSet is not null) log.WriteLine($"golden_set_hash={goldenSet}");
        return problems.Count == 0 && threadMismatch.Count == 0 ? 0 : 1;
    }

    /// <summary>SHA-256 over "run_id golden_hash\n" lines in run order: one hash for the whole matrix.</summary>
    private static string GoldenSetHash(List<Job> jobs, JobResult[] results)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < jobs.Count; i++)
            sb.Append(jobs[i].RunId).Append(' ').Append(results[i].Golden).Append('\n');
        return CanonicalJson.Sha256Hex(sb.ToString());
    }

    private static JobResult RunChild(Manifest m, Job job, int timeoutSeconds)
    {
        // The matrix reuses the output directory only with --force; stale files of an earlier
        // run (old CSVs, run.json) must not leak into this run's results.
        if (Directory.Exists(job.Dir))
            Directory.Delete(job.Dir, recursive: true);
        Directory.CreateDirectory(job.Dir);
        var psi = SelfInvocation();
        foreach (string a in new[]
                 {
                     "run", "--manifest", m.SourcePath, "--point", job.Point.Index.ToString(CultureInfo.InvariantCulture),
                     "--seed", job.Seed.ToString(CultureInfo.InvariantCulture), "--threads", job.Threads.ToString(CultureInfo.InvariantCulture),
                     "--out", job.Dir, "--force", "--quiet",
                 })
            psi.ArgumentList.Add(a);
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;

        int exit;
        using (Process p = Process.Start(psi)!)
        {
            Task<string> err = p.StandardError.ReadToEndAsync();
            Task<string> output = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(TimeSpan.FromSeconds(timeoutSeconds)))
            {
                try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* already exited */ }
                p.WaitForExit();
                File.WriteAllText(Path.Combine(job.Dir, "log.txt"), output.Result + err.Result + $"\nkilled after {timeoutSeconds} s\n", Csv.Utf8NoBom);
                return new JobResult(-1, "timeout", null, $"run exceeded {timeoutSeconds} s and was killed", null, null);
            }
            p.WaitForExit(); // flush redirected streams
            exit = p.ExitCode;
            File.WriteAllText(Path.Combine(job.Dir, "log.txt"), output.Result + err.Result, Csv.Utf8NoBom);
        }

        string runJson = Path.Combine(job.Dir, RunExecutor.RunJsonFile);
        if (!File.Exists(runJson))
            return new JobResult(exit, "no-run-json", null, "child did not write run.json; see log.txt", null, null);
        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(runJson));
            JsonElement r = doc.RootElement;
            string status = r.GetProperty("status").GetString() ?? "unknown";
            if (status == "ok" && exit != 0) status = "exit-code";
            return new JobResult(exit, status,
                r.GetProperty("golden_hash").ValueKind == JsonValueKind.String ? r.GetProperty("golden_hash").GetString() : null,
                r.TryGetProperty("error", out JsonElement e) ? e.GetString() : null,
                r.GetProperty("core_commit").GetString(), r.GetProperty("manifest_hash").GetString());
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // A broken run.json is a failed run, not a crash of the whole matrix.
            return new JobResult(exit, "bad-run-json", null, $"{e.GetType().Name}: {e.Message}", null, null);
        }
    }

    /// <summary>The same Lab binary: apphost directly, or `dotnet Singularity.Lab.dll`.</summary>
    private static ProcessStartInfo SelfInvocation()
    {
        string process = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot find own executable.");
        if (string.Equals(Path.GetFileNameWithoutExtension(process), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var psi = new ProcessStartInfo(process);
            psi.ArgumentList.Add(typeof(MatrixRunner).Assembly.Location);
            return psi;
        }
        return new ProcessStartInfo(process);
    }

    /// <summary>Concatenates each metric CSV over successful runs in run order; headers must match.</summary>
    private static List<string> MergeCsvs(string outDir, List<Job> jobs, JobResult[] results, List<string> problems)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < jobs.Count; i++)
        {
            if (results[i].Status != "ok") continue;
            foreach (string f in Directory.GetFiles(jobs[i].Dir, "*.csv"))
            {
                string name = Path.GetFileName(f);
                if (name != RunExecutor.RunsFile) names.Add(name);
            }
        }

        foreach (string name in names)
        {
            using StreamWriter w = Csv.Create(Path.Combine(outDir, name));
            string? header = null;
            for (int i = 0; i < jobs.Count; i++)
            {
                if (results[i].Status != "ok") continue;
                string path = Path.Combine(jobs[i].Dir, name);
                if (!File.Exists(path))
                {
                    problems.Add($"{jobs[i].RunId}: missing {name}");
                    continue;
                }
                using var reader = new StreamReader(path, Csv.Utf8NoBom);
                string? h = reader.ReadLine();
                if (header is null)
                {
                    header = h;
                    w.WriteLine(header);
                }
                else if (h != header)
                {
                    problems.Add($"{jobs[i].RunId}: {name} header differs");
                    continue;
                }
                string? line;
                while ((line = reader.ReadLine()) is not null)
                    w.WriteLine(line);
            }
        }
        return names.ToList();
    }

    private static void WriteMatrixJson(string outDir, Manifest m, Provenance prov, List<Job> jobs, JobResult[] results,
        List<string> problems, List<string> threadMismatch, List<string> merged, string? goldenSet,
        DateTime startedUtc, long elapsedMs, int jobCount)
    {
        using var stream = File.Create(Path.Combine(outDir, MatrixJsonFile));
        using var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        w.WriteStartObject();
        w.WriteNumber("lab_format", RunExecutor.RunJsonFormat);
        w.WriteString("status", problems.Count == 0 && threadMismatch.Count == 0 ? "ok" : "failed");
        w.WriteString("experiment", m.Experiment);
        w.WriteString("model", m.Model);
        w.WriteBoolean("model_is_test", ModelRegistry.Find(m.Model).IsTestModel);
        w.WriteString("core_commit", prov.CoreCommit);
        if (prov.Dirty is bool d) w.WriteBoolean("dirty", d); else w.WriteNull("dirty");
        if (prov.DirtyUntrackedSrc is bool u) w.WriteBoolean("dirty_untracked_src", u); else w.WriteNull("dirty_untracked_src");
        w.WriteString("build_commit", prov.BuildCommit);
        w.WriteString("manifest_hash", m.Hash);
        w.WriteString("manifest_path", m.SourcePath);
        w.WriteString("data_hash", prov.DataHash);
        w.WriteStartArray("seeds");
        foreach (ulong s in m.Seeds) w.WriteNumberValue(s);
        w.WriteEndArray();
        w.WriteStartArray("threads");
        foreach (int t in m.Threads) w.WriteNumberValue(t);
        w.WriteEndArray();
        w.WriteNumber("points", m.Points.Count);
        w.WriteNumber("runs", jobs.Count);
        w.WriteNumber("runs_ok", results.Count(r => r.Status == "ok"));
        if (goldenSet is null) w.WriteNull("golden_set_hash"); else w.WriteString("golden_set_hash", goldenSet);
        w.WriteStartArray("problems");
        foreach (string p in problems) w.WriteStringValue(p);
        w.WriteEndArray();
        w.WriteStartArray("thread_mismatch");
        foreach (string p in threadMismatch) w.WriteStringValue(p);
        w.WriteEndArray();
        w.WriteStartArray("merged_csv");
        w.WriteStringValue(RunExecutor.RunsFile);
        foreach (string n in merged) w.WriteStringValue(n);
        w.WriteEndArray();
        w.WriteStartObject("golden");
        for (int i = 0; i < jobs.Count; i++)
        {
            if (results[i].Golden is null) w.WriteNull(jobs[i].RunId); else w.WriteString(jobs[i].RunId, results[i].Golden);
        }
        w.WriteEndObject();
        w.WriteNumber("jobs", jobCount);
        w.WriteString("started_utc", startedUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
        w.WriteNumber("elapsed_ms", elapsedMs);
        w.WriteEndObject();
    }
}
