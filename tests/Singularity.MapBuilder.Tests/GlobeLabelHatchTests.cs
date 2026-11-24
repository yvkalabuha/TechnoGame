using Singularity.Client.Spike;
using Xunit;
using Xunit.Abstractions;

namespace Singularity.MapBuilder.Tests;

// TASK-0142: pure label placement and hatch choice of the globe map.
public class GlobeLabelHatchTests
{
    readonly ITestOutputHelper _out;
    public GlobeLabelHatchTests(ITestOutputHelper o) => _out = o;

    const float Vw = 1920, Vh = 1080;

    [Fact]
    public void Big_region_with_anchor_off_screen_keeps_its_label_inside_the_frame()
    {
        // "French Algeria": the anchor is 40 px above the frame, the visible part of the region is hundreds of px deep.
        Assert.False(LabelPlacement.Place(700, -40, 150, 20, Vw, Vh, out _, out _));   // the old rule hid it
        Assert.True(LabelPlacement.PlaceInRegion(700, -40, 150, 20, Vw, Vh, 300, 960, 540, 900, null, out float x, out float y));
        Assert.Equal(LabelPlacement.Margin, y);
        Assert.Equal(700 - 75, x);
    }

    [Fact]
    public void Long_strip_with_a_far_anchor_keeps_its_label_where_the_region_is_visible()
    {
        // "Sweden" on the Europe frame: the anchor is far above the frame, the radius disc of the area is too small, but the top edge of the frame is still Sweden.
        static bool Sweden(float x, float y) => x > 870 && x < 1010 && y >= 150 && y < 280;   // the strip starts 150 px below the top edge
        Assert.False(LabelPlacement.PlaceInRegion(940, -300, 80, 20, Vw, Vh, 120, 960, 540, 1190, null, out _, out _));        // the radius rule alone hides it
        Assert.True(LabelPlacement.PlaceInRegion(940, -300, 80, 20, Vw, Vh, 120, 960, 540, 1190, Sweden, out float x, out float y));
        Assert.True(y >= 150 - 10 - 10 && y < 280, "the plate settles on the visible strip");
        Assert.Equal(940 - 40, x);
        Assert.False(LabelPlacement.PlaceInRegion(300, -300, 80, 20, Vw, Vh, 120, 960, 540, 1190, Sweden, out _, out _));       // the clamped point is not on the region
    }

    [Fact]
    public void Label_is_hidden_when_the_visible_part_of_the_region_is_too_small_or_off_the_globe()
    {
        Assert.False(LabelPlacement.PlaceInRegion(700, -400, 150, 20, Vw, Vh, 300, 960, 540, 900, null, out _, out _));   // the region barely touches the frame
        Assert.False(LabelPlacement.PlaceInRegion(20, 540, 150, 20, Vw, Vh, 300, 960, 540, 600, null, out _, out _));      // the clamped plate would lie in space
        Assert.False(LabelPlacement.PlaceInRegion(960, 540, 3000, 20, Vw, Vh, 900, 960, 540, 900, null, out _, out _));    // the plate is wider than the frame
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

    [Fact]
    public void Islet_specks_carry_no_coast_stroke_but_real_land_does()
    {
        // 8x4 raster: a 2x2 islet (4 texels), a 4x2 island (8 texels) wrapping over the dateline, a 3x2 block (6) apart; minTexels 6.
        var ids = new ushort[8 * 4];
        void Set(int x, int y) => ids[y * 8 + x] = 1;
        Set(3, 0); Set(4, 0);                                   // islet of 2 texels
        Set(7, 2); Set(0, 2); Set(7, 3); Set(0, 3); Set(1, 3);  // 5 texels joined over the dateline
        Set(2, 2); Set(3, 3);                                   // diagonal neighbours: 8-connected, 7 texels in all
        var keep = CoastMask.Build(ids, 8, 4, 6);
        Assert.False(keep[0 * 8 + 3]);
        Assert.True(keep[2 * 8 + 7] && keep[3 * 8 + 1] && keep[3 * 8 + 3]);
        Assert.Equal(0, System.Array.IndexOf(CoastMask.Build(new ushort[8], 8, 1), true) + 1);   // sea: nothing kept
    }

    // A1/A3 of the colony labels: ink on the plate (sheet at 85 % over the lit fill), worst tone at light 0.70; and tint.9 against the other tones at light 0.70 and 1.05.
    [Fact]
    public void Colony_label_contrast_and_tint9_delta_e_are_measured()
    {
        static double Srgb(double l) => l <= 0.0031308 ? l * 12.92 : 1.055 * System.Math.Pow(l, 1 / 2.4) - 0.055;
        static double Lin(double c) => c <= 0.04045 ? c / 12.92 : System.Math.Pow((c + 0.055) / 1.055, 2.4);
        var ink = ColorMath.FromHex(Palette.Ink); var sheet = ColorMath.FromHex(Palette.C.Sheet);
        double worst = 1e9; int worstTone = -1;
        for (int t = 0; t < CountryPalette.Hex.Length; t++)
        {
            var f = ColorMath.FromHex(CountryPalette.Hex[t]);
            double Mix(double s, double fl) => Lin(0.85 * Srgb(s) + 0.15 * Srgb(fl * 0.70));
            var plate = new ColorMath.Lin(Mix(sheet.R, f.R), Mix(sheet.G, f.G), Mix(sheet.B, f.B));
            double c = ColorMath.Contrast(ink, plate);
            if (c < worst) { worst = c; worstTone = t; }
        }
        _out.WriteLine($"colony label ink on plate, worst of 12 tones at light 0.70: {worst:F2}:1 (tone {worstTone + 1}); A3 needs 4.5");
        Assert.True(worst >= 4.5);
        foreach (double k in new[] { 0.70, 1.05 })
        {
            var t9 = ColorMath.Scale(ColorMath.FromHex(CountryPalette.Hex[8]), k, k, k);
            string line = "";
            double min = 1e9;
            for (int o = 0; o < CountryPalette.Hex.Length; o++)
            {
                if (o == 8) continue;
                var c = ColorMath.Scale(ColorMath.FromHex(CountryPalette.Hex[o]), k, k, k);
                double de = ColorMath.DeltaE2000(t9, c);
                line += $" {o + 1}:{de:F1}";
                if (!CountryPalette.IsForbidden(8, o) && de < min) min = de;
            }
            _out.WriteLine($"tint.9 deltaE2000 at light {k:F2}:{line}; min over allowed neighbours {min:F1}");
        }
    }
}
