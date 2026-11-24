using Singularity.Client.Spike;
using Xunit;

namespace Singularity.Client.Audio.Tests;

public class ShotFilterTests
{
    [Fact]
    public void ExactNameWithOrWithoutPng()
    {
        var f = new ShotFilter("area_science_1920x1080, map.png");
        Assert.True(f.Wants("area_science_1920x1080.png"));
        Assert.True(f.Wants("map"));
        Assert.False(f.Wants("area_science_1920x1080_en.png"));
    }

    [Fact]
    public void Wildcards()
    {
        var f = new ShotFilter("area_*_en");
        Assert.True(f.Wants("area_finance_1920x1080_en.png"));
        Assert.False(f.Wants("area_finance_1920x1080.png"));
        Assert.True(new ShotFilter("a?c").Wants("abc.png"));
    }

    [Fact]
    public void SuffixPrunesOtherCombinations()
    {
        var f = new ShotFilter("area_science_1920x1080");
        Assert.True(f.WantsSuffix("_1920x1080"));
        Assert.False(f.WantsSuffix("_1920x1080_en"));
        Assert.False(f.WantsSuffix("_1920x1080_s150"));
        Assert.True(new ShotFilter("*_en").WantsSuffix("_1920x1080_s125_en"));
        Assert.False(new ShotFilter("*_en").WantsSuffix("_1920x1080_s125"));
        Assert.True(new ShotFilter("tree_*").WantsSuffix("_1920x1080_s150_en"));
    }

    [Fact]
    public void QuestionMarkInTailAndUnmatchedPatterns()
    {
        Assert.True(new ShotFilter("a_1920x1080_s1?5").WantsSuffix("_1920x1080_s125"));
        Assert.False(new ShotFilter("a_1920x1080_s1?5_en").WantsSuffix("_1920x1080_s125"));
        var f = new ShotFilter("map,typo*");
        Assert.Equal(new[] { "typo*" }, f.Unmatched(new[] { "map.png" }));
        Assert.Equal(new[] { "map", "typo*" }, f.Unmatched(System.Array.Empty<string>()));
    }

    [Fact]
    public void LegacyStageOnlyWhenAPatternIsUnsized()
    {
        Assert.False(new ShotFilter("area_science_1920x1080").WantsLegacyStage);
        Assert.True(new ShotFilter("area_science_1920x1080,globe_panel").WantsLegacyStage);
        Assert.True(new ShotFilter("tree_*").WantsLegacyStage);
    }
}
