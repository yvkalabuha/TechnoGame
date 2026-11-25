using System.Collections.Immutable;
using Singularity.Kernel;

namespace Singularity.Core.Institutes;

/// <summary>
/// Constants of the slot science model (SCIENCE_SLOTS.md, #561–#576, O-088..O-092; numbers from the Lab pilot TASK-0185). A run without these parameters is the
/// old shares model, byte for byte (S11). All dimensionless or per month; no calendar.
/// </summary>
public sealed record SlotParams
{
    private static Fx6 F(long raw) => Fx6.FromRaw(raw);

    /// <summary>Months per cost unit of the node time: <c>T_n = TimeBase · c_type · (1 + TimeGenSlope · gen)</c> (pilot T*: 6, 0.5).</summary>
    public Fx6 TimeBase { get; init; } = Fx6.FromInt(6);

    public Fx6 TimeGenSlope { get; init; } = F(500_000);

    /// <summary>Scale of the money price of a node in the slot model (#573: ×0.25).</summary>
    public Fx6 PriceScale { get; init; } = F(250_000);

    /// <summary>Most the total speed multiplier <c>M</c> reaches (#568).</summary>
    public Fx6 SpeedCap { get; init; } = F(2_500_000);

    /// <summary>Speed <c>s</c> of focus level 0, 1, 2 (O-088; placeholders until the owner's play session).</summary>
    public ImmutableArray<Fx6> FocusSpeed { get; init; } = [Fx6.One, F(1_250_000), F(1_500_000)];

    /// <summary>The price surcharge <c>s^0.6</c> of the focus levels (a table, so no power function is needed): 1, 1.25^0.6, 1.5^0.6.</summary>
    public ImmutableArray<Fx6> FocusMoney { get; init; } = [Fx6.One, F(1_143_263), F(1_275_425)];

    /// <summary>Queued nodes behind the active one (#567).</summary>
    public int QueueLength { get; init; } = 5;

    /// <summary>A focus level of an institute may be chosen again at most once in this many months.</summary>
    public int FocusRepeatMonths { get; init; } = 6;

    /// <summary>A hazard node at its progress cap leaves the slot; its chance keeps running at this share of the usual one (O-092).</summary>
    public Fx6 ReleasedHazardShare { get; init; } = F(250_000);

    /// <summary>Most directions an institute holds (#571).</summary>
    public int MaxDirections { get; init; } = 4;

    /// <summary>Weight of the main direction of a node in the overlap level and in the experience (O-092); the rest share <c>1 − MainWeight</c>.</summary>
    public Fx6 MainWeight { get; init; } = F(500_000);

    /// <summary>A start after which less than this share of the money the slots ask is there needs the player's confirmation (§1.2).</summary>
    public Fx6 UnderfundedBelow { get; init; } = F(600_000);

    /// <summary>Most the research_speed bonuses add to <c>R - 1</c> (#568; 0.25).</summary>
    public Fx6 ResearchSpeedCap { get; init; } = F(250_000);

    /// <summary>Upkeep of the built institutes may take at most this share of the research money (#574).</summary>
    public Fx6 UpkeepCeilingShare { get; init; } = F(250_000);

    /// <summary>The AI starts a project of its own accord only if the upkeep of all institutes after it stays within this share of the research money (§8).</summary>
    public Fx6 AiBuildUpkeepShare { get; init; } = F(150_000);

    /// <summary>AI focus: level 1 below this air-power ratio, level 2 below <see cref="AiFocusRatio2"/> (#565, SCIENCE_SLOTS §8).</summary>
    public Fx6 AiFocusRatio1 { get; init; } = Fx6.One;

    public Fx6 AiFocusRatio2 { get; init; } = F(700_000);

    /// <summary>AI focus needs this share of the research money left after the rates.</summary>
    public Fx6 AiFocusMargin { get; init; } = F(100_000);

    public void Validate()
    {
        if (TimeBase <= Fx6.Zero || TimeGenSlope < Fx6.Zero || PriceScale <= Fx6.Zero)
            throw new ArgumentException("Slot time base, slope and price scale must be positive.");
        if (SpeedCap < Fx6.One)
            throw new ArgumentException("The speed cap must be at least 1.");
        if (FocusSpeed.IsDefault || FocusMoney.IsDefault || FocusSpeed.Length != 3 || FocusMoney.Length != 3)
            throw new ArgumentException("Three focus levels (0, 1, 2) are needed.");
        if (QueueLength is < 0 or > 5 || MaxDirections is < 1 or > 4)
            throw new ArgumentException("Queue length 0..5 and 1..4 directions.");
    }
}
