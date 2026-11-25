namespace Singularity.Client.Spike;

/// <summary>Game-speed steps (owner #481): pause, 1 day, 3 days, 1 week, 1 month per second. A tick is one day (#60). Godot-free, so that a test pins the table.</summary>
public static class SpeedSteps
{
    public const int Max = 4, Default = 2;
    public static readonly double[] TicksPerSecond = { 0, 1, 3, 7, 30 };
    public static readonly string[] Words = { "speed.pause", "speed.day", "speed.days3", "speed.week", "speed.month" };
    public static int Clamp(int s) => s < 0 ? 0 : s > Max ? Max : s;
}
