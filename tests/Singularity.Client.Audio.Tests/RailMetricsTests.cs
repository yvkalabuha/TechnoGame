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
        Assert.True(RailMetrics.BadgeTextX + 2 * RailMetrics.MonoAdvanceEm * RailMetrics.BadgeFontDu + 2 <= RailMetrics.BadgeW);   // "9+" and 2 du before the right edge of the plate
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

    public static readonly object[][] Cases =
    {
        new object[] { false, false, true, 1 }, new object[] { false, false, false, 3 }, new object[] { false, true, false, 1 },
        new object[] { true, false, true, 1 }, new object[] { true, false, true, 3 }, new object[] { true, true, false, 1 }, new object[] { true, true, false, 3 },
    };

    [Theory, MemberData(nameof(Cases))]
    public void Button_EveryMarkIsAtLeast5DuFromTheEdge(bool wide, bool locked, bool badge, int chars)
    {
        var r = RailMetrics.Of(wide, locked, badge, chars);
        Assert.Equal(RailMetrics.ButtonW(wide), r.W); Assert.Equal(RailMetrics.ButtonH(wide, locked), r.H);
        var marks = new System.Collections.Generic.List<Box> { r.Icon, r.Letter };
        if (r.Badge is { } b) marks.Add(b);
        if (r.LockInk is { } l) marks.Add(l);
        foreach (var m in marks) Assert.True(m.EdgeGap(r.W, r.H) >= 5 - 1e-9, $"{m} gap {m.EdgeGap(r.W, r.H)}");
    }

    [Theory, MemberData(nameof(Cases))]
    public void Button_MarksDoNotOverlap(bool wide, bool locked, bool badge, int chars)
    {
        var r = RailMetrics.Of(wide, locked, badge, chars);
        if (r.Badge is { } b) { Assert.False(b.Intersects(r.Icon)); Assert.False(b.Intersects(r.Letter)); }
        if (r.LockInk is { } l) { Assert.False(l.Intersects(r.Icon)); Assert.False(l.Intersects(r.Letter)); Assert.True(l.Y - r.Icon.B >= 0 || wide); }
        Assert.False(r.Letter.Intersects(r.Icon));
    }

    [Fact]
    public void LockedButton_HasLockOf28DuAndTheIconStays()
    {
        foreach (bool wide in new[] { false, true })
        {
            var r = RailMetrics.Of(wide, true, false, 1);
            Assert.Equal(28, r.LockDraw.W); Assert.Equal(24, r.Icon.W);   // the area icon is kept, the lock is an extra sign
            Assert.True(r.LockInk!.Value.W >= 16);
        }
    }

    [Fact]
    public void WideButton_PlateAndLetterShareNoColumn_NameEndsBeforeThePlate()
    {
        var r = RailMetrics.Of(true, false, true, 3);   // "Esc" is the widest letter
        Assert.True(r.Badge!.Value.R <= r.Letter.X);
        Assert.True(r.NameX + r.NameW + 5 <= r.Badge.Value.X);
    }

    [Fact]
    public void WideLockedButton_HoldsNameAndCaptionWithoutPlate()
    {
        var r = RailMetrics.Of(true, true, false, 1);
        Assert.True(r.Caption); Assert.Null(r.Badge);
        Assert.True(r.NameX + r.NameW <= r.LockInk!.Value.X - 5);   // name and caption stop before the lock
        Assert.True(RailMetrics.CentreTop(r.H, RailMetrics.Line14Du + RailMetrics.Line12Du) >= 5);   // name line + caption (measured line heights) inside 52 du with 5 du above
    }

    [Fact]
    public void RailWidth_HoldsTheButtonCentred()
    {
        foreach (bool wide in new[] { false, true })
        {
            int width = wide ? RailMetrics.WideWidth : RailMetrics.NarrowWidth;
            Assert.Equal(width, RailMetrics.ButtonW(wide) + 2 * RailMetrics.SideMargin(wide));
        }
    }

    [Theory, MemberData(nameof(Cases))]
    public void Button_LetterMarkerBadgeLockAreSeparate(bool wide, bool locked, bool badge, int chars)
    {
        var r = RailMetrics.Of(wide, locked, badge, chars);
        var marks = new System.Collections.Generic.List<Box> { r.Letter, r.Marker };
        if (r.Badge is { } b) marks.Add(b);
        if (r.LockInk is { } l) marks.Add(l);
        for (int i = 0; i < marks.Count; i++) for (int j = i + 1; j < marks.Count; j++) Assert.False(marks[i].Intersects(marks[j]), $"{marks[i]} / {marks[j]}");
        if (wide)
        {
            Assert.True(r.Marker.X - r.Letter.R >= 6);
            Assert.Equal(230, r.Letter.R, 6);
            if (r.Badge is { } bd) Assert.True(bd.X - (r.NameX + r.NameW) >= 6);
        }
    }

    [Fact]
    public void WideBadgeColumn_ClearsTheWidestLetter()
    {
        var r = RailMetrics.Of(true, false, true, 3);   // "Esc" under a hypothetical badge on Map
        Assert.True(r.Letter.X - r.Badge!.Value.R >= 4.5);   // advance box of "Esc" (25.2 du); the spec's ink box (206..230) gives 6
        Assert.Equal(166, r.Badge.Value.X); Assert.Equal(238, r.Marker.X); Assert.Equal(244, r.Marker.R);
    }
}
