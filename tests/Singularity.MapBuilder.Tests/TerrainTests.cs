using System.IO.Compression;
using System.Text;
using NetTopologySuite.Geometries;
using Singularity.MapBuilder;

namespace Singularity.MapBuilder.Tests;

/// <summary>Terrain bake and rivers (TASK-0168): TIFF reader, scanline rasteriser, zonal statistics, corrections and the committed tables.</summary>
public class TerrainTests
{
    private static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Singularity.sln"))) return d.FullName;
        throw new InvalidOperationException("repo root not found");
    }

    private static Geometry Rect(double x0, double y0, double x1, double y1) =>
        Sph.Gf.CreatePolygon(new[] { new Coordinate(x0, y0), new Coordinate(x1, y0), new Coordinate(x1, y1), new Coordinate(x0, y1), new Coordinate(x0, y0) });

    /// <summary>Little-endian classic TIFF writer for tests: single band, 8 or 16 bit, tiled or striped, none or deflate, predictor 1 or 2.</summary>
    private static byte[] MakeTiff(int w, int h, int[] px, int bits, bool tiled, int blk, bool deflate, bool predictor, double originLon = -180, double originLat = 90, double pixel = 1.0 / 120)
    {
        var blocks = new List<byte[]>();
        int across = tiled ? (w + blk - 1) / blk : 1, down = tiled ? (h + blk - 1) / blk : (h + blk - 1) / blk;
        int bw = tiled ? blk : w;
        for (int by = 0; by < down; by++)
            for (int bx = 0; bx < across; bx++)
            {
                int rows = tiled ? blk : Math.Min(blk, h - by * blk);
                var vals = new int[bw * rows];
                for (int r = 0; r < rows; r++)
                    for (int x = 0; x < bw; x++)
                    {
                        int gx = bx * bw + x, gy = by * (tiled ? blk : blk) + r;
                        vals[r * bw + x] = gx < w && gy < h ? px[gy * w + gx] : 0;
                    }
                if (predictor)
                    for (int r = 0; r < rows; r++)
                        for (int x = bw - 1; x >= 1; x--) vals[r * bw + x] -= vals[r * bw + x - 1];
                var raw = new byte[vals.Length * bits / 8];
                for (int i = 0; i < vals.Length; i++)
                {
                    if (bits == 8) raw[i] = (byte)vals[i];
                    else { raw[2 * i] = (byte)vals[i]; raw[2 * i + 1] = (byte)(vals[i] >> 8); }
                }
                if (deflate)
                {
                    var ms = new MemoryStream();
                    using (var z = new ZLibStream(ms, CompressionLevel.Fastest, true)) z.Write(raw);
                    raw = ms.ToArray();
                }
                blocks.Add(raw);
            }
        var ms2 = new MemoryStream();
        var bw2 = new BinaryWriter(ms2);
        bw2.Write((byte)'I'); bw2.Write((byte)'I'); bw2.Write((ushort)42); bw2.Write(8u);
        var entries = new List<(ushort Tag, ushort Type, uint Count, object Val)>
        {
            (256, 4, 1, (uint)w), (257, 4, 1, (uint)h), (258, 3, 1, (uint)bits), (259, 3, 1, (uint)(deflate ? 8 : 1)),
            (277, 3, 1, 1u), (317, 3, 1, (uint)(predictor ? 2 : 1)), (339, 3, 1, 1u),
        };
        if (tiled) { entries.Add((322, 4, 1, (uint)blk)); entries.Add((323, 4, 1, (uint)blk)); }
        else entries.Add((278, 4, 1, (uint)blk));
        entries.Add((tiled ? (ushort)324 : (ushort)273, 4, (uint)blocks.Count, "offsets"));
        entries.Add((tiled ? (ushort)325 : (ushort)279, 4, (uint)blocks.Count, "counts"));
        entries.Add((33550, 12, 3, new[] { pixel, pixel, 0.0 }));
        entries.Add((33922, 12, 6, new[] { 0.0, 0.0, 0.0, originLon, originLat, 0.0 }));
        entries.Sort((a, b) => a.Tag.CompareTo(b.Tag));
        long ifdSize = 2 + entries.Count * 12 + 4;
        long extra = 8 + ifdSize;
        long offsetsAt = extra; extra += blocks.Count * 4;
        long countsAt = extra; extra += blocks.Count * 4;
        long scaleAt = extra; extra += 24;
        long tieAt = extra; extra += 48;
        long dataAt = extra;
        bw2.Write((ushort)entries.Count);
        foreach (var (tag, type, count, val) in entries)
        {
            bw2.Write(tag); bw2.Write(type); bw2.Write(count);
            if (val is string s) bw2.Write((uint)(s == "offsets" ? (blocks.Count == 1 ? dataAt : offsetsAt) : (blocks.Count == 1 ? blocks[0].Length : countsAt)));
            else if (val is uint u) bw2.Write(u);
            else if (val is double[] d) bw2.Write((uint)(d.Length == 3 ? scaleAt : tieAt));
        }
        bw2.Write(0u);
        foreach (var b in blocks) bw2.Write(0u);   // placeholders replaced below
        if (blocks.Count == 1) { ms2.Position = offsetsAt; }
        ms2.Position = offsetsAt;
        long pos = dataAt;
        foreach (var b in blocks) { bw2.Write((uint)pos); pos += b.Length; }
        foreach (var b in blocks) bw2.Write((uint)b.Length);
        foreach (var d in new[] { pixel, pixel, 0.0 }) bw2.Write(d);
        foreach (var d in new[] { 0.0, 0.0, 0.0, originLon, originLat, 0.0 }) bw2.Write(d);
        foreach (var b in blocks) bw2.Write(b);
        return ms2.ToArray();
    }

    private static TiffImage Open(byte[] file, int ifd = 0) => TiffImage.Open((o, n) => file.AsSpan((int)o, n).ToArray(), ifd);

    [Theory]
    [InlineData(8, true, 16, false, false)]
    [InlineData(8, true, 16, true, true)]
    [InlineData(16, true, 16, true, true)]
    [InlineData(16, false, 7, true, true)]
    [InlineData(8, false, 5, false, false)]
    public void Tiff_reader_decodes_tiled_striped_deflate_and_predictor(int bits, bool tiled, int blk, bool deflate, bool predictor)
    {
        int w = 40, h = 37;
        var px = new int[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = bits == 8 ? (i * 7 + i / w) % 251 : (i * 131 + 5) % 30000;
        var img = Open(MakeTiff(w, h, px, bits, tiled, blk, deflate, predictor, 10, 50, 0.5));
        Assert.Equal((w, h), (img.Width, img.Height));
        Assert.Equal(10, img.OriginLon, 9);
        Assert.Equal(50, img.OriginLat, 9);
        Assert.Equal(0.5, img.PixelX, 9);
        for (int b = 0; b < img.Offsets.Length; b++)
        {
            var blkPx = img.ReadBlock(b);
            int bx = tiled ? b % img.BlocksAcross * img.BlockW : 0, by = (tiled ? b / img.BlocksAcross : b) * img.BlockH;
            for (int i = 0; i < blkPx.Length; i++)
            {
                int x = bx + i % img.BlockW, y = by + i / img.BlockW;
                if (x < w && y < h) Assert.Equal(px[y * w + x], blkPx[i]);
            }
        }
    }

    [Fact]
    public void Tiff_reader_rejects_files_that_are_not_tiff()
    {
        Assert.Throws<InvalidDataException>(() => Open(new byte[64]));
    }

    private static byte[] LzwEncode(byte[] data)
    {
        var o = new List<byte>();
        int buf = 0, nb = 0, bits = 9, next = 258;
        void Put(int code) { buf = buf << bits | code; nb += bits; while (nb >= 8) { o.Add((byte)(buf >> (nb - 8))); nb -= 8; buf &= (1 << nb) - 1; } }
        var dict = new Dictionary<(int, byte), int>();
        Put(256);
        int w = -1;
        foreach (var b in data)
        {
            if (w < 0) { w = b; continue; }
            if (dict.TryGetValue((w, b), out int c)) { w = c; continue; }
            Put(w);
            dict[(w, b)] = next++;
            w = b;
            bits = next >= 2048 ? 12 : next >= 1024 ? 11 : next >= 512 ? 10 : 9;
            if (next >= 4094) { Put(256); dict.Clear(); next = 258; bits = 9; }
        }
        if (w >= 0) Put(w);
        Put(257);
        if (nb > 0) o.Add((byte)(buf << (8 - nb)));
        return o.ToArray();
    }

    [Fact]
    public void Tiff_reader_decodes_lzw_and_bigtiff()
    {
        int w = 64, h = 300;
        var px = new int[w * h];
        uint s = 12345;
        for (int i = 0; i < px.Length; i++) { s = s * 1664525 + 1013904223; px[i] = (i / 40) % 3 == 0 ? (int)(s >> 24) : (i % 7); }
        var raw = px.Select(v => (byte)v).ToArray();
        var comp = LzwEncode(raw);
        Assert.True(comp.Length < raw.Length);
        // BigTIFF, little endian, one strip of all rows, LZW
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write((byte)'I'); bw.Write((byte)'I'); bw.Write((ushort)43); bw.Write((ushort)8); bw.Write((ushort)0); bw.Write(16UL);
        var tags = new (ushort Tag, ushort Type, ulong Val)[]
        {
            (256, 4, (ulong)w), (257, 3, (ulong)h), (258, 3, 8), (259, 3, 5), (273, 16, 16 + 8 + 8 * 20 + 8), (277, 3, 1), (278, 3, (ulong)h), (279, 16, (ulong)comp.Length),
        };
        bw.Write((ulong)tags.Length);
        foreach (var (tag, type, val) in tags) { bw.Write(tag); bw.Write(type); bw.Write(1UL); bw.Write(val); }
        bw.Write(0UL);
        bw.Write(comp);
        var img = Open(ms.ToArray());
        Assert.Equal((w, h, 5), (img.Width, img.Height, img.Compression));
        Assert.Equal(px, img.ReadBlock(0));
    }

    [Fact]
    public void Mosaic_places_tiles_by_their_georeference_and_marks_gaps()
    {
        // two 4x4 tiles side by side at 1 cell per degree: lon -180..-176 and -176..-172, top at 90N
        var a = Enumerable.Range(0, 16).Select(i => 100 + i).ToArray();
        var b = Enumerable.Range(0, 16).Select(i => 200 + i).ToArray();
        var dir = Path.Combine(Path.GetTempPath(), "mosaic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "a.tif"), MakeTiff(4, 4, a, 16, false, 2, true, true, -180, 90, 1));
            File.WriteAllBytes(Path.Combine(dir, "b.tif"), MakeTiff(4, 4, b, 16, true, 2, false, false, -176, 90, 1));
            using var m = new TiffMosaic(new[] { Path.Combine(dir, "a.tif"), Path.Combine(dir, "b.tif") }, 1);
            var row = new int[12];
            m.ReadRow(1, 0, 12, row);
            Assert.Equal(new[] { 104, 105, 106, 107, 204, 205, 206, 207, Raster.NoData, Raster.NoData, Raster.NoData, Raster.NoData }, row);
            m.ReadRow(10, 0, 4, row);
            Assert.All(row.Take(4), v => Assert.Equal(Raster.NoData, v));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Spans_cover_cells_with_centres_inside_the_polygon_and_respect_holes()
    {
        int n = 10;  // cells per degree
        var spans = Raster.Spans(Rect(10.0, 20.0, 12.0, 21.0), n);
        Assert.Equal(10, spans.Count);
        Assert.Equal(20 * 10 * 1, spans.Sum(s => s.X1 - s.X0));
        Assert.All(spans, s => { Assert.Equal((10 + 180) * n, s.X0); Assert.Equal((12 + 180) * n, s.X1); });
        Assert.Equal(90 * n - 21 * n, spans.Min(s => s.Y));

        var outer = new[] { new Coordinate(0, 0), new Coordinate(4, 0), new Coordinate(4, 4), new Coordinate(0, 4), new Coordinate(0, 0) };
        var hole = new[] { new Coordinate(1, 1), new Coordinate(1, 3), new Coordinate(3, 3), new Coordinate(3, 1), new Coordinate(1, 1) };
        var donut = Sph.Gf.CreatePolygon(Sph.Gf.CreateLinearRing(outer), new[] { Sph.Gf.CreateLinearRing(hole) });
        Assert.Equal((16 - 4) * n * n, Raster.Spans(donut, n).Sum(s => s.X1 - s.X0));
        // a polygon smaller than a cell has no spans: the caller falls back to the cell of its interior point
        Assert.Empty(Raster.Spans(Rect(5.01, 5.01, 5.02, 5.02), n));
    }

    [Fact]
    public void Adjacent_polygons_share_every_cell_exactly_once()
    {
        // a triangle split by a diagonal: every cell centre goes to exactly one side
        int n = 20;
        var a = Sph.Gf.CreatePolygon(new[] { new Coordinate(0, 0), new Coordinate(3.1, 0), new Coordinate(0, 2.7), new Coordinate(0, 0) });
        var b = Sph.Gf.CreatePolygon(new[] { new Coordinate(3.1, 0), new Coordinate(3.1, 2.7), new Coordinate(0, 2.7), new Coordinate(3.1, 0) });
        var whole = Rect(0, 0, 3.1, 2.7);
        long ca = Raster.Spans(a, n).Sum(s => (long)(s.X1 - s.X0)), cb = Raster.Spans(b, n).Sum(s => (long)(s.X1 - s.X0)), cw = Raster.Spans(whole, n).Sum(s => (long)(s.X1 - s.X0));
        Assert.Equal(cw, ca + cb);
    }

    private sealed class ConstGrid : IGrid
    {
        private readonly Func<int, int, int> f;
        public int PerDeg { get; }
        public ConstGrid(int perDeg, Func<int, int, int> f) { PerDeg = perDeg; this.f = f; }
        public void ReadRow(int y, int x0, int x1, int[] dst) { for (int x = x0; x < x1; x++) dst[x - x0] = f(x, y); }
    }

    [Fact]
    public void Zonal_statistics_use_integer_area_weights_and_are_repeatable()
    {
        var p = new TerrainParams();
        var shape = Rect(10, 40, 12, 42);
        // west half of the province: forest and mountains, east half: steppe and plains
        int mid = (11 + 180) * 10;
        var mean = new ConstGrid(10, (x, y) => x < mid ? 2000 : 100);
        var std = new ConstGrid(10, (x, y) => x < mid ? 150 : 5);
        var cover = new ConstGrid(10, (x, y) => x < mid ? 10 : 30);
        var pct = new ConstGrid(10, (x, y) => 0);
        var cls = new ConstGrid(10, (x, y) => 0);
        TerrainRow Bake()
        {
            var st = new RawStats();
            Terrain.Relief(shape, mean, std, Raster.RowWeights(10), p, st);
            Terrain.Wetland(shape, pct, cls, Raster.RowWeights(10), st);
            Terrain.CoverShares(shape, cover, Raster.RowWeights(10), p, st);
            return Terrain.Finish("x.y", st);
        }
        var r = Bake();
        Assert.InRange(r.Elev, 1049, 1051);
        Assert.InRange(r.Rugged, 76, 78);
        Assert.Equal(500, r.Mountains);
        Assert.Equal(0, r.Hills);
        Assert.Equal(500, r.Cover[Terrain.Forest]);
        Assert.Equal(500, r.Cover[Terrain.Steppe]);
        Assert.Equal(1000, r.Cover.Sum());
        Assert.Equal(Terrain.Write(new[] { r }), Terrain.Write(new[] { Bake() }));
        Assert.Equal("mountains", Terrain.Classify(r, p));
    }

    [Fact]
    public void Wetland_excess_over_land_cover_is_taken_from_forest_first()
    {
        var p = new TerrainParams();
        var shape = Rect(10, 40, 12, 42);
        var cover = new ConstGrid(10, (x, y) => (x + y) % 2 == 0 ? 10 : 30);   // half forest, half grass
        var pct = new ConstGrid(10, (x, y) => 60);
        // main_class_50pct: a vegetated wetland class in half of the cells; the other half has no class covering half of the cell
        var cls = new ConstGrid(10, (x, y) => (x + y) % 2 == 0 ? 16 : Raster.NoData);
        var st = new RawStats();
        Terrain.Wetland(shape, pct, cls, Raster.RowWeights(10), st);
        Terrain.CoverShares(shape, cover, Raster.RowWeights(10), p, st);
        var r = Terrain.Finish("x.y", st);
        Assert.Equal(500, r.Cover[Terrain.Swamp]);
        Assert.Equal(0, r.Cover[Terrain.Forest]);
        Assert.Equal(500, r.Cover[Terrain.Steppe]);
        Assert.Equal(1000, r.Cover.Sum());
        Assert.Equal("swamp", Terrain.Classify(r, p));
    }

    [Fact]
    public void Cog_grid_reads_only_pinned_tiles_and_checks_their_bytes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            const int level = 30;
            var raw = new byte[level * level]; Array.Fill(raw, (byte)10);
            void Put(string tile) { using var z = new System.IO.Compression.GZipStream(File.Create(CogGrid.TilePath(dir, tile, level)), System.IO.Compression.CompressionLevel.Fastest); z.Write(raw); }
            Put("N42E009"); Put("N42E012");
            string sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(raw));
            var dst = new int[10];
            // y of a row at 44.5 N; x0 at 9 E (10 cells per degree)
            int y = (int)((90 - 44.5) * 10), x0 = (9 + 180) * 10;
            new CogGrid(dir, level, new[] { ("N42E009", sha) }).ReadRow(y, x0, x0 + 10, dst);
            Assert.All(dst, v => Assert.Equal(10, v));
            // the tile N42E012 is in the cache but not in the lock: never read
            int x1 = (12 + 180) * 10;
            new CogGrid(dir, level, new[] { ("N42E009", sha) }).ReadRow(y, x1, x1 + 10, dst);
            Assert.All(dst, v => Assert.Equal(Raster.NoData, v));
            // a pinned tile whose bytes differ from the lock is a loud error
            Assert.Throws<InvalidDataException>(() => new CogGrid(dir, level, new[] { ("N42E009", new string('0', 64)) }).ReadRow(y, x0, x0 + 10, dst));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Built_up_land_is_urban_and_not_farm()
    {
        var shape = Rect(10, 40, 12, 42);
        var cover = new ConstGrid(10, (x, y) => x % 2 == 0 ? 50 : 40);   // half built-up, half cropland
        var st = new RawStats();
        Terrain.CoverShares(shape, cover, Raster.RowWeights(10), new TerrainParams(), st);
        var r = Terrain.Finish("x.y", st);
        Assert.Equal(500, r.Cover[Terrain.Urban]);
        Assert.Equal(500, r.Cover[Terrain.Farm]);
    }

    [Fact]
    public void Province_without_cover_data_needs_a_documented_entry()
    {
        var row = new TerrainRow { Id = "a.isle" };
        Assert.Throws<InvalidDataException>(() => Terrain.ApplyNoData(new[] { row }, new List<NoDataCover>()));
        var e = new NoDataCover { Province = "a.isle", Cover = new() { ["ice"] = 900, ["desert"] = 100 }, Source = "x", Accessed = "2026-11-25" };
        Terrain.ApplyNoData(new[] { row }, new[] { e });
        Assert.Equal(900, row.Cover[Terrain.Ice]);
        Assert.Equal(1000, row.Cover.Sum());
        var bad = new NoDataCover { Province = "a.isle", Cover = new() { ["ice"] = 900 }, Source = "x", Accessed = "2026-11-25" };
        Assert.Throws<InvalidDataException>(() => Terrain.ApplyNoData(new[] { row }, new[] { bad }));
    }

    [Fact]
    public void Corrections_move_area_between_classes_and_demand_a_source()
    {
        var row = new TerrainRow { Id = "a.b" };
        row.Cover[Terrain.Desert] = 800; row.Cover[Terrain.Water] = 200;
        var area = new Dictionary<string, long> { ["a.b"] = 100000 };
        var warn = Terrain.ApplyCorrections(new[] { row }, area, new[] { new Correction { Id = "aral", Province = "a.b", Km2 = 30000, From = "desert", To = "water", Source = "https://example.org", Accessed = "2026-11-25" } });
        Assert.Empty(warn);
        Assert.Equal(500, row.Cover[Terrain.Desert]);
        Assert.Equal(500, row.Cover[Terrain.Water]);
        warn = Terrain.ApplyCorrections(new[] { row }, area, new[] { new Correction { Id = "big", Province = "a.b", Km2 = 90000, From = "desert", To = "water", Source = "https://example.org", Accessed = "2026-11-25" } });
        Assert.Single(warn);
        Assert.Equal(0, row.Cover[Terrain.Desert]);
        Assert.Throws<InvalidDataException>(() => Terrain.ApplyCorrections(new[] { row }, area, new[] { new Correction { Id = "nosrc", Province = "a.b", Km2 = 1, From = "water", To = "farm" } }));
    }

    [Fact]
    public void Lock_pins_every_terrain_source_with_an_allowed_licence()
    {
        var lk = Sources.Load(Path.Combine(RepoRoot(), "tools/map/sources.lock.yaml"));
        var terrain = lk.Sources.Where(Sources.IsTerrain).ToList();
        Assert.Contains(terrain, s => s.Id == "gmted_mea30");
        Assert.Contains(terrain, s => s.Id == "gmted_std30");
        Assert.Contains(terrain, s => s.Id == "glwd_v2");
        Assert.Contains(terrain, s => s.Id == "worldcover_2021");
        Assert.Contains(lk.Sources, s => s.Id == "ne_rivers");
        foreach (var s in lk.Sources)
        {
            Assert.Matches("^[0-9a-f]{64}$", s.Sha256);
            Assert.All(s.License.Split('+'), part => Assert.True(Licenses.Allowed(part), s.Id + ": " + s.License));
            foreach (var f in s.Files) Assert.Matches("^[0-9a-f]{64}$", f.Sha256);
        }
        foreach (var s in terrain.Where(s => s.Kind == "raster"))
            Assert.Equal(s.Sha256, TerrainSources.AggregateSha(s.Files.Select(f => (f.Name, f.Sha256))));
    }

    [Fact]
    public void Committed_terrain_covers_every_province_and_shares_add_up()
    {
        var map = Path.Combine(RepoRoot(), "mods", "core", "map");
        var provinces = File.ReadAllLines(Path.Combine(map, "provinces.yaml")).Where(l => l.StartsWith("  - {id: ")).Select(l => l[9..l.IndexOf(',')]).ToList();
        var rows = File.ReadAllLines(Path.Combine(map, "terrain.yaml")).Where(l => l.StartsWith("  - {id: ")).ToList();
        Assert.Equal(provinces, rows.Select(l => l[9..l.IndexOf(',')]).ToList());
        string[] cover = Terrain.CoverNames;
        var classes = new HashSet<string> { "mountains", "hills", "swamp", "tundra", "desert", "forest", "steppe", "plain" };
        int FieldOf(string l, string name) => int.Parse(l.Split(name + ": ")[1].Split(new[] { ',', '}' })[0]);
        foreach (var l in rows)
        {
            Assert.Contains(l.Split("class: ")[1].Split(',')[0], classes);
            Assert.Equal(1000, cover.Sum(c => FieldOf(l, c)));
            Assert.InRange(FieldOf(l, "mountains") + FieldOf(l, "hills"), 0, 1000);
        }
    }

    [Fact]
    public void Committed_terrain_matches_known_places()
    {
        var map = Path.Combine(RepoRoot(), "mods", "core", "map");
        var rows = File.ReadAllLines(Path.Combine(map, "terrain.yaml")).Where(l => l.StartsWith("  - {id: ")).ToDictionary(l => l[9..l.IndexOf(',')]);
        string Class(string id) => rows[id].Split("class: ")[1].Split(',')[0];
        Assert.Equal("mountains", Class("austria_british_zone.tirol_british_zone"));
        Assert.Equal("desert", Class("niger_agadez.agadez_1"));
        Assert.Equal("tundra", Class("norway.bouvet_island"));
        Assert.Contains("urban: 1000", rows["vatican.vatican"]);
        Assert.Equal("forest", Class("russia_irkutsk.irkutsk_1"));
    }

    [Fact]
    public void Corrections_file_has_a_source_and_date_for_every_object()
    {
        var list = SpecSet.Read<CorrectionFile>(Path.Combine(RepoRoot(), "tools/map/spec/terrain_1945.yaml")).Corrections;
        Assert.InRange(list.Count, 25, 60);
        Assert.Equal(list.Count, list.Select(c => c.Id).Distinct().Count());
        var provinces = File.ReadAllLines(Path.Combine(RepoRoot(), "mods/core/map/provinces.yaml")).Where(l => l.StartsWith("  - {id: ")).Select(l => l[9..l.IndexOf(',')]).ToHashSet();
        foreach (var c in list)
        {
            Assert.StartsWith("https://", c.Source);
            Assert.Matches("^2026-\\d\\d-\\d\\d$", c.Accessed);
            Assert.Contains(c.Province, provinces);
            Assert.Contains(c.From, Terrain.CoverNames);
            Assert.Contains(c.To, Terrain.CoverNames);
            Assert.True(c.Km2 > 0 && c.Note.Length > 0);
        }
    }

    [Fact]
    public void Border_rivers_are_ranked_pairs_of_adjacent_provinces_and_navigable_lists_name_real_provinces()
    {
        var map = Path.Combine(RepoRoot(), "mods", "core", "map");
        var adj = File.ReadAllLines(Path.Combine(map, "adjacency.yaml")).Where(l => l.StartsWith("  - [")).Select(l => l[5..^1].Split(", ")).ToList();
        var ranked = adj.Where(p => p.Length == 3).ToList();
        Assert.InRange(ranked.Count, 50, adj.Count / 2);
        Assert.All(ranked, p => Assert.InRange(int.Parse(p[2]), 1, 4));
        Assert.All(adj, p => Assert.InRange(p.Length, 2, 3));
        var provinces = File.ReadAllLines(Path.Combine(map, "provinces.yaml")).Where(l => l.StartsWith("  - {id: ")).Select(l => l[9..l.IndexOf(',')]).ToHashSet();
        var nav = File.ReadAllLines(Path.Combine(map, "rivers.yaml")).Where(l => l.StartsWith("  - {river: ")).ToList();
        Assert.InRange(nav.Count, 5, 80);
        foreach (var l in nav)
            foreach (var id in l.Split("provinces: [")[1].TrimEnd('}', ']').Split(", ")) Assert.Contains(id, provinces);
        // Known places: a river along a border counts for both banks, the delta branches reach Cairo.
        string Along(string river) => nav.Single(l => l.StartsWith("  - {river: " + river + ","));
        Assert.Contains("vienna.wien", Along("Danube"));
        Assert.Contains("minnesota_3", Along("Mississippi"));
        Assert.Contains("wisconsin_1", Along("Mississippi"));
        Assert.Contains("egypt_matruh.al_qahirah", Along("Nile"));
        Assert.DoesNotContain(nav, l => l.StartsWith("  - {river: St. Lawrence,"));
    }

    [Fact]
    public void Lake_Tharthar_was_a_dry_depression_in_1945()
    {
        var line = File.ReadAllLines(Path.Combine(RepoRoot(), "tools", "map", "spec", "terrain_1945.yaml")).Single(l => l.Contains("{id: tharthar,"));
        Assert.Contains("from: water, to: desert", line);
        Assert.Contains("1956", line);
    }

    [Fact]
    public void Row_weights_are_symmetric_and_sum_to_the_sphere()
    {
        var w = Raster.RowWeights(10);
        Assert.Equal(1800, w.Length);
        Assert.True(w[900] > w[100]);
        Assert.Equal(w[900], w[899]);
        Assert.InRange(w.Sum(), 2_000_000_000L - 2000, 2_000_000_000L + 2000);   // sum of sin differences from pole to pole = 2
    }
}
