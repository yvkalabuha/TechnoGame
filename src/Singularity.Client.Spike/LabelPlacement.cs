namespace Singularity.Client.Spike;

/// <summary>Godot-free label placement near the screen edge (A9): the plate is moved inside the viewport, never hidden for touching the edge.</summary>
public static class LabelPlacement
{
    public const float Margin = 8f;

    /// <summary>
    /// Plate centred on (cx, cy) with size (w, h). Returns false (hide) only when its centre lies outside the visible area,
    /// that is when less than half of the plate would remain visible; otherwise the plate is shifted to keep Margin from every screen edge.
    /// </summary>
    public static bool Place(float cx, float cy, float w, float h, float vpW, float vpH, out float x, out float y)
    {
        x = cx - w * 0.5f; y = cy - h * 0.5f;
        if (w > vpW - 2 * Margin || h > vpH - 2 * Margin) return false;
        if (cx < 0 || cy < 0 || cx > vpW || cy > vpH) return false;
        x = System.Math.Clamp(x, Margin, vpW - Margin - w);
        y = System.Math.Clamp(y, Margin, vpH - Margin - h);
        return true;
    }
}
