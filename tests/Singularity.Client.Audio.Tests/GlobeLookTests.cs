using Singularity.Client.Spike;
using Xunit;

namespace Singularity.Client.Audio.Tests;

public class GlobeLookTests
{
    [Theory]
    [InlineData(null, GlobeLook.Current)]
    [InlineData("", GlobeLook.Current)]
    [InlineData("nonsense", GlobeLook.Current)]
    [InlineData("a", GlobeLook.A)]
    [InlineData("A+", GlobeLook.APlus)]
    [InlineData("aplus", GlobeLook.APlus)]
    [InlineData("B", GlobeLook.B)]
    public void Parse_MapsFlagValues_UnknownKeepsCurrent(string? s, GlobeLook want) => Assert.Equal(want, GlobeLookLogic.Parse(s!));

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
