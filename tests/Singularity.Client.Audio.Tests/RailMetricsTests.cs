using System;
using System.IO;
using Singularity.Client.Spike;
using Xunit;

namespace Singularity.Client.Audio.Tests;

public class RailMetricsTests
{
    static string Repo()
    {
        var d = AppContext.BaseDirectory;
        while (d != null && !File.Exists(Path.Combine(d, "CLAUDE.md"))) d = Path.GetDirectoryName(d);
        return d ?? throw new InvalidOperationException("repo root not found");
    }

    static TtfReader Mono() => new(Path.Combine(Repo(), "src/Singularity.Client.Spike/assets/fonts/PTMono.ttf"));

    [Fact]
    public void BadgeCount_MeetsA1TextFloor()
    {
        // A1: data >= 13 du and a glyph height >= 9 physical px at 1920x1080, 100 %
        Assert.True(RailMetrics.BadgeFontDu >= 13);
        var ttf = Mono();
        foreach (char c in "0123456789")
            Assert.True(ttf.YMax(c) * (double)RailMetrics.BadgeFontDu / ttf.UnitsPerEm >= 9.0, $"digit {c}");
    }

    [Fact]
    public void BadgeFont12_WasBelowTheFloor()
    {
        // the previous 12 du size: the digit 9 stood 8 px high
        var ttf = Mono();
        Assert.True(ttf.YMax('9') * 12.0 / ttf.UnitsPerEm < 9.0);
    }

    [Fact]
    public void BadgeBox_HoldsSignAndTwoGlyphs()
    {
        // mono advance is 0.6 em; the sign takes 15 du from the left edge (Rail.DrawBadge)
        Assert.True(15 + 2 * 0.6 * RailMetrics.BadgeFontDu + 2 <= RailMetrics.BadgeW);
    }

    [Fact]
    public void Lock_VisibleBodyIsAtLeast16Px()
    {
        Assert.True(RailMetrics.LockVisibleWidthPx(RailMetrics.LockDrawDu) >= 16.0);
        Assert.True(RailMetrics.LockVisibleWidthPx(16) < 16.0);   // the old 16 du box showed about 9 px
    }

    [Fact]
    public void LockSvg_BodyMatchesTheConstants()
    {
        var svg = File.ReadAllText(Path.Combine(Repo(), "docs/ui/assets/area-icons/lock-24.svg"));
        Assert.Contains("M5 11h14v10H5Z", svg);   // body x 5..19 = 14 of 24 units
        Assert.Equal(14.0, RailMetrics.LockBodyUnits);
    }
}
