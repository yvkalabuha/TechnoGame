using Singularity.Core.Industry;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Core.Policy;

/// <summary>
/// One design of an actor as the AI reads it from the snapshot (spec A40, A41): stage, platform generation, the line columns of contract
/// r4, the gate and the numbers of the launch rule. <see cref="GateOpen"/> and <see cref="Hidden"/> are those of the month being computed.
/// </summary>
public readonly record struct PolicyDesign(DesignStage Stage, int PlatformGen, bool Cancelled, bool BuiltAny, int IdleMonths,
    int StageOMonth, int Parent, bool GateOpen, Fx6 Hidden, Fx6 UnitCostFloor);

/// <summary>
/// What the product AI reads from the knowledge, capability and product layers in the compute of month t: pure functions of the
/// snapshot t−1, no side effects, no RNG (as <see cref="IPolicyInputs"/>). Without it the policy has no product decisions at all.
/// </summary>
public interface IPolicyProducts
{
    ProductsLayout Layout { get; }

    /// <summary><c>D_ref = d0·c_ref</c> (contract §4.4b).</summary>
    Fx6 DRef { get; }

    PolicyDesign Design(int actorRow, int design, long month);

    /// <summary>Layout-global alternative per slot of the design (−1 for an empty slot).</summary>
    int[] Alternatives(int actorRow, int design);

    bool Knows(int actorRow, int nodeNumber, long month);

    /// <summary>
    /// <c>admitted(home(a), prc_set(d), t)</c> (contract §4.2, spec A41 F17): every process of the design's <c>prc_set</c> is known to the actor and
    /// has <c>yield &gt; 0</c> at its site in month t.
    /// </summary>
    bool Admitted(int actorRow, int design, long month);

    /// <summary><c>yield_p</c> of the actor's site in month t, 0 when the process is unknown (spec §1.4).</summary>
    Fx6 AdmittedYield(int actorRow, int processNumber, long month);

    Fx6 AlloySupply(int actorRow, long month);

    /// <summary>Bureaus without a design in the snapshot.</summary>
    int FreeBureaus(int actorRow);

    /// <summary><c>FV</c> of a hypothetical project with these alternatives at <paramref name="budget"/> (spec §5.5; the preset <c>adaptive</c>).</summary>
    Fx6 CandidateValue(int actorRow, ReadOnlySpan<int> alts, Fx6 budget, long month);

    /// <summary>
    /// The actor's fleet as the snapshot holds it (spec A42.4; <c>ProductsSystem.SnapshotBook</c>), a fresh copy the caller projects and changes.
    /// Null for a build without a fleet layer: the fleet rule is then silent.
    /// </summary>
    FleetBook? Fleet(int actorRow, long month) => null;

    /// <summary><c>G_front</c> of the snapshot (contract §9.1, #146).</summary>
    int GFront(long month) => 0;
}
