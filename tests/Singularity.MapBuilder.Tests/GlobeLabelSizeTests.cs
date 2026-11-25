using Singularity.Client.Spike;
using Xunit;

namespace Singularity.MapBuilder.Tests;

public class GlobeLabelSizeTests
{
    [Fact] public void Size_is_clamped_to_min_and_max()
    {
        Assert.Equal(LabelPlacement.MinFont, LabelPlacement.FontSizeFor(500f, 120f));
        Assert.Equal(LabelPlacement.MaxFont, LabelPlacement.FontSizeFor(1_000_000f, 60f));
    }

    [Fact] public void Size_grows_with_the_country_and_shrinks_with_the_word()
    {
        float a = LabelPlacement.FontSizeFor(30_000f, 100f), b = LabelPlacement.FontSizeFor(60_000f, 100f), c = LabelPlacement.FontSizeFor(30_000f, 150f);
        Assert.True(b >= a); Assert.True(c <= a);
        Assert.InRange(a, LabelPlacement.MinFont, LabelPlacement.MaxFont);
    }

    [Fact] public void Mode_and_pre_test()
    {
        Assert.Equal(LabelPlacement.PlateMode.Callout, LabelPlacement.ModeFor(399f));
        Assert.Equal(LabelPlacement.PlateMode.Plain, LabelPlacement.ModeFor(400f));
        Assert.True(LabelPlacement.MayFit(10_000f, 100f, 16f));
        Assert.False(LabelPlacement.MayFit(1_000f, 100f, 16f));
    }

    [Fact] public void Fit_needs_every_probe_on_the_land()
    {
        System.Func<float, float, bool> land = (x, y) => x >= 0 && x <= 100 && y >= 0 && y <= 40;
        Assert.True(LabelPlacement.FitsInside(50, 20, 80, 16, land));
        Assert.False(LabelPlacement.FitsInside(50, 20, 120, 16, land));   // sticks out left and right
        Assert.False(LabelPlacement.FitsInside(50, 36, 80, 16, land));    // sticks out at the bottom
    }
}
