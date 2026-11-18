using System.Buffers.Text;
using System.Diagnostics;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Singularity.Lab.Bench;

/// <summary>
/// Conditions of a bench process (ADR-0016 п. 5, EXP-0007 П5): build, runtime, GC, OS, CPU, power. Written into every bench JSON so a run
/// that broke a condition can be marked INVALID afterwards; the harness itself refuses nothing except a Debug build (unless --allow-debug).
/// </summary>
public static class BenchEnv
{
    /// <summary>
    /// True when Lab and the timed assemblies (Kernel, Core, Session) are all JIT-optimized: a Release Lab on a Debug Core would time
    /// unoptimized simulation code.
    /// </summary>
    public static bool IsOptimizedBuild() =>
        TimedAssemblies.All(IsOptimized);

    internal static readonly Assembly[] TimedAssemblies =
    [
        typeof(BenchEnv).Assembly,
        typeof(Singularity.Kernel.Table).Assembly,
        typeof(Singularity.Core.Commands.SetBudgetShares).Assembly,
        typeof(Singularity.Session.SliceSession).Assembly,
    ];

    private static bool IsOptimized(Assembly a)
    {
        var attr = a.GetCustomAttribute<DebuggableAttribute>();
        return attr is null || !attr.IsJITOptimizerDisabled;
    }

    public static void Write(Utf8JsonWriter w, string repoDir)
    {
        (string commit, bool? dirty) = Provenance.Git(repoDir);
        w.WriteStartObject("provenance");
        w.WriteString("core_commit", commit);
        if (dirty is bool d) w.WriteBoolean("dirty", d); else w.WriteNull("dirty");
        if (Provenance.UntrackedSrc(repoDir) is bool u) w.WriteBoolean("dirty_untracked_src", u); else w.WriteNull("dirty_untracked_src");
        w.WriteString("build_commit", Provenance.BinaryBuildCommit());
        w.WriteEndObject();

        w.WriteStartObject("conditions");
        w.WriteString("configuration", IsOptimizedBuild() ? "Release" : "Debug");
        w.WriteStartObject("assemblies_optimized");
        foreach (Assembly a in TimedAssemblies)
            w.WriteBoolean(a.GetName().Name ?? "?", IsOptimized(a));
        w.WriteEndObject();
        w.WriteString("runtime", RuntimeInformation.FrameworkDescription);
        w.WriteString("runtime_version", Environment.Version.ToString());
        w.WriteString("os", RuntimeInformation.OSDescription);
        w.WriteString("arch", RuntimeInformation.ProcessArchitecture.ToString());
        w.WriteString("cpu_model", CpuModel());
        w.WriteNumber("logical_cpus", Environment.ProcessorCount);
        w.WriteString("cpu_governor", ReadTrim("/sys/devices/system/cpu/cpu0/cpufreq/scaling_governor") ?? "unknown");
        w.WriteString("power", Power());
        w.WriteString("loadavg_at_start", ReadTrim("/proc/loadavg") ?? "unknown");
        w.WriteBoolean("gc_server", GCSettings.IsServerGC);
        w.WriteBoolean("gc_concurrent", AppContext.GetData("System.GC.Concurrent") is not bool c || c);
        w.WriteString("gc_latency_mode", GCSettings.LatencyMode.ToString());
        w.WriteString("env_DOTNET_gcServer", Environment.GetEnvironmentVariable("DOTNET_gcServer") ?? "");
        w.WriteString("env_DOTNET_TieredPGO", Environment.GetEnvironmentVariable("DOTNET_TieredPGO") ?? "");
        w.WriteString("env_DOTNET_TieredCompilation", Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "");
        w.WriteNumber("process_id", Environment.ProcessId);
        w.WriteEndObject();
    }

    public static string CpuModel()
    {
        try
        {
            foreach (string line in File.ReadLines("/proc/cpuinfo"))
            {
                if (line.StartsWith("model name", StringComparison.Ordinal) && line.IndexOf(':') is int i and >= 0)
                    return line[(i + 1)..].Trim();
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return "unknown";
    }

    // "ac", "battery" or "unknown": any online mains supply wins (Linux power_supply class).
    private static string Power()
    {
        const string root = "/sys/class/power_supply";
        if (!Directory.Exists(root))
            return "unknown";
        bool sawBattery = false;
        foreach (string dir in Directory.GetDirectories(root).Order(StringComparer.Ordinal))
        {
            string? type = ReadTrim(Path.Combine(dir, "type"));
            if (type == "Mains" && ReadTrim(Path.Combine(dir, "online")) == "1")
                return "ac";
            sawBattery |= type == "Battery";
        }
        return sawBattery ? "battery" : "unknown";
    }

    internal static string? ReadTrim(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}

/// <summary>
/// Samples the mean CPU frequency (MHz) over logical CPUs once per interval (EXP-0007 П5): <c>scaling_cur_freq</c> of every
/// <c>/sys/devices/system/cpu/cpu*/cpufreq/</c>, else «cpu MHz» of <c>/proc/cpuinfo</c>. Time stamps are Stopwatch ticks of the host.
/// The sampler thread runs inside the timed windows and <c>GC.GetTotalAllocatedBytes</c> counts every thread, so the loop does not
/// allocate in steady state: file handles are opened once and re-read with <c>pread</c> at offset 0 into a reused byte buffer, parsed
/// with <see cref="Utf8Parser"/>, and samples go into a pre-sized array. What it still allocates (buffer or array growth) is counted in
/// <see cref="AllocatedBytes"/> so the bench can subtract it.
/// </summary>
public sealed class FreqSampler : IDisposable
{
    private const int InitialCapacity = 1 << 14;
    private (long Ts, double Mhz)[] _samples = new (long, double)[InitialCapacity];
    private int _count;
    private readonly object _lock = new();
    private readonly SafeFileHandle[] _handles;
    private byte[] _buffer = new byte[4096];
    private readonly Thread _thread;
    private readonly int _intervalMs;
    private long _allocated;
    private volatile bool _stop;

    public FreqSampler(int intervalMs = 1000)
    {
        _intervalMs = intervalMs;
        (Source, _handles) = OpenSource();
        _thread = new Thread(Loop) { IsBackground = true, Name = "bench-freq" };
        _thread.Start();
    }

    /// <summary>"scaling_cur_freq", "proc_cpuinfo" or "none".</summary>
    public string Source { get; }

    /// <summary>Bytes the sampler thread allocated in its loop since start (0 in steady state).</summary>
    public long AllocatedBytes => Interlocked.Read(ref _allocated);

    public IReadOnlyList<(long Ts, double Mhz)> Samples
    {
        get { lock (_lock) return _samples.AsSpan(0, _count).ToArray(); }
    }

    /// <summary>Median of the samples whose time stamp falls inside any of the windows; null if none.</summary>
    public double? MedianIn(IReadOnlyList<(long Start, long End)> windows)
    {
        var inside = new List<double>();
        foreach ((long ts, double mhz) in Samples)
        {
            foreach ((long s, long e) in windows)
            {
                if (ts >= s && ts <= e)
                {
                    inside.Add(mhz);
                    break;
                }
            }
        }
        return inside.Count == 0 ? null : BenchStats.Median(inside);
    }

    private void Loop()
    {
        while (!_stop)
        {
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            if (Read() is double mhz)
            {
                lock (_lock)
                {
                    if (_count == _samples.Length)
                        Array.Resize(ref _samples, _samples.Length * 2);
                    _samples[_count++] = (Stopwatch.GetTimestamp(), mhz);
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

    private double? Read()
    {
        try
        {
            if (Source == "scaling_cur_freq")
            {
                double sum = 0;
                int n = 0;
                foreach (SafeFileHandle h in _handles)
                {
                    int len = ReadAll(h);
                    if (Utf8Parser.TryParse(_buffer.AsSpan(0, len), out long khz, out _) && khz > 0)
                    {
                        sum += khz / 1000.0;
                        n++;
                    }
                }
                return n == 0 ? null : sum / n;
            }
            if (Source == "proc_cpuinfo")
            {
                ReadOnlySpan<byte> rest = _buffer.AsSpan(0, ReadAll(_handles[0]));
                double sum = 0;
                int n = 0;
                while (!rest.IsEmpty)
                {
                    int eol = rest.IndexOf((byte)'\n');
                    ReadOnlySpan<byte> line = eol < 0 ? rest : rest[..eol];
                    rest = eol < 0 ? [] : rest[(eol + 1)..];
                    if (!line.StartsWith("cpu MHz"u8) || line.IndexOf((byte)':') is not (int i and >= 0))
                        continue;
                    if (Utf8Parser.TryParse(line[(i + 1)..].TrimStart((byte)' ').TrimStart((byte)'\t'), out double mhz, out _))
                    {
                        sum += mhz;
                        n++;
                    }
                }
                return n == 0 ? null : sum / n;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    public void Dispose()
    {
        _stop = true;
        _thread.Join();
        foreach (SafeFileHandle h in _handles)
            h.Dispose();
    }
}

/// <summary>Synthetic background load (EXP-0007 C2 «weak machine», f_ref calibration): N threads of integer work until disposed.</summary>
public sealed class BackgroundLoad : IDisposable
{
    private readonly Thread[] _threads;
    private volatile bool _stop;
    private long _sink;

    public BackgroundLoad(int threads)
    {
        _threads = new Thread[threads];
        for (int i = 0; i < threads; i++)
        {
            int seed = i + 1;
            _threads[i] = new Thread(() => Spin(seed)) { IsBackground = true, Name = $"bench-bg-{i}" };
            _threads[i].Start();
        }
    }

    public int Threads => _threads.Length;

    private void Spin(int seed)
    {
        ulong x = (ulong)seed * 0x9E3779B97F4A7C15UL;
        while (!_stop)
        {
            for (int i = 0; i < 1 << 20; i++)
            {
                // xorshift64: integer work, no allocation, no memory traffic.
                x ^= x << 13;
                x ^= x >> 7;
                x ^= x << 17;
            }
        }
        Interlocked.Add(ref _sink, (long)(x & 1));
    }

    public void Dispose()
    {
        _stop = true;
        foreach (Thread t in _threads)
            t.Join();
    }
}
