using Singularity.Client.Spike;

namespace Singularity.MapBuilder.Tests;

/// <summary>A fact that is skipped (not passed) only where CI has no pinned map sources (SINGULARITY_GEO_OPTIONAL=1 and no arcs.bin).</summary>
public sealed class GeoFactAttribute : FactAttribute
{
    public GeoFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SINGULARITY_GEO_OPTIONAL") != "1") return;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Singularity.sln")))
            {
                if (!File.Exists(Path.Combine(d.FullName, "mods/core/map/geo/arcs.bin"))) Skip = "geo/arcs.bin absent (optional in this CI run)";
                return;
            }
    }
}

public sealed class GeoTheoryAttribute : TheoryAttribute
{
    public GeoTheoryAttribute() { var f = new GeoFactAttribute(); Skip = f.Skip; }
}

/// <summary>TASK-0138: the client's 1945 world map (regions from arcs.bin, owners from owners.yaml, palette assignment).</summary>
public class WorldMapTests
{
    private static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Singularity.sln"))) return d.FullName;
        throw new InvalidOperationException("repo root not found");
    }

    private static readonly Lazy<(WorldMap Map, ushort[] Raster)?> World = new(() =>
    {
        var root = RepoRoot();
        if (!File.Exists(Path.Combine(root, "mods/core/map/geo/arcs.bin")))
        {
            // CI without the pinned sources may skip; everywhere else a missing arcs.bin is a failure with the fix.
            if (Environment.GetEnvironmentVariable("SINGULARITY_GEO_OPTIONAL") == "1") return null;
            throw new FileNotFoundException("geo/arcs.bin is missing: " + WorldMap.FixHint);
        }
        var m = WorldMap.Load(root);
        return (m, m.RasteriseRegions());
    });

    private static (WorldMap Map, ushort[] Raster) Need() => World.Value ?? throw new Xunit.Sdk.XunitException("geo optional and absent");

    private static int RegionAt(WorldMap m, ushort[] raster, double lon, double lat)
    {
        int x = (int)((lon + 180) / 360 * WorldMap.W), y = (int)((90 - lat) / 180 * WorldMap.H);
        return raster[y * WorldMap.W + x];
    }

    // Ten places, each checked against the owners.yaml row of the region that the raster puts there (lon, lat, region id, owner, status).
    public static IEnumerable<object[]> Places() => new[]
    {
        new object[] { 37.6, 55.7, "russia_nizhegorod", "SUN", "core" },
        new object[] { 2.3, 48.85, "france_marne", "FRA", "core" },
        new object[] { 13.4, 52.5, null!, "DEU", "core" },
        new object[] { 77.2, 28.6, null!, "GBR", "colony" },
        new object[] { 24.0, -2.0, null!, "BEL", "colony" },
        new object[] { 3.0, 36.7, null!, "FRA", null! },
        new object[] { -77.0, 38.9, null!, "USA", "core" },
        new object[] { 18.0, 59.3, null!, "SWE", "core" },
        new object[] { 139.7, 35.7, null!, "JPN", "core" },
        new object[] { 32.5, 15.6, null!, "SDN_AE", "colony" },
        new object[] { 106.9, 47.9, null!, "MNG", "core" }, // Ulaanbaatar: Outer Mongolia is its own country in 1945 (#550)
    };

    [GeoTheory]
    [MemberData(nameof(Places))]
    public void Raster_pixel_agrees_with_owners_yaml(double lon, double lat, string? region, string owner, string? status)
    {
        var (map, raster) = Need();
        int id = RegionAt(map, raster, lon, lat);
        Assert.NotEqual(0, id);
        var row = map.Rows[id - 1];
        if (region != null) Assert.Equal(region, row.Region);
        Assert.Equal(owner, row.Owner);
        if (status != null) Assert.Equal(status, row.Status);
        Assert.Equal(map.RegionOwner[id - 1], map.Owners.ToList().IndexOf(owner) + 1);
    }

    [GeoFact]
    public void Every_region_has_pixels_or_is_tiny_and_every_owner_row_is_known()
    {
        var (map, raster) = Need();
        Assert.Equal(379, map.RegionIds.Length);
        var seen = new HashSet<int>(raster.Where(r => r != 0).Select(r => (int)r));
        Assert.True(seen.Count > 340, "regions on the raster: " + seen.Count);
        Assert.All(map.Rows, r => Assert.NotNull(r));
        Assert.All(map.Rows.Where(r => r.Owner != ""), r => Assert.Contains(map.Polities, p => p.Id == r.Owner));
    }

    [GeoFact]
    public void Binding_mismatch_is_a_loud_error()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "geo-binding-" + Guid.NewGuid().ToString("N"));
        try
        {
            var src = Path.Combine(RepoRoot(), "mods");
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(src, f);
                if (!(rel.StartsWith("core/map") || rel.StartsWith("core/scenarios/1945"))) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(tmp, "mods", rel))!);
                File.Copy(f, Path.Combine(tmp, "mods", rel));
            }
            File.AppendAllText(Path.Combine(tmp, "mods/core/map/regions.yaml"), "# edited\n");
            var ex = Assert.Throws<InvalidDataException>(() => WorldMap.Load(tmp));
            Assert.Contains("data_binding", ex.Message);
            Assert.Contains("ensure_geo.py", ex.Message);
        }
        finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); }
    }

    [Fact]
    public void Missing_arcs_bin_names_the_fix()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "geo-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmp, "mods/core/map/geo"));
        try
        {
            var ex = Assert.Throws<FileNotFoundException>(() => WorldMap.Load(tmp));
            Assert.Contains("ensure_geo.py", ex.Message);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [GeoFact]
    public void Palette_is_deterministic_fixes_the_five_powers_and_keeps_neighbours_apart()
    {
        var (map, _) = Need();
        var a = CountryPalette.Build(map); var b = CountryPalette.Build(map);
        Assert.Equal(a.CodeTone, b.CodeTone);
        foreach (var (code, tone) in CountryPalette.Fixed) Assert.Equal(tone, a.CodeTone[Array.IndexOf(a.Codes, code)]);
        // Core regions of different owners that touch never share a tone or a forbidden pair, except where the rule had no free tone left.
        int clashes = 0, pairs = 0;
        foreach (var (x, y) in map.RegionPairs)
        {
            var rx = map.Rows[x]; var ry = map.Rows[y];
            if (rx.Status != "core" || ry.Status != "core" || rx.Owner == "" || ry.Owner == "" || rx.Owner == ry.Owner) continue;
            pairs++;
            if (a.Regions[x].Tone == a.Regions[y].Tone) clashes++;
        }
        Assert.True(pairs > 100);
        Assert.Equal(0, clashes);
        Assert.True(a.CodeTone.Distinct().Count() >= 10, "tones used: " + a.CodeTone.Distinct().Count());
    }

    [GeoFact]
    public void Status_is_drawn_by_hatch_and_not_by_colour_alone()
    {
        var (map, _) = Need();
        var st = CountryPalette.Build(map);
        int Ix(string id) => Array.IndexOf(map.RegionIds, id);
        var india = st.Regions[Ix("india_maharashtra")];
        Assert.Equal(HatchKind.Colony, india.Hatch);
        Assert.Equal(1, india.Tone);                                      // the tone of the metropole (GBR)
        var sov = st.Regions[Ix("germany_soviet_zone")];
        Assert.Equal(HatchKind.Occupied, sov.Hatch);
        Assert.Equal(2, sov.HatchTone);                                   // the controller's tone (SUN)
        Assert.NotEqual(sov.Tone, sov.HatchTone);
        Assert.Equal(HatchKind.None, st.Regions[Ix("france_marne")].Hatch);
    }

    [Fact]
    public void Palette_pairs_below_the_limit_are_all_in_the_forbidden_table()
    {
        // Spec 2: only the listed pairs fall below delta E 8 at the base tone; the table of forbidden neighbours must cover them.
        var low = new List<(int, int)>();
        for (int i = 0; i < 12; i++) for (int j = i + 1; j < 12; j++) if (CountryPalette.DeltaE(i, j) < 8) low.Add((i + 1, j + 1));
        Assert.All(low, p => Assert.True(CountryPalette.IsForbidden(p.Item1 - 1, p.Item2 - 1), "pair below 8 not forbidden: " + p));
    }

    [Fact]
    public void No_tone_blends_into_the_sea()
    {
        // The first frame showed China in a cyan-grey tone that read as sea; every tone must stay clear of the sea fill.
        var sea = ColorMath.FromHex("BBDDE8")   /* the lit sea as rendered in the 1920x1080 frames */;
        for (int i = 0; i < CountryPalette.Hex.Length; i++)
            if (i != 3)   // FRA blue is fixed by the brief and is told apart by its ink contour
                Assert.True(ColorMath.DeltaE2000(ColorMath.FromHex(CountryPalette.Hex[i]), sea) >= 9, "tone " + (i + 1) + " is too close to the sea");
    }

    [Fact]
    public void Borders_are_drawn_above_the_hatching_and_coast_is_one_dark_stroke()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "src/Singularity.Client.Spike/shaders/globe.gdshader"));
        int hatch = src.IndexOf("col = mix(col, hatch_line_of", StringComparison.Ordinal);
        int line = src.IndexOf("col = mix(col, bc, a * fade)", StringComparison.Ordinal);
        Assert.True(hatch > 0 && line > hatch, "the border and coast lines must be mixed after the hatching");
        Assert.Contains("vec3 coast_color : source_color = vec3(0.13, 0.14, 0.15)", src);   // dark for every fill, L* <= 35
    }

    [Fact]
    public void Label_near_the_screen_edge_moves_inside_instead_of_vanishing()
    {
        // "French Algeria" sits 5 px from the top edge, "Thailand" 6 px from the bottom edge of a 1920x1080 frame.
        Assert.True(LabelPlacement.Place(640, 5, 150, 20, 1920, 1080, out _, out float y1));
        Assert.Equal(LabelPlacement.Margin, y1);
        Assert.True(LabelPlacement.Place(570, 1074, 80, 20, 1920, 1080, out _, out float y2));
        Assert.Equal(1080 - LabelPlacement.Margin - 20, y2);
        Assert.False(LabelPlacement.Place(640, -30, 150, 20, 1920, 1080, out _, out _));   // centre off screen: hidden
        Assert.True(LabelPlacement.Place(960, 540, 150, 20, 1920, 1080, out float x3, out _));
        Assert.Equal(960 - 75, x3);                                                          // inside: untouched
    }

    [GeoFact]
    public void Occupation_zones_are_told_apart_by_controller_number_not_by_tone()
    {
        // TASK-0142 G3: the line of control compares controller numbers; two controllers may share a tone (a joint administration takes its first member's tone).
        var (m, _) = Need();
        var st = CountryPalette.Build(m);
        var seen = new System.Collections.Generic.Dictionary<int, string>();
        int occupied = 0;
        for (int i = 0; i < m.Rows.Length; i++)
        {
            var s = st.Regions[i];
            if (s.Hatch != HatchKind.Occupied) { Assert.Equal(0, s.Controller); continue; }
            occupied++;
            Assert.True(s.Controller >= 1 && s.Controller <= st.Controllers.Length);
            Assert.Equal(st.Controllers[s.Controller - 1], m.Rows[i].Controller);
            if (seen.TryGetValue(s.Controller, out var code)) Assert.Equal(code, m.Rows[i].Controller); else seen[s.Controller] = m.Rows[i].Controller;
        }
        Assert.True(occupied > 0);
        Assert.Equal(st.Controllers.Length, new System.Collections.Generic.HashSet<string>(st.Controllers).Count);   // one number per controller code
    }
}
