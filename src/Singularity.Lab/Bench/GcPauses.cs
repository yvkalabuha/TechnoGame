using System.Text.Json;

namespace Singularity.Lab.Bench;

/// <summary>
/// GC pauses from the runtime events kept by <see cref="GcEventListener"/> (decision #416, the pause criterion that replaces «0 gen2» in
/// ADR-0023 п. 2.4). Pure: no clock, no runtime calls; host-side only, never world state.
/// <para>
/// A pause is one execution-engine suspension: <c>GCSuspendEEBegin</c> → <c>GCRestartEEEnd</c> (the same span as PerfView's pause; it
/// includes the time to reach the safe point). It is attributed to the GC whose <c>GCStart</c> lies inside it (the deepest one if several);
/// a GC suspension without a start inside belongs to the background GC in progress (its final-mark suspension). Kind follows
/// <see cref="GCKind"/>: type 1 = <c>Background</c>; depth 2 non-concurrent (type 0) = <c>FullBlocking</c>; everything else
/// (gen0/gen1, foreground during a background GC) = <c>Ephemeral</c>. A GC suspension with no GC to attribute is <c>Unknown</c>; a
/// suspension for a non-GC reason without a GC inside (e.g. the runtime's own suspensions) is kept apart (<see cref="Timeline.Other"/>).
/// </para>
/// </summary>
internal static class GcPauses
{
    public const string Ephemeral = nameof(GCKind.Ephemeral), Background = nameof(GCKind.Background), FullBlocking = nameof(GCKind.FullBlocking), Unknown = "Unknown";

    /// <summary>Suspend reasons of the runtime that belong to a GC: SUSPEND_FOR_GC = 1, SUSPEND_FOR_GC_PREP = 6.</summary>
    public static bool IsGcSuspendReason(int reason) => reason is 1 or 6;

    public readonly record struct Pause(DateTime Start, double Ms, string Kind, int Gen, int GcReason, int GcCount, int SuspendReason);

    public readonly record struct GcRecord(int Count, DateTime Start, int Gen, int Type, int Reason, string Kind);

    public readonly record struct Suspension(DateTime Start, double Ms, int Reason);

    /// <summary><paramref name="Unpaired"/>: a suspension without its restart or a restart without its suspension (events lost or cut by the flush).</summary>
    public sealed record Timeline(IReadOnlyList<Pause> Pauses, IReadOnlyList<GcRecord> Gcs, IReadOnlyList<Suspension> Other, int Unpaired);

    public static string KindOf(int depth, int type) => type == 1 ? Background : depth == 2 && type == 0 ? FullBlocking : Ephemeral;

    /// <summary>Pairs the events (stable order by time) into pauses and GCs.</summary>
    public static Timeline Build(IReadOnlyList<GcEventListener.GcEvent> events)
    {
        var ev = events.Select((e, i) => (e, i)).Where(x => x.e.Name != "tick").OrderBy(x => x.e.Ts).ThenBy(x => x.i).Select(x => x.e).ToList();
        var pauses = new List<Pause>();
        var gcs = new List<GcRecord>();
        var other = new List<Suspension>();
        int unpaired = 0;
        GcEventListener.GcEvent? open = null;
        GcRecord? inPause = null, bgc = null;
        foreach (GcEventListener.GcEvent e in ev)
        {
            switch (e.Name)
            {
                case "suspend":
                    if (open is not null)
                        unpaired++;
                    open = e;
                    inPause = null;
                    break;
                case "start":
                    var g = new GcRecord(e.Count, e.Ts, e.Gen, e.Type, e.Reason, KindOf(e.Gen, e.Type));
                    gcs.Add(g);
                    if (open is not null && (inPause is not GcRecord p || g.Gen > p.Gen))
                        inPause = g;
                    if (e.Type == 1)
                        bgc = g;
                    break;
                case "end":
                    if (bgc is GcRecord b && b.Count == e.Count)
                        bgc = null;
                    break;
                case "restart":
                    if (open is not GcEventListener.GcEvent s)
                    {
                        unpaired++;
                        break;
                    }
                    double ms = (e.Ts - s.Ts).TotalMilliseconds;
                    if (inPause is GcRecord ip)
                        pauses.Add(new Pause(s.Ts, ms, ip.Kind, ip.Gen, ip.Reason, ip.Count, s.Reason));
                    else if (IsGcSuspendReason(s.Reason) && bgc is GcRecord bg)
                        pauses.Add(new Pause(s.Ts, ms, Background, bg.Gen, bg.Reason, bg.Count, s.Reason));
                    else if (IsGcSuspendReason(s.Reason))
                        pauses.Add(new Pause(s.Ts, ms, Unknown, -1, -1, s.Count, s.Reason));
                    else
                        other.Add(new Suspension(s.Ts, ms, s.Reason));
                    open = null;
                    inPause = null;
                    break;
            }
        }
        if (open is not null)
            unpaired++;
        return new Timeline(pauses, gcs, other, unpaired);
    }

    private static bool In(DateTime t, IReadOnlyList<(DateTime From, DateTime To)> windows)
    {
        foreach ((DateTime f, DateTime to) in windows)
        {
            if (t >= f && t <= to)
                return true;
        }
        return false;
    }

    /// <summary>Pauses (by their start) and GCs (by their GCStart) inside the windows, plus the summary statistics.</summary>
    public sealed record Summary(IReadOnlyList<Pause> Pauses, IReadOnlyList<GcRecord> Gcs, IReadOnlyList<Suspension> Other, double WallMs)
    {
        public int Count => Pauses.Count;
        public double TotalMs => Pauses.Sum(p => p.Ms);
        public double? MaxMs => Pauses.Count > 0 ? Pauses.Max(p => p.Ms) : null;
        public double? P50Ms => Pauses.Count > 0 ? BenchStats.Percentile([.. Pauses.Select(p => p.Ms)], 0.50) : null;
        public double? P99Ms => Pauses.Count > 0 ? BenchStats.Percentile([.. Pauses.Select(p => p.Ms)], 0.99) : null;
        public int GcsOf(string kind) => Gcs.Count(g => g.Kind == kind);
        /// <summary>Blocking gen2 = <c>FullBlocking</c> GCs (the window lies after the warm-up by construction).</summary>
        public int BlockingGen2 => GcsOf(FullBlocking);
        public int Gen2 => Gcs.Count(g => g.Gen == 2);
        public double? Share => WallMs > 0 ? TotalMs / WallMs : null;
    }

    public static Summary Summarize(Timeline t, IReadOnlyList<(DateTime From, DateTime To)> windows, double wallMs) =>
        new([.. t.Pauses.Where(p => In(p.Start, windows))], [.. t.Gcs.Where(g => In(g.Start, windows))], [.. t.Other.Where(o => In(o.Start, windows))], wallMs);

    /// <summary>Pauses and non-GC suspensions by start inside the time windows; GCs by index in (After, Upto] of the ranges (see <see cref="SummarizeGcs"/>).</summary>
    public static Summary Summarize(Timeline t, IReadOnlyList<(DateTime From, DateTime To)> windows, IReadOnlyList<(int After, int Upto)> gcRanges, double wallMs) =>
        new([.. t.Pauses.Where(p => In(p.Start, windows))], [.. t.Gcs.Where(g => InRange(g.Count, gcRanges))], [.. t.Other.Where(o => In(o.Start, windows))], wallMs);

    /// <summary>
    /// Pauses and GCs of the GCs with index in (After, Upto] — <c>GC.CollectionCount(0)</c> read before and after a forced collection
    /// (the GCStart <c>Count</c> of a GC is <c>CollectionCount(0)</c> + 1 at its start). Clock-free: the event time stamps of the runtime
    /// (EventPipe) and the host <see cref="DateTime.UtcNow"/> differ by up to milliseconds off Linux, so a span as short as one settle
    /// collection is matched by GC index, not by time (TASK-0116).
    /// </summary>
    public static Summary SummarizeGcs(Timeline t, IReadOnlyList<(int After, int Upto)> ranges, double wallMs) =>
        new([.. t.Pauses.Where(p => InRange(p.GcCount, ranges))], [.. t.Gcs.Where(g => InRange(g.Count, ranges))], [], wallMs);

    private static bool InRange(int gc, IReadOnlyList<(int After, int Upto)> ranges)
    {
        foreach ((int after, int upto) in ranges)
        {
            if (gc > after && gc <= upto)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Writes the summary object <paramref name="name"/>: count, total, max, p50, p99, share (events), per kind (count of GCs and of pauses,
    /// total and max pause), blocking gen2, gen2, unknown pauses, non-GC suspensions; with <paramref name="origin"/> also every pause
    /// (<c>t_ms</c> from the origin, <c>ms</c>, <c>kind</c>, <c>gen</c>, <c>reason</c>).
    /// </summary>
    public static void Write(Utf8JsonWriter j, string name, Summary s, DateTime? origin)
    {
        j.WriteStartObject(name);
        j.WriteNumber("pause_count", s.Count);
        j.WriteNumber("pause_ms_total", s.TotalMs);
        LoadMeter.WriteNum(j, "pause_max_ms", s.MaxMs);
        LoadMeter.WriteNum(j, "pause_p50_ms", s.P50Ms);
        LoadMeter.WriteNum(j, "pause_p99_ms", s.P99Ms);
        j.WriteNumber("window_wall_ms", s.WallMs);
        LoadMeter.WriteNum(j, "pause_share", s.Share);
        j.WriteNumber("gcs", s.Gcs.Count);
        j.WriteNumber("gen2", s.Gen2);
        j.WriteNumber("gen2_blocking", s.BlockingGen2);
        j.WriteNumber("gen2_background", s.Gcs.Count(g => g.Kind == Background));
        j.WriteStartObject("by_kind");
        foreach (string k in (string[])[Ephemeral, Background, FullBlocking, Unknown])
        {
            List<Pause> ps = [.. s.Pauses.Where(p => p.Kind == k)];
            j.WriteStartObject(k);
            j.WriteNumber("gcs", s.GcsOf(k));
            j.WriteNumber("pauses", ps.Count);
            j.WriteNumber("pause_ms_total", ps.Sum(p => p.Ms));
            LoadMeter.WriteNum(j, "pause_max_ms", ps.Count > 0 ? ps.Max(p => p.Ms) : null);
            j.WriteEndObject();
        }
        j.WriteEndObject();
        j.WriteNumber("non_gc_suspensions", s.Other.Count);
        LoadMeter.WriteNum(j, "non_gc_suspension_max_ms", s.Other.Count > 0 ? s.Other.Max(o => o.Ms) : null);
        if (origin is DateTime o0)
        {
            j.WriteStartArray("pauses");
            foreach (Pause p in s.Pauses)
            {
                j.WriteStartObject();
                j.WriteNumber("t_ms", Math.Round((p.Start - o0).TotalMilliseconds, 3));
                j.WriteNumber("ms", Math.Round(p.Ms, 4));
                j.WriteString("kind", p.Kind);
                j.WriteNumber("gen", p.Gen);
                j.WriteNumber("reason", p.GcReason);
                j.WriteEndObject();
            }
            j.WriteEndArray();
        }
        j.WriteEndObject();
    }
}
