using Singularity.Kernel;

namespace Singularity.Core.Knowledge;

/// <summary>
/// RNG streams of the knowledge layer (slice spec §9.1). Keys use stable data identifiers only: the
/// actor number (scenario order, §3.5 A7) and the node <c>number</c> from the data, never a storage
/// row; <c>tick</c> is the month index t, not the day tick (#60).
/// </summary>
public static class KnowledgeRng
{
    public const int SystemKnow = 1;
    public const int SystemPrioJ = 2;

    /// <summary>Node numbers fit into <c>actor·64 + node</c> (§9.1); node 0 is reserved for L_K.</summary>
    public const int MaxNodeNumber = 63;

    public static int Entity(int actorNumber, int nodeNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(actorNumber);
        ArgumentOutOfRangeException.ThrowIfNegative(nodeNumber);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(nodeNumber, MaxNodeNumber);
        return checked(actorNumber * 64 + nodeNumber);
    }

    /// <summary><c>U_know</c> of a hazard node in month t.</summary>
    public static ulong Hazard(CounterRng rng, long month, int actorNumber, int nodeNumber) =>
        rng.NextU64(month, SystemKnow, Entity(actorNumber, nodeNumber), 0);

    /// <summary>
    /// Spread multiplier <c>j ∈ [0.5; 1.5]</c> of year y, drawn at month 12y (§3.1, §9.2):
    /// <c>500 000 + ⌊r·1 000 001 / 2⁵³⌋</c> raw.
    /// </summary>
    public static Fx6 J(CounterRng rng, int year, int actorNumber, int nodeNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(year);
        ulong u = rng.NextU64(12L * year, SystemPrioJ, Entity(actorNumber, nodeNumber), 0);
        return Fx6.FromRaw(CounterRng.UniformInt(u, 500_000, 1_500_000));
    }

    /// <summary>L_K "generation clock" draw of generation g (1…4): <c>n = 1 + g</c> at month 0, entity <c>actor·64</c>.</summary>
    public static ulong ScheduleClock(CounterRng rng, int actorNumber, int gen) =>
        rng.NextU64(0, SystemKnow, Entity(actorNumber, 0), 1 + gen);

    /// <summary>L_K "never" indicator draw of generation g (1…4): <c>n = 5 + g</c>.</summary>
    public static ulong ScheduleNever(CounterRng rng, int actorNumber, int gen) =>
        rng.NextU64(0, SystemKnow, Entity(actorNumber, 0), 5 + gen);
}
