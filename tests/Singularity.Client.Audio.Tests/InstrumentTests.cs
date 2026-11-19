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
    public void AlloyFlask_NoMeltIsEmptyHatchedAndNamedNoSmelting()
    {
        var f = InstrumentLogic.AlloyFlask(0, 0, null);
        Assert.Equal(0, f.Level); Assert.True(f.Hatched); Assert.Equal(InstrumentLogic.AlloyState.NoSmelting, f.State);
        Assert.Equal("flask.alloy.none", InstrumentLogic.AlloyTipKey(f.State));
    }

    [Fact]
    public void AlloyFlask_LinkWithHaveBelowNeedIsAShortage()
    {
        var f = InstrumentLogic.AlloyFlask(40, 0, (40, 100));
        Assert.Equal(0.4, f.Level, 9); Assert.True(f.Hatched); Assert.Equal(InstrumentLogic.AlloyState.Shortage, f.State);
        Assert.Equal("flask.alloy.deficit", InstrumentLogic.AlloyTipKey(f.State));
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(120, 100)]
    public void AlloyFlask_LinkWithHaveAtOrAboveNeedIsNoShortage(double have, double need)
    {
        var f = InstrumentLogic.AlloyFlask(have, 0, (have, need));
        Assert.False(f.Hatched); Assert.Equal(InstrumentLogic.AlloyState.Normal, f.State); Assert.Equal(1.0, f.Level);
        Assert.Equal("flask.alloy.tip", InstrumentLogic.AlloyTipKey(f.State));
    }

    [Theory]
    [InlineData(InstrumentLogic.AlloyState.Normal, "flask.alloy.cap.ok")]
    [InlineData(InstrumentLogic.AlloyState.Shortage, "flask.alloy.cap.deficit")]
    [InlineData(InstrumentLogic.AlloyState.Disrupted, "flask.alloy.cap.disrupted")]
    [InlineData(InstrumentLogic.AlloyState.NoSmelting, "flask.alloy.cap.none")]
    public void AlloyCaptionKey_NamesWhatEachStateMeasures(InstrumentLogic.AlloyState state, string key) => Assert.Equal(key, InstrumentLogic.AlloyCaptionKey(state));

    [Fact]
    public void AlloyFlask_DisruptionIsNotAShortage_AndHealthyStockIsNormal()
    {
        var d = InstrumentLogic.AlloyFlask(10, 0.25, null);
        Assert.Equal(0.75, d.Level, 9); Assert.True(d.Hatched); Assert.Equal(InstrumentLogic.AlloyState.Disrupted, d.State);
        Assert.Equal("flask.alloy.disrupted", InstrumentLogic.AlloyTipKey(d.State));
        var n = InstrumentLogic.AlloyFlask(10, 0, null);
        Assert.Equal(1.0, n.Level); Assert.False(n.Hatched); Assert.Equal(InstrumentLogic.AlloyState.Normal, n.State);
    }

    [Theory]
    [InlineData(0.996, true, 99)]    // a hatched flask never reads 100 %
    [InlineData(0.9, true, 90)]
    [InlineData(1.0, false, 100)]
    [InlineData(0.996, false, 100)]
    [InlineData(0.0, true, 0)]
    public void FlaskPercent_CapsHatchedAt99_ForCaptionAndTooltipAlike(double level, bool hatched, int expect) => Assert.Equal(expect, InstrumentLogic.FlaskPercent(level, hatched));

    [Fact]
    public void AlloyFlask_TinyDisruptionTooltipPercentIs99()
    {
        var f = InstrumentLogic.AlloyFlask(10, 0.001, null);
        Assert.Equal(99, InstrumentLogic.FlaskPercent(f.Level, f.Hatched));
    }

    [Fact]
    public void FlaskLevel_ClampsAndHandlesNoNeed()
    {
        Assert.Equal(0.5, InstrumentLogic.FlaskLevel(5, 10));
        Assert.Equal(1, InstrumentLogic.FlaskLevel(12, 10));
        Assert.Equal(0, InstrumentLogic.FlaskLevel(-1, 10));
        Assert.Equal(1, InstrumentLogic.FlaskLevel(0, 0));
    }

    [Theory]
    [InlineData(0.0, 1.0)]      // no disruption: the deficit fraction is 0, the stock is the whole normal supply
    [InlineData(0.5, 0.5)]      // PolicyParams.DisruptionDeficit = 0.5 (a real view value): half of the normal supply
    [InlineData(0.25, 0.75)]
    [InlineData(1.0, 0.0)]
    [InlineData(1.5, 0.0)]
    public void AlloyLevel_IsWhatIsLeftOfTheNormalSupply(double deficitFraction, double expected) => Assert.Equal(expected, InstrumentLogic.AlloyLevel(deficitFraction), 9);

    [Fact]
    public void AlloyLevel_IgnoresTheKilogramSupply()
    {
        // the view gives supply in kg (cap * (1 - deficit) * yield, e.g. 4200 kg) and the deficit as a fraction; only the fraction may set the level
        Assert.Equal(0.5, InstrumentLogic.AlloyLevel(0.5));
    }

    [Theory]
    [InlineData(1e-6)]
    [InlineData(0.01)]
    [InlineData(0.5)]
    public void AlloyLevel_AnyDeficitIsBelowFull(double deficitFraction) => Assert.True(InstrumentLogic.AlloyLevel(deficitFraction) < 1);

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
