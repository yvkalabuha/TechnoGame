using Singularity.Core.Industry;

namespace Singularity.Core.Policy;

/// <summary>
/// What limits the bottleneck process of a design: the source of the profile bottleneck, <c>limiter(site, p_b)</c> and whether the process
/// has output (<c>proc_v[site, p_b] &gt; 0</c>; spec A41, v0.13 <c>out_lim</c>). <see cref="Process"/> is <c>p_b</c> (node number), −1 if unknown.
/// </summary>
public readonly record struct BottleneckLimit(BottleneckSource Source, LimiterKind Limiter, bool HasOutput, int Process = -1)
{
    /// <summary>
    /// <c>out_lim(site, p)</c> (spec A41-T, v0.13): the limit of the process is its output — <c>limiter ∈ {VOLUME, LAB_CEILING}</c>, or
    /// <c>EFFORT</c> while the process has no output (a series without output leads its cell since contract r4, but tooling money does
    /// not lift the limit there: <c>v_p = 0</c> ⇒ the useful money stops at <c>ℓ·E_full</c>).
    /// </summary>
    public bool OutLim => Limiter is LimiterKind.Volume or LimiterKind.LabCeiling || Limiter == LimiterKind.Effort && !HasOutput;
}

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
