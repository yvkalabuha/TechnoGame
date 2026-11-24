using System.Collections.Immutable;
using Singularity.Core.Products;

namespace Singularity.Core.Commands;

/// <summary>Why a command was refused (at Submit or at application); the text is for the sender, the code is stable.</summary>
public enum CommandRefusal
{
    None = 0,
    UnknownActor,
    BadSource,
    FocusTooMany,
    FocusRepeat,
    FocusUnknownProcess,
    FocusNotKnown,
    BadShares,
    BadAlternatives,
    BadDesign,
    BadRate,
    DuplicateKey,
    LocalNotMonotonic,
    TooManyDesignCommands,
    SlotOverflow,

    /// <summary>A design handle is malformed: its generation is below 1 (ADR-0005 p. 3). Only the form; whether it is stale is the layer's check at application.</summary>
    BadHandle,

    /// <summary>The handle named a project that no longer stood in its slot when the command was applied (ADR-0005 p. 3); the layer counts it as <c>StaleHandle</c> and leaves the new tenant untouched.</summary>
    StaleHandle,

    /// <summary>
    /// A <c>retire_cohort</c> would lower the best generation in service (#437) and the command did not carry <c>Acknowledged</c>: nothing was queued;
    /// <see cref="CommandVerdict.BestGenBefore"/> and <see cref="CommandVerdict.BestGenAfter"/> say what changes. Not a refusal of capacity or money: the UI asks and sends again.
    /// </summary>
    NeedsConfirmation,

    /// <summary>A mastery programme command names a process that is not in the layout or has no programme (tier below 5).</summary>
    BadProcess,

    /// <summary>The mastery programme check refused the start; <see cref="CommandVerdict.Mastery"/> is the <c>why_not</c> code.</summary>
    ProgrammeRefused,

    /// <summary>A command needs the reserve account and the run has none (<see cref="CommandLimits.ReserveEnabled"/> is false).</summary>
    ReserveOff,

    /// <summary>A programme names a node that is not in the graph.</summary>
    BadNode,

    /// <summary>A programme amount must be positive.</summary>
    BadAmount,

    /// <summary>At application: the reserve holds less than the amount, or the node is not in the frontier of the actor.</summary>
    ProgrammeNotFundable,

    /// <summary>A command needs the institutes and the run has none.</summary>
    InstitutesOff,

    /// <summary>The actor has no institute in that slot.</summary>
    BadSlot,

    /// <summary>At application: the node is known already, or another slot of the actor works on it.</summary>
    PriorityNotAvailable,

    /// <summary>At application: the slot got its node from the player less than the reassignment period ago.</summary>
    PriorityTooSoon,
}

public readonly record struct CommandVerdict(CommandRefusal Reason, string Message, int BestGenBefore = 0, int BestGenAfter = 0,
    MasteryRefusal Mastery = MasteryRefusal.None, int MasteryDetail = 0)
{
    public bool Accepted => Reason == CommandRefusal.None;

    public static CommandVerdict Ok => new(CommandRefusal.None, "accepted");

    public static CommandVerdict Refuse(CommandRefusal reason, string message) => new(reason, message);
}

/// <summary>Sizes a command is checked against, from the layouts of the world (no state).</summary>
public sealed record CommandLimits(int ActorCount, int SlotCount, int Designs, ImmutableArray<int> ProcessNumbers)
{
    /// <summary>The run has the reserve account: budget shares may sum to less than 1 and <c>fund_programme</c> is accepted.</summary>
    public bool ReserveEnabled { get; init; }

    /// <summary>Node numbers of the knowledge graph (for <c>fund_programme</c>).</summary>
    public ImmutableArray<int> NodeNumbers { get; init; } = [];

    /// <summary>Institutes per actor row (<c>set_priority_node</c>); empty = the run has no institutes.</summary>
    public ImmutableArray<int> InstituteSlots { get; init; } = [];

    /// <summary><c>compose</c>/<c>derive</c>/<c>cancel</c> commands one actor can have in force in one month (SLICE_COMPOSITION §7.3, [Предп.] О-5).</summary>
    public const int DesignSlots = 4;

    /// <summary><c>retire_cohort</c>/<c>sell_cohort</c> commands one actor can have in force in one month (a project takes at most one of each type; contract r6 §4.4f).</summary>
    public const int FleetSlots = 8;
}

/// <summary>
/// Validation in two steps (SLICE_COMPOSITION §7.4). <see cref="CheckForm"/> runs at Submit and needs no state: the actor exists, the
/// source may send commands (never <see cref="SourceClass.Ai"/>, so <c>force</c> never comes from the AI, #94), focus without repeats, shares summing to 1
/// exactly, five alternatives. <see cref="CheckState"/> runs at application: the focus names only processes the actor knows.
/// Checks of the product layer (slot, bureau, gate, platform threshold) stay in the layer.
/// </summary>
public static class CommandValidator
{
    public static CommandVerdict CheckForm(in LoggedCommand logged, CommandLimits limits)
    {
        SliceCommand c = logged.Command;
        CommandKey key = logged.Key;
        if (key.SourceId < 0 || key.LocalN < 0 || key.ApplyTick < 0 || !Enum.IsDefined(key.Source))
            return CommandVerdict.Refuse(CommandRefusal.BadSource, $"invalid key {key}");
        // AI decisions are pol.* columns, never commands: nothing from SourceClass.Ai enters the journal (SLICE_COMPOSITION §7.5).
        if (key.Source == SourceClass.Ai)
            return CommandVerdict.Refuse(CommandRefusal.BadSource, "the AI submits no commands: its decisions are pol.* columns");
        if (c.Actor < 0 || c.Actor >= limits.ActorCount)
            return CommandVerdict.Refuse(CommandRefusal.UnknownActor, $"actor {c.Actor} does not exist");
        switch (c)
        {
            case SetBudgetShares s:
                if (limits.ReserveEnabled)
                {
                    // The quality item has no reader in the core: a share there would be money that does nothing.
                    if (s.Shares.Quality > Singularity.Kernel.Fx6.Zero)
                        return CommandVerdict.Refuse(CommandRefusal.BadShares, "the quality item has no effect: its share must be 0 (leave it in the reserve)");
                    return s.Shares.IsValidWithReserve
                        ? CommandVerdict.Ok
                        : CommandVerdict.Refuse(CommandRefusal.BadShares, $"shares must be non-negative and sum to at most 1, got {s.Shares.Sum}");
                }
                return s.Shares.IsValid
                    ? CommandVerdict.Ok
                    : CommandVerdict.Refuse(CommandRefusal.BadShares, $"shares must be non-negative and sum to exactly 1, got {s.Shares.Sum}");
            case SetPriorityNode pn:
                if (limits.InstituteSlots.IsDefaultOrEmpty)
                    return CommandVerdict.Refuse(CommandRefusal.InstitutesOff, "this run has no institutes");
                if ((uint)pn.Actor >= (uint)limits.InstituteSlots.Length || (uint)pn.Slot >= (uint)limits.InstituteSlots[pn.Actor])
                    return CommandVerdict.Refuse(CommandRefusal.BadSlot, $"actor {pn.Actor} has no institute slot {pn.Slot}");
                return limits.NodeNumbers.Contains(pn.Node)
                    ? CommandVerdict.Ok
                    : CommandVerdict.Refuse(CommandRefusal.BadNode, $"node {pn.Node} is not in the graph");
            case FundProgramme fp:
                if (!limits.ReserveEnabled)
                    return CommandVerdict.Refuse(CommandRefusal.ReserveOff, "this run has no reserve account");
                if (!limits.NodeNumbers.Contains(fp.Node))
                    return CommandVerdict.Refuse(CommandRefusal.BadNode, $"node {fp.Node} is not in the graph");
                return fp.Amount > Singularity.Kernel.Fx6.Zero
                    ? CommandVerdict.Ok
                    : CommandVerdict.Refuse(CommandRefusal.BadAmount, "the programme amount must be positive");
            case SetEffortFocus f:
                if (f.Processes.IsDefault || f.Processes.Length > 3)
                    return CommandVerdict.Refuse(CommandRefusal.FocusTooMany, "effort_focus holds at most 3 processes");
                for (int i = 0; i < f.Processes.Length; i++)
                {
                    if (!limits.ProcessNumbers.Contains(f.Processes[i]))
                        return CommandVerdict.Refuse(CommandRefusal.FocusUnknownProcess, $"process {f.Processes[i]} is not in the layout");
                    for (int j = 0; j < i; j++)
                    {
                        if (f.Processes[i] == f.Processes[j])
                            return CommandVerdict.Refuse(CommandRefusal.FocusRepeat, $"effort_focus repeats process {f.Processes[i]}");
                    }
                }
                return CommandVerdict.Ok;
            case ComposeDesign d:
                return d.Generation < 1 || d.Alts.IsDefault || d.Alts.Length != limits.SlotCount || d.Alts.Any(a => a < 0)
                    ? CommandVerdict.Refuse(CommandRefusal.BadAlternatives, $"compose_design needs generation ≥ 1 and {limits.SlotCount} alternatives")
                    : CommandVerdict.Ok;
            case DeriveDesign v:
                if (HandleRefusal(v.Parent, limits) is { } badParent)
                    return badParent;
                return v.Alts.IsDefault || v.Alts.Length != limits.SlotCount || v.Alts.Any(a => a < -1)
                    ? CommandVerdict.Refuse(CommandRefusal.BadAlternatives, $"derive needs {limits.SlotCount} alternatives (−1 = unchanged)")
                    : CommandVerdict.Ok;
            case StartSeries s:
                if (HandleRefusal(s.Design, limits) is { } badStart)
                    return badStart;
                if (s.Rate.Raw < 0)
                    return CommandVerdict.Refuse(CommandRefusal.BadRate, "the rate must not be negative");
                return CommandVerdict.Ok;
            case CancelDesign x:
                return HandleRefusal(x.Design, limits) ?? CommandVerdict.Ok;
            case RetireCohort r:
                return HandleRefusal(r.Design, limits) ?? CommandVerdict.Ok;
            case SellCohort v:
                return HandleRefusal(v.Design, limits) ?? CommandVerdict.Ok;
            case CloseLine l:
                return HandleRefusal(l.Design, limits) ?? CommandVerdict.Ok;
            case StartMasteryProgramme sp:
                return limits.ProcessNumbers.Contains(sp.ProcessNumber) ? CommandVerdict.Ok : CommandVerdict.Refuse(CommandRefusal.BadProcess, $"process {sp.ProcessNumber} is not in the layout");
            case CancelMasteryProgramme cp:
                return limits.ProcessNumbers.Contains(cp.ProcessNumber) ? CommandVerdict.Ok : CommandVerdict.Refuse(CommandRefusal.BadProcess, $"process {cp.ProcessNumber} is not in the layout");
            default:
                throw new ArgumentOutOfRangeException(nameof(logged), c.GetType(), "Unknown command type.");
        }
    }

    // Form only: a stale handle is accepted into the journal and rejected by the layer at application, identically in replay (ADR-0005 p. 3, note §3.3).
    private static CommandVerdict? HandleRefusal(DesignHandle h, CommandLimits limits)
    {
        if (h.Slot < 0 || h.Slot >= limits.Designs)
            return CommandVerdict.Refuse(CommandRefusal.BadDesign, $"design {h.Slot} is out of range");
        return h.Generation < 1 ? CommandVerdict.Refuse(CommandRefusal.BadHandle, $"design handle ({h.Slot}, {h.Generation}) has no generation ≥ 1") : null;
    }

    /// <summary>Checks that depend on the state at application: every process of the focus is known to the actor.</summary>
    public static CommandVerdict CheckState(SliceCommand command, Func<int, int, bool> actorKnowsProcess)
    {
        if (command is SetEffortFocus f)
        {
            foreach (int p in f.Processes)
            {
                if (!actorKnowsProcess(f.Actor, p))
                    return CommandVerdict.Refuse(CommandRefusal.FocusNotKnown, $"actor {f.Actor} does not know process {p}");
            }
        }
        return CommandVerdict.Ok;
    }
}
