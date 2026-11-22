using System;

namespace Singularity.Client.Spike;

/// <summary>Godot-free rules of the settings screen: UI scale steps, upscale ratios, and the 15-second rollback timer of the video dialog (spec §4).</summary>
public static class SettingsLogic
{
    public const double UiScaleMin = 100, UiScaleMax = 150, UiScaleStep = 12.5;

    /// <summary>Old three-state video.window_mode (0 window, 1 borderless, 2 fullscreen) to the current display mode (0 borderless, 1 fullscreen); a framed window has no successor and becomes fullscreen.</summary>
    public static int MigrateWindowMode(int oldMode) => oldMode == 1 ? 0 : 1;

    /// <summary>Nearest allowed UI scale in percent: 100 to 150 in steps of 12.5.</summary>
    public static double SnapUiScale(double percent)
    {
        double snapped = Math.Round((Math.Clamp(percent, UiScaleMin, UiScaleMax) - UiScaleMin) / UiScaleStep) * UiScaleStep + UiScaleMin;
        return Math.Clamp(snapped, UiScaleMin, UiScaleMax);
    }

    /// <summary>Base layout (UI_DESIGN_SYSTEM 4.3): 1920x1080 du; 1 du is 1 px at this size and 100 %.</summary>
    public const int BaseWidth = 1920, BaseHeight = 1080;

    /// <summary>Physical pixels per du as the engine computes it (canvas_items, aspect expand): the smaller screen ratio times the UI scale. A window below the base scales down (tests only).</summary>
    public static double PixelsPerDu(int width, int height, double uiScalePercent) =>
        Math.Min(width / (double)BaseWidth, height / (double)BaseHeight) * uiScalePercent / 100.0;

    /// <summary>A1 floor: the UI never renders below 1 px/du at 100 % (below the px/du of its setting at a larger scale).</summary>
    public static bool ScaleFloorOk(int width, int height, double uiScalePercent) => PixelsPerDu(width, height, uiScalePercent) >= uiScalePercent / 100.0 - 1e-9;

    /// <summary>Canvas size in du for a window size and a UI scale (the extra width or height of a wider or taller screen goes to the content).</summary>
    public static (double W, double H) CanvasSize(int width, int height, double uiScalePercent)
    {
        double k = PixelsPerDu(width, height, uiScalePercent);
        return (width / k, height / k);
    }

    /// <summary>Side of the why-not root square in du (tree chart and legend): a meaningful icon is at least 16 physical px at 100 % (UI_DESIGN_SYSTEM 12).</summary>
    public const float WhyNotMarkDu = 16;

    /// <summary>Legend row of the why-not root: the swatch centre is at row y - 5; the square is centred on it (x, y, side).</summary>
    public static (float X, float Y, float Side) WhyNotLegendMark(float xl, float rowY) => (xl + 2, rowY - 5 - WhyNotMarkDu / 2, WhyNotMarkDu);

    /// <summary>A saved full frame must be a real base-size render; anything else is not evidence for A1/A2/A9.</summary>
    public static bool IsBaseFrame(int width, int height) => width == BaseWidth && height == BaseHeight;

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
