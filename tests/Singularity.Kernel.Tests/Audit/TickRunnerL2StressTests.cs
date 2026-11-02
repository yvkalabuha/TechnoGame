using Singularity.Kernel;

namespace Singularity.Kernel.Tests.Audit;

/// <summary>
/// L2 stress: random (rows, chunkSize, threads, seed) shapes; hash sequence must equal the 1-thread run.
/// Two systems: one with neighbour reads, RNG and order-sensitive double merge; one integer counter
/// reading another system's column from Current.
/// </summary>
public class TickRunnerL2StressTests
{
    private const int Shapes = 40;
    private const int Ticks = 12;

    private sealed class NoiseSystem : ITickSystem
    {
        private readonly LongColumn _v;
        private readonly LongColumn _total;
        private readonly double[] _partials;

        public NoiseSystem(Table t, int chunkCount)
        {
            _v = t.AddLong("v");
            _total = t.AddLong("total");
            _partials = new double[chunkCount];
            var init = new long[t.RowCount];
            for (int i = 0; i < init.Length; i++) init[i] = Fx6.FromInt(50 + i % 13).Raw;
            _v.Load(init);
        }

        public int SystemId => 3;

        public void Compute(in TickContext ctx, int chunk, int rowStart, int rowEnd)
        {
            ReadOnlySpan<long> cur = _v.Current;
            Span<long> next = _v.Next;
            double sum = 0;
            for (int r = rowStart; r < rowEnd; r++)
            {
                long right = cur[(r + 1) % cur.Length];
                double u = ctx.Rng.NextUnit(ctx.Tick, SystemId, r, 0);
                double w = ctx.Rng.NextUnit(ctx.Tick, SystemId, r, 1);
                Fx6 val = Fx6.FromRaw(cur[r]) * Fx6.FromDouble(0.9 + 0.2 * u) + Fx6.FromRaw(right / 100) + Fx6.FromDouble(DetMath.Sqrt(w));
                next[r] = val.Raw;
                sum += val.ToDouble() * 1.0000001;
            }
            _partials[chunk] = sum;
        }

        public void Merge(in TickContext ctx, int chunkCount)
        {
            double acc = 0.1;
            for (int c = 0; c < chunkCount; c++) acc = acc * 0.999999 + _partials[c];
            if (_total.Length > 0) _total.Next[0] = Fx6.FromDouble(acc / 1e6).Raw;
        }

        public void Post(in TickContext ctx) { }
    }

    private sealed class CounterSystem : ITickSystem
    {
        private readonly LongColumn _v;
        private readonly IntColumn _hits;
        private readonly int[] _chunkHits;

        public CounterSystem(Table t, int chunkCount)
        {
            _v = t.GetLong("v");
            _hits = t.AddInt("hits");
            _chunkHits = new int[chunkCount];
        }

        public int SystemId => 4;

        public void Compute(in TickContext ctx, int chunk, int rowStart, int rowEnd)
        {
            ReadOnlySpan<long> v = _v.Current;
            Span<int> next = _hits.Next;
            int local = 0;
            for (int r = rowStart; r < rowEnd; r++)
            {
                if ((v[r] & 0xFF) < 64) { next[r]++; local++; }
            }
            _chunkHits[chunk] = local;
        }

        public void Merge(in TickContext ctx, int chunkCount)
        {
            // Order-sensitive integer fold: a wrong chunk order gives a different value.
            int fold = 17;
            for (int c = 0; c < chunkCount; c++) fold = unchecked(fold * 31 + _chunkHits[c]);
            if (_hits.Length > 0) _hits.Next[0] = fold;
        }

        public void Post(in TickContext ctx) { }
    }

    private static List<string> Run(int rows, int chunkSize, int threads, ulong seed)
    {
        var table = new Table(rows);
        int chunks = (rows + chunkSize - 1) / chunkSize;
        var runner = new TickRunner(table, seed, [new NoiseSystem(table, chunks), new CounterSystem(table, chunks)], chunkSize, threads);
        var hashes = new List<string>(Ticks);
        for (int i = 0; i < Ticks; i++)
        {
            runner.RunTick();
            hashes.Add(StateHash.ComputeHex(table, 7, runner.Tick));
        }
        return hashes;
    }

    [Fact]
    public void RandomShapes_ThreadCountNeverChangesHashes()
    {
        var rnd = new Random(20261102);
        int[] threadChoices = [2, 3, 4, 7, 8, 16, 64];
        for (int s = 0; s < Shapes; s++)
        {
            int rows = rnd.Next(4) == 0 ? rnd.Next(0, 4) : rnd.Next(1, 3000);
            int chunkSize = rnd.Next(3) switch { 0 => rnd.Next(1, 8), 1 => rnd.Next(1, 300), _ => rnd.Next(1, rows + 20) };
            ulong seed = (ulong)rnd.NextInt64();
            List<string> single = Run(rows, chunkSize, 1, seed);
            int threads = threadChoices[rnd.Next(threadChoices.Length)];
            List<string> multi = Run(rows, chunkSize, threads, seed);
            Assert.True(single.SequenceEqual(multi), $"L2 mismatch rows={rows} chunk={chunkSize} threads={threads} seed={seed}");
            Assert.True(single.SequenceEqual(Run(rows, chunkSize, 1, seed)), $"L1 mismatch rows={rows} chunk={chunkSize} seed={seed}");
        }
    }

    [Fact]
    public void LargeWorld_ManyThreads_RepeatedRuns()
    {
        List<string> reference = Run(20_000, 97, 1, 1910);
        for (int i = 0; i < 3; i++)
            Assert.Equal(reference, Run(20_000, 97, Environment.ProcessorCount * 2 + 1, 1910));
        Assert.Equal(Ticks, reference.Distinct().Count()); // world evolves, test is not vacuous
    }
}
