using System;
using Singularity.Client.Spike;
using Xunit;

namespace Singularity.Client.Audio.Tests;

public class TimeGlyphMetricsTests
{
    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void TimeGlyph_InkIsAtLeast16DuAndKeeps5DuFromTheButtonEdge(int kind)
    {
        var (x, y, w, h) = TimeGlyphMetrics.InkBox(kind);   // pause, slower, faster
        Assert.True(w >= 16 && h >= 16, $"ink {w} x {h}");   // A2: a meaningful glyph is at least 16 x 16 px at 1920x1080, 100 %
        Assert.True(Math.Min(Math.Min(x, TimeGlyphMetrics.Button - (x + w)), Math.Min(y, TimeGlyphMetrics.Button - (y + h))) >= 5 - 1e-9);   // T6
    }
}
