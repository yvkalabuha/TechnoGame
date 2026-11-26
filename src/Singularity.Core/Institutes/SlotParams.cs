using System.Collections.Immutable;
using Singularity.Kernel;

namespace Singularity.Core.Institutes;

/// <summary>
/// Constants of the slot science model (SCIENCE_SLOTS.md, #561–#576, O-088..O-092; numbers from the Lab pilot TASK-0185). A run without these parameters is the
/// old shares model (S11: the `Legacy` configuration is byte for byte the build before). All dimensionless or per month; no calendar.
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

    /// <summary>One point of the era price curve: from this calendar year the node price is multiplied by <see cref="Mult"/> (linear between points, flat after the last).</summary>
    public readonly record struct EraPoint(int Year, Fx6 Mult);

    /// <summary>
    /// Era price curve D (#589, era-node-price.md §5): ×2 per ten years from 1990, ×16 by 2030. The factor of a node is read at <c>min(node year, calendar year)</c>, so
    /// a world before 1990 has factor 1 everywhere. Empty = no era price.
    /// </summary>
    public ImmutableArray<EraPoint> EraPrice { get; init; } =
        [new(1990, Fx6.One), new(2000, Fx6.FromInt(2)), new(2010, Fx6.FromInt(4)), new(2020, Fx6.FromInt(8)), new(2030, Fx6.FromInt(16))];

    /// <summary>The AI builds an institute on a surplus when the returned share of the research money, averaged over <see cref="SurplusWindowMonths"/>, is above this (#589: 20 %).</summary>
    public Fx6 SurplusBuildShare { get; init; } = F(200_000);

    /// <summary>Time constant of the averaging of the returned share, in months (an exponential window: one column, no history).</summary>
    public int SurplusWindowMonths { get; init; } = 12;

    /// <summary>The surplus rule fires at most once in this many months for an actor (N of #589; placeholder until the Lab measurement).</summary>
    public int SurplusBuildEveryMonths { get; init; } = 24;

    /// <summary>The surplus rule and its average run from this calendar year; before it the world is the one of the shares before #589. <see cref="int.MaxValue"/> = off.</summary>
    public int SurplusBuildFromYear { get; init; } = 1990;

    /// <summary>The era factor for each calendar year 1945..2100 (index = year − 1945); null without a curve. Integer interpolation, no floating point.</summary>
    public Fx6[]? EraFactors()
    {
        if (EraPrice.IsDefaultOrEmpty)
            return null;
        var table = new Fx6[EraLastYear - EraFirstYear + 1];
        for (int y = EraFirstYear; y <= EraLastYear; y++)
        {
            EraPoint first = EraPrice[0], last = EraPrice[^1];
            Fx6 m;
            if (y <= first.Year)
                m = Fx6.One;
            else if (y >= last.Year)
                m = last.Mult;
            else
            {
                int i = 1;
                while (EraPrice[i].Year < y)
                    i++;
                EraPoint lo = EraPrice[i - 1], hi = EraPrice[i];
                m = Fx6.FromRaw(lo.Mult.Raw + (hi.Mult.Raw - lo.Mult.Raw) * (y - lo.Year) / (hi.Year - lo.Year));
            }
            table[y - EraFirstYear] = m;
        }
        return table;
    }

    public const int EraFirstYear = 1945, EraLastYear = 2100;

    public void Validate()
    {
        if (!EraPrice.IsDefaultOrEmpty)
        {
            if (EraPrice[0].Mult != Fx6.One)
                throw new ArgumentException("The era price curve starts at ×1.");
            for (int i = 1; i < EraPrice.Length; i++)
            {
                if (EraPrice[i].Year <= EraPrice[i - 1].Year || EraPrice[i].Mult < EraPrice[i - 1].Mult)
                    throw new ArgumentException("The era price curve needs rising years and a multiplier that does not fall.");
            }
        }
        if (SurplusBuildShare < Fx6.Zero || SurplusWindowMonths < 1 || SurplusBuildEveryMonths < 1)
            throw new ArgumentException("Surplus build share >= 0, window and period >= 1 month.");
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
