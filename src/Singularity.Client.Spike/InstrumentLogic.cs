using System;

namespace Singularity.Client.Spike;

/// <summary>Godot-free rules of the instrument layers (UI_INSTRUMENTS §3, decisions #404 and #409).</summary>
public static class InstrumentLogic
{
    /// <summary>Instrument-controls level: 0 off («before» look), 1 weak (ink only), 2 normal (the three inks). Set from the settings.</summary>
    public static int OrganLevel = 2;

    /// <summary>Screenshot runs pin the game year seen by the panel instruments (the speed gauge has its own override in the signal field).</summary>
    public static int? YearOverride;

    /// <summary>Era step of an instrument by game year: 1 = 1945–69 (and earlier), 2 = 1970–99, 3 = 2000–2100.</summary>
    public static int Era(int year) => year < 1970 ? 1 : year < 2000 ? 2 : 3;

    /// <summary>Needle angle in degrees from vertical for a speed step 0..3 (E1 dial: the step ticks, 30 degrees apart).</summary>
    public static readonly double[] NeedleDeg = { -60, -30, 0, 30 };

    /// <summary>Caliper step in percent points (UI_INSTRUMENTS §3).</summary>
    public const int CaliperStep = 5;

    /// <summary>Nearest multiple of the caliper step, clamped to 0..100.</summary>
    public static int CaliperSnap(double percent) => Math.Clamp((int)Math.Round(percent / CaliperStep, MidpointRounding.AwayFromZero) * CaliperStep, 0, 100);

    /// <summary>One arrow-key step: off the grid (the other shares are rounded shares) the jaw goes to the next grid line in that direction; on it, by one step.</summary>
    public static int CaliperArrow(int value, int dir)
    {
        int r = value % CaliperStep;
        int v = r == 0 ? value + dir * CaliperStep : dir > 0 ? value - r + CaliperStep : value - r;
        return Math.Clamp(v, 0, 100);
    }

    /// <summary>Fill of a flask 0..1 from have and need; no need (0 or less) reads as full.</summary>
    public static double FlaskLevel(double have, double need) => need <= 0 ? 1 : Math.Clamp(have / need, 0, 1);

    /// <summary>Alloy stock as a share of what the power needs: supply against supply plus deficit; nothing needed and nothing in stock reads as full.</summary>
    public static double AlloyLevel(double supply, double deficit) => FlaskLevel(supply, supply + Math.Max(deficit, 0));

    /// <summary>Era-1 dial (DEBT-0059): radius, tick length and tick width in du; larger and heavier than the first cut so that the dial reads at 100 % UI scale.</summary>
    public const float DialRadius = 33, DialTickLength = 11, DialTickWidth = 2.5f;
}
