using Singularity.MapBuilder;

// Usage: MapBuilder <fetch [--pin] | find <text> | bootstrap | build> [--root tools/map] [--cache dir] [--out dir] [--render file.png]
return await Cli.RunAsync(args);

internal static class Cli
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0) { Console.Error.WriteLine("commands: fetch [--pin] | find <text> | bootstrap | build"); return 2; }
        string Opt(string name, string def)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : def;
        }
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        var root = Path.GetFullPath(Opt("--root", FindRoot()));
        var repo = Path.GetFullPath(Path.Combine(root, "..", ".."));
        var o = new BuildOptions
        {
            Root = root,
            Cache = Path.GetFullPath(Opt("--cache", Environment.GetEnvironmentVariable("SINGULARITY_MAP_CACHE") ?? Path.Combine(root, ".cache"))),
            OutDir = Path.GetFullPath(Opt("--out", Path.Combine(repo, "mods", "core", "map"))),
            ScenarioDir = Path.GetFullPath(Opt("--scenario", Path.Combine(repo, "mods", "core", "scenarios", "1945"))),
        };
        try
        {
            switch (args[0])
            {
                case "fetch":
                    await Sources.FetchAsync(Sources.Load(Path.Combine(root, "sources.lock.yaml")), o.Cache, args.Contains("--pin"));
                    return 0;
                case "find":
                    return Find(o, args[1]);
                case "verify":
                    Writer.VerifyBinding(o.OutDir);
                    Console.WriteLine("binding OK");
                    return 0;
                case "overlap":
                    return Overlap(o, args[1], args.Length > 2 ? args[2] : "");
                case "at":
                    return At(o, args.Skip(1).TakeWhile(a => !a.StartsWith("--")).Select(a => a.Split(',').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray()).ToList());
                case "dump":
                    return Dump(o);
                case "bootstrap":
                    return DoBootstrap(o);
                case "build":
                    return DoBuild(o, Opt("--render", ""));
                default:
                    Console.Error.WriteLine("unknown command " + args[0]);
                    return 2;
            }
        }
        catch (Exception e) when (e is InvalidDataException or FileNotFoundException)
        {
            Console.Error.WriteLine("ERROR: " + e.Message);
            return 1;
        }
    }

    private static string FindRoot()
    {
        for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d != null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "sources.lock.yaml"))) return d.FullName;
            var t = Path.Combine(d.FullName, "tools", "map");
            if (File.Exists(Path.Combine(t, "sources.lock.yaml"))) return t;
        }
        return Path.Combine(Directory.GetCurrentDirectory(), "tools", "map");
    }

    private static int Find(BuildOptions o, string text)
    {
        var lk = Sources.Load(Path.Combine(o.Root, "sources.lock.yaml"));
        var spec = SpecSet.Load(Path.Combine(o.Root, "spec"));
        foreach (var a in Data.LoadNe(Sources.ReadVerified(lk, o.Cache, Builder.NeAdmin1), spec.Area))
            if (a.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || a.Code.Contains(text, StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"{a.Code}\t{a.Adm0}\t{a.Name}\t{Math.Round(a.AreaM2 / 1e6)} km2");
        return 0;
    }

    /// <summary>Development helper: share of each built piece (optionally filtered by id prefix) inside an OHM polygon.</summary>
    private static int Overlap(BuildOptions o, string ohmId, string prefix)
    {
        var lk = Sources.Load(Path.Combine(o.Root, "sources.lock.yaml"));
        var spec = SpecSet.Load(Path.Combine(o.Root, "spec"));
        var shapes = new Dictionary<string, OhmShape>(StringComparer.Ordinal);
        OhmShape Ohm(string id) { if (!shapes.TryGetValue(id, out var s)) shapes[id] = s = Data.LoadOhm(Sources.ReadVerified(lk, o.Cache, id), id); return s; }
        var atoms = Data.LoadNe(Sources.ReadVerified(lk, o.Cache, Builder.NeAdmin1), spec.Area);
        var pieces = Pipeline.Apply(Pipeline.Initial(atoms), spec, Ohm);
        Ids.Assign(pieces, spec.Ids, new List<Piece>());
        var area = Ohm(ohmId).Area;
        foreach (var p in pieces.Where(p => p.Id.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            double inside = Sph.AreaM2(NetTopologySuite.Operation.OverlayNG.OverlayNG.Overlay(p.Shape, area, NetTopologySuite.Operation.Overlay.SpatialFunction.Intersection, Sph.Pm));
            double share = inside / Sph.AreaM2(p.Shape);
            if (share > 0.001) Console.WriteLine($"{p.Id}\t{p.Adm0}\t{share:P1}\t{Sph.AreaM2(p.Shape) / 1e6:F0} km2\t{string.Join(',', p.Src.Take(2))}");
        }
        return 0;
    }

    /// <summary>Development helper: id of the province covering a point (lat lon).</summary>
    private static int At(BuildOptions o, List<double[]> points)
    {
        var lk = Sources.Load(Path.Combine(o.Root, "sources.lock.yaml"));
        var spec = SpecSet.Load(Path.Combine(o.Root, "spec"));
        var shapes = new Dictionary<string, OhmShape>(StringComparer.Ordinal);
        OhmShape Ohm(string id) { if (!shapes.TryGetValue(id, out var s)) shapes[id] = s = Data.LoadOhm(Sources.ReadVerified(lk, o.Cache, id), id); return s; }
        var atoms = Data.LoadNe(Sources.ReadVerified(lk, o.Cache, Builder.NeAdmin1), spec.Area);
        var pieces = Pipeline.Apply(Pipeline.Initial(atoms), spec, Ohm);
        Ids.Assign(pieces, spec.Ids, new List<Piece>());
        foreach (var ll in points)
        {
            var pt = new NetTopologySuite.Geometries.Coordinate(ll[1], ll[0]);
            var hit = pieces.Where(p => NetTopologySuite.Algorithm.Locate.SimplePointInAreaLocator.Locate(pt, p.Shape) != NetTopologySuite.Geometries.Location.Exterior).Select(p => p.Id);
            Console.WriteLine($"{ll[0]},{ll[1]}\t{string.Join(" ", hit)}");
        }
        return 0;
    }

    /// <summary>Development helper: id, ADM0_A3, area, NE sources and cuts of every province (input for hand-written tables).</summary>
    private static int Dump(BuildOptions o)
    {
        var lk = Sources.Load(Path.Combine(o.Root, "sources.lock.yaml"));
        var spec = SpecSet.Load(Path.Combine(o.Root, "spec"));
        var shapes = new Dictionary<string, OhmShape>(StringComparer.Ordinal);
        OhmShape Ohm(string id) { if (!shapes.TryGetValue(id, out var s)) shapes[id] = s = Data.LoadOhm(Sources.ReadVerified(lk, o.Cache, id), id); return s; }
        var atoms = Data.LoadNe(Sources.ReadVerified(lk, o.Cache, Builder.NeAdmin1), spec.Area);
        var pieces = Pipeline.Apply(Pipeline.Initial(atoms), spec, Ohm);
        Ids.Assign(pieces, spec.Ids, new List<Piece>());
        foreach (var p in pieces.OrderBy(p => p.Id, StringComparer.Ordinal))
            Console.WriteLine($"{p.Id}\t{p.Adm0}\t{Sph.AreaKm2(p.Shape)}\t{string.Join(',', p.Src)}\t{string.Join(',', p.Cuts)}\t{p.Hint}");
        return 0;
    }

    private static int DoBootstrap(BuildOptions o)
    {
        var lk = Sources.Load(Path.Combine(o.Root, "sources.lock.yaml"));
        var spec = SpecSet.Load(Path.Combine(o.Root, "spec"));
        var cache = new Dictionary<string, OhmShape>(StringComparer.Ordinal);
        OhmShape Ohm(string id) { if (!cache.TryGetValue(id, out var s)) cache[id] = s = Data.LoadOhm(Sources.ReadVerified(lk, o.Cache, id), id); return s; }
        var atoms = Data.LoadNe(Sources.ReadVerified(lk, o.Cache, Builder.NeAdmin1), spec.Area);
        Bootstrap.Run(Path.Combine(o.Root, "spec"), atoms, Ohm);
        return 0;
    }

    private static int DoBuild(BuildOptions o, string render)
    {
        var r = Builder.Build(o);
        foreach (var s in r.Info) Console.WriteLine("info: " + s);
        foreach (var s in r.Warnings) Console.WriteLine("warning: " + s);
        foreach (var s in r.Errors) Console.WriteLine("ERROR: " + s);
        Console.WriteLine($"build: {r.Pieces.Count} provinces, {r.Spec.Regions.Count} regions, {r.Adjacency.Count} adjacency pairs, {r.Errors.Count} errors, {r.Warnings.Count} warnings");
        if (r.Errors.Count > 0) return 1;
        Writer.Write(r, o.OutDir);
        if (render != "") Render.Png(r, render);
        return 0;
    }
}
