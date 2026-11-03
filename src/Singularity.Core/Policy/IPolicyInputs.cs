using Singularity.Core.Industry;

namespace Singularity.Core.Policy;

/// <summary>What limits the bottleneck process of a design: the source of the profile bottleneck and <c>limiter(site, p_b)</c> (spec A41).</summary>
public readonly record struct BottleneckLimit(BottleneckSource Source, LimiterKind Limiter);

/// <summary>
/// What the AI policy reads from the other layers in the compute of month t. As the other inputs: pure functions of the snapshot t−1, no
/// side effects, thread-safe, no RNG.
/// </summary>
public interface IPolicyInputs
{
    /// <summary>The actor's design in series (stage С) with the highest <c>design_id</c>, −1 without one.</summary>
    int NewestSeriesDesign(int actorRow, long month);

    /// <summary>Bottleneck of the production profile of the design in month t and the limiter of that process at the actor's site; null when the profile has none.</summary>
    BottleneckLimit? Bottleneck(int actorRow, int design, long month);

    /// <summary>The actor has a design in series whose profile quality <c>q</c> is below <paramref name="threshold"/> (the P2 trigger, spec A41).</summary>
    bool SeriesBelowQuality(int actorRow, Kernel.Fx6 threshold, long month);
}
