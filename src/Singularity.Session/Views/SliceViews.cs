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
/// The state after the last non-empty tick. <see cref="StateHash"/> is the hash taken on this tick (ADR-0017 п. 2-4) or null; a view never computes it. <see cref="Actors"/> is the post state of month <see cref="Month"/> (what the compute of
/// <c>Month + 1</c> reads); <see cref="Inputs"/> are the inputs the compute of <see cref="Month"/> saw, null if this tick ran no monthly step.
/// The decisions of month 0 are made by <c>Initialize</c> before tick 0, so the initial view (<c>Month</c> −1) carries them in <c>Decisions</c>; pair them with the
/// <c>Requirement</c> of the <c>Inputs</c> of the month-0 view. <see cref="Step"/> holds the results of that step (step traces, SLICE_COMPOSITION §13.1); it is null exactly when <see cref="Inputs"/> is.
/// </summary>
public sealed record SliceView(
    long Tick, CivilDate Date, string? StateHash,
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
    NodeList Nodes,
    ImmutableArray<DesignView> Designs,
    PolicyDecisionView Decisions,
    AlloyCoverage Alloy = default,
    ImmutableArray<ResearchView> Research = default,
    int UnknownNodes = 0,
    ImmutableArray<InstituteView> Institutes = default);

/// <summary>
/// An institute of an actor (null array without institutes): <see cref="Id"/> is the data ID, <see cref="Levels"/> the level 0..4 per discipline of the scenario (0 = hidden in the card, #489),
/// <see cref="Goal"/> the node number it works on (0 = free or not built). A candidate has <see cref="Standing"/> false; <see cref="BuildStart"/> is the month its project started (−1 none)
/// and <see cref="BuildMonths"/> its length, <see cref="ProposedGoal"/> / <see cref="ProposesBuild"/> what the AI would do (the adviser: filled for the actor of the player, whose slots the AI does not touch), <see cref="BuildRate"/> the share of the research money it takes per month while it runs, <see cref="RefYear"/> the historical year.
/// </summary>
public sealed record InstituteView(int Slot, string Id, bool Standing, int Goal, ImmutableArray<int> Levels, long BuildStart, int BuildMonths, Fx6 BuildRate, int RefYear, int TotalLevels,
    int ProposedGoal = 0, bool ProposesBuild = false);

/// <summary>A research in progress (TASK-0119): a frontier node with accumulated progress. <see cref="Have"/> against <see cref="Need"/> is the scale of the why_not link;
/// <see cref="Threshold"/> is a hazard node at its threshold, where its discovery is a draw, not certain.</summary>
public readonly record struct ResearchView(int Node, Fx6 Have, Fx6 Need, bool Threshold);

/// <summary>
/// A node of the knowledge graph; <see cref="Process"/> is set for the nodes that are capability processes (PRC). <see cref="KnownSince"/> is
/// <c>know.since.N</c> (−1 unknown), present only in L_C and L_P, null elsewhere.
/// </summary>
public readonly record struct NodeView(int Number, bool Known, bool ByLicence, ProcessView? Process, long? KnownSince);

/// <summary>A process at the actor's home site (A27: a site is an actor), month <c>Month + 1</c> of the snapshot.</summary>
public readonly record struct ProcessView(Fx6 Yield, Fx6 V, bool Admitted, Fx6 UnitCostMult, bool Mastered, Fx6 LicenceShare);

/// <summary>
/// A design slot. <c>Gen</c> is the technology generation of the design (G1..G4 of the product class); <c>SlotGeneration</c> counts how many
/// times the slot was taken (ADR-0005 p. 3): the pair (<c>Design</c>, <c>SlotGeneration</c>) is the handle of a project, and a reused slot has a larger value.
/// </summary>
public sealed record DesignView(
    int Design, DesignStage Stage, int PlatformGen, int Gen, int Parent, int StageOMonth,
    bool BuiltAny, bool Cancelled, Fx6 Rate, Fx6 UnitCost, Fx6 UnitCostFloor,
    Fx6 InService, Fx6 Readiness, Fx6 Perf, LineBind Bind, bool AiLine,
    ImmutableArray<int> PrcSet, int SlotGeneration = 0)
{
    /// <summary>The handle UI and clients put into commands about this project: never computed on the client side (ADR-0005 p. 3).</summary>
    public DesignHandle Handle => new(Design, SlotGeneration);
}

public readonly record struct BureauView(int Bureau, int Design, long Granted, long Required);
public readonly record struct QualityView(bool Active, int Start, int TriggerCount, int TrainingTarget);

/// <summary>The licence of the actor as buyer; <see cref="Refusals"/> counts <c>BAD_LICENCE</c> by reason: Self, Empty, Gen, Money.</summary>
public sealed record LicenceView(int Gen, int Seller, int Start, LicenceMask Mask, LicenceMask OfferMask, Fx6 Flow, ImmutableArray<long> Refusals, bool Active);

public readonly record struct DisruptionView(int Start, int End);

/// <summary>The AI decisions of compute <c>Month</c>; they act from <c>Month + 1</c>.</summary>
public sealed record PolicyDecisionView(
    ImmutableArray<DesignCommandView> DesignCommands,
    ImmutableArray<StartView> Starts,
    ImmutableArray<CloseView> Closes,
    ImmutableArray<FleetCommand> Fleet,
    ImmutableArray<int> Repeats);

/// <summary><see cref="Handle"/> is the project a Derive or Cancel names (the generation the AI saw at its decision, ADR-0005 p. 3); <c>default</c> for a Compose.</summary>
public sealed record DesignCommandView(DesignCommandKind Kind, int Target, ImmutableArray<int> Alts, DesignHandle Handle);
/// <summary>An AI start; <see cref="Handle"/> is the handle the AI decided on.</summary>
public readonly record struct StartView(int Design, Fx6 Rate, DesignHandle Handle);

/// <summary>A closed line and the rule that closed it; <see cref="Reason"/> is <c>None</c> in a snapshot without a monthly step (the trace is of that step only).</summary>
public readonly record struct CloseView(int Design, LineCloseReason Reason, DesignHandle Handle);

/// <summary>Inputs of the monthly step <see cref="Month"/> as its compute saw them (after the deals and the commands of the tick, before the step).</summary>
public sealed record MonthInputsView(long Month, int GFront, ImmutableArray<ActorInputsView> Actors);

public sealed record ActorInputsView(
    int Actor, Fx6 Budget, BudgetShares Shares, Fx6 ProcurementBudget,
    ToolingLedgerView Tooling, CapabilityView Capability,
    ImmutableArray<DesignInputsView> Designs,
    RequirementView? Requirement,
    ReserveView? Reserve = null);

/// <summary>
/// The reserve account of an actor (null when the run has none): the balance, what the five items leave into it this month, and what the next step takes
/// as the leak. Money in the units of the core (million 2011 dollars); the display in 1990 prices is the client's.
/// </summary>
public readonly record struct ReserveView(Fx6 Balance, Fx6 Inflow, Fx6 LeakNextMonth);

/// <summary>The variants of the yearly Requirement of the AI (spec §5.5, §7 S1): null unless the month is a yearly one (month mod 12 = 0), and in <see cref="SliceViewMode.Calibration"/>.</summary>
public sealed record RequirementView(int Gen, int Count, ImmutableArray<int> Superior, ImmutableArray<int> Producible, Fx6 FvSuperior, Fx6? FvProducible,
    ImmutableArray<int> PilotSlots);

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
