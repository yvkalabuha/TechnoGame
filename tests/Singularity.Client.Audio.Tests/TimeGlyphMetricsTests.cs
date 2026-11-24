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

    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void TimeGlyph_OpticalCentreIsTheButtonCentre(int kind)
    {
        var (cx, cy) = TimeGlyphMetrics.StrokeCentroid(kind);
        double mid = TimeGlyphMetrics.Button / 2.0;
        Assert.True(Math.Abs(cx - mid) <= 0.5 && Math.Abs(cy - mid) <= 0.5, $"centre of mass {cx:F2}, {cy:F2}");
    }

    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void TimeGlyph_CentreLinesAreWholeDu(int kind)
    {
        foreach (var poly in TimeGlyphMetrics.Shapes(kind))
            foreach (var (x, y) in poly) Assert.True(x == Math.Round(x) && y == Math.Round(y));   // a 2 du stroke then covers whole pixels
    }

    [Fact]
    public void TimeGauge_GroupIsCentredInItsBox()
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = 0, y1 = 0;
        for (int i = 0; i < TimeGaugeGeometry.Bars; i++)
        {
            var b = TimeGaugeGeometry.Bar(i);
            x0 = Math.Min(x0, b.X); y0 = Math.Min(y0, b.Y); x1 = Math.Max(x1, b.X + b.W); y1 = Math.Max(y1, b.Y + b.H);
        }
        Assert.Equal(TimeGaugeGeometry.Width, x0 + x1);   // equal margins left and right
        Assert.Equal(TimeGaugeGeometry.Height, y0 + y1);
    }

    [Fact]
    public void TimeGauge_ActiveFrameIsSymmetricInsideTheBoxAndTouchesNoNeighbour()
    {
        for (int i = 0; i < TimeGaugeGeometry.Bars; i++)
        {
            var b = TimeGaugeGeometry.Bar(i); var f = TimeGaugeGeometry.Frame(i);
            Assert.Equal(b.X - f.X, f.X + f.W - (b.X + b.W)); Assert.Equal(b.Y - f.Y, f.Y + f.H - (b.Y + b.H));   // the same margin on every side
            Assert.True(f.X >= 0 && f.Y >= 0 && f.X + f.W <= TimeGaugeGeometry.Width && f.Y + f.H <= TimeGaugeGeometry.Height);
            for (int j = 0; j < TimeGaugeGeometry.Bars; j++)
            {
                if (j == i) continue;
                var n = TimeGaugeGeometry.Bar(j);
                Assert.True(n.X >= f.X + f.W || n.X + n.W <= f.X, $"frame {i} meets bar {j}");
            }
        }
    }
}
