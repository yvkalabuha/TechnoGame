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
        var res = new List<string>(); string line = "";
        foreach (var word in s.Split(' '))
        {
            string next = line.Length == 0 ? word : line + " " + word;
            if (width(next) <= maxW) { line = next; continue; }
            if (line.Length > 0) { res.Add(line); line = ""; }
            string rest = word;
            while (width(rest) > maxW && rest.Length > 1)
            {
                int n = 1;
                while (n < rest.Length - 1 && width(rest[..(n + 1)]) <= maxW) n++;
                res.Add(rest[..n]); rest = rest[n..];
            }
            line = rest;
        }
        res.Add(line);
        return res;
    }
}
