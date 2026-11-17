using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace Singularity.Client.Spike;

/// <summary>One Natural Earth admin-0 country: rings per polygon (outer first), id = index + 1 in the ID raster.</summary>
public sealed class Country
{
    public int Id;
    public string Name;
    public List<List<Vector2[]>> Polygons = new();
    public double BoxArea;
    public Vector2 LabelPoint;
    public int Tint;
}

/// <summary>Loads Natural Earth data and rasterises an equirectangular ID map (spike: runs at startup, no caching).</summary>
public sealed class GlobeData
{
    public const int W = 4096, H = 2048;
    public readonly List<Country> Countries = new();
    public readonly byte[] Ids = new byte[W * H]; // 0 = sea, else country id (1..255)

    public void Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (var f in doc.RootElement.GetProperty("features").EnumerateArray())
        {
            var c = new Country { Id = Countries.Count + 1, Name = f.GetProperty("properties").GetProperty("name").GetString() };
            var g = f.GetProperty("geometry");
            var coords = g.GetProperty("coordinates");
            if (g.GetProperty("type").GetString() == "Polygon") c.Polygons.Add(ReadPolygon(coords));
            else foreach (var p in coords.EnumerateArray()) c.Polygons.Add(ReadPolygon(p));
            Countries.Add(c);
        }
        if (Countries.Count > 255) throw new InvalidOperationException("ID raster is 8-bit");
    }

    static List<Vector2[]> ReadPolygon(JsonElement rings)
    {
        var res = new List<Vector2[]>();
        foreach (var r in rings.EnumerateArray())
        {
            var pts = new List<Vector2>();
            foreach (var p in r.EnumerateArray()) pts.Add(new Vector2((float)p[0].GetDouble(), (float)p[1].GetDouble()));
            res.Add(pts.ToArray());
        }
        return res;
    }

    public void Rasterise()
    {
        foreach (var c in Countries)
            foreach (var poly in c.Polygons) FillPolygon(poly, (byte)c.Id);
    }

    // Even-odd scanline fill over all rings of one polygon (holes included), pixel centres.
    void FillPolygon(List<Vector2[]> rings, byte id)
    {
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var r in rings) foreach (var p in r) { minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y); }
        int y0 = Math.Clamp((int)Math.Floor((90 - maxY) / 180f * H), 0, H - 1);
        int y1 = Math.Clamp((int)Math.Ceiling((90 - minY) / 180f * H), 0, H - 1);
        var xs = new List<float>[y1 - y0 + 1];
        foreach (var r in rings)
        {
            for (int i = 0; i < r.Length; i++)
            {
                var a = r[i]; var b = r[(i + 1) % r.Length];
                if (a.Y == b.Y) continue;
                for (int y = y0; y <= y1; y++)
                {
                    float lat = 90 - (y + 0.5f) * 180f / H;
                    if ((a.Y <= lat) == (b.Y <= lat)) continue;
                    float t = (lat - a.Y) / (b.Y - a.Y);
                    (xs[y - y0] ??= new List<float>()).Add(a.X + t * (b.X - a.X));
                }
            }
        }
        for (int y = y0; y <= y1; y++)
        {
            var l = xs[y - y0];
            if (l == null) continue;
            l.Sort();
            for (int k = 0; k + 1 < l.Count; k += 2)
            {
                int xa = Math.Clamp((int)Math.Ceiling((l[k] + 180) / 360f * W - 0.5f), 0, W - 1);
                int xb = Math.Clamp((int)Math.Floor((l[k + 1] + 180) / 360f * W - 0.5f), 0, W - 1);
                for (int x = xa; x <= xb; x++) Ids[y * W + x] = id;
            }
        }
    }

    /// <summary>Greedy colouring of the adjacency graph from the ID raster, 8 tints (stand-in for the palette validator).</summary>
    public void AssignTints()
    {
        int n = Countries.Count + 1;
        var adj = new bool[n, n];
        for (int y = 0; y < H - 1; y++)
            for (int x = 0; x < W; x++)
            {
                int a = Ids[y * W + x];
                int b = Ids[y * W + (x + 1) % W], c = Ids[(y + 1) * W + x];
                if (a != 0 && b != 0 && a != b) adj[a, b] = adj[b, a] = true;
                if (a != 0 && c != 0 && a != c) adj[a, c] = adj[c, a] = true;
            }
        var order = new List<Country>(Countries);
        order.Sort((p, q) => q.BoxArea.CompareTo(p.BoxArea));
        foreach (var c in Countries) c.Tint = -1;
        foreach (var c in order)
        {
            var used = new bool[8];
            for (int o = 1; o < n; o++) if (adj[c.Id, o] && Countries[o - 1].Tint >= 0) used[Countries[o - 1].Tint] = true;
            int t = 0; while (t < 7 && used[t]) t++;
            c.Tint = t;
        }
    }

    public void ComputeBoxAreas()
    {
        foreach (var c in Countries)
            foreach (var poly in c.Polygons)
            {
                var r = poly[0]; float x0 = 1e9f, x1 = -1e9f, y0 = 1e9f, y1 = -1e9f;
                foreach (var p in r) { x0 = Math.Min(x0, p.X); x1 = Math.Max(x1, p.X); y0 = Math.Min(y0, p.Y); y1 = Math.Max(y1, p.Y); }
                c.BoxArea = Math.Max(c.BoxArea, (x1 - x0) * (y1 - y0));
            }
    }

    /// <summary>Interior point away from the border: grid candidates scored by min distance to ring vertices (approximates the pole of inaccessibility).</summary>
    public void ComputeLabelPoint(Country c)
    {
        List<Vector2[]> best = null; double bestArea = -1;
        foreach (var poly in c.Polygons)
        {
            double a = 0; var r = poly[0];
            for (int i = 0; i < r.Length; i++) { var p = r[i]; var q = r[(i + 1) % r.Length]; a += p.X * q.Y - q.X * p.Y; }
            if (Math.Abs(a) > bestArea) { bestArea = Math.Abs(a); best = poly; }
        }
        float x0 = 1e9f, x1 = -1e9f, y0 = 1e9f, y1 = -1e9f;
        foreach (var p in best[0]) { x0 = Math.Min(x0, p.X); x1 = Math.Max(x1, p.X); y0 = Math.Min(y0, p.Y); y1 = Math.Max(y1, p.Y); }
        float step = Math.Max(0.5f, Math.Max(x1 - x0, y1 - y0) / 40f);
        float bestD = -1; c.LabelPoint = new Vector2((x0 + x1) / 2, (y0 + y1) / 2);
        var verts = new List<Vector2>();
        foreach (var r in best) verts.AddRange(r);
        for (float y = y0; y <= y1; y += step)
            for (float x = x0; x <= x1; x += step)
            {
                if (IdAt(x, y) != c.Id) continue;
                float d = float.MaxValue;
                foreach (var v in verts) d = Math.Min(d, (v.X - x) * (v.X - x) + (v.Y - y) * (v.Y - y));
                if (d > bestD) { bestD = d; c.LabelPoint = new Vector2(x, y); }
            }
    }

    public int IdAt(float lon, float lat)
    {
        int x = Math.Clamp((int)((lon + 180) / 360f * W), 0, W - 1);
        int y = Math.Clamp((int)((90 - lat) / 180f * H), 0, H - 1);
        return Ids[y * W + x];
    }

    /// <summary>Unit-sphere point for lon/lat in degrees (lon 0 at +Z, east towards +X).</summary>
    public static Vector3 ToSphere(float lonDeg, float latDeg)
    {
        float lo = Mathf.DegToRad(lonDeg), la = Mathf.DegToRad(latDeg);
        return new Vector3(Mathf.Cos(la) * Mathf.Sin(lo), Mathf.Sin(la), Mathf.Cos(la) * Mathf.Cos(lo));
    }

    public static Vector2 ToLonLat(Vector3 p) => new(Mathf.RadToDeg(Mathf.Atan2(p.X, p.Z)), Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(p.Y, -1f, 1f))));
}
