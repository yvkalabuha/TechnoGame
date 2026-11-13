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
/// </summary>
public sealed record SliceView(
    long Tick, CivilDate Date, string StateHash,
    long Month,
    ImmutableArray<ActorView> Actors,
    MonthInputsView? Inputs);

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

/// <summary>A node of the knowledge graph; <see cref="Process"/> is set for the nodes that are capability processes (PRC).</summary>
public sealed record NodeView(int Number, bool Known, bool ByLicence, ProcessView? Process);

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
public sealed record LicenceView(int Gen, int Seller, int Start, ulong Mask, ulong OfferMask, Fx6 Flow, ImmutableArray<long> Refusals);

public readonly record struct DisruptionView(int Start, int End);

/// <summary>The AI decisions of compute <c>Month</c>; they act from <c>Month + 1</c>.</summary>
public sealed record PolicyDecisionView(
    ImmutableArray<DesignCommandView> DesignCommands,
    ImmutableArray<StartView> Starts,
    ImmutableArray<int> Closes,
    ImmutableArray<FleetCommand> Fleet,
    ImmutableArray<int> Repeats);

public sealed record DesignCommandView(DesignCommandKind Kind, int Target, ImmutableArray<int> Alts);
public readonly record struct StartView(int Design, Fx6 Rate);

/// <summary>Inputs of the monthly step <see cref="Month"/> as its compute saw them (after the deals and the commands of the tick, before the step).</summary>
public sealed record MonthInputsView(long Month, int GFront, ImmutableArray<ActorInputsView> Actors);

public sealed record ActorInputsView(
    int Actor, Fx6 Budget, BudgetShares Shares, Fx6 ProcurementBudget,
    ToolingLedgerView Tooling, CapabilityView Capability,
    ImmutableArray<DesignInputsView> Designs);

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
