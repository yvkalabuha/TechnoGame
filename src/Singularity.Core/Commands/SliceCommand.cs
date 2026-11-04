using System.Collections.Immutable;
using Singularity.Core.Policy;
using Singularity.Kernel;

namespace Singularity.Core.Commands;

/// <summary>
/// Class of a command source (ADR-0014 п. 2): the fixed order at equal <c>apply_tick</c>, the player first. Part of the journal
/// schema: a change of the numbers or of the order is a change of the format (SLICE_COMPOSITION О-2).
/// </summary>
public enum SourceClass
{
    Player = 0,

    /// <summary>A Lab harness (the FORCE proxy, §6.8): an external source, not the AI.</summary>
    Harness = 1,

    /// <summary>Decisions of the AI; they are <c>pol.*</c> columns, never journal entries: Submit refuses this class. It keeps the key order complete.</summary>
    Ai = 2,
}

/// <summary>
/// Order key of a command (ADR-0014 п. 2): <c>(apply_tick, source_class, source_id, local_n)</c>. <c>local_n</c> is the number
/// of the command at its source, set by the sender and kept in the command itself.
/// </summary>
public readonly record struct CommandKey(long ApplyTick, SourceClass Source, int SourceId, int LocalN) : IComparable<CommandKey>
{
    public int CompareTo(CommandKey other)
    {
        int c = ApplyTick.CompareTo(other.ApplyTick);
        if (c != 0)
            return c;
        c = ((int)Source).CompareTo((int)other.Source);
        if (c != 0)
            return c;
        c = SourceId.CompareTo(other.SourceId);
        return c != 0 ? c : LocalN.CompareTo(other.LocalN);
    }

    public static bool operator <(CommandKey a, CommandKey b) => a.CompareTo(b) < 0;
    public static bool operator >(CommandKey a, CommandKey b) => a.CompareTo(b) > 0;
    public static bool operator <=(CommandKey a, CommandKey b) => a.CompareTo(b) <= 0;
    public static bool operator >=(CommandKey a, CommandKey b) => a.CompareTo(b) >= 0;
}

public enum CommandKind
{
    SetBudgetShares = 0,
    SetEffortFocus = 1,
    ComposeDesign = 2,
    DeriveDesign = 3,
    StartSeries = 4,
    CloseLine = 5,
    CancelDesign = 6,
    RetireCohort = 7,
    SellCohort = 8,
}

/// <summary>
/// A typed external command of the slice (ADR-0014 п. 3; contract §5). Arguments are integers and <see cref="Fx6"/> only.
/// Only commands with a consumer in the current build exist; AI decisions are not commands (they are <c>pol.*</c> columns).
/// </summary>
public abstract record SliceCommand(int Actor)
{
    public abstract CommandKind Kind { get; }
}

/// <summary><c>set_budget_shares</c>: R / capability / D / procurement / QP; sums to exactly 1 (validated at Submit).</summary>
public sealed record SetBudgetShares(int Actor, BudgetShares Shares) : SliceCommand(Actor)
{
    public override CommandKind Kind => CommandKind.SetBudgetShares;
}

/// <summary>
/// <c>set_effort_focus</c>: up to three process numbers in priority order, no repeats (contract §3.1). Player only; the AI has no focus.
/// An empty list clears the focus.
/// </summary>
public sealed record SetEffortFocus(int Actor, ImmutableArray<int> Processes) : SliceCommand(Actor)
{
    public override CommandKind Kind => CommandKind.SetEffortFocus;
}

/// <summary><c>compose_design(g*, alts[5])</c>: one layout-global alternative index per slot.</summary>
public sealed record ComposeDesign(int Actor, int Generation, ImmutableArray<int> Alts) : SliceCommand(Actor)
{
    public override CommandKind Kind => CommandKind.ComposeDesign;
}

/// <summary><c>derive(parent, alts[5])</c>: −1 = unchanged slot.</summary>
public sealed record DeriveDesign(int Actor, int Parent, ImmutableArray<int> Alts) : SliceCommand(Actor)
{
    public override CommandKind Kind => CommandKind.DeriveDesign;
}

/// <summary>
/// <c>start_series(design, rate, force)</c>. <c>force = true</c> is allowed for the player and a harness only (#94, A36): the AI
/// never sends commands at all (<see cref="SourceClass.Ai"/> is refused at Submit).
/// </summary>
public sealed record StartSeries(int Actor, int Design, Fx6 Rate, bool Force) : SliceCommand(Actor)
{
    public override CommandKind Kind => CommandKind.StartSeries;
}

/// <summary><c>close_line(design)</c>: stage С → В.</summary>
public sealed record CloseLine(int Actor, int Design) : SliceCommand(Actor)
{
    public override CommandKind Kind => CommandKind.CloseLine;
}

/// <summary>
/// <c>cancel_design(design)</c> (contract §4.4e, F14): an own project in П or О becomes «снят». The player may send it any time; the AI only by the
/// rule A40 (a policy decision in <c>pol.*</c>, not a journal command).
/// </summary>
public sealed record CancelDesign(int Actor, int Design) : SliceCommand(Actor)
{
    public override CommandKind Kind => CommandKind.CancelDesign;
}

/// <summary>
/// <c>retire_cohort(design, n)</c> (contract r6 §4.4f, #141): <c>n</c> units of a project in С or В leave the fleet without money, the oldest first.
/// The player and the AI send the same command (the AI's are <c>pol.*</c> columns). Checks that need the state are the layer's (<c>BAD_RETIRE</c>).
/// </summary>
public sealed record RetireCohort(int Actor, int Design, int N) : SliceCommand(Actor)
{
    public override CommandKind Kind => CommandKind.RetireCohort;
}

/// <summary>
/// <c>sell_cohort(design, n)</c> (contract r6 §4.4f, #142): <c>n</c> units of a project in В go to buyers outside the slice for money, the oldest first.
/// Checks that need the state are the layer's (<c>BAD_SELL</c>).
/// </summary>
public sealed record SellCohort(int Actor, int Design, int N) : SliceCommand(Actor)
{
    public override CommandKind Kind => CommandKind.SellCohort;
}

/// <summary>An accepted command with its key: one line of <c>commands.log</c>.</summary>
public readonly record struct LoggedCommand(CommandKey Key, SliceCommand Command);
