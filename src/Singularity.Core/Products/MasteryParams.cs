using Singularity.Kernel;

namespace Singularity.Core.Products;

/// <summary>Which parts of the production-mastery rules run (TASK-0134, #439, #440). The arms B0-B3 of the probe are subsets.</summary>
[Flags]
public enum MasteryRules
{
    None = 0,

    /// <summary>The series of a tier ≥ 5 design starts only with <c>y_min ≥ y_start</c>.</summary>
    Gate = 1,

    /// <summary>The candidate line below <c>y_prod</c> for <c>T_Ab</c> months closes.</summary>
    CandidateClose = 2,

    /// <summary>Static foresight with hysteresis in <c>Requirement</c>.</summary>
    Foresight = 4,

    /// <summary>The mastery programme (commands and the AI).</summary>
    Programme = 8,

    All = Gate | CandidateClose | Foresight | Programme,
}

/// <summary>
/// Parameters of the production-mastery rules (docs/game-design/notes/production-mastery.md rev 3, O-045). The rules touch processes of
/// tier ≥ <see cref="MinTier"/> and act from month <see cref="FromMonth"/> only, so the run of 1945-1977 is the run without them.
/// </summary>
/// <summary>One programme command in force at a month: start (with the mode) or cancel of the programme on a process.</summary>
public readonly record struct ProgrammeCommand(int ProcessNumber, bool Start, bool Abroad);

/// <summary>Why a programme cannot start (<c>why_not</c> codes of the command, production-mastery.md §1).</summary>
public enum MasteryRefusal
{
    None = 0,
    UnknownProcess,

    /// <summary>The process is below tier 5 (G1-G4 are not touched).</summary>
    TierLtMin,

    /// <summary>Before 1978-01, or the programme rule is switched off.</summary>
    OutOfScope,
    AlreadyActive,

    /// <summary>The limiter is not <c>LabCeiling</c>.</summary>
    LimiterNotLabCeiling,

    /// <summary><c>Hcap ≤ r_d</c>: the programme waits for the ceiling and spends nothing. Accepted by a command (queue), never started by the AI.</summary>
    SaturationWait,
    Tooling,

    /// <summary>Another process leads the cell; <see cref="ProgrammeVerdict.Detail"/> is its number.</summary>
    CellLedBy,

    /// <summary>The ETA is above <c>T_look</c> (or the rate is zero); <see cref="ProgrammeVerdict.Detail"/> is the ETA in months.</summary>
    EtaGtTLook,
    SlotsFull,

    /// <summary>Abroad asked, no other country has mastered the process.</summary>
    NoDonor,
}

/// <summary>Verdict of the start check; a command is accepted for <c>None</c> and <c>SaturationWait</c>.</summary>
public readonly record struct ProgrammeVerdict(MasteryRefusal Reason, int Detail = 0)
{
    public bool Accepted => Reason is MasteryRefusal.None or MasteryRefusal.SaturationWait;
}

public sealed record MasteryParams
{
    /// <summary>Indexes (into <c>ProcNumber</c>) of the processes the programme may run on: tier ≥ <see cref="MinTier"/>, ascending. One column per entry.</summary>
    public static int[] ProgrammeProcesses(ProductsLayout l)
    {
        var rows = new List<int>();
        for (int k = 0; k < l.ProcNumber.Length; k++)
        {
            if (l.ProcTier[k] >= MinTier)
                rows.Add(k);
        }
        return [.. rows];
    }

    /// <summary>First tier the rules and the programme apply to (G5 and G6 processes).</summary>
    public const int MinTier = 5;

    public MasteryRules Rules { get; init; } = MasteryRules.All;

    /// <summary>First month index the rules act in; the assembly sets 1978-01. <see cref="int.MaxValue"/> = never (the default of a bare record).</summary>
    public int FromMonth { get; init; } = int.MaxValue;

    /// <summary><c>T_look</c> = <c>T_max</c>, months (O-045): foresight horizon and the longest programme (≈ ETA of process 73 of the USA, 7-12 years).</summary>
    public int TLook { get; init; } = 144;

    /// <summary><c>S_max</c>: programmes of one actor at a time (the number of Наладка focuses).</summary>
    public int SMax { get; init; } = 3;

    /// <summary><c>x_prog</c>: share of the nominal volume the programme feeds into the culture (O-045: 1).</summary>
    public Fx6 XProg { get; init; } = Fx6.One;

    public bool On(long month, MasteryRules rule) => (Rules & rule) == rule && month >= FromMonth;

    public void Validate()
    {
        if (TLook < 1 || SMax < 1 || XProg <= Fx6.Zero || XProg > Fx6.One)
            throw new ArgumentException("T_look and S_max must be positive, x_prog in (0; 1].");
    }
}
