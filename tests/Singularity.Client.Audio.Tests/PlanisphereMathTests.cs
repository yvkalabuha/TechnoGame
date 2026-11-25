using Singularity.Client.Spike;
using Xunit;

namespace Singularity.Client.Audio.Tests;

public class PlanisphereMathTests
{
    [Theory]
    [InlineData(1f, Lod.Overview)]
    [InlineData(1.79f, Lod.Overview)]
    [InlineData(1.8f, Lod.Nodes)]
    [InlineData(4.99f, Lod.Nodes)]
    [InlineData(5f, Lod.Icons)]
    [InlineData(12f, Lod.Icons)]
    public void Lod_follows_zoom(float zoom, Lod expected) => Assert.Equal(expected, PlanisphereMath.LodOf(zoom));

    [Fact]
    public void Zoom_is_clamped()
    {
        Assert.Equal(PlanisphereMath.MinZoom, PlanisphereMath.ClampZoom(0.2f));
        Assert.Equal(PlanisphereMath.MaxZoom, PlanisphereMath.ClampZoom(50f));
    }

    [Fact]
    public void Point_under_cursor_stays_put_when_zooming()
    {
        // Drawing centre c, a point at offset v from it, cursor on that point: after zooming the point is still at the cursor.
        float cx = 400, cy = 300, panX = 12, panY = -7, oldZ = 2, newZ = 3.5f;
        float vx = 90, vy = -40;                                   // point offset from the centre at zoom oldZ
        float ax = cx + vx, ay = cy + vy;
        var (px, py) = PlanisphereMath.PanAfterZoom(panX, panY, cx, cy, ax, ay, oldZ, newZ);
        float k = newZ / oldZ;
        float newCx = cx + (px - panX), newCy = cy + (py - panY);
        Assert.Equal(ax, newCx + vx * k, 3);
        Assert.Equal(ay, newCy + vy * k, 3);
    }

    [Fact]
    public void Pan_is_limited_by_radius()
    {
        var (x, y) = PlanisphereMath.ClampPan(300, 400, 250);
        Assert.Equal(250f, MathF.Sqrt(x * x + y * y), 2);
        Assert.Equal((10f, 20f), PlanisphereMath.ClampPan(10, 20, 250));
    }

    [Theory]
    [InlineData(10f, 350f, -20f)]
    [InlineData(350f, 10f, 20f)]
    [InlineData(-170f, 170f, -20f)]
    [InlineData(0f, 90f, 90f)]
    [InlineData(725f, 5f, 0f)]
    public void Camera_turns_the_short_way(float from, float to, float expected)
    {
        Assert.Equal(expected, PlanisphereMath.ShortestTurn(from, to), 3);
    }

    [Fact]
    public void Easing_is_frame_rate_independent()
    {
        float one = 1f - PlanisphereMath.EaseStep(0.1, 8f);
        float two = (1f - PlanisphereMath.EaseStep(0.05, 8f)) * (1f - PlanisphereMath.EaseStep(0.05, 8f));
        Assert.Equal(one, two, 4);
    }
}
