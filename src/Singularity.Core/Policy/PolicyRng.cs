using Singularity.Core.Data;
using Singularity.Kernel;

namespace Singularity.Core.Policy;

/// <summary>
/// RNG streams of the policy and of the start perturbation (spec §9.1): <c>AI</c> (system 5) draws only the profile at tick 0;
/// <c>START</c> (system 6) draws the <c>START_PERT</c> multipliers. Keys use the stable actor number (§3.5 A7).
/// </summary>
public static class PolicyRng
{
    public const int SystemAi = 5;
    public const int SystemStart = 6;

    /// <summary><c>⌊3·U⌋</c> of the stream <c>AI</c>: key (0, 5, actor, 0) (§4, §9.2).</summary>
    public static PolicyProfile Profile(CounterRng ai, int actorNumber) =>
        (PolicyProfile)CounterRng.Index(ai.NextU64(0, SystemAi, actorNumber, 0), 3);

    /// <summary>
    /// <c>START_PERT</c> multiplier of the field with index <paramref name="fieldIndex"/> (§6.5): <c>800 000 + ⌊r·400 001/2⁵³⌋</c> raw,
    /// i.e. [0.8; 1.2], key (0, 6, fieldIndex, 0).
    /// </summary>
    public static Fx6 StartMultiplier(CounterRng start, int fieldIndex) =>
        Fx6.FromRaw(CounterRng.UniformInt(start.NextU64(0, SystemStart, fieldIndex, 0), 800_000, 1_200_000));
}
