using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Singularity.Client.Spike;

/// <summary>Validator rules T1-T5 for the paper texture and the speed gauge checks (CLIENT_V05C_SPEC §2, §3.4). Godot-free and deterministic.
/// The texture multiplies sRGB channels by k in [0.94, 1.02]; alert plates, the halo, the map and the globe never get it, so plates are measured at k = 1.
/// T3 (no texture inside a plate) is a pixel test on a screenshot: <see cref="T3"/> is fed by the client.</summary>
public static class TextureValidator
{
    public const double TextRatio = 4.5, WarnLossMax = 3, WarnDeltaLMin = 12;
    static readonly double[] Ks = { PaperTexture.KMin, PaperTexture.KMax };

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
                double r = ColorMath.Contrast(ColorMath.FromHex(th), ColorMath.FromHex(sh, PaperTexture.KMin));
                res.Add(new PairResult($"T1 {tn} on {sn} x{F(PaperTexture.KMin, 2)}", r >= TextRatio, $"contrast {F(r)}:1 (min {F(TextRatio)})"));
            }
        foreach (var (wn, wh, th) in InkWashText(p))
        {
            double r = ColorMath.Contrast(ColorMath.FromHex(th), ColorMath.FromHex(wh, PaperTexture.KMin));
            res.Add(new PairResult($"T1 text on {wn} x{F(PaperTexture.KMin, 2)}", r >= TextRatio, $"contrast {F(r)}:1 (min {F(TextRatio)})"));
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

        // T5: grey-scale margin of warn: the texture may not eat more than 3 units of dL* and dL* stays >= 12. Plates sit inside their surface (not in the edge-wear band),
        // so k is the range the tile really gives in the interior; the edge band and the hard cap are covered by T2.
        var sheetR = PaperTexture.InteriorRange(SurfaceKind.Sheet); var paperR = PaperTexture.InteriorRange(SurfaceKind.Paper);
        double[] interior = { Math.Min(sheetR.Min, paperR.Min), Math.Max(sheetR.Max, paperR.Max) };
        var warn = ColorMath.FromHex(p.Warn);
        foreach (var (sn, sh) in Surfaces(p))
        {
            double d1 = Math.Abs(ColorMath.Lstar(warn) - ColorMath.Lstar(ColorMath.FromHex(sh)));
            foreach (double k in interior)
            {
                double dk = Math.Abs(ColorMath.Lstar(warn) - ColorMath.Lstar(ColorMath.FromHex(sh, k)));
                res.Add(new PairResult($"T5 warn on {sn} interior x{F(k, 3)}", dk >= WarnDeltaLMin && d1 - dk <= WarnLossMax, $"dL* {F(d1)} -> {F(dk)} (loss {F(d1 - dk)}, max {F(WarnLossMax, 0)}; min {F(WarnDeltaLMin, 0)})"));
            }
        }
        return res;
    }

    static IEnumerable<(string, string, string)> InkWashText(PaletteSet p)
    {
        for (int i = 0; i < p.Wash.Length; i++) yield return ("wash." + Palette.InkNames[i], p.Wash[i], p.Text[i]);
        yield return ("wash.moss", AreaData.SeventhWash, AreaData.SeventhText);
    }

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
        sb.Append($"verdict: {(fail == 0 ? "PASS" : "FAIL")} ({results.Count - fail}/{results.Count} pairs pass)\n");
        return sb.ToString();
    }
}
