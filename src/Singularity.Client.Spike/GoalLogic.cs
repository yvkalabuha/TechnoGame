using System.Globalization;

namespace Singularity.Client.Spike;

/// <summary>Godot-free text rules of the air power goal indicator (#565): number plus step in words (never colour alone), the estimate as a range, the end-of-game line.</summary>
public static class GoalLogic
{
    /// <summary>Locale key of a step word: 0 none, 1 parity, 2 superiority, 3 dominance.</summary>
    public static string StageKey(int stage) => "goal.stage." + Math.Clamp(stage, 0, 3);

    /// <summary>Glyph that repeats the step beside the word (a second channel): none ○, parity ◐, superiority ●, dominance ◆.</summary>
    public static string StageMark(int stage) => Math.Clamp(stage, 0, 3) switch { 0 => "○", 1 => "◐", 2 => "●", _ => "◆" };

    /// <summary>The ratio as shown: exact, a range when it is an estimate, "?" when the intelligence gives none. Ratios of 99 and above are shown as "99+".</summary>
    public static string RatioText(double ratio, bool estimated, bool known, double low, double high, string decimalSep)
    {
        if (estimated && !known)
            return "?";
        if (estimated)
            return Num(low, decimalSep) + "–" + Num(high, decimalSep);
        return Num(ratio, decimalSep);
    }

    static string Num(double v, string sep) => v >= 99 ? "99+" : (Math.Floor(v * 100 + 0.5) / 100).ToString("0.00", CultureInfo.InvariantCulture).Replace(".", sep);

    /// <summary>The next line to reach (1.0, 1.5, 2.0) or 0 above the top step.</summary>
    public static double NextLine(int stage) => stage switch { 0 => 1.0, 1 => 1.5, 2 => 2.0, _ => 0 };

    /// <summary>Years and months of hold of the step, for the tooltip: "2 y 3 m" pieces; the hold needed is 60 months.</summary>
    public static (int Years, int Months) Split(int months) => (months / 12, months % 12);

    /// <summary>Share of the hold done, 0…1, shown as a bar next to the words.</summary>
    public static double HoldShare(int heldMonths, int requiredMonths) => requiredMonths <= 0 ? 0 : Math.Clamp((double)heldMonths / requiredMonths, 0, 1);

    /// <summary>Locale key of the end-of-game line from the highest step held for the full time (0 = the goal was missed; the defeat stays soft: no early end).</summary>
    public static string EndKey(int achieved) => "goal.end." + Math.Clamp(achieved, 0, 3);

    /// <summary>The month a change of step happened is announced for this many months in the header.</summary>
    public const int NoticeMonths = 3;

    /// <summary>The notice key of a change of step in the last <see cref="NoticeMonths"/> months, or null: reached (up) or lost (down).</summary>
    public static string? ChangeKey(int previous, int stage, long changeMonth, long month)
    {
        if (changeMonth < 0 || month - changeMonth >= NoticeMonths || previous == stage)
            return null;
        return stage > previous ? "goal.reached" : "goal.lost";
    }
}
