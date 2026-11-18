namespace Singularity.Lab.Bench;

/// <summary>One attempt of a series process as the gate sees it (read back from the attempt JSON).</summary>
/// <param name="Number">0 = first attempt (main pass), 1 and 2 = repeats.</param>
/// <param name="MedianTMs">Median month time of the daily run (T), ms; null for an R mode (no (м) signal).</param>
/// <param name="ForeignCpus">(н1) mean foreign load of the union window in absolute CPUs; null when the meter was unavailable.</param>
/// <param name="ForeignOverShare">(н1) share of the window's seconds above the per-second level; null without a per-second series.</param>
/// <param name="RunDelay">(н2) run-queue wait share of the own threads; null when unavailable.</param>
/// <param name="Injected">Smoke only: an artificial (н1) flag (EXP-0008 П10 п. 7).</param>
/// <param name="Virtual">A repeat not started because the #355 window ended (ADR-0023 п. 1.9): counts as an attempt flagged by (м) only.</param>
public sealed record GateAttempt(int Number, double? MedianTMs, double? ForeignCpus, double? ForeignOverShare, double? RunDelay,
    bool Injected = false, bool Virtual = false);

/// <summary>A process of the gate: its plan index, block, group key of med_point, seed and replica.</summary>
/// <param name="Group">
/// The med_point group: (block, point, threads, background). EXP-0008 П5.3 says «point and block»; C1 holds t4 and t16 of the same point
/// and would mix two thread counts, so threads and background are part of the key (TASK-0106, EXP-0008 «Разногласия»).
/// </param>
public sealed record GateProcess(int Index, string Block, string Group, ulong Seed, int Replica);

/// <summary>The gate evaluation of one attempt.</summary>
public sealed record AttemptEval(bool Flag, bool Evidence, IReadOnlyList<string> Reasons);

public enum ProcessStatus { NeedsRepeat, Accepted, Excluded }

/// <summary>A forbidden process seen in a foreign list (#355): where, pid, comm, mean CPUs over the window, and whether it is fatal.</summary>
public sealed record ForbiddenHit(string Where, int Pid, string Comm, double Cpus, bool Fatal)
{
    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Where}: {Comm} (pid {Pid}, {Cpus:F3} CPU{(Fatal ? "" : ", below the limit")})");
}

/// <summary>The decision on a process after its attempts so far.</summary>
/// <param name="AcceptedAttempt">The attempt number that enters the statistics (Accepted only).</param>
/// <param name="AcceptedBy">"clean" (first attempt without a flag) or "no-evidence" (independent-evidence rule: every attempt flagged, but not every one has evidence).</param>
public sealed record ProcessDecision(ProcessStatus Status, int? AcceptedAttempt, string? AcceptedBy, IReadOnlyList<AttemptEval> Evals);

/// <summary>The series verdict of EXP-0008 П5.4 / ADR-0023 п. 1.7 (exclusion part; the noise criterion is computed by the analysis).</summary>
public sealed record SeriesVerdict(bool Inconclusive, IReadOnlyList<string> Reasons, int Excluded, int Processes, int OutcomeExcluded, int OutcomeProcesses,
    IReadOnlyDictionary<ulong, (int Accepted, int Total)> OutcomeSeeds);

/// <summary>
/// Pure decision logic of <c>lab bench --mode series</c> (EXP-0008 П5.3–П5.4, ADR-0023 п. 1.3–1.7 and the gate addition). No I/O, no
/// clock: the driver feeds the attempt records in and runs what <see cref="Decide"/> asks for. Every threshold comes from
/// <see cref="BenchGate"/>. Frequency is not an input: it decides nothing (ADR-0023 п. 1.2).
/// </summary>
public static class SeriesGate
{
    /// <summary>
    /// med_point of every group: the median of the first-attempt medians of its processes (EXP-0008 П5.3). Computed once after the main
    /// pass and never recomputed for the repeats: the driver passes the same dictionary to every later call.
    /// </summary>
    public static Dictionary<string, double> MedPoints(IReadOnlyList<GateProcess> processes, IReadOnlyDictionary<int, GateAttempt> firstAttempts)
    {
        var byGroup = new SortedDictionary<string, List<double>>(StringComparer.Ordinal);
        foreach (GateProcess p in processes)
        {
            if (!firstAttempts.TryGetValue(p.Index, out GateAttempt? a) || a.MedianTMs is not double m)
                continue;
            if (!byGroup.TryGetValue(p.Group, out List<double>? list))
                byGroup[p.Group] = list = [];
            list.Add(m);
        }
        var r = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach ((string g, List<double> ms) in byGroup)
            r[g] = BenchStats.Median(ms);
        return r;
    }

    /// <summary>
    /// Signals of one attempt (ADR-0023 п. 1.3): (н1) foreign CPUs mean &gt; max or share of seconds above the level &gt; max; (н2) run delay &gt;
    /// max; (м) median T &gt; ratio × med_point, only when med_point ≥ the minimum. Any signal is a flag (п. 1.4). (н1) and (н2) are the
    /// independent evidence of foreign load; (м) is not (it selects by the outcome value itself).
    /// </summary>
    public static AttemptEval Evaluate(GateAttempt a, double? medPoint, BenchGate g)
    {
        var reasons = new List<string>();
        if (a.Virtual)
            return new AttemptEval(true, false, ["not_started_window_end(м)"]);
        bool n1 = a.Injected || a.ForeignCpus is double f && f > g.ForeignCpusMax || a.ForeignOverShare is double o && o > g.ForeignOverShareMax;
        if (a.Injected) reasons.Add("injected(н1)");
        if (a.ForeignCpus is double f2 && f2 > g.ForeignCpusMax) reasons.Add("н1_mean");
        if (a.ForeignOverShare is double o2 && o2 > g.ForeignOverShareMax) reasons.Add("н1_seconds");
        bool n2 = a.RunDelay is double d && d > g.RunDelayMax;
        if (n2) reasons.Add("н2");
        bool m = a.MedianTMs is double t && medPoint is double mp && mp >= g.MedianGateMinMs && t > g.MedianRatioMax * mp;
        if (m) reasons.Add("м");
        return new AttemptEval(n1 || n2 || m, n1 || n2, reasons);
    }

    /// <summary>
    /// The state of a process after its attempts (in order, 0-based numbers; ADR-0023 п. 1.4–1.5): accepted with the first attempt without
    /// a flag; a repeat while fewer than 1 + max_repeats attempts exist; after the last one excluded only if every attempt has (н1) or (н2)
    /// (rule <c>independent-evidence</c>; <c>any-flag</c> excludes on any flags), otherwise accepted with the first attempt without
    /// independent evidence. A virtual attempt (window end) has no data: then the first real attempt without evidence is taken, else the
    /// first real attempt ("window-end").
    /// </summary>
    public static ProcessDecision Decide(IReadOnlyList<GateAttempt> attempts, double? medPoint, BenchGate g)
    {
        if (attempts.Count == 0)
            throw new ArgumentException("A process has at least its first attempt.", nameof(attempts));
        if (attempts.Count > 1 + g.MaxRepeats)
            throw new ArgumentException($"{attempts.Count} attempts exceed 1 + max_repeats = {1 + g.MaxRepeats}.", nameof(attempts));
        if (attempts[0].Virtual)
            throw new ArgumentException("The first attempt is always run.", nameof(attempts));
        var evals = new List<AttemptEval>(attempts.Count);
        for (int i = 0; i < attempts.Count; i++)
        {
            if (attempts[i].Number != i)
                throw new ArgumentException("Attempts must be numbered 0, 1, 2, … in order.", nameof(attempts));
            AttemptEval e = Evaluate(attempts[i], medPoint, g);
            evals.Add(e);
            if (!e.Flag)
                return new ProcessDecision(ProcessStatus.Accepted, i, "clean", evals);
        }
        bool hasVirtual = attempts.Any(a => a.Virtual);
        if (attempts.Count < 1 + g.MaxRepeats && !hasVirtual)
            return new ProcessDecision(ProcessStatus.NeedsRepeat, null, null, evals);
        if (g.ExclusionRule == ExclusionRule.IndependentEvidence)
        {
            int noEvidence = Enumerable.Range(0, attempts.Count).FirstOrDefault(i => !attempts[i].Virtual && !evals[i].Evidence, -1);
            if (noEvidence >= 0)
                return new ProcessDecision(ProcessStatus.Accepted, noEvidence, "no-evidence", evals);
            if (hasVirtual)
                return new ProcessDecision(ProcessStatus.Accepted, 0, "window-end", evals);
        }
        return new ProcessDecision(ProcessStatus.Excluded, null, null, evals);
    }

    /// <summary>
    /// #355 check (ADR-0023 п. 1.1): the foreign processes whose comm starts with a forbidden name (case-insensitive), skipping
    /// <paramref name="excluded"/> (the driver and its ancestor chain). Fatal = mean CPUs over the window ≥ <c>forbidden_min_cpus</c>;
    /// a hit below it is recorded only.
    /// </summary>
    public static List<ForbiddenHit> Forbidden(IEnumerable<(int Pid, string Comm, double Cpus)> procs, BenchGate g, IReadOnlySet<int> excluded, string where) =>
        [.. procs.Where(p => !excluded.Contains(p.Pid) && g.ForbiddenProcesses.Any(n => p.Comm.StartsWith(n, StringComparison.OrdinalIgnoreCase)))
            .Select(p => new ForbiddenHit(where, p.Pid, p.Comm, p.Cpus, p.Cpus >= g.ForbiddenMinCpus))];

    /// <summary>
    /// The exclusion part of INCONCLUSIVE (ADR-0023 п. 1.7, EXP-0008 П5.4): in the outcome block excluded &gt; frac; a seed of the outcome block
    /// with accepted ≤ seed_accept_min_frac × its processes; over the series excluded &gt; frac. Only final decisions may be passed.
    /// </summary>
    public static SeriesVerdict Verdict(IReadOnlyList<GateProcess> processes, IReadOnlyDictionary<int, ProcessDecision> decisions, BenchGate g)
    {
        var reasons = new List<string>();
        int excluded = 0, outEx = 0, outN = 0;
        var seeds = new SortedDictionary<ulong, (int Accepted, int Total)>();
        foreach (GateProcess p in processes)
        {
            ProcessDecision d = decisions.TryGetValue(p.Index, out ProcessDecision? x) ? x : throw new ArgumentException($"no decision for process {p.Index}");
            if (d.Status == ProcessStatus.NeedsRepeat)
                throw new ArgumentException($"process {p.Index} is not decided yet");
            bool ex = d.Status == ProcessStatus.Excluded;
            excluded += ex ? 1 : 0;
            if (p.Block != g.OutcomeBlock)
                continue;
            outN++;
            outEx += ex ? 1 : 0;
            (int acc, int tot) = seeds.TryGetValue(p.Seed, out var s) ? s : (0, 0);
            seeds[p.Seed] = (acc + (ex ? 0 : 1), tot + 1);
        }
        if (outN > 0 && outEx > g.ExcludeMaxFrac * outN)
            reasons.Add($"outcome block {g.OutcomeBlock}: {outEx} of {outN} excluded > {g.ExcludeMaxFrac * 100:F0} %");
        foreach ((ulong seed, (int acc, int tot)) in seeds)
        {
            if (acc <= g.SeedAcceptMinFrac * tot)
                reasons.Add($"outcome block {g.OutcomeBlock} seed {seed}: {acc} of {tot} accepted (needs > {g.SeedAcceptMinFrac * 100:F0} %)");
        }
        if (processes.Count > 0 && excluded > g.ExcludeMaxFrac * processes.Count)
            reasons.Add($"series: {excluded} of {processes.Count} excluded > {g.ExcludeMaxFrac * 100:F0} %");
        return new SeriesVerdict(reasons.Count > 0, reasons, excluded, processes.Count, outEx, outN, seeds);
    }
}
