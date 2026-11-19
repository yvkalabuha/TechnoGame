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
}
