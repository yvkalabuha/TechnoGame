using System.IO.Compression;
using NetTopologySuite.Geometries;

namespace Singularity.MapBuilder;

/// <summary>Debug render for review: owner fill, controller hatching, thick owner borders (software rasteriser, own PNG writer, no imaging library).</summary>
internal static class Render
{
    private const double Lon0 = -25, Lon1 = 60, Lat0 = 34, Lat1 = 72, Scale = 36;

    public static List<(string Polity, uint Rgb)> Palette(BuildResult r)
    {
        var res = new List<(string, uint)>();
        var states = r.Polities.Where(p => p.Kind == "state").OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
        int n = states.Count;
        for (int i = 0; i < n; i++)
        {
            // Hue steps by 13/n of the circle (13 is coprime with the typical n), lightness and saturation alternate.
            int hue = i * 13 % Math.Max(n, 1) * 360 / Math.Max(n, 1);
            uint c = Hsl(hue, 55 + i % 2 * 20, 48 + i % 3 * 10);
            if (states[i].Id == "DEU") c = 0x8A8A8A;     // Germany under occupation: neutral grey
            if (states[i].Id == "SUN") c = 0xC8463C;     // the one actor that must stand out
            res.Add((states[i].Id, c));
        }
        return res;
    }

    private static uint Hsl(int h, int s, int l)
    {
        // Integer HSL to RGB (no floating point, reproducible).
        int c = (100 - Math.Abs(2 * l - 100)) * s / 100;           // 0..100
        int hp = h % 360 * 100 / 60;                                // 0..600
        int x = c * (100 - Math.Abs(hp % 200 - 100)) / 100;
        int m = l - c / 2;
        (int R, int G, int B) = (hp / 100) switch { 0 => (c, x, 0), 1 => (x, c, 0), 2 => (0, c, x), 3 => (0, x, c), 4 => (x, 0, c), _ => (c, 0, x) };
        static int Ch(int v) => Math.Clamp((v * 255 + 50) / 100, 0, 255);
        return (uint)(Ch(R + m) << 16 | Ch(G + m) << 8 | Ch(B + m));
    }

    public static void Png(BuildResult r, string path)
    {
        double k = Math.Cos(53 * Math.PI / 180);
        int w = (int)((Lon1 - Lon0) * k * Scale), h = (int)((Lat1 - Lat0) * Scale);
        var ids = new int[w * h];
        Array.Fill(ids, -1);
        for (int p = 0; p < r.Simplified.Length; p++) Fill(r.Simplified[p], p, ids, w, h, k);

        var pal = Palette(r).ToDictionary(x => x.Polity, x => x.Rgb, StringComparer.Ordinal);
        var polity = r.Polities.ToDictionary(p => p.Id, StringComparer.Ordinal);
        uint Col(string id) => pal.GetValueOrDefault(id, 0x808080u);
        uint[] Colours(string id) =>
            polity.TryGetValue(id, out var pe) && pe.Kind == "joint" ? pe.Members.Select(Col).ToArray() : new[] { Col(id) };

        var owner = new string[r.Pieces.Count]; var ctrl = new string[r.Pieces.Count];
        for (int i = 0; i < r.Pieces.Count; i++)
        {
            var o = r.Owners.GetValueOrDefault(r.Pieces[i].Id);
            owner[i] = o?.Owner ?? ""; ctrl[i] = o?.Controller ?? "";
        }
        var px = new uint[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int id = ids[y * w + x];
                if (id < 0) { px[y * w + x] = 0xC9D9E6; continue; }
                var oc = Colours(owner[id]);
                uint c = oc[0];
                if (oc.Length > 1 && (x + y) / 5 % 2 == 1) c = oc[1];
                if (ctrl[id] != owner[id])
                {
                    var cc = Colours(ctrl[id]);
                    int band = (x + y) / 4;
                    if (band % 2 == 0) c = cc[band / 2 % cc.Length];
                }
                px[y * w + x] = c;
            }
        // Borders: province lines are light, owner or controller changes are dark and 2 px wide.
        var line = new uint[w * h];
        for (int y = 0; y < h - 1; y++)
            for (int x = 0; x < w - 1; x++)
            {
                int a = ids[y * w + x];
                foreach (var b in new[] { ids[y * w + x + 1], ids[(y + 1) * w + x] })
                {
                    if (a == b) continue;
                    bool state = a < 0 || b < 0 || owner[a] != owner[b];
                    if (state) { line[y * w + x] = 2; line[Math.Min(h - 1, y + 1) * w + x] = 2; line[y * w + x + 1] = Math.Max(line[y * w + x + 1], 2); }
                    else if (line[y * w + x] == 0) line[y * w + x] = 1;
                }
            }
        for (int i = 0; i < px.Length; i++)
        {
            if (line[i] == 2) px[i] = 0x202020;
            else if (line[i] == 1) px[i] = Darken(px[i]);
        }
        File.WriteAllBytes(path, Encode(px, w, h));
    }

    private static uint Darken(uint c) => ((c >> 16 & 0xFF) * 7 / 10) << 16 | ((c >> 8 & 0xFF) * 7 / 10) << 8 | (c & 0xFF) * 7 / 10;

    private static void Fill(Geometry g, int id, int[] ids, int w, int h, double k)
    {
        foreach (var poly in Sph.Polygons(g))
        {
            var edges = new List<(double X0, double Y0, double X1, double Y1)>();
            void AddRing(Coordinate[] cs)
            {
                for (int i = 1; i < cs.Length; i++)
                    edges.Add(((cs[i - 1].X - Lon0) * k * Scale, (Lat1 - cs[i - 1].Y) * Scale, (cs[i].X - Lon0) * k * Scale, (Lat1 - cs[i].Y) * Scale));
            }
            AddRing(poly.ExteriorRing.Coordinates);
            for (int i = 0; i < poly.NumInteriorRings; i++) AddRing(poly.GetInteriorRingN(i).Coordinates);
            double minY = edges.Min(e => Math.Min(e.Y0, e.Y1)), maxY = edges.Max(e => Math.Max(e.Y0, e.Y1));
            for (int y = Math.Max(0, (int)Math.Floor(minY)); y <= Math.Min(h - 1, (int)Math.Ceiling(maxY)); y++)
            {
                double yc = y + 0.5;
                var xs = new List<double>();
                foreach (var e in edges)
                {
                    if ((e.Y0 <= yc) == (e.Y1 <= yc)) continue;
                    xs.Add(e.X0 + (yc - e.Y0) / (e.Y1 - e.Y0) * (e.X1 - e.X0));
                }
                xs.Sort();
                for (int i = 0; i + 1 < xs.Count; i += 2)
                    for (int x = Math.Max(0, (int)Math.Ceiling(xs[i] - 0.5)); x <= Math.Min(w - 1, (int)Math.Ceiling(xs[i + 1] - 0.5) - 1); x++)
                        ids[y * w + x] = id;
            }
        }
    }

    private static uint Crc(byte[] data, uint crc = 0xFFFFFFFF)
    {
        foreach (var b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        }
        return crc;
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        var len = new byte[] { (byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length };
        s.Write(len);
        s.Write(t);
        s.Write(data);
        uint crc = ~Crc(data, Crc(t));
        s.Write(new[] { (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc });
    }

    public static byte[] Encode(uint[] px, int w, int h)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        Chunk(ms, "IHDR", new byte[] { (byte)(w >> 24), (byte)(w >> 16), (byte)(w >> 8), (byte)w, (byte)(h >> 24), (byte)(h >> 16), (byte)(h >> 8), (byte)h, 8, 2, 0, 0, 0 });
        var raw = new byte[h * (w * 3 + 1)];
        int o = 0;
        for (int y = 0; y < h; y++)
        {
            raw[o++] = 0;
            for (int x = 0; x < w; x++)
            {
                uint c = px[y * w + x];
                raw[o++] = (byte)(c >> 16); raw[o++] = (byte)(c >> 8); raw[o++] = (byte)c;
            }
        }
        using var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, true)) zs.Write(raw);
        Chunk(ms, "IDAT", z.ToArray());
        Chunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }
}
