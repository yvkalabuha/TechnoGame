using System.Linq;
using Singularity.Client.Spike;
using Xunit;

namespace Singularity.Client.Audio.Tests;

public class ContrastValidatorTests
{
    static double De(double l1, double a1, double b1, double l2, double a2, double b2) =>
        ColorMath.DeltaE2000(new ColorMath.Lab(l1, a1, b1), new ColorMath.Lab(l2, a2, b2));

    // Sharma, Wu, Dalal (2005), supplementary table: all 34 pairs, numbered as in the paper (InlineData order = pair number).
    [Theory]
    [InlineData(50.0, 2.6772, -79.7751, 50.0, 0.0, -82.7485, 2.0425)]
    [InlineData(50.0, 3.1571, -77.2803, 50.0, 0.0, -82.7485, 2.8615)]
    [InlineData(50.0, 2.8361, -74.0200, 50.0, 0.0, -82.7485, 3.4412)]
    [InlineData(50.0, -1.3802, -84.2814, 50.0, 0.0, -82.7485, 1.0000)]
    [InlineData(50.0, -1.1848, -84.8006, 50.0, 0.0, -82.7485, 1.0000)]
    [InlineData(50.0, -0.9009, -85.5211, 50.0, 0.0, -82.7485, 1.0000)]
    [InlineData(50.0, 0.0, 0.0, 50.0, -1.0, 2.0, 2.3669)]
    [InlineData(50.0, -1.0, 2.0, 50.0, 0.0, 0.0, 2.3669)]
    [InlineData(50.0, 2.49, -0.001, 50.0, -2.49, 0.0009, 7.1792)]
    [InlineData(50.0, 2.49, -0.001, 50.0, -2.49, 0.0010, 7.1792)]
    [InlineData(50.0, 2.49, -0.001, 50.0, -2.49, 0.0011, 7.2195)]
    [InlineData(50.0, 2.49, -0.001, 50.0, -2.49, 0.0012, 7.2195)]
    [InlineData(50.0, -0.001, 2.49, 50.0, 0.0009, -2.49, 4.8045)]
    [InlineData(50.0, -0.001, 2.49, 50.0, 0.0010, -2.49, 4.8045)]
    [InlineData(50.0, -0.001, 2.49, 50.0, 0.0011, -2.49, 4.7461)]
    [InlineData(50.0, 2.5, 0.0, 50.0, 0.0, -2.5, 4.3065)]
    [InlineData(50.0, 2.5, 0.0, 73.0, 25.0, -18.0, 27.1492)]
    [InlineData(50.0, 2.5, 0.0, 61.0, -5.0, 29.0, 22.8977)]
    [InlineData(50.0, 2.5, 0.0, 56.0, -27.0, -3.0, 31.9030)]
    [InlineData(50.0, 2.5, 0.0, 58.0, 24.0, 15.0, 19.4535)]
    [InlineData(50.0, 2.5, 0.0, 50.0, 3.1736, 0.5854, 1.0000)]
    [InlineData(50.0, 2.5, 0.0, 50.0, 3.2972, 0.0, 1.0000)]
    [InlineData(50.0, 2.5, 0.0, 50.0, 1.8634, 0.5757, 1.0000)]
    [InlineData(50.0, 2.5, 0.0, 50.0, 3.2592, 0.3350, 1.0000)]
    [InlineData(60.2574, -34.0099, 36.2677, 60.4626, -34.1751, 39.4387, 1.2644)]
    [InlineData(63.0109, -31.0961, -5.8663, 62.8187, -29.7946, -4.0864, 1.2630)]
    [InlineData(61.2901, 3.7196, -5.3901, 61.4292, 2.2480, -4.9620, 1.8731)]
    [InlineData(35.0831, -44.1164, 3.7933, 35.0232, -40.0716, 1.5901, 1.8645)]
    [InlineData(22.7233, 20.0904, -46.6940, 23.0331, 14.9730, -42.5619, 2.0373)]
    [InlineData(36.4612, 47.8580, 18.3852, 36.2715, 50.5065, 21.2231, 1.4146)]
    [InlineData(90.8027, -2.0831, 1.4410, 91.1528, -1.6435, 0.0447, 1.4441)]
    [InlineData(90.9257, -0.5406, -0.9208, 88.6381, -0.8985, -0.7239, 1.5381)]
    [InlineData(6.7747, -0.2908, -2.4247, 5.8714, -0.0985, -2.2286, 0.6377)]
    [InlineData(2.0776, 0.0795, -1.1350, 0.9033, -0.0636, -0.5514, 0.9082)]
    public void Delta_e_2000_matches_the_Sharma_pairs(double l1, double a1, double b1, double l2, double a2, double b2, double expected) =>
        Assert.Equal(expected, De(l1, a1, b1, l2, a2, b2), 4);

    [Fact]
    public void Black_and_white_are_100_apart_in_lightness_and_the_same_colour_is_zero()
    {
        var black = ColorMath.FromHex("000000"); var white = ColorMath.FromHex("FFFFFF");
        Assert.Equal(0, ColorMath.Lstar(black), 6);
        Assert.Equal(100, ColorMath.Lstar(white), 3);
        Assert.Equal(100, ColorMath.DeltaE2000(black, white), 0);
        Assert.Equal(0, ColorMath.DeltaE2000(white, white), 9);
    }

    [Fact]
    public void Validator_is_deterministic()
    {
        string a = ContrastValidator.Format(Palette.C, ContrastValidator.Run(Palette.C));
        string b = ContrastValidator.Format(Palette.C, ContrastValidator.Run(Palette.C));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Alert_plates_on_the_v02_palette_pass_on_the_interface_and_on_the_tints()
    {
        // v0.2 has a pale sea and no-man's land close to the sheet, so the halo there is weak (warn on sea, sea.deep, land-none): those are reported, not asserted.
        var bad = ContrastValidator.Run(Palette.V02).Where(r => !r.Pass && r.Name.Contains(" on ") && !r.Name.Contains("sea") && !r.Name.Contains("land-none")).ToList();
        Assert.True(bad.Count == 0, string.Join("\n", bad.Select(r => r.Name + ": " + r.Detail)));
    }

    [Fact]
    public void A_yellow_plate_directly_on_a_yellow_tint_fails()
    {
        // The deliberately bad pair: a yellow plate on the yellow paper-side backgrounds, with no halo to rescue it.
        var p = Palette.V02 with { Warn = "F2DE9E" };
        Assert.False(ContrastValidator.Run(p).Single(r => r.Name == "warn on paper").Pass);
    }

    [Fact]
    public void Palette_c_passes_every_pair_with_the_calibrated_thresholds()
    {
        var res = ContrastValidator.Run(Palette.C);
        var bad = res.Where(r => !r.Pass).ToList();
        Assert.True(bad.Count == 0, string.Join("\n", bad.Select(r => r.Name + ": " + r.Detail)));
        Assert.Equal(107, res.Count);
    }

    [Fact]
    public void Calibrated_limits_are_the_ones_of_spec_7()
    {
        Assert.Equal(12, ContrastValidator.WarnLimits.DeltaL);
        Assert.Equal(0.17, ContrastValidator.AlarmLimits.Chroma);
        Assert.Equal(0.15, ContrastValidator.CommandLimits.Chroma);
        Assert.Equal(0.145, ContrastValidator.WarnLimits.Chroma);
    }

    [Fact]
    public void A_halo_that_merges_with_the_background_is_not_a_failure_when_direct_contact_passes()
    {
        // warn on land-none: the sheet halo is close to the background (halo-bg under 8) but direct contact holds.
        var r = ContrastValidator.Run(Palette.C).Single(x => x.Name == "warn on land-none (shaded)");
        Assert.True(r.Pass, r.Detail);
    }

    [Fact]
    public void Palette_c_tint3_is_the_calibrated_one_and_keeps_decor_chroma()
    {
        Assert.Equal("BBDE86", Palette.C.Tints[2]);
        Assert.True(ColorMath.OkChroma(ColorMath.FromHex(Palette.C.Tints[2])) <= ContrastValidator.DecorChroma);
    }

    [Fact]
    public void Palette_c_tokens_are_the_documented_ones()
    {
        Assert.Equal("D9860F", Palette.C.Warn);
        Assert.Equal(8, Palette.C.Tints.Length);
        Assert.Equal(6, Palette.C.Line.Length);
        Assert.Equal("F8D56F", Palette.C.Tints[4]);
    }
}

public class ContrastValidatorReport
{
    readonly Xunit.Abstractions.ITestOutputHelper _out;
    public ContrastValidatorReport(Xunit.Abstractions.ITestOutputHelper o) => _out = o;

    [Fact]
    public void Prints_the_report_for_both_palettes()
    {
        foreach (var p in new[] { Palette.V02, Palette.C }) _out.WriteLine(ContrastValidator.Format(p, ContrastValidator.Run(p)));
    }
}
