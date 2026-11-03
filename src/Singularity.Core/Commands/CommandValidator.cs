using System.Collections.Immutable;

namespace Singularity.Core.Commands;

/// <summary>Why a command was refused (at Submit or at application); the text is for the sender, the code is stable.</summary>
public enum CommandRefusal
{
    None = 0,
    UnknownActor,
    BadSource,
    ForceNotAllowed,
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
}

public readonly record struct CommandVerdict(CommandRefusal Reason, string Message)
{
    public bool Accepted => Reason == CommandRefusal.None;

    public static CommandVerdict Ok => new(CommandRefusal.None, "accepted");

    public static CommandVerdict Refuse(CommandRefusal reason, string message) => new(reason, message);
}

/// <summary>Sizes a command is checked against, from the layouts of the world (no state).</summary>
public sealed record CommandLimits(int ActorCount, int SlotCount, int Designs, ImmutableArray<int> ProcessNumbers)
{
    /// <summary><c>compose</c>/<c>derive</c> commands one actor can have in force in one month (SLICE_COMPOSITION §7.3, [Предп.] О-5).</summary>
    public const int DesignSlots = 4;
}

/// <summary>
/// Validation in two steps (SLICE_COMPOSITION §7.4). <see cref="CheckForm"/> runs at Submit and needs no state: the actor exists, the
/// source may send the command (<c>force</c> never from <see cref="SourceClass.Ai"/>), focus without repeats, shares summing to 1
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
        if (c.Actor < 0 || c.Actor >= limits.ActorCount)
            return CommandVerdict.Refuse(CommandRefusal.UnknownActor, $"actor {c.Actor} does not exist");
        switch (c)
        {
            case SetBudgetShares s:
                return s.Shares.IsValid
                    ? CommandVerdict.Ok
                    : CommandVerdict.Refuse(CommandRefusal.BadShares, $"shares must be non-negative and sum to exactly 1, got {s.Shares.Sum}");
            case SetEffortFocus f:
                if (key.Source == SourceClass.Ai)
                    return CommandVerdict.Refuse(CommandRefusal.BadSource, "the AI has no effort focus");
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
                if (v.Parent < 0 || v.Parent >= limits.Designs)
                    return CommandVerdict.Refuse(CommandRefusal.BadDesign, $"design {v.Parent} is out of range");
                return v.Alts.IsDefault || v.Alts.Length != limits.SlotCount || v.Alts.Any(a => a < -1)
                    ? CommandVerdict.Refuse(CommandRefusal.BadAlternatives, $"derive needs {limits.SlotCount} alternatives (−1 = unchanged)")
                    : CommandVerdict.Ok;
            case StartSeries s:
                if (s.Design < 0 || s.Design >= limits.Designs)
                    return CommandVerdict.Refuse(CommandRefusal.BadDesign, $"design {s.Design} is out of range");
                if (s.Rate.Raw < 0)
                    return CommandVerdict.Refuse(CommandRefusal.BadRate, "the rate must not be negative");
                return s.Force && key.Source == SourceClass.Ai
                    ? CommandVerdict.Refuse(CommandRefusal.ForceNotAllowed, "force = true is never sent by the AI (#94)")
                    : CommandVerdict.Ok;
            case CloseLine l:
                return l.Design < 0 || l.Design >= limits.Designs
                    ? CommandVerdict.Refuse(CommandRefusal.BadDesign, $"design {l.Design} is out of range")
                    : CommandVerdict.Ok;
            default:
                throw new ArgumentOutOfRangeException(nameof(logged), c.GetType(), "Unknown command type.");
        }
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
