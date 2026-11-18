using System;

namespace Singularity.Client.Spike;

/// <summary>Level of detail of the planisphere (tech-tree-scale.html): cells with counts, individual nodes, node icons.</summary>
public enum Lod { Overview, Nodes, Icons }

/// <summary>Godot-free view math of the client (zoom about the cursor, pan limits, level of detail, shortest camera turn). Presentation only, not simulation.</summary>
public static class PlanisphereMath
{
    public const float MinZoom = 1f, MaxZoom = 9f;
    public const float NodesFrom = 1.6f, IconsFrom = 3.4f;

    public static float ClampZoom(float z) => Math.Clamp(z, MinZoom, MaxZoom);

    public static Lod LodOf(float zoom) => zoom < NodesFrom ? Lod.Overview : zoom < IconsFrom ? Lod.Nodes : Lod.Icons;

    /// <summary>New pan so that the point under the anchor stays under it when the zoom changes from <paramref name="oldZ"/> to <paramref name="newZ"/>.
    /// The centre is the current centre of the drawing (base centre + pan).</summary>
    public static (float X, float Y) PanAfterZoom(float panX, float panY, float centreX, float centreY, float anchorX, float anchorY, float oldZ, float newZ)
    {
        float k = newZ / oldZ;
        return (panX + (anchorX - centreX) * (1f - k), panY + (anchorY - centreY) * (1f - k));
    }

    /// <summary>Keeps the drawing centre within <paramref name="limit"/> pixels of the base centre, so the rings cannot be dragged out of sight.</summary>
    public static (float X, float Y) ClampPan(float panX, float panY, float limit)
    {
        float len = MathF.Sqrt(panX * panX + panY * panY);
        if (len <= limit || len == 0f) return (panX, panY);
        float k = limit / len;
        return (panX * k, panY * k);
    }

    /// <summary>Signed shortest turn in degrees from <paramref name="from"/> to <paramref name="to"/>, in (-180, 180].</summary>
    public static float ShortestTurn(float from, float to)
    {
        float d = (to - from) % 360f;
        if (d > 180f) d -= 360f; else if (d <= -180f) d += 360f;
        return d;
    }

    /// <summary>Fraction of the remaining distance to cover this frame (frame-rate independent easing).</summary>
    public static float EaseStep(double dt, float rate) => 1f - (float)Math.Exp(-rate * Math.Max(0, dt));
}
