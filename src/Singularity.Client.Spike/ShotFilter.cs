using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Singularity.Client.Spike;

/// <summary>Godot-free frame filter of the shot runs (TASK-0158): --only a,b,c* shoots just the named frames; no list means the full set.</summary>
public sealed class ShotFilter
{
    static readonly Regex SizeToken = new(@"\d{3,4}x\d{3,4}");
    readonly string[] _patterns;

    public ShotFilter(string list) =>
        _patterns = list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(p => p.EndsWith(".png") ? p[..^4] : p).ToArray();

    /// <summary>True when no pattern has a wildcard, so the set of wanted frames is known and the run can stop once all are saved.</summary>
    public bool AllExact => _patterns.All(p => p.IndexOfAny(new[] { '*', '?' }) < 0);

    public string Describe => string.Join(",", _patterns);

    /// <summary>True when the frame (with or without .png) matches a pattern; * and ? are wildcards.</summary>
    public bool Wants(string name)
    {
        string n = name.EndsWith(".png") ? name[..^4] : name;
        return _patterns.Any(p => Glob(p, n));
    }

    /// <summary>True when any frame of a group of steps (base names; the size suffix and the greyscale twin are added) is wanted: the group is skipped otherwise.</summary>
    public bool WantsGroup(string sfx, params string[] bases) => bases.Any(b => Wants(b + sfx) || Wants(b + sfx + "_grey"));

    /// <summary>The patterns that matched none of the saved frames (a typo must fail the run, not pass with no frames).</summary>
    public string[] Unmatched(System.Collections.Generic.IEnumerable<string> saved)
    {
        var names = saved.Select(n => n.EndsWith(".png") ? n[..^4] : n).ToArray();
        return _patterns.Where(p => !names.Any(n => Glob(p, n))).ToArray();
    }

    /// <summary>A size-matrix combination (suffix like _1920x1080_s125_en) is worth running when some pattern can end in it; a wildcard tail keeps it (conservative).</summary>
    public bool WantsSuffix(string sfx)
    {
        if (!AllExact) return true;   // a mask can match frames of any combination: no pruning
        foreach (var p in _patterns)
        {
            // A greyscale twin is named <frame>_grey: it belongs to the same combination as its colour frame.
            string q = p.EndsWith("_grey", StringComparison.Ordinal) ? p[..^5] : p;
            string tail = q[(q.LastIndexOfAny(new[] { '*', '?' }) + 1)..];
            if (tail.Length == 0 || sfx.EndsWith(tail, StringComparison.Ordinal) || tail.EndsWith(sfx, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>The first (legacy) stage has frames without a size suffix; it is needed unless every pattern names a sized frame.</summary>
    /// <summary>
    /// A pattern without a size asks for the first stage unless it names only frames of the size matrix: it matches a registered sized frame and no frame of the first stage
    /// (the first batch of tech_s1_* walked the whole first stage because «no size» meant «legacy»). A pattern that matches nothing known also keeps the stage, so that a typo is reported.
    /// </summary>
    public bool WantsLegacyStage => _patterns.Any(p => !SizeToken.IsMatch(p) && (ShotRegistry.Legacy.Any(n => Glob(p, n)) || !ShotRegistry.SizedNames().Any(n => Glob(p, n))));

    public static bool Glob(string p, string s)
    {
        int pi = 0, si = 0, star = -1, mark = 0;
        while (si < s.Length)
        {
            if (pi < p.Length && (p[pi] == '?' || p[pi] == s[si])) { pi++; si++; }
            else if (pi < p.Length && p[pi] == '*') { star = pi++; mark = si; }
            else if (star >= 0) { pi = star + 1; si = ++mark; }
            else return false;
        }
        while (pi < p.Length && p[pi] == '*') pi++;
        return pi == p.Length;
    }
}
