using System.Diagnostics.Tracing;

namespace Singularity.Lab.Bench;

/// <summary>
/// In-process listener of the runtime GC events for <c>lab bench --mode alloc</c> and <c>--mode process</c> (ADR-0023 п. 2.2, 2.4, decision
/// #416): <c>GCAllocationTick</c> with its heap kind (0 small, 1 large = LOH, 2 pinned) and amount (Verbose level only), <c>GCStart</c> with
/// generation, reason (4 = AllocLarge), type (0 non-concurrent, 1 background, 2 foreground) and count, <c>GCEnd</c>,
/// <c>GCSuspendEEBegin</c> (with its suspend reason) and <c>GCRestartEEEnd</c>: a pause is SuspendEEBegin → RestartEEEnd
/// (<see cref="GcPauses"/>).
/// <para>
/// Precision [Факт: runtime GC event contract]: an allocation tick fires each time ~100 KB have been allocated on a heap kind since the last
/// tick, so LOH ticks = 0 over a window means &lt; 100 KB of LOH there, not exactly 0 B; a single array of 85–100 KB may stay unseen until
/// the next one. The bytes are a sample sum, not an exact count.
/// </para>
/// <para>
/// The listener runs on its own dispatch thread and allocates (event payloads): <see cref="OwnBytes"/> / <see cref="OwnBytesAt"/> give the
/// dispatch thread's own allocations so the bench can subtract them. Events arrive with a delay; <see cref="Flush"/> forces one gen0
/// collection after the last window and waits for its events.
/// </para>
/// </summary>
internal sealed class GcEventListener : EventListener
{
    /// <summary>
    /// One kept event. <paramref name="Name"/>: tick, start, end, suspend, restart. <paramref name="Gen"/>: depth of start/end;
    /// <paramref name="Reason"/>: GC reason of start, suspend reason of suspend; <paramref name="Type"/>: GC type of start;
    /// <paramref name="Count"/>: GC count of start/end/suspend.
    /// </summary>
    public readonly record struct GcEvent(DateTime Ts, string Name, int Kind, long Amount, int Gen, int Reason, long OwnBytes, int Type = -1, int Count = -1);

    private const string RuntimeSource = "Microsoft-Windows-DotNETRuntime";
    private List<GcEvent>? _events;
    private readonly object _lock = new();
    [ThreadStatic] private static long t_last;
    private long _own;
    private readonly List<string>? _raw;
    private EventSource? _runtime;
    private EventLevel _level;
    private bool _configured;

    /// <summary>
    /// <paramref name="raw"/>: also keep every GC event (start, end, heap stats, global history, allocation ticks with type name) as one JSON
    /// line (DEBT-0057 diagnosis). <paramref name="verbose"/>: Verbose level with allocation ticks (alloc mode); false = Informational
    /// (start, end, suspension, restart only: the timed process mode keeps the listener's work per GC, not per 100 KB).
    /// </summary>
    public GcEventListener(bool raw = false, bool verbose = true)
    {
        if (raw)
            lock (_lock) _raw = new List<string>(1 << 12);
        lock (_lock)
        {
            _level = verbose || raw ? EventLevel.Verbose : EventLevel.Informational;
            _configured = true;
        }
        Enable();
    }

    private List<GcEvent> Events
    {
        get
        {
            // OnEventSourceCreated may run from the base constructor, before the constructor body.
            lock (_lock) return _events ??= new List<GcEvent>(1 << 12);
        }
    }

    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name != RuntimeSource)
            return;
        // Called from the base constructor for sources that already exist (field initializers have run, the constructor body has not):
        // the level is known only after the constructor body, which then enables the source.
        lock (_lock)
            _runtime = source;
        Enable();
    }

    private void Enable()
    {
        EventSource? src;
        EventLevel level;
        lock (_lock)
        {
            if (!_configured || _runtime is null)
                return;
            src = _runtime;
            level = _level;
        }
        EnableEvents(src, level, (EventKeywords)0x1);   // GC keyword
    }

    /// <summary>Name without the version suffix: "GCStart_V2" → "GCStart".</summary>
    internal static string BaseName(string name)
    {
        int i = name.IndexOf("_V", StringComparison.Ordinal);
        return i > 0 ? name[..i] : name;
    }

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        string name = BaseName(e.EventName ?? "");
        // The raw line is built before `now` so that its bytes count as the listener's own (subtracted from the paths).
        if (_raw is not null)
            RawAdd(e);
        long now = GC.GetAllocatedBytesForCurrentThread();
        if (t_last != 0)
            Interlocked.Add(ref _own, now - t_last);
        string? kind = name switch
        {
            "GCAllocationTick" => "tick",
            "GCStart" => "start",
            "GCEnd" => "end",
            "GCSuspendEEBegin" => "suspend",
            "GCRestartEEEnd" => "restart",
            _ => null,
        };
        if (kind is not null)
        {
            DateTime ts = e.TimeStamp.Kind == DateTimeKind.Utc ? e.TimeStamp : e.TimeStamp.ToUniversalTime();
            long own = Interlocked.Read(ref _own);
            GcEvent ev = kind switch
            {
                "tick" => new GcEvent(ts, kind, U32(e, "AllocationKind"), Payload(e, "AllocationAmount64") is long a64 ? a64 : U32(e, "AllocationAmount"), -1, -1, own),
                "start" => new GcEvent(ts, kind, -1, 0, U32(e, "Depth"), U32(e, "Reason"), own, U32(e, "Type"), U32(e, "Count")),
                "end" => new GcEvent(ts, kind, -1, 0, U32(e, "Depth"), -1, own, -1, U32(e, "Count")),
                "suspend" => new GcEvent(ts, kind, -1, 0, -1, U32(e, "Reason"), own, -1, U32(e, "Count")),
                _ => new GcEvent(ts, kind, -1, 0, -1, -1, own),
            };
            List<GcEvent> list = Events;
            lock (_lock)
                list.Add(ev);
        }
        t_last = GC.GetAllocatedBytesForCurrentThread();
    }

    /// <summary>Bytes allocated so far by the dispatch thread (read at window bounds, like the sampler's and the meter's).</summary>
    public long OwnBytes => Interlocked.Read(ref _own);

    private void RawAdd(EventWrittenEventArgs e)
    {
        var sb = new System.Text.StringBuilder(256);
        DateTime ts = e.TimeStamp.Kind == DateTimeKind.Utc ? e.TimeStamp : e.TimeStamp.ToUniversalTime();
        sb.Append("{\"ts\":\"").Append(ts.ToString("O", System.Globalization.CultureInfo.InvariantCulture)).Append("\",\"event\":\"").Append(e.EventName)
            .Append("\",\"os_thread\":").Append(e.OSThreadId);
        if (e.PayloadNames is not null && e.Payload is not null)
            for (int i = 0; i < e.PayloadNames.Count && i < e.Payload.Count; i++)
            {
                object? v = e.Payload[i];
                sb.Append(",\"").Append(e.PayloadNames[i]).Append("\":");
                if (v is string str)
                    sb.Append('"').Append(str.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
                else if (v is null || v is byte[] || v is Guid)
                    sb.Append('"').Append(v?.ToString() ?? "").Append('"');
                else
                    sb.Append(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture));
            }
        sb.Append('}');
        lock (_lock) _raw!.Add(sb.ToString());
    }

    /// <summary>The raw event lines (empty unless constructed with <c>raw: true</c>).</summary>
    public IReadOnlyList<string> Raw()
    {
        lock (_lock) return _raw is null ? [] : [.. _raw];
    }

    private static object? PayloadObj(EventWrittenEventArgs e, string key)
    {
        if (e.PayloadNames is null || e.Payload is null)
            return null;
        int i = e.PayloadNames.IndexOf(key);
        return i >= 0 && i < e.Payload.Count ? e.Payload[i] : null;
    }

    private static long? Payload(EventWrittenEventArgs e, string key) => PayloadObj(e, key) switch
    {
        ulong u => (long)u,
        long l => l,
        uint u32 => u32,
        int i => i,
        _ => null,
    };

    private static int U32(EventWrittenEventArgs e, string key) => Payload(e, key) is long v ? (int)v : -1;

    public IReadOnlyList<GcEvent> Snapshot()
    {
        List<GcEvent> list = Events;
        lock (_lock) return [.. list];
    }

    /// <summary>
    /// After the last measured window: one induced blocking gen0 collection (the runtime may escalate it), then waits (≤ timeout) for its GCStart and RestartEEEnd, so
    /// that every earlier event has been delivered. <c>ClockOffsetMs</c> = event time of that GCStart − host <see cref="DateTime.UtcNow"/>
    /// just before the call (expected in [0, host span]; a check of the window bounds, which are host times, against event times).
    /// </summary>
    public (bool Flushed, double? ClockOffsetMs, double HostSpanMs) Flush(TimeSpan timeout)
    {
        DateTime before = DateTime.UtcNow;
        GC.Collect(0, GCCollectionMode.Forced, blocking: true);
        DateTime after = DateTime.UtcNow;
        double span = (after - before).TotalMilliseconds;
        DateTime until = DateTime.UtcNow + timeout;
        while (true)
        {
            IReadOnlyList<GcEvent> ev = Snapshot();
            // The flush GC: the last induced (reason 1) start not earlier than 1 s before the call, followed by a restart. Any depth: the
            // runtime may escalate the requested gen0 (seen: gen1).
            int si = -1;
            for (int i = ev.Count - 1; i >= 0; i--)
            {
                if (ev[i].Name == "start" && ev[i].Reason == 1 && ev[i].Ts >= before.AddSeconds(-1))
                {
                    si = i;
                    break;
                }
            }
            if (si >= 0)
            {
                for (int i = si + 1; i < ev.Count; i++)
                {
                    if (ev[i].Name == "restart")
                        return (true, (ev[si].Ts - before).TotalMilliseconds, span);
                }
            }
            if (DateTime.UtcNow >= until)
                return (false, si >= 0 ? (ev[si].Ts - before).TotalMilliseconds : null, span);
            Thread.Sleep(50);
        }
    }

    /// <summary>LOH ticks, sampled LOH bytes, GCStart with reason AllocLarge and gen2 GCStart inside [from, to].</summary>
    public static (int LohTicks, long LohBytes, int AllocLargeGcs, int Gen2Gcs) In(IReadOnlyList<GcEvent> events, DateTime from, DateTime to)
    {
        int ticks = 0, large = 0, gen2 = 0;
        long bytes = 0;
        foreach (GcEvent e in events)
        {
            if (e.Ts < from || e.Ts > to)
                continue;
            if (e.Name == "tick" && e.Kind == 1)
            {
                ticks++;
                bytes += e.Amount;
            }
            else if (e.Name == "start")
            {
                large += e.Reason == 4 ? 1 : 0;
                gen2 += e.Gen == 2 ? 1 : 0;
            }
        }
        return (ticks, bytes, large, gen2);
    }

    /// <summary>Own bytes of the dispatch thread up to <paramref name="t"/>: the value of the last event at or before it (0 before the first).</summary>
    public static long OwnBytesAt(IReadOnlyList<GcEvent> events, DateTime t)
    {
        long own = 0;
        foreach (GcEvent e in events)
        {
            if (e.Ts > t)
                break;
            own = e.OwnBytes;
        }
        return own;
    }
}
