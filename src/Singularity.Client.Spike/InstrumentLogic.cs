using System;
using System.Collections.Generic;
using Singularity.Core.WhyNot;

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

    /// <summary>One research flask: a Knowledge link of the why_not chain as Have against Need; Threshold marks a hazard node at its threshold.</summary>
    public readonly record struct ResearchFlask(int Node, double Have, double Need, bool Threshold);

    /// <summary>The flasks of the Science panel from the why_not links: Knowledge links only (product and capability blockers stay as text), in link order, at most <paramref name="max"/>.
    /// One function for the live panel and the screenshot run.</summary>
    public static List<ResearchFlask> ResearchFlasks(IReadOnlyList<Link> links, int max)
    {
        var r = new List<ResearchFlask>();
        foreach (var l in links)
        {
            if (r.Count >= max) break;
            if (l.Layer != WhyNotLayer.Knowledge) continue;
            double have = l.Have.ToDouble(), need = l.Need.ToDouble();
            r.Add(new ResearchFlask(l.SubjectId, have, need, ThresholdReached(have, need)));
        }
        return r;
    }

    /// <summary>Focus ring of a focusable button (A7): outer tone and inner tone, hex. Each background the ring meets has one tone at 3:1 or better against it.</summary>
    public const string FocusOuter = Palette.Ink;
    public static string FocusInner => Palette.C.Paper;

    /// <summary>A research link still listed with Have at or above Need: a hazard node past the threshold where its discovery becomes possible; the discovery is a draw, not certain.</summary>
    public static bool ThresholdReached(double have, double need) => need > 0 && have >= need;

    /// <summary>Alloy stock as a share of the normal supply. The view gives the supply in kg already cut by the disruption (cap·(1 − deficit)·yield) and the deficit as a fraction 0..1 (the cut itself), never in kg:
    /// the two are not added. The level is what is left of the normal supply, 1 − deficit.</summary>
    public static double AlloyLevel(double deficitFraction) => Math.Clamp(1 - deficitFraction, 0, 1);

    /// <summary>Which case a Production flask shows: Disrupted is the supply below normal (demand not measured), Shortage is the supply against a measured demand (the AlloyDeficit link), NoSmelting is no admitted melt.</summary>
    public enum AlloyState { Normal, Disrupted, Shortage, NoSmelting }

    public readonly record struct AlloyFlaskView(double Level, bool Hatched, AlloyState State);

    /// <summary>The Production flask. A why_not alloy link with Have below Need is a shortage (real supply against the planned demand); with Have at or above Need it is no shortage and the other rules apply.
    /// No admitted melt (supply 0) is an empty hatched flask; otherwise the stock is what the policy disruption leaves, hatched while the disruption lasts.</summary>
    public static AlloyFlaskView AlloyFlask(double supply, double deficitFraction, (double Have, double Need)? shortage)
    {
        if (shortage is { } s && s.Have < s.Need) return new(FlaskLevel(s.Have, s.Need), true, AlloyState.Shortage);
        if (supply <= 0) return new(0, true, AlloyState.NoSmelting);
        return deficitFraction > 0 ? new(AlloyLevel(deficitFraction), true, AlloyState.Disrupted) : new(AlloyLevel(deficitFraction), false, AlloyState.Normal);
    }

    /// <summary>Percent shown for a flask level, in the caption and in the tooltip alike: rounded, and a hatched (deficit) flask never reads 100 %.</summary>
    public static int FlaskPercent(double level, bool hatched)
    {
        int pct = (int)Math.Round(level * 100, MidpointRounding.AwayFromZero);
        return hatched ? Math.Min(pct, 99) : pct;
    }

    /// <summary>Tooltip key of the Production flask per case; each states only what was measured.</summary>
    public static string AlloyTipKey(AlloyState state) => state switch
    {
        AlloyState.Shortage => "flask.alloy.deficit",
        AlloyState.Disrupted => "flask.alloy.disrupted",
        AlloyState.NoSmelting => "flask.alloy.none",
        _ => "flask.alloy.tip",
    };

    /// <summary>Visible caption key of the Production flask per case; it names what the number is measured against (the denominators differ).</summary>
    public static string AlloyCaptionKey(AlloyState state) => state switch
    {
        AlloyState.Shortage => "flask.alloy.cap.deficit",
        AlloyState.Disrupted => "flask.alloy.cap.disrupted",
        AlloyState.NoSmelting => "flask.alloy.cap.none",
        _ => "flask.alloy.cap.ok",
    };

    /// <summary>Layer-2 instruments of the budget block show at organ levels 1 and 2 and only in an area panel (never in the read-only panel of a foreign power).</summary>
    public static bool OrganShown(int level, bool areaMode) => level > 0 && areaMode;

    /// <summary>The caliper row: the instrument layer is on and the panel is the player's own.</summary>
    public static bool CaliperShown(int level, bool areaMode, bool foreign) => OrganShown(level, areaMode) && !foreign;

    /// <summary>Clips a 45-degree hatching line x + y = c (t = c - centre) to a band y0..y1 whose half-width is a0 + slope * y; returns the y range on the line, empty when lo >= hi.</summary>
    public static (float Lo, float Hi) HatchSpan(float y0, float y1, float a0, float slope, float t) =>
        (Math.Max(y0, (t - a0) / (1 + slope)), Math.Min(y1, (t + a0) / (1 - slope)));
}
