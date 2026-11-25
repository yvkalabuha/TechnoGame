#nullable enable
using System;

namespace Singularity.Client.Spike;

/// <summary>Second channel of a sector (A5): spokes every <see cref="SpokeDeg"/> degrees and/or arcs every <see cref="ArcUnits"/> base units; 0 = none.</summary>
public readonly record struct HatchSpec(float SpokeDeg, float ArcUnits);

/// <summary>Godot-free layout of the knowledge tree (owner #473): radius = year, sector = discipline, 6 zoom steps. Presentation only.</summary>
public static class TreeYearLayout
{
    /// <summary>The centre is "1945, roots": earlier anchors sit on the innermost circle.</summary>
    public const int FirstYear = 1945, EdgeYear = 2040;
    /// <summary>Radius of year 1945 and of the edge of the disc, in base units.</summary>
    public const float Inner = 1.0f, Edge = 6.3f;
    public const int MaxSectors = 8;
    /// <summary>Opacity of the grid ink (rings, sector lines) over the paper: keeps the grid at 1.6:1 or less (a unit test holds it).</summary>
    public const float GridAlpha = 0.75f;

    /// <summary>Decade rings with a label: 1950 ... 2030.</summary>
    public static readonly int[] RingYears = { 1950, 1960, 1970, 1980, 1990, 2000, 2010, 2020, 2030 };

    /// <summary>The thicker landmark rings.</summary>
    public static bool IsLandmark(int ringYear) => ringYear is 1980 or 2000;

    /// <summary>Radius in base units of a (fractional) year; years outside 1945..2040 are clamped.</summary>
    public static float RadiusOf(float year)
    {
        float y = Math.Clamp(year, FirstYear, EdgeYear);
        return Inner + (y - FirstYear) * ((Edge - Inner) / (EdgeYear - FirstYear));
    }

    /// <summary>Year at a radius (inverse of <see cref="RadiusOf"/>), for tests and tips.</summary>
    public static float YearAt(float radius) => FirstYear + (radius - Inner) * ((EdgeYear - FirstYear) / (Edge - Inner));

    /// <summary>Sector width in degrees: the disc is shared equally, and a sector appended later only shrinks the others.</summary>
    public static float SectorWidth(int sectors) => 360f / Math.Max(1, sectors);

    /// <summary>Start angle of a sector in degrees (screen angle: 0 = right, y down); sector 0 starts at the top, the next ones follow clockwise.</summary>
    public static float SectorStart(int sector, int sectors) => -90f + sector * SectorWidth(sectors);

    public static float SectorMid(int sector, int sectors) => SectorStart(sector, sectors) + SectorWidth(sectors) * 0.5f;

    /// <summary>Hatch of a sector: five different patterns, then they repeat.</summary>
    public static HatchSpec HatchOf(int sector) => (sector % 5) switch
    {
        0 => new(6f, 0f),
        1 => new(0f, 0.3f),
        2 => new(12f, 0.6f),
        3 => new(4f, 0f),
        _ => new(0f, 0.2f),
    };

    /// <summary>Points for an arc of a given span at a given screen radius: chords stay short enough that the polygon sag is invisible at the maximum zoom (a fixed count showed corners).</summary>
    public static int ArcPoints(float radiusDu, float spanDeg)
    {
        float chord = Math.Min(40f, Math.Max(radiusDu * 0.2f, 1f)), len = radiusDu * spanDeg * MathF.PI / 180f;
        return Math.Clamp((int)MathF.Ceiling(len / chord), 8, 1024);
    }

    /// <summary>Slots across the usable arc of a sector that the greedy placement tries.</summary>
    const int Slots = 24;

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
            float r = RadiusOf(y[i]);
            radius[i] = r;
            float a0 = SectorStart(s[i], sectors) + w * margin, span = w * (1f - 2f * margin);
            int pref = ((num[i] * 5 + 3) % Slots + Slots) % Slots, best = pref; float bestD = -1f;
            for (int t = 0; t < Slots; t++)
            {
                int slot = (pref + t) % Slots;
                float ang = a0 + span * (slot + 0.5f) / Slots, rad = ang * MathF.PI / 180f;
                float x = r * MathF.Cos(rad), yy = r * MathF.Sin(rad), nearest = float.MaxValue;
                for (int j = sectorFrom; j < k; j++) nearest = Math.Min(nearest, MathF.Sqrt((x - px[order[j]]) * (x - px[order[j]]) + (yy - py[order[j]]) * (yy - py[order[j]])));
                if (nearest >= minGap) { best = slot; bestD = float.MaxValue; break; }
                if (nearest > bestD) { bestD = nearest; best = slot; }
            }
            float angle0 = a0 + span * (best + 0.5f) / Slots, rr = angle0 * MathF.PI / 180f;
            angle[i] = angle0; px[i] = r * MathF.Cos(rr); py[i] = r * MathF.Sin(rr);
        }
    }
}
