namespace Singularity.Kernel;

/// <summary>
/// Counter-based RNG (ADR-0004): a random value is a pure function of
/// (seed, tick, system, entity, n). There is no mutable state, so the result does not
/// depend on thread count or on the order in which entities are processed.
/// </summary>
/// <remarks>
/// Key chain: h = mix(seed ^ C_seed); h = mix(h ^ tick ^ C_tick); h = mix(h ^ system ^ C_system);
/// h = mix(h ^ entity ^ C_entity); h = mix(h ^ n ^ C_n). Chaining makes the key order-sensitive,
/// so (tick=a, entity=b) and (tick=b, entity=a) give different values; the per-field constants
/// keep zero fields from collapsing into each other. tick is taken as 64-bit two's complement,
/// int fields are zero-extended from 32 bits. Changing any of this changes every reference vector.
/// </remarks>
public readonly struct CounterRng
{
    // Per-field constants: SHA-512 initial hash values (nothing-up-my-sleeve numbers).
    private const ulong SeedSalt = 0x6a09e667f3bcc908UL;
    private const ulong TickSalt = 0xbb67ae8584caa73bUL;
    private const ulong SystemSalt = 0x3c6ef372fe94f82bUL;
    private const ulong EntitySalt = 0xa54ff53a5f1d36f1UL;
    private const ulong CounterSalt = 0x510e527fade682d1UL;

    private const double TwoPowMinus53 = 1.0 / (1UL << 53);

    public ulong Seed { get; }

    public CounterRng(ulong seed) => Seed = seed;

    public ulong NextU64(long tick, int system, int entity, int n)
    {
        ulong h = Mix(Seed ^ SeedSalt);
        h = Mix(h ^ (ulong)tick ^ TickSalt);
        h = Mix(h ^ (uint)system ^ SystemSalt);
        h = Mix(h ^ (uint)entity ^ EntitySalt);
        return Mix(h ^ (uint)n ^ CounterSalt);
    }

    /// <summary>Uniform value in [0, 1): the top 53 bits scaled by 2^-53 (exact in double).</summary>
    public double NextUnit(long tick, int system, int entity, int n) =>
        (NextU64(tick, system, entity, n) >> 11) * TwoPowMinus53;

    /// <summary>SplitMix64 step: golden-gamma increment followed by the Stafford variant-13 finalizer.</summary>
    public static ulong Mix(ulong x)
    {
        unchecked
        {
            ulong z = x + 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
