using System.Buffers.Text;
using System.Diagnostics;
using Microsoft.Win32.SafeHandles;

namespace Singularity.Lab.Bench;

/// <summary>
/// Samples the frequency (MHz) of every logical CPU once per interval: <c>scaling_cur_freq</c> of every
/// <c>/sys/devices/system/cpu/cpu*/cpufreq/</c>, else the «cpu MHz» lines of <c>/proc/cpuinfo</c>. Time stamps are Stopwatch ticks of the host.
/// Per-CPU values are kept (EXP-0008 П10 п. 3): under <c>powersave</c> the mean over all CPUs measures how many cores are busy, not the
/// frequency (EXP-0007 «Ошибки и аномалии»), so the record is the mean of the top-k values of every sample, k = the busy threads.
/// The frequency decides nothing (ADR-0023 п. 1.2): it only gives the flag «possible thermal throttling».
/// The sampler thread runs inside the timed windows and <c>GC.GetTotalAllocatedBytes</c> counts every thread, so the loop does not
/// allocate in steady state: file handles are opened once and re-read with <c>pread</c> at offset 0 into a reused byte buffer, parsed
/// with <see cref="Utf8Parser"/>, and samples go into pre-sized arrays. What it still allocates (growth) is in <see cref="AllocatedBytes"/>.
/// </summary>
public sealed class FreqSampler : IDisposable
{
    private const int InitialCapacity = 1 << 12;
    private readonly int _width;
    private long[] _ts = new long[InitialCapacity];
    private double[] _values;
    private int[] _n = new int[InitialCapacity];
    private int _count;
    private readonly object _lock = new();
    private readonly SafeFileHandle[] _handles;
    private byte[] _buffer = new byte[4096];
    private readonly double[] _row;
    private readonly Thread _thread;
    private readonly int _intervalMs;
    private long _allocated;
    private volatile bool _stop;

    public FreqSampler(int intervalMs = 1000)
    {
        _intervalMs = intervalMs;
        (Source, _handles) = OpenSource();
        _width = Math.Max(1, Source == "scaling_cur_freq" ? _handles.Length : Environment.ProcessorCount);
        _values = new double[InitialCapacity * _width];
        _row = new double[_width];
        _thread = new Thread(Loop) { IsBackground = true, Name = "bench-freq" };
        _thread.Start();
    }

    /// <summary>"scaling_cur_freq", "proc_cpuinfo" or "none".</summary>
    public string Source { get; }

    /// <summary>Bytes the sampler thread allocated in its loop since start (0 in steady state).</summary>
    public long AllocatedBytes => Interlocked.Read(ref _allocated);

    /// <summary>Mean over all CPUs of every sample (the EXP-0007 metric, kept as a record).</summary>
    public IReadOnlyList<(long Ts, double Mhz)> Samples
    {
        get
        {
            lock (_lock)
            {
                var r = new (long, double)[_count];
                for (int i = 0; i < _count; i++)
                    r[i] = (_ts[i], _values.AsSpan(i * _width, _n[i]).ToArray().Average());
                return r;
            }
        }
    }

    /// <summary>Per-CPU values of every sample.</summary>
    public IReadOnlyList<(long Ts, double[] Mhz)> PerCpu
    {
        get
        {
            lock (_lock)
            {
                var r = new (long, double[])[_count];
                for (int i = 0; i < _count; i++)
                    r[i] = (_ts[i], _values.AsSpan(i * _width, _n[i]).ToArray());
                return r;
            }
        }
    }

    /// <summary>Median of the all-CPU mean of the samples inside any of the windows; null if none.</summary>
    public double? MedianIn(IReadOnlyList<(long Start, long End)> windows) =>
        MedianOf(PerCpu, windows, values => values.Average());

    /// <summary>Median over the samples inside the windows of the mean of the top-k CPU values (EXP-0008 П5.2); null if none.</summary>
    public double? TopKMedianIn(IReadOnlyList<(long Start, long End)> windows, int k) =>
        MedianOf(PerCpu, windows, values => LoadMath.TopKMean(values, k) ?? double.NaN);

    /// <summary>Pure core of the two medians above (selftest fixtures): samples with an empty or NaN statistic are skipped.</summary>
    public static double? MedianOf(IReadOnlyList<(long Ts, double[] Mhz)> samples, IReadOnlyList<(long Start, long End)> windows, Func<double[], double> stat)
    {
        var inside = new List<double>();
        foreach ((long ts, double[] mhz) in samples)
        {
            if (mhz.Length == 0 || !windows.Any(w => ts >= w.Start && ts <= w.End))
                continue;
            double v = stat(mhz);
            if (!double.IsNaN(v))
                inside.Add(v);
        }
        return inside.Count == 0 ? null : BenchStats.Median(inside);
    }

    private void Loop()
    {
        while (!_stop)
        {
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            int n = Read();
            if (n > 0)
            {
                lock (_lock)
                {
                    if (_count == _ts.Length)
                    {
                        Array.Resize(ref _ts, _ts.Length * 2);
                        Array.Resize(ref _n, _n.Length * 2);
                        Array.Resize(ref _values, _values.Length * 2);
                    }
                    _ts[_count] = Stopwatch.GetTimestamp();
                    _n[_count] = n;
                    _row.AsSpan(0, n).CopyTo(_values.AsSpan(_count * _width));
                    _count++;
                }
            }
            Interlocked.Add(ref _allocated, GC.GetAllocatedBytesForCurrentThread() - a0);
            Thread.Sleep(_intervalMs);
        }
    }

    private static (string, SafeFileHandle[]) OpenSource()
    {
        SafeFileHandle[] scaling = OpenAll(ScalingFiles());
        if (scaling.Length > 0) return ("scaling_cur_freq", scaling);
        SafeFileHandle[] cpuinfo = OpenAll(["/proc/cpuinfo"]);
        return cpuinfo.Length > 0 ? ("proc_cpuinfo", cpuinfo) : ("none", []);
    }

    private static SafeFileHandle[] OpenAll(string[] paths)
    {
        var handles = new List<SafeFileHandle>(paths.Length);
        foreach (string p in paths)
        {
            try
            {
                handles.Add(File.OpenHandle(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return [.. handles];
    }

    private static string[] ScalingFiles()
    {
        const string root = "/sys/devices/system/cpu";
        if (!Directory.Exists(root)) return [];
        return [.. Directory.GetDirectories(root, "cpu*").Select(d => Path.Combine(d, "cpufreq", "scaling_cur_freq")).Where(File.Exists).Order(StringComparer.Ordinal)];
    }

    // Whole file into _buffer (grown, never shrunk); returns the length.
    private int ReadAll(SafeFileHandle h)
    {
        int len = 0;
        while (true)
        {
            if (len == _buffer.Length)
                Array.Resize(ref _buffer, _buffer.Length * 2);
            int n = RandomAccess.Read(h, _buffer.AsSpan(len), len);
            if (n <= 0)
                return len;
            len += n;
        }
    }

    // Per-CPU values into _row; returns how many.
    private int Read()
    {
        try
        {
            if (Source == "scaling_cur_freq")
            {
                int n = 0;
                foreach (SafeFileHandle h in _handles)
                {
                    int len = ReadAll(h);
                    if (n < _row.Length && Utf8Parser.TryParse(_buffer.AsSpan(0, len), out long khz, out _) && khz > 0)
                        _row[n++] = khz / 1000.0;
                }
                return n;
            }
            if (Source == "proc_cpuinfo")
                return CpuinfoMhz(_buffer.AsSpan(0, ReadAll(_handles[0])), _row);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return 0;
    }

    /// <summary>The «cpu MHz» values of a <c>/proc/cpuinfo</c> text into <paramref name="row"/>; returns how many (fixture-tested).</summary>
    public static int CpuinfoMhz(ReadOnlySpan<byte> rest, Span<double> row)
    {
        int n = 0;
        while (!rest.IsEmpty && n < row.Length)
        {
            int eol = rest.IndexOf((byte)'\n');
            ReadOnlySpan<byte> line = eol < 0 ? rest : rest[..eol];
            rest = eol < 0 ? [] : rest[(eol + 1)..];
            if (!line.StartsWith("cpu MHz"u8) || line.IndexOf((byte)':') is not (int i and >= 0))
                continue;
            if (Utf8Parser.TryParse(line[(i + 1)..].TrimStart((byte)' ').TrimStart((byte)'\t'), out double mhz, out _))
                row[n++] = mhz;
        }
        return n;
    }

    public void Dispose()
    {
        _stop = true;
        _thread.Join();
        foreach (SafeFileHandle h in _handles)
            h.Dispose();
    }
}
