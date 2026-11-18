using Singularity.Client.Spike;
using Xunit;

namespace Singularity.Client.Audio.Tests;

public class SettingsLogicTests
{
    [Theory]
    [InlineData(80, 100)]
    [InlineData(100, 100)]
    [InlineData(105, 100)]
    [InlineData(107, 112.5)]
    [InlineData(125, 125)]
    [InlineData(149, 150)]
    [InlineData(300, 150)]
    public void Ui_scale_snaps_to_steps_of_12_5(double input, double expected) => Assert.Equal(expected, SettingsLogic.SnapUiScale(input));

    [Fact]
    public void Rollback_fires_once_after_the_countdown()
    {
        var t = new RollbackTimer(15);
        Assert.False(t.Tick(1));
        t.Start();
        Assert.False(t.Tick(14.9));
        Assert.True(t.Tick(0.2));
        Assert.False(t.Tick(1));
        Assert.False(t.Active);
    }

    [Fact]
    public void Confirm_stops_the_rollback()
    {
        var t = new RollbackTimer(15);
        t.Start(); t.Confirm();
        Assert.False(t.Tick(20));
    }

    [Fact]
    public void Alert_volume_never_goes_below_the_floor_by_the_shared_clamp() =>
        Assert.Equal(AudioSettings.AlertFloor, AudioSettings.Clamp(Bus.Alert, 0.05));

    [Theory]
    [InlineData(60f, 30f, 0f)]
    [InlineData(60f, 30f, 260f)]
    [InlineData(-90f, -20f, 380f)]
    [InlineData(10f, 60f, 270f)]
    [InlineData(37f, 55f, 270f)]
    public void Camera_for_shift_puts_the_point_at_the_free_area_centre(float lon, float lat, float shift)
    {
        float dist = 2.6f, fov = 35, h = 720;
        var (yaw, pitch) = PlanisphereMath.CameraForShift(lon, lat, dist, fov, h, shift);
        var (x, y) = PlanisphereMath.Project(yaw, pitch, dist, fov, h, lon, lat);
        Assert.InRange(x, -shift - 1, -shift + 1);
        Assert.InRange(y, -1, 1);
    }

    [Fact]
    public void Zero_shift_is_plain_centring()
    {
        var (yaw, pitch) = PlanisphereMath.CameraForShift(20f, 40f, 3f, 35, 900, 0f);
        Assert.Equal(20f, yaw, 2);
        Assert.Equal(40f, pitch, 2);
    }
}
