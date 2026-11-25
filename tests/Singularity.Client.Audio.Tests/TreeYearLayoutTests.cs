using System;
using System.Linq;
using Singularity.Client.Spike;
using Xunit;

namespace Singularity.Client.Audio.Tests;

public class TreeYearLayoutTests
{
    [Fact]
    public void RadiusGrowsWithTheYearAndIsClampedAtTheEnds()
    {
        Assert.Equal(TreeYearLayout.Inner - TreeYearLayout.RootsBand, TreeYearLayout.RadiusOf(1900), 4);
        Assert.Equal(TreeYearLayout.RadiusOf(1900), TreeYearLayout.RadiusOf(1850), 4);
        Assert.Equal(TreeYearLayout.Inner, TreeYearLayout.RadiusOf(1945), 4);
        Assert.Equal(TreeYearLayout.Edge, TreeYearLayout.RadiusOf(2040), 4);
        Assert.Equal(TreeYearLayout.Edge, TreeYearLayout.RadiusOf(2100), 4);
        for (int y = 1900; y < 2040; y++) Assert.True(TreeYearLayout.RadiusOf(y + 1) > TreeYearLayout.RadiusOf(y), $"year {y}");
    }

    [Fact]
    public void TheRootsBandGivesTheYearsJustBefore1945MoreRoomThanTheOldOnes()
    {
        float Step(int y) => TreeYearLayout.RadiusOf(y + 1) - TreeYearLayout.RadiusOf(y);
        Assert.True(Step(1944) > Step(1930) && Step(1930) > Step(1905));   // square-root scale: the finest spread nearest 1945
        // the 1940-44 nodes (the crowded ones) get a band of at least a quarter of a unit, where a linear scale gave them 0.05
        Assert.True(TreeYearLayout.RadiusOf(1945) - TreeYearLayout.RadiusOf(1940) > 0.25f);
    }

    [Fact]
    public void DecadeRingsFromTheNineteenFiftiesAreEquallySpaced()
    {
        var r = TreeYearLayout.RingYears.Select(y => TreeYearLayout.RadiusOf(y)).ToArray();
        for (int k = 2; k < r.Length; k++) Assert.Equal(r[1] - r[0], r[k] - r[k - 1], 4);
        Assert.Equal(1950, TreeYearLayout.RingYears[0]); Assert.Equal(2030, TreeYearLayout.RingYears[^1]);
    }

    [Fact]
    public void YearAtInvertsRadiusOf() { foreach (int y in new[] { 1904, 1930, 1943, 1950, 1987, 2021 }) Assert.Equal(y, TreeYearLayout.YearAt(TreeYearLayout.RadiusOf(y)), 2); }

    [Fact]
    public void SectorsShareTheCircleClockwiseFromTheTop()
    {
        Assert.Equal(72f, TreeYearLayout.SectorWidth(5), 4);
        Assert.Equal(-90f, TreeYearLayout.SectorStart(0, 5), 4);
        Assert.Equal(-18f, TreeYearLayout.SectorStart(1, 5), 4);
        Assert.Equal(-54f, TreeYearLayout.SectorMid(0, 5), 4);
    }

    [Fact]
    public void ANewSectorIsAppendedAfterTheOthers()
    {
        Assert.Equal(60f, TreeYearLayout.SectorWidth(6), 4);
        Assert.Equal(TreeYearLayout.SectorStart(0, 5), TreeYearLayout.SectorStart(0, 6), 4);   // the first one keeps its start
        Assert.Equal(270f, TreeYearLayout.SectorStart(5, 6) + 60f, 4);                          // the new one ends where the circle closes
    }

    [Fact]
    public void EverySectorHasItsOwnHatch()
    {
        var all = Enumerable.Range(0, 5).Select(TreeYearLayout.HatchOf).ToArray();
        Assert.Equal(5, all.Distinct().Count());
        // distinct directions: no two kinds share the same set of line normals
        Assert.Equal(5, all.Select(k => string.Join(";", TreeYearLayout.HatchNormals(k))).Distinct().Count());
        Assert.True(TreeYearLayout.HatchAlpha >= 0.5f && TreeYearLayout.HatchSpacing <= 6f);
    }

    static (float[] r, float[] a) Place(int[] year, int[] sector, int[] number, int sectors, float margin = 0.08f, float gap = 0.3f)
    {
        var r = new float[year.Length]; var a = new float[year.Length];
        TreeYearLayout.Place(year, sector, number, sectors, margin, gap, r, a);
        return (r, a);
    }

    [Fact]
    public void PlaceKeepsRadiusFromTheYearAndAngleInsideTheSector()
    {
        var year = new[] { 1940, 1953, 1953, 1967, 2001, 1949, 2025 }; var sec = new[] { 0, 1, 1, 2, 4, 3, 0 }; var num = new[] { 1, 2, 3, 4, 5, 6, 7 };
        var (r, a) = Place(year, sec, num, 5);
        for (int i = 0; i < year.Length; i++)
        {
            Assert.Equal(TreeYearLayout.RadiusOf(year[i]), r[i], 4);
            float s0 = TreeYearLayout.SectorStart(sec[i], 5), w = TreeYearLayout.SectorWidth(5);
            Assert.InRange(a[i], s0 + w * 0.08f, s0 + w * 0.92f);
        }
    }

    [Fact]
    public void NodesOfOneYearAndSectorDoNotOverlap()
    {
        int n = 6; var year = Enumerable.Repeat(1960, n).ToArray(); var sec = new int[n]; var num = Enumerable.Range(1, n).ToArray();
        var (r, a) = Place(year, sec, num, 5);
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                double xi = r[i] * Math.Cos(a[i] * Math.PI / 180), yi = r[i] * Math.Sin(a[i] * Math.PI / 180), xj = r[j] * Math.Cos(a[j] * Math.PI / 180), yj = r[j] * Math.Sin(a[j] * Math.PI / 180);
                Assert.True(Math.Sqrt((xi - xj) * (xi - xj) + (yi - yj) * (yi - yj)) >= 0.3 - 1e-4, $"nodes {i} and {j} overlap");
            }
    }

    [Fact]
    public void PlaceIsDeterministicAndIgnoresTheInputOrder()
    {
        var year = new[] { 1950, 1950, 1951, 1960 }; var sec = new[] { 1, 1, 1, 1 }; var num = new[] { 9, 3, 5, 1 };
        var (r1, a1) = Place(year, sec, num, 5); var (r2, a2) = Place(year, sec, num, 5);
        Assert.Equal(a1, a2); Assert.Equal(r1, r2);
        // the same nodes given in another order get the same angles (the order inside a sector is (year, number))
        var (r3, a3) = Place(new[] { 1960, 1951, 1950, 1950 }, new[] { 1, 1, 1, 1 }, new[] { 1, 5, 3, 9 }, 5);
        Assert.Equal(a1[3], a3[0], 4); Assert.Equal(a1[2], a3[1], 4); Assert.Equal(a1[1], a3[2], 4); Assert.Equal(a1[0], a3[3], 4);
    }

    [Fact]
    public void ACrowdedYearStillFillsEverySlotWithoutFailing()
    {
        int n = 40; var year = Enumerable.Repeat(1946, n).ToArray(); var sec = new int[n]; var num = Enumerable.Range(1, n).ToArray();
        var (r, a) = Place(year, sec, num, 5);
        Assert.All(a, x => Assert.InRange(x, -90f, -18f));
    }

    [Theory]
    [InlineData(66f, 360f)] [InlineData(800f, 360f)] [InlineData(5000f, 360f)] [InlineData(5000f, 60f)]
    public void ArcChordsStayShortSoTheSagIsInvisible(float radius, float span)
    {
        int pts = TreeYearLayout.ArcPoints(radius, span);
        double chord = radius * span * Math.PI / 180 / pts, sag = chord * chord / (8 * radius);
        Assert.InRange(pts, 8, 1024);
        if (pts < 1024) Assert.True(sag < 0.6, $"sag {sag:F2} px");
    }

    [Theory]
    [InlineData("Ink2")] [InlineData("Ink3")]
    public void SecondaryTextHolds4Point5OnPaperAndSheet(string which)
    {
        string ink = which == "Ink2" ? Palette.Ink2 : Palette.Ink3;
        foreach (var bg in new[] { Palette.C.Paper, Palette.C.Sheet })
            Assert.True(ColorMath.Contrast(ColorMath.FromHex(ink), ColorMath.FromHex(bg)) >= 4.5, $"{which} on {bg}");
    }

    [Fact]
    public void GridContrastOnPaperIsAtMost1Point6()
    {
        // The grid is Palette.Rule at TreeYearLayout.GridAlpha over the paper (TechTree.GridColor); the Gemini finding asks for 1.6:1 at most.
        static int Ch(string hex, int k) => Convert.ToInt32(hex.Substring(k * 2, 2), 16);
        string paper = Palette.C.Paper, rule = Palette.Rule;
        string mixed = string.Concat(Enumerable.Range(0, 3).Select(k => ((int)Math.Round(Ch(rule, k) * TreeYearLayout.GridAlpha + Ch(paper, k) * (1 - TreeYearLayout.GridAlpha))).ToString("X2")));
        double c = ColorMath.Contrast(ColorMath.FromHex(mixed), ColorMath.FromHex(paper));
        Assert.True(c <= 1.6, $"grid contrast {c:F2}");
    }
}

public class ZoomStepsTests
{
    [Fact]
    public void SixStepsFromOneToTwelve()
    {
        Assert.Equal(6, ZoomSteps.Count); Assert.Equal(1f, ZoomSteps.Scales[0]); Assert.Equal(PlanisphereMath.MaxZoom, ZoomSteps.Scales[^1]);
        for (int k = 0; k < 6; k++) Assert.Equal(k + 1, ZoomSteps.StepOf(ZoomSteps.Scales[k]));
    }

    [Fact]
    public void AWheelZoomBetweenStepsBelongsToTheLowerOne()
    {
        Assert.Equal(1, ZoomSteps.StepOf(1.25f)); Assert.Equal(1, ZoomSteps.StepOf(1.5625f)); Assert.Equal(2, ZoomSteps.StepOf(1.953f));
        Assert.Equal(3, ZoomSteps.StepOf(4.9f)); Assert.Equal(6, ZoomSteps.StepOf(12f));
    }

    [Fact]
    public void ButtonsMoveOneStepAndStopAtTheEnds()
    {
        Assert.Equal(1.8f, ZoomSteps.Next(1f, 1)); Assert.Equal(12f, ZoomSteps.Next(12f, 1)); Assert.Equal(1f, ZoomSteps.Next(1f, -1));
        Assert.Equal(3f, ZoomSteps.Next(1.95f, 1)); Assert.Equal(1.8f, ZoomSteps.Next(1.95f, -1));   // from between steps: the nearest one in that direction
        Assert.Equal(3f, ZoomSteps.Next(5f, -1));
    }

    [Fact]
    public void LevelsOfDetailFollowTheSteps()
    {
        Assert.Equal(Lod.Overview, PlanisphereMath.LodOf(ZoomSteps.Scales[0]));
        Assert.Equal(Lod.Nodes, PlanisphereMath.LodOf(ZoomSteps.Scales[1])); Assert.Equal(Lod.Nodes, PlanisphereMath.LodOf(ZoomSteps.Scales[2]));
        Assert.Equal(Lod.Icons, PlanisphereMath.LodOf(ZoomSteps.Scales[3]));   // icons from step 4
        Assert.Equal(12f, PlanisphereMath.ClampZoom(40f));
    }
}

public class NodeMenuLogicTests
{
    static NodeMenuLogic.Ctx C(bool known = true, bool process = true, bool own = true, bool inFocus = false, int count = 0) => new(known, process, own, false, inFocus, count);

    [Fact]
    public void TheMenuHasTheFourItemsInOrder()
    {
        var m = NodeMenuLogic.Items(C());
        Assert.Equal(new[] { NodeAction.SetGoal, NodeAction.MasteryFocus, NodeAction.WhyBlocked, NodeAction.ShowLinks }, m.Select(i => i.Action).ToArray());
    }

    [Fact]
    public void SetGoalIsUnavailableWithAReasonWhileTheSessionCannotTakeIt()
    {
        var m = NodeMenuLogic.Items(C());
        Assert.False(m[0].Enabled); Assert.Equal("menu.why.goal", m[0].WhyKey);
        Assert.True(NodeMenuLogic.Items(new NodeMenuLogic.Ctx(true, true, true, true, false, 0))[0].Enabled);
    }

    [Theory]
    [InlineData(false, true, true, 0, "menu.why.focus.process")]
    [InlineData(true, false, true, 0, "menu.why.focus.process")]
    [InlineData(true, true, false, 0, "menu.why.focus.power")]
    [InlineData(true, true, true, 3, "menu.why.focus.full")]
    [InlineData(true, true, true, 2, null)]
    public void FocusNeedsAKnownProcessOfOwnPowerAndRoom(bool known, bool process, bool own, int count, string? why)
    {
        Assert.Equal(why, NodeMenuLogic.Items(C(known, process, own, false, count))[1].WhyKey);
    }

    [Fact]
    public void ANodeAlreadyInTheFocusCanBeTakenOutEvenWhenTheFocusIsFull()
    {
        var item = NodeMenuLogic.Items(C(inFocus: true, count: 3))[1];
        Assert.True(item.Enabled); Assert.Equal("menu.focus.remove", item.LabelKey);
    }

    [Fact]
    public void WhatBlocksItIsForUnknownNodesOnlyAndLinksAreAlwaysThere()
    {
        Assert.Equal("menu.why.blocked.known", NodeMenuLogic.Items(C(known: true))[2].WhyKey);
        Assert.True(NodeMenuLogic.Items(C(known: false))[2].Enabled);
        Assert.True(NodeMenuLogic.Items(C(false, false, false))[3].Enabled);
    }

    [Fact]
    public void EveryUnavailableItemSaysWhy() { foreach (var c in new[] { C(), C(false), C(own: false), C(count: 3) }) Assert.All(NodeMenuLogic.Items(c), i => Assert.Equal(i.Enabled, i.WhyKey == null)); }

    [Fact]
    public void LongPressNeedsHalfASecondAndAlmostNoMovement()
    {
        Assert.False(NodeMenuLogic.IsLongPress(0.49f, 0f));
        Assert.True(NodeMenuLogic.IsLongPress(0.5f, 0f));
        Assert.True(NodeMenuLogic.IsLongPress(0.8f, 6f));
        Assert.False(NodeMenuLogic.IsLongPress(0.8f, 6.1f));
    }

    [Fact]
    public void KeyboardSkipsDisabledItemsAndWraps()
    {
        var m = NodeMenuLogic.Items(C());   // goal disabled, focus enabled, blocked disabled (known), links enabled
        Assert.Equal(1, NodeMenuLogic.NextEnabled(m, -1, 1));
        Assert.Equal(3, NodeMenuLogic.NextEnabled(m, 1, 1));
        Assert.Equal(1, NodeMenuLogic.NextEnabled(m, 3, 1));
        Assert.Equal(1, NodeMenuLogic.NextEnabled(m, 3, -1));
    }
}
