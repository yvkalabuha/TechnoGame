using Singularity.Core.Industry;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Core.Policy;

/// <summary>
/// One design of an actor as the AI reads it from the snapshot (spec A40, A41): stage, platform generation, the line columns of contract
/// r4, the gate and the numbers of the launch rule. <see cref="GateOpen"/> and <see cref="Hidden"/> are those of the month being computed.
/// </summary>
public readonly record struct PolicyDesign(DesignStage Stage, int PlatformGen, bool Cancelled, bool BuiltAny, int IdleMonths,
    int StageOMonth, int Parent, bool GateOpen, Fx6 Hidden, Fx6 UnitCostFloor, int SlotGeneration)
{
    /// <summary>The handle the AI's decisions about this project carry (ADR-0005 p. 3): the generation is the snapshot's at the moment of decision.</summary>
    public DesignHandle HandleOf(int slot) => new(slot, SlotGeneration);
}

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

    /// <summary><c>prc_set</c> of the design (ascending process numbers); an implementation may return a shared array: do not mutate.</summary>
    int[] PrcSet(int actorRow, int design) => DesignRules.PrcSet(Layout, Alternatives(actorRow, design));

    bool Knows(int actorRow, int nodeNumber, long month);

    /// <summary>
    /// <c>admitted(home(a), prc_set(d), t)</c> (contract §4.2, spec A41 F17): every process of the design's <c>prc_set</c> is known to the actor and
    /// has <c>yield &gt; 0</c> at its site in month t.
    /// </summary>
    bool Admitted(int actorRow, int design, long month);

    /// <summary><c>yield_p</c> of the actor's site in month t, 0 when the process is unknown (spec §1.4).</summary>
    Fx6 AdmittedYield(int actorRow, int processNumber, long month);

    /// <summary>The yield the culture allows without the experience ramp (see <c>IProductsInputs.CultureYield</c>); 0 when unknown.</summary>
    Fx6 CultureYield(int actorRow, int processNumber, long month) => AdmittedYield(actorRow, processNumber, month);

    Fx6 AlloySupply(int actorRow, long month);

    /// <summary>Bureaus without a design in the snapshot.</summary>
    int FreeBureaus(int actorRow);

    /// <summary><c>FV</c> of a hypothetical project with these alternatives at <paramref name="budget"/> (spec §5.5; the preset <c>adaptive</c>).</summary>
    Fx6 CandidateValue(int actorRow, ReadOnlySpan<int> alts, Fx6 budget, long month);

    /// <summary>
    /// The actor's fleet as the snapshot holds it (spec A42.4; <c>ProductsSystem.SnapshotBook</c>), a copy the caller projects and changes. It may be refilled by the next call on the same thread: use it before asking again, never keep it.
    /// Null for a build without a fleet layer: the fleet rule is then silent.
    /// </summary>
    FleetBook? Fleet(int actorRow, long month) => null;

    /// <summary>
    /// Static foresight (production-mastery.md rev 2 p. 3): <c>Hcap(month + horizon) &gt; r_d</c> in every active dimension of the process at the actor's site.
    /// A pure function of the snapshot. A build without mastery says yes.
    /// </summary>
    bool Masterable(int actorRow, int processNumber, long month, int horizon) => true;

    /// <summary>The actor has a mastery programme on the process in the snapshot (TASK-0134).</summary>
    bool ProgrammeActive(int actorRow, int processNumber) => false;

    /// <summary>Mastery programmes of the actor in the snapshot.</summary>
    int ProgrammeCount(int actorRow) => 0;

    /// <summary>Another actor has mastered the process: the «abroad» mode of the programme has a donor (<c>world_lead</c>).</summary>
    bool Donor(int actorRow, int processNumber) => false;

    /// <summary>Whether a programme may start on the process now; the one pure function the command and the AI share.</summary>
    ProgrammeVerdict ProgrammeVerdict(int actorRow, int processNumber, bool abroad, long month) => new(MasteryRefusal.OutOfScope);

    /// <summary><c>G_front</c> of the snapshot (contract §9.1, #146).</summary>
    int GFront(long month) => 0;

    /// <summary>
    /// The slot generation of the project whose open (or <paramref name="close"/>) proposal the player refused, consumed at <c>month</c> (TASK-0153); 0 = no refusal.
    /// A build without an adviser says 0.
    /// </summary>
    int ProposalRefusal(int actorRow, int design, bool close, long month) => 0;

    /// <summary>The player's <c>start_series</c> for the design is consumed at <c>month</c> (TASK-0153): on an AI line it hands the line over to the player.</summary>
    bool PlayerSeries(int actorRow, int design, long month) => false;
}
