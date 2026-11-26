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
        Assert.True(new ShotFilter("*_en").WantsSuffix("_1920x1080_s125"));   // a mask is never pruned
        Assert.True(new ShotFilter("tree_*").WantsSuffix("_1920x1080_s150_en"));
        Assert.True(new ShotFilter("area_science_1920x1080_en,tree_?").WantsSuffix("_1920x1080"));
    }

    [Fact]
    public void QuestionMarkInTailAndUnmatchedPatterns()
    {
        Assert.True(new ShotFilter("a_1920x1080_s1?5").WantsSuffix("_1920x1080_s125"));
        Assert.True(new ShotFilter("a_1920x1080_s1?5_en").WantsSuffix("_1920x1080_s125"));
        var f = new ShotFilter("map,typo*");
        Assert.Equal(new[] { "typo*" }, f.Unmatched(new[] { "map.png" }));
        Assert.Equal(new[] { "map", "typo*" }, f.Unmatched(System.Array.Empty<string>()));
    }

    [Fact]
    public void GreyTwinKeepsItsCombination()
    {
        var f = new ShotFilter("area_science_1920x1080_grey");
        Assert.True(f.WantsSuffix("_1920x1080"));
        Assert.False(f.WantsSuffix("_1920x1080_en"));
        Assert.True(f.Wants("area_science_1920x1080_grey.png"));
    }

    [Fact]
    public void AllExactOnlyWithoutWildcards()
    {
        Assert.True(new ShotFilter("a,b_grey.png").AllExact);
        Assert.False(new ShotFilter("a,b*").AllExact);
        Assert.False(new ShotFilter("a?").AllExact);
    }

    [Fact]
    public void LegacyStageOnlyWhenAPatternIsUnsized()
    {
        Assert.False(new ShotFilter("area_science_1920x1080").WantsLegacyStage);
        Assert.True(new ShotFilter("area_science_1920x1080,globe_panel").WantsLegacyStage);
        Assert.True(new ShotFilter("tree_*").WantsLegacyStage);
    }

    [Fact]
    public void WantsGroup_RunsAGroupOnlyWhenOneOfItsFramesIsWanted()
    {
        var f = new ShotFilter("tech_s1_*,tree_year_step2_1920x1080");
        Assert.True(f.WantsGroup("_1920x1080", "tech_s1_overview", "tech_s1_card"));
        Assert.True(f.WantsGroup("_1920x1080", "tree_year_step2", "tree_year_step5"));
        Assert.False(f.WantsGroup("_1920x1080", "area_finance", "area_science", "menu_ingame"));
        Assert.False(f.WantsGroup("_1920x1080_s125", "tree_year_step2"));
        Assert.True(new ShotFilter("area_finance_1920x1080_grey").WantsGroup("_1920x1080", "area_finance"));
    }
}
