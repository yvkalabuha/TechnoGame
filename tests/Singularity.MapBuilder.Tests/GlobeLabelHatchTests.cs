using Singularity.Client.Spike;
using Xunit;

namespace Singularity.MapBuilder.Tests;

// TASK-0142: pure label placement and hatch choice of the globe map.
public class GlobeLabelHatchTests
{
    const float Vw = 1920, Vh = 1080;

    [Fact]
    public void Big_region_with_anchor_off_screen_keeps_its_label_inside_the_frame()
    {
        // "French Algeria": the anchor is 40 px above the frame, the visible part of the region is hundreds of px deep.
        Assert.False(LabelPlacement.Place(700, -40, 150, 20, Vw, Vh, out _, out _));   // the old rule hid it
        Assert.True(LabelPlacement.PlaceInRegion(700, -40, 150, 20, Vw, Vh, 300, 960, 540, 900, out float x, out float y));
        Assert.Equal(LabelPlacement.Margin, y);
        Assert.Equal(700 - 75, x);
    }

    [Fact]
    public void Label_is_hidden_when_the_visible_part_of_the_region_is_too_small_or_off_the_globe()
    {
        Assert.False(LabelPlacement.PlaceInRegion(700, -400, 150, 20, Vw, Vh, 300, 960, 540, 900, out _, out _));   // the region barely touches the frame
        Assert.False(LabelPlacement.PlaceInRegion(20, 540, 150, 20, Vw, Vh, 300, 960, 540, 600, out _, out _));      // the clamped plate would lie in space
        Assert.False(LabelPlacement.PlaceInRegion(960, 540, 3000, 20, Vw, Vh, 900, 960, 540, 900, out _, out _));    // the plate is wider than the frame
    }

    [Fact]
    public void Plate_mode_follows_the_screen_area()
    {
        Assert.Equal(LabelPlacement.PlateMode.Callout, LabelPlacement.ModeFor(63f));
        Assert.Equal(LabelPlacement.PlateMode.Framed, LabelPlacement.ModeFor(64f));
        Assert.Equal(LabelPlacement.PlateMode.Framed, LabelPlacement.ModeFor(399f));
        Assert.Equal(LabelPlacement.PlateMode.Plain, LabelPlacement.ModeFor(400f));
        float near = LabelPlacement.ScreenArea(500, 1000, 1f), far = LabelPlacement.ScreenArea(500, 400, 1f);
        Assert.True(near > far * 6);                                                    // zooming in grows the area quadratically
        Assert.Equal(0f, LabelPlacement.ScreenArea(500, 1000, -0.2f));                  // behind the limb
    }

    [Fact]
    public void Callout_plate_hangs_up_right_and_flips_left_at_the_edge()
    {
        LabelPlacement.CalloutCentre(500, 500, 100, 20, Vw, out float cx, out float cy);
        Assert.True(cx > 500 + 50 && cy < 500 - 10);
        LabelPlacement.CalloutCentre(1890, 500, 100, 20, Vw, out cx, out _);
        Assert.True(cx < 1890 - 50);
    }

    [Fact]
    public void Hatch_is_chosen_by_controller_before_status()
    {
        Assert.Equal(HatchKind.Occupied, HatchLogic.Choose("DEU", "USA", false));
        Assert.Equal(HatchKind.Occupied, HatchLogic.Choose("FRA", "DEU", true));   // an occupied colony reads as occupied
        Assert.Equal(HatchKind.Colony, HatchLogic.Choose("GBR", "GBR", true));
        Assert.Equal(HatchKind.None, HatchLogic.Choose("FRA", "FRA", false));
        Assert.Equal(HatchKind.None, HatchLogic.Choose("", "", false));
    }

    [Fact]
    public void Occupation_hatch_is_wider_and_lighter_than_colony_hatch()
    {
        var c = HatchLogic.Params(HatchKind.Colony); var o = HatchLogic.Params(HatchKind.Occupied);
        Assert.True(o.Period >= c.Period + 3 && o.Alpha < c.Alpha);
        Assert.True(HatchLogic.LineScale(HatchKind.Occupied) > HatchLogic.LineScale(HatchKind.Colony));
        var dark = HatchLogic.Line(9, HatchLogic.LineScale(HatchKind.Colony)); var light = HatchLogic.Line(9, HatchLogic.LineScale(HatchKind.Occupied));
        Assert.True(light.Item1 > dark.Item1 && light.Item2 > dark.Item2);
    }
}
