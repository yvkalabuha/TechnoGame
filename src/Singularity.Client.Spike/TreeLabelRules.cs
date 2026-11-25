#nullable enable
using System;

namespace Singularity.Client.Spike;

/// <summary>A rectangle in du (Godot-free twin of Rect2) for the label rules below.</summary>
public readonly record struct LabelBox(float X, float Y, float W, float H)
{
    public float Right => X + W;
    public float Bottom => Y + H;
    public bool Encloses(LabelBox b) => b.X >= X && b.Y >= Y && b.Right <= Right && b.Bottom <= Bottom;
    public bool Intersects(LabelBox b) => X < b.Right && Right > b.X && Y < b.Bottom && Bottom > b.Y;
}

/// <summary>
/// Godot-free rules of where a label of the knowledge tree may stand (TASK-0161, art director round 5): a label is shown whole or not at all,
/// and never without its icon. Presentation only.
/// </summary>
public static class TreeLabelRules
{
    /// <summary>Paper field around the text of a plate, in du (C1/C5).</summary>
    public const float PlateField = 4f;
    /// <summary>Gap between an icon and the year written beside it, in du, counted from the edge of the drawn outline.</summary>
    public const float YearGap = 4f;

    /// <summary>A decade plate is drawn only whole: inside the map window, clear of the zoom panel and of every node mark (they are tested by the caller with <see cref="Clear"/>).</summary>
    public static bool PlateFits(LabelBox plate, LabelBox area, LabelBox keepOut) => area.Encloses(plate) && !keepOut.Intersects(plate);

    /// <summary>True when the plate does not touch the obstacle.</summary>
    public static bool Clear(LabelBox plate, LabelBox obstacle) => !plate.Intersects(obstacle);

    /// <summary>The shot-run glyph probe records a glyph only for a real node row: the legend draws samples with row -1, which have no node box.</summary>
    public static bool RecordsGlyph(bool probeOn, int row) => probeOn && row >= 0;

    /// <summary>A node name is drawn only when the icon of its node is fully visible in the window: a name whose icon is cut off by the edge would hang alone.</summary>
    public static bool IconVisible(float cx, float cy, float half, LabelBox area) => area.Encloses(new LabelBox(cx - half, cy - half, half * 2, half * 2));

    /// <summary>
    /// Shifts a label into the window (margin from each edge). False (do not draw) when it is wider or taller than the window allows,
    /// or when it would have to move farther than <paramref name="maxShift"/> (then it would stand far from what it names).
    /// </summary>
    public static bool ClampInto(LabelBox area, float margin, LabelBox label, float maxShift, out LabelBox result)
    {
        result = label;
        float minX = area.X + margin, maxX = area.Right - margin - label.W, minY = area.Y + margin, maxY = area.Bottom - margin - label.H;
        if (maxX < minX || maxY < minY) return false;
        float nx = Math.Clamp(label.X, minX, maxX), ny = Math.Clamp(label.Y, minY, maxY);
        if (Math.Abs(nx - label.X) > maxShift || Math.Abs(ny - label.Y) > maxShift) return false;
        result = new LabelBox(nx, ny, label.W, label.H);
        return true;
    }

    /// <summary>The plate of the "now" label for a text of the given size: the text plus <see cref="PlateField"/> on every side.</summary>
    public static (float W, float H) NowPlateSize(float textW, float textH) => (textW + 2 * PlateField + 2, textH + 2 * PlateField);
}
