using System;

namespace Singularity.Client.Spike;

/// <summary>Globe look prototype (TASK-0137): which of the variants is shown. Current is the default until the owner picks one.</summary>
public enum GlobeLook { Current = 0, A = 1, APlus = 2, B = 3 }

public static class GlobeLookLogic
{
    public static readonly string[] Names = { "current", "a", "aplus", "b" };

    /// <summary>Launch flag value to look; unknown or empty text keeps the current look.</summary>
    public static GlobeLook Parse(string s)
    {
        if (s != null)
            for (int i = 0; i < Names.Length; i++)
                if (string.Equals(Names[i], s.Trim().Replace("+", "plus"), StringComparison.OrdinalIgnoreCase)) return (GlobeLook)i;
        return GlobeLook.Current;
    }

    public static GlobeLook Next(GlobeLook l) => (GlobeLook)(((int)l + 1) % Names.Length);

    /// <summary>Unit vector towards a light at lon/lat, in the same axes as GlobeData.ToSphere.</summary>
    public static (float X, float Y, float Z) Direction(float lonDeg, float latDeg)
    {
        float lo = lonDeg * MathF.PI / 180f, la = latDeg * MathF.PI / 180f;
        return (MathF.Cos(la) * MathF.Sin(lo), MathF.Sin(la), MathF.Cos(la) * MathF.Cos(lo));
    }
}
