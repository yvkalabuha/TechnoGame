using System.Linq;
using Singularity.Client.Spike;
using Xunit;

namespace Singularity.Client.Audio.Tests;

public class ContrastValidatorTests
{
    static double De(double l1, double a1, double b1, double l2, double a2, double b2) =>
        ColorMath.DeltaE2000(new ColorMath.Lab(l1, a1, b1), new ColorMath.Lab(l2, a2, b2));

    // Sharma, Wu, Dalal (2005), supplementary table: pairs 1, 2, 3, 7, 11, 17.
    [Theory]
    [InlineData(50.0000, 2.6772, -79.7751, 50.0000, 0.0000, -82.7485, 2.0425)]
    [InlineData(50.0000, 3.1571, -77.2803, 50.0000, 0.0000, -82.7485, 2.8615)]
    [InlineData(50.0000, 2.8361, -74.0200, 50.0000, 0.0000, -82.7485, 3.4412)]
    [InlineData(50.0000, 2.5000, 0.0000, 50.0000, 0.0000, -2.5000, 4.3065)]
    [InlineData(50.0000, 2.5000, 0.0000, 73.0000, 25.0000, -18.0000, 27.1492)]
    [InlineData(60.2574, -34.0099, 36.2677, 60.4626, -34.1751, 39.4387, 1.2644)]
    public void Delta_e_2000_matches_the_Sharma_pairs(double l1, double a1, double b1, double l2, double a2, double b2, double expected) =>
        Assert.Equal(expected, De(l1, a1, b1, l2, a2, b2), 3);

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
