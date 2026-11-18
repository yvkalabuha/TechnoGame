using System.Diagnostics;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using System.Text.Json;
using Singularity.Kernel;
using Singularity.Session;

namespace Singularity.Lab.Bench;

/// <summary>
/// ADR-0017 «Research» R1–R3 and the ADR-0016 п. 2 fallback <c>k_copy</c>: the hash and copy paths of the slice table, timed in the host.
/// Every copy variant runs on a Lab clone of the session table (same columns, rows and values), never on the live session, except R2 (б)
/// which reads which columns a real monthly step opens, and R2 (г) which times real months.
/// </summary>
public static class CopyBench
{
    public static double Ms(long ticks) => ProcessBench.Ms(ticks);

    /// <summary>A table of the same shape and current values (Lab-only; the hash of the clone equals the hash of the source).</summary>
    public static Table Clone(Table src)
    {
        var t = new Table(src.RowCount);
        foreach (Column c in src.Columns)
        {
            switch (c)
            {
                case LongColumn l: t.AddLong(l.Name).Load(l.Current); break;
                case IntColumn i: t.AddInt(i.Name).Load(i.Current); break;
                default: throw new InvalidOperationException($"Column '{c.Name}' has an unknown type {c.GetType().Name}.");
            }
        }
        return t;
    }

    public static long Bytes(Table t)
    {
        long b = 0;
        foreach (Column c in t.Columns)
            b += (c is LongColumn ? 8L : 4L) * c.Length;
        return b;
    }

    /// <summary>Times <paramref name="body"/> (ms): <paramref name="warmup"/> untimed calls, then <paramref name="reps"/> timed ones.</summary>
    public static List<double> Time(int warmup, int reps, Action body)
    {
        for (int i = 0; i < warmup; i++)
            body();
        var ms = new List<double>(reps);
        for (int i = 0; i < reps; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            body();
            ms.Add(Ms(Stopwatch.GetTimestamp() - t0));
        }
        return ms;
    }

    // ---- R1: StateHash (SHA-256) ----

    public static void R1(Utf8JsonWriter j, SynthWorld w, Table table, int warmup, int calls)
    {
        j.WriteStartObject();
        BenchJson.WriteWorld(j, w, 0);
        BenchJson.WriteTableShape(j, table);
        string h = StateHash.ComputeHex(table, SliceWorld.SchemaVersion);
        j.WriteString("state_hash", h);
        long a0 = GC.GetTotalAllocatedBytes(true);
        List<double> ms = Time(warmup, calls, () => StateHash.Compute(table, SliceWorld.SchemaVersion));
        j.WriteNumber("allocated_bytes_per_call", (GC.GetTotalAllocatedBytes(true) - a0) / (double)(warmup + calls));
        BenchJson.WriteSummary(j, "state_hash_ms", BenchStats.Summarize(ms));
        j.WriteBoolean("p95_le_15ms", BenchStats.Percentile(ms, 0.95) <= 15);
        j.WriteEndObject();
    }

    // ---- R3: XxHash3 / XxHash128 of the same bytes (reference for MP, Lab only) ----

    public static void R3(Utf8JsonWriter j, SynthWorld w, Table table, int warmup, int calls)
    {
        j.WriteStartObject();
        BenchJson.WriteWorld(j, w, 0);
        BenchJson.WriteTableShape(j, table);
        BenchJson.WriteSummary(j, "sha256_state_hash_ms", BenchStats.Summarize(Time(warmup, calls, () => StateHash.Compute(table, SliceWorld.SchemaVersion))));
        var x3 = new XxHash3();
        BenchJson.WriteSummary(j, "xxhash3_ms", BenchStats.Summarize(Time(warmup, calls, () => HashAll(x3, table))));
        var x128 = new XxHash128();
        BenchJson.WriteSummary(j, "xxhash128_ms", BenchStats.Summarize(Time(warmup, calls, () => HashAll(x128, table))));
        j.WriteEndObject();
    }

    // The same byte layout as StateHash on a little-endian host: header (schema, rows), then every column's Current in order.
    private static void HashAll(NonCryptographicHashAlgorithm h, Table t)
    {
        h.Reset();
        Span<byte> header = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header, SliceWorld.SchemaVersion);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header[4..], t.RowCount);
        h.Append(header);
        foreach (Column c in t.Columns)
        {
            switch (c)
            {
                case LongColumn l: h.Append(MemoryMarshal.AsBytes(l.Current)); break;
                case IntColumn i: h.Append(MemoryMarshal.AsBytes(i.Current)); break;
            }
        }
        Span<byte> outBuf = stackalloc byte[16];
        h.GetCurrentHash(outBuf[..h.HashLengthInBytes]);
    }

    // ---- R2 (а) and k_copy: full CopyCurrentToNext + Swap ----

    public static void R2a(Utf8JsonWriter j, Table clone, int warmup, int reps)
    {
        var copy = new List<double>(reps);
        var swap = new List<double>(reps);
        var total = new List<double>(reps);
        for (int i = 0; i < warmup + reps; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            clone.CopyCurrentToNext();
            long t1 = Stopwatch.GetTimestamp();
            clone.Swap();
            long t2 = Stopwatch.GetTimestamp();
            if (i < warmup) continue;
            copy.Add(Ms(t1 - t0));
            swap.Add(Ms(t2 - t1));
            total.Add(Ms(t2 - t0));
        }
        j.WriteStartObject("a_full_copy");
        BenchJson.WriteSummary(j, "copy_ms", BenchStats.Summarize(copy));
        BenchJson.WriteSummary(j, "swap_ms", BenchStats.Summarize(swap));
        BenchJson.WriteSummary(j, "k_copy_ms", BenchStats.Summarize(total));
        j.WriteEndObject();
    }

    // ---- R2 (б): CoW of the columns a real monthly step opens, against a full copy, on the clone ----

    /// <summary>
    /// Indexes of the columns one monthly tick of the session opens (copy-on-write), read from the public Kernel
    /// <see cref="ColumnWriteTrace"/> switched on for that one tick only. The trace slows <c>Next</c> but changes no state.
    /// </summary>
    public static int[] OpenedByMonthlyStep(SliceSession s, int month)
    {
        s.StepTo(s.World.Calendar.TickOfMonth(month));
        Table t = s.World.Table;
        var trace = new ColumnWriteTrace();
        t.Trace = trace;
        try
        {
            s.Step(1);
        }
        finally
        {
            t.Trace = null;
        }
        var names = new List<string>();
        foreach ((_, string column) in trace.Pairs)
        {
            if (!names.Contains(column))
                names.Add(column);
        }
        var opened = new List<int>();
        for (int i = 0; i < t.Columns.Count; i++)
        {
            if (names.Contains(t.Columns[i].Name))
                opened.Add(i);
        }
        if (opened.Count != names.Count)
            throw new InvalidOperationException("R2 (б): a traced column name is not unique or not in the table.");
        return [.. opened];
    }

    public static void R2b(Utf8JsonWriter j, Table clone, int[] opened, int month, int warmup, int reps)
    {
        long openedBytes = 0;
        foreach (int i in opened)
            openedBytes += (clone.Columns[i] is LongColumn ? 8L : 4L) * clone.Columns[i].Length;
        var cowOpen = new List<double>(reps);
        var cowSwap = new List<double>(reps);
        var cowTotal = new List<double>(reps);
        clone.FullCopyMode = false;
        for (int r = 0; r < warmup + reps; r++)
        {
            long t0 = Stopwatch.GetTimestamp();
            clone.BeginTick();
            long t1 = Stopwatch.GetTimestamp();
            foreach (int i in opened)
                Touch(clone.Columns[i]);
            long t2 = Stopwatch.GetTimestamp();
            clone.Swap();
            clone.EndTick(true);
            long t3 = Stopwatch.GetTimestamp();
            if (r < warmup) continue;
            cowOpen.Add(Ms(t2 - t1));
            cowSwap.Add(Ms(t3 - t2));
            cowTotal.Add(Ms(t3 - t0));
        }
        var full = new List<double>(reps);
        clone.FullCopyMode = true;
        for (int r = 0; r < warmup + reps; r++)
        {
            long t0 = Stopwatch.GetTimestamp();
            clone.BeginTick();
            clone.Swap();
            clone.EndTick(true);
            long t1 = Stopwatch.GetTimestamp();
            if (r >= warmup) full.Add(Ms(t1 - t0));
        }
        clone.FullCopyMode = false;
        j.WriteStartObject("b_cow_monthly_step");
        j.WriteNumber("month", month);
        j.WriteNumber("opened_columns", opened.Length);
        j.WriteNumber("columns", clone.Columns.Count);
        j.WriteNumber("opened_share", clone.Columns.Count == 0 ? 0 : opened.Length / (double)clone.Columns.Count);
        j.WriteNumber("opened_bytes", openedBytes);
        BenchJson.WriteSummary(j, "cow_open_copy_ms", BenchStats.Summarize(cowOpen));
        BenchJson.WriteSummary(j, "cow_swap_ms", BenchStats.Summarize(cowSwap));
        BenchJson.WriteSummary(j, "cow_total_ms", BenchStats.Summarize(cowTotal));
        BenchJson.WriteSummary(j, "full_copy_total_ms", BenchStats.Summarize(full));
        j.WriteBoolean("cow_le_full_plus_5pct_p50", BenchStats.Median(cowTotal) <= 1.05 * BenchStats.Median(full));
        j.WriteEndObject();
    }

    private static void Touch(Column c)
    {
        switch (c)
        {
            case LongColumn l: _ = l.Next; break;
            case IntColumn i: _ = i.Next; break;
        }
    }

    // ---- R2 (в): a synthetic daily system writing a share of the columns, CoW against full copy ----

    /// <summary>Columns j with ⌊(j+1)·pct/100⌋ > ⌊j·pct/100⌋: exactly ⌊C·pct/100⌋ columns, spread evenly over the registration order.</summary>
    public static int[] Pick(int columns, int pct)
    {
        var list = new List<int>();
        for (int c = 0; c < columns; c++)
        {
            if ((c + 1) * pct / 100 > c * pct / 100)
                list.Add(c);
        }
        return [.. list];
    }

    /// <summary>Lab-only daily system: every chunk rewrites its rows of the chosen columns with their current values (state unchanged).</summary>
    private sealed class WriteShareSystem(Column[] columns) : ITickSystem
    {
        public int SystemId => 990;

        public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd)
        {
            foreach (Column c in columns)
            {
                switch (c)
                {
                    case LongColumn l:
                    {
                        Span<long> next = l.Next;
                        l.Current[rowStart..rowEnd].CopyTo(next[rowStart..rowEnd]);
                        break;
                    }
                    case IntColumn i:
                    {
                        Span<int> next = i.Next;
                        i.Current[rowStart..rowEnd].CopyTo(next[rowStart..rowEnd]);
                        break;
                    }
                }
            }
        }

        public void Merge(in TickContext context, int chunkCount) { }

        public void Post(in TickContext context) { }
    }

    public static void R2c(Utf8JsonWriter j, Table clone, int[] percents, int threads, int chunk, int warmup, int reps)
    {
        j.WriteStartObject("c_synthetic_daily_system");
        j.WriteNumber("threads", threads);
        j.WriteNumber("chunk_size", chunk);
        j.WriteStartArray("curve");
        string h0 = StateHash.ComputeHex(clone, SliceWorld.SchemaVersion);
        foreach (int pct in percents)
        {
            int[] pick = Pick(clone.Columns.Count, pct);
            Column[] cols = [.. pick.Select(i => clone.Columns[i])];
            var runner = new TickRunner(clone, 1, [new WriteShareSystem(cols)], chunk, threads);
            j.WriteStartObject();
            j.WriteNumber("percent", pct);
            j.WriteNumber("columns_written", cols.Length);
            foreach (bool full in (bool[])[false, true])
            {
                clone.FullCopyMode = full;
                List<double> ms = Time(warmup, reps, runner.RunTick);
                BenchJson.WriteSummary(j, full ? "full_copy_tick_ms" : "cow_tick_ms", BenchStats.Summarize(ms));
            }
            clone.FullCopyMode = false;
            j.WriteEndObject();
        }
        j.WriteEndArray();
        j.WriteBoolean("state_unchanged", StateHash.ComputeHex(clone, SliceWorld.SchemaVersion) == h0);
        j.WriteEndObject();
    }

    // ---- R2 (г): month M of the real systems, CoW against full copy, paired months of two sessions ----

    public static void R2d(Utf8JsonWriter j, SynthWorld w, ulong seed, int[] threadsList, int chunk, int warmup, int measure, TextWriter log)
    {
        j.WriteStartObject("d_month_cow_vs_full");
        j.WriteNumber("warmup_months", warmup);
        j.WriteNumber("measure_months", measure);
        j.WriteStartArray("by_threads");
        foreach (int t in threadsList)
        {
            var sw = new RunSwitches(HashCheckpoints: false, HashEveryTick: false, DailyNoop: false, FullCopy: false);
            SliceSession cow = SliceSession.Create(w.Data, ProcessBench.Config(w, seed, t, chunk, daily: false, sw));
            SliceSession full = SliceSession.Create(w.Data, ProcessBench.Config(w, seed, t, chunk, daily: false, sw));
            full.World.Table.FullCopyMode = true;
            var mc = new List<double>(measure);
            var mf = new List<double>(measure);
            var ratio = new List<double>(measure);
            for (int m = 0; m < warmup + measure; m++)
            {
                // Alternate which session runs first, so drift inside the pair cancels.
                double a, b;
                if (m % 2 == 0) { a = Month(cow, m); b = Month(full, m); }
                else { b = Month(full, m); a = Month(cow, m); }
                if (m < warmup) continue;
                mc.Add(a);
                mf.Add(b);
                ratio.Add(a / b);
            }
            string hc = cow.LastHash, hf = full.LastHash;
            log.WriteLine($"R2(г) t{t}: CoW p50 {BenchStats.Median(mc):F1} ms, full p50 {BenchStats.Median(mf):F1} ms, hashes {(hc == hf ? "equal" : "DIFFERENT")}");
            j.WriteStartObject();
            j.WriteNumber("threads", t);
            BenchJson.WriteSummary(j, "cow_month_ms", BenchStats.Summarize(mc));
            BenchJson.WriteSummary(j, "full_copy_month_ms", BenchStats.Summarize(mf));
            BenchJson.WriteSummary(j, "ratio_cow_over_full", BenchStats.Summarize(ratio));
            // Every pairwise ratio: the EXP-0008 П9 п. 4 interval pools the pairs of seeds 1–3 (360 per thread count).
            BenchJson.WriteArray(j, "ratio_cow_over_full_all", ratio);
            j.WriteNumber("S_growth_p50", BenchStats.Median(mc) / BenchStats.Median(mf) - 1);
            j.WriteNumber("S_growth_p95", BenchStats.Percentile(mc, 0.95) / BenchStats.Percentile(mf, 0.95) - 1);
            j.WriteBoolean("S_growth_p50_le_2pct", BenchStats.Median(mc) <= 1.02 * BenchStats.Median(mf));
            j.WriteBoolean("hashes_equal", hc == hf);
            j.WriteEndObject();
        }
        j.WriteEndArray();
        j.WriteEndObject();
    }

    private static double Month(SliceSession s, int m)
    {
        long end = s.World.Calendar.TickOfMonth(m) + 1;
        long total = 0;
        while (s.Tick < end)
        {
            long t0 = Stopwatch.GetTimestamp();
            s.Step(1);
            total += Stopwatch.GetTimestamp() - t0;
        }
        return Ms(total);
    }

    // ---- R2 (д): slab — one memcpy of the same number of bytes ----

    public static void R2e(Utf8JsonWriter j, Table clone, int warmup, int reps)
    {
        long bytes = Bytes(clone);
        var src = new long[(bytes + 7) / 8];
        var dst = new long[src.Length];
        for (int i = 0; i < src.Length; i++)
            src[i] = i;
        List<double> ms = Time(warmup, reps, () => src.AsSpan().CopyTo(dst));
        j.WriteStartObject("e_slab_memcpy");
        j.WriteNumber("bytes", bytes);
        BenchJson.WriteSummary(j, "memcpy_ms", BenchStats.Summarize(ms));
        j.WriteEndObject();
    }

    /// <summary>Sanity check of the clone: the same StateHash as the session table.</summary>
    public static bool SameHash(Table a, Table b) =>
        StateHash.ComputeHex(a, SliceWorld.SchemaVersion) == StateHash.ComputeHex(b, SliceWorld.SchemaVersion);
}
