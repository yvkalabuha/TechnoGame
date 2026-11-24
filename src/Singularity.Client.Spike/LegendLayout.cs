namespace Singularity.Client.Spike;

/// <summary>Godot-free placement of the map key (art director, TASK-0142): top left of the map area under the top bar; when the left corner is taken (an area panel)
/// the top right; hidden when both are taken (the key never sits over a panel).</summary>
public static class LegendLayout
{
    public const float Margin = 8f;

    public static bool Place(float vpW, float mapLeft, float mapTop, float w, bool leftBusy, bool rightBusy, out float x, out float y)
    {
        y = mapTop + Margin; x = 0f;
        if (!leftBusy) { x = mapLeft + Margin; return true; }
        if (!rightBusy) { x = vpW - Margin - w; return true; }
        return false;
    }
}
