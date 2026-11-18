using System;

namespace Singularity.Client.Spike;

/// <summary>Godot-free rules of the settings screen: UI scale steps, upscale ratios, and the 15-second rollback timer of the video dialog (spec §4).</summary>
public static class SettingsLogic
{
    public const double UiScaleMin = 100, UiScaleMax = 150, UiScaleStep = 12.5;

    /// <summary>Nearest allowed UI scale in percent: 100 to 150 in steps of 12.5.</summary>
    public static double SnapUiScale(double percent)
    {
        double snapped = Math.Round((Math.Clamp(percent, UiScaleMin, UiScaleMax) - UiScaleMin) / UiScaleStep) * UiScaleStep + UiScaleMin;
        return Math.Clamp(snapped, UiScaleMin, UiScaleMax);
    }

    /// <summary>3D render scale for the upscale mode (Quality, Balance, Performance), as in FSR: 1/1.5, 1/1.7, 1/2.</summary>
    public static double RenderScale(int mode) => mode switch { 0 => 0.667, 1 => 0.588, _ => 0.5 };
}

/// <summary>«Keep these settings? Reverting in N s»: counts down and reports when the previous values must be restored.</summary>
public sealed class RollbackTimer
{
    public double Remaining { get; private set; }
    public bool Active { get; private set; }

    public RollbackTimer(double seconds = 15) { Seconds = seconds; }
    public double Seconds { get; }

    public void Start() { Active = true; Remaining = Seconds; }
    public void Confirm() { Active = false; }

    /// <summary>Advances the timer; returns true exactly once, when it runs out and the old settings must come back.</summary>
    public bool Tick(double dt)
    {
        if (!Active) return false;
        Remaining -= dt;
        if (Remaining > 0) return false;
        Active = false; Remaining = 0;
        return true;
    }
}
