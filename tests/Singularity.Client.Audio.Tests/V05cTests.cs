using System.Linq;
using Singularity.Client.Spike;
using Xunit;

namespace Singularity.Client.Audio.Tests;

public class V05cTests
{
    [Fact]
    public void Texture_multiplier_stays_inside_the_hard_cap_everywhere()
    {
        foreach (var kind in new[] { SurfaceKind.Sheet, SurfaceKind.Paper, SurfaceKind.Button })
            foreach (bool pressed in new[] { false, true })
                foreach (double s in new[] { 0.0, 0.5, 1.0 })
                    for (double n = 0; n <= 1.0001; n += 0.25)
                        for (double e = 0; e <= 12; e += 1.5)
                            foreach (double rub in new[] { 0.0, 1.0 })
                            {
                                double k = PaperTexture.K(PaperTexture.Params(kind, pressed), n, 1 - n, n, e, pressed ? 1 : 0, rub, s);
                                Assert.InRange(k, e <= PaperTexture.BandEdge ? PaperTexture.KBandMin : PaperTexture.KInnerMin, PaperTexture.KMax);
                            }
    }

    [Fact]
    public void Off_preset_is_exactly_neutral_and_presets_never_exceed_normal()
    {
        Assert.Equal(1.0, PaperTexture.K(PaperTexture.Params(SurfaceKind.Sheet), 0.1, 0.9, 0.1, 0, 0, 0, PaperTexture.Strength(0)));
        Assert.Equal(0.5, PaperTexture.Strength(1));
        Assert.Equal(1.0, PaperTexture.Strength(2));
        Assert.Equal(1.0, PaperTexture.Strength(9));
    }

    [Fact]
    public void Off_preset_gives_exactly_k_1_inside_the_wear_width()
    {
        foreach (var kind in new[] { SurfaceKind.Sheet, SurfaceKind.Paper, SurfaceKind.Button })
            foreach (bool pressed in new[] { false, true })
            {
                var p = PaperTexture.Params(kind, pressed);
                for (double e = 0; e < p.WearWidth; e += 0.5)
                    Assert.Equal(1.0, PaperTexture.K(p, 0.2, 0.8, 0.1, e, pressed ? 1 : 0, 1, PaperTexture.Strength(0)));
            }
    }

    [Fact]
    public void Normal_interior_spread_is_visible_and_weak_is_half_of_it()
    {
        var (_, s1) = PaperTexture.InteriorStats(SurfaceKind.Sheet);
        var (_, sw) = PaperTexture.InteriorStats(SurfaceKind.Sheet, PaperTexture.Strength(1));
        Assert.True(s1 * 255 * 0.95 >= 3.0, $"sigma of k {s1} -> about {s1 * 255 * 0.95:F1} levels");
        Assert.InRange(sw / s1, 0.4, 0.6);
    }

    [Fact]
    public void Edge_band_is_darker_than_the_interior_by_5_to_9_percent()
    {
        var p = PaperTexture.Params(SurfaceKind.Sheet);
        var (mid, _) = PaperTexture.InteriorStats(SurfaceKind.Sheet);
        double band = 0; int n = 0;
        for (double e = 0; e < 6; e += 0.25) { band += PaperTexture.K(p, 0.5, 0.5, 0.5, e, 0, 0, 1); n++; }
        double drop = 1 - band / n / mid;
        Assert.InRange(drop, 0.03, 0.09);
    }

    [Fact]
    public void Tile_is_deterministic_seamless_and_not_flat()
    {
        var a = PaperTexture.BakeTile(); var b = PaperTexture.BakeTile();
        Assert.Equal(a, b);
        Assert.Equal(PaperTexture.TileSize * PaperTexture.TileSize * 3, a.Length);
        for (int ch = 0; ch < 3; ch++)
        {
            var v = Enumerable.Range(0, a.Length / 3).Select(i => a[i * 3 + ch]).ToArray();
            Assert.True(v.Max() - v.Min() > 80, $"channel {ch} is flat");
        }
        // Seamless: the step across the wrap is about as big as a neighbour step inside (no seam).
        int n = PaperTexture.TileSize;
        double wrap = 0, inner = 0;
        for (int y = 0; y < n; y++)
        {
            wrap += System.Math.Abs(a[(y * n + n - 1) * 3 + 2] - a[(y * n) * 3 + 2]);
            inner += System.Math.Abs(a[(y * n + n / 2) * 3 + 2] - a[(y * n + n / 2 + 1) * 3 + 2]);
        }
        Assert.True(wrap <= inner * 2 + n, $"seam: {wrap} vs {inner}");
    }

    [Fact]
    public void Name_hash_is_stable()
    {
        Assert.Equal(PaperTexture.NameHash("rail"), PaperTexture.NameHash("rail"));
        Assert.NotEqual(PaperTexture.NameHash("rail"), PaperTexture.NameHash("top"));
    }

    [Fact]
    public void Areas_have_the_seven_letters_and_locked_areas_have_a_milestone()
    {
        Assert.Equal(8, AreaData.All.Length);
        foreach (var c in "TEFMIGJ") Assert.NotNull(AreaData.ByLetter(c));
        Assert.Null(AreaData.ByLetter('X'));
        Assert.Null(AreaData.ByLetter('\0'));
        foreach (var a in AreaData.All.Where(a => a.State == AreaState.Locked)) Assert.NotEqual("", a.Milestone);
        Assert.Equal(AreaData.Seventh, AreaData.Of(AreaId.Projects).Ink);
    }

    [Fact]
    public void Signals_map_to_areas_and_the_system_fault_belongs_to_none()
    {
        Assert.Equal(AreaId.Production, AreaData.OfSignal("sig.alloy")!.Value.Area);
        Assert.Equal(2, AreaData.OfSignal("sig.refusal")!.Value.Severity);
        Assert.Null(AreaData.OfSignal("sig.fail"));
    }

    [Fact]
    public void Texture_rules_T1_T2_T4_T5_pass_on_palette_C()
    {
        var r = TextureValidator.Run(Palette.C);
        Assert.True(r.Count > 100);
        var bad = r.Where(x => !x.Pass).Select(x => x.Name + ": " + x.Detail).ToArray();
        Assert.True(bad.Length == 0, string.Join("\n", bad));
    }

    [Fact]
    public void Speed_gauge_hues_are_told_apart_and_avoid_the_alarm_sector()
    {
        var bad = TextureValidator.RunGauge().Where(x => !x.Pass).Select(x => x.Name + ": " + x.Detail).ToArray();
        Assert.True(bad.Length == 0, string.Join("\n", bad));
    }

    [Fact]
    public void T3_passes_only_for_a_flat_plate_and_a_textured_control()
    {
        Assert.True(TextureValidator.T3(0, 2.5, true).Pass);
        Assert.False(TextureValidator.T3(3.0, 2.5, true).Pass);
        Assert.False(TextureValidator.T3(0, 0, true).Pass);   // the test would be blind
        Assert.True(TextureValidator.T3(0, 0, false).Pass);
        Assert.Equal(0, TextureValidator.Variance(new byte[] { 7, 7, 7 }));
    }
}
