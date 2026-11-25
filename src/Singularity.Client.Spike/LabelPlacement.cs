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

    public enum PlateMode { Plain, Callout }
    /// <summary>Screen area in du^2: under CalloutBelow the word hangs on a leader line, under HiddenBelow there is no label at all (it appears when the player zooms in).</summary>
    public const float CalloutBelow = 400f, HiddenBelow = 16f;
    /// <summary>A country whose word does not fit on its land hangs on a leader line from this screen area up (Denmark); below it the name waits for a zoom.</summary>
    public const float CalloutFallbackFrom = 60f;
    public const float MinFont = 12f, NominalFont = 14f;

    const float DegPerRasterPx = 360f / 4096f;

    /// <summary>
    /// Raster pixels of a region as screen area in du^2 (1 du = 1 px at 100 %), from the projection at the label point: (exx, exy) and (eyx, eyy) are the screen
    /// vectors of one degree of longitude and of latitude there, so latitude and foreshortening at the limb are inside the number.
    /// </summary>
    public static float ScreenArea(double rasterPx, float exx, float exy, float eyx, float eyy) =>
        (float)rasterPx * DegPerRasterPx * DegPerRasterPx * System.Math.Abs(exx * eyy - exy * eyx);

    /// <summary>Radius in px of a disc of the same screen area, a little shrunk so that a long thin region does not claim too much.</summary>
    public static float RegionRadius(float screenArea) => 0.8f * System.MathF.Sqrt(screenArea / 3.14159265f);

    /// <summary>Small regions get a leader line, the rest carry the word on the land (no plate in either case, #523).</summary>
    public static PlateMode ModeFor(float screenAreaDu2) => screenAreaDu2 < CalloutBelow ? PlateMode.Callout : PlateMode.Plain;

    public const int MaxFontPx = 28, BaseFontPx = 14;
    const float SizeFromExtent = 90f, SizeToExtent = 600f;   // sqrt(area) in du that maps to the smallest and the largest preferred size

    /// <summary>Preferred font size from the projected extent of the region: 14 px up to 28 px by sqrt(area); the fit search may go down to MinFont.</summary>
    public static int FontSizeFor(float screenAreaDu2)
    {
        float t = System.Math.Clamp((System.MathF.Sqrt(System.Math.Max(screenAreaDu2, 0f)) - SizeFromExtent) / (SizeToExtent - SizeFromExtent), 0f, 1f);
        return (int)System.MathF.Round(BaseFontPx + t * (MaxFontPx - BaseFontPx));
    }

    /// <summary>Next smaller size to try when the word does not fit: halfway to 14, then 14, 13, 12; 0 when nothing is left.</summary>
    public static int NextSize(int s) => s >= 20 ? (s + BaseFontPx) / 2 : s > BaseFontPx ? BaseFontPx : s > (int)MinFont ? s - 1 : 0;

    /// <summary>Hysteresis (#523): a hidden name must fit as it is to appear, a shown one stays while it fits at 85 % of its size.</summary>
    public static float FitScale(bool wasShown) => wasShown ? 0.85f : 1.0f;

    /// <summary>Cheap pre-test: a text box of w x h can only sit in a region whose projected area is clearly larger than the box.</summary>
    public static bool MayFit(float screenAreaDu2, float w, float h) => w * h <= 0.6f * screenAreaDu2;

    /// <summary>The text box centred on (cx, cy) lies inside the region: centre, four corners and four edge midpoints are on its land.</summary>
    public static bool FitsInside(float cx, float cy, float w, float h, System.Func<float, float, bool> onRegion)
    {
        // 5 x 3 probes: a thin gap or a notch of the coast under the word (Italy, British West Africa) is caught between the corners.
        float hw = w * 0.5f, hh = h * 0.5f;
        for (int j = -1; j <= 1; j++)
            for (int i = -2; i <= 2; i++)
                if (!onRegion(cx + i * 0.5f * hw, cy + j * hh)) return false;
        return true;
    }

    /// <summary>All four corners of the box centred on (cx, cy) with half sizes (hw, hh) lie within radius r of (gcx, gcy).</summary>
    public static bool InsideDisc(float cx, float cy, float hw, float hh, float gcx, float gcy, float r)
    {
        float dx = System.MathF.Abs(cx - gcx) + hw, dy = System.MathF.Abs(cy - gcy) + hh;
        return dx * dx + dy * dy <= r * r;
    }

    const int SearchR = 6;
    static readonly (int I, int J)[] Offsets = BuildOffsets();
    static (int, int)[] BuildOffsets()
    {
        var l = new System.Collections.Generic.List<(int I, int J)>();
        for (int j = -SearchR; j <= SearchR; j++) for (int i = -SearchR; i <= SearchR; i++) l.Add((i, j));
        l.Sort((a, b) => { int c = (a.I * a.I + a.J * a.J).CompareTo(b.I * b.I + b.J * b.J); return c != 0 ? c : a.J != b.J ? a.J.CompareTo(b.J) : a.I.CompareTo(b.I); });
        return l.ToArray();
    }

    /// <summary>
    /// Inscribed text rectangle (#523): the grid point nearest to the anchor where a box of fit x (w, h) lies wholly on the region. The anchor is first clamped into the safe
    /// rectangle (Margin from every edge for the true box w x h), so a region partly off the frame is searched where it is visible. No allocation.
    /// </summary>
    public static bool FindSpot(float ax, float ay, float w, float h, float fit, float pad, float gcx, float gcy, float limbR, float vpW, float vpH, float step, System.Func<float, float, bool> onRegion, out float cx, out float cy)
    {
        cx = cy = 0;
        float minX = Margin + w * 0.5f, maxX = vpW - Margin - w * 0.5f, minY = Margin + h * 0.5f, maxY = vpH - Margin - h * 0.5f;
        if (maxX < minX || maxY < minY) return false;
        float sx = System.Math.Clamp(ax, minX, maxX), sy = System.Math.Clamp(ay, minY, maxY);
        foreach (var (i, j) in Offsets)
        {
            float px = sx + i * step, py = sy + j * step;
            if (px < minX || px > maxX || py < minY || py > maxY) continue;
            if (!InsideDisc(px, py, w * 0.5f, h * 0.5f, gcx, gcy, limbR)) continue;   // the word keeps off the limb, where the land is foreshortened to a sliver
            if (!FitsInside(px, py, w * fit + pad, h * fit + pad, onRegion)) continue;   // pad: the coast stroke is drawn over the edge of the land
            cx = px; cy = py; return true;
        }
        return false;
    }

    /// <summary>Screen rectangle of a placed label (Godot-free twin of Rect2).</summary>
    public readonly record struct Box(float X, float Y, float W, float H)
    {
        public bool Intersects(Box o) => X < o.X + o.W && o.X < X + W && Y < o.Y + o.H && o.Y < Y + H;
        public Box Grow(float g) => new(X - g, Y - g, W + 2 * g, H + 2 * g);
    }

    public const float LeaderGap = 22f, LimbKeep = 0.95f;
    // Preferred directions of a leader (unit vectors, 8 of them): up-right first, as before.
    static readonly (float X, float Y)[] Dirs = { (0.7071f, -0.7071f), (1f, 0f), (0f, -1f), (0.7071f, 0.7071f), (-0.7071f, -0.7071f), (-1f, 0f), (0f, 1f), (-0.7071f, 0.7071f) };
    static readonly float[] LengthSteps = { 1f, 1.5f, 2f };

    /// <summary>
    /// Leader label (small countries): the text box centre is searched on 8 directions and 3 lengths (from <see cref="LeaderGap"/> or the region radius up to twice that);
    /// the first box that keeps Margin from the frame, stays inside the globe disc, touches no placed label and covers no land of another country wins.
    /// <paramref name="blocked"/> says whether the globe under a screen point is land that is not the label's own. False (the label is hidden) when no place is free.
    /// </summary>
    public static bool PlaceLeader(float ax, float ay, float w, float h, float vpW, float vpH, float gcx, float gcy, float limbR, float regionR,
        System.Collections.Generic.IReadOnlyList<Box> placed, System.Func<float, float, bool> blocked, out float cx, out float cy)
    {
        cx = cy = 0;
        if (ax < Margin || ax > vpW - Margin || ay < Margin || ay > vpH - Margin) return false;   // the leader would run off the frame (Panama at the bottom edge)
        float hw = w * 0.5f, hh = h * 0.5f, baseL = System.MathF.Max(LeaderGap, regionR + 4f);
        foreach (float k in LengthSteps)
            foreach (var (ux, uy) in Dirs)
            {
                float l = baseL * k;
                float px = ax + ux * l + (ux > 0.3f ? hw : ux < -0.3f ? -hw : 0f), py = ay + uy * l + (uy > 0.3f ? hh : uy < -0.3f ? -hh : 0f);
                if (px - hw < Margin || px + hw > vpW - Margin || py - hh < Margin || py + hh > vpH - Margin) continue;
                if (!InsideDisc(px, py, hw, hh, gcx, gcy, limbR)) continue;
                var box = new Box(px - hw, py - hh, w, h).Grow(2f);
                bool hit = false;
                for (int i = 0; i < placed.Count && !hit; i++) hit = placed[i].Intersects(box);
                if (hit || AnyBlocked(px, py, hw, hh, blocked)) continue;
                cx = px; cy = py; return true;
            }
        return false;
    }

    /// <summary>Thin boxes along the leader line from the anchor to the text box, so that no later label is laid across the line.</summary>
    public static void LineBoxes(float ax, float ay, float cx, float cy, float hw, float hh, System.Collections.Generic.List<Box> into)
    {
        float tx = System.Math.Clamp(ax, cx - hw, cx + hw), ty = System.Math.Clamp(ay, cy - hh, cy + hh);
        float len = System.MathF.Sqrt((tx - ax) * (tx - ax) + (ty - ay) * (ty - ay));
        int n = (int)(len / 6f) + 1;
        for (int i = 0; i <= n; i++) { float t = i / (float)n; into.Add(new Box(ax + (tx - ax) * t - 3f, ay + (ty - ay) * t - 3f, 6f, 6f)); }
    }

    // Probes over the whole box: a coarse cover test without allocation.
    static bool AnyBlocked(float cx, float cy, float hw, float hh, System.Func<float, float, bool> blocked)
    {
        for (int j = -2; j <= 2; j++)
            for (int i = -2; i <= 2; i++)
                if (blocked(cx + i * 0.5f * hw, cy + j * 0.5f * hh)) return true;
        return false;
    }

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
