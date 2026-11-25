using System;
using System.Collections.Generic;
using System.Linq;
using Singularity.Client.Spike;
using Xunit;

namespace Singularity.MapBuilder.Tests;

public class PaletteReserveTests
{
    [Fact] public void Stone_is_only_for_DEU_and_turquoise_only_for_PRT()
    {
        // isolated codes and a crowded clique: nobody but DEU gets stone, nobody but PRT turquoise
        var codes = new List<string> { "DEU", "PRT", "EGY", "ETH", "LBR", "THA", "A1", "A2", "A3", "A4", "A5", "A6", "A7", "A8", "A9", "B1", "B2", "B3" }.OrderBy(c => c, StringComparer.Ordinal).ToList();
        var size = codes.Select(_ => 2).ToList();
        var nb = codes.Select((_, i) => codes.Select((_, j) => j).Where(j => j != i).ToList()).ToList();   // everyone borders everyone: the last-resort path
        var iso = codes.Select(_ => new List<int>()).ToList();                                            // nobody borders anyone: the no-neighbour path
        foreach (var g in new[] { nb, iso })
        {
            var t = CountryPalette.Assign(codes, size, g);
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i] != "DEU") Assert.NotEqual(CountryPalette.Stone, t[i]);
                if (codes[i] != "PRT") Assert.NotEqual(CountryPalette.Turquoise, t[i]);
                if (codes[i] != "SUN") Assert.NotEqual(2, t[i]);   // terracotta is SUN's (none of these codes is SUN)
                if (codes[i] != "FRA") Assert.NotEqual(3, t[i]);   // blue is FRA's
            }
        }
    }
}
