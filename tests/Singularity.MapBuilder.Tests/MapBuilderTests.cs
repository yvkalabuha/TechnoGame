using System.Security.Cryptography;
using NetTopologySuite.Coverage;
using NetTopologySuite.Geometries;
using Singularity.MapBuilder;

namespace Singularity.MapBuilder.Tests;

public class MapBuilderTests
{
    private static Geometry Rect(double x0, double y0, double x1, double y1) =>
        Sph.Gf.CreatePolygon(new[] { new Coordinate(x0, y0), new Coordinate(x1, y0), new Coordinate(x1, y1), new Coordinate(x0, y1), new Coordinate(x0, y0) });

    private static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Singularity.sln"))) return d.FullName;
        throw new InvalidOperationException("repo root not found");
    }

    [Fact]
    public void Area_of_one_degree_cell_matches_authalic_sphere()
    {
        // R^2 * dLon * (sin 1deg - sin 0) = 12363.7 km2
        Assert.Equal(12364, Sph.AreaKm2(Rect(0, 0, 1, 1)));
    }

    [Fact]
    public void Area_subtracts_holes_and_rounds_to_even()
    {
        var shell = Sph.Gf.CreateLinearRing(new[] { new Coordinate(0, 0), new Coordinate(4, 0), new Coordinate(4, 4), new Coordinate(0, 4), new Coordinate(0, 0) });
        var hole = Sph.Gf.CreateLinearRing(new[] { new Coordinate(1, 1), new Coordinate(1, 2), new Coordinate(2, 2), new Coordinate(2, 1), new Coordinate(1, 1) });
        var withHole = Sph.Gf.CreatePolygon(shell, new[] { hole });
        Assert.True(Sph.AreaM2(withHole) < Sph.AreaM2(Sph.Gf.CreatePolygon(shell)));
        Assert.Equal(2, (long)Math.Round(2.5, MidpointRounding.ToEven));
    }

    [Fact]
    public void Halton_sequence_is_the_van_der_Corput_sequence()
    {
        Assert.Equal(0.5, Subdivision.Halton(1, 2));
        Assert.Equal(0.25, Subdivision.Halton(2, 2));
        Assert.Equal(0.75, Subdivision.Halton(3, 2));
        Assert.Equal(1.0 / 3, Subdivision.Halton(1, 3), 12);
    }

    [Fact]
    public void Voronoi_cells_cover_the_object_exactly_and_repeat()
    {
        var g = Rect(10, 50, 20, 58);
        var a = Subdivision.Voronoi(g, 5);
        var b = Subdivision.Voronoi(g, 5);
        Assert.Equal(5, a.Count);
        Assert.Equal(Sph.AreaM2(g), a.Sum(Sph.AreaM2), 3);
        for (int i = 0; i < a.Count; i++) Assert.True(a[i].EqualsExact(b[i]));
        // The cells are a valid coverage: no overlaps, no mismatched edges.
        Assert.All(CoverageValidator.Validate(a.ToArray()), x => Assert.True(x == null || x.IsEmpty));
    }

    [Fact]
    public void Bisect_halves_the_area()
    {
        var parts = Subdivision.Bisect(Rect(0, 40, 10, 50), 2);
        Assert.Equal(2, parts.Count);
        Assert.InRange(Math.Abs(Sph.AreaM2(parts[0]) - Sph.AreaM2(parts[1])), 0, Sph.AreaM2(parts[0]) * 1e-6);
    }

    [Fact]
    public void Topology_shares_arcs_and_rebuilds_rings()
    {
        // The top polygon carries the vertex (1,1) so that the three polygons form a noded coverage.
        var top = Sph.Gf.CreatePolygon(new[] { new Coordinate(0, 1), new Coordinate(1, 1), new Coordinate(2, 1), new Coordinate(2, 2), new Coordinate(0, 2), new Coordinate(0, 1) });
        var geoms = new Geometry[] { Rect(0, 0, 1, 1), Rect(1, 0, 2, 1), top };
        var t = Topology.Build(geoms);
        var shared = t.Arcs.Where(a => a.Left >= 0 && a.Right >= 0).ToList();
        Assert.Equal(3, shared.Count);                                   // 0|1, 0|2, 1|2
        Assert.All(t.Arcs, a => Assert.True(a.Left != a.Right));
        for (int p = 0; p < geoms.Length; p++)
        {
            var ring = t.RingCoordinates(t.Rings[p][0]);
            Assert.Equal(ring[0], ring[^1]);   // the rebuilt ring is already closed
            var rebuilt = Sph.Gf.CreatePolygon(ring.ToArray());
            Assert.InRange(Math.Abs(Sph.AreaM2(geoms[p]) - Sph.AreaM2(rebuilt)), 0, Sph.AreaM2(geoms[p]) * 1e-9);
        }
        // Arc ids are sorted by (min side, max side, start vertex).
        var keys = t.Arcs.Select(a => (Math.Min(a.Left, a.Right), Math.Max(a.Left, a.Right))).ToList();
        Assert.Equal(keys.OrderBy(k => k).ToList(), keys);
    }

    [Fact]
    public void Topology_rebuilds_rings_with_negative_coordinates()
    {
        // Regression: vertex keys must keep (lat, lon) order and round-trip for negative longitudes and latitudes.
        var geoms = new[] { Rect(-5.5, -3.25, -4.5, -2.25), Rect(-4.5, -3.25, -3.5, -2.25) };
        var t = Topology.Build(geoms);
        for (int p = 0; p < geoms.Length; p++)
        {
            var ring = t.RingCoordinates(t.Rings[p][0]);
            Assert.Equal(geoms[p].Coordinates.Length, ring.Count);
            Assert.All(ring, c => Assert.Contains(geoms[p].Coordinates, g => g.Equals2D(c)));
        }
    }

    [Fact]
    public void Topology_is_identical_on_repeat()
    {
        var geoms = new[] { Rect(0, 0, 1, 1), Rect(1, 0, 2, 1) };
        var a = Writer.ArcsBin(Topology.Build(geoms), new byte[32]);
        var b = Writer.ArcsBin(Topology.Build(geoms), new byte[32]);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Cut_by_parallel_makes_two_pieces_with_a_valid_coverage_after_healing()
    {
        // Two neighbours; the cut of the left one ends on the shared border at a point the right one has no vertex at.
        var left = new Piece { Shape = Rect(0, 0, 1, 1), Adm0 = "AAA", Name = "left" };
        left.Src.Add("A-1");
        var right = new Piece { Shape = Rect(1, 0, 2, 1), Adm0 = "AAA", Name = "right" };
        right.Src.Add("A-2");
        var spec = new SpecSet();
        spec.Rules.Add(("20-test.yaml", new Rule
        {
            Cut = new CutRule { Id = "t", Within = new() { "A-1" }, Parallel = 0.4, Pieces = new() { "south", "north" }, Source = "param", AsOf = "1945-10-01", Precision = "A", Ref = "test", MinPieceKm2 = 1 },
        }));
        var res = Pipeline.Apply(new List<Piece> { left, right }, spec, _ => throw new InvalidOperationException());
        Assert.Equal(3, res.Count);
        Assert.Equal(new[] { "south", "north" }, res.Where(p => p.Cuts.Count > 0).Select(p => p.Hint));
        Assert.All(CoverageValidator.Validate(res.Select(p => p.Shape).ToArray()), x => Assert.True(x == null || x.IsEmpty));
        Assert.Equal(Sph.AreaM2(Rect(0, 0, 2, 1)), res.Sum(p => Sph.AreaM2(p.Shape)), 3);
        Assert.All(res.Where(p => p.Cuts.Count > 0), p => Assert.True(p.ClassA));
    }

    [Fact]
    public void Cut_without_ref_for_class_A_is_an_error()
    {
        var p = new Piece { Shape = Rect(0, 0, 1, 1) };
        p.Src.Add("A-1");
        var spec = new SpecSet();
        spec.Rules.Add(("20-test.yaml", new Rule
        {
            Cut = new CutRule { Id = "t", Within = new() { "A-1" }, Parallel = 0.5, Source = "s", AsOf = "1945-10-01", Precision = "A" },
        }));
        Assert.Throws<InvalidDataException>(() => Pipeline.Apply(new List<Piece> { p }, spec, _ => throw new InvalidOperationException()));
    }

    [Fact]
    public void Anchors_decide_ids_and_wrong_counts_are_errors()
    {
        var a = new Piece { Shape = Rect(0, 0, 1, 1) };
        var b = new Piece { Shape = Rect(1, 0, 2, 1) };
        var ids = new List<IdEntry>
        {
            new() { Id = "r.a", Anchor = new[] { 500000, 500000 } },
            new() { Id = "r.b", Anchor = new[] { 500000, 1500000 } },
        };
        var un = new List<Piece>();
        Assert.Empty(Ids.Assign(new List<Piece> { a, b }, ids, un));
        Assert.Equal("r.a", a.Id);
        Assert.Equal("r.b", b.Id);

        ids.Add(new IdEntry { Id = "r.c", Anchor = new[] { 600000, 600000 } });          // second anchor in piece a
        Assert.NotEmpty(Ids.Assign(new List<Piece> { a, b }, ids, new List<Piece>()));
        ids.RemoveAt(0); ids.RemoveAt(0);
        var left = new List<Piece>();
        Assert.NotEmpty(Ids.Assign(new List<Piece> { new() { Shape = Rect(0, 0, 1, 1) }, new() { Shape = Rect(1, 0, 2, 1) } }, ids, left));
        Assert.Single(left);                                                              // piece b has no anchor
    }

    [Theory]
    [InlineData("bavaria.upper_bavaria", true)]
    [InlineData("bavaria/upper", false)]
    [InlineData("Bavaria.upper", false)]
    [InlineData("bavaria.", false)]
    [InlineData("nodot", false)]
    public void Province_id_grammar(string id, bool ok) => Assert.Equal(ok, Ids.ValidProvinceId(id));

    [Fact]
    public void Slug_is_ascii_and_stable()
    {
        Assert.Equal("arkhangelsk", Bootstrap.Slug("Arkhangel'sk"));
        Assert.Equal("baden_wurttemberg", Bootstrap.Slug("Baden-Württemberg"));
        Assert.Equal("sjaaelland", Bootstrap.Slug("Sjaælland"));
        Assert.Equal("p5_th", Bootstrap.Slug("5 th"));
    }

    [Fact]
    public void Cache_hash_mismatch_is_a_loud_error()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var lk = new SourceLock { Sources = { new SourceEntry { Id = "x", Kind = "ne", Sha256 = "00" } } };
            File.WriteAllText(Sources.CachePath(dir, lk.Sources[0]), "data");
            var e = Assert.Throws<InvalidDataException>(() => Sources.ReadVerified(lk, dir, "x"));
            Assert.Contains("SHA-256 mismatch", e.Message);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Lock_pins_every_source_with_licence_and_date()
    {
        var lk = Sources.Load(Path.Combine(RepoRoot(), "tools", "map", "sources.lock.yaml"));
        Assert.Contains(lk.Sources, s => s.Id == Builder.NeAdmin1);
        Assert.Contains(lk.Sources, s => s.Id == Builder.NeUnits);
        Assert.All(lk.Sources, s =>
        {
            Assert.Matches("^[0-9a-f]{64}$", s.Sha256);
            Assert.False(string.IsNullOrEmpty(s.License));
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", s.Retrieved);
            Assert.True(Licenses.Allowed(s.License), s.License);
        });
    }

    [Fact]
    public void Package_graph_has_no_lgpl_modules()
    {
        // ADR-0011 §2.9. Reads the restore result of the builder project (built by the project reference of this test).
        var assets = Path.Combine(RepoRoot(), "tools", "map", "MapBuilder", "obj", "project.assets.json");
        var text = File.ReadAllText(assets);
        foreach (var bad in new[] { "NetTopologySuite.IO.ShapeFile", "CoordinateSystems", "ProjNet" })
            Assert.DoesNotContain(bad, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NetTopologySuite/2.6.0", text);
    }

    [Fact]
    public void Committed_manifest_binds_the_committed_tables()
    {
        Writer.VerifyBinding(Path.Combine(RepoRoot(), "mods", "core", "map"));
    }

    [Fact]
    public void Editing_a_table_without_rebuilding_geo_fails_the_binding()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var src = Path.Combine(RepoRoot(), "mods", "core", "map");
            Directory.CreateDirectory(Path.Combine(dir, "geo"));
            foreach (var f in new[] { "provinces.yaml", "adjacency.yaml", "regions.yaml" }) File.Copy(Path.Combine(src, f), Path.Combine(dir, f));
            File.Copy(Path.Combine(src, "geo", "manifest.json"), Path.Combine(dir, "geo", "manifest.json"));
            Writer.VerifyBinding(dir);
            File.AppendAllText(Path.Combine(dir, "provinces.yaml"), "# edit\n");
            var e = Assert.Throws<InvalidDataException>(() => Writer.VerifyBinding(dir));
            Assert.Contains("rebuild geo/", e.Message);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Committed_tables_are_sorted_and_consistent()
    {
        var map = Path.Combine(RepoRoot(), "mods", "core", "map");
        var provinces = File.ReadAllLines(Path.Combine(map, "provinces.yaml")).Where(l => l.StartsWith("  - {id: ")).Select(l => l[9..l.IndexOf(',')]).ToList();
        Assert.Equal(provinces.OrderBy(x => x, StringComparer.Ordinal).ToList(), provinces);
        Assert.Equal(provinces.Count, provinces.Distinct().Count());
        var set = provinces.ToHashSet();
        foreach (var l in File.ReadAllLines(Path.Combine(map, "adjacency.yaml")).Where(l => l.StartsWith("  - [")))
        {
            var pair = l[5..^1].Split(", ");
            Assert.True(string.CompareOrdinal(pair[0], pair[1]) < 0);
            Assert.Contains(pair[0], set);
            Assert.Contains(pair[1], set);
        }
    }

    [Fact]
    public void Palette_is_deterministic_integer_math()
    {
        var r = new BuildResult();
        r.Polities.AddRange(new[] { new PolityEntry { Id = "AAA" }, new PolityEntry { Id = "SUN" }, new PolityEntry { Id = "DEU" } });
        var a = Render.Palette(r);
        var b = Render.Palette(r);
        Assert.Equal(a, b);
        Assert.Equal(0xC8463Cu, a.First(x => x.Polity == "SUN").Rgb);
    }

    [Fact]
    public void Png_encoder_writes_a_valid_signature_and_chunks()
    {
        var png = Render.Encode(new uint[] { 0xFF0000, 0x00FF00, 0x0000FF, 0xFFFFFF }, 2, 2);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(png, 12, 4));
        Assert.Equal("IEND", System.Text.Encoding.ASCII.GetString(png, png.Length - 8, 4));
        _ = SHA256.HashData(png);
    }

    [Theory]
    [InlineData("CC0-1.0", true)]
    [InlineData("CC-BY-4.0", true)]
    [InlineData("CC BY 4.0", true)]
    [InlineData("CC-BY 4.0", true)]
    [InlineData("Creative Commons BY 4.0 International", true)]
    [InlineData("CC-BY-SA-4.0", false)]
    [InlineData("CC BY-NC 4.0", false)]
    [InlineData("CC-BY-1.0", false)]
    [InlineData("ODbL-1.0", false)]
    public void Licence_allow_list_is_explicit(string tag, bool ok) => Assert.Equal(ok, Licenses.Allowed(Licenses.Normalize(tag)));

    private static byte[] OhmSnapshot(string wayLicense, int version = 7) => System.Text.Encoding.UTF8.GetBytes(
        "[{\"type\":\"relation\",\"id\":42,\"version\":" + version + ",\"tags\":{\"name\":\"X\"},\"members\":[" +
        "{\"type\":\"way\",\"ref\":9,\"role\":\"outer\",\"geometry\":[{\"lat\":0,\"lon\":0},{\"lat\":0,\"lon\":1},{\"lat\":1,\"lon\":1},{\"lat\":1,\"lon\":0},{\"lat\":0,\"lon\":0}]}]}," +
        "{\"type\":\"way\",\"id\":9,\"tags\":{\"license\":\"" + wayLicense + "\",\"attribution\":\"Some Agency\"}}]");

    [Fact]
    public void Ohm_member_way_licence_is_read_and_cc_by_goes_to_attribution()
    {
        var shape = Data.LoadOhm(OhmSnapshot("CC BY 4.0"), "ohm:relation/42@v7");
        Assert.Contains("CC-BY-4.0", shape.Licenses);
        Assert.Contains("CC0-1.0", shape.Licenses);
        Assert.All(shape.Licenses, l => Assert.True(Licenses.Allowed(l)));
        var r = new BuildResult();
        r.Ohm["ohm:relation/42@v7"] = shape;
        var text = System.Text.Encoding.UTF8.GetString(Writer.Attribution(r));
        Assert.Contains("Some Agency, CC-BY-4.0: way/9", text);
        Assert.Contains("None.", System.Text.Encoding.UTF8.GetString(Writer.Attribution(new BuildResult())));
        Assert.Contains(Data.LoadOhm(OhmSnapshot("CC-BY-SA-4.0"), "ohm:relation/42@v7").Licenses, l => !Licenses.Allowed(l));
    }

    [Fact]
    public void Ohm_lock_id_must_match_snapshot_version()
    {
        Assert.Throws<InvalidDataException>(() => Data.LoadOhm(OhmSnapshot("CC0-1.0"), "ohm:relation/42@v8"));
        Assert.Throws<InvalidDataException>(() => Data.LoadOhm(OhmSnapshot("CC0-1.0"), "ohm:relation/43@v7"));
    }

    [Fact]
    public void Writer_output_does_not_depend_on_the_current_culture()
    {
        var r = new BuildResult();
        var shape = Rect(-5.5, -3.25, -4.5, -2.25);
        var p = new Piece { Shape = shape, Id = "a.b", AreaKm2 = 12345 };
        var q = new Piece { Shape = Rect(-4.5, -3.25, -3.5, -2.25), Id = "a.c", AreaKm2 = 1234567 };
        r.Pieces.AddRange(new[] { p, q });
        r.Coastal.AddRange(new[] { true, false });
        r.Adjacency[("a.b", "a.c")] = 111.1;
        r.Spec.Regions.Add(new RegionEntry { Id = "a", Capital = "a.b" });
        r.Simple = Topology.Build(new[] { shape, q.Shape });
        byte[] All() => Writer.Provinces(r).Concat(Writer.Adjacency(r)).Concat(Writer.Regions(r)).Concat(Writer.Straits(r))
            .Concat(Writer.ArcsBin(r.Simple, new byte[32])).Concat(Writer.Manifest(r, new byte[32], new byte[4])).ToArray();
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            var inv = All();
            // sv-SE writes U+2212 as the minus sign when ICU is present.
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("sv-SE");
            Assert.Equal(inv, All());
            Assert.Contains("-2750000", System.Text.Encoding.UTF8.GetString(Writer.Provinces(r)));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void Cut_flags_class_A_only_on_the_named_piece_and_merge_drops_it()
    {
        var p = new Piece { Shape = Rect(0, 0, 1, 1), Adm0 = "A", Name = "p" };
        p.Src.Add("A-1");
        var spec = new SpecSet();
        spec.Rules.Add(("20.yaml", new Rule { Cut = new CutRule { Id = "t", Within = new() { "A-1" }, Meridian = 0.5, Pieces = new() { "w", "e" }, APieces = new() { "e" }, Source = "s", AsOf = "1945-10-01", Precision = "A", Ref = "r", MinPieceKm2 = 1 } }));
        var res = Pipeline.Apply(new List<Piece> { p }, spec, _ => throw new InvalidOperationException());
        Assert.Equal(new[] { false, true }, res.Select(x => x.ClassA));
        var m = new SpecSet();
        m.Rules.Add(("10.yaml", new Rule { Merge = new MergeRule { From = new() { "A-1" } } }));
        // A merge of the two halves (same Src) is a no-op for one code; a real merge of two pieces must not inherit the flag.
        var a = new Piece { Shape = Rect(0, 0, 1, 1), ClassA = true, Ref = "r", Adm0 = "A", Name = "a" }; a.Src.Add("A-1");
        var b = new Piece { Shape = Rect(1, 0, 2, 1), Adm0 = "A", Name = "b" }; b.Src.Add("A-2");
        m.Rules.Clear();
        m.Rules.Add(("10.yaml", new Rule { Merge = new MergeRule { From = new() { "A-1", "A-2" } } }));
        var merged = Pipeline.Apply(new List<Piece> { a, b }, m, _ => throw new InvalidOperationException());
        Assert.Single(merged);
        Assert.False(merged[0].ClassA);
    }

    [Fact]
    public void Moving_a_cut_line_within_the_anchors_keeps_the_id_set()
    {
        IEnumerable<string> IdsFor(double parallel)
        {
            var p = new Piece { Shape = Rect(0, 0, 1, 1), Adm0 = "A", Name = "p" };
            p.Src.Add("A-1");
            var spec = new SpecSet();
            spec.Rules.Add(("20.yaml", new Rule { Cut = new CutRule { Id = "t", Within = new() { "A-1" }, Parallel = parallel, Pieces = new() { "s", "n" }, Source = "s", AsOf = "1945-10-01", Precision = "A", Ref = "r", MinPieceKm2 = 1 } }));
            var pieces = Pipeline.Apply(new List<Piece> { p }, spec, _ => throw new InvalidOperationException());
            var ids = new List<IdEntry>
            {
                new() { Id = "r.south", Anchor = new[] { 100000, 500000 } },
                new() { Id = "r.north", Anchor = new[] { 900000, 500000 } },
            };
            Assert.Empty(Ids.Assign(pieces, ids, new List<Piece>()));
            return pieces.Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
        }
        Assert.Equal(IdsFor(0.4), IdsFor(0.6));
        Assert.Equal(new[] { "r.north", "r.south" }, IdsFor(0.5));
    }
}
