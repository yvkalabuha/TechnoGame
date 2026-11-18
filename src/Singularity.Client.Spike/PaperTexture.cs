using System;
using System.Linq;

namespace Singularity.Client.Spike;

public enum SurfaceKind { Sheet, Paper, Button }

/// <summary>Amplitudes of the three noise channels (grain, fibre, blotch), the edge wear depth and its width in du.</summary>
public readonly record struct SurfaceParams(double Grain, double Fibre, double Blotch, double Wear, double WearWidth);

/// <summary>Procedural paper texture (CLIENT_V05C_SPEC §3): a luminance-only multiplier k on the surface colour, hard-capped (interior [0.955, 1.02], edge band down to 0.90).
/// Godot-free: the baked tile, the parameter table and a CPU copy of the shader formula (shaders/paper_surface.gdshader) live here, so the limits are tested and
/// the validator reads the same numbers. Purely cosmetic and unrelated to the simulation (it has its own hash).</summary>
public static class PaperTexture
{
    /// <summary>Hard limits of k (CLIENT_V05C_SPEC §3.3): interior [KInnerMin, KMax]; inside the edge band (e at most BandEdge du) down to KBandMin. Text keeps out of the band (T6).</summary>
    public const double KInnerMin = 0.955, KBandMin = 0.90, KMax = 1.02, BandEdge = 8.5, KCentre = 0.99, Rub = 0.02;
    public const int TileSize = 384;          // pixels; one tile covers 192 du (2 px per du)
    public const double TileDu = 192;

    /// <summary>Settings presets: 0 off, 1 weak (x0.5), 2 normal (x1). Nothing stronger exists.</summary>
    public static double Strength(int preset) => preset switch { 0 => 0, 1 => 0.5, _ => 1 };

    // Wear is 0.100-0.105 (table of the spec: 0.075-0.080): with the spec values the measured edge band (0-6 du) came out 4.3% darker, the acceptance asks 5-9%.
    public static SurfaceParams Params(SurfaceKind k, bool pressed = false) => k switch
    {
        SurfaceKind.Sheet => new(0.020, 0.030, 0.035, 0.100, 7),
        SurfaceKind.Paper => new(0.020, 0.030, 0.040, 0.105, 7),
        _ => pressed ? new(0.015, 0.015, 0.020, 0.080, 6) : new(0.020, 0.015, 0.020, 0.080, 5),   // pressed: K applies the x1.5 wear (0.12, capped at 0.90)
    };

    /// <summary>The shader formula. n = tile sample 0..1 per channel, edge = distance to the rect edge in du, press 0..1, rub = 0..1 weight of the pressed-centre oval.</summary>
    public static double K(SurfaceParams p, double nr, double ng, double nb, double edge, double press, double rub, double strength)
    {
        double kInt = Math.Clamp(KCentre + 2.0 * (p.Grain * (nr - 0.5) + p.Fibre * (ng - 0.5) + p.Blotch * (nb - 0.5)), KInnerMin, KMax);
        double e = edge + 1.5 * (nb - 0.5) + 1.0 * (nr - 0.5);   // rough edge, about +-2 du
        double band = 1.0 - Smooth(0, p.WearWidth, e);
        double k = kInt - p.Wear * band * (1.0 + 0.5 * press) + Rub * press * rub;
        k = 1.0 + strength * (k - 1.0);
        return Math.Clamp(k, edge <= BandEdge ? KBandMin : KInnerMin, KMax);
    }

    static readonly Lazy<byte[]> Tile = new(() => BakeTile());

    /// <summary>Mean and standard deviation of k over the baked tile away from the edges (strength given, nothing pressed): the expected luminance spread of a panel interior.</summary>
    public static (double Mean, double Sigma) InteriorStats(SurfaceKind kind, double strength = 1)
    {
        var t = Tile.Value; var p = Params(kind);
        double sum = 0, sum2 = 0; int n = t.Length / 3;
        for (int i = 0; i < t.Length; i += 3)
        {
            double k = K(p, t[i] / 255.0, t[i + 1] / 255.0, t[i + 2] / 255.0, 1e6, 0, 0, strength);
            sum += k; sum2 += k * k;
        }
        double m = sum / n;
        return (m, Math.Sqrt(Math.Max(0, sum2 / n - m * m)));
    }

    static double Smooth(double a, double b, double x) { double t = Math.Clamp((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); }

    /// <summary>Baked once at start with a fixed seed: R fine grain (2 du), G fibre (anisotropic, 16-24 du long, 1-2 du thick), B blotches (48-96 du cells). RGB8, seamless;
    /// every channel is stretched to the full 0..255 range so that the amplitudes of the table mean what they say.</summary>
    public static byte[] BakeTile(int seed = 7)
    {
        var ch = new double[3][];
        for (int c = 0; c < 3; c++) ch[c] = new double[TileSize * TileSize];
        for (int y = 0; y < TileSize; y++)
            for (int x = 0; x < TileSize; x++)
            {
                int j = y * TileSize + x;
                ch[0][j] = Lattice(seed, 0, x, y, 4, 4);                                                    // 2 du: grain
                ch[1][j] = 0.65 * Lattice(seed, 1, x, y, 32, 2) + 0.35 * Lattice(seed, 2, x, y, 48, 4);      // 16-24 du long, 1-2 du thick
                ch[2][j] = 0.65 * Lattice(seed, 3, x, y, 192, 192) + 0.35 * Lattice(seed, 4, x, y, 192, 96); // 96 du and 96x48 du blotches
            }
        var data = new byte[TileSize * TileSize * 3];
        for (int c = 0; c < 3; c++)
        {
            double lo = ch[c].Min(), hi = ch[c].Max(), span = Math.Max(1e-9, hi - lo);
            for (int j = 0; j < ch[c].Length; j++) data[j * 3 + c] = (byte)Math.Clamp((int)Math.Round((ch[c][j] - lo) / span * 255), 0, 255);
        }
        return data;
    }

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
