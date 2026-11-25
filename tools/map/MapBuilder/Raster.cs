using System.IO.Compression;
using NetTopologySuite.Geometries;

namespace Singularity.MapBuilder;

/// <summary>A global raster on the lat/lon grid with <see cref="PerDeg"/> cells per degree, cell (0, 0) at 180W, 90N.</summary>
internal interface IGrid
{
    int PerDeg { get; }
    /// <summary>Fills dst[0..x1-x0) with the cells of row y; <see cref="Raster.NoData"/> where the raster has no value.</summary>
    void ReadRow(int y, int x0, int x1, int[] dst);
}

internal static class Raster
{
    public const int NoData = int.MinValue;

    /// <summary>Row weight: area of the cell row on the unit sphere, in 1e-9 units (integer, so sums are exact). The only trigonometry of the bake.</summary>
    public static long[] RowWeights(int perDeg)
    {
        int h = 180 * perDeg;
        var w = new long[h];
        for (int y = 0; y < h; y++)
        {
            double top = (90.0 - (double)y / perDeg) * Math.PI / 180.0, bot = (90.0 - (double)(y + 1) / perDeg) * Math.PI / 180.0;
            w[y] = (long)Math.Round((Math.Sin(top) - Math.Sin(bot)) * 1e9, MidpointRounding.ToEven);
        }
        return w;
    }

    /// <summary>Latitude of the centre of row y, degrees.</summary>
    public static double RowLat(int y, int perDeg) => 90.0 - (y + 0.5) / perDeg;

    public readonly record struct Span(int Y, int X0, int X1);

    /// <summary>
    /// Scanline rasterisation: the cells whose centres lie inside the (multi)polygon (even-odd rule, holes included), as row spans.
    /// Same polygon and grid give the same spans (IEEE doubles, no transcendental functions).
    /// </summary>
    public static List<Span> Spans(Geometry g, int perDeg)
    {
        var edges = new List<(double X1, double Y1, double X2, double Y2, int R0, int R1)>();
        void Ring(Coordinate[] c)
        {
            for (int i = 1; i < c.Length; i++)
            {
                var a = c[i - 1]; var b = c[i];
                if (a.Y == b.Y) continue;
                var (lo, hi) = a.Y < b.Y ? (a, b) : (b, a);
                int r0 = (int)Math.Floor((90.0 - hi.Y) * perDeg - 0.5) + 1, r1 = (int)Math.Floor((90.0 - lo.Y) * perDeg - 0.5);
                if (r1 < r0) continue;
                edges.Add((lo.X, lo.Y, hi.X, hi.Y, r0, r1));
            }
        }
        void Walk(Geometry x)
        {
            if (x is Polygon p) { Ring(p.ExteriorRing.Coordinates); for (int i = 0; i < p.NumInteriorRings; i++) Ring(p.GetInteriorRingN(i).Coordinates); }
            else for (int i = 0; i < x.NumGeometries; i++) Walk(x.GetGeometryN(i));
        }
        Walk(g);
        var res = new List<Span>();
        if (edges.Count == 0) return res;
        edges.Sort((a, b) => a.R0 != b.R0 ? a.R0.CompareTo(b.R0) : a.X1.CompareTo(b.X1));
        int next = 0, rowMax = edges.Max(e => e.R1), width = 360 * perDeg;
        var active = new List<int>();
        var xs = new List<double>();
        for (int r = edges[0].R0; r <= rowMax; r++)
        {
            while (next < edges.Count && edges[next].R0 <= r) active.Add(next++);
            double lat = RowLat(r, perDeg);
            xs.Clear();
            for (int k = active.Count - 1; k >= 0; k--)
            {
                var e = edges[active[k]];
                if (e.R1 < r) { active.RemoveAt(k); continue; }
                xs.Add(e.X1 + (lat - e.Y1) * (e.X2 - e.X1) / (e.Y2 - e.Y1));
            }
            xs.Sort();
            for (int i = 0; i + 1 < xs.Count; i += 2)
            {
                int x0 = Math.Max(0, (int)Math.Ceiling((xs[i] + 180.0) * perDeg - 0.5));
                int x1 = Math.Min(width, (int)Math.Ceiling((xs[i + 1] + 180.0) * perDeg - 0.5));
                if (x1 > x0) res.Add(new Span(r, x0, x1));
            }
        }
        return res;
    }

    /// <summary>The single cell holding a point (used for provinces smaller than one cell).</summary>
    public static Span CellAt(double lon, double lat, int perDeg)
    {
        int x = Math.Clamp((int)Math.Floor((lon + 180.0) * perDeg), 0, 360 * perDeg - 1);
        int y = Math.Clamp((int)Math.Floor((90.0 - lat) * perDeg), 0, 180 * perDeg - 1);
        return new Span(y, x, x + 1);
    }
}

/// <summary>Least recently used cache of decoded blocks.</summary>
internal sealed class Lru<TKey, TValue> where TKey : notnull
{
    private readonly int cap;
    private readonly Dictionary<TKey, LinkedListNode<(TKey K, TValue V)>> map = new();
    private readonly LinkedList<(TKey K, TValue V)> order = new();
    public Lru(int cap) { this.cap = cap; }
    public TValue GetOrAdd(TKey k, Func<TKey, TValue> load)
    {
        if (map.TryGetValue(k, out var n)) { order.Remove(n); order.AddFirst(n); return n.Value.V; }
        var v = load(k);
        map[k] = order.AddFirst((k, v));
        if (map.Count > cap) { map.Remove(order.Last!.Value.K); order.RemoveLast(); }
        return v;
    }
}

/// <summary>A set of GeoTIFF files (GMTED2010 tiles, GLWD v2) as one global grid. The files are read as plain data.</summary>
internal sealed class TiffMosaic : IGrid, IDisposable
{
    private readonly record struct Tile(TiffImage Img, int XOff, int YOff, Microsoft.Win32.SafeHandles.SafeFileHandle Handle);
    private readonly List<Tile> tiles = new();
    private readonly Lru<(int, int), int[]> cache;
    public int PerDeg { get; }

    public TiffMosaic(IEnumerable<string> paths, int perDeg)
    {
        PerDeg = perDeg;
        foreach (var path in paths)
        {
            var h = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
            TiffImage img;
            try { img = TiffImage.Open((off, n) => { var b = new byte[n]; RandomAccess.Read(h, b, off); return b; }); }
            catch (InvalidDataException e) { throw new InvalidDataException($"{path}: {e.Message}"); }
            if (Math.Abs(img.PixelX * perDeg - 1) > 1e-6) throw new InvalidDataException($"{path}: pixel size {img.PixelX} is not 1/{perDeg} degree");
            int xo = (int)Math.Round((img.OriginLon + 180.0) * perDeg), yo = (int)Math.Round((90.0 - img.OriginLat) * perDeg);
            tiles.Add(new Tile(img, xo, yo, h));
        }
        // A province row crosses many blocks: keep about 256 MB of decoded blocks so a row is not decoded again for the next row.
        cache = new Lru<(int, int), int[]>((int)Math.Clamp((256L << 20) / (4L * tiles.Max(t => t.Img.BlockW * t.Img.BlockH)), 8, 8192));
    }

    public void ReadRow(int y, int x0, int x1, int[] dst)
    {
        Array.Fill(dst, Raster.NoData, 0, x1 - x0);
        for (int t = 0; t < tiles.Count; t++)
        {
            var (img, xo, yo, _) = tiles[t];
            int ly = y - yo;
            if (ly < 0 || ly >= img.Height) continue;
            int a = Math.Max(x0, xo), b = Math.Min(x1, xo + img.Width);
            if (a >= b) continue;
            int by = ly / img.BlockH;
            for (int bx = (a - xo) / img.BlockW; bx <= (b - 1 - xo) / img.BlockW; bx++)
            {
                int tt = t, idx = by * img.BlocksAcross + bx;
                var blk = cache.GetOrAdd((tt, idx), _ => img.ReadBlock(idx));
                int rowInBlock = ly - by * img.BlockH;
                int bxStart = xo + bx * img.BlockW;
                int from = Math.Max(a, bxStart), to = Math.Min(b, Math.Min(bxStart + img.BlockW, xo + img.Width));
                for (int x = from; x < to; x++)
                {
                    int v = blk[rowInBlock * img.BlockW + (x - bxStart)];
                    dst[x - x0] = img.NoData == v ? Raster.NoData : v;
                }
            }
        }
    }

    public void Dispose() { foreach (var t in tiles) t.Handle.Dispose(); }
}

/// <summary>ESA WorldCover overview tiles (3x3 degrees, one byte per cell, gzip) as one global grid.</summary>
internal sealed class CogGrid : IGrid
{
    private readonly string dir;
    private readonly int level;
    private readonly Lru<string, byte[]?> cache = new(40);
    public int PerDeg { get; }

    public CogGrid(string dir, int level) { this.dir = dir; this.level = level; PerDeg = level / 3; }

    public static string TileName(int lat0, int lon0) => $"{(lat0 < 0 ? 'S' : 'N')}{Math.Abs(lat0):00}{(lon0 < 0 ? 'W' : 'E')}{Math.Abs(lon0):000}";
    public static string TilePath(string dir, string tile, int level) => Path.Combine(dir, $"{tile}.l{level}.gz");

    private byte[]? Load(string tile)
    {
        var path = TilePath(dir, tile, level);
        if (!File.Exists(path)) return null;
        using var z = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        var raw = new byte[level * level];
        z.ReadExactly(raw);
        return raw;
    }

    public void ReadRow(int y, int x0, int x1, int[] dst)
    {
        Array.Fill(dst, Raster.NoData, 0, x1 - x0);
        double lat = Raster.RowLat(y, PerDeg);
        int lat0 = (int)Math.Floor(lat / 3.0) * 3;
        int ry = y - (90 - (lat0 + 3)) * PerDeg;
        for (int x = x0; x < x1;)
        {
            int lon0 = (int)Math.Floor(((x + 0.5) / PerDeg - 180.0) / 3.0) * 3;
            int cellEnd = Math.Min(x1, (lon0 + 180 + 3) * PerDeg);
            var data = cache.GetOrAdd(TileName(lat0, lon0), Load);
            if (data != null)
            {
                int xo = (lon0 + 180) * PerDeg;
                for (int i = x; i < cellEnd; i++) { int v = data[ry * level + (i - xo)]; if (v != 0) dst[i - x0] = v; }
            }
            x = cellEnd;
        }
    }
}
