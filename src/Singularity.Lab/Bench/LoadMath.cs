namespace Singularity.Lab.Bench;

/// <summary>One foreign process of the top list: CPU ticks in the window and the share of the whole machine (Δticks / ΔTotal of /proc/stat).</summary>
public sealed record ForeignProcess(int Pid, string Comm, long CpuTicks, double MachineShare);

/// <summary>Pure arithmetic of the machine-load records (EXP-0008 П5.2, П5.3, П5.5). Host-side only, never world state.</summary>
public static class LoadMath
{
    /// <summary>
    /// foreign_cpu = (Δ busy of the machine − Δ CPU of the own process) / Δ total of the machine (EXP-0008 П5.3). ΔTotal counts every tick of
    /// every CPU, so it equals window × logical CPUs × USER_HZ without needing USER_HZ. Null when ΔTotal ≤ 0. The value is not clamped: tick
    /// granularity can make it slightly negative on an idle machine; the gate compares it as is.
    /// </summary>
    public static double? ForeignCpu(long dBusy, long dTotal, long dSelf) =>
        dTotal <= 0 ? null : (dBusy - dSelf) / (double)dTotal;

    /// <summary>Mean of the k largest values (EXP-0008 П5.2 top-k); k is clamped to [1, count]. Null on an empty input.</summary>
    public static double? TopKMean(ReadOnlySpan<double> values, int k)
    {
        if (values.IsEmpty)
            return null;
        k = Math.Clamp(k, 1, values.Length);
        Span<double> x = values.Length <= 256 ? stackalloc double[values.Length] : new double[values.Length];
        values.CopyTo(x);
        x.Sort();
        double sum = 0;
        for (int i = x.Length - k; i < x.Length; i++)
            sum += x[i];
        return sum / k;
    }

    /// <summary>Frequency flag of EXP-0008 П5.2: median top-k frequency below ratio × f_ref(k). Null when either value is missing.</summary>
    public static bool? PossibleThermalThrottle(double? topkMedianMhz, double? fRefMhz, double ratio) =>
        topkMedianMhz is double f && fRefMhz is double r ? f < ratio * r : null;

    /// <summary>
    /// The n foreign processes with the most CPU in the window (EXP-0008 П5.5): Δ(utime + stime) between the start and the end snapshot,
    /// own pid excluded. A process is the same one when pid and start time agree; one that is only in the end snapshot started inside the
    /// window and counts all its time. Ties: by pid. Share = Δticks / ΔTotal (fraction of the whole machine).
    /// </summary>
    public static List<ForeignProcess> TopForeign(IReadOnlyList<PidStat> start, IReadOnlyList<PidStat> end, int selfPid, long dTotal, int n)
    {
        var before = new Dictionary<(int, long), long>(start.Count);
        foreach (PidStat p in start)
            before[(p.Pid, p.StartTime)] = p.CpuTicks;
        var list = new List<ForeignProcess>();
        foreach (PidStat p in end)
        {
            if (p.Pid == selfPid)
                continue;
            long d = p.CpuTicks - (before.TryGetValue((p.Pid, p.StartTime), out long b) ? b : 0);
            if (d > 0)
                list.Add(new ForeignProcess(p.Pid, p.Comm, d, dTotal > 0 ? d / (double)dTotal : 0));
        }
        list.Sort((x, y) => x.CpuTicks != y.CpuTicks ? y.CpuTicks.CompareTo(x.CpuTicks) : x.Pid.CompareTo(y.Pid));
        return list.Count > n ? list.GetRange(0, n) : list;
    }

    /// <summary>
    /// The ancestor chain of <paramref name="pid"/> (parent, grandparent, … up to pid 1 or an unreadable entry) through the ppid of each
    /// <c>/proc/[pid]/stat</c>. Used by the #355 check: the session that launched the series driver is not a foreign build or agent.
    /// Stops on a cycle or after 64 levels.
    /// </summary>
    public static List<PidStat> Ancestors(int pid, Func<int, PidStat?> stat)
    {
        var chain = new List<PidStat>();
        var seen = new HashSet<int> { pid };
        int cur = pid;
        for (int i = 0; i < 64; i++)
        {
            if (stat(cur) is not PidStat s || s.Ppid <= 0 || !seen.Add(s.Ppid) || stat(s.Ppid) is not PidStat parent)
                break;
            chain.Add(parent);
            cur = parent.Pid;
        }
        return chain;
    }

    /// <summary>Sums the top lists of several windows by (pid, comm) and keeps the n largest (the union window of a process).</summary>
    public static List<ForeignProcess> MergeTop(IEnumerable<IReadOnlyList<ForeignProcess>> windows, long dTotal, int n)
    {
        var sum = new Dictionary<(int, string), long>();
        foreach (IReadOnlyList<ForeignProcess> w in windows)
        {
            foreach (ForeignProcess p in w)
                sum[(p.Pid, p.Comm)] = (sum.TryGetValue((p.Pid, p.Comm), out long s) ? s : 0) + p.CpuTicks;
        }
        var list = sum.Select(kv => new ForeignProcess(kv.Key.Item1, kv.Key.Item2, kv.Value, dTotal > 0 ? kv.Value / (double)dTotal : 0)).ToList();
        list.Sort((x, y) => x.CpuTicks != y.CpuTicks ? y.CpuTicks.CompareTo(x.CpuTicks) : x.Pid.CompareTo(y.Pid));
        return list.Count > n ? list.GetRange(0, n) : list;
    }

    /// <summary>
    /// Run-queue wait of the own threads per window and thread (ADR-0023 gate addition (a)): Σ Δ schedstat wait (ns) / (window ns × threads).
    /// A thread that is only in the end snapshot started inside the window and counts all its wait. Null when the window or threads are ≤ 0.
    /// </summary>
    public static double? RunDelayShare(IReadOnlyDictionary<int, long> startWaitNs, IReadOnlyDictionary<int, long> endWaitNs, double windowNs, int threads)
    {
        if (windowNs <= 0 || threads <= 0)
            return null;
        return SumDelta(startWaitNs, endWaitNs) / (windowNs * threads);
    }

    /// <summary>Σ over the threads of the end snapshot of (end − start), a missing start counting as 0. Exited threads are lost (they took their counters).</summary>
    public static long SumDelta(IReadOnlyDictionary<int, long> start, IReadOnlyDictionary<int, long> end)
    {
        long sum = 0;
        foreach ((int tid, long e) in end)
            sum += Math.Max(0, e - (start.TryGetValue(tid, out long s) ? s : 0));
        return sum;
    }
}
