#nullable enable
using System;

namespace Singularity.Client.Spike;

public enum AreaId { Map, Science, Production, Finance, Forces, Intel, Politics, Projects }

/// <summary>How much of an area works: Live = real data and orders, Part = only some of it, Locked = no mechanic yet (no numbers, no controls).</summary>
public enum AreaState { Live, Part, Locked }

/// <summary>One navigation area (CLIENT_V05C_SPEC §4, decisions #352, #360). Ink: index into the six inks, -1 neutral, 6 the seventh colour (hatched, #360).</summary>
public sealed record AreaDef(AreaId Id, string Slug, char Letter, int Ink, AreaState State, string Milestone, int Group);

/// <summary>A rectangle in du, relative to the top-left corner of a rail button (no Godot types: the metrics are tested without the engine).</summary>
public readonly record struct Box(double X, double Y, double W, double H)
{
    public double R => X + W;
    public double B => Y + H;
    public bool Intersects(Box o) => X < o.R && o.X < R && Y < o.B && o.Y < B;
    /// <summary>Smallest distance from this box to the edge of a w x h button (negative when it sticks out).</summary>
    public double EdgeGap(double w, double h) => Math.Min(Math.Min(X, w - R), Math.Min(Y, h - B));
}

/// <summary>Where the marks of one rail button sit. Ink boxes: Letter and Lock are the drawn ink, not the padding of the glyph box.</summary>
public readonly record struct ButtonRects(double W, double H, Box Icon, Box Letter, double LetterBase, Box? Badge, Box? LockInk, Box LockDraw, double NameX, double NameW, bool Caption);

/// <summary>Geometry of the rail (TASK-0117, docs/ui/specs/task-0117-rail-and-legend.md) and the floors of its marks (UI_DESIGN_SYSTEM A1, A2), in du; 1 du = 1 px at 1920x1080 and 100 %.
/// The one source: the painting code and the T6 glyph bounds of the button read the same boxes.</summary>
public static class RailMetrics
{
    public const int BadgeFontDu = 14, CaptionFontDu = 12, LetterFontDu = 14;
    public const int BadgeW = 34, BadgeH = 20;
    // Sign and count inside the badge (offsets from the badge corner).
    public const int BadgeSignX = 4, BadgeSignY = 6, BadgeSignSize = 8, BadgeTextX = 14, BadgeTextBase = 15, BadgeTextW = 18;
    /// <summary>lock-24.svg: the body spans x 5..19 of a 24-unit box (stroke 1.5), the rest is transparent padding.</summary>
    public const double LockBodyUnits = 14, LockBoxUnits = 24;
    public const int LockDrawDu = 28;
    /// <summary>Ink of the whole lock (shackle top y 3, body bottom y 21, x 5..19, stroke 1.5 grows it by 0.75), in 24-unit box coordinates.</summary>
    const double LockInkX0 = 4.25, LockInkX1 = 19.75, LockInkY0 = 2.25, LockInkY1 = 21.75;
    public static double LockVisibleWidthPx(double drawDu) => drawDu * LockBodyUnits / LockBoxUnits;

    /// <summary>The spec widens the wide rail by the measured shortfall of the longest locked name ("Projects and people", 134 du at 14 du, against 101 in the spec): the spec says to widen the rail, not to cut the name. 134 + 1 du of slack - 101 = 34.</summary>
    public const int WideGrow = 34;
    public const int NarrowWidth = 68, WideWidth = 224 + WideGrow;
    public const int NarrowButtonW = 60, WideButtonW = 212 + WideGrow, NarrowButtonH = 58, WideButtonH = 46, WideLockedButtonH = 52;
    // The spec has 44 du with a two-line name of 34 du and 5 du margins; the real line height of the 14 du text is 18, so two lines are 36 and the button is 46 (measured in the shot run).
    const double SpecWideButtonH = 44;
    /// <summary>Side margin of the rail panel: the button is centred in the rail.</summary>
    public static int SideMargin(bool wide) => ((wide ? WideWidth : NarrowWidth) - (wide ? WideButtonW : NarrowButtonW)) / 2;
    public static int ButtonW(bool wide) => wide ? WideButtonW : NarrowButtonW;
    public static int ButtonH(bool wide, bool locked) => wide ? (locked ? WideLockedButtonH : WideButtonH) : NarrowButtonH;

    /// <summary>Advance of a mono glyph in em (PT Mono).</summary>
    public const double MonoAdvanceEm = 0.6;
    /// <summary>Cap height of the 14 du hotkey letter in du (PT Mono caps are about 0.7 em).</summary>
    public const double LetterCapDu = 10;
    public static double LetterWidth(int chars) => chars * MonoAdvanceEm * LetterFontDu;

    /// <summary>Width of the name column of a wide button: 93 beside the plate, 101 in a locked button (no plate).</summary>
    public const int WideNameX = 44, WideNameW = 93 + WideGrow, WideLockedNameW = 101 + WideGrow;
    /// <summary>Top of a text block of height textH centred in a button of height h.</summary>
    public static double CentreTop(double h, double textH) => (h - textH) / 2.0;

    /// <summary>The boxes of a button. badge: a plate sits on it (never with locked); letterChars: 1, or 3 for "Esc".</summary>
    public static ButtonRects Of(bool wide, bool locked, bool badge, int letterChars)
    {
        double w = ButtonW(wide), h = ButtonH(wide, locked), lw = LetterWidth(letterChars);
        double lockDu = LockDrawDu, k = lockDu / LockBoxUnits;
        if (!wide)
        {
            bool left = badge || locked;
            double lx = left ? 6 : (w - lw) / 2.0;
            // The lock box sits so that its ink is 1 du under the icon box (y 29) and then 5+ du over the bottom edge.
            var lockBox = new Box((w - lockDu) / 2.0, 27.5, lockDu, lockDu);
            return new ButtonRects(w, h, new Box(18, 5, 24, 24), new Box(lx, 46 - LetterCapDu, lw, LetterCapDu), 46,
                badge ? new Box(21, 32, BadgeW, BadgeH) : null, locked ? InkOf(lockBox, k) : null, lockBox, 0, 0, false);
        }
        double dy = locked ? 0 : (WideButtonH - SpecWideButtonH) / 2.0;   // the spec rows are centred in 44 du; keep them centred
        double iy = locked ? 14 : 10 + dy, bl = locked ? 31 : 27 + dy;
        var lockW = new Box(147 + WideGrow, 12, lockDu, lockDu);
        return new ButtonRects(w, h, new Box(10, iy, 24, 24), new Box(207 + WideGrow - lw, bl - LetterCapDu, lw, LetterCapDu), bl,
            badge ? new Box(143 + WideGrow, 12 + dy, BadgeW, BadgeH) : null, locked ? InkOf(lockW, k) : null, lockW, WideNameX, locked ? WideLockedNameW : WideNameW, locked);
    }

    static Box InkOf(Box b, double k) => new(b.X + LockInkX0 * k, b.Y + LockInkY0 * k, (LockInkX1 - LockInkX0) * k, (LockInkY1 - LockInkY0) * k);
}

public static class AreaData
{
    public const int Neutral = -1, Seventh = 6;

    // Sand 0, teal 1, violet 2, olive 3, plum 4, slate 5. Only Science, Production and Finance have real data in the client today:
    // Forces stays locked (no force data in the Session API yet; the budget shares are the budget, not forces): no fake mechanics.
    public static readonly AreaDef[] All =
    {
        new(AreaId.Map, "map", '\0', Neutral, AreaState.Live, "", 0),
        new(AreaId.Science, "science", 'T', 2, AreaState.Live, "", 1),
        new(AreaId.Production, "production", 'E', 1, AreaState.Part, "M1", 1),
        new(AreaId.Finance, "finance", 'F', 0, AreaState.Part, "M1", 1),
        new(AreaId.Forces, "forces", 'M', 5, AreaState.Locked, "M2–M3", 2),
        new(AreaId.Intel, "intel", 'I', 4, AreaState.Locked, "M2–M3", 2),
        new(AreaId.Politics, "politics", 'G', 3, AreaState.Locked, "M1–M3", 3),
        new(AreaId.Projects, "projects", 'J', Seventh, AreaState.Locked, "M1", 3),
    };

    public static AreaDef Of(AreaId id) => All[(int)id];

    /// <summary>The area opened by a hotkey letter (upper case), or null.</summary>
    public static AreaDef? ByLetter(char c)
    {
        foreach (var a in All) if (a.Letter == c && c != '\0') return a;
        return null;
    }

    /// <summary>Area a signal belongs to and its severity (2 alarm, 1 warn); null for signals that belong to no area (system fault: journal only).</summary>
    public static (AreaId Area, int Severity)? OfSignal(string key) => key switch
    {
        "sig.alloy" => (AreaId.Production, 1),
        "sig.licence" => (AreaId.Finance, 1),
        "sig.refusal" => (AreaId.Finance, 2),
        _ => null,
    };

    /// <summary>The seventh ink: a moss green, always drawn with 45-degree hatching as a second channel (#360). Line, wash, text step.</summary>
    public const string SeventhLine = "3A8A5C", SeventhWash = "C6E6D2", SeventhText = "2A5E3F";
}
