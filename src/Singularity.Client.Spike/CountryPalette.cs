using System;
using System.Collections.Generic;
using System.Linq;

namespace Singularity.Client.Spike;

/// <summary>How a region says that it is not a plain core region of its owner (A5: never colour alone).</summary>
public enum HatchKind : byte { None = 0, Colony = 1, Occupied = 2 }

/// <summary>Fill tone, hatch and owner of one region as the globe shader reads them.</summary>
/// <summary>Controller is the number (1..) of the controlling polity in <see cref="CountryPalette.Result.Controllers"/>, 0 when the region is not occupied; it differs between zones even when their tones are equal.</summary>
public readonly record struct RegionStyle(int Tone, HatchKind Hatch, int HatchTone, int Owner, int Controller = 0);

/// <summary>
/// The 12-tone country palette and its assignment rule (docs/ui/specs/task-0138-country-palette.md sections 2-4).
/// Deterministic: arrays and ordinal sorting only, so every run gives the same map.
/// </summary>
public static class CountryPalette
{
    /// <summary>Base fills tint.1..tint.12 (index 0..11).</summary>
    public static readonly string[] Hex = { "B6DEBF", "FCD2DB", "F9BBAC", "B1D6F5", "EBE0B2", "ABDED8", "D3CEEC", "F6D7BD", "EDD49E", "D0DBBE", "E2C6E6", "CEC2B5" };
    public const int Stone = 11, Turquoise = 5;
    // Terracotta (2) is SUN's and blue (3) is FRA's alone: Egypt must not read as the USSR, Ethiopia not as an unhatched France.
    static bool Reserved(int t) => t == Stone || t == Turquoise || t == 2 || t == 3;
    static IEnumerable<int> Open => Enumerable.Range(0, Hex.Length).Where(t => !Reserved(t));

    /// <summary>
    /// Atlas traditions (docs/ui/specs/political-map-palette.md section 2). The first five entries keep their place (HatchLogic and the slice read them by index):
    /// USA green, GBR pink, SUN terracotta, FRA blue, SWE yellow; the dominions share the pink of GBR (no hatch: they are core land).
    /// </summary>
    public static readonly (string Code, int Tone)[] Fixed =
    {
        ("USA", 0), ("GBR", 1), ("SUN", 2), ("FRA", 3), ("SWE", 4),
        ("CAN", 1), ("AUS", 1), ("NZL", 1), ("ZAF", 1),
        ("PRT", 5), ("JPN", 7), ("NLD", 7), ("ROC", 8), ("ESP", 8), ("ITA", 9), ("BEL", 10), ("DEU", 11),
    };

    /// <summary>Colony fill: the metropole tone mixed with white by 18 %, the 45 degree hatch carries the status. The stone tone has no colonies in 1945 and stays unmixed (it would fall under the land-none delta).</summary>
    public const float ColonyLighten = 0.18f;
    public static (byte R, byte G, byte B) ColonyFill(int tone)
    {
        int v = int.Parse(Hex[tone], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
        float k = tone == Stone ? 0f : ColonyLighten;
        static byte M(int c, float k) => (byte)Math.Round(c + (255 - c) * k);
        return (M((v >> 16) & 255, k), M((v >> 8) & 255, k), M(v & 255, k));
    }

    // Neighbours must not share these pairs (CVD or base delta E below the limit, spec 3.5); 0-based tone indices.
    static readonly (int, int)[] Forbidden = { (0, 9), (6, 10), (7, 9), (3, 6), (4, 8) };
    public static bool IsForbidden(int a, int b) => a == b || Forbidden.Any(p => (p.Item1 == a && p.Item2 == b) || (p.Item1 == b && p.Item2 == a));

    static readonly double[,] Delta = BuildDelta();
    static double[,] BuildDelta()
    {
        var d = new double[Hex.Length, Hex.Length];
        for (int i = 0; i < Hex.Length; i++)
            for (int j = 0; j < Hex.Length; j++) d[i, j] = ColorMath.DeltaE2000(ColorMath.FromHex(Hex[i]), ColorMath.FromHex(Hex[j]));
        return d;
    }

    public static double DeltaE(int a, int b) => Delta[a, b];

    /// <summary>
    /// Tone per code. <paramref name="codes"/> are sorted ordinally; <paramref name="size"/> is the region count (bigger first);
    /// <paramref name="neighbours"/> lists the indices of bordering codes. Fixed powers first; then free tones 5..10, then 0..4, then the
    /// least bad tone (the border gets a second outline then, spec 3.5). The stone tone (11) is for codes without neighbours and for the last resort.
    /// </summary>
    public static int[] Assign(IReadOnlyList<string> codes, IReadOnlyList<int> size, IReadOnlyList<List<int>> neighbours)
    {
        int n = codes.Count;
        var tone = new int[n];
        Array.Fill(tone, -1);
        var used = new int[Hex.Length];
        foreach (var (code, t) in Fixed)
        {
            for (int i = 0; i < n; i++) if (codes[i] == code) { tone[i] = t; used[t]++; }
        }
        var order = Enumerable.Range(0, n).Where(i => tone[i] < 0).OrderByDescending(i => size[i]).ThenBy(i => codes[i], StringComparer.Ordinal).ToList();
        foreach (int i in order)
        {
            var nb = neighbours[i].Where(j => tone[j] >= 0).Select(j => tone[j]).ToList();
            int pick = -1;
            // Stone is DEU's alone (fixed), turquoise (5) is PRT's and its colonies': the greedy pass never hands them out.
            if (neighbours[i].Count == 0) pick = Best(Open.ToList(), nb, used);
            else
                foreach (var tier in new[] { Enumerable.Range(6, 5), Enumerable.Range(0, 5).Where(t => !Reserved(t)) })
                {
                    var ok = tier.Where(t => !nb.Any(o => IsForbidden(t, o))).ToList();
                    if (ok.Count > 0) { pick = Best(ok, nb, used); break; }
                }
            if (pick < 0) pick = Best(Open.ToList(), nb, used);
            tone[i] = pick; used[pick]++;
        }
        return tone;
    }

    // Largest minimal delta E to the neighbours, then the least used tone, then the smaller index.
    static int Best(List<int> cand, List<int> nb, int[] used)
    {
        int best = -1; double bd = -1;
        foreach (int t in cand)
        {
            double d = nb.Count == 0 ? 1e9 : nb.Min(o => Delta[t, o]);
            if (best < 0 || d > bd + 1e-9 || (Math.Abs(d - bd) <= 1e-9 && used[t] < used[best])) { best = t; bd = d; }
        }
        return best;
    }

    /// <summary>Colony, mandate, protectorate and leased territory are drawn in the tone of the metropole with a 45 degree hatch.</summary>
    public static bool IsDependent(string status) => status is "colony" or "mandate" or "protectorate" or "leased";

    /// <summary>Tone per owner number (index 0 unused) and style per region.</summary>
    public sealed record Result(int[] OwnerTone, RegionStyle[] Regions, int[] CodeTone, string[] Codes, string[] Controllers);

    public static Result Build(WorldMap map)
    {
        // Codes that get a tone: owners and non-joint controllers (a controller colours the occupation hatch).
        var joint = map.Polities.Where(p => p.Members.Length > 0).ToDictionary(p => p.Id, p => p.Members, StringComparer.Ordinal);
        var codes = map.Rows.SelectMany(r => new[] { r.Owner, r.Controller }).Where(c => c != "" && !joint.ContainsKey(c)).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();
        int Ix(string c) => codes.BinarySearch(c, StringComparer.Ordinal);
        var size = new int[codes.Count];
        foreach (var r in map.Rows) if (r.Owner != "" && Ix(r.Owner) >= 0) size[Ix(r.Owner)]++;
        // The graph joins owners whose core regions touch: colonies do not take part (spec 3.3).
        var nb = codes.Select(_ => new List<int>()).ToArray();
        foreach (var (a, b) in map.RegionPairs)
        {
            var ra = map.Rows[a]; var rb = map.Rows[b];
            if (ra.Status != "core" || rb.Status != "core" || ra.Owner == "" || rb.Owner == "" || ra.Owner == rb.Owner) continue;
            int i = Ix(ra.Owner), j = Ix(rb.Owner);
            if (i < 0 || j < 0) continue;   // a joint administration has no tone of its own
            if (!nb[i].Contains(j)) nb[i].Add(j);
            if (!nb[j].Contains(i)) nb[j].Add(i);
        }
        var tone = Assign(codes, size, nb);
        int ToneOf(string c) => c == "" ? Stone : joint.TryGetValue(c, out var m) ? ToneOf(m[0]) : tone[Ix(c)];

        var ownerTone = new int[map.Owners.Length + 1];
        for (int o = 1; o <= map.Owners.Length; o++) ownerTone[o] = ToneOf(map.Owners[o - 1]);
        var reg = new RegionStyle[map.Rows.Length];
        var controllers = new List<string>();   // polity codes of the occupiers in order of first use (rows are ordered)
        for (int i = 0; i < reg.Length; i++)
        {
            var r = map.Rows[i];
            int t = ToneOf(r.Owner);
            var hk = HatchLogic.Choose(r.Owner, r.Controller, IsDependent(r.Status));
            int ctrl = 0;
            if (hk == HatchKind.Occupied)
            {
                ctrl = controllers.IndexOf(r.Controller) + 1;
                if (ctrl == 0) { controllers.Add(r.Controller); ctrl = controllers.Count; }
            }
            reg[i] = new RegionStyle(t, hk, hk == HatchKind.Occupied ? ToneOf(r.Controller) : t, map.RegionOwner[i], ctrl);
        }
        return new Result(ownerTone, reg, tone, codes.ToArray(), controllers.ToArray());
    }
}
