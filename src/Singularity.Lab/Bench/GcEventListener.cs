using System.Diagnostics.Tracing;

namespace Singularity.Lab.Bench;

/// <summary>
/// In-process listener of the runtime GC events for <c>lab bench --mode alloc</c> (ADR-0023 п. 2.2, 2.4): <c>GCAllocationTick</c> with its
/// heap kind (0 small, 1 large = LOH, 2 pinned) and amount, and <c>GCStart</c> with generation and reason (4 = AllocLarge).
/// <para>
/// Precision [Факт: runtime GC event contract]: an allocation tick fires each time ~100 KB have been allocated on a heap kind since the last
/// tick, so LOH ticks = 0 over a window means &lt; 100 KB of LOH there, not exactly 0 B; a single array of 85–100 KB may stay unseen until
/// the next one. The bytes are a sample sum, not an exact count.
/// </para>
/// <para>
/// The listener runs on its own dispatch thread and allocates (event payloads): <see cref="OwnBytesAt"/> gives the dispatch thread's own
/// allocations so the bench can subtract them. Events arrive with a delay; <see cref="Flush"/> waits for a sentinel LOH allocation made
/// after the last mark.
/// </para>
/// </summary>
internal sealed class GcEventListener : EventListener
{
    public readonly record struct GcEvent(DateTime Ts, string Name, int Kind, long Amount, int Gen, int Reason, long OwnBytes);

    private List<GcEvent>? _events;
    private readonly object _lock = new();
    [ThreadStatic] private static long t_last;
    private long _own;

    private List<GcEvent> Events
    {
        get
        {
            // OnEventSourceCreated runs from the base constructor before field initializers of this class.
            lock (_lock) return _events ??= new List<GcEvent>(1 << 12);
        }
    }

    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "Microsoft-Windows-DotNETRuntime")
            EnableEvents(source, EventLevel.Verbose, (EventKeywords)0x1);   // GC keyword
    }

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        string name = e.EventName ?? "";
        bool tick = name.StartsWith("GCAllocationTick", StringComparison.Ordinal);
        bool start = !tick && name.StartsWith("GCStart", StringComparison.Ordinal);
        long now = GC.GetAllocatedBytesForCurrentThread();
        if (t_last != 0)
            Interlocked.Add(ref _own, now - t_last);
        if (tick || start)
        {
            var ev = new GcEvent(e.TimeStamp.Kind == DateTimeKind.Utc ? e.TimeStamp : e.TimeStamp.ToUniversalTime(), tick ? "tick" : "start",
                tick ? U32(e, "AllocationKind") : -1,
                tick ? (Payload(e, "AllocationAmount64") is long a64 ? a64 : U32(e, "AllocationAmount")) : 0,
                start ? U32(e, "Depth") : -1, start ? U32(e, "Reason") : -1, Interlocked.Read(ref _own));
            List<GcEvent> list = Events;
            lock (_lock)
                list.Add(ev);
        }
        t_last = GC.GetAllocatedBytesForCurrentThread();
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

    /// <summary>Allocates one LOH array after <paramref name="after"/> and waits (≤ timeout) until an event later than it has arrived.</summary>
    public bool Flush(DateTime after, TimeSpan timeout)
    {
        var sentinel = new byte[512 * 1024];
        GC.KeepAlive(sentinel);
        DateTime until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (Snapshot().Any(x => x.Ts > after))
                return true;
            Thread.Sleep(50);
        }
        return false;
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
