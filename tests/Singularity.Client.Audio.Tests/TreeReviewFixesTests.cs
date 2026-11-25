using System.Collections.Generic;
using System.IO;
using System.Linq;
using Singularity.Client.Spike;
using Xunit;

namespace Singularity.Client.Audio.Tests;

/// <summary>TASK-0161 Codex round 2: reduced motion and the 0.2 s zoom, the long-press tolerance, what blocks the menu node, the effect line words.</summary>
public class TreeReviewFixesTests
{
    [Fact]
    public void ReducedMotionJumpsStraightToTheTargetZoom() =>
        Assert.Equal(5.5f, PlanisphereMath.ZoomEase(1f, 5.5f, 1.0 / 60, animate: false));

    [Theory]
    [InlineData(1.0f, 1.8f)]
    [InlineData(1.0f, 12.0f)]
    [InlineData(12.0f, 1.0f)]
    public void TheZoomAnimationSettlesWithinTwoHundredMilliseconds(float from, float to)
    {
        float z = from; int frames = 0;
        while (z != to && frames < 100) { z = PlanisphereMath.ZoomEase(z, to, 1.0 / 60, true); frames++; }
        Assert.Equal(to, z);
        Assert.True(frames / 60.0 <= 0.2 + 1e-9, $"{frames} frames at 60 fps");
    }

    [Fact]
    public void AMoveInsideTheLongPressToleranceIsNeitherADragNorACancel()
    {
        for (float d = 0f; d <= 12f; d += 0.25f)
        {
            bool drag = NodeMenuLogic.IsDrag(d), hold = NodeMenuLogic.IsLongPress(0.6f, d);
            Assert.NotEqual(drag, hold);   // a held press is exactly one of: still a candidate for the menu, or a drag
        }
        Assert.False(NodeMenuLogic.IsDrag(5.5f)); Assert.True(NodeMenuLogic.IsLongPress(0.6f, 5.5f));   // the old 5 du drag threshold cancelled this
    }

    [Fact]
    public void OnlyGroupsWithNoKnownMemberBlockTheNode()
    {
        var groups = new List<int[]> { new[] { 1 }, new[] { 2, 3 }, new[] { 4, 5 } };
        var known = new HashSet<int> { 1, 5 };
        var unmet = TreeClarity.UnmetGroups(groups, known.Contains);
        Assert.Single(unmet); Assert.Equal(new[] { 2, 3 }, unmet[0]);
        Assert.Empty(TreeClarity.UnmetGroups(groups, _ => true));
    }

    static string Csv() { var d = new DirectoryInfo(System.AppContext.BaseDirectory); while (d != null && !File.Exists(Path.Combine(d.FullName, "src", "Singularity.Client.Spike", "locale", "ui.csv"))) d = d.Parent; return File.ReadAllText(Path.Combine(d!.FullName, "src", "Singularity.Client.Spike", "locale", "ui.csv")); }

    [Theory]
    [InlineData("fx.alt")] [InlineData("fx.alloy")] [InlineData("fx.process")] [InlineData("fx.mastery")] [InlineData("fx.gen")]
    [InlineData("fx.informs")] [InlineData("fx.required")] [InlineData("fx.path")] [InlineData("fx.more")]
    [InlineData("tree.effects.line")] [InlineData("inset.why.node")] [InlineData("inset.why.node.free")]
    public void TheNewLinesExistInRussian(string key)
    {
        var row = Csv().Split('\n').First(l => l.StartsWith(key + ","));
        Assert.Contains(row, row); Assert.True(row.Any(c => c >= 'А' && c <= 'я'), key);
    }
}
