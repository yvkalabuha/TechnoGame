using System;
using System.Collections.Generic;

namespace Singularity.Client.Spike;

/// <summary>Wrapped lines by (text, width, size, font): a hit allocates nothing, so a canvas redraw does not re-wrap or re-allocate.</summary>
public sealed class WrapCache
{
    readonly Dictionary<(string, float, int, int), List<string>> _map = new();
    public bool TryGet(string s, float maxW, int fs, int kind, out List<string> lines) => _map.TryGetValue((s, maxW, fs, kind), out lines!);
    public List<string> Add(string s, float maxW, int fs, int kind, List<string> lines) { _map[(s, maxW, fs, kind)] = lines; return lines; }
}

/// <summary>Word wrap for text drawn straight on a canvas. Godot-free: the caller gives the measure of a string in du. A word wider than a line is broken between characters (no hyphen), so no line is wider than maxW unless one character is.</summary>
public static class TextWrap
{
    public static List<string> Lines(string s, float maxW, Func<string, float> width)
    {
        var res = new List<string>();
        foreach (var para in s.Split('\n'))   // a hard break is never measured as part of a word
        {
            string line = ""; bool any = false;
            foreach (var word in para.Split(' '))
            {
                string next = line.Length == 0 ? word : line + " " + word;
                if (width(next) <= maxW) { line = next; continue; }
                if (line.Length > 0) { res.Add(line); any = true; line = ""; }
                string rest = word;
                while (width(rest) > maxW && rest.Length > 1)
                {
                    int n = Take(rest, maxW, width);
                    res.Add(rest[..n]); any = true; rest = rest[n..];
                }
                line = rest;
            }
            if (line.Length > 0 || !any) res.Add(line);
        }
        return res;
    }

    /// <summary>A wrapped line wider than the paragraph width; a single glyph wider than the column counts too (the column is too narrow for the text).</summary>
    public static bool Overflows(string line, float lineWidth, float maxW) => line.Length > 0 && lineWidth > maxW + 0.5f;

    // Length of the longest prefix of a long word that fits (at least one character, never half of a surrogate pair).
    static int Take(string rest, float maxW, Func<string, float> width)
    {
        int n = 1;
        while (n < rest.Length - 1 && width(rest[..(n + 1)]) <= maxW) n++;
        if (char.IsHighSurrogate(rest[n - 1]) && n < rest.Length) n = n > 1 ? n - 1 : Math.Min(2, rest.Length);
        return n;
    }
}

/// <summary>Where a tree label stands against its node (Godot-free): a label that another node is as near to as its own needs a leader line.</summary>
public static class LabelGeometry
{
    public static float DistToRect(float px, float py, float x, float y, float w, float h)
    {
        float dx = Math.Max(Math.Max(x - px, 0), px - (x + w)), dy = Math.Max(Math.Max(y - py, 0), py - (y + h));
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>A name on a far ring: the distance from the node centre to its box exceeds the node radius plus `margin` du (the near places are at most about 1.4 x (radius + 8) away, so margin 20 keeps them out).</summary>
    public static bool IsApart(float distToBox, float nodeR, float margin = 20f) => distToBox > nodeR + margin;

    /// <summary>Per-point core of NeedsLeader (no allocation): is a node at (qx, qy) as near to the box as the label's own node at distance ownDist?</summary>
    public static bool OtherIsAsNear(float ownDist, float qx, float qy, float x, float y, float w, float h, float slack = 4f) => DistToRect(qx, qy, x, y, w, h) <= ownDist + slack;

    /// <summary>Placement priority of a tree label, lower is placed first and drawn last (on top): the goal, selected or root name 0, the route 1, the frontier 2, the rest 3.</summary>
    public static int Rank(bool named, bool onRoute, bool frontier) => named ? 0 : onRoute ? 1 : frontier ? 2 : 3;

    /// <summary>Does the segment a-b run through the box? Exact (Liang-Barsky): any overlap of positive length counts, so a short clip of a corner is a hit; a point touch is not.</summary>
    public static bool SegmentHitsRect(float ax, float ay, float bx, float by, float x, float y, float w, float h)
    {
        float dx = bx - ax, dy = by - ay, t0 = 0f, t1 = 1f;
        bool Clip(float p, float q)   // p: direction against this edge, q: distance of a from it (inside when q >= 0)
        {
            if (p == 0f) return q >= 0f;
            float t = q / p;
            if (p < 0f) { if (t > t1) return false; if (t > t0) t0 = t; }
            else { if (t < t0) return false; if (t < t1) t1 = t; }
            return true;
        }
        if (!Clip(-dx, ax - x) || !Clip(dx, x + w - ax) || !Clip(-dy, ay - y) || !Clip(dy, y + h - ay)) return false;
        return (t1 - t0) * MathF.Sqrt(dx * dx + dy * dy) > 0.01f;
    }

    /// <summary>True when some other node centre is within `slack` du of being as near to the label box as the own node.</summary>
    public static bool NeedsLeader(float ownX, float ownY, ReadOnlySpan<(float X, float Y)> others, float x, float y, float w, float h, float slack = 4f)
    {
        float own = DistToRect(ownX, ownY, x, y, w, h);
        foreach (var o in others) if (OtherIsAsNear(own, o.X, o.Y, x, y, w, h, slack)) return true;
        return false;
    }
}
