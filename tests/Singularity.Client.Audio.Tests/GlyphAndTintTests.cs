using System;
using System.Linq;
using Singularity.Client.Spike;
using Xunit;

namespace Singularity.Client.Audio.Tests;

public class GlyphAndTintTests
{
    const float NodeHalf = 7f;   // a node is about 14 px across

    [Fact]
    public void Eng_and_cmp_differ_by_silhouette_and_are_larger_than_before()
    {
        var eng = GlyphGeometry.Outline("ENG", NodeHalf)!; var cmp = GlyphGeometry.Outline("CMP", NodeHalf)!;
        Assert.Equal(6, eng.Length); Assert.Equal(5, cmp.Length);
        Assert.Equal(2, GlyphGeometry.TopVertices(eng)); Assert.Equal(1, GlyphGeometry.TopVertices(cmp));   // flat top vs apex
        // At least 15 % over the first v0.5d cut (hexagon r*1.1, regular pentagon r*1.15).
        double oldEng = 1.5 * Math.Sqrt(3) * (NodeHalf * 1.1) * (NodeHalf * 1.1);
        double oldCmp = 2.5 * (NodeHalf * 1.15) * (NodeHalf * 1.15) * Math.Sin(2 * Math.PI / 5);
        Assert.True(GlyphGeometry.Area(eng) >= 1.15 * oldEng);
        Assert.True(GlyphGeometry.Area(cmp) >= 1.15 * oldCmp);
        Assert.True(GlyphGeometry.Overlap(eng, cmp, 14) < 0.85);
    }

    [Fact]
    public void Polygon_glyphs_differ_by_vertex_count_or_overlap()
    {
        var shapes = new[] { "FND", "ENG", "CMP", "PRC", "APP" }.Select(c => (c, p: GlyphGeometry.Outline(c, NodeHalf)!)).ToArray();
        foreach (var a in shapes)
            foreach (var b in shapes)
                if (string.CompareOrdinal(a.c, b.c) < 0)
                    Assert.True(a.p.Length != b.p.Length || GlyphGeometry.Overlap(a.p, b.p, 14) < 0.8, $"{a.c} vs {b.c}");
        Assert.Null(GlyphGeometry.Outline("PRN", NodeHalf));
    }

    [Fact]
    public void Every_state_tint_either_stands_off_the_paper_by_15_dL_or_gets_an_edge()
    {
        foreach (var t in Palette.C.Tints)
            Assert.Equal(TintStrip.DeltaL(t, Palette.C.Paper) < TintStrip.MinDeltaL, TintStrip.NeedsEdge(t, Palette.C.Paper));
        // The palette does have pale tints, so the edge rule is live; the ink edge itself is far above the limit.
        Assert.Contains(Palette.C.Tints, t => TintStrip.NeedsEdge(t, Palette.C.Paper));
        Assert.True(TintStrip.DeltaL(Palette.Ink, Palette.C.Paper) >= TintStrip.MinDeltaL);
    }
}
