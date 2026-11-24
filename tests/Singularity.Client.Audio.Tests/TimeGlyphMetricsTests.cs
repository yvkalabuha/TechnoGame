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

    // Scales of the Settings slider (100..150 step 12.5) and fractional layout origins.
    public static readonly double[] Scales = { 1.0, 1.125, 1.25, 1.375, 1.5 };
    public static readonly double[] Origins = { 0.0, 0.25, 0.5, 0.75, 13.75, 167.5 };

    [Fact]
    public void TimeSnap_EdgesStayWithinHalfDuOfTheirPosition()
    {
        foreach (double s in Scales)
            foreach (double o in Origins)
                for (double u = 0; u <= 60; u += 0.5)
                    Assert.True(Math.Abs((TimeSnap.Edge(o, u, s) - o) / s - u) <= 0.5 + 1e-9, $"s {s} origin {o} u {u}");
    }

    [Fact]
    public void TimeSnap_PauseGlyphIsMirrorSymmetricInPixelsAtEveryScale()
    {
        foreach (double s in Scales)
            foreach (double o in Origins)
            {
                double c = TimeSnap.Edge(o, 16, s);
                // pause: ink edges of the two bars 8..14 and 18..24 du
                Assert.Equal(c - TimeSnap.GlyphEdge(o, 8, s), TimeSnap.GlyphEdge(o, 24, s) - c);
                Assert.Equal(c - TimeSnap.GlyphEdge(o, 14, s), TimeSnap.GlyphEdge(o, 18, s) - c);
                Assert.True(Math.Abs((c - o) / s - 16) <= 0.5 + 1e-9);   // the pixel centre is within 0.5 du of the button centre
            }
    }

    [Fact]
    public void TimeGauge_PixelFrameIsSymmetricAroundThePixelBarAtEveryScale()
    {
        foreach (double s in Scales)
            foreach (double o in Origins)
                for (int i = 0; i < TimeGaugeGeometry.Bars; i++)
                {
                    var b = TimeGaugeGeometry.BarPx(i, o, o, s); var f = TimeGaugeGeometry.FramePx(i, o, o, s);
                    Assert.Equal(b.X0 - f.X0, f.X1 - b.X1); Assert.Equal(b.Y0 - f.Y0, f.Y1 - b.Y1); Assert.True(b.X0 - f.X0 >= 2);   // equal margin, wider than the 1 px gap
                    Assert.True(Math.Abs((b.X0 - o) / s - TimeGaugeGeometry.Bar(i).X) <= 0.5 + 1e-9);
                }
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
