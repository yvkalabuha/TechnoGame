using System.Buffers.Text;
using System.Globalization;

namespace Singularity.Lab.Bench;

/// <summary>CPU time of the whole machine from the aggregate <c>cpu</c> line of <c>/proc/stat</c>, in USER_HZ ticks summed over all CPUs.</summary>
/// <param name="Busy">user + nice + system + irq + softirq + steal (EXP-0008 П10 п. 1; guest time is already inside user/nice).</param>
/// <param name="Total">Busy + idle + iowait: every tick of every CPU, so ΔTotal = window × logical CPUs × USER_HZ.</param>
public readonly record struct CpuTotals(long Busy, long Total);

/// <summary>One line of <c>/proc/[pid]/stat</c>: pid, comm, utime + stime (USER_HZ ticks, all threads), the start time (ticks after boot) and the parent pid (field 4).</summary>
public readonly record struct PidStat(int Pid, string Comm, long CpuTicks, long StartTime, int Ppid = 0);

/// <summary>
/// Pure parsers of the Linux <c>/proc</c> files the bench reads (EXP-0008 П10, ADR-0023 gate addition). No I/O here: the callers pass the
/// file content, so the fixtures of <c>lab selftest</c> and the unit tests cover every branch. Every parser returns null (or false) on a
/// malformed input instead of throwing: the meter degrades to <c>available: false</c>, it never fails a bench run.
/// The span parsers do not allocate: the meter calls them once per second inside the timed windows.
/// </summary>
public static class ProcParse
{
    /// <summary>The aggregate <c>cpu</c> line (the first line starting with <c>"cpu "</c>) of <c>/proc/stat</c>.</summary>
    public static CpuTotals? CpuLine(ReadOnlySpan<byte> stat)
    {
        while (!stat.IsEmpty)
        {
            int eol = stat.IndexOf((byte)'\n');
            ReadOnlySpan<byte> line = eol < 0 ? stat : stat[..eol];
            stat = eol < 0 ? [] : stat[(eol + 1)..];
            if (!line.StartsWith("cpu "u8))
                continue;
            // user nice system idle iowait irq softirq steal [guest guest_nice]; kernels before 2.6.11 have fewer fields.
            Span<long> f = stackalloc long[8];
            int n = Longs(line[4..], f);
            if (n < 4)
                return null;
            long busy = f[0] + f[1] + f[2] + f[5] + f[6] + f[7];
            long idle = f[3] + f[4];
            return new CpuTotals(busy, busy + idle);
        }
        return null;
    }

    /// <summary>utime + stime (fields 14 and 15) of a <c>/proc/[pid]/stat</c> line. comm may contain spaces and ')': fields start after the last ')'.</summary>
    public static long? StatCpuTicks(ReadOnlySpan<byte> stat)
    {
        int close = stat.LastIndexOf((byte)')');
        if (close < 0)
            return null;
        // After ')': field 3 (state) is index 0, so utime (14) is index 11, stime (15) index 12.
        Span<long> f = stackalloc long[2];
        return Fields(stat[(close + 1)..], 11, f) ? f[0] + f[1] : null;
    }

    /// <summary>The whole <c>/proc/[pid]/stat</c> line: pid, comm, utime + stime, starttime (field 22).</summary>
    public static PidStat? Stat(string line)
    {
        int open = line.IndexOf('(', StringComparison.Ordinal);
        int close = line.LastIndexOf(')');
        if (open <= 0 || close < open)
            return null;
        if (!int.TryParse(line.AsSpan(0, open).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int pid))
            return null;
        string[] rest = line[(close + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // state=0 … utime=11, stime=12, starttime (field 22) = 19.
        if (rest.Length < 20
            || !long.TryParse(rest[11], NumberStyles.None, CultureInfo.InvariantCulture, out long ut)
            || !long.TryParse(rest[12], NumberStyles.None, CultureInfo.InvariantCulture, out long st)
            || !long.TryParse(rest[19], NumberStyles.None, CultureInfo.InvariantCulture, out long start))
            return null;
        // ppid (field 4) = index 1 after ')'.
        int ppid = int.TryParse(rest[1], NumberStyles.None, CultureInfo.InvariantCulture, out int pp) ? pp : 0;
        return new PidStat(pid, line[(open + 1)..close], ut + st, start, ppid);
    }

    /// <summary>The first field of a stat line: the pid.</summary>
    public static int? StatPid(ReadOnlySpan<byte> stat) =>
        Utf8Parser.TryParse(stat.TrimStart((byte)' '), out int pid, out _) ? pid : null;

    /// <summary><c>/proc/loadavg</c>: "1.20 0.95 0.80 3/1234 5678" → (1 min, 5 min, 15 min, runnable, total tasks).</summary>
    public static (double L1, double L5, double L15, int Running, int Tasks)? Loadavg(string text)
    {
        string[] f = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length < 4)
            return null;
        string[] rt = f[3].Split('/');
        if (!double.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double l1)
            || !double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double l5)
            || !double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double l15)
            || rt.Length != 2
            || !int.TryParse(rt[0], NumberStyles.None, CultureInfo.InvariantCulture, out int run)
            || !int.TryParse(rt[1], NumberStyles.None, CultureInfo.InvariantCulture, out int tasks))
            return null;
        return (l1, l5, l15, run, tasks);
    }

    /// <summary><c>/proc/[pid]/task/[tid]/schedstat</c>: "run_ns wait_ns timeslices" → the run-queue wait (field 2), ns, cumulative.</summary>
    public static long? SchedstatWaitNs(string text)
    {
        string[] f = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return f.Length >= 2 && long.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out long w) ? w : null;
    }

    /// <summary>The <c>nonvoluntary_ctxt_switches</c> line of <c>/proc/[pid]/task/[tid]/status</c>.</summary>
    public static long? NonvoluntarySwitches(string status)
    {
        foreach (string line in status.Split('\n'))
        {
            if (!line.StartsWith("nonvoluntary_ctxt_switches:", StringComparison.Ordinal))
                continue;
            return long.TryParse(line.AsSpan("nonvoluntary_ctxt_switches:".Length).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long n)
                ? n : null;
        }
        return null;
    }

    // Space-separated non-negative integers into f; returns how many were read (stops at the first non-number or when f is full).
    private static int Longs(ReadOnlySpan<byte> s, Span<long> f)
    {
        int n = 0;
        while (n < f.Length)
        {
            s = s.TrimStart((byte)' ');
            if (s.IsEmpty || !Utf8Parser.TryParse(s, out long v, out int used) || v < 0)
                break;
            f[n++] = v;
            s = s[used..];
        }
        return n;
    }

    // Fields skip, skip + 1, … (0-based, space-separated) into f; false when the line is too short or a field is not a number.
    private static bool Fields(ReadOnlySpan<byte> s, int skip, Span<long> f)
    {
        for (int i = 0; i < skip; i++)
        {
            s = s.TrimStart((byte)' ');
            int sp = s.IndexOf((byte)' ');
            if (sp < 0)
                return false;
            s = s[sp..];
        }
        return Longs(s, f) == f.Length;
    }
}
