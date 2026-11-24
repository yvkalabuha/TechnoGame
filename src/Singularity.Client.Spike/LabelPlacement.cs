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
    /// (Margin from every edge) and on the globe disc (centre gcx, gcy, radius gr). A moved plate must sit over its own land:
    /// <paramref name="onRegion"/> says whether the point of the globe under a screen point belongs to the label's region; when the clamped point does not,
    /// the search walks inward (the way the anchor lies off the frame, then on) with lateral probes. Hidden when no such point exists.
    /// Without <paramref name="onRegion"/> a moved plate is accepted within <paramref name="regionR"/> of the anchor. A plate that does not move stays on its anchor.
    /// </summary>
    public static bool PlaceInRegion(float cx, float cy, float w, float h, float vpW, float vpH, float regionR, float gcx, float gcy, float gr, System.Func<float, float, bool>? onRegion, out float x, out float y)
    {
        x = cx - w * 0.5f; y = cy - h * 0.5f;
        float minX = Margin + w * 0.5f, maxX = vpW - Margin - w * 0.5f, minY = Margin + h * 0.5f, maxY = vpH - Margin - h * 0.5f;
        if (maxX < minX || maxY < minY) return false;
        float qx = System.Math.Clamp(cx, minX, maxX), qy = System.Math.Clamp(cy, minY, maxY);
        float dx = qx - cx, dy = qy - cy, gx = qx - gcx, gy = qy - gcy;
        if (gx * gx + gy * gy > gr * gr) return false;
        float d2 = dx * dx + dy * dy;
        if (d2 > 0.25f)   // the plate has to move
        {
            if (onRegion == null) { if (d2 > regionR * regionR) return false; }
            else
            {
                float len = System.MathF.Sqrt(d2), ux = dx / len, uy = dy / len, vx = -uy, vy = ux;   // inward direction and its normal
                bool found = false;
                for (int k = 0; k <= ProbeSteps && !found; k++)
                    for (int l = 0; l < 7 && !found; l++)
                    {
                        float lat = ((l + 1) / 2) * ProbeStep * ((l & 1) == 1 ? 1 : -1), along = k * ProbeStep;
                        float px = System.Math.Clamp(qx + ux * along + vx * lat, minX, maxX), py = System.Math.Clamp(qy + uy * along + vy * lat, minY, maxY);
                        if (!onRegion(px, py) || !onRegion(System.Math.Clamp(px + ux * 12f, minX, maxX), System.Math.Clamp(py + uy * 12f, minY, maxY))) continue;
                        qx = px; qy = py; found = true;
                    }
                if (!found) found = FitOnGrid(w, h, minX, maxX, minY, maxY, cx, cy, gcx, gcy, gr, onRegion, ref qx, ref qy);
                if (!found) return false;
                gx = qx - gcx; gy = qy - gcy;
                if (gx * gx + gy * gy > gr * gr) return false;
            }
        }
        x = qx - w * 0.5f; y = qy - h * 0.5f;
        return true;
    }

    const float GridStep = 32f;

    /// <summary>
    /// Last resort of the search: every grid point of the safe rectangle on the globe disc; the nearest one to the anchor where the whole plate (centre and four corners)
    /// lies on the region wins, else the nearest one whose centre does. A bent or detached visible part of the territory is found wherever it is.
    /// </summary>
    static bool FitOnGrid(float w, float h, float minX, float maxX, float minY, float maxY, float ax, float ay, float gcx, float gcy, float gr,
        System.Func<float, float, bool> onRegion, ref float qx, ref float qy)
    {
        float bestFull = float.MaxValue, bestCentre = float.MaxValue, fx = 0, fy = 0, cx = 0, cy = 0;
        float hw = w * 0.5f, hh = h * 0.5f;
        for (float py = minY; py <= maxY + 0.01f; py += GridStep)
            for (float px = minX; px <= maxX + 0.01f; px += GridStep)
            {
                float gx = px - gcx, gy = py - gcy;
                if (gx * gx + gy * gy > gr * gr) continue;
                float d = (px - ax) * (px - ax) + (py - ay) * (py - ay);
                if (d >= bestCentre && d >= bestFull) continue;
                if (!onRegion(px, py)) continue;
                if (d < bestCentre) { bestCentre = d; cx = px; cy = py; }
                if (d < bestFull && onRegion(px - hw, py - hh) && onRegion(px + hw, py - hh) && onRegion(px - hw, py + hh) && onRegion(px + hw, py + hh)) { bestFull = d; fx = px; fy = py; }
            }
        if (bestFull < float.MaxValue) { qx = fx; qy = fy; return true; }
        if (bestCentre < float.MaxValue) { qx = cx; qy = cy; return true; }
        return false;
    }

    public enum PlateMode { Plain, Framed, Callout }
    public const float FramedBelow = 400f, CalloutBelow = 64f;   // du^2 on screen (spec task-0138-country-palette 3.7)

    const float DegPerRasterPx = 360f / 4096f;

    /// <summary>
    /// Raster pixels of a region as screen area in du^2 (1 du = 1 px at 100 %), from the projection at the label point: (exx, exy) and (eyx, eyy) are the screen
    /// vectors of one degree of longitude and of latitude there, so latitude and foreshortening at the limb are inside the number.
    /// </summary>
    public static float ScreenArea(double rasterPx, float exx, float exy, float eyx, float eyy) =>
        (float)rasterPx * DegPerRasterPx * DegPerRasterPx * System.Math.Abs(exx * eyy - exy * eyx);

    /// <summary>Radius in px of a disc of the same screen area, a little shrunk so that a long thin region does not claim too much.</summary>
    public static float RegionRadius(float screenArea) => 0.8f * System.MathF.Sqrt(screenArea / 3.14159265f);

    /// <summary>Under 64 du^2 the word hangs on a leader line, under 400 du^2 the plate gets a frame in the line tone, else it sits on the region.</summary>
    public static PlateMode ModeFor(float screenAreaDu2) => screenAreaDu2 < CalloutBelow ? PlateMode.Callout : screenAreaDu2 < FramedBelow ? PlateMode.Framed : PlateMode.Plain;

    /// <summary>Centre of a callout plate: up and to the right of the anchor, to the left when the right side would leave the frame.</summary>
    public static void CalloutCentre(float ax, float ay, float w, float h, float vpW, out float cx, out float cy)
    {
        const float gap = 22f;
        cx = ax + gap + w * 0.5f;
        if (cx + w * 0.5f > vpW - Margin) cx = ax - gap - w * 0.5f;
        cy = ay - gap - h * 0.5f;
        if (cy - h * 0.5f < Margin) cy = ay + gap + h * 0.5f;   // no room above (state near the top edge): hang below the anchor
    }
}
