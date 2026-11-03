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

    // Integer draws for Core, which has no double (ADR-0003 R1-R2). Each takes one NextU64 value u
    // and uses its top 53 bits r = u >> 11, i.e. U = r / 2^53, the same U as NextUnit.

    /// <summary>
    /// True with probability p: <c>r &lt; floor(p.Raw * 2^53 / 10^6)</c>, computed in Int128.
    /// p &lt;= 0 is never true, p &gt;= 1 always.
    /// </summary>
    public static bool Chance(ulong u, Fx6 p)
    {
        if (p.Raw <= 0)
            return false;
        Int128 threshold = ((Int128)p.Raw << 53) / Fx6.Scale;
        return (u >> 11) < threshold;
    }

    /// <summary>
    /// Uniform integer on [lo; hi]: <c>lo + floor((hi - lo + 1) * U)</c>, exact in integers.
    /// U has 53 bits, so spans wider than 2^53 do not reach every value.
    /// </summary>
    public static long UniformInt(ulong u, long lo, long hi)
    {
        if (hi < lo)
            throw new ArgumentOutOfRangeException(nameof(hi), hi, "hi must be >= lo.");
        // span <= 2^64 and r < 2^53, so the product fits into 117 bits.
        UInt128 span = (UInt128)((Int128)hi - lo + 1);
        return (long)((Int128)lo + (Int128)(((UInt128)(u >> 11) * span) >> 53));
    }

    /// <summary>Uniform index on [0; count): <c>floor(count * U)</c>, e.g. a profile out of 3.</summary>
    public static int Index(ulong u, int count)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "count must be positive.");
        return (int)UniformInt(u, 0, count - 1);
    }

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
