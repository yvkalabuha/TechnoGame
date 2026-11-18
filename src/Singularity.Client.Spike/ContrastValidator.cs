using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Singularity.Client.Spike;

/// <summary>Colour maths for the validator: sRGB, linear RGB, CIE Lab (D65), CIEDE2000, OKLCH chroma, CVD (Machado 2009, severity 1). Godot-free.</summary>
public static class ColorMath
{
    public readonly record struct Lin(double R, double G, double B);
    public readonly record struct Lab(double L, double A, double B);

    /// <summary>Hex colour to linear RGB. <paramref name="k"/> multiplies the sRGB channels first: the 2D canvas works in sRGB, so the paper texture (a grey multiplier) acts there.</summary>
    public static Lin FromHex(string hex, double k = 1)
    {
        int v = int.Parse(hex.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        double R(int sh) => Math.Min(1.0, ((v >> sh) & 255) / 255.0 * k);
        return new Lin(ToLinear(R(16)), ToLinear(R(8)), ToLinear(R(0)));
    }

    /// <summary>WCAG relative luminance contrast ratio of two colours.</summary>
    public static double Contrast(Lin a, Lin b)
    {
        double ya = 0.2126729 * a.R + 0.7151522 * a.G + 0.072175 * a.B, yb = 0.2126729 * b.R + 0.7151522 * b.G + 0.072175 * b.B;
        return (Math.Max(ya, yb) + 0.05) / (Math.Min(ya, yb) + 0.05);
    }

    static double ToLinear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    static double Clamp01(double v) => Math.Clamp(v, 0, 1);

    public static Lab ToLab(Lin c)
    {
        double x = 0.4124564 * c.R + 0.3575761 * c.G + 0.1804375 * c.B;
        double y = 0.2126729 * c.R + 0.7151522 * c.G + 0.0721750 * c.B;
        double z = 0.0193339 * c.R + 0.1191920 * c.G + 0.9503041 * c.B;
        double fx = F(x / 0.95047), fy = F(y), fz = F(z / 1.08883);
        return new Lab(116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    static double F(double t) => t > 216.0 / 24389.0 ? Math.Cbrt(t) : (24389.0 / 27.0 * t + 16) / 116;

    /// <summary>Lightness L* from the luminance Y: the greyscale view of a colour.</summary>
    public static double Lstar(Lin c) => ToLab(c).L;

    /// <summary>CIEDE2000 with kL = kC = kH = 1 (Sharma, Wu, Dalal 2005).</summary>
    public static double DeltaE2000(Lab a, Lab b)
    {
        double c1 = Math.Sqrt(a.A * a.A + a.B * a.B), c2 = Math.Sqrt(b.A * b.A + b.B * b.B);
        double cm = (c1 + c2) / 2, cm7 = Math.Pow(cm, 7);
        double g = 0.5 * (1 - Math.Sqrt(cm7 / (cm7 + Math.Pow(25, 7))));
        double a1 = (1 + g) * a.A, a2 = (1 + g) * b.A;
        double cp1 = Math.Sqrt(a1 * a1 + a.B * a.B), cp2 = Math.Sqrt(a2 * a2 + b.B * b.B);
        double h1 = Hue(a.B, a1), h2 = Hue(b.B, a2);
        double dL = b.L - a.L, dC = cp2 - cp1, dh;
        if (cp1 * cp2 == 0) dh = 0;
        else if (Math.Abs(h2 - h1) <= 180) dh = h2 - h1;
        else dh = h2 <= h1 ? h2 - h1 + 360 : h2 - h1 - 360;
        double dH = 2 * Math.Sqrt(cp1 * cp2) * Math.Sin(Rad(dh / 2));
        double lm = (a.L + b.L) / 2, cpm = (cp1 + cp2) / 2, hm;
        if (cp1 * cp2 == 0) hm = h1 + h2;
        else if (Math.Abs(h1 - h2) <= 180) hm = (h1 + h2) / 2;
        else hm = h1 + h2 < 360 ? (h1 + h2 + 360) / 2 : (h1 + h2 - 360) / 2;
        double t = 1 - 0.17 * Math.Cos(Rad(hm - 30)) + 0.24 * Math.Cos(Rad(2 * hm)) + 0.32 * Math.Cos(Rad(3 * hm + 6)) - 0.20 * Math.Cos(Rad(4 * hm - 63));
        double dTheta = 30 * Math.Exp(-Math.Pow((hm - 275) / 25, 2));
        double rc = 2 * Math.Sqrt(Math.Pow(cpm, 7) / (Math.Pow(cpm, 7) + Math.Pow(25, 7)));
        double sl = 1 + 0.015 * Math.Pow(lm - 50, 2) / Math.Sqrt(20 + Math.Pow(lm - 50, 2));
        double sc = 1 + 0.045 * cpm, sh = 1 + 0.015 * cpm * t;
        double rt = -Math.Sin(Rad(2 * dTheta)) * rc;
        double x = dL / sl, y = dC / sc, z = dH / sh;
        return Math.Sqrt(x * x + y * y + z * z + rt * y * z);
    }

    static double Rad(double d) => d * Math.PI / 180;

    static double Hue(double b, double a)
    {
        if (a == 0 && b == 0) return 0;
        double h = Math.Atan2(b, a) * 180 / Math.PI;
        return h < 0 ? h + 360 : h;
    }

    public static double DeltaE2000(Lin a, Lin b) => DeltaE2000(ToLab(a), ToLab(b));

    /// <summary>Chroma in OKLCH (Ottosson 2020).</summary>
    public static double OkChroma(Lin c)
    {
        double l = Math.Cbrt(0.4122214708 * c.R + 0.5363325363 * c.G + 0.0514459929 * c.B);
        double m = Math.Cbrt(0.2119034982 * c.R + 0.6806995451 * c.G + 0.1073969566 * c.B);
        double s = Math.Cbrt(0.0883024619 * c.R + 0.2817188376 * c.G + 0.6299787005 * c.B);
        double a = 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s;
        double b = 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s;
        return Math.Sqrt(a * a + b * b);
    }

    static readonly double[][] CvdMatrices =
    {
        new[] { 0.152286, 1.052583, -0.204868, 0.114503, 0.786281, 0.099216, -0.003882, -0.048116, 1.051998 },   // protan
        new[] { 0.367322, 0.860646, -0.227968, 0.280085, 0.672501, 0.047413, -0.011820, 0.042940, 0.968881 },    // deutan
        new[] { 1.255528, -0.076749, -0.178779, -0.078411, 0.930809, 0.147602, 0.004733, 0.691367, 0.303900 },   // tritan
    };

    public static Lin Cvd(Lin c, int kind)
    {
        var m = CvdMatrices[kind];
        return new Lin(Clamp01(m[0] * c.R + m[1] * c.G + m[2] * c.B), Clamp01(m[3] * c.R + m[4] * c.G + m[5] * c.B), Clamp01(m[6] * c.R + m[7] * c.G + m[8] * c.B));
    }

    public static Lin Scale(Lin c, double r, double g, double b, double add = 0) =>
        new(Clamp01(c.R * r + add), Clamp01(c.G * g + add), Clamp01(c.B * b + add));
}

/// <summary>Result of one checked pair or context.</summary>
public sealed record PairResult(string Name, bool Pass, string Detail);

/// <summary>Alert-contrast validator (CLIENT_V05B_SPEC §7, decisions #303 and #326). Deterministic: same palette, same output.
/// Thresholds are [Предп.] from the spec, to be calibrated on real screenshots.</summary>
public static class ContrastValidator
{
    /// <summary>Shading of the globe fill from globe.gdshader: multiplier per channel and an additive glint (sea only; applied to all as a worst case).</summary>
    public sealed record Shade(string Name, double R, double G, double B, double Add = 0);

    public static readonly Shade[] Shades =
    {
        new("day", 1.0, 1.0, 1.0), new("terminator", 0.86, 0.86, 0.86), new("night", 0.72, 0.72, 0.72),
        new("night+tint", 0.72 * 0.86, 0.72 * 0.9, 0.72), new("glint", 1.0, 1.0, 1.0, 0.2),
    };

    public sealed record Limits(double DeltaE, double DeltaL, double Cvd, double Chroma);
    public static readonly Limits AlarmLimits = new(25, 25, 18, 0.17), CommandLimits = new(25, 25, 18, 0.15), WarnLimits = new(20, 12, 14, 0.145);
    public const double HaloVsBackground = 8, DecorChroma = 0.13, TintPair = 8;

    sealed record Worst(double DeltaE, double DeltaL, double Cvd, double Halo);

    static ColorMath.Lin Apply(ColorMath.Lin c, Shade s) => ColorMath.Scale(c, s.R, s.G, s.B, s.Add);

    // Worst case over the shades of the background (the plate and its halo are overlay, never shaded).
    static Worst Measure(ColorMath.Lin plate, ColorMath.Lin sheet, ColorMath.Lin bg, bool shaded)
    {
        double de = double.MaxValue, dl = double.MaxValue, cv = double.MaxValue, halo = double.MaxValue;
        foreach (var s in shaded ? Shades : new[] { Shades[0] })
        {
            var b = Apply(bg, s);
            de = Math.Min(de, ColorMath.DeltaE2000(plate, b));
            dl = Math.Min(dl, Math.Abs(ColorMath.Lstar(plate) - ColorMath.Lstar(b)));
            halo = Math.Min(halo, ColorMath.DeltaE2000(sheet, b));
            for (int k = 0; k < 3; k++)
                cv = Math.Min(cv, ColorMath.DeltaE2000(ColorMath.Cvd(plate, k), ColorMath.Cvd(b, k)));
        }
        return new Worst(de, dl, cv, halo);
    }

    static string F(double v, int d = 1) => v.ToString("F" + d, CultureInfo.InvariantCulture);

    public static IReadOnlyList<PairResult> Run(PaletteSet p)
    {
        var res = new List<PairResult>();
        var plates = new[] { ("alarm", p.Alarm, AlarmLimits), ("warn", p.Warn, WarnLimits), ("command", p.Command, CommandLimits) };
        var sheet = ColorMath.FromHex(p.Sheet);
        var maps = new List<(string, string)>();
        for (int i = 0; i < p.Tints.Length; i++) maps.Add(("tint." + (i + 1), p.Tints[i]));
        maps.Add(("sea", p.Sea)); maps.Add(("sea.deep", p.SeaDeep)); maps.Add(("land-none", p.LandNone));
        var ui = new List<(string, string)> { ("paper", p.Paper), ("sheet", p.Sheet), ("hover", p.Hover) };
        for (int i = 0; i < p.Wash.Length; i++) ui.Add(("wash." + Palette.InkNames[i], p.Wash[i]));

        foreach (var (pn, ph, lim) in plates)
        {
            var plate = ColorMath.FromHex(ph);
            // Map and globe: shaded backgrounds. An alert there always has the sheet halo (#326): a weak direct contrast passes when the halo holds.
            foreach (var (bn, bh) in maps)
            {
                var w = Measure(plate, sheet, ColorMath.FromHex(bh), true);
                bool direct = w.DeltaE >= lim.DeltaE && w.DeltaL >= lim.DeltaL && w.Cvd >= lim.Cvd;
                var wp = Measure(plate, sheet, sheet, false);
                bool haloOk = w.Halo >= HaloVsBackground && wp.DeltaE >= lim.DeltaE && wp.DeltaL >= lim.DeltaL && wp.Cvd >= lim.Cvd;
                string how = direct ? "direct; " : haloOk ? "via halo; " : "";
                res.Add(new PairResult($"{pn} on {bn} (shaded)", direct || haloOk,
                    $"{how}dE {F(w.DeltaE)} dL {F(w.DeltaL)} cvd {F(w.Cvd)} | halo-bg {F(w.Halo)}, plate-halo dE {F(wp.DeltaE)} dL {F(wp.DeltaL)} cvd {F(wp.Cvd)}"));
            }
            // Interface: no shading, no halo, direct contact.
            foreach (var (bn, bh) in ui)
            {
                var w = Measure(plate, sheet, ColorMath.FromHex(bh), false);
                bool direct = w.DeltaE >= lim.DeltaE && w.DeltaL >= lim.DeltaL && w.Cvd >= lim.Cvd;
                res.Add(new PairResult($"{pn} on {bn}", direct, $"dE {F(w.DeltaE)} dL {F(w.DeltaL)} cvd {F(w.Cvd)}"));
            }
            double ch = ColorMath.OkChroma(plate);
            res.Add(new PairResult($"{pn} chroma", ch >= lim.Chroma, $"OKLCH C {F(ch, 3)} (min {F(lim.Chroma, 3)})"));
        }

        // Pairwise tints and tint vs sea at every shade (design system §2.2: closes the shaded palette of review v0.5a §5).
        var named = new List<(string, ColorMath.Lin)>();
        for (int i = 0; i < p.Tints.Length; i++) named.Add(("tint." + (i + 1), ColorMath.FromHex(p.Tints[i])));
        int seaIdx = named.Count;
        named.Add(("sea", ColorMath.FromHex(p.Sea)));
        for (int i = 0; i < seaIdx; i++)
            for (int j = i + 1; j < named.Count; j++)
            {
                double worst = double.MaxValue; string at = "";
                foreach (var s in Shades)
                {
                    if (s.Add > 0) continue;   // the glint lies on the sea only, not between land tints
                    double d = ColorMath.DeltaE2000(Apply(named[i].Item2, s), Apply(named[j].Item2, s));
                    if (d < worst) { worst = d; at = s.Name; }
                }
                res.Add(new PairResult($"{named[i].Item1} vs {named[j].Item1}", worst >= TintPair, $"dE {F(worst)} at {at} (min {F(TintPair, 0)})"));
            }

        for (int i = 0; i < p.Tints.Length; i++)
        {
            double c = ColorMath.OkChroma(ColorMath.FromHex(p.Tints[i]));
            res.Add(new PairResult($"tint.{i + 1} chroma", c <= DecorChroma, $"OKLCH C {F(c, 3)} (max {F(DecorChroma, 2)})"));
        }
        return res;
    }

    /// <summary>Thresholds of the instrument layers, UI_INSTRUMENTS §5 [Предл.].</summary>
    public const double L1DeMin = 6, L1DeMax = 14, L1HatchDeMin = 3, L1ChromaMax = 0.06, L1WcagMax = 1.6, L1VsInkMin = 40;
    public const double InkWcag = 7, SoftInkWcag = 4.5, LineWcag = 3, WashDeMin = 6, LineChromaMax = 0.16, WashChromaMax = 0.13;
    static readonly double[] TextureK = { 0.94, 0.955, 1.0, 1.02 };

    /// <summary>Instrument rules of UI_INSTRUMENTS §5: layer 1 (desk-line, desk-hatch on the desk) is visible but quiet, layer 2 (ink and the six inks on sheet and paper) is legible.
    /// <paramref name="p"/> supplies the six inks and surfaces; desk and layer-1 strokes are the Palette tokens.</summary>
    public static IReadOnlyList<PairResult> RunInstruments(PaletteSet p, string desk = Palette.Desk, string deskLine = Palette.DeskLine, string deskHatch = Palette.DeskHatch)
    {
        var res = new List<PairResult>();
        var d0 = ColorMath.FromHex(desk);
        foreach (var (key, hex, hatch) in new[] { ("desk-line", deskLine, false), ("desk-hatch", deskHatch, true) })
        {
            double lo = double.MaxValue, hi = 0;
            foreach (double k in TextureK)
            {
                // the line against the shaded desk, and both shaded alike (the texture sits under and over the stroke)
                double a = ColorMath.DeltaE2000(ColorMath.FromHex(desk, k), ColorMath.FromHex(hex, k)), b = ColorMath.DeltaE2000(ColorMath.FromHex(desk, k), ColorMath.FromHex(hex));
                lo = Math.Min(lo, Math.Min(a, b)); hi = Math.Max(hi, Math.Max(a, b));
            }
            double min = hatch ? L1HatchDeMin : L1DeMin;
            res.Add(new PairResult($"L1 {key} vs desk dE00 in [{F(min, 0)}, {F(L1DeMax, 0)}]", lo >= min && hi <= L1DeMax, $"min {F(lo)} max {F(hi)}"));
            double c = ColorMath.OkChroma(ColorMath.FromHex(hex));
            res.Add(new PairResult($"L1 {key} OKLCH chroma <= {F(L1ChromaMax, 2)}", c <= L1ChromaMax, $"C {F(c, 3)}"));
        }
        double w = ColorMath.Contrast(ColorMath.FromHex(deskLine), d0);
        res.Add(new PairResult($"L1 desk-line vs desk WCAG < {F(L1WcagMax)} (never reads as a control)", w < L1WcagMax, F(w, 2)));
        double cap = ColorMath.Contrast(ColorMath.FromHex(desk, 0.94), ColorMath.FromHex(Palette.Ink));
        res.Add(new PairResult($"ink caption on desk x0.94 WCAG >= {F(SoftInkWcag)}", cap >= SoftInkWcag, F(cap, 2)));
        double sep = ColorMath.DeltaE2000(ColorMath.FromHex(deskLine), ColorMath.FromHex(Palette.Ink));
        res.Add(new PairResult($"L1 desk-line vs L2 ink dE00 >= {F(L1VsInkMin, 0)} (the layers never confusable)", sep >= L1VsInkMin, F(sep)));

        double Worst(string fg, string bg, params double[] ks)
        {
            double m = double.MaxValue;
            foreach (double k in ks) m = Math.Min(m, ColorMath.Contrast(ColorMath.FromHex(bg, k), ColorMath.FromHex(fg)));
            return m;
        }
        foreach (var (hex, name, need) in new[] { (Palette.Ink, "ink strokes", InkWcag), (Palette.Ink2, "ink-2 ticks and labels", SoftInkWcag), (Palette.Ink3, "ink-3 minor ticks", SoftInkWcag) })
        {
            double r = Math.Min(Worst(hex, p.Sheet, 0.94, 1.0), Worst(hex, p.Paper, 0.94, 1.0));
            res.Add(new PairResult($"L2 {name} on sheet/paper WCAG >= {F(need)}", r >= need, F(r, 2)));
        }
        for (int i = 0; i < p.Line.Length; i++)
        {
            string n = Palette.InkNames[i];
            double r = Worst(p.Line[i], p.Sheet, 0.955, 1.0);
            res.Add(new PairResult($"L2 line.{n} on sheet WCAG >= {F(LineWcag)}", r >= LineWcag, F(r, 2)));
            double de = double.MaxValue;
            foreach (double k in new[] { 0.955, 1.0, 1.02 }) de = Math.Min(de, ColorMath.DeltaE2000(ColorMath.FromHex(p.Sheet, k), ColorMath.FromHex(p.Wash[i], k)));
            res.Add(new PairResult($"L2 wash.{n} vs sheet dE00 >= {F(WashDeMin, 0)}", de >= WashDeMin, F(de)));
            double inkOn = Worst(Palette.Ink, p.Wash[i], 0.94, 1.0);
            res.Add(new PairResult($"L2 ink on wash.{n} WCAG >= {F(InkWcag)}", inkOn >= InkWcag, F(inkOn, 2)));
            double cw = ColorMath.OkChroma(ColorMath.FromHex(p.Wash[i])), cl = ColorMath.OkChroma(ColorMath.FromHex(p.Line[i]));
            res.Add(new PairResult($"L2 wash.{n} OKLCH chroma <= {F(WashChromaMax, 2)}", cw <= WashChromaMax, F(cw, 3)));
            res.Add(new PairResult($"L2 line.{n} OKLCH chroma <= {F(LineChromaMax, 2)}", cl <= LineChromaMax, F(cl, 3)));
            double rt = Worst(p.Text[i], p.Sheet, 0.94, 1.0);
            res.Add(new PairResult($"L2 text.{n} on sheet x0.94 WCAG >= {F(SoftInkWcag)}", rt >= SoftInkWcag, F(rt, 2)));
        }
        return res;
    }

    public static string Format(PaletteSet p, IReadOnlyList<PairResult> results)
    {
        var sb = new StringBuilder();
        int fail = 0;
        sb.Append($"alert-contrast validator, palette {p.Name}\n");
        foreach (var r in results)
        {
            if (!r.Pass) fail++;
            sb.Append(r.Pass ? "PASS " : "FAIL ").Append(r.Name).Append(": ").Append(r.Detail).Append('\n');
        }
        sb.Append($"validator verdict: {(fail == 0 ? "PASS" : "FAIL")} ({results.Count - fail}/{results.Count} pairs pass)\n");
        return sb.ToString();
    }
}
