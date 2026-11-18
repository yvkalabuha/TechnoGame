using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Singularity.Lab.Bench;

/// <summary>A point-in-time reading of the machine (EXP-0008 П5.5): taken at the start and the end of every timed window, outside it.</summary>
public sealed class MachineSnapshot
{
    public required long Ts { get; init; }
    public CpuTotals? Cpu { get; init; }
    public long? SelfTicks { get; init; }
    /// <summary>Own CPU user + sys, seconds, from <c>Process.TotalProcessorTime</c> (available on every OS; ADR-0023 gate addition (b)).</summary>
    public required double SelfCpuSeconds { get; init; }
    public string? Loadavg { get; init; }
    public IReadOnlyList<PidStat>? Pids { get; init; }
    public IReadOnlyDictionary<int, long>? TaskWaitNs { get; init; }
    public IReadOnlyDictionary<int, long>? TaskNvcsw { get; init; }
}

/// <summary>One timed window: its start and end snapshot.</summary>
public sealed record MachineWindow(string Name, MachineSnapshot Start, MachineSnapshot End);

/// <summary>
/// The machine-load meter of EXP-0008 П10 п. 1–2 and the ADR-0023 gate addition: once per second it reads the aggregate <c>cpu</c> line of
/// <c>/proc/stat</c> and utime + stime of <c>/proc/self/stat</c> (per-second <c>foreign_cpu</c> series); at the window bounds
/// (<see cref="Snapshot"/>) also <c>/proc/loadavg</c>, every <c>/proc/[pid]/stat</c> (top-5 foreign processes) and the schedstat wait and
/// non-voluntary context switches of the own threads. <paramref name="procRoot"/> lets the fixtures replace <c>/proc</c>.
/// Without a readable <c>/proc/stat</c> and <c>/proc/self/stat</c> the meter is <see cref="Available"/> = false with a <see cref="Reason"/>
/// and writes <c>available: false</c>; it never throws, so a bench run on Windows or macOS is not failed by it.
/// Like <see cref="FreqSampler"/> the per-second loop does not allocate in steady state (handles opened once, <c>pread</c> into a reused
/// buffer, span parsers); what it allocates is in <see cref="AllocatedBytes"/>. The snapshots allocate and are taken outside the
/// allocation counts of the runs.
/// </summary>
public sealed class LoadMeter : IDisposable
{
    private const int InitialCapacity = 1 << 14;
    private readonly string _root;
    private readonly SafeFileHandle? _stat, _self;
    private readonly byte[] _buffer = new byte[16384];
    private (long Ts, long Busy, long Total, long Self)[] _series = new (long, long, long, long)[InitialCapacity];
    private int _count;
    private readonly object _lock = new();
    private readonly Thread? _thread;
    private readonly int _intervalMs;
    private long _allocated;
    private volatile bool _stop;

    public LoadMeter(string procRoot = "/proc", int intervalMs = 1000, bool background = true)
    {
        _root = procRoot;
        _intervalMs = intervalMs;
        try
        {
            if (!File.Exists(Path.Combine(procRoot, "stat")) || !File.Exists(Path.Combine(procRoot, "self", "stat")))
            {
                Reason = $"{procRoot}/stat or {procRoot}/self/stat not found (not Linux?)";
                return;
            }
            _stat = File.OpenHandle(Path.Combine(procRoot, "stat"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            _self = File.OpenHandle(Path.Combine(procRoot, "self", "stat"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int len = ReadAll(_self);
            SelfPid = ProcParse.StatPid(_buffer.AsSpan(0, len)) ?? Environment.ProcessId;
            if (ReadPair() is null)
            {
                Reason = $"{procRoot}/stat or {procRoot}/self/stat could not be parsed";
                _stat.Dispose();
                _self.Dispose();
                _stat = _self = null;
                return;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Reason = $"{procRoot}: {e.GetType().Name}: {e.Message}";
            _stat?.Dispose();
            _self?.Dispose();
            _stat = _self = null;
            return;
        }
        Available = true;
        if (background)
        {
            _thread = new Thread(Loop) { IsBackground = true, Name = "bench-load" };
            _thread.Start();
        }
    }

    public bool Available { get; }
    public string? Reason { get; }
    public int SelfPid { get; } = Environment.ProcessId;

    /// <summary>Bytes the per-second loop allocated since start (0 in steady state).</summary>
    public long AllocatedBytes => Interlocked.Read(ref _allocated);

    public IReadOnlyList<(long Ts, long Busy, long Total, long Self)> Series
    {
        get { lock (_lock) return _series.AsSpan(0, _count).ToArray(); }
    }

    /// <summary>Reads the machine now. <paramref name="full"/>: also every pid (top list) and the own threads (schedstat, status).</summary>
    public MachineSnapshot Snapshot(bool full = true)
    {
        long ts = Stopwatch.GetTimestamp();
        double selfCpu = SelfCpu();
        if (!Available)
            return new MachineSnapshot { Ts = ts, SelfCpuSeconds = selfCpu };
        (CpuTotals Cpu, long Self)? pair;
        lock (_buffer)
            pair = ReadPair();
        string? load = BenchEnv.ReadTrim(Path.Combine(_root, "loadavg"));
        return new MachineSnapshot
        {
            Ts = ts,
            Cpu = pair?.Cpu,
            SelfTicks = pair?.Self,
            SelfCpuSeconds = selfCpu,
            Loadavg = load,
            Pids = full ? ScanPids() : null,
            TaskWaitNs = full ? ScanTasks("schedstat", ProcParse.SchedstatWaitNs) : null,
            TaskNvcsw = full ? ScanTasks("status", ProcParse.NonvoluntarySwitches) : null,
        };
    }

    private static double SelfCpu()
    {
        try
        {
            using Process p = Process.GetCurrentProcess();
            return p.TotalProcessorTime.TotalSeconds;
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or PlatformNotSupportedException)
        {
            return double.NaN;
        }
    }

    private List<PidStat> ScanPids()
    {
        var list = new List<PidStat>();
        IEnumerable<string> dirs;
        try { dirs = Directory.EnumerateDirectories(_root); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return list; }
        foreach (string d in dirs)
        {
            string name = Path.GetFileName(d);
            if (name.Length == 0 || !name.All(char.IsAsciiDigit))
                continue;
            // A process may exit between the listing and the read: skip it.
            if (BenchEnv.ReadTrim(Path.Combine(d, "stat")) is string line && ProcParse.Stat(line) is PidStat p)
                list.Add(p);
        }
        list.Sort((a, b) => a.Pid.CompareTo(b.Pid));
        return list;
    }

    private Dictionary<int, long> ScanTasks(string file, Func<string, long?> parse)
    {
        var map = new Dictionary<int, long>();
        string tasks = Path.Combine(_root, "self", "task");
        IEnumerable<string> dirs;
        try { dirs = Directory.Exists(tasks) ? Directory.EnumerateDirectories(tasks) : []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return map; }
        foreach (string d in dirs)
        {
            if (!int.TryParse(Path.GetFileName(d), NumberStyles.None, CultureInfo.InvariantCulture, out int tid))
                continue;
            if (BenchEnv.ReadTrim(Path.Combine(d, file)) is string text && parse(text) is long v)
                map[tid] = v;
        }
        return map;
    }

    private void Loop()
    {
        while (!_stop)
        {
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            (CpuTotals Cpu, long Self)? r;
            lock (_buffer)
                r = ReadPair();
            if (r is { } v)
            {
                lock (_lock)
                {
                    if (_count == _series.Length)
                        Array.Resize(ref _series, _series.Length * 2);
                    _series[_count++] = (Stopwatch.GetTimestamp(), v.Cpu.Busy, v.Cpu.Total, v.Self);
                }
            }
            Interlocked.Add(ref _allocated, GC.GetAllocatedBytesForCurrentThread() - a0);
            Thread.Sleep(_intervalMs);
        }
    }

    // Caller holds the _buffer lock (or is the constructor).
    private (CpuTotals Cpu, long Self)? ReadPair()
    {
        if (_stat is null || _self is null)
            return null;
        try
        {
            CpuTotals? c = ProcParse.CpuLine(_buffer.AsSpan(0, ReadAll(_stat)));
            long? s = ProcParse.StatCpuTicks(_buffer.AsSpan(0, ReadAll(_self)));
            return c is CpuTotals cc && s is long ss ? (cc, ss) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            return null;
        }
    }

    // /proc/stat of a large machine fits in 16 KB; a longer file is cut, which only drops the per-CPU lines after the aggregate one.
    private int ReadAll(SafeFileHandle h)
    {
        int len = 0;
        while (len < _buffer.Length)
        {
            int n = RandomAccess.Read(h, _buffer.AsSpan(len), len);
            if (n <= 0)
                break;
            len += n;
        }
        return len;
    }

    public void Dispose()
    {
        _stop = true;
        _thread?.Join();
        lock (_buffer)
        {
            _stat?.Dispose();
            _self?.Dispose();
        }
    }

    // ---------------- records ----------------

    /// <summary>
    /// foreign_cpu of the union of the windows (EXP-0008 П5.3): Σ Δbusy − Σ Δself over Σ Δtotal. Null when the meter is unavailable or a bound
    /// could not be read.
    /// </summary>
    public static double? ForeignCpu(IReadOnlyList<MachineWindow> windows)
    {
        long busy = 0, total = 0, self = 0;
        foreach (MachineWindow w in windows)
        {
            if (w.Start.Cpu is not CpuTotals a || w.End.Cpu is not CpuTotals b || w.Start.SelfTicks is not long sa || w.End.SelfTicks is not long sb)
                return null;
            busy += b.Busy - a.Busy;
            total += b.Total - a.Total;
            self += sb - sa;
        }
        return LoadMath.ForeignCpu(busy, total, self);
    }

    /// <summary>Run-queue wait share of the own threads over the union of the windows; <paramref name="threads"/> = process threads + background.</summary>
    public static double? RunDelayShare(IReadOnlyList<MachineWindow> windows, int threads)
    {
        long wait = 0;
        double ns = 0;
        foreach (MachineWindow w in windows)
        {
            if (w.Start.TaskWaitNs is not { } a || w.End.TaskWaitNs is not { } b || b.Count == 0)
                return null;
            wait += LoadMath.SumDelta(a, b);
            ns += (w.End.Ts - w.Start.Ts) * 1e9 / Stopwatch.Frequency;
        }
        return ns <= 0 || threads <= 0 ? null : wait / (ns * threads);
    }

    /// <summary>
    /// Writes the <c>machine</c> block (EXP-0008 П5.5, ADR-0023 gate addition): availability, per window (loadavg at start and end,
    /// foreign_cpu, own CPU, wall, run delay, non-voluntary switches, top-5), the union, and the per-second foreign_cpu series inside the
    /// windows. <paramref name="threads"/> = process threads + background threads (the k of П5.2).
    /// </summary>
    public void Write(Utf8JsonWriter j, IReadOnlyList<MachineWindow> windows, int threads, int top = 5)
    {
        j.WriteStartObject("machine");
        j.WriteBoolean("available", Available);
        if (Reason is null) j.WriteNull("reason"); else j.WriteString("reason", Reason);
        j.WriteNumber("logical_cpus", Environment.ProcessorCount);
        j.WriteNumber("threads_k", threads);
        j.WriteNumber("meter_alloc_bytes_total", AllocatedBytes);
        j.WriteStartArray("windows");
        var tops = new List<IReadOnlyList<ForeignProcess>>();
        long totalAll = 0;
        foreach (MachineWindow w in windows)
        {
            j.WriteStartObject();
            j.WriteString("name", w.Name);
            double wall = (w.End.Ts - w.Start.Ts) / (double)Stopwatch.Frequency;
            j.WriteNumber("wall_s", wall);
            WriteStr(j, "loadavg_start", w.Start.Loadavg);
            WriteStr(j, "loadavg_end", w.End.Loadavg);
            WriteNum(j, "foreign_cpu", ForeignCpu([w]));
            double selfCpu = w.End.SelfCpuSeconds - w.Start.SelfCpuSeconds;
            WriteNum(j, "self_cpu_s", double.IsNaN(selfCpu) ? null : selfCpu);
            WriteNum(j, "self_cpu_ticks", w.Start.SelfTicks is long a && w.End.SelfTicks is long b ? b - a : null);
            WriteNum(j, "run_delay_share", RunDelayShare([w], threads));
            WriteNum(j, "run_delay_ns", w.Start.TaskWaitNs is { } ws && w.End.TaskWaitNs is { Count: > 0 } we ? LoadMath.SumDelta(ws, we) : null);
            WriteNum(j, "nonvoluntary_ctxt_switches", w.Start.TaskNvcsw is { } ns && w.End.TaskNvcsw is { Count: > 0 } ne ? LoadMath.SumDelta(ns, ne) : null);
            if (w.Start.Cpu is CpuTotals c0 && w.End.Cpu is CpuTotals c1 && w.Start.Pids is { } p0 && w.End.Pids is { } p1)
            {
                long dTotal = c1.Total - c0.Total;
                totalAll += dTotal;
                List<ForeignProcess> t = LoadMath.TopForeign(p0, p1, SelfPid, dTotal, top);
                tops.Add(LoadMath.TopForeign(p0, p1, SelfPid, dTotal, int.MaxValue));
                WriteTop(j, "top_foreign", t);
            }
            else
            {
                j.WriteNull("top_foreign");
            }
            j.WriteEndObject();
        }
        j.WriteEndArray();
        WriteNum(j, "foreign_cpu_window", ForeignCpu(windows));
        WriteNum(j, "run_delay_share_window", RunDelayShare(windows, threads));
        double self = windows.Sum(w => w.End.SelfCpuSeconds - w.Start.SelfCpuSeconds);
        WriteNum(j, "self_cpu_s_window", double.IsNaN(self) ? null : self);
        j.WriteNumber("wall_s_window", windows.Sum(w => (w.End.Ts - w.Start.Ts) / (double)Stopwatch.Frequency));
        if (tops.Count == windows.Count && windows.Count > 0)
            WriteTop(j, "top_foreign_window", LoadMath.MergeTop(tops, totalAll, top));
        else
            j.WriteNull("top_foreign_window");
        // Per-second series: one value per pair of consecutive samples that both fall inside a window.
        var series = Series;
        j.WriteStartArray("foreign_cpu_series");
        for (int i = 1; i < series.Count; i++)
        {
            var (t0, b0, tt0, s0) = series[i - 1];
            var (t1, b1, tt1, s1) = series[i];
            if (!windows.Any(w => t0 >= w.Start.Ts && t1 <= w.End.Ts))
                continue;
            if (LoadMath.ForeignCpu(b1 - b0, tt1 - tt0, s1 - s0) is double f)
                j.WriteNumberValue(Math.Round(f, 6));
        }
        j.WriteEndArray();
        j.WriteEndObject();
    }

    private static void WriteTop(Utf8JsonWriter j, string name, IReadOnlyList<ForeignProcess> top)
    {
        j.WriteStartArray(name);
        foreach (ForeignProcess p in top)
        {
            j.WriteStartObject();
            j.WriteNumber("pid", p.Pid);
            j.WriteString("comm", p.Comm);
            j.WriteNumber("cpu_ticks", p.CpuTicks);
            j.WriteNumber("machine_share", Math.Round(p.MachineShare, 6));
            j.WriteNumber("cores", Math.Round(p.MachineShare * Environment.ProcessorCount, 4));
            j.WriteEndObject();
        }
        j.WriteEndArray();
    }

    private static void WriteStr(Utf8JsonWriter j, string name, string? v)
    {
        if (v is null) j.WriteNull(name); else j.WriteString(name, v);
    }

    internal static void WriteNum(Utf8JsonWriter j, string name, double? v)
    {
        if (v is double d && double.IsFinite(d)) j.WriteNumber(name, d); else j.WriteNull(name);
    }
}
