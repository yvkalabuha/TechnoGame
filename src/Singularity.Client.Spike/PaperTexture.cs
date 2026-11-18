using System;

namespace Singularity.Client.Spike;

public enum SurfaceKind { Sheet, Paper, Button }

/// <summary>Amplitudes of the three noise channels (grain, fibre, blotch), the edge wear depth and its width in du.</summary>
public readonly record struct SurfaceParams(double Grain, double Fibre, double Blotch, double Wear, double WearWidth);

/// <summary>Procedural paper texture (CLIENT_V05C_SPEC §3): a luminance-only multiplier k on the surface colour, hard-capped to [0.94, 1.02].
/// Godot-free: the baked tile, the parameter table and a CPU copy of the shader formula (shaders/paper_surface.gdshader) live here, so the limits are tested and
/// the validator reads the same numbers. Purely cosmetic and unrelated to the simulation (it has its own hash).</summary>
public static class PaperTexture
{
    public const double KMin = 0.94, KMax = 1.02;
    public const int TileSize = 256;          // pixels; one tile covers 128 du
    public const double TileDu = 128;

    /// <summary>Settings presets: 0 off, 1 weak (x0.5), 2 normal (x1). Nothing stronger exists.</summary>
    public static double Strength(int preset) => preset switch { 0 => 0, 1 => 0.5, _ => 1 };

    public static SurfaceParams Params(SurfaceKind k, bool pressed = false) => k switch
    {
        SurfaceKind.Sheet => new(0.015, 0.020, 0.020, 0.04, 6),
        SurfaceKind.Paper => new(0.015, 0.020, 0.025, 0.05, 6),
        _ => pressed ? new(0.010, 0.010, 0.010, 0.05, 5) : new(0.015, 0.010, 0.010, 0.05, 4),   // pressed: K adds the x1.5 wear (0.075 in the spec table)
    };

    /// <summary>The shader formula. n = tile sample 0..1 per channel, edge = distance to the rect edge in du, press 0..1, rub = 0..1 weight of the pressed-centre oval.</summary>
    public static double K(SurfaceParams p, double nr, double ng, double nb, double edge, double press, double rub, double strength)
    {
        double wearMask = 1.0 - Smooth(0, p.WearWidth, edge + 1.5 * (nb - 0.5));
        double k = 1.0 + 2.0 * (p.Grain * (nr - 0.5) + p.Fibre * (ng - 0.5) + p.Blotch * (nb - 0.5))
                   - p.Wear * wearMask * (1.0 + 0.5 * press) + 0.02 * press * rub;
        k = 1.0 + strength * (k - 1.0);
        return Math.Clamp(k, KMin, KMax);
    }

    static readonly Lazy<byte[]> Tile = new(() => BakeTile());

    /// <summary>Range of k that the baked tile really produces away from the edges (strength 1, nothing pressed). The validator uses it for T5: an alert plate sits
    /// at least one wear width inside its surface, so the edge wear does not reach it.</summary>
    public static (double Min, double Max) InteriorRange(SurfaceKind kind)
    {
        var t = Tile.Value; var p = Params(kind);
        double lo = double.MaxValue, hi = double.MinValue;
        for (int i = 0; i < t.Length; i += 3)
        {
            double k = K(p, t[i] / 255.0, t[i + 1] / 255.0, t[i + 2] / 255.0, 1e6, 0, 0, 1);
            lo = Math.Min(lo, k); hi = Math.Max(hi, k);
        }
        return (lo, hi);
    }

    static double Smooth(double a, double b, double x) { double t = Math.Clamp((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); }

    /// <summary>Baked once at start with a fixed seed: R fine grain, G fibre (anisotropic, long strokes), B blotches (period over 60 du). RGB8, seamless.</summary>
    public static byte[] BakeTile(int seed = 7)
    {
        var data = new byte[TileSize * TileSize * 3];
        for (int y = 0; y < TileSize; y++)
            for (int x = 0; x < TileSize; x++)
            {
                double r = Lattice(seed, 0, x, y, 2, 2);                                                   // cell 1 du: grain
                double g = 0.65 * Lattice(seed, 1, x, y, 32, 2) + 0.35 * Lattice(seed, 2, x, y, 64, 4);    // 16-32 du long, 1-2 du thick
                double b = 0.7 * Lattice(seed, 3, x, y, 128, 128) + 0.3 * Lattice(seed, 4, x, y, 64, 128); // 64 du and longer
                int i = (y * TileSize + x) * 3;
                data[i] = To8(r); data[i + 1] = To8(g); data[i + 2] = To8(b);
            }
        return data;
    }

    static byte To8(double v) => (byte)Math.Clamp((int)Math.Round((v - 0.5) * 0.9 * 255 + 127.5), 0, 255);   // soft value noise as is (a stretch to the full range would break the T5 margin of warn)

    // Value noise on a wrapping lattice with cells of cw x ch pixels (the tile size is a multiple of both, so the tile repeats seamlessly).
    static double Lattice(int seed, int channel, int x, int y, int cw, int ch)
    {
        int gx = TileSize / cw, gy = TileSize / ch;
        double fx = (double)x / cw, fy = (double)y / ch;
        int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
        double tx = Smooth(0, 1, fx - x0), ty = Smooth(0, 1, fy - y0);
        double a = Hash(seed, channel, x0 % gx, y0 % gy), b = Hash(seed, channel, (x0 + 1) % gx, y0 % gy);
        double c = Hash(seed, channel, x0 % gx, (y0 + 1) % gy), d = Hash(seed, channel, (x0 + 1) % gx, (y0 + 1) % gy);
        return (a + (b - a) * tx) * (1 - ty) + (c + (d - c) * tx) * ty;
    }

    static double Hash(int seed, int channel, int ix, int iy)
    {
        unchecked
        {
            uint h = (uint)ix * 374761393u + (uint)iy * 668265263u + (uint)seed * 2246822519u + (uint)channel * 3266489917u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return h / (double)uint.MaxValue;
        }
    }

    /// <summary>Stable 0..1 pair from a node name (tile offset and the position of the pressed-centre oval); FNV-1a, never the process hash.</summary>
    public static (double A, double B) NameHash(string name)
    {
        unchecked
        {
            uint h = 2166136261u;
            foreach (char c in name) { h ^= c; h *= 16777619u; }
            return ((h & 0xFFFF) / 65535.0, ((h >> 16) & 0xFFFF) / 65535.0);
        }
    }
}
