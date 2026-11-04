using Singularity.Core.WhyNot;
using Singularity.Kernel;

namespace Singularity.Core.Policy;

/// <summary>The subject of a licence offer (spec A43.2): the seller's project <c>d_s</c> in С/В with the newest engine, as its price and mask need it.</summary>
/// <param name="UnitCostFloor"><c>unit_cost_floor(d_s)</c>.</param>
/// <param name="Mask">PRC of the alternatives of the slots <c>engine</c> and <c>hot_section</c> (bit <c>n − 1</c> for node <c>n</c>), as of the decision.</param>
public readonly record struct LicenceSubject(Fx6 UnitCostFloor, ulong Mask);

/// <summary>
/// What the events and the why_not-driven decisions of the policy read (spec §3.5 A43): pure functions of the snapshot of month t, no side
/// effects, no RNG (as <see cref="IPolicyInputs"/>). Without it the policy has no licences and no why_not triggers.
/// </summary>
public interface IPolicyEvents
{
    /// <summary><c>g_a</c> (A43.2): the highest <c>eng_gen</c> of the engine alternative among the actor's designs in С or В; 0 without one.</summary>
    int EngineGen(int actorRow, long month);

    /// <summary><c>d_s</c> of the seller for the engine generation <paramref name="gen"/> (the largest <c>design_id</c> in С/В with that <c>eng_gen</c>); null if none.</summary>
    LicenceSubject? Subject(int sellerRow, int gen, long month);

    /// <summary><c>W(a, t) = why_not(a, τ(a), t)</c> (A43.7): <c>τ</c> is the current requirement, <c>Requirement(1)</c> if no APP is known, none (empty list) if the newest one is fielded.</summary>
    IReadOnlyList<Link> WhyNot(int actorRow, long month);

    /// <summary><c>why_not(a, Design(d), t)</c>.</summary>
    IReadOnlyList<Link> WhyNotDesign(int actorRow, int design, long month);
}
