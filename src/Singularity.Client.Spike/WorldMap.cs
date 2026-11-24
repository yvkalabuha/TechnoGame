using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Singularity.Client.Spike;

/// <summary>One row of scenarios/1945/owners.yaml. An empty Owner means "no owner" (unclaimed).</summary>
public sealed record RegionRow(string Region, string Owner, string Controller, string Status);

/// <summary>A polity of polities.yaml; Members is non-empty for a joint administration (the first member gives the tone).</summary>
public sealed record PolityRow(string Id, string Name, string[] Members);

/// <summary>
/// The world map of the client (ADR-0011 section 5): region geometry from geo/arcs.bin, regions, adjacency and the 1945 owners from the mod tables.
/// Godot-free so that the tests read the very same data. A binding mismatch or a missing arcs.bin is a loud error that names the fix.
/// </summary>
public sealed class WorldMap
{
    public const int W = 4096, H = 2048;
    public const string FixHint = "run: python3 tools/map/ensure_geo.py (builds geo/arcs.bin from the map cache, see tools/map/README.md)";

    public string[] RegionIds = Array.Empty<string>();          // regions.yaml order; region number = index + 1, 0 = sea
    public RegionRow[] Rows = Array.Empty<RegionRow>();         // per region
    public PolityRow[] Polities = Array.Empty<PolityRow>();
    public string[] Owners = Array.Empty<string>();             // sorted codes of the owners; owner number = index + 1, 0 = none
    public int[] RegionOwner = Array.Empty<int>();              // region index -> owner number
    public (int A, int B)[] RegionPairs = Array.Empty<(int, int)>();   // adjacent regions, A < B, sorted
    readonly List<List<(float Lon, float Lat)[]>> _rings = new();     // per province: rings (outer first)
    readonly List<int> _provinceRegion = new();

    static readonly Regex IdRx = new(@"\{id: ([^,}\s]+)", RegexOptions.Compiled);
    static readonly Regex RowRx = new(@"\{region: ([^,]+), owner: ([^,]*), controller: ([^,]*), status: ([^,}]*)", RegexOptions.Compiled);
    static readonly Regex PolityRx = new(@"\{id: ([A-Za-z_0-9]+), name: ""([^""]*)""(.*)\}", RegexOptions.Compiled);

    public static WorldMap Load(string repoRoot)
    {
        var map = new WorldMap();
        string dir = Path.Combine(repoRoot, "mods", "core", "map"), scen = Path.Combine(repoRoot, "mods", "core", "scenarios", "1945");
        string arcsPath = Path.Combine(dir, "geo", "arcs.bin");
        if (!File.Exists(arcsPath)) throw new FileNotFoundException("geo/arcs.bin is missing (it is generated, not stored in git): " + FixHint, arcsPath);

        var tableBytes = new List<byte>();
        foreach (var f in new[] { "provinces.yaml", "adjacency.yaml", "regions.yaml", "territories.yaml" }) tableBytes.AddRange(File.ReadAllBytes(Path.Combine(dir, f)));
        string binding = Convert.ToHexString(SHA256.HashData(tableBytes.ToArray())).ToLowerInvariant();
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir, "geo", "manifest.json")));
        if (manifest.RootElement.GetProperty("data_binding").GetString() != binding)
            throw new InvalidDataException("geo/manifest.json does not match the map tables (data_binding differs): " + FixHint);
        var arcs = File.ReadAllBytes(arcsPath);
        if (Convert.ToHexString(SHA256.HashData(arcs)).ToLowerInvariant() != manifest.RootElement.GetProperty("files").GetProperty("arcs.bin").GetProperty("sha256").GetString())
            throw new InvalidDataException("geo/arcs.bin differs from the SHA-256 in geo/manifest.json: " + FixHint);
        if (Convert.ToHexString(arcs.AsSpan(8, 32)).ToLowerInvariant() != binding)
            throw new InvalidDataException("geo/arcs.bin was built from other tables (data_binding differs): " + FixHint);

        map.RegionIds = File.ReadLines(Path.Combine(dir, "regions.yaml")).Where(l => l.TrimStart().StartsWith("- {id: ")).Select(l => IdRx.Match(l).Groups[1].Value).ToArray();
        var regionIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < map.RegionIds.Length; i++) regionIndex[map.RegionIds[i]] = i;
        var provinceIds = File.ReadLines(Path.Combine(dir, "provinces.yaml")).Where(l => l.TrimStart().StartsWith("- {id: ")).Select(l => IdRx.Match(l).Groups[1].Value).ToArray();
        var provRegion = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var p in provinceIds) provRegion[p] = regionIndex[p[..p.IndexOf('.')]];

        map.ReadArcs(arcs, provinceIds, regionIndex);
        map.ReadAdjacency(Path.Combine(dir, "adjacency.yaml"), provRegion);
        map.ReadScenario(scen, regionIndex);
        return map;
    }

    void ReadArcs(byte[] data, string[] provinceIds, Dictionary<string, int> regionIndex)
    {
        using var r = new BinaryReader(new MemoryStream(data));
        if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "SGMP" || r.ReadUInt32() != 1) throw new InvalidDataException("geo/arcs.bin: unknown magic or format version: " + FixHint);
        r.ReadBytes(32);
        int nArcs = (int)r.ReadUInt32(), nProv = (int)r.ReadUInt32();
        r.ReadUInt32();
        if (nProv != provinceIds.Length) throw new InvalidDataException($"geo/arcs.bin has {nProv} provinces, provinces.yaml {provinceIds.Length}: " + FixHint);
        var arcs = new (float Lon, float Lat)[nArcs][];
        for (int a = 0; a < nArcs; a++)
        {
            r.ReadInt32(); r.ReadInt32();
            int n = (int)r.ReadUInt32();
            var v = new (float, float)[n];
            int lat = r.ReadInt32(), lon = r.ReadInt32();
            v[0] = (lon / 1e6f, lat / 1e6f);
            for (int i = 1; i < n; i++) { lat += r.ReadInt32(); lon += r.ReadInt32(); v[i] = (lon / 1e6f, lat / 1e6f); }
            arcs[a] = v;
        }
        for (int p = 0; p < nProv; p++)
        {
            var rings = new List<(float, float)[]>();
            int nr = (int)r.ReadUInt32();
            for (int k = 0; k < nr; k++)
            {
                int len = (int)r.ReadUInt32();
                var pts = new List<(float, float)>();
                for (int j = 0; j < len; j++)
                {
                    int x = r.ReadInt32(); var arc = arcs[Math.Abs(x) - 1];
                    int from = pts.Count == 0 ? 0 : 1;   // the first vertex of an arc is the last one of the previous arc
                    if (x > 0) for (int i = from; i < arc.Length; i++) pts.Add(arc[i]);
                    else for (int i = arc.Length - 1 - from; i >= 0; i--) pts.Add(arc[i]);
                }
                rings.Add(pts.ToArray());
            }
            _rings.Add(rings);
            _provinceRegion.Add(regionIndex[provinceIds[p][..provinceIds[p].IndexOf('.')]]);
        }
    }

    void ReadAdjacency(string path, Dictionary<string, int> provRegion)
    {
        var pairs = new SortedSet<(int, int)>();
        foreach (var l in File.ReadLines(path))
        {
            var t = l.Trim();
            if (!t.StartsWith("- [")) continue;
            var ab = t[3..t.IndexOf(']')].Split(", ");
            if (provRegion.TryGetValue(ab[0], out int a) && provRegion.TryGetValue(ab[1], out int b) && a != b) pairs.Add(a < b ? (a, b) : (b, a));
        }
        RegionPairs = pairs.ToArray();
    }

    void ReadScenario(string dir, Dictionary<string, int> regionIndex)
    {
        var rows = new RegionRow[RegionIds.Length];
        foreach (var l in File.ReadLines(Path.Combine(dir, "owners.yaml")))
        {
            var m = RowRx.Match(l);
            if (!m.Success) continue;
            string Norm(string s) { s = s.Trim(); return s == "null" ? "" : s; }
            if (!regionIndex.TryGetValue(m.Groups[1].Value.Trim(), out int i)) throw new InvalidDataException("owners.yaml names a region that is not in regions.yaml: " + m.Groups[1].Value);
            rows[i] = new RegionRow(RegionIds[i], Norm(m.Groups[2].Value), Norm(m.Groups[3].Value), m.Groups[4].Value.Trim());
        }
        for (int i = 0; i < rows.Length; i++) if (rows[i] == null) throw new InvalidDataException("owners.yaml has no row for region " + RegionIds[i]);
        Rows = rows;
        var pol = new List<PolityRow>();
        foreach (var l in File.ReadLines(Path.Combine(dir, "polities.yaml")))
        {
            var m = PolityRx.Match(l);
            if (!m.Success) continue;
            var mem = Regex.Match(m.Groups[3].Value, @"members: \[([^\]]*)\]");
            pol.Add(new PolityRow(m.Groups[1].Value, m.Groups[2].Value, mem.Success ? mem.Groups[1].Value.Split(',').Select(s => s.Trim()).ToArray() : Array.Empty<string>()));
        }
        Polities = pol.ToArray();
        Owners = rows.Select(r => r.Owner).Where(o => o != "").Distinct().OrderBy(o => o, StringComparer.Ordinal).ToArray();
        RegionOwner = rows.Select(r => r.Owner == "" ? 0 : Array.BinarySearch(Owners, r.Owner, StringComparer.Ordinal) + 1).ToArray();
    }

    public int RegionNumber(string regionId) => Array.IndexOf(RegionIds, regionId) + 1;

    /// <summary>Region number per pixel (0 = sea) of an equirectangular raster; pixel centres, even-odd over the rings of a province.</summary>
    public ushort[] RasteriseRegions()
    {
        var ids = new ushort[W * H];
        var rows = new List<float>[H];
        for (int p = 0; p < _rings.Count; p++)
        {
            ushort id = (ushort)(_provinceRegion[p] + 1);
            int lo = H, hi = -1;
            foreach (var ring in _rings[p])
                for (int i = 0; i < ring.Length; i++)
                {
                    var a = ring[i]; var b = ring[(i + 1) % ring.Length];
                    if (a.Lat == b.Lat) continue;
                    float mn = Math.Min(a.Lat, b.Lat), mx = Math.Max(a.Lat, b.Lat);
                    int y0 = Math.Max(0, (int)Math.Ceiling((90 - mx) / 180f * H - 0.5f)), y1 = Math.Min(H - 1, (int)Math.Floor((90 - mn) / 180f * H - 0.5f));
                    for (int y = y0; y <= y1; y++)
                    {
                        float lat = 90 - (y + 0.5f) * 180f / H;
                        if ((a.Lat <= lat) == (b.Lat <= lat)) continue;
                        float t = (lat - a.Lat) / (b.Lat - a.Lat);
                        (rows[y] ??= new List<float>()).Add(a.Lon + t * (b.Lon - a.Lon));
                    }
                    lo = Math.Min(lo, y0); hi = Math.Max(hi, y1);
                }
            for (int y = lo; y <= hi; y++)
            {
                var l = rows[y];
                if (l == null) continue;
                l.Sort();
                for (int k = 0; k + 1 < l.Count; k += 2)
                {
                    int xa = Math.Clamp((int)Math.Ceiling((l[k] + 180) / 360f * W - 0.5f), 0, W - 1);
                    int xb = Math.Clamp((int)Math.Floor((l[k + 1] + 180) / 360f * W - 0.5f), 0, W - 1);
                    for (int x = xa; x <= xb; x++) ids[y * W + x] = id;
                }
                l.Clear();
            }
        }
        return ids;
    }

    /// <summary>Owner number per pixel (0 = sea or no owner), for picking.</summary>
    public byte[] OwnerRaster(ushort[] regions)
    {
        var o = new byte[regions.Length];
        for (int i = 0; i < o.Length; i++) if (regions[i] != 0) o[i] = (byte)RegionOwner[regions[i] - 1];
        return o;
    }

    /// <summary>
    /// Label anchor per owner (index = owner number, [0] unused): the deepest point of the owner's home regions (status core) found with a chamfer
    /// distance on a raster downsampled 4x; an owner without home regions uses all of them. One anchor lies in the fattest connected area.
    /// </summary>
    public (float Lon, float Lat)[] LabelAnchors(ushort[] regions)
    {
        var home = new bool[Owners.Length + 1];
        for (int i = 0; i < Rows.Length; i++) if (Rows[i].Status == "core" && RegionOwner[i] != 0) home[RegionOwner[i]] = true;
        return Anchors(regions, Owners.Length, r => { int o = RegionOwner[r]; return o != 0 && (!home[o] || Rows[r].Status == "core") ? o : 0; });
    }

    /// <summary>Colonial areas that get a label of their own (1945 names, locale polity.C_KEY): all dependent regions of the owner whose id is one of the prefixes.</summary>
    public static readonly (string Key, string Owner, string[] Prefixes)[] ColonyGroups =
    {
        ("BRITISH_INDIA", "GBR", new[] { "india", "pakistan", "bangladesh" }),
        ("BURMA", "GBR", new[] { "myanmar" }),
        ("BRITISH_MALAYA", "GBR", new[] { "malaysia", "singapore" }),
        ("BRITISH_EAST_AFRICA", "GBR", new[] { "kenya", "uganda", "tanzania" }),
        ("BRITISH_WEST_AFRICA", "GBR", new[] { "nigeria", "ghana", "sierra_leone", "gambia" }),
        ("FRENCH_WEST_AFRICA", "FRA", new[] { "mauritania", "mali", "niger", "senegal", "burkina_faso", "ivory_coast", "guinea", "benin" }),
        ("FRENCH_EQUATORIAL_AFRICA", "FRA", new[] { "chad", "gabon", "central_african_republic", "republic_of_the_congo" }),
        ("FRENCH_ALGERIA", "FRA", new[] { "algeria" }),
        ("FRENCH_MOROCCO", "FRA", new[] { "morocco" }),
        ("FRENCH_INDOCHINA", "FRA", new[] { "vietnam", "laos", "cambodia" }),
        ("FRENCH_MADAGASCAR", "FRA", new[] { "madagascar" }),
        ("BELGIAN_CONGO", "BEL", new[] { "democratic_republic_of_the_congo" }),
        ("DUTCH_EAST_INDIES", "NLD", new[] { "indonesia" }),
        ("PORTUGUESE_ANGOLA", "PRT", new[] { "angola" }),
        ("PORTUGUESE_MOZAMBIQUE", "PRT", new[] { "mozambique" }),
        ("ITALIAN_LIBYA", "ITA", new[] { "libya" }),
        ("PHILIPPINES", "USA", new[] { "philippines" }),
        ("GREENLAND", "DNK", new[] { "greenland" }),
        ("SOUTH_WEST_AFRICA", "ZAF", new[] { "namibia" }),
    };

    /// <summary>Group number (1-based, index into ColonyGroups) per region, 0 = none.</summary>
    public int[] ColonyGroupOfRegion()
    {
        var g = new int[Rows.Length];
        for (int i = 0; i < Rows.Length; i++)
            for (int k = 0; k < ColonyGroups.Length; k++)
            {
                var (_, owner, prefixes) = ColonyGroups[k];
                if (Rows[i].Owner != owner || !(Rows[i].Status is "colony" or "mandate" or "protectorate" or "leased")) continue;
                if (prefixes.Any(p => RegionIds[i] == p || RegionIds[i].StartsWith(p + "_", StringComparison.Ordinal))) { g[i] = k + 1; break; }
            }
        return g;
    }

    public (float Lon, float Lat)[] ColonyAnchors(ushort[] regions)
    {
        var g = ColonyGroupOfRegion();
        return Anchors(regions, ColonyGroups.Length, r => g[r]);
    }

    (float Lon, float Lat)[] Anchors(ushort[] regions, int classes, Func<int, int> classOf)
    {
        const int S = 4, w = W / S, h = H / S;
        var cls = new int[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int rg = regions[(y * S + S / 2) * W + x * S + S / 2];
                if (rg != 0) cls[y * w + x] = classOf(rg - 1);
            }
        var d = new int[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int c = cls[y * w + x]; bool edge = x == 0 || y == 0 || x == w - 1 || y == h - 1;
                if (!edge) edge = cls[y * w + x - 1] != c || cls[y * w + x + 1] != c || cls[(y - 1) * w + x] != c || cls[(y + 1) * w + x] != c;
                d[y * w + x] = edge ? 0 : 1 << 20;
            }
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                int i = y * w + x;
                d[i] = Math.Min(d[i], Math.Min(Math.Min(d[i - 1] + 3, d[i - w] + 3), Math.Min(d[i - w - 1] + 4, d[i - w + 1] + 4)));
            }
        for (int y = h - 2; y >= 1; y--)
            for (int x = w - 2; x >= 1; x--)
            {
                int i = y * w + x;
                d[i] = Math.Min(d[i], Math.Min(Math.Min(d[i + 1] + 3, d[i + w] + 3), Math.Min(d[i + w - 1] + 4, d[i + w + 1] + 4)));
            }
        var best = new int[classes + 1]; var at = new int[classes + 1];
        for (int i = 0; i < w * h; i++) { int o = cls[i]; if (o != 0 && d[i] > best[o]) { best[o] = d[i]; at[o] = i; } }
        var res = new (float, float)[classes + 1];
        for (int o = 1; o <= classes; o++) res[o] = ((at[o] % w + 0.5f) * S / W * 360f - 180f, 90f - (at[o] / w + 0.5f) * S / H * 180f);
        return res;
    }
}
