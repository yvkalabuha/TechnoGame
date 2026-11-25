using Singularity.Client.Spike;
using Xunit;

namespace Singularity.Client.Audio.Tests;

public class TreeLabelRulesTests
{
    static readonly LabelBox Area = new(0, 44, 1000, 900);
    static readonly LabelBox ZoomBar = new(0, 880, 300, 100);

    [Fact]
    public void ADecadePlateCutByTheWindowEdgeIsNotDrawn()
    {
        Assert.True(TreeLabelRules.PlateFits(new LabelBox(400, 300, 40, 18), Area, ZoomBar));
        Assert.False(TreeLabelRules.PlateFits(new LabelBox(-10, 300, 40, 18), Area, ZoomBar));      // half off the left edge ("980")
        Assert.False(TreeLabelRules.PlateFits(new LabelBox(400, 38, 40, 18), Area, ZoomBar));       // cut at the top ("1980" axis)
        Assert.False(TreeLabelRules.PlateFits(new LabelBox(980, 300, 40, 18), Area, ZoomBar));      // cut at the right
    }

    [Fact]
    public void ADecadePlateUnderTheZoomPanelIsNotDrawn() =>
        Assert.False(TreeLabelRules.PlateFits(new LabelBox(150, 890, 40, 18), Area, ZoomBar));

    [Fact]
    public void ANodeNameNeedsItsIconInsideTheWindow()
    {
        Assert.True(TreeLabelRules.IconVisible(500, 500, 14, Area));
        Assert.False(TreeLabelRules.IconVisible(-5, 500, 14, Area));     // icon beyond the left edge: no orphan name
        Assert.False(TreeLabelRules.IconVisible(500, 50, 14, Area));     // icon half under the top edge
        Assert.False(TreeLabelRules.IconVisible(995, 500, 14, Area));
    }

    [Fact]
    public void ASectorLabelAtTheRightEdgeIsShiftedInWholeOrHidden()
    {
        Assert.True(TreeLabelRules.ClampInto(Area, 4, new LabelBox(900, 400, 150, 34), 90, out var r));
        Assert.True(Area.Encloses(r)); Assert.Equal(1000 - 4 - 150, r.X); Assert.Equal(150, r.W);
        Assert.False(TreeLabelRules.ClampInto(Area, 4, new LabelBox(1200, 400, 150, 34), 90, out _));   // would stand far from its sector
        Assert.False(TreeLabelRules.ClampInto(new LabelBox(0, 0, 100, 100), 4, new LabelBox(0, 0, 150, 20), 500, out _));   // wider than the window
        Assert.True(TreeLabelRules.ClampInto(Area, 4, new LabelBox(300, 400, 150, 34), 90, out var same)); Assert.Equal(new LabelBox(300, 400, 150, 34), same);
    }

    [Fact]
    public void TheNowPlateHasAFourDuFieldOnEverySide()
    {
        var (w, h) = TreeLabelRules.NowPlateSize(100, 16);
        Assert.True(w >= 108 && h >= 24);
    }

    [Fact]
    public void SecondaryInkMeetsFourAndAHalfToOneOnPaperAndSheet()
    {
        var ink2 = ColorMath.FromHex(Palette.Ink2);
        var p = Palette.C;
        Assert.True(ColorMath.Contrast(ink2, ColorMath.FromHex(p.Paper)) >= 4.5);
        Assert.True(ColorMath.Contrast(ink2, ColorMath.FromHex(p.Sheet)) >= 4.5);
    }
}
