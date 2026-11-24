using System;
using Singularity.Client.Spike;
using Xunit;

namespace Singularity.Client.Audio.Tests;

public class GlyphExtentTests
{
    [Theory]
    [InlineData("FND")] [InlineData("PRC")] [InlineData("APP")] [InlineData("ENG")] [InlineData("CMP")] [InlineData("PRN")]
    public void ExtentReachesTheFarthestCornerAndScalesWithRadius(string code)
    {
        float r = 10f, e = GlyphGeometry.Extent(code, r);
        var o = GlyphGeometry.Outline(code, r);
        if (o != null) foreach (var (x, y) in o) Assert.True(MathF.Sqrt(x * x + y * y) <= e + 1e-4f);
        Assert.True(e >= r - 1e-4f);
        Assert.Equal(e * 1.5f, GlyphGeometry.Extent(code, 15f), 3);
    }

    [Fact]
    public void ASquareReachesPastItsRadiusToTheCorner() => Assert.Equal(10f * MathF.Sqrt(2f), GlyphGeometry.Extent("PRC", 10f), 3);

    [Fact]
    public void ADiamondAndATriangleReachPastTheMarkRadius()
    {
        Assert.Equal(13f, GlyphGeometry.Extent("APP", 10f), 3);
        Assert.Equal(12.5f, GlyphGeometry.Extent("FND", 10f), 3);
    }
}
