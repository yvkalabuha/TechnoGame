using Singularity.Kernel;

namespace Singularity.Lab.Models.Test;

/// <summary>
/// TEST MODEL — NOT A SCIENTIFIC MODEL. Stand-in until TASK-0004 so the Lab pipeline
/// (manifest → run → golden_hash → CSV → matrix) can be exercised end to end.
/// Each row does an integer random walk: value += drift + U{-noise..+noise} (Fx6 raw units).
/// Parameters (all optional): rows (64), start (100), drift (0), noise (1), observe_every (1),
/// fail_at_tick (-1 = never; throws inside Compute to exercise the failure path).
/// Follows the world-state rules (ADR-0003/0004): integer arithmetic, CounterRng only.
/// </summary>
public sealed class RandomWalkTestModelFactory : ILabModelFactory
{
    public string Id => "test.random-walk";

    public bool IsTestModel => true;

    public string Description => "TEST MODEL: integer random walk per row (Lab self-checks only).";

    public ILabModel Create(ModelSetup setup) => new RandomWalkTestModel(setup);
}

public sealed class RandomWalkTestModel : ILabModel
{
    public const int Schema = 1;

    private readonly long _observeEvery;
    private readonly LongColumn _value;
    private readonly IntColumn _ups;

    public RandomWalkTestModel(ModelSetup setup)
    {
        ParamSet p = setup.Parameters;
        long rows = p.GetLong("rows", 64);
        if (rows < 1 || rows > 10_000_000)
            throw new ParamException("test.random-walk: rows must be in [1, 10^7].");
        long start = p.GetFx6Raw("start", 100 * Fx6.Scale);
        long drift = p.GetFx6Raw("drift", 0);
        long noise = p.GetFx6Raw("noise", Fx6.Scale);
        if (noise < 0 || noise > 1_000_000 * Fx6.Scale)
            throw new ParamException("test.random-walk: noise must be in [0, 10^6].");
        _observeEvery = p.GetLong("observe_every", 1);
        if (_observeEvery < 1)
            throw new ParamException("test.random-walk: observe_every must be >= 1.");
        long failAt = p.GetLong("fail_at_tick", -1);

        Table = new Table((int)rows);
        _value = Table.AddLong("value");
        _ups = Table.AddInt("up_steps");
        var init = new long[rows];
        Array.Fill(init, start);
        _value.Load(init);

        Systems = [new WalkSystem(_value, _ups, drift, noise, failAt)];
        MetricTables = [new MetricTableSpec("test_walk", ["tick", "mean", "min", "max", "up_steps_total"])];
    }

    public int SchemaVersion => Schema;

    public Table Table { get; }

    public IReadOnlyList<ITickSystem> Systems { get; }

    public IReadOnlyList<MetricTableSpec> MetricTables { get; }

    public void Observe(long tick, IMetricSink sink)
    {
        if (tick % _observeEvery != 0)
            return;
        ReadOnlySpan<long> v = _value.Current;
        ReadOnlySpan<int> ups = _ups.Current;
        Int128 sum = 0;
        long min = long.MaxValue, max = long.MinValue, upTotal = 0;
        for (int i = 0; i < v.Length; i++)
        {
            sum += v[i];
            if (v[i] < min) min = v[i];
            if (v[i] > max) max = v[i];
            upTotal += ups[i];
        }
        // Integer division truncates toward zero: exact and platform-independent.
        long mean = (long)(sum / v.Length);
        sink.Row(0, MetricFormat.Int(tick), MetricFormat.Fx6(mean), MetricFormat.Fx6(min), MetricFormat.Fx6(max), MetricFormat.Int(upTotal));
    }

    private sealed class WalkSystem : ITickSystem
    {
        private readonly LongColumn _value;
        private readonly IntColumn _ups;
        private readonly long _drift;
        private readonly long _noise;
        private readonly long _failAt;

        public WalkSystem(LongColumn value, IntColumn ups, long drift, long noise, long failAt)
        {
            _value = value;
            _ups = ups;
            _drift = drift;
            _noise = noise;
            _failAt = failAt;
        }

        public int SystemId => 1;

        public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd)
        {
            if (context.Tick == _failAt && chunk == 0)
                throw new InvalidOperationException("test.random-walk: fail_at_tick reached (intended failure).");
            ReadOnlySpan<long> cur = _value.Current;
            Span<long> next = _value.Next;
            ReadOnlySpan<int> upsCur = _ups.Current;
            Span<int> upsNext = _ups.Next;
            ulong span = (ulong)(2 * _noise + 1);
            for (int r = rowStart; r < rowEnd; r++)
            {
                // Modulo bias is < 2^-40 for these ranges; irrelevant for a test model.
                long step = (long)(context.Rng.NextU64(context.Tick, SystemId, r, 0) % span) - _noise;
                long delta = checked(_drift + step);
                next[r] = checked(cur[r] + delta);
                upsNext[r] = delta > 0 ? upsCur[r] + 1 : upsCur[r];
            }
        }

        public void Merge(in TickContext context, int chunkCount) { }

        public void Post(in TickContext context) { }
    }
}
