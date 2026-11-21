using System;
using System.Collections.Generic;

namespace Singularity.Client.Spike;

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
