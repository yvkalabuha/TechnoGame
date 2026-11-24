namespace Singularity.Client.Spike;

/// <summary>Godot-free label placement near the screen edge (A9): the plate is moved inside the viewport, never hidden for touching the edge.</summary>
public static class LabelPlacement
{
    public const float Margin = 8f;
    const int ProbeSteps = 40;
    const float ProbeStep = 8f;

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

    /// <summary>
    /// Plate for a region whose anchor may lie outside the frame (G4): the plate centre is moved to the nearest point of the safe rectangle
    /// (Margin from every edge) and stays on the label when that point is still inside the region (distance to the anchor within regionR)
    /// and on the globe disc (centre gcx, gcy, radius gr). The centre counts as inside the region when <paramref name="onRegion"/> says that the point
    /// of the globe under it belongs to the region (a long strip such as Sweden has a far anchor but a large visible part), or else by the radius.
    /// Hidden only when the visible part of the region cannot hold the plate.
    /// </summary>
    public static bool PlaceInRegion(float cx, float cy, float w, float h, float vpW, float vpH, float regionR, float gcx, float gcy, float gr, System.Func<float, float, bool>? onRegion, out float x, out float y)
    {
        x = cx - w * 0.5f; y = cy - h * 0.5f;
        float minX = Margin + w * 0.5f, maxX = vpW - Margin - w * 0.5f, minY = Margin + h * 0.5f, maxY = vpH - Margin - h * 0.5f;
        if (maxX < minX || maxY < minY) return false;
        float qx = System.Math.Clamp(cx, minX, maxX), qy = System.Math.Clamp(cy, minY, maxY);
        float dx = qx - cx, dy = qy - cy, gx = qx - gcx, gy = qy - gcy;
        if (gx * gx + gy * gy > gr * gr) return false;
        if (dx * dx + dy * dy > regionR * regionR)
        {
            if (onRegion == null) return false;
            // Walk inward from the clamped point (the way the anchor lies off the frame, then the same way on) with lateral probes, until the plate centre
            // and a point a little further in are on the region.
            float len = System.MathF.Sqrt(dx * dx + dy * dy);
            if (len < 1e-3f) return false;
            float ux = dx / len, uy = dy / len, vx = -uy, vy = ux;   // inward direction and its normal
            bool found = false;
            for (int k = 0; k <= ProbeSteps && !found; k++)
                for (int l = 0; l < 7 && !found; l++)
                {
                    float lat = ((l + 1) / 2) * ProbeStep * ((l & 1) == 1 ? 1 : -1), along = k * ProbeStep;
                    float px = System.Math.Clamp(qx + ux * along + vx * lat, minX, maxX), py = System.Math.Clamp(qy + uy * along + vy * lat, minY, maxY);
                    if (!onRegion(px, py) || !onRegion(System.Math.Clamp(px + ux * 12f, minX, maxX), System.Math.Clamp(py + uy * 12f, minY, maxY))) continue;
                    qx = px; qy = py; found = true;
                }
            if (!found) return false;
            gx = qx - gcx; gy = qy - gcy;
            if (gx * gx + gy * gy > gr * gr) return false;
        }
        x = qx - w * 0.5f; y = qy - h * 0.5f;
        return true;
    }

    public enum PlateMode { Plain, Framed, Callout }
    public const float FramedBelow = 400f, CalloutBelow = 64f;   // du^2 on screen (spec task-0138-country-palette 3.7)

    /// <summary>Raster pixels of a region as screen area in du^2 (1 du = 1 px at 100 %): a raster pixel spans PI/2048 rad of the globe, foreshortened by cosTo.</summary>
    public static float ScreenArea(double rasterPx, float globeRadiusPx, float cosTo)
    {
        float s = globeRadiusPx * 3.14159265f / 2048f;
        return (float)rasterPx * s * s * System.Math.Max(cosTo, 0f);
    }

    /// <summary>Radius in px of a disc of the same screen area as the region, a little shrunk so that a long thin region does not claim too much.</summary>
    public static float RegionRadius(double rasterPx, float globeRadiusPx)
    {
        float s = globeRadiusPx * 3.14159265f / 2048f;
        return 0.8f * s * (float)System.Math.Sqrt(rasterPx / 3.14159265);
    }

    /// <summary>Under 64 du^2 the word hangs on a leader line, under 400 du^2 the plate gets a frame in the line tone, else it sits on the region.</summary>
    public static PlateMode ModeFor(float screenAreaDu2) => screenAreaDu2 < CalloutBelow ? PlateMode.Callout : screenAreaDu2 < FramedBelow ? PlateMode.Framed : PlateMode.Plain;

    /// <summary>Centre of a callout plate: up and to the right of the anchor, to the left when the right side would leave the frame.</summary>
    public static void CalloutCentre(float ax, float ay, float w, float h, float vpW, out float cx, out float cy)
    {
        const float gap = 22f;
        cx = ax + gap + w * 0.5f;
        if (cx + w * 0.5f > vpW - Margin) cx = ax - gap - w * 0.5f;
        cy = ay - gap - h * 0.5f;
    }
}
