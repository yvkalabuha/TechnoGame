using System.Text.RegularExpressions;
using Singularity.Client.Spike;

namespace Singularity.Client.Audio.Tests;

/// <summary>TASK-0192: --only must select the groups of the real step registry; the registry is checked against the frames Main.cs saves.</summary>
public class ShotRegistryTests
{
    static string MainSource()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Singularity.sln"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!, "src", "Singularity.Client.Spike", "Main.cs"));
    }

    [Fact]
    public void EverySizedFrameMainSaves_IsInAGroupOfTheRegistry()
    {
        var bases = new HashSet<string>(ShotRegistry.Groups.SelectMany(g => g.Bases).Concat(ShotRegistry.LargeBases));
        var missing = new List<string>();
        foreach (Match m in Regex.Matches(MainSource(), "Shot\\(\\$\"([a-z0-9_]+)(\\{cap\\})?\\{sfx\\}\\.png\""))
        {
            string b = m.Groups[1].Value;
            bool ok = m.Groups[2].Success ? bases.Any(x => x.StartsWith(b)) : bases.Contains(b);
            if (!ok) missing.Add(b);
        }
        Assert.Empty(missing);
    }

    [Fact]
    public void EveryUnsizedFrameMainSaves_IsInTheLegacyList()
    {
        var missing = new List<string>();
        foreach (Match m in Regex.Matches(MainSource(), "Shot\\(\"([A-Za-z0-9_]+)\\.png\""))
            if (!ShotRegistry.Legacy.Contains(m.Groups[1].Value)) missing.Add(m.Groups[1].Value);
        Assert.Empty(missing);
    }

    [Fact]
    public void TechS1Pattern_SelectsEveryTechFrameOfTheSourceAndNothingElse_AndSkipsTheLegacyStage()
    {
        var only = new ShotFilter("tech_s1_*");
        var inSource = Regex.Matches(MainSource(), "Shot\\(\\$\"(tech_s1_[a-z0-9_]+)\\{sfx\\}\\.png\"").Select(m => m.Groups[1].Value).Distinct().ToArray();
        Assert.Equal(8, inSource.Length);
        Assert.Equal(inSource.OrderBy(x => x), ShotRegistry.Group("tech_s1").OrderBy(x => x));
        foreach (string name in inSource)
        {
            Assert.True(only.Wants(name + "_1920x1080"));
            Assert.True(only.WantsGroup("_1920x1080", ShotRegistry.Group("tech_s1")));
        }
        Assert.Equal(["tech_s1"], ShotRegistry.Plan(only));
        Assert.False(only.WantsLegacyStage);   // the first batch walked the whole first stage because the pattern had no size
    }

    [Fact]
    public void Plan_OfTheYearStepFrames_IsTheYearGroupOnly()
    {
        var only = new ShotFilter("tree_year_step2_1920x1080,tree_year_step5_1920x1080");
        Assert.Equal(["year_rings"], ShotRegistry.Plan(only));
        Assert.False(only.WantsLegacyStage);
    }

    [Theory]
    [InlineData("tree_*")]
    [InlineData("globe_panel")]
    [InlineData("time_day")]
    [InlineData("typo_frame")]
    public void LegacyFramesAndUnknownNames_KeepTheFirstStage(string pattern) => Assert.True(new ShotFilter(pattern).WantsLegacyStage);

    [Fact]
    public void NoFilter_PlansEveryGroup() => Assert.Equal(ShotRegistry.Groups.Length, ShotRegistry.Plan(null).Length);
}
