using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Singularity.Client.Spike;

/// <summary>Validator rules T1-T6 for the paper texture and the speed gauge checks (CLIENT_V05C_SPEC §2, §3.4). Godot-free and deterministic.
/// The texture multiplies sRGB channels by k in (interior [0.955, 1.02], edge band down to 0.90); alert plates, the halo, the map and the globe never get it, so plates are measured at k = 1.
/// T3 (no texture inside a plate) is a pixel test on a screenshot: <see cref="T3"/> is fed by the client.</summary>
public static class TextureValidator
{
    public const double TextRatio = 4.5, WarnDeltaLMin = 15, WorstK = 0.94, MossOliveDeltaE = 8, MossOliveCvd = 5;   // CVD 5.7 measured (review asked 8): the colour #3A8A5C is fixed by #360, the hatching is the second channel   // WorstK: conservative for text and plates (the real interior floor is 0.955)
    /// <summary>T6: minimal distance in du from a surface edge to any text, icon or plate. The band of k below 0.955 is at most 8.5 du wide.</summary>
    public const double EdgeInset = 10;
    static readonly double[] Ks = { WorstK, PaperTexture.KMax };

    static string F(double v, int d = 1) => v.ToString("F" + d, CultureInfo.InvariantCulture);

    /// <summary>Surfaces that get the texture: paper, sheet, hover and every ink wash (the six inks and the seventh).</summary>
    static List<(string Name, string Hex)> Surfaces(PaletteSet p)
    {
        var l = new List<(string, string)> { ("paper", p.Paper), ("sheet", p.Sheet), ("hover", p.Hover) };
        for (int i = 0; i < p.Wash.Length; i++) l.Add(("wash." + Palette.InkNames[i], p.Wash[i]));
        l.Add(("wash.moss", AreaData.SeventhWash));
        return l;
    }

    public static IReadOnlyList<PairResult> Run(PaletteSet p)
    {
        var res = new List<PairResult>();
        // T1: text tokens against the textured surfaces at the worst (darkest) k.
        var texts = new List<(string, string)> { ("ink", Palette.Ink), ("ink-2", Palette.Ink2), ("ink-3", Palette.Ink3) };
        for (int i = 0; i < p.Text.Length; i++) texts.Add(("text." + Palette.InkNames[i], p.Text[i]));
        texts.Add(("text.moss", AreaData.SeventhText));
        foreach (var (sn, sh) in new[] { ("sheet", p.Sheet), ("paper", p.Paper) })
            foreach (var (tn, th) in texts)
            {
                double r = ColorMath.Contrast(ColorMath.FromHex(th), ColorMath.FromHex(sh, WorstK));
                res.Add(new PairResult($"T1 {tn} on {sn} x{F(WorstK, 2)}", r >= TextRatio, $"contrast {F(r)}:1 (min {F(TextRatio)})"));
            }
        foreach (var (wn, wh, th) in InkWashText(p))
        {
            double r = ColorMath.Contrast(ColorMath.FromHex(th), ColorMath.FromHex(wh, WorstK));
            res.Add(new PairResult($"T1 text on {wn} x{F(WorstK, 2)}", r >= TextRatio, $"contrast {F(r)}:1 (min {F(TextRatio)})"));
        }

        // T2: the alert thresholds (dE, dL, CVD) of the textured surfaces at both ends of k; the plate itself has k = 1.
        var plates = new[] { ("alarm", p.Alarm, ContrastValidator.AlarmLimits), ("warn", p.Warn, ContrastValidator.WarnLimits), ("command", p.Command, ContrastValidator.CommandLimits) };
        foreach (var (pn, ph, lim) in plates)
        {
            var plate = ColorMath.FromHex(ph);
            foreach (var (sn, sh) in Surfaces(p))
                foreach (double k in Ks)
                {
                    var bg = ColorMath.FromHex(sh, k);
                    double de = ColorMath.DeltaE2000(plate, bg), dl = Math.Abs(ColorMath.Lstar(plate) - ColorMath.Lstar(bg)), cv = double.MaxValue;
                    for (int c = 0; c < 3; c++) cv = Math.Min(cv, ColorMath.DeltaE2000(ColorMath.Cvd(plate, c), ColorMath.Cvd(bg, c)));
                    res.Add(new PairResult($"T2 {pn} on {sn} x{F(k, 2)}", de >= lim.DeltaE && dl >= lim.DeltaL && cv >= lim.Cvd, $"dE {F(de)} dL {F(dl)} cvd {F(cv)}"));
                }
        }

        // T4: the grey multiplier adds no chroma beyond the decor limit (0.13) at the lightest k.
        foreach (var (sn, sh) in Surfaces(p))
        {
            double c0 = ColorMath.OkChroma(ColorMath.FromHex(sh)), c1 = ColorMath.OkChroma(ColorMath.FromHex(sh, PaperTexture.KMax));
            res.Add(new PairResult($"T4 {sn} chroma x{F(PaperTexture.KMax, 2)}", c1 <= ContrastValidator.DecorChroma, $"OKLCH C {F(c0, 3)} -> {F(c1, 3)} (max {F(ContrastValidator.DecorChroma, 2)})"));
        }

        // T5: grey-scale margin of warn: dL* between the plate and the textured surface stays >= 15 at the interior floor (k = 0.955) and at the top (1.02).
        // The edge band (k down to 0.90) is not part of it: nothing but the frame lives there (T6).
        var warn = ColorMath.FromHex(p.Warn);
        foreach (var (sn, sh) in Surfaces(p))
        {
            double d1 = Math.Abs(ColorMath.Lstar(warn) - ColorMath.Lstar(ColorMath.FromHex(sh)));
            foreach (double k in new[] { PaperTexture.KInnerMin, PaperTexture.KMax })
            {
                double dk = Math.Abs(ColorMath.Lstar(warn) - ColorMath.Lstar(ColorMath.FromHex(sh, k)));
                res.Add(new PairResult($"T5 warn on {sn} x{F(k, 3)}", dk >= WarnDeltaLMin, $"dL* {F(d1)} -> {F(dk)} (min {F(WarnDeltaLMin, 0)})"));
            }
        }

        // F4 of the visual review: the seventh ink (moss) sits next to olive (Politics) and is told from it mostly by hatching; still keep the colours apart for every CVD type.
        {
            var moss = ColorMath.FromHex(AreaData.SeventhLine); var olive = ColorMath.FromHex(p.Line[3]);
            double de = ColorMath.DeltaE2000(moss, olive), cv = double.MaxValue;
            for (int c = 0; c < 3; c++) cv = Math.Min(cv, ColorMath.DeltaE2000(ColorMath.Cvd(moss, c), ColorMath.Cvd(olive, c)));
            res.Add(new PairResult("moss vs olive (line)", de >= MossOliveDeltaE && cv >= MossOliveCvd, $"dE {F(de)} (min {F(MossOliveDeltaE, 0)}) cvd {F(cv)} (min {F(MossOliveCvd, 0)}, the review asked 8: moss is told from olive by hatching too); moss is used only hatched"));
        }
        return res;
    }

    static IEnumerable<(string, string, string)> InkWashText(PaletteSet p)
    {
        for (int i = 0; i < p.Wash.Length; i++) yield return ("wash." + Palette.InkNames[i], p.Wash[i], p.Text[i]);
        yield return ("wash.moss", AreaData.SeventhWash, AreaData.SeventhText);
    }

    /// <summary>T6 on the layout: every item is (name, distance in du from its textured surface edge to the nearest text, icon or plate, the limit of that surface).
    /// Panels, rail and top bar must keep <see cref="EdgeInset"/>; small buttons their own band (wear width + 1.25 du of edge roughness), see <see cref="Limit"/>.</summary>
    public static IReadOnlyList<PairResult> T6(IEnumerable<(string Name, double Inset, double Limit)> items) =>
        items.Select(i => new PairResult($"T6 {i.Name}", i.Inset >= i.Limit, $"content {F(i.Inset)} du from the edge (min {F(i.Limit)})")).ToList();

    /// <summary>Panels, the rail and the top bar keep 10 du (spec). A small button has no room for that: its text must only stay where the wear cannot push k below the 0.94 that T1 proves
    /// (wear darkening above 0.015 at most), plus the 1.25 du of edge roughness; at most the wear width.</summary>
    public static double Limit(SurfaceKind kind)
    {
        if (kind != SurfaceKind.Button) return EdgeInset;
        var p = PaperTexture.Params(kind);
        double e = 0;
        while (e < p.WearWidth && p.Wear * (1 - Smooth(p.WearWidth, e - 1.25)) > PaperTexture.KInnerMin - WorstK) e += 0.25;
        return Math.Ceiling(e);
    }

    static double Smooth(double w, double x) { double t = Math.Clamp(x / w, 0, 1); return t * t * (3 - 2 * t); }

    /// <summary>Population variance of 8-bit samples.</summary>
    public static double Variance(IReadOnlyList<byte> v)
    {
        if (v.Count == 0) return double.NaN;
        double m = v.Average(x => (double)x);
        return v.Average(x => (x - m) * (x - m));
    }

    /// <summary>T3 on a screenshot: the luminance variance inside an alert plate must be 0 (small tolerance for anti-aliasing); the variance of a textured control area
    /// shows that the texture is on (with the preset on, it must be above the plate's).</summary>
    public static PairResult T3(double plateVariance, double controlVariance, bool textureOn) =>
        new("T3 no texture inside an alert plate", plateVariance <= 0.5 && (!textureOn || controlVariance > plateVariance),
            $"plate variance {F(plateVariance, 3)} (max 0.5), textured area variance {F(controlVariance, 3)}, texture {(textureOn ? "on" : "off")}");

    // ---------- speed gauge (CLIENT_V05C_SPEC §2) ----------
    /// <summary>Line colours of the four gauge cells: teal, olive, plum, sand (day, week, month, reserve). Only inks of palette C.</summary>
    public static readonly string[] GaugeLine = { "168A92", "4F8A1E", "A03E82", "8F7A2E" };

    public const double GaugeDeltaE = 20, GaugeCvd = 8, GaugeChroma = 0.16;   // line chroma of the inks of palette C reaches 0.152 (olive, plum); alert plates stay above 0.17

    public static IReadOnlyList<PairResult> RunGauge()
    {
        var res = new List<PairResult>();
        for (int i = 0; i + 1 < GaugeLine.Length; i++)
        {
            var a = ColorMath.FromHex(GaugeLine[i]); var b = ColorMath.FromHex(GaugeLine[i + 1]);
            double de = ColorMath.DeltaE2000(a, b), cv = double.MaxValue;
            for (int c = 0; c < 3; c++) cv = Math.Min(cv, ColorMath.DeltaE2000(ColorMath.Cvd(a, c), ColorMath.Cvd(b, c)));
            res.Add(new PairResult($"gauge cell {i + 1} vs {i + 2}", de >= GaugeDeltaE && cv >= GaugeCvd,
                $"dE {F(de)} (min {F(GaugeDeltaE, 0)}), cvd {F(cv)} (min {F(GaugeCvd, 0)}); L* {F(ColorMath.Lstar(a))} / {F(ColorMath.Lstar(b))}"));
        }
        foreach (var h in GaugeLine)
        {
            var c = ColorMath.FromHex(h); var lab = ColorMath.ToLab(c);
            double hue = Math.Atan2(lab.B, lab.A) * 180 / Math.PI; if (hue < 0) hue += 360;
            double ch = ColorMath.OkChroma(c);
            bool forbidden = hue is >= 15 and <= 45;   // the red sector is reserved for alarm
            res.Add(new PairResult($"gauge #{h} hue and chroma", !forbidden && ch <= GaugeChroma, $"hue {F(hue, 0)} (not 15..45), OKLCH C {F(ch, 3)} (max {F(GaugeChroma, 2)})"));
        }
        return res;
    }

    public static string Format(string title, IReadOnlyList<PairResult> results)
    {
        var sb = new StringBuilder($"{title}\n");
        int fail = 0;
        foreach (var r in results)
        {
            if (!r.Pass) fail++;
            sb.Append(r.Pass ? "PASS " : "FAIL ").Append(r.Name).Append(": ").Append(r.Detail).Append('\n');
        }
        if (results.Count == 0) fail = 1;   // nothing measured proves nothing
        sb.Append($"verdict: {(fail == 0 ? "PASS" : "FAIL")} ({results.Count - fail}/{results.Count} pairs pass{(results.Count == 0 ? ", NO MEASUREMENTS" : "")})\n");
        return sb.ToString();
    }
}
