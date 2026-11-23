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

    /// <summary>A wrapped line wider than the paragraph width (a single character wider than the line is unavoidable and not an overflow).</summary>
    public static bool Overflows(string line, float lineWidth, float maxW) => lineWidth > maxW + 0.5f && line.Length > 1;

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

    /// <summary>Per-point core of NeedsLeader (no allocation): is a node at (qx, qy) as near to the box as the label's own node at distance ownDist?</summary>
    public static bool OtherIsAsNear(float ownDist, float qx, float qy, float x, float y, float w, float h, float slack = 4f) => DistToRect(qx, qy, x, y, w, h) <= ownDist + slack;

    /// <summary>True when some other node centre is within `slack` du of being as near to the label box as the own node.</summary>
    public static bool NeedsLeader(float ownX, float ownY, ReadOnlySpan<(float X, float Y)> others, float x, float y, float w, float h, float slack = 4f)
    {
        float own = DistToRect(ownX, ownY, x, y, w, h);
        foreach (var o in others) if (OtherIsAsNear(own, o.X, o.Y, x, y, w, h, slack)) return true;
        return false;
    }
}
