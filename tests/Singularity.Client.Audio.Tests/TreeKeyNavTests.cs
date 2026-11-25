using Singularity.Client.Spike;
using Xunit;

namespace Singularity.Client.Audio.Tests;

public class TreeKeyNavTests
{
    static readonly int[] Order = { 4, 2, 7, 1 };

    [Fact]
    public void TabWalksTheOrderAndLeavesAtTheEnds()
    {
        Assert.Equal(4, TreeKeyNav.Step(Order, -1, 1, _ => true));
        Assert.Equal(1, TreeKeyNav.Step(Order, -1, -1, _ => true));
        Assert.Equal(7, TreeKeyNav.Step(Order, 2, 1, _ => true));
        Assert.Equal(-1, TreeKeyNav.Step(Order, 1, 1, _ => true));
        Assert.Equal(-1, TreeKeyNav.Step(Order, 4, -1, _ => true));
    }

    [Fact]
    public void TabSkipsHiddenNodes() => Assert.Equal(1, TreeKeyNav.Step(Order, 2, 1, r => r != 7));

    [Fact]
    public void ArrowPicksTheNearestAheadAndPrefersTheStraightOne()
    {
        float[] xs = { 0, 50, 100, 40, 0 }, ys = { 0, 0, 0, 30, -80 };
        Assert.Equal(1, TreeKeyNav.Direction(xs, ys, 0, 1, 0, _ => true));   // right: (50,0) beats (40,30)
        Assert.Equal(4, TreeKeyNav.Direction(xs, ys, 0, 0, -1, _ => true));   // up
        Assert.Equal(-1, TreeKeyNav.Direction(xs, ys, 0, -1, 0, _ => true));   // nothing to the left
        Assert.Equal(2, TreeKeyNav.Direction(xs, ys, 0, 1, 0, r => r != 1 && r != 3));
    }

    [Fact]
    public void AKeyboardZoomKeepsAFocusedNodeInViewByAnchoringOnIt()
    {
        // Area 0,44 960x1000; a focused node near the right edge: the zoom must stay fixed on it, not on the centre.
        var (x, y) = TreeKeyNav.ZoomAnchor(true, 900, 300, 0, 44, 960, 1000, 12);
        Assert.Equal((900f, 300f), (x, y));
        // a focus that is already out of view, or none, falls back to the centre
        Assert.Equal((480f, 544f), TreeKeyNav.ZoomAnchor(true, 2000, 300, 0, 44, 960, 1000, 12));
        Assert.Equal((480f, 544f), TreeKeyNav.ZoomAnchor(false, 900, 300, 0, 44, 960, 1000, 12));
    }

    [Fact]
    public void AFocusAtTheEdgeIsNotInView()
    {
        Assert.False(TreeKeyNav.Inside(955, 300, 0, 44, 960, 1000, 12));
        Assert.True(TreeKeyNav.Inside(500, 300, 0, 44, 960, 1000, 12));
    }

    [Fact]
    public void ASegmentThatClipsOnlyACornerIsAHit()
    {
        // Box (10,10)-(20,20); x+y=24 cuts its corner with a chord (10,14)-(14,10): the old 6 du sampling stepped over it.
        Assert.True(LabelGeometry.SegmentHitsRect(0, 24, 24, 0, 10, 10, 10, 10));
        Assert.False(LabelGeometry.SegmentHitsRect(0, 20, 20, 0, 10, 10, 10, 10));   // touches the corner point only
        Assert.False(LabelGeometry.SegmentHitsRect(0, 19, 19, 0, 10, 10, 10, 10));   // passes beside it
        Assert.True(LabelGeometry.SegmentHitsRect(0, 15, 30, 15, 10, 10, 10, 10));   // straight through
        Assert.False(LabelGeometry.SegmentHitsRect(5, 5, 5, 5, 0, 0, 10, 10));   // a point is no segment
    }
}
