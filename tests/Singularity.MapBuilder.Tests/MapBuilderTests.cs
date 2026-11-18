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
    public void Owners_1945_cover_every_region_with_known_polities_and_confidence()
    {
        var root = RepoRoot();
        var regions = File.ReadAllLines(Path.Combine(root, "mods/core/map/regions.yaml"))
            .Where(l => l.TrimStart().StartsWith("- {id: ")).Select(l => l.Split("id: ")[1].Split(',')[0]).ToList();
        var owners = File.ReadAllLines(Path.Combine(root, "mods/core/scenarios/1945/owners.yaml")).Where(l => l.TrimStart().StartsWith("- {region: ")).ToList();
        var polities = File.ReadAllLines(Path.Combine(root, "mods/core/scenarios/1945/polities.yaml")).Where(l => l.Contains("{id: ")).Select(l => l.Split("id: ")[1].Split(',')[0]).ToList();
        Assert.Equal(polities.Count, polities.Distinct().Count());
        Assert.DoesNotContain("CHN", polities);
        var byRegion = owners.Select(l => l.Split("region: ")[1].Split(',')[0]).ToList();
        Assert.Equal(regions.OrderBy(x => x, StringComparer.Ordinal), byRegion.OrderBy(x => x, StringComparer.Ordinal));
        foreach (var l in owners)
        {
            Assert.Contains("confidence: ", l);
            Assert.Contains("source: \"", l);
            var ownerId = l.Split("owner: ")[1].Split(',')[0];
            if (ownerId == "null") Assert.Contains("status: unclaimed", l);
            else Assert.Contains(ownerId, polities);
            Assert.Contains(l.Split("controller: ")[1].Split(',')[0], polities);
        }
    }

    private static List<string> OwnerErrors(string? owner, string status, string? controller = "JPN")
    {
        var r = new BuildResult();
        r.Pieces.Add(new Piece { Id = "reg.p1" });
        var f = new OwnerFile
        {
            AsOf = "1945-10-01",
            Defaults = { new OwnerRow { Region = "reg", Owner = owner, Controller = controller ?? "", Status = status, Source = "s", Confidence = "L" } }
        };
        Validator.ValidateOwners(r, f, new HashSet<string> { "reg.p1" }, new HashSet<string> { "reg" }, new HashSet<string> { "JPN", "FRA" });
        return r.Errors;
    }

    private static BuildResult Quota(string owner, int core, int colony, GreatPowerFile gp, bool world = true, string? secondStatusForFirst = null)
    {
        var r = new BuildResult();
        r.Spec.Area.World = world;
        var f = new OwnerFile { AsOf = "1945-10-01" };
        var ids = new HashSet<string>();
        var regs = new HashSet<string>();
        for (int i = 0; i < core + colony; i++)
        {
            var reg = "r" + i;
            regs.Add(reg);
            r.Pieces.Add(new Piece { Id = reg + ".p1" });
            ids.Add(reg + ".p1");
            f.Defaults.Add(new OwnerRow { Region = reg, Owner = owner, Controller = owner, Status = i < core ? "core" : "colony", Source = "s", Confidence = "H" });
        }
        if (secondStatusForFirst != null)
        {
            r.Pieces.Add(new Piece { Id = "r0.p2" });
            ids.Add("r0.p2");
            f.Exceptions.Add(new OwnerRow { Province = "r0.p2", Owner = owner, Controller = owner, Status = secondStatusForFirst, Source = "s", Confidence = "H" });
        }
        Validator.ValidateOwners(r, f, ids, regs, new HashSet<string> { "GBR", "SUN", "XXX" }, gp);
        return r;
    }

    private static GreatPowerFile Gp(params string[] ids) => new() { Ids = ids.ToList() };
    private static GreatPowerFile GpSun() => new()
    {
        Ids = { "SUN" },
        Exceptions = { new GreatPowerException { Id = "SUN", MaxCore = 35, Reason = "#406" } }
    };

    [Fact]
    public void Region_count_limit_is_150_to_400_warning_only()
    {
        foreach (var (n, warns) in new[] { (400, false), (401, true), (150, false), (149, true) })
        {
            var r = new BuildResult();
            Validator.CheckRegionCount(r, n);
            Assert.Equal(warns, r.Warnings.Count == 1);
            Assert.Empty(r.Errors);
        }
        var hi = new BuildResult(); Validator.CheckRegionCount(hi, 401);
        Assert.Contains("exceed the ADR-0022 provisional limit of 400 (report to the owner)", hi.Warnings[0]);
        var lo = new BuildResult(); Validator.CheckRegionCount(lo, 149);
        Assert.Contains("below the ADR-0022 minimum of 150", lo.Warnings[0]);
    }

    [Fact]
    public void Quota_warns_above_15_and_below_5_core()
    {
        Assert.Contains(Quota("GBR", 16, 0, Gp("GBR")).Warnings, w => w.Contains("actor GBR") && w.Contains("maximum of 15"));
        Assert.Contains(Quota("GBR", 4, 0, Gp("GBR")).Warnings, w => w.Contains("below the #171 minimum of 5"));
        Assert.DoesNotContain(Quota("GBR", 5, 0, Gp("GBR")).Warnings, w => w.Contains("actor"));
    }

    [Fact]
    public void Quota_counts_only_core_and_prints_status_info()
    {
        var r = Quota("GBR", 10, 80, Gp("GBR"));
        Assert.Empty(r.Warnings);
        Assert.Contains("actor GBR: core 10, colony 80, protectorate 0, mandate 0, leased 0", r.Info);
    }

    [Fact]
    public void Quota_ignores_actors_outside_great_powers()
    {
        var r = Quota("XXX", 20, 0, Gp());
        Assert.DoesNotContain(r.Warnings, w => w.Contains("actor"));
        Assert.Empty(r.Errors);
    }

    [Fact]
    public void Great_powers_unknown_id_is_an_error()
    {
        Assert.Contains(Quota("GBR", 5, 0, Gp("GBR", "NOPE")).Errors, e => e.Contains("unknown id 'NOPE'"));
    }

    [Fact]
    public void Region_with_two_statuses_is_an_error_listing_provinces()
    {
        var r = Quota("GBR", 5, 0, Gp("GBR"), secondStatusForFirst: "colony");
        var e = Assert.Single(r.Errors);
        Assert.Contains("region r0 has several statuses", e);
        Assert.Contains("r0.p1", e);
        Assert.Contains("r0.p2", e);
    }

    [Fact]
    public void Quota_named_exception_raises_the_limit_for_SUN_only()
    {
        Assert.DoesNotContain(Quota("SUN", 31, 0, GpSun()).Warnings, w => w.Contains("actor"));
        Assert.Contains(Quota("SUN", 36, 0, GpSun()).Warnings, w => w.Contains("maximum of 35"));
    }

    [Fact]
    public void Quota_outside_world_build_prints_info_without_warnings()
    {
        var r = Quota("GBR", 2, 3, Gp("GBR"), world: false);
        Assert.Empty(r.Warnings);
        Assert.Contains("actor GBR: core 2, colony 3, protectorate 0, mandate 0, leased 0", r.Info);
    }

    [Fact]
    public void Great_powers_exception_misuse_is_an_error()
    {
        GreatPowerFile F(params GreatPowerException[] ex) => new() { Ids = { "SUN", "GBR" }, Exceptions = ex.ToList() };
        GreatPowerException E(string id, int max) => new() { Id = id, MaxCore = max, Reason = "x" };
        Assert.Contains(Quota("SUN", 5, 0, F(E("USA", 35))).Errors, e => e.Contains("not in ids"));
        Assert.Contains(Quota("SUN", 5, 0, F(E("SUN", 35), E("SUN", 36))).Errors, e => e.Contains("duplicate exception"));
        Assert.Contains(Quota("SUN", 5, 0, F(E("SUN", 0))).Errors, e => e.Contains("above 15"));
        Assert.Contains(Quota("SUN", 5, 0, F(E("SUN", 15))).Errors, e => e.Contains("above 15"));
        var dup = new GreatPowerFile { Ids = { "SUN", "SUN" } };
        Assert.Contains(Quota("SUN", 5, 0, dup).Errors, e => e.Contains("duplicate id 'SUN'"));
    }

    [Fact]
    public void Territory_exception_with_another_status_is_an_error()
    {
        var r = new BuildResult();
        r.Spec.Territories.Add(new TerritoryEntry { Id = "reg.t1" });
        r.Pieces.Add(new Piece { Id = "reg.p1" });
        var f = new OwnerFile
        {
            AsOf = "1945-10-01",
            Defaults = { new OwnerRow { Region = "reg", Owner = "GBR", Controller = "GBR", Status = "core", Source = "s", Confidence = "H" } },
            Exceptions = { new OwnerRow { Province = "reg.t1", Owner = "GBR", Controller = "GBR", Status = "colony", Source = "s", Confidence = "H" } }
        };
        Validator.ValidateOwners(r, f, new HashSet<string> { "reg.p1" }, new HashSet<string> { "reg" }, new HashSet<string> { "GBR" });
        var e = Assert.Single(r.Errors);
        Assert.Contains("reg.t1", e);
        Assert.Contains("reg.p1", e);
    }

    [Fact]
    public void Great_powers_file_1945_has_SUN_exception_with_reason()
    {
        var gp = SpecSet.Read<GreatPowerFile>(Path.Combine(RepoRoot(), "tools/map/spec/great_powers.yaml"));
        Assert.Equal(new[] { "USA", "SUN", "GBR", "FRA", "ROC" }, gp.Ids);
        var ex = Assert.Single(gp.Exceptions);
        Assert.Equal(("SUN", 35), (ex.Id, ex.MaxCore));
        Assert.False(string.IsNullOrWhiteSpace(ex.Reason));
    }

    [Fact]
    public void Owners_unclaimed_allows_null_owner_only_with_that_status()
    {
        Assert.Empty(OwnerErrors(null, "unclaimed"));
        Assert.Contains(OwnerErrors(null, "core"), e => e.Contains("only status unclaimed"));
        Assert.Contains(OwnerErrors("FRA", "unclaimed"), e => e.Contains("unclaimed needs owner null"));
        Assert.Contains(OwnerErrors(null, "unclaimed", "XXX"), e => e.Contains("unknown controller"));
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
    public void Cut_with_an_unknown_a_pieces_hint_is_an_error()
    {
        var p = new Piece { Shape = Rect(0, 0, 1, 1) };
        p.Src.Add("A-1");
        var spec = new SpecSet();
        spec.Rules.Add(("20-test.yaml", new Rule
        {
            Cut = new CutRule { Id = "t", Within = new() { "A-1" }, Parallel = 0.5, Pieces = new() { "south", "north" }, APieces = new() { "nrth" }, Source = "s", AsOf = "1945-10-01", Precision = "A", Ref = "r" },
        }));
        var ex = Assert.Throws<InvalidDataException>(() => Pipeline.Apply(new List<Piece> { p }, spec, _ => throw new InvalidOperationException()));
        Assert.Contains("nrth", ex.Message);
    }

    private static Piece Holed(double hole)
    {
        var shell = Sph.Gf.CreateLinearRing(new[] { new Coordinate(0, 0), new Coordinate(3, 0), new Coordinate(3, 3), new Coordinate(0, 3), new Coordinate(0, 0) });
        var h = Sph.Gf.CreateLinearRing(new[] { new Coordinate(1, 1), new Coordinate(1 + hole, 1), new Coordinate(1 + hole, 1 + hole), new Coordinate(1, 1 + hole), new Coordinate(1, 1) });
        var p = new Piece { Shape = Sph.Gf.CreatePolygon(shell, new[] { h }), Adm0 = "AAA", Name = "holed" };
        p.Src.Add("A-1");
        p.Cuts.Add("t");
        return p;
    }

    [Fact]
    public void Heal_fails_loudly_when_a_large_face_belongs_to_no_piece()
    {
        // A 0.1 degree hole (about 120 km2) is a gap no piece owns.
        var ex = Assert.Throws<InvalidDataException>(() => Pipeline.Heal(new List<Piece> { Holed(0.1) }));
        Assert.Contains("dropped a face", ex.Message);
    }

    [Fact]
    public void Heal_fails_loudly_when_a_small_overlap_changes_the_area()
    {
        // Pieces overlapping by 0.01 x 0.01 degrees (about 1.2 km2): the face goes to one piece only, so the total drops beyond 1e-6.
        var a = new Piece { Shape = Rect(0, 0, 1, 1), Adm0 = "AAA", Name = "a" };
        a.Src.Add("A-1");
        a.Cuts.Add("t");
        var b = new Piece { Shape = Rect(0.99, 0.99, 2, 2), Adm0 = "AAA", Name = "b" };
        b.Src.Add("A-2");
        var ex = Assert.Throws<InvalidDataException>(() => Pipeline.Heal(new List<Piece> { a, b }));
        Assert.Contains("changed the area", ex.Message);
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
            Assert.All(s.License.Split('+'), l => Assert.True(Licenses.Allowed(l), s.License));
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
            foreach (var f in new[] { "provinces.yaml", "adjacency.yaml", "regions.yaml", "territories.yaml" }) File.Copy(Path.Combine(src, f), Path.Combine(dir, f));
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

    // ---- TASK-0091: world build, event lines, territories ----

    private static CutRule ParallelCut(double parallel, string[] regions, double minKm2 = 1) => new()
    {
        Id = "t", Within = new() { "A-1" }, Parallel = parallel, Pieces = new() { "south", "north" }, Regions = regions.ToList(),
        Source = "param", AsOf = "1945-10-01", Precision = "A", Ref = "test", MinPieceKm2 = minKm2,
    };

    private static Piece Unit(string code, string adm0, Geometry shape)
    {
        var p = new Piece { Shape = shape, Adm0 = adm0, Name = code };
        p.Src.Add(code);
        return p;
    }

    [Fact]
    public void Cut_pieces_carry_their_side_and_region_hint()
    {
        var spec = new SpecSet();
        spec.Rules.Add(("20-test.yaml", new Rule { Cut = ParallelCut(0.4, new[] { "r_south", "r_north" }) }));
        var res = Pipeline.Apply(new List<Piece> { Unit("A-1", "AAA", Rect(0, 0, 1, 1)) }, spec, _ => throw new InvalidOperationException());
        Assert.Equal(new[] { "t:south", "t:north" }, res.Select(p => Assert.Single(p.Sides)));
        Assert.Equal(new[] { "r_south", "r_north" }, res.Select(p => p.RegionHint));
    }

    [Fact]
    public void A_piece_wholly_on_one_side_keeps_its_shape_and_is_tagged()
    {
        var spec = new SpecSet();
        spec.Rules.Add(("20-test.yaml", new Rule { Cut = ParallelCut(5, new[] { "r_south", "r_north" }) }));
        var whole = Rect(0, 0, 1, 1);
        var res = Pipeline.Apply(new List<Piece> { Unit("A-1", "AAA", whole) }, spec, _ => throw new InvalidOperationException());
        var p = Assert.Single(res);
        Assert.Equal(new[] { "t:south" }, p.Sides);
        Assert.Equal("r_south", p.RegionHint);
        Assert.Empty(p.Cuts);   // no new geometry: the piece is still a plain unit
        Assert.True(whole.EqualsExact(p.Shape));
    }

    [Fact]
    public void A_cut_that_leaves_only_slivers_on_one_side_does_not_split_the_piece()
    {
        var spec = new SpecSet();
        spec.Rules.Add(("20-test.yaml", new Rule { Cut = ParallelCut(0.0001, new[] { "r_south", "r_north" }, 40) }));
        var whole = Rect(0, 0, 1, 1);
        var res = Pipeline.Apply(new List<Piece> { Unit("A-1", "AAA", whole) }, spec, _ => throw new InvalidOperationException());
        var p = Assert.Single(res);
        Assert.Equal(new[] { "t:north" }, p.Sides);
        Assert.True(whole.EqualsExact(p.Shape));
    }

    [Fact]
    public void Cut_within_adm0_reaches_every_piece_of_the_country()
    {
        var spec = new SpecSet();
        var cut = ParallelCut(0.5, new[] { "s", "n" });
        cut.Within = new();
        cut.WithinAdm0 = new() { "AAA" };
        spec.Rules.Add(("20-test.yaml", new Rule { Cut = cut }));
        var res = Pipeline.Apply(new List<Piece> { Unit("A-1", "AAA", Rect(0, 0, 1, 1)), Unit("A-2", "AAA", Rect(1, 0, 2, 1)), Unit("B-1", "BBB", Rect(2, 0, 3, 1)) }, spec, _ => throw new InvalidOperationException());
        Assert.Equal(5, res.Count);
        Assert.Empty(res.Single(p => p.Src.Contains("B-1")).Sides);
        Assert.Throws<InvalidDataException>(() => Pipeline.Apply(new List<Piece> { Unit("B-1", "BBB", Rect(2, 0, 3, 1)) }, spec, _ => throw new InvalidOperationException()));
    }

    private static BuildResult EventWorld(params (string Id, string Adm0, string[] Sides)[] provinces)
    {
        var r = new BuildResult();
        foreach (var (id, adm0, sides) in provinces)
            r.Pieces.Add(new Piece { Id = id, Adm0 = adm0, Sides = sides.ToList() });
        return r;
    }

    [Fact]
    public void Event_check_accepts_regions_that_follow_the_line_and_rejects_a_crossing_region()
    {
        var r = EventWorld(("north.a", "KOR", new[] { "korea:north" }), ("north.b", "KOR", new[] { "korea:north" }), ("south.a", "KOR", new[] { "korea:south" }), ("other.a", "JPN", new string[0]));
        r.Spec.Events.Add(new EventEntry
        {
            Id = "korea", Date = "1948-09-09", Rr = "C22",
            Sets = new() { new EventSet { Name = "north", Cuts = new() { "korea:north" } } },
        });
        Validator.CheckEvents(r, new List<string>());
        Assert.Empty(r.Errors);

        // A province of the south in the northern region: the region crosses the line.
        r.Pieces[2].Id = "north.c";
        Validator.CheckEvents(r, new List<string>());
        var e = Assert.Single(r.Errors);
        Assert.Contains("region north crosses the line", e);
    }

    [Fact]
    public void Event_check_by_source_codes_fails_when_a_region_straddles_a_line_it_names()
    {
        // Region 'zone' carries the name of the event set, but one of its provinces was made from a unit outside the set (a tautological prefix selector would pass).
        var r = EventWorld(("zone.a", "DEU", new string[0]), ("zone.b", "DEU", new string[0]), ("rest.a", "DEU", new string[0]));
        r.Pieces[0].Src.Add("DEU-1");
        r.Pieces[1].Src.Add("DEU-2");
        r.Pieces[2].Src.Add("DEU-3");
        r.Spec.Events.Add(new EventEntry { Id = "zone", Date = "1949-10-07", Rr = "C05", Sets = new() { new EventSet { Name = "zone", Codes = new() { "DEU-1" } } } });
        Validator.CheckEvents(r, new List<string>());
        Assert.Contains(r.Errors, e => e.Contains("region zone crosses the line"));

        r.Errors.Clear();
        r.Spec.Events[0].Sets[0].Codes.Add("DEU-2");
        Validator.CheckEvents(r, new List<string>());
        Assert.Empty(r.Errors);

        r.Spec.Events[0].Sets[0].ExceptCodes.Add("DEU-2");
        Validator.CheckEvents(r, new List<string>());
        Assert.Contains(r.Errors, e => e.Contains("region zone crosses the line"));
    }

    [Fact]
    public void Committed_event_sets_do_not_select_by_region_prefix_inside_a_country()
    {
        var spec = SpecSet.Load(Path.Combine(RepoRoot(), "tools", "map", "spec"));
        Assert.All(spec.Events.SelectMany(e => e.Sets), s => Assert.True(s.Ids.Count == 0, $"set {s.Name} selects by id prefix"));
    }

    [Fact]
    public void Slivers_an_empty_side_keeps_the_piece_whole_on_the_other_side()
    {
        // The whole piece is smaller than the threshold, so All(Small) is true for the non-empty side: the empty side must still lose.
        var whole = Rect(0, 0, 0.05, 0.05);
        var empty = Sph.Gf.CreatePolygon();
        var (a, b) = Pipeline.AbsorbSlivers(whole, empty, 100, whole);
        Assert.Same(whole, a);
        Assert.True(b.IsEmpty);
        var (c, d) = Pipeline.AbsorbSlivers(empty, whole, 100, whole);
        Assert.True(c.IsEmpty);
        Assert.Same(whole, d);
    }

    [Fact]
    public void Event_check_separates_two_sets_of_one_event_and_covers_territories()
    {
        var r = EventWorld(("zone_a.x", "ITA", new[] { "tr:a" }), ("zone_b.x", "HRV", new[] { "tr:b" }), ("italy.rest", "ITA", new string[0]));
        r.Spec.Events.Add(new EventEntry
        {
            Id = "tr", Date = "1954-10-26", Rr = "C14",
            Sets = new()
            {
                new EventSet { Name = "a", Cuts = new() { "tr:a" } },
                new EventSet { Name = "b", Cuts = new() { "tr:b" } },
                new EventSet { Name = "t", Ids = new() { "tiny.*" } },
            },
        });
        Validator.CheckEvents(r, new List<string> { "tiny.one" });
        Assert.Empty(r.Errors);

        // The territory joins the region of zone A: two sets in one region.
        Validator.CheckEvents(r, new List<string> { "tiny.one", "zone_a.t" });
        Assert.Contains(r.Errors, e => e.Contains("region zone_a crosses the line"));
    }

    [Fact]
    public void Event_check_reports_empty_sets_exceptions_and_undrawn_lines()
    {
        var r = EventWorld(("a.x", "AAA", new string[0]), ("a.y", "AAA", new string[0]), ("b.x", "BBB", new string[0]));
        r.Spec.Events.Add(new EventEntry { Id = "empty", Date = "1950-01-01", Rr = "B1", Sets = new() { new EventSet { Name = "nothing", Ids = new() { "zzz.*" } } } });
        r.Spec.Events.Add(new EventEntry { Id = "undrawn", Date = "1950-01-01", Rr = "B2", NotDrawn = "no source" });
        r.Spec.Events.Add(new EventEntry { Id = "except", Date = "1950-01-01", Rr = "B3", Sets = new() { new EventSet { Name = "aaa", Adm0 = new() { "AAA" }, ExceptIds = new() { "a.y" } } } });
        Validator.CheckEvents(r, new List<string>());
        Assert.Contains(r.Errors, e => e.Contains("event empty") && e.Contains("matches nothing"));
        Assert.Contains(r.Errors, e => e.Contains("event line except") && e.Contains("region a crosses the line"));
        Assert.Contains(r.Warnings, i => i.StartsWith("pending_map:") && i.Contains("not drawn: undrawn"));
        Assert.Equal(2, r.Errors.Count);
    }

    [Fact]
    public void Committed_spec_events_territories_and_cuts_are_well_formed()
    {
        var spec = SpecSet.Load(Path.Combine(RepoRoot(), "tools", "map", "spec"));
        Assert.NotEmpty(spec.Events);
        Assert.Equal(spec.Events.Count, spec.Events.Select(e => e.Id).Distinct().Count());
        Assert.All(spec.Events, e =>
        {
            Assert.Matches("^[a-z][a-z0-9_]*$", e.Id);
            Assert.False(string.IsNullOrWhiteSpace(e.Rr));
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", e.Date);
            Assert.True((e.NotDrawn != null) != (e.Sets.Count > 0), e.Id + ": either sets or not_drawn");
        });
        Assert.All(spec.Territories, t => { Assert.True(Ids.ValidProvinceId(t.Id)); Assert.False(string.IsNullOrWhiteSpace(t.Ref)); });
        foreach (var (file, rule) in spec.Rules.Where(r => r.Rule.Cut != null))
        {
            var c = rule.Cut!;
            Assert.True(c.Regions.Count <= Math.Max(2, c.Pieces.Count), $"{file} {c.Id}");
            Assert.All(c.Regions.Where(x => x != ""), x => Assert.True(Ids.ValidRegionId(x), $"{file} {c.Id} region {x}"));
        }
        Assert.All(spec.Rules.Where(r => r.Rule.Isolate != null), r => { Assert.True(Ids.ValidRegionId(r.Rule.Isolate!.Region)); Assert.NotEmpty(r.Rule.Isolate.Codes); Assert.False(string.IsNullOrWhiteSpace(r.Rule.Isolate.Ref)); });
        // Every cut that names a region for one side names a source and a date.
        Assert.All(spec.Rules.Where(r => r.Rule.Cut is { Regions.Count: > 0 }), r => { Assert.NotEqual("", r.Rule.Cut!.AsOf); Assert.NotEqual("", r.Rule.Cut.Source); });
    }

    [Fact]
    public void Committed_world_tables_hold_regions_territories_and_counts()
    {
        var map = Path.Combine(RepoRoot(), "mods", "core", "map");
        var provinces = File.ReadAllLines(Path.Combine(map, "provinces.yaml")).Where(l => l.StartsWith("  - {id: ")).Select(l => l[9..l.IndexOf(',')]).ToList();
        var territories = File.ReadAllLines(Path.Combine(map, "territories.yaml")).Where(l => l.StartsWith("  - {id: ")).Select(l => l[9..l.IndexOf(',')]).ToList();
        Assert.Equal(territories.OrderBy(x => x, StringComparer.Ordinal).ToList(), territories);
        Assert.Empty(territories.Intersect(provinces));
        var regions = File.ReadAllLines(Path.Combine(map, "regions.yaml")).Where(l => l.StartsWith("  - {id: ")).Select(l => l[9..l.IndexOf(',')]).ToList();
        var used = provinces.Concat(territories).Select(i => i[..i.IndexOf('.')]).ToHashSet();
        Assert.Equal(regions.OrderBy(x => x, StringComparer.Ordinal).ToList(), regions);
        Assert.Equal(regions.ToHashSet(), used);   // no empty region, no province of an undeclared region
        Assert.True(provinces.Count > 1500, "the world build has more than the Europe set");
    }
}
