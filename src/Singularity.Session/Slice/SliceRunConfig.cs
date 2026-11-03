using Singularity.Kernel;

namespace Singularity.Session;

/// <summary>Channel ablations of the slice (spec §6.2): each switch zeroes exactly one quantity (CbCultureRule → V23CultureRule for the last).</summary>
[Flags]
public enum SliceChannels
{
    None = 0,

    /// <summary><c>α = 0</c>.</summary>
    Alpha = 1 << 0,

    /// <summary><c>ramp ≡ 1</c> (<c>λ0 = 1</c>).</summary>
    Ramp = 1 << 1,

    /// <summary><c>b = 0</c>: no assembly learning (<c>q10_b = 0</c>).</summary>
    B = 1 << 2,

    /// <summary><c>η = 0</c>.</summary>
    Eta = 1 << 3,

    /// <summary><c>k_catch = 0</c>.</summary>
    KCatch = 1 << 4,

    /// <summary><c>σ = 0</c>.</summary>
    Sigma = 1 << 5,

    /// <summary><c>k_diff = 0</c>: the catching-up channel of knowledge.</summary>
    KDiff = 1 << 6,

    /// <summary><c>E ≡ 0</c> (ZERO): no process-development distribution, the whole item D stays in <c>rest_a</c> (<c>E_full = 0</c>).</summary>
    EffortZero = 1 << 7,

    /// <summary><c>k_tr = 0</c>: the licence acts only through <c>X½</c>.</summary>
    KTr = 1 << 8,

    /// <summary><c>k_qp = 0</c>: the quality programme's contribution.</summary>
    KQp = 1 << 9,

    /// <summary>C-B → R0: the culture rule of contract v2.3 (<see cref="Singularity.Core.Industry.V23CultureRule"/>).</summary>
    CultureR0 = 1 << 10,
}

/// <summary>Start variant of the scenario (spec §6.5).</summary>
public enum StartVariant
{
    Scenario = 0,

    /// <summary><c>START_PERT</c>: estimated fields scaled by ±20% multipliers from the <c>START</c> stream.</summary>
    Perturbed = 1,

    /// <summary><c>START_SYM</c>: USA, GBR and SUN start identical.</summary>
    Symmetric = 2,
}

/// <summary>
/// What defines a run of the slice besides the data (spec §6): the seed, which RNG sources are fixed (<c>FIX_*</c>, §6.3), the start
/// variant, the factorial profile cell (§6.6) and the channel switches (§6.2). All of it except the execution options is written to
/// the <c>run.*</c> columns, so it is part of the state hash.
/// </summary>
public sealed record SliceRunConfig
{
    /// <summary>The seed of a source under <c>FIX_*</c> (spec §6.3).</summary>
    public const ulong FixedSeed = 0x5EED;

    public const string DefaultScenario = "jet_slice_1945";

    public ulong Seed { get; init; }

    public string ScenarioId { get; init; } = DefaultScenario;

    /// <summary><c>FIX_K</c>: streams <c>KNOW</c>, <c>PRIO_J</c> run on <see cref="FixedSeed"/>.</summary>
    public bool FixK { get; init; }

    /// <summary><c>FIX_E</c>: streams <c>EVT_LIC</c>, <c>EVT_DIS</c>.</summary>
    public bool FixE { get; init; }

    /// <summary><c>FIX_A</c>: stream <c>AI</c> (the profiles).</summary>
    public bool FixA { get; init; }

    public StartVariant Start { get; init; }

    /// <summary>Factorial cell <c>9u + 3g + s</c> of the profiles of USA, GBR, SUN (§6.6); null = all profiles from the stream (main).</summary>
    public int? ProfileCell { get; init; }

    public SliceChannels Channels { get; init; }

    /// <summary>Execution only (L2): threads and rows per chunk. They never change the result.</summary>
    public int Threads { get; init; } = 1;

    public int ChunkSize { get; init; } = 1;

    /// <summary><c>FIX_ALL</c>: every source fixed; with it all seeds give one trajectory (a determinism check).</summary>
    public static SliceRunConfig FixAll(ulong seed) => new() { Seed = seed, FixK = true, FixE = true, FixA = true };

    internal ulong SeedOf(bool fixedSource) => fixedSource ? FixedSeed : Seed;
}

/// <summary>The RNG streams of a run, one <see cref="CounterRng"/> per source (spec §9.1), the seed of a source taken from <c>FIX_*</c>.</summary>
public sealed record SliceStreams(CounterRng Know, CounterRng Events, CounterRng Ai, CounterRng Start)
{
    public static SliceStreams Create(SliceRunConfig config) => new(
        new CounterRng(config.SeedOf(config.FixK)), new CounterRng(config.SeedOf(config.FixE)),
        new CounterRng(config.SeedOf(config.FixA)), new CounterRng(config.Seed));
}
