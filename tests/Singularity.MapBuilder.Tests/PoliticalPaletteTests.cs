using System;
using System.Collections.Generic;
using System.Linq;
using Singularity.Client.Spike;
using Xunit;

namespace Singularity.MapBuilder.Tests;

// docs/ui/specs/political-map-palette.md: atlas traditions for the fixed powers, colony = metropole tone + 18 % white. No map data needed.
public class PoliticalPaletteTests
{
    static int ToneOf(string code) => CountryPalette.Fixed.First(f => f.Code == code).Tone;

    [Fact] public void Fixed_table_follows_the_traditions()
    {
        foreach (var c in new[] { "CAN", "AUS", "NZL", "ZAF" }) Assert.Equal(ToneOf("GBR"), ToneOf(c));   // the empire is pink, dominions without hatch
        Assert.Equal(ToneOf("JPN"), ToneOf("NLD"));
        Assert.Equal(ToneOf("ROC"), ToneOf("ESP"));
        Assert.Equal(11, ToneOf("DEU"));
        Assert.Equal(10, ToneOf("BEL"));
        Assert.Equal(0, CountryPalette.Fixed[0].Tone); Assert.Equal("SUN", CountryPalette.Fixed[2].Code);   // HatchLogic reads these by index
        Assert.Equal(CountryPalette.Fixed.Length, CountryPalette.Fixed.Select(f => f.Code).Distinct().Count());
    }

    [Fact] public void Fixed_neighbours_are_never_a_forbidden_pair()
    {
        // real 1945 land borders between fixed powers (the map graph itself is checked on the data in WorldMapTests)
        foreach (var (a, b) in new[] { ("FRA", "BEL"), ("DEU", "NLD"), ("ESP", "PRT"), ("ITA", "FRA"), ("SUN", "ROC"), ("FRA", "ESP"), ("DEU", "FRA"), ("SWE", "SUN") })
            Assert.False(CountryPalette.IsForbidden(ToneOf(a), ToneOf(b)), a + "-" + b);
    }

    [Fact] public void Assign_puts_every_fixed_code_on_its_tone_and_is_deterministic()
    {
        var codes = CountryPalette.Fixed.Select(f => f.Code).Concat(new[] { "ARG", "BRA" }).OrderBy(c => c, StringComparer.Ordinal).ToList();
        var size = codes.Select(_ => 3).ToList();
        var nb = codes.Select(_ => new List<int>()).ToList();
        var a = CountryPalette.Assign(codes, size, nb); var b = CountryPalette.Assign(codes, size, nb);
        Assert.Equal(a, b);
        foreach (var (code, tone) in CountryPalette.Fixed) Assert.Equal(tone, a[codes.IndexOf(code)]);
        Assert.All(a, t => Assert.InRange(t, 0, 11));
    }

    [Fact] public void Colony_fill_is_the_metropole_tone_lightened_and_stays_below_L91()
    {
        for (int t = 0; t < 11; t++)
        {
            var (r, g, bl) = CountryPalette.ColonyFill(t);
            var fill = ColorMath.FromHex($"{r:X2}{g:X2}{bl:X2}");
            var baseTone = ColorMath.FromHex(CountryPalette.Hex[t]);
            double de = ColorMath.DeltaE2000(fill, baseTone);
            Assert.InRange(de, 1.5, 5.0);                                   // same family: the hatch carries the difference
            Assert.True(ColorMath.Lstar(fill) <= 91.5, $"tone {t} L*={ColorMath.Lstar(fill):F1}");
            Assert.True(ColorMath.Lstar(fill) > ColorMath.Lstar(baseTone));
        }
        var s = CountryPalette.ColonyFill(11);
        Assert.Equal(CountryPalette.Hex[11], $"{s.R:X2}{s.G:X2}{s.B:X2}");   // stone: no mix
    }
}
