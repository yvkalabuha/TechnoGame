using System;
using System.Linq;
using Singularity.Client.Spike;
using Xunit;

namespace Singularity.Client.Audio.Tests;

public class InstrumentTests
{
    [Fact]
    public void PaletteC_PassesInstrumentRules()
    {
        var res = ContrastValidator.RunInstruments(Palette.C);
        Assert.Empty(res.Where(r => !r.Pass).Select(r => r.Name + ": " + r.Detail));
        Assert.Equal(47, res.Count);
    }

    [Fact]
    public void LoudDeskLine_Fails()
    {
        // a dark line on the desk reads as a control: dE and WCAG rules break
        var res = ContrastValidator.RunInstruments(Palette.C, deskLine: "5A5648");
        Assert.Contains(res, r => !r.Pass && r.Name.StartsWith("L1 desk-line vs desk dE00"));
        Assert.Contains(res, r => !r.Pass && r.Name.Contains("never reads as a control"));
    }

    [Fact]
    public void ChromaticDeskLine_FailsChroma()
    {
        var res = ContrastValidator.RunInstruments(Palette.C, deskLine: "C8B070");
        Assert.Contains(res, r => !r.Pass && r.Name.Contains("desk-line OKLCH chroma"));
    }

    [Fact]
    public void InvisibleDeskLine_Fails()
    {
        var res = ContrastValidator.RunInstruments(Palette.C, deskLine: Palette.Desk);
        Assert.Contains(res, r => !r.Pass && r.Name.StartsWith("L1 desk-line vs desk dE00"));
    }

    [Fact]
    public void WeakInkLine_Fails()
    {
        var weak = Palette.C with { Line = Palette.C.Line.Select(_ => "E0D8B8").ToArray() };
        var res = ContrastValidator.RunInstruments(weak);
        Assert.Contains(res, r => !r.Pass && r.Name.StartsWith("L2 line.teal on sheet"));
    }

    [Theory]
    [InlineData(1910, 1)]
    [InlineData(1945, 1)]
    [InlineData(1969, 1)]
    [InlineData(1970, 2)]
    [InlineData(1999, 2)]
    [InlineData(2000, 3)]
    [InlineData(2100, 3)]
    public void Era_ByYear(int year, int era) => Assert.Equal(era, InstrumentLogic.Era(year));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 5)]
    [InlineData(47, 45)]
    [InlineData(48, 50)]
    [InlineData(100, 100)]
    [InlineData(130, 100)]
    public void CaliperSnap_ToStepOfFive(double v, int expect) => Assert.Equal(expect, InstrumentLogic.CaliperSnap(v));

    [Theory]
    [InlineData(40, 1, 45)]
    [InlineData(40, -1, 35)]
    [InlineData(47, 1, 50)]
    [InlineData(47, -1, 45)]
    [InlineData(100, 1, 100)]
    [InlineData(0, -1, 0)]
    [InlineData(3, -1, 0)]
    public void CaliperArrow_StepsOnTheGrid(int v, int dir, int expect) => Assert.Equal(expect, InstrumentLogic.CaliperArrow(v, dir));

    [Fact]
    public void FlaskLevel_ClampsAndHandlesNoNeed()
    {
        Assert.Equal(0.5, InstrumentLogic.FlaskLevel(5, 10));
        Assert.Equal(1, InstrumentLogic.FlaskLevel(12, 10));
        Assert.Equal(0, InstrumentLogic.FlaskLevel(-1, 10));
        Assert.Equal(1, InstrumentLogic.FlaskLevel(0, 0));
    }

    [Fact]
    public void AlloyLevel_IsSupplyAgainstSupplyPlusDeficit()
    {
        Assert.Equal(0.75, InstrumentLogic.AlloyLevel(3, 1));
        Assert.Equal(1, InstrumentLogic.AlloyLevel(3, 0));
        Assert.Equal(0, InstrumentLogic.AlloyLevel(0, 2));
        Assert.Equal(1, InstrumentLogic.AlloyLevel(0, 0));
    }

    [Theory]
    [InlineData(1e9, 1e-6)]
    [InlineData(1, 1)]
    [InlineData(0, 5)]
    public void AlloyLevel_AnyDeficitIsBelowFull(double supply, double deficit) => Assert.True(InstrumentLogic.AlloyLevel(supply, deficit) < 1);

    [Theory]
    [InlineData(0, true, false, false)]    // layer off
    [InlineData(1, true, false, true)]     // weak, own area panel
    [InlineData(2, true, false, true)]     // normal, own area panel
    [InlineData(2, true, true, false)]     // foreign power
    [InlineData(2, false, false, false)]   // not an area panel
    public void CaliperShown_OnlyOwnAreaPanelWithTheLayerOn(int level, bool area, bool foreign, bool expected) =>
        Assert.Equal(expected, InstrumentLogic.CaliperShown(level, area, foreign));

    [Fact]
    public void Caliper_NeverShownAtLevelZeroOrForeign()
    {
        Assert.False(InstrumentLogic.CaliperShown(0, true, false));
        Assert.False(InstrumentLogic.CaliperShown(2, true, true));
        Assert.False(InstrumentLogic.OrganShown(2, false));
        Assert.True(InstrumentLogic.CaliperShown(1, true, false));
    }

    [Fact]
    public void HatchSpan_NeckLinesStayInsideTheNeck()
    {
        // flask neck: y 2..16, half-width 6 about the axis; every drawn point (c - y, y) must lie within 6 of the axis (t = c - axis)
        int drawn = 0;
        for (float t = -4; t <= 80; t += 5)
        {
            var (lo, hi) = InstrumentLogic.HatchSpan(2, 16, 6, 0, t);
            if (lo >= hi) continue;
            drawn++;
            foreach (float y in new[] { lo, (lo + hi) / 2, hi })
            {
                Assert.InRange(y, 2f, 16f);
                Assert.InRange(Math.Abs(t - y), 0f, 6.001f);
            }
        }
        Assert.True(drawn >= 3);   // the neck really gets hatching lines
    }

    [Theory]
    [InlineData(10f)]
    [InlineData(14f)]
    [InlineData(16f)]
    public void HatchSpan_NeckIsHatchedAtAnyEmptyHeight(float yLevel)
    {
        // the empty neck above the liquid is y 2..yLevel: at least one line is drawn for every level below full
        bool any = false;
        for (float t = -4; t <= 80; t += 5) { var (lo, hi) = InstrumentLogic.HatchSpan(2, yLevel, 6, 0, t); any |= lo < hi; }
        Assert.True(any);
    }
}
