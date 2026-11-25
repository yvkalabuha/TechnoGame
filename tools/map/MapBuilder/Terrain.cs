using System.Diagnostics;
using System.Text;
using NetTopologySuite.Geometries;

namespace Singularity.MapBuilder;

/// <summary>Thresholds of the terrain bake (spec/terrain.yaml). Everything is an integer; shares are permille.</summary>
internal sealed class TerrainParams
{
    public int MountainStdM { get; set; } = 100;       // cell std-dev of elevation at or above which a cell is mountains
    public int MountainElevM { get; set; } = 3000;     // mean elevation at or above which a cell is mountains
    public int HillsStdM { get; set; } = 35;           // cell std-dev at or above which a cell is hills
    public int ArcticLatDeg { get; set; } = 66;        // bare ground and grass beyond this latitude count as tundra
    public int MountainsPm { get; set; } = 450;        // class thresholds, permille
    public int HillsPm { get; set; } = 500;            // mountains + hills
    public int SwampPm { get; set; } = 250;
    public int TundraPm { get; set; } = 500;           // tundra + ice
    public int DesertPm { get; set; } = 500;
    public int ForestPm { get; set; } = 400;
    public int SteppePm { get; set; } = 400;
    public List<NoDataCover> NoData { get; set; } = new();   // cover of provinces that lie outside every WorldCover tile
}

internal sealed class NoDataCover
{
    public string Province { get; set; } = "";
    public Dictionary<string, int> Cover { get; set; } = new();   // permille by class name, sums to 1000 (empty: cover has data)
    public int? ElevM { get; set; }       // mean elevation for a province without GMTED cells
    public int? RuggedM { get; set; }
    public string Note { get; set; } = "";
    public string Source { get; set; } = "";
    public string Accessed { get; set; } = "";
}

internal sealed class Correction
{
    public string Id { get; set; } = "";
    public string Province { get; set; } = "";
    public long Km2 { get; set; }
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Note { get; set; } = "";
    public string Source { get; set; } = "";
    public string Accessed { get; set; } = "";
}

internal sealed class CorrectionFile { public List<Correction> Corrections { get; set; } = new(); }

/// <summary>Per-province sums of the zonal statistics (integer weights, see <see cref="Raster.RowWeights"/>).</summary>
internal sealed class RawStats
{
    public long ElevW, ElevSum, StdSum, MountW, HillW;
    public long CoverW;
    public readonly long[] Cover = new long[Terrain.CoverNames.Length];
    public long WetW, WetPctSum;
}

internal sealed class TerrainRow
{
    public string Id = "";
    public int Elev, Rugged;
    public int Mountains, Hills;
    public readonly int[] Cover = new int[Terrain.CoverNames.Length];
    public string Class = "plain";
    public bool NoElev;   // no GMTED cell inside the province: elevation fields are not measured
}

/// <summary>Terrain of every province from GMTED2010, ESA WorldCover and GLWD v2 (TASK-0168, #528): integer zonal statistics, then the 1945 corrections.</summary>
internal static class Terrain
{
    public static readonly string[] CoverNames = { "forest", "swamp", "steppe", "farm", "desert", "tundra", "ice", "water", "urban" };
    public const int Forest = 0, Swamp = 1, Steppe = 2, Farm = 3, Desert = 4, Tundra = 5, Ice = 6, Water = 7, Urban = 8;

    /// <summary>Row-wise visit of the cells of a province.</summary>
    private static void Visit(List<Raster.Span> spans, IGrid[] grids, Action<int, int, int[][]> onRow)
    {
        int maxW = spans.Count == 0 ? 0 : spans.Max(s => s.X1 - s.X0);
        var bufs = grids.Select(_ => new int[maxW]).ToArray();
        foreach (var s in spans)
        {
            for (int g = 0; g < grids.Length; g++) grids[g].ReadRow(s.Y, s.X0, s.X1, bufs[g]);
            onRow(s.Y, s.X1 - s.X0, bufs);
        }
    }

    private static List<Raster.Span> SpansOrPoint(Geometry shape, int perDeg)
    {
        var s = Raster.Spans(shape, perDeg);
        if (s.Count == 0) { var ip = shape.InteriorPoint; s.Add(Raster.CellAt(ip.X, ip.Y, perDeg)); }
        return s;
    }

    /// <summary>Elevation, ruggedness and relief shares from the GMTED mean and std-dev grids.</summary>
    public static void Relief(Geometry shape, IGrid mean, IGrid std, long[] rowW, TerrainParams p, RawStats st)
    {
        Visit(SpansOrPoint(shape, mean.PerDeg), new[] { mean, std }, (y, n, b) =>
        {
            long w = rowW[y];
            for (int i = 0; i < n; i++)
            {
                int m = b[0][i];
                if (m == Raster.NoData) continue;
                int sd = b[1][i] == Raster.NoData ? 0 : b[1][i];
                st.ElevW += w; st.ElevSum += m * w; st.StdSum += sd * w;
                if (sd >= p.MountainStdM || m >= p.MountainElevM) st.MountW += w;
                else if (sd >= p.HillsStdM) st.HillW += w;
            }
        });
    }

    /// <summary>Wetland area from GLWD v2: a cell counts where one vegetated wetland class (8..32) covers at least half of it (the main_class_50pct layer),
    /// weighted by the area_pct of the cell, so a 51 % wet cell adds about 51 %. area_pct also holds open water and rice, so a cell is an upper bound;
    /// mixed cells without a class reaching half are left out, which under-counts a little.</summary>
    public static void Wetland(Geometry shape, IGrid pct, IGrid main50, long[] rowW, RawStats st)
    {
        Visit(SpansOrPoint(shape, pct.PerDeg), new[] { pct, main50 }, (y, n, b) =>
        {
            long w = rowW[y];
            for (int i = 0; i < n; i++)
            {
                if (b[0][i] == Raster.NoData) continue;
                st.WetW += w;
                if (b[1][i] is >= 8 and <= 32) st.WetPctSum += Math.Clamp(b[0][i], 0, 100) * w;
            }
        });
    }

    /// <summary>Land cover shares from ESA WorldCover classes.</summary>
    public static void CoverShares(Geometry shape, IGrid cover, long[] rowW, TerrainParams p, RawStats st)
    {
        Visit(SpansOrPoint(shape, cover.PerDeg), new[] { cover }, (y, n, b) =>
        {
            long w = rowW[y];
            bool arctic = Math.Abs(Raster.RowLat(y, cover.PerDeg)) >= p.ArcticLatDeg;
            for (int i = 0; i < n; i++)
            {
                int c = b[0][i];
                if (c == Raster.NoData) continue;
                int k = c switch
                {
                    10 => Forest,
                    95 or 90 => Swamp,
                    20 or 30 => arctic ? Tundra : Steppe,
                    40 => Farm,
                    50 => Urban,
                    60 => arctic ? Tundra : Desert,
                    70 => Ice,
                    80 => Water,
                    100 => Tundra,
                    _ => -1,
                };
                if (k < 0) continue;
                st.CoverW += w; st.Cover[k] += w;
            }
        });
    }

    public static long Pm(long part, long whole) => whole <= 0 ? 0 : (part * 1000 + whole / 2) / whole;

    private static long DivRound(long a, long b) => b <= 0 ? 0 : a >= 0 ? (a + b / 2) / b : -((-a + b / 2) / b);

    /// <summary>Largest remainder rounding so that the shares sum to exactly 1000.</summary>
    private static int[] Shares(long[] parts, long whole)
    {
        var res = new int[parts.Length];
        if (whole <= 0) return res;
        var rem = new long[parts.Length];
        int sum = 0;
        for (int i = 0; i < parts.Length; i++) { res[i] = (int)(parts[i] * 1000 / whole); rem[i] = parts[i] * 1000 % whole; sum += res[i]; }
        while (sum < 1000)
        {
            int best = 0;
            for (int i = 1; i < parts.Length; i++) if (rem[i] > rem[best]) best = i;
            res[best]++; rem[best] = -1; sum++;
        }
        return res;
    }

    public static TerrainRow Finish(string id, RawStats st)
    {
        var row = new TerrainRow { Id = id };
        row.NoElev = st.ElevW == 0;
        row.Elev = (int)DivRound(st.ElevSum, st.ElevW);
        row.Rugged = (int)DivRound(st.StdSum, st.ElevW);
        row.Mountains = (int)Pm(st.MountW, st.ElevW);
        row.Hills = (int)Pm(st.HillW, st.ElevW);
        var cov = Shares(st.Cover, st.CoverW);
        Array.Copy(cov, row.Cover, cov.Length);
        // GLWD sees wetlands under forest and grass that WorldCover shows as trees or grass: the excess comes out of those classes.
        long wet = st.WetW == 0 ? 0 : Pm(st.WetPctSum, st.WetW * 100);
        int excess = (int)Math.Max(0, wet - row.Cover[Swamp]);
        if (st.CoverW > 0 && excess > 0)
        {
            row.Cover[Swamp] += excess;
            foreach (int k in new[] { Forest, Steppe, Farm, Tundra, Desert })
            {
                int take = Math.Min(excess, row.Cover[k]);
                row.Cover[k] -= take; excess -= take;
            }
            row.Cover[Swamp] -= excess;
        }
        return row;
    }

    public static string Classify(TerrainRow r, TerrainParams p)
    {
        if (r.Mountains >= p.MountainsPm) return "mountains";
        if (r.Mountains + r.Hills >= p.HillsPm) return "hills";
        if (r.Cover[Swamp] >= p.SwampPm) return "swamp";
        if (r.Cover[Tundra] + r.Cover[Ice] >= p.TundraPm) return "tundra";
        if (r.Cover[Desert] >= p.DesertPm) return "desert";
        if (r.Cover[Forest] >= p.ForestPm) return "forest";
        if (r.Cover[Steppe] >= p.SteppePm) return "steppe";
        return "plain";
    }

    /// <summary>Provinces without any WorldCover cell take their cover from the documented list in spec/terrain.yaml; a province without data and without an entry fails the bake.</summary>
    public static void ApplyNoData(IEnumerable<TerrainRow> rows, IReadOnlyList<NoDataCover> list)
    {
        foreach (var e in list)
            if (e.Source.Length == 0 || e.Accessed.Length == 0 || (e.Cover.Count > 0 && e.Cover.Values.Sum() != 1000))
                throw new InvalidDataException($"no_data {e.Province}: source, access date and cover shares summing to 1000 are required");
        foreach (var row in rows.Where(r => r.Cover.Sum() == 0 || r.NoElev))
        {
            var e = list.FirstOrDefault(x => x.Province == row.Id) ?? throw new InvalidDataException($"{row.Id}: no land cover or elevation data and no no_data entry in spec/terrain.yaml");
            if (row.Cover.Sum() == 0)
            {
                if (e.Cover.Count == 0) throw new InvalidDataException($"no_data {row.Id}: the province has no land cover data, the entry needs cover");
                foreach (var (name, pm) in e.Cover.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    int k = Array.IndexOf(CoverNames, name);
                    if (k < 0) throw new InvalidDataException($"no_data {e.Province}: unknown class '{name}'");
                    row.Cover[k] = pm;
                }
            }
            if (row.NoElev)
            {
                if (e.ElevM is null) throw new InvalidDataException($"no_data {row.Id}: the province has no elevation data, the entry needs elev_m");
                row.Elev = e.ElevM.Value; row.Rugged = e.RuggedM ?? 0;
                row.NoElev = false;
            }
        }
    }

    /// <summary>Applies the 1945 corrections: moves km2 of a province from one cover class to another. Returns warnings (a correction that did not fit).</summary>
    public static List<string> ApplyCorrections(IEnumerable<TerrainRow> rows, IReadOnlyDictionary<string, long> areaKm2, IEnumerable<Correction> list)
    {
        var byId = rows.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var warn = new List<string>();
        foreach (var c in list)
        {
            int from = Array.IndexOf(CoverNames, c.From), to = Array.IndexOf(CoverNames, c.To);
            if (from < 0 || to < 0) throw new InvalidDataException($"correction {c.Id}: unknown class '{c.From}' or '{c.To}'");
            if (!byId.TryGetValue(c.Province, out var row)) throw new InvalidDataException($"correction {c.Id}: province {c.Province} is not on the map");
            if (c.Source.Length == 0 || c.Accessed.Length == 0) throw new InvalidDataException($"correction {c.Id}: source and access date are required");
            int want = (int)Pm(c.Km2, areaKm2[c.Province]);
            int d = Math.Min(want, row.Cover[from]);
            if (d < want) warn.Add($"correction {c.Id}: {c.Province} has only {row.Cover[from]} permille of {c.From}, {want} wanted");
            row.Cover[from] -= d; row.Cover[to] += d;
        }
        return warn;
    }

    public static byte[] Write(IEnumerable<TerrainRow> rows)
    {
        var sb = new StringBuilder("# Generated by tools/map 'terrain' (TASK-0168). Do not edit: rebuild with 'MapBuilder terrain'.\n");
        sb.Append("# elev_m: mean elevation, rugged_m: mean std-dev of elevation inside 1 km cells (GMTED2010). Shares are permille:\n");
        sb.Append("# mountains and hills of land with elevation data; forest..urban of the province area (WorldCover, GLWD wetlands, 1945 corrections) and sum to 1000.\n");
        sb.Append("terrain:\n");
        foreach (var r in rows.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            sb.Append(FormattableString.Invariant($"  - {{id: {r.Id}, class: {r.Class}, elev_m: {r.Elev}, rugged_m: {r.Rugged}, mountains: {r.Mountains}, hills: {r.Hills}"));
            for (int k = 0; k < CoverNames.Length; k++) sb.Append(FormattableString.Invariant($", {CoverNames[k]}: {r.Cover[k]}"));
            sb.Append("}\n");
        }
        return new UTF8Encoding(false).GetBytes(sb.ToString());
    }

    public static int Run(BuildOptions o)
    {
        var sw = Stopwatch.StartNew();
        var lk = Sources.Load(Path.Combine(o.Root, "sources.lock.yaml"));
        SourceEntry Src(string id) => lk.Sources.FirstOrDefault(s => s.Id == id) ?? throw new InvalidDataException($"source {id} is not in the lock");
        foreach (var id in new[] { "gmted_mea30", "gmted_std30", "glwd_v2", "worldcover_grid" })
        {
            var e = Src(id);
            TerrainSources.Verify(e, e.Files.Select(f => (f.Name, Sha: TerrainSources.FileSha(TerrainSources.FilePath(o.Cache, e, f)))).ToList());
        }
        var wc = Src("worldcover_2021");
        var wcDir = TerrainSources.Dir(o.Cache, wc.Id);
        var wcTiles = TerrainSources.Tiles(lk, o.Cache, wc.Grid!);
        var wcShas = TerrainSources.CogShas(wcDir, wcTiles, wc.Level);
        var agg = TerrainSources.AggregateSha(wcShas);
        if (agg != wc.Sha256) throw new InvalidDataException($"SHA-256 mismatch for {wc.Id}: lock {wc.Sha256}, got {agg}");
        Console.WriteLine($"sources verified in {sw.Elapsed.TotalSeconds:F0} s");

        var pPath = Path.Combine(o.Root, "spec", "terrain.yaml");
        var prm = File.Exists(pPath) ? Sources.Yaml.Deserialize<TerrainParams>(File.ReadAllText(pPath)) : new TerrainParams();
        var corrections = Sources.Yaml.Deserialize<CorrectionFile>(File.ReadAllText(Path.Combine(o.Root, "spec", "terrain_1945.yaml"))).Corrections;

        var r = Builder.Build(o);
        if (r.Errors.Count > 0) { foreach (var e in r.Errors) Console.Error.WriteLine("ERROR: " + e); return 1; }
        Console.WriteLine($"map built in {sw.Elapsed.TotalSeconds:F0} s: {r.Pieces.Count} provinces");

        IGrid mean, std, pct, main;
        var meaE = Src("gmted_mea30"); var stdE = Src("gmted_std30");
        using var meanM = new TiffMosaic(meaE.Files.Select(f => TerrainSources.FilePath(o.Cache, meaE, f)), 120);
        using var stdM = new TiffMosaic(stdE.Files.Select(f => TerrainSources.FilePath(o.Cache, stdE, f)), 120);
        var gl = TerrainSources.ExtractZipMembers(o.Cache, Src("glwd_v2"), new[] { "GLWD_v2_0_area_pct.tif", "GLWD_v2_0_main_class_50pct.tif" });
        using var pctM = new TiffMosaic(new[] { gl[0] }, 240);
        using var mainM = new TiffMosaic(new[] { gl[1] }, 240);
        mean = meanM; std = stdM; pct = pctM; main = mainM;
        var cover = new CogGrid(wcDir, wc.Level, wcShas);
        long[] w120 = Raster.RowWeights(120), w240 = Raster.RowWeights(240), wCover = Raster.RowWeights(cover.PerDeg);

        var rows = new List<TerrainRow>();
        foreach (var piece in r.Pieces)
        {
            var st = new RawStats();
            Relief(piece.Shape, mean, std, w120, prm, st);
            Wetland(piece.Shape, pct, main, w240, st);
            CoverShares(piece.Shape, cover, wCover, prm, st);
            rows.Add(Finish(piece.Id, st));
            if (rows.Count % 250 == 0) Console.WriteLine($"{rows.Count}/{r.Pieces.Count} provinces, {sw.Elapsed.TotalSeconds:F0} s");
        }
        ApplyNoData(rows, prm.NoData);
        foreach (var w in ApplyCorrections(rows, r.Pieces.ToDictionary(p => p.Id, p => p.AreaKm2, StringComparer.Ordinal), corrections)) Console.WriteLine("warning: " + w);
        foreach (var row in rows)
        {
            row.Class = Classify(row, prm);
            if (row.Cover.Sum() != 1000) throw new InvalidDataException($"{row.Id}: cover shares sum to {row.Cover.Sum()}, not 1000");
        }
        File.WriteAllBytes(Path.Combine(o.OutDir, "terrain.yaml"), Write(rows));
        foreach (var g in rows.GroupBy(x => x.Class).OrderBy(g => g.Key, StringComparer.Ordinal)) Console.WriteLine($"class {g.Key}: {g.Count()}");
        Console.WriteLine($"terrain: {rows.Count} provinces in {sw.Elapsed.TotalSeconds:F0} s");
        return 0;
    }
}
