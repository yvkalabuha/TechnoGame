using Singularity.Client.Spike;
using Xunit;

namespace Singularity.Client.Audio.Tests;

/// <summary>A2 sweep (TASK-0117 round 9): a meaningful icon is at least 16 x 16 physical px at 1920x1080 and 100 %. Ink boxes are measured, not nominal radii.</summary>
public class IconFloorTests
{
    static readonly string[] Types = { "FND", "ENG", "CMP", "PRC", "APP", "PRN" };

    [Theory, InlineData("FND"), InlineData("ENG"), InlineData("CMP"), InlineData("PRC"), InlineData("APP"), InlineData("PRN")]
    public void LegendSwatch_BothSidesReachTheFloor(string code)
    {
        float r = GlyphGeometry.LegendRadius(code);
        var (x0, y0, x1, y1) = GlyphGeometry.Bounds(code, r);
        Assert.True(x1 - x0 >= GlyphGeometry.IconFloor, $"{code} width {x1 - x0}");
        Assert.True(y1 - y0 >= GlyphGeometry.IconFloor, $"{code} height {y1 - y0}");
        Assert.True(r <= 9, $"{code} radius {r} must fit the 21 du legend row");
    }

    [Fact]
    public void LegendRadius7_WasBelowTheFloor()
    {
        var (_, y0, _, y1) = GlyphGeometry.Bounds("FND", 7);
        Assert.True(y1 - y0 < GlyphGeometry.IconFloor);   // 13.1 du, the Codex finding
    }

    [Fact]
    public void LegendSwatches_FitTheRow()
    {
        foreach (var t in Types)
        {
            var (x0, y0, x1, y1) = GlyphGeometry.Bounds(t, GlyphGeometry.LegendRadius(t));
            Assert.True(y1 - y0 <= 19, t);   // 21 du row pitch, at least 2 du between swatches
            Assert.True(x1 - x0 <= 20, t);   // the column has 16 du between the number and the code
        }
    }

    // What is drawn, measured: ink box incl. stroke >= 16 x 16 du and every stroke >= 2 du for the glyphs that carry meaning (UI_DESIGN_SYSTEM A2).
    static float Plate => GlyphGeometry.IconsPlateMin;   // the smallest plate of the Icons level (TechTree.NR starts here)

    [Theory, InlineData("FND"), InlineData("PRN"), InlineData("ENG"), InlineData("CMP"), InlineData("PRC"), InlineData("APP")]
    public void NodeGlyph_InkAndStroke(string code)
    {
        var g = GlyphPrims.NodeGlyph(code, Plate * GlyphGeometry.NodeGlyphShare);
        var (x0, y0, x1, y1) = GlyphPrims.Ink(g);
        Assert.True(x1 - x0 >= GlyphPrims.IconFloor, $"{code} ink width {x1 - x0}");
        Assert.True(y1 - y0 >= GlyphPrims.IconFloor, $"{code} ink height {y1 - y0}");
        float s = GlyphPrims.MinStroke(g);
        Assert.True(s == 0 || s >= GlyphPrims.ArrowStroke, $"{code} stroke {s}");
    }

    [Theory, InlineData("FND"), InlineData("PRN"), InlineData("ENG"), InlineData("CMP"), InlineData("PRC"), InlineData("APP")]
    public void LegendSwatch_InkAndRow(string code)
    {
        var (x0, y0, x1, y1) = GlyphPrims.Ink(GlyphPrims.LegendSwatch(code));
        Assert.True(x1 - x0 >= GlyphPrims.IconFloor && y1 - y0 >= GlyphPrims.IconFloor, $"{code} {x1 - x0} x {y1 - y0}");
        Assert.True(y1 - y0 <= 20 && x1 - x0 <= 21, code);   // 21 du row pitch; the column is 16 du wide between number and code
    }

    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void TimeGlyph_StrokeIsAtLeast2(int kind) => Assert.True(GlyphPrims.MinStroke(TimeGlyphMetrics.Prims(kind)) >= GlyphPrims.ArrowStroke);

    [Fact]
    public void MenuGlyph_InkAndStroke()
    {
        var m = GlyphPrims.MenuGlyph();
        var (x0, y0, x1, y1) = GlyphPrims.Ink(m);
        Assert.True(x1 - x0 >= GlyphPrims.IconFloor && y1 - y0 >= GlyphPrims.IconFloor, $"{x1 - x0} x {y1 - y0}");
        Assert.True(GlyphPrims.MinStroke(m) >= GlyphPrims.ArrowStroke);
        Assert.True(y1 - y0 <= 30 - 10 && x1 - x0 <= 34 - 10);   // inside the 34 x 30 button with 5 du around
    }

    [Fact]
    public void NodePlate_ShapeBoxesReachTheFloorInTheSmallestIconsPlate()
    {
        foreach (var t in Types)
        {
            var (x0, y0, x1, y1) = GlyphGeometry.Bounds(t, GlyphGeometry.IconsPlateMin);
            Assert.True(x1 - x0 >= GlyphGeometry.IconFloor && y1 - y0 >= GlyphGeometry.IconFloor, t);
        }
    }

    [Theory, InlineData(false, false), InlineData(true, false), InlineData(false, true), InlineData(true, true)]
    public void RailMarks_ReachTheFloor(bool wide, bool locked)
    {
        var r = RailMetrics.Of(wide, locked, !locked, 1);
        Assert.True(r.Icon.W >= 16 && r.Icon.H >= 16);
        if (r.Badge is { } b) Assert.True(b.W >= 16 && b.H >= 16);   // the plate is the icon; the sign inside is its second channel
        if (r.LockInk is { } l) Assert.True(l.W >= 16 && l.H >= 16);
    }

    [Fact]
    public void WhyNotSquare_IsCentredOnItsLegendRow()
    {
        // the geometry TechTree draws: swatch centre at row y - 5 (it stood 6 du too high)
        var (_, y, side) = SettingsLogic.WhyNotLegendMark(0, 100);
        Assert.Equal(95, y + side / 2, 6);
        Assert.True(side >= GlyphGeometry.IconFloor);
    }
}
