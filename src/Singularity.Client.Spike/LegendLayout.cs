using System;

namespace Singularity.Client.Spike;

/// <summary>Godot-free placement of the map key (art director, TASK-0142): top left of the map area under the top bar; when the left corner is taken (an area panel)
/// the top right; hidden when both are taken (the key never sits over a panel).</summary>
public static class LegendLayout
{
    public const float Margin = 8f;

    /// <summary>
    /// Height of the tree legend sheet from the line counts of its wrapped paragraphs (art director, TASK-0132 round 2: the counters line ran into the close line).
    /// Rows are 22 du apart from the heading; the right column ends with the root note, the keyboard-focus line, the counters line and the close line, the left one with the dial note.
    /// Returns the sheet height and the baseline of the close line, both from the sheet top.
    /// </summary>
    public static (float Height, float CloseBaseline) TreeLegendHeight(int rootLines, int focusLines, int countLines, int dialLines, float row = 22f, float pad = 12f, float line = 18f)
    {
        float ry = pad + 15 + 10 * row + 16;                 // the root row
        float y = ry + 19 + rootLines * line + 12;           // the keyboard-focus line
        y += focusLines * line + 8;                          // the counters line
        y += countLines * line;                              // its bottom
        float close = y + 14;                                // the close line, one gap below
        float left = pad + 15 + 13 * row + 8 + dialLines * line + 10;
        return (Math.Max(Math.Max(close + pad, left), 352f), close);
    }

    public static bool Place(float vpW, float mapLeft, float mapTop, float w, bool leftBusy, bool rightBusy, out float x, out float y)
    {
        y = mapTop + Margin; x = 0f;
        if (!leftBusy) { x = mapLeft + Margin; return true; }
        if (!rightBusy) { x = vpW - Margin - w; return true; }
        return false;
    }
}
