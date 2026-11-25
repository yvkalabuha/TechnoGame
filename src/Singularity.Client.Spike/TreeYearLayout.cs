#nullable enable
using System;

namespace Singularity.Client.Spike;

/// <summary>Second channel of a sector (A5): the direction of its hatch lines; five patterns that stay apart in greyscale.</summary>
public enum SectorHatch { Vertical, Horizontal, Diagonal, AntiDiagonal, Grid }

/// <summary>Godot-free layout of the knowledge tree (owner #473): radius = year, sector = discipline, 6 zoom steps. Presentation only.</summary>
public static class TreeYearLayout
{
    /// <summary>Anchors up to 1945 are the roots: they have their own inner band, spread by a square-root scale so that the many years just before 1945 do not pile on one circle.</summary>
    public const int FirstYear = 1945, EdgeYear = 2040, RootsFrom = 1900;
    /// <summary>Radius of year 1945, the width of the roots band below it, and the edge of the disc (2040), in base units.</summary>
    public const float Inner = 2.0f, RootsBand = 1.2f, Edge = 6.3f;
    /// <summary>Hatch line spacing in du; opacity by zoom step: quiet enough not to bury the links (owner 2026-11-25), still distinct in greyscale (A5).</summary>
    public const float HatchSpacing = 6f;
    public static float HatchAlphaAt(int step) => step <= 1 ? 0.3f : 0.22f;
    public const int MaxSectors = 8;
    /// <summary>Opacity of the grid ink (rings, sector lines) over the paper: keeps the grid at 1.6:1 or less (a unit test holds it).</summary>
    public const float GridAlpha = 0.75f;

    /// <summary>Decade rings with a label: 1950 ... 2030.</summary>
    public static readonly int[] RingYears = { 1950, 1960, 1970, 1980, 1990, 2000, 2010, 2020, 2030 };

    /// <summary>The thicker landmark rings.</summary>
    public static bool IsLandmark(int ringYear) => ringYear is 1980 or 2000;

    /// <summary>Radius in base units of a (fractional) year. Before 1945: Inner - RootsBand * sqrt((1945 - year) / 45), so the years nearest 1945 get the most room; from 1945: linear to the edge.
    /// Years outside 1900..2040 are clamped. The two parts meet at 1945.</summary>
    public static float RadiusOf(float year)
    {
        float y = Math.Clamp(year, RootsFrom, EdgeYear);
        if (y < FirstYear) return Inner - RootsBand * MathF.Sqrt((FirstYear - y) / (FirstYear - RootsFrom));
        return Inner + (y - FirstYear) * ((Edge - Inner) / (EdgeYear - FirstYear));
    }

    /// <summary>Year at a radius (inverse of <see cref="RadiusOf"/>), for tests and tips.</summary>
    public static float YearAt(float radius)
    {
        if (radius < Inner) { float q = (Inner - radius) / RootsBand; return FirstYear - (FirstYear - RootsFrom) * q * q; }
        return FirstYear + (radius - Inner) * ((EdgeYear - FirstYear) / (Edge - Inner));
    }

    /// <summary>Sector width in degrees: the disc is shared equally, and a sector appended later only shrinks the others.</summary>
    public static float SectorWidth(int sectors) => 360f / Math.Max(1, sectors);

    /// <summary>Start angle of a sector in degrees (screen angle: 0 = right, y down); sector 0 starts at the top, the next ones follow clockwise.</summary>
    public static float SectorStart(int sector, int sectors) => -90f + sector * SectorWidth(sectors);

    public static float SectorMid(int sector, int sectors) => SectorStart(sector, sectors) + SectorWidth(sectors) * 0.5f;

    /// <summary>Hatch of a sector: vertical, horizontal, 45 degrees, -45 degrees, grid; then they repeat.</summary>
    public static SectorHatch HatchOf(int sector) => (SectorHatch)(sector % 5);

    /// <summary>Unit normal of the hatch lines of a kind (the lines run along its perpendicular); a grid has two.</summary>
    public static (float X, float Y)[] HatchNormals(SectorHatch k)
    {
        const float h = 0.70710678f;
        return k switch
        {
            SectorHatch.Vertical => new[] { (1f, 0f) },
            SectorHatch.Horizontal => new[] { (0f, 1f) },
            SectorHatch.Diagonal => new[] { (h, h) },
            SectorHatch.AntiDiagonal => new[] { (h, -h) },
            _ => new[] { (1f, 0f), (0f, 1f) },
        };
    }

    /// <summary>Points for an arc of a given span at a given screen radius: chords stay short enough that the polygon sag is invisible at the maximum zoom (a fixed count showed corners).</summary>
    public static int ArcPoints(float radiusDu, float spanDeg)
    {
        float chord = Math.Min(40f, Math.Max(radiusDu * 0.2f, 1f)), len = radiusDu * spanDeg * MathF.PI / 180f;
        return Math.Clamp((int)MathF.Ceiling(len / chord), 8, 1024);
    }

    /// <summary>Slots across the usable arc of a sector that the greedy placement tries.</summary>
    const int Slots = 48;
    const int LaneYear = 1950; const float LaneStep = 0.2f;
    static readonly int[] LaneOffsets = { 0, 1, -1, 2, -2 };

    /// <summary>
    /// Places the nodes: radius from the year, angle inside the sector. Nodes of a sector go in the order (year, number); each takes the first free slot of the sector arc
    /// (starting at a slot derived from its number, so that one year does not form a line) whose distance to the nodes already placed is at least <paramref name="minGap"/> base units;
    /// when none is free, the slot with the largest distance. Deterministic: the result depends only on the inputs.
    /// </summary>
    public static void Place(ReadOnlySpan<int> year, ReadOnlySpan<int> sector, ReadOnlySpan<int> number, int sectors, float margin, float minGap, Span<float> radius, Span<float> angle)
    {
        int n = year.Length;
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        var y = year.ToArray(); var s = sector.ToArray(); var num = number.ToArray();
        Array.Sort(order, (a, b) => s[a] != s[b] ? s[a].CompareTo(s[b]) : y[a] != y[b] ? y[a].CompareTo(y[b]) : num[a].CompareTo(num[b]));
        var px = new float[n]; var py = new float[n];
        float w = SectorWidth(sectors);
        int sectorFrom = 0;
        for (int k = 0; k < n; k++)
        {
            int i = order[k];
            if (k > 0 && s[order[k - 1]] != s[i]) sectorFrom = k;
            float r0 = RadiusOf(y[i]), bestR = r0;
            float a0 = SectorStart(s[i], sectors) + w * margin, span = w * (1f - 2f * margin);
            int pref = ((num[i] * 5 + 3) % Slots + Slots) % Slots, best = pref; float bestD = -1f;
            // The crowded roots and the 1940s may step onto a neighbouring sub-ring (0.2 unit apart) when their own ring has no free place; other years stay on their ring.
            int lanes = y[i] <= LaneYear ? LaneOffsets.Length : 1;
            bool found = false;
            for (int ln = 0; ln < lanes && !found; ln++)
            {
                float r = Math.Max(r0 + LaneOffsets[ln] * LaneStep, Inner - RootsBand);
                for (int t = 0; t < Slots; t++)
                {
                    int slot = (pref + t) % Slots;
                    float ang = a0 + span * (slot + 0.5f) / Slots, rad = ang * MathF.PI / 180f;
                    float x = r * MathF.Cos(rad), yy = r * MathF.Sin(rad), nearest = float.MaxValue;
                    for (int j = sectorFrom; j < k; j++) nearest = Math.Min(nearest, MathF.Sqrt((x - px[order[j]]) * (x - px[order[j]]) + (yy - py[order[j]]) * (yy - py[order[j]])));
                    if (nearest >= minGap) { best = slot; bestR = r; found = true; break; }
                    if (nearest > bestD) { bestD = nearest; best = slot; bestR = r; }
                }
            }
            radius[i] = bestR;
            float angle0 = a0 + span * (best + 0.5f) / Slots, rr = angle0 * MathF.PI / 180f;
            angle[i] = angle0; px[i] = bestR * MathF.Cos(rr); py[i] = bestR * MathF.Sin(rr);
        }
    }
}
