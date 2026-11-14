using System.Collections.Immutable;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session;

// Read views of the slice (SLICE_COMPOSITION §12, ADR-0014 п. 5–6): immutable snapshots published after a non-empty tick.
// Only data and no logic: every number is a call of a public pure function of Core, made in ViewBuilder.

/// <summary>Read mode of the views (an execution option, not state): <see cref="Calibration"/> never calls <c>KeyOf</c>, <c>MaxFleetValue</c> or <c>why_not</c> (spec §8.2 п. 2).</summary>
public enum SliceViewMode
{
    Full = 0,
    Calibration = 1,
}

/// <summary>Fixed shape of the world: ids, numbers and sizes the rows of the views refer to. Built once in <c>Create</c>.</summary>
public sealed record SliceLayoutView(
    ImmutableArray<string> ActorIds,
    ImmutableArray<int> NodeNumbers,
    ImmutableArray<int> NodeGen,
    ImmutableArray<int> ProcessNumbers,
    ImmutableArray<int> ProcessTier,
    ImmutableArray<string> Clusters,
    ImmutableArray<int> GenApp,
    int Designs, int Bureaus, int Generations, int TAb, Fx6 MasteredYield, Fx6 FvBudget);

/// <summary>
/// The state after the last non-empty tick. <see cref="Actors"/> is the post state of month <see cref="Month"/> (what the compute of
/// <c>Month + 1</c> reads); <see cref="Inputs"/> are the inputs the compute of <see cref="Month"/> saw, null if this tick ran no monthly step.
/// The decisions of month 0 are made by <c>Initialize</c> before tick 0, so the initial view (<c>Month</c> −1) carries them in <c>Decisions</c>; pair them with the
/// <c>Requirement</c> of the <c>Inputs</c> of the month-0 view. <see cref="Step"/> holds the results of that step (step traces, SLICE_COMPOSITION §13.1); it is null exactly when <see cref="Inputs"/> is.
/// </summary>
public sealed record SliceView(
    long Tick, CivilDate Date, string StateHash,
    long Month,
    ImmutableArray<ActorView> Actors,
    MonthInputsView? Inputs,
    MonthStepView? Step);

public sealed record ActorView(
    int Actor, PolicyProfile Profile,
    ImmutableArray<Fx6> KD,
    int FieldedGen,
    TechKey? Key,
    Fx6? FleetValue,
    ImmutableArray<BureauView> Bureaus,
    QualityView Quality, LicenceView Licence, DisruptionView Disruption,
    ImmutableArray<NodeView> Nodes,
    ImmutableArray<DesignView> Designs,
    PolicyDecisionView Decisions);

/// <summary>
/// A node of the knowledge graph; <see cref="Process"/> is set for the nodes that are capability processes (PRC). <see cref="KnownSince"/> is
/// <c>know.since.N</c> (−1 unknown), present only in L_C and L_P, null elsewhere.
/// </summary>
public sealed record NodeView(int Number, bool Known, bool ByLicence, ProcessView? Process, long? KnownSince);

/// <summary>A process at the actor's home site (A27: a site is an actor), month <c>Month + 1</c> of the snapshot.</summary>
public readonly record struct ProcessView(Fx6 Yield, Fx6 V, bool Admitted, Fx6 UnitCostMult, bool Mastered, Fx6 LicenceShare);

public sealed record DesignView(
    int Design, DesignStage Stage, int PlatformGen, int Gen, int Parent, int StageOMonth,
    bool BuiltAny, bool Cancelled, Fx6 Rate, Fx6 UnitCost, Fx6 UnitCostFloor,
    Fx6 InService, Fx6 Readiness, Fx6 Perf, LineBind Bind, bool AiLine,
    ImmutableArray<int> PrcSet);

public readonly record struct BureauView(int Bureau, int Design, long Granted, long Required);
public readonly record struct QualityView(bool Active, int Start, int TriggerCount, int TrainingTarget);

/// <summary>The licence of the actor as buyer; <see cref="Refusals"/> counts <c>BAD_LICENCE</c> by reason: Self, Empty, Gen, Money.</summary>
public sealed record LicenceView(int Gen, int Seller, int Start, ulong Mask, ulong OfferMask, Fx6 Flow, ImmutableArray<long> Refusals, bool Active);

public readonly record struct DisruptionView(int Start, int End);

/// <summary>The AI decisions of compute <c>Month</c>; they act from <c>Month + 1</c>.</summary>
public sealed record PolicyDecisionView(
    ImmutableArray<DesignCommandView> DesignCommands,
    ImmutableArray<StartView> Starts,
    ImmutableArray<CloseView> Closes,
    ImmutableArray<FleetCommand> Fleet,
    ImmutableArray<int> Repeats);

public sealed record DesignCommandView(DesignCommandKind Kind, int Target, ImmutableArray<int> Alts);
public readonly record struct StartView(int Design, Fx6 Rate);

/// <summary>A closed line and the rule that closed it; <see cref="Reason"/> is <c>None</c> in a snapshot without a monthly step (the trace is of that step only).</summary>
public readonly record struct CloseView(int Design, LineCloseReason Reason);

/// <summary>Inputs of the monthly step <see cref="Month"/> as its compute saw them (after the deals and the commands of the tick, before the step).</summary>
public sealed record MonthInputsView(long Month, int GFront, ImmutableArray<ActorInputsView> Actors);

public sealed record ActorInputsView(
    int Actor, Fx6 Budget, BudgetShares Shares, Fx6 ProcurementBudget,
    ToolingLedgerView Tooling, CapabilityView Capability,
    ImmutableArray<DesignInputsView> Designs,
    RequirementView? Requirement);

/// <summary>The variants of the yearly Requirement of the AI (spec §5.5, §7 S1): null unless the month is a yearly one (month mod 12 = 0), and in <see cref="SliceViewMode.Calibration"/>.</summary>
public sealed record RequirementView(int Gen, int Count, ImmutableArray<int> Superior, ImmutableArray<int> Producible, Fx6 FvSuperior, Fx6? FvProducible);

/// <summary>Results of the monthly step <see cref="Month"/> (step traces, SLICE_COMPOSITION §13.1): read after the tick, never from an earlier step.</summary>
public sealed record MonthStepView(long Month, ImmutableArray<ActorStepView> Actors);

/// <summary><see cref="Money"/> is null in L_P (no step 7).</summary>
public sealed record ActorStepView(int Actor, MoneyStepView? Money, ImmutableArray<LicenceRefusalView> LicenceMoneyRefusals);

public sealed record MoneyStepView(Fx6 LeftStart, ImmutableArray<LineMoneyView> Lines);

/// <summary>Step 7 money of a line in stage С, by ascending design (<see cref="LineMoneyStep"/>).</summary>
public readonly record struct LineMoneyView(int Design, Fx6 LeftIn, Fx6 FromLeft, Fx6 Training, Fx6 FromTraining);

/// <summary>A licence offer the AI wanted and could not pay (<see cref="MoneyRefusal"/>).</summary>
public readonly record struct LicenceRefusalView(int Seller, int Gen, Fx6 Price, Fx6 Room);

public sealed record ToolingLedgerView(
    Fx6 Rest, Fx6 CapBudget, Fx6 Total, Fx6 TakenRest, Fx6 TakenCap, Fx6 RestReturned, Fx6 TrainingBudget,
    int Participating,
    ImmutableArray<Fx6> UnmetByCluster);

public readonly record struct CapabilityView(Fx6 AlloySupply, Fx6 AlloyDeficit);

/// <summary><see cref="GiveUp"/> is the class of a stage-О give-up (<c>ClassifyGiveUp</c>): set only in stage О of a run with the product AI.</summary>
public readonly record struct DesignInputsView(
    int Design, DesignStage Stage, int IdleMonths, bool GateOpen, Fx6 Hidden, bool Admitted,
    Fx6 YMin, int ArgP,
    GiveUpClass? GiveUp);
