using System.Linq;
using Singularity.Client.Spike;
using Xunit;

namespace Singularity.Client.Audio.Tests;

public class LegendDataTests
{
    [Fact]
    public void Discipline_colours_are_the_palette_c_inks()
    {
        var d = LegendData.Disciplines(Palette.C);
        Assert.Equal(5, d.Length);
        // Aero teal, Prop plum, Metal slate, Chem olive, Mech sand (the tree's own mapping).
        Assert.Equal(new[] { "168A92", "A03E82", "4A7088", "4F8A1E", "8F7A2E" }, d.Select(x => x.LineHex));
        Assert.Equal(new[] { "BFE6E5", "F0C9E0", "C3D8EA", "CFE6AA", "EAD9A3" }, d.Select(x => x.WashHex));
        Assert.Equal(new[] { "disc.0", "disc.1", "disc.2", "disc.3", "disc.4" }, d.Select(x => x.NameKey));
    }

    [Fact]
    public void Glyph_codes_follow_the_core_node_type_order_and_each_has_a_shape()
    {
        Assert.Equal(new[] { "FND", "PRN", "ENG", "CMP", "PRC", "APP" }, LegendData.Types.Select(t => t.Code));
        Assert.Equal(new[] { "ntype.0", "ntype.1", "ntype.2", "ntype.3", "ntype.4", "ntype.5" }, LegendData.Types.Select(t => t.NameKey));
        Assert.Equal(6, LegendData.Types.Select(t => t.Shape).Distinct().Count());
        Assert.Equal("square", LegendData.Types[4].Shape);
        Assert.Equal("diamond", LegendData.Types[5].Shape);
    }
}
