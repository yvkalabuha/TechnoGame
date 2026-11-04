using System.Collections.Immutable;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Kernel;

namespace Singularity.Core.Products;

/// <summary>Kind of a design command (contract §5).</summary>
public enum DesignCommandKind
{
    /// <summary><c>compose_design(requirement_id, {slot → alt_id})</c>: a new project from the five slot alternatives.</summary>
    Compose,

    /// <summary><c>derive(d_parent, {slot → alt_id})</c>: a project on the platform of a design in С or В; unchanged slots are −1.</summary>
    Derive,

    /// <summary><c>cancel_design(design_id)</c> (contract §4.4e, F14): an own project in П or О becomes «снят»; <c>Target</c> is the <c>design_id</c>.</summary>
    Cancel,
}

/// <summary>
/// A <c>compose_design</c>, <c>derive</c> or <c>cancel_design</c> command in force at month t (contract §4.4a, §4.4e, §5; ADR-0006, ADR-0014: applied at the
/// start of the tick, in command order). <paramref name="Target"/> is the requirement generation <c>g*</c> (the newest APP the actor
/// knows) for <see cref="DesignCommandKind.Compose"/> and the parent <c>design_id</c> for <see cref="DesignCommandKind.Derive"/>.
/// <paramref name="Alts"/> holds one layout-global alternative index per slot (−1 = unchanged, Derive only).
/// </summary>
public sealed record DesignCommand(DesignCommandKind Kind, int Target, ImmutableArray<int> Alts);

/// <summary>
/// <c>start_series(design_id, rate, force)</c> in force at month t (contract §4.4c). <c>force = true</c> is a player command only:
/// the AI never sends it (the decision-journal invariant lives with the AI; the layer cannot tell the sender).
/// </summary>
public readonly record struct SeriesStart(Fx6 Rate, bool Force);

/// <summary>Why the layer rejected a command; counted per actor (<see cref="ProductsSystem.RejectionCount"/>) as the journal of rejections.</summary>
public enum CommandRejection
{
    /// <summary>No free design slot (<c>DESIGN_CAPACITY</c>, contract §3.1).</summary>
    DesignCapacity = 0,

    /// <summary>No free bureau.</summary>
    NoBureau = 1,

    /// <summary>An alternative is not in <c>avail</c>, a slot is missing or out of range.</summary>
    SlotUnavailable = 2,

    /// <summary><c>rank(airframe) &lt; platform_min_rank[g*]</c>.</summary>
    PlatformThreshold = 3,

    /// <summary>Wrong requirement generation (not the newest known APP).</summary>
    BadRequirement = 4,

    /// <summary>Derive: parent not an own design in С or В, airframe changed, or no slot changed.</summary>
    BadDerive = 5,

    /// <summary><c>SERIES_GATE</c>: the series gate is closed and <c>force = false</c> (contract §4.4c, §6).</summary>
    SeriesGate = 6,

    /// <summary>
    /// <c>start_series</c> of a parent whose line was moved to a live derived design: two lines of one platform are FUTURE (#118).
    /// </summary>
    LineTransferred = 7,

    /// <summary><c>BAD_CANCEL</c>: <c>cancel_design</c> of a design that is not an own project in П or О (contract §4.4e).</summary>
    BadCancel = 8,

    // Fleet commands (contract r6 §4.4f): the code names the command and the first check it failed, in the order of the validator.
    /// <summary><c>BAD_RETIRE{SLOT}</c>: a second <c>retire_cohort</c> of the project in the month.</summary>
    BadRetireSlot = 9,
    /// <summary><c>BAD_RETIRE{STAGE}</c>: not an own project in С or В.</summary>
    BadRetireStage = 10,
    /// <summary><c>BAD_RETIRE{COUNT}</c>: <c>n</c> outside <c>1 … ⌊Σcount⌋</c>.</summary>
    BadRetireCount = 11,
    /// <summary><c>BAD_SELL{SLOT}</c>: a second <c>sell_cohort</c> of the project in the month.</summary>
    BadSellSlot = 12,
    /// <summary><c>BAD_SELL{STAGE}</c>: not an own project in В.</summary>
    BadSellStage = 13,
    /// <summary><c>BAD_SELL{COUNT}</c>: <c>n</c> outside <c>1 … ⌊Σcount⌋</c>.</summary>
    BadSellCount = 14,
    /// <summary><c>BAD_SELL{NOT_SURPLUS}</c>: <c>gap = G_front − gen(d) &lt; 1</c>.</summary>
    BadSellNotSurplus = 15,
    /// <summary><c>BAD_SELL{QUOTA}</c>: over the project's quota of the policy year.</summary>
    BadSellQuota = 16,
    /// <summary><c>BAD_SELL{REVENUE_CAP}</c>: over the actor's revenue ceiling of the policy year.</summary>
    BadSellRevenueCap = 17,
}

/// <summary>Kind of a fleet command (contract r6 §4.4f). Names leave room for <c>mothball_cohort</c> of the second wave.</summary>
public enum FleetCommandKind
{
    /// <summary><c>sell_cohort(design_id, n)</c>: a stage В project, to buyers outside the slice, for money.</summary>
    Sell = 0,
    /// <summary><c>retire_cohort(design_id, n)</c>: units leave the fleet without money.</summary>
    Retire = 1,
}

/// <summary><c>retire_cohort</c> / <c>sell_cohort</c> in force at month t, in command order (contract r6 §4.4f; ADR-0006, ADR-0014).</summary>
public readonly record struct FleetCommand(FleetCommandKind Kind, int Design, long N);

/// <summary>
/// What the product layer reads from other layers and from commands in the compute of month t. Contract (as
/// <c>IIndustryInputs</c>): every method is a pure function of the snapshot t−1 (the state after month t−1, holding the
/// decisions in force at t) and of its arguments; no side effects, thread-safe. The layer has no RNG (#28): nothing
/// here may draw one on its behalf either.
/// </summary>
public interface IProductsInputs
{
    /// <summary><c>known(actor, node)</c> by stable node number in the knowledge snapshot (contract §5).</summary>
    bool Knows(int actorRow, int nodeNumber, long month);

    /// <summary>
    /// <c>yield_p</c> of the process on the site for month t, 0 when the owner does not know it (the value capability
    /// runs output at, contract §4.2). Read by the «технологичный» variant rule (spec §1.4).
    /// </summary>
    Fx6 AdmittedYield(int siteRow, int processNumber, long month);

    /// <summary><c>production_profile(site, prc_set)</c> of month t (contract §5).</summary>
    ProductionProfile Profile(int siteRow, ReadOnlySpan<PrcSetItem> prcSet, long month);

    /// <summary>Alloy supply of the site in month t, kg (spec §1.4, §3.5 A23).</summary>
    Fx6 AlloySupply(int siteRow, long month);

    /// <summary>
    /// <c>granted</c> of the request this layer made in month t−1 (contract §4.6: visible at t+1). Category and
    /// requester id as in <see cref="ProductsLayout"/>.
    /// </summary>
    long Granted(int siteRow, int category, int requesterId, long month);

    /// <summary>Procurement budget of month t: <c>share_P(profile)·budget_a(t)</c> (spec §3.3, §3.4); ≥ 0.</summary>
    Fx6 ProcurementBudget(int actorRow, long month);

    /// <summary>
    /// <c>ProcurementBase(a, t) = share_P·budget_a(t)</c>: the procurement item without licence flows and sale revenue (contract r6 §5); ≥ 0.
    /// <see cref="ProcurementBudget"/> minus this is the <c>LicenceFlow</c>.
    /// </summary>
    Fx6 ProcurementBase(int actorRow, long month);

    /// <summary>
    /// <c>mastered(a, p)</c>: <c>mastered_run ≥ 6</c> at some site of the actor (contract r6 §9.1), a pure function of the capability snapshot.
    /// </summary>
    bool Mastered(int actorRow, int processNumber, long month);

    /// <summary><c>retire_cohort</c> and <c>sell_cohort</c> in force at month t: the external ones first, then the AI's (contract r6 §4.4f).</summary>
    IReadOnlyList<FleetCommand> FleetCommands(int actorRow, long month);

    /// <summary>
    /// <c>compose_design</c> and <c>derive</c> commands applied at the start of month t, in command order (contract §4.4a). The
    /// AI sends the same commands as the player; presets (<see cref="DesignRules"/>) are its helpers, not part of the layer.
    /// </summary>
    IReadOnlyList<DesignCommand> DesignCommands(int actorRow, long month);

    /// <summary><c>start_series(design, rate, force)</c> in force for a design in stage О or В, or null (contract §4.4c).</summary>
    SeriesStart? StartSeries(int actorRow, int design, long month);

    /// <summary>Close the line of a design in series (stage С → В; «снят» when it never produced, contract §4.4e r4).</summary>
    bool CloseLine(int actorRow, int design, long month);

    /// <summary>
    /// Line rate the AI asks for in month t, units per month (spec §4 "темп линий"); ≥ 0. Null keeps the rate of the last
    /// <c>start_series</c> command.
    /// </summary>
    Fx6? LineRate(int actorRow, int design, long month);

    /// <summary>
    /// <c>TrainingBudget(actor, design, t)</c> = <c>T_a</c> of the tooling plan of month t when the line of <paramref name="design"/> is the
    /// actor's training series, else 0 (contract §4.4d step 7, spec §3.5 A39, A41; F15); ≥ 0. Step 7 spends it after the common money
    /// left, on that line only; what is not spent is an unspent residue of the month.
    /// </summary>
    Fx6 TrainingBudget(int actorRow, int design, long month);

    /// <summary>L_P only: month index in whose merge the APP node of the generation became known to the actor, −1 if unknown.</summary>
    long AppKnownSince(int actorRow, int gen, long month);
}
