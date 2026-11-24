using System;

namespace Singularity.Client.Spike;

/// <summary>Globe look (TASK-0137): which variant is shown. B (physical atmosphere) is the default, owner decision #444; Current is the prior flat look.</summary>
public enum GlobeLook { Current = 0, A = 1, APlus = 2, B = 3 }

public static class GlobeLookLogic
{
    /// <summary>Owner decision #444: the physical atmosphere is the default look.</summary>
    public const GlobeLook Default = GlobeLook.B;

    public static readonly string[] Names = { "current", "a", "aplus", "b" };

    /// <summary>Launch flag value to look; unknown or empty text gives the default look.</summary>
    public static GlobeLook Parse(string s)
    {
        if (s != null)
            for (int i = 0; i < Names.Length; i++)
                if (string.Equals(Names[i], s.Trim().Replace("+", "plus"), StringComparison.OrdinalIgnoreCase)) return (GlobeLook)i;
        return Default;
    }

    public static GlobeLook Next(GlobeLook l) => (GlobeLook)(((int)l + 1) % Names.Length);

    /// <summary>Unit vector towards a light at lon/lat, in the same axes as GlobeData.ToSphere.</summary>
    public static (float X, float Y, float Z) Direction(float lonDeg, float latDeg)
    {
        float lo = lonDeg * MathF.PI / 180f, la = latDeg * MathF.PI / 180f;
        return (MathF.Cos(la) * MathF.Sin(lo), MathF.Sin(la), MathF.Cos(la) * MathF.Cos(lo));
    }

    static double Lin(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    static double F(double t) => t > 216.0 / 24389.0 ? Math.Cbrt(t) : (24389.0 / 27.0 * t + 16.0) / 116.0;

    /// <summary>CIE76 colour difference between two sRGB colours (channels 0..1, D65).</summary>
    public static double DeltaE(double r1, double g1, double b1, double r2, double g2, double b2)
    {
        var a = Lab(r1, g1, b1); var b = Lab(r2, g2, b2);
        double dl = a.L - b.L, da = a.A - b.A, db = a.B - b.B;
        return Math.Sqrt(dl * dl + da * da + db * db);
    }

    static (double L, double A, double B) Lab(double r, double g, double b)
    {
        double lr = Lin(r), lg = Lin(g), lb = Lin(b);
        double x = (0.4124564 * lr + 0.3575761 * lg + 0.1804375 * lb) / 0.95047;
        double y = 0.2126729 * lr + 0.7151522 * lg + 0.0721750 * lb;
        double z = (0.0193339 * lr + 0.1191920 * lg + 0.9503041 * lb) / 1.08883;
        double fx = F(x), fy = F(y), fz = F(z);
        return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    /// <summary>The atmosphere may colour the disk only inside this share of the radius from the limb (VISUAL_DIRECTION_V06 §2 B, #444).</summary>
    public const double InwardLimit = 0.04;

    /// <summary>Largest tolerated ΔE (CIE76) of a fill under the atmosphere at 0.96 R against the same fill without it.</summary>
    public const double MaxLimbDeltaE = 2.0;

    /// <summary>Largest tolerated atmosphere share at 0.98 R (halfway into the 4 % band, eased in): measured, then fixed with margin (art-director review of TASK-0137).</summary>
    public const double MaxInnerDeltaE = 6.0;

    /// <summary>Angle in degrees between the view axis and a globe point whose screen distance from the disk centre is ratio * (disk radius), for a camera at distance dist (globe radius 1).</summary>
    public static double AngleForLimbRatio(double dist, double ratio)
    {
        double tanLimb = 1.0 / Math.Sqrt(dist * dist - 1.0), lo = 0, hi = Math.Acos(1.0 / dist);
        for (int i = 0; i < 60; i++)
        {
            double th = 0.5 * (lo + hi);
            double r = Math.Sin(th) / (dist - Math.Cos(th)) / tanLimb;
            if (r < ratio) lo = th; else hi = th;
        }
        return 0.5 * (lo + hi) * 180.0 / Math.PI;
    }
}
