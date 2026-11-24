using Singularity.Client.Spike;
using Xunit;

namespace Singularity.Client.Audio.Tests;

public class GlobeLookTests
{
    [Theory]
    [InlineData(null, GlobeLookLogic.Default)]
    [InlineData("", GlobeLookLogic.Default)]
    [InlineData("nonsense", GlobeLookLogic.Default)]
    [InlineData("a", GlobeLook.A)]
    [InlineData("A+", GlobeLook.APlus)]
    [InlineData("aplus", GlobeLook.APlus)]
    [InlineData("B", GlobeLook.B)]
    public void Parse_MapsFlagValues_UnknownGivesDefault(string? s, GlobeLook want) => Assert.Equal(want, GlobeLookLogic.Parse(s!));

    [Fact]
    public void Default_IsThePhysicalAtmosphere_Owner444() => Assert.Equal(GlobeLook.B, GlobeLookLogic.Default);

    [Fact]
    public void DeltaE_ZeroForSame_AndLargeForBlackWhite()
    {
        Assert.Equal(0.0, GlobeLookLogic.DeltaE(0.4, 0.5, 0.6, 0.4, 0.5, 0.6), 6);
        Assert.InRange(GlobeLookLogic.DeltaE(0, 0, 0, 1, 1, 1), 99.0, 101.0);
        Assert.True(GlobeLookLogic.DeltaE(0.80, 0.82, 0.85, 0.80, 0.82, 0.88) < GlobeLookLogic.DeltaE(0.80, 0.82, 0.85, 0.70, 0.82, 0.85));
    }

    [Fact]
    public void AngleForLimbRatio_FindsTheLimbAndTheCentre()
    {
        Assert.Equal(System.Math.Acos(1 / 2.6) * 180 / System.Math.PI, GlobeLookLogic.AngleForLimbRatio(2.6, 1.0), 3);
        Assert.Equal(0.0, GlobeLookLogic.AngleForLimbRatio(2.6, 0.0), 3);
        var a = GlobeLookLogic.AngleForLimbRatio(2.6, 0.96);
        Assert.InRange(a, 52, 55);   // tan(alpha) = 0.96 * tan(limb) = 0.4 gives about 53.5 degrees
    }

    [Fact]
    public void ProjectedRadiusRatio_IsOneAtTheLimb_AndBelowTheViewSpaceDistanceInside()
    {
        Assert.Equal(1.0, GlobeLookLogic.ProjectedRadiusRatio(1.0, 2.0), 9);
        Assert.InRange(GlobeLookLogic.ProjectedRadiusRatio(0.96, 2.0), 0.945, 0.952);   // h = 0.96 is 0.948 R on screen at distance 2: the clamp must use this, not h
        Assert.True(GlobeLookLogic.ProjectedRadiusRatio(0.5, 3.0) < GlobeLookLogic.ProjectedRadiusRatio(0.6, 3.0));
    }

    [Fact]
    public void MaxLightDotOnVisibleCap_AntipodeIsDark_EquatorSunOnA48DegViewIsNot()
    {
        Assert.True(GlobeLookLogic.MaxLightDotOnVisibleCap(180, 2.6) <= -0.22);
        // the first night frame: view at lat 48, sun at the equator on the opposite meridian = 132 degrees from the axis, sunlit terrain remains
        Assert.True(GlobeLookLogic.MaxLightDotOnVisibleCap(132, 2.6) > -0.22);
    }

    [Fact]
    public void Next_CyclesThroughAllFourAndBack()
    {
        var l = GlobeLook.Current; var seen = new System.Collections.Generic.List<GlobeLook>();
        for (int i = 0; i < 4; i++) { l = GlobeLookLogic.Next(l); seen.Add(l); }
        Assert.Equal(new[] { GlobeLook.A, GlobeLook.APlus, GlobeLook.B, GlobeLook.Current }, seen);
    }

    [Fact]
    public void Direction_IsUnitAndMatchesSphereAxes()
    {
        var (x, y, z) = GlobeLookLogic.Direction(0, 0);
        Assert.Equal(0f, x, 4); Assert.Equal(0f, y, 4); Assert.Equal(1f, z, 4);
        var d = GlobeLookLogic.Direction(75, 30);
        Assert.Equal(1f, d.X * d.X + d.Y * d.Y + d.Z * d.Z, 4);
        Assert.True(d.X > 0 && d.Y > 0);
    }
}
