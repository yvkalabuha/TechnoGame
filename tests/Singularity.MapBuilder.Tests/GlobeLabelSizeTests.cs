using Singularity.Client.Spike;
using Xunit;

namespace Singularity.MapBuilder.Tests;

public class GlobeLabelSizeTests
{
    [Fact] public void Size_runs_from_14_to_28_by_extent()
    {
        Assert.Equal(14, LabelPlacement.FontSizeFor(500f));
        Assert.Equal(28, LabelPlacement.FontSizeFor(1_000_000f));
        int a = LabelPlacement.FontSizeFor(30_000f), b = LabelPlacement.FontSizeFor(120_000f);
        Assert.True(a < b); Assert.InRange(a, 14, 28);
    }

    [Fact] public void Fallback_ladder_ends_at_the_minimum()
    {
        var seen = new System.Collections.Generic.List<int>();
        for (int s = 28; s > 0; s = LabelPlacement.NextSize(s)) seen.Add(s);
        Assert.Equal(new[] { 28, 21, 17, 14, 13, 12 }, seen.ToArray());
    }

    [Fact] public void Mode_and_pre_test()
    {
        Assert.Equal(LabelPlacement.PlateMode.Callout, LabelPlacement.ModeFor(399f));
        Assert.Equal(LabelPlacement.PlateMode.Plain, LabelPlacement.ModeFor(400f));
        Assert.True(LabelPlacement.MayFit(10_000f, 100f, 16f));
        Assert.False(LabelPlacement.MayFit(1_000f, 100f, 16f));
    }

    [Fact] public void Hysteresis_shows_at_full_size_and_hides_at_85_percent()
    {
        Assert.Equal(1.0f, LabelPlacement.FitScale(false));
        Assert.Equal(0.85f, LabelPlacement.FitScale(true));
        System.Func<float, float, bool> land = (x, y) => x >= 0 && x <= 100 && y >= 0 && y <= 40;
        // a 110 wide box fits only at 85 % (93.5): a shown name stays, a hidden one does not appear
        Assert.True(LabelPlacement.FitsInside(50, 20, 110 * LabelPlacement.FitScale(true), 16, land));
        Assert.False(LabelPlacement.FitsInside(50, 20, 110 * LabelPlacement.FitScale(false), 16, land));
    }

    [Fact] public void Spot_search_keeps_off_the_limb_and_pads_the_coast()
    {
        System.Func<float, float, bool> land = (x, y) => y >= 0 && y <= 1080 && x >= 0 && x <= 1920;
        // globe centre (960, 540), limb radius 500: a spot at the anchor on the rim is moved in
        Assert.True(LabelPlacement.FindSpot(1440, 540, 100, 16, 1f, 0f, 960, 540, 500f, 1920, 1080, 12, land, out float cx, out _));
        Assert.True(LabelPlacement.InsideDisc(cx, 540, 50, 8, 960, 540, 500f));
        Assert.False(LabelPlacement.InsideDisc(1440, 540, 50, 8, 960, 540, 500f));
        // a 20 tall band takes a 16 tall word without padding, not with a 6 px coast pad
        System.Func<float, float, bool> band = (x, y) => y >= 300 && y <= 320 && x >= 100 && x <= 700;
        Assert.True(LabelPlacement.FindSpot(400, 310, 120, 16, 1f, 0f, 960, 540, 1e9f, 1920, 1080, 12, band, out _, out _));
        Assert.False(LabelPlacement.FindSpot(400, 310, 120, 16, 1f, 6f, 960, 540, 1e9f, 1920, 1080, 12, band, out _, out _));
    }

    [Fact] public void Fit_needs_every_probe_on_the_land()
    {
        System.Func<float, float, bool> land = (x, y) => x >= 0 && x <= 100 && y >= 0 && y <= 40;
        Assert.True(LabelPlacement.FitsInside(50, 20, 80, 16, land));
        Assert.False(LabelPlacement.FitsInside(50, 20, 120, 16, land));
        Assert.False(LabelPlacement.FitsInside(50, 36, 80, 16, land));
    }

    [Fact] public void Spot_search_finds_a_thin_diagonal_strip_the_anchor_misses_and_keeps_the_margin()
    {
        // a horizontal band 20 tall at y 300..320; the anchor lies 60 above it
        System.Func<float, float, bool> band = (x, y) => y >= 300 && y <= 320 && x >= 100 && x <= 700;
        Assert.True(LabelPlacement.FindSpot(400, 240, 120, 16, 1f, 0f, 960, 540, 1e9f, 1920, 1080, 12, band, out float cx, out float cy));
        Assert.InRange(cy, 308f, 312f);
        Assert.False(LabelPlacement.FindSpot(400, 240, 120, 30, 1f, 0f, 960, 540, 1e9f, 1920, 1080, 12, band, out _, out _));   // the band is too thin for a taller word
        // a region that touches the window edge: the box keeps 8 px from it
        System.Func<float, float, bool> edge = (x, y) => x >= 0 && x <= 500 && y >= 0 && y <= 500;
        Assert.True(LabelPlacement.FindSpot(2, 250, 100, 16, 1f, 0f, 960, 540, 1e9f, 1920, 1080, 12, edge, out float ex, out _));
        Assert.True(ex - 50f >= LabelPlacement.Margin - 0.01f);
    }
}
