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

    [Fact]
    public void Dial_IsLargerThanTheFirstCut()
    {
        // DEBT-0059: the first cut had radius 30 and 2 px ticks of 8 du
        Assert.True(InstrumentLogic.DialRadius > 30 && InstrumentLogic.DialTickLength > 8 && InstrumentLogic.DialTickWidth > 2);
    }
}
