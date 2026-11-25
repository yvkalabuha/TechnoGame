using System;

namespace Singularity.Client.Spike;

/// <summary>Level of detail of the planisphere (tech-tree-scale.html): cells with counts, individual nodes, node icons.</summary>
public enum Lod { Overview, Nodes, Icons }

/// <summary>Zoom step of the tree (owner #473): 1 overview ... 6 maximum.</summary>
public static class ZoomSteps
{
    public const int Count = 6;
    public static readonly float[] Scales = { 1f, 1.8f, 3f, 5f, 8f, 12f };
    /// <summary>From this step: node names (3), icons (4), effect chips and year (5), edge labels (6).</summary>
    public const int Names = 3, Icons = 4, Effects = 5, EdgeLabels = 6;

    /// <summary>The step 1..6 a zoom factor belongs to: the highest step whose scale it has reached.</summary>
    public static int StepOf(float zoom)
    {
        int s = 1;
        for (int k = 1; k < Count; k++) if (zoom >= Scales[k] - 0.001f) s = k + 1;
        return s;
    }

    /// <summary>The scale of the next step up (+1) or down (-1) from a zoom factor; stays at the ends. A zoom between steps moves to the nearest step in that direction.</summary>
    public static float Next(float zoom, int dir)
    {
        if (dir > 0) { for (int k = 0; k < Count; k++) if (Scales[k] > zoom + 0.001f) return Scales[k]; return Scales[Count - 1]; }
        for (int k = Count - 1; k >= 0; k--) if (Scales[k] < zoom - 0.001f) return Scales[k];
        return Scales[0];
    }
}

/// <summary>Godot-free view math of the client (zoom about the cursor, pan limits, level of detail, shortest camera turn). Presentation only, not simulation.</summary>
public static class PlanisphereMath
{
    public const float MinZoom = 1f, MaxZoom = 12f;
    public const float NodesFrom = 1.8f, IconsFrom = 5f;

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

    /// <summary>Screen offset (pixels from the viewport centre; x right, y down) of a surface point of the unit globe for a camera at yaw/pitch (degrees) looking at the centre.</summary>
    public static (double X, double Y) Project(double yawDeg, double pitchDeg, double dist, double fovDeg, double viewportH, double lonDeg, double latDeg)
    {
        double y = yawDeg * Math.PI / 180, p = pitchDeg * Math.PI / 180, lo = lonDeg * Math.PI / 180, la = latDeg * Math.PI / 180;
        double cx = dist * Math.Cos(p) * Math.Sin(y), cy = dist * Math.Sin(p), cz = dist * Math.Cos(p) * Math.Cos(y);
        double len = Math.Sqrt(cx * cx + cy * cy + cz * cz);
        double fx = -cx / len, fy = -cy / len, fz = -cz / len;
        double rx = -fz, rz = fx, rl = Math.Sqrt(rx * rx + rz * rz);
        rx /= rl; rz /= rl;
        double ux = -rz * fy, uy = rz * fx - rx * fz, uz = rx * fy;   // up = right x forward
        double px = Math.Cos(la) * Math.Sin(lo) - cx, py = Math.Sin(la) - cy, pz = Math.Cos(la) * Math.Cos(lo) - cz;
        double depth = px * fx + py * fy + pz * fz;
        double k = viewportH / 2 / Math.Tan(fovDeg * Math.PI / 360) / depth;
        return ((px * rx + pz * rz) * k, -(px * ux + py * uy + pz * uz) * k);
    }

    public static double ProjectX(double yawDeg, double pitchDeg, double dist, double fovDeg, double viewportH, double lonDeg, double latDeg) =>
        Project(yawDeg, pitchDeg, dist, fovDeg, viewportH, lonDeg, latDeg).X;

    /// <summary>Camera yaw that puts the point (lon, lat) <paramref name="shiftPx"/> pixels left of the viewport centre, i.e. at the middle of the free area beside a panel
    /// that is 2 * shiftPx wide on the right. Zero shift gives the plain centring.</summary>
    public static float YawForShift(float lonDeg, float latDeg, float pitchDeg, float dist, float fovDeg, float viewportH, float shiftPx)
    {
        double lo = lonDeg - 70, hi = lonDeg + 70;   // the projected x falls as the yaw grows
        for (int i = 0; i < 40; i++)
        {
            double mid = (lo + hi) / 2;
            if (ProjectX(mid, pitchDeg, dist, fovDeg, viewportH, lonDeg, latDeg) > -shiftPx) lo = mid; else hi = mid;
        }
        return (float)((lo + hi) / 2);
    }

    /// <summary>Camera (yaw, pitch) that puts the point (lon, lat) <paramref name="shiftPx"/> pixels left of the viewport centre and at its vertical middle. The yaw is solved for the
    /// horizontal position, the pitch is corrected from the remaining vertical error (the sphere curves, so a pitch equal to the latitude is not enough away from the axis).</summary>
    public static (float Yaw, float Pitch) CameraForShift(float lonDeg, float latDeg, float dist, float fovDeg, float viewportH, float shiftPx)
    {
        float pitch = latDeg, yaw = lonDeg;
        double focal = viewportH / 2 / Math.Tan(fovDeg * Math.PI / 360);
        for (int i = 0; i < 8; i++)
        {
            yaw = YawForShift(lonDeg, latDeg, pitch, dist, fovDeg, viewportH, shiftPx);
            double y = Project(yaw, pitch, dist, fovDeg, viewportH, lonDeg, latDeg).Y;
            pitch = Math.Clamp(pitch - (float)(Math.Atan(y / focal) * 180 / Math.PI), -80f, 80f);
        }
        return (yaw, pitch);
    }
}
