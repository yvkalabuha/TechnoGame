using System.Text.RegularExpressions;

namespace Singularity.MapBuilder;

/// <summary>Validator of ADR-0011 §2.10 for the selected area. World-wide count ranges are reported, not enforced, when the area is partial.</summary>
internal static class Validator
{
    private static readonly string[] Statuses = { "core", "colony", "protectorate", "mandate", "leased" };
    private static readonly Regex PolityId = new("^[A-Z][A-Z0-9_]*$", RegexOptions.Compiled);

    public static void Run(BuildResult r, List<Atom> atoms, SortedSet<string> adm0, OwnerFile? owners, BuildOptions o)
    {
        var area = r.Spec.Area;
        var ids = r.Pieces.Select(p => p.Id).ToList();
        var idSet = new HashSet<string>(ids, StringComparer.Ordinal);
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < ids.Count; i++) index[ids[i]] = i;

        // Regions: declared, capital inside, every province's region declared.
        var regionIds = r.Spec.Regions.Select(x => x.Id).ToList();
        if (regionIds.Distinct(StringComparer.Ordinal).Count() != regionIds.Count) r.Errors.Add("regions: duplicate ids");
        var regionSet = regionIds.ToHashSet(StringComparer.Ordinal);
        foreach (var p in r.Pieces)
        {
            var reg = p.Id[..p.Id.IndexOf('.')];
            if (!regionSet.Contains(reg)) r.Errors.Add($"province {p.Id}: region '{reg}' is not declared");
        }
        foreach (var reg in r.Spec.Regions)
        {
            if (!Ids.ValidRegionId(reg.Id)) r.Errors.Add($"region '{reg.Id}': bad format");
            if (!idSet.Contains(reg.Capital) || !reg.Capital.StartsWith(reg.Id + ".", StringComparison.Ordinal))
                r.Errors.Add($"region {reg.Id}: capital '{reg.Capital}' is not one of its provinces");
            if (!ids.Any(i => i.StartsWith(reg.Id + ".", StringComparison.Ordinal))) r.Errors.Add($"region {reg.Id}: no provinces");
        }

        // Area bands and exceptions by rule (a) whole map unit, (b) island, (c) class-A cut piece.
        var adm0Count = r.Pieces.GroupBy(p => p.Adm0).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var degree = new int[ids.Count];
        foreach (var (a, b) in r.Adjacency.Keys) { degree[index[a]]++; degree[index[b]]++; }
        for (int i = 0; i < r.Pieces.Count; i++)
        {
            var p = r.Pieces[i];
            double min = area.MinAreaKm2, max = p.Sparse ? area.SparseMaxKm2 : area.MaxAreaKm2;
            if (p.AreaKm2 < min && !(adm0Count[p.Adm0] == 1 || degree[i] == 0 || p.ClassA))
                r.Errors.Add($"area: {p.Id} {p.AreaKm2} km2 is below {min} and matches no exception (a)(b)(c)");
            if (p.AreaKm2 > max) r.Errors.Add($"area: {p.Id} {p.AreaKm2} km2 exceeds {max}");
            foreach (var edge in new[] { area.MinAreaKm2, area.MaxAreaKm2, area.SparseMaxKm2 })
                if (Math.Abs(p.AreaKm2 - edge) <= edge * 0.01) r.Warnings.Add($"area: {p.Id} {p.AreaKm2} km2 is within 1% of the band edge {edge}");
        }

        r.Info.Add("island provinces (no land neighbour, exception b): " + string.Join(", ", ids.Where((_, i) => degree[i] == 0)));
        // Region graph from province adjacency and straits; every region outside the main component must be isolated.
        var regionIndex = regionIds.OrderBy(x => x, StringComparer.Ordinal).Select((x, i) => (x, i)).ToDictionary(t => t.x, t => t.i, StringComparer.Ordinal);
        var parent = Enumerable.Range(0, regionIndex.Count).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        void Union(string pa, string pb)
        {
            if (!regionIndex.TryGetValue(pa[..pa.IndexOf('.')], out var ra) || !regionIndex.TryGetValue(pb[..pb.IndexOf('.')], out var rb)) return;
            parent[Find(ra)] = Find(rb);
        }
        foreach (var (a, b) in r.Adjacency.Keys) Union(a, b);
        foreach (var (a, b) in r.Straits)
        {
            if (!idSet.Contains(a) || !idSet.Contains(b)) r.Errors.Add($"strait {a} - {b}: unknown province");
            else Union(a, b);
        }
        var sizes = Enumerable.Range(0, parent.Length).GroupBy(Find).ToDictionary(g => g.Key, g => g.Count());
        int main = sizes.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
        foreach (var reg in r.Spec.Regions)
        {
            bool outside = regionIndex.TryGetValue(reg.Id, out var ri) && Find(ri) != main;
            if (outside && !reg.Isolated) r.Errors.Add($"region {reg.Id}: not connected to the main component and not marked isolated");
            if (!outside && reg.Isolated) r.Warnings.Add($"region {reg.Id}: marked isolated but connected");
        }

        // Polities and owners.
        var polityIds = r.Polities.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var p in r.Polities)
        {
            if (!PolityId.IsMatch(p.Id)) r.Errors.Add($"polity '{p.Id}': bad format");
            if (p.NeAdm0 != null && p.NeAdm0 != p.Id) r.Errors.Add($"polity {p.Id}: ne_adm0 '{p.NeAdm0}' must equal the id");
            if (p.NeAdm0 != null && !adm0.Contains(p.NeAdm0)) r.Errors.Add($"polity {p.Id}: '{p.NeAdm0}' is not an ADM0_A3 of the NE file");
            if (p.NeAdm0 == null && adm0.Contains(p.Id)) r.Errors.Add($"polity {p.Id}: project code collides with an ADM0_A3");
            if (p.Kind == "joint")
            {
                if (p.Members.Count < 2) r.Errors.Add($"polity {p.Id}: joint needs >= 2 members");
                foreach (var m in p.Members) if (!polityIds.Contains(m)) r.Errors.Add($"polity {p.Id}: unknown member {m}");
            }
            else if (p.Kind != "state") r.Errors.Add($"polity {p.Id}: kind must be state or joint");
        }
        if (polityIds.Count != r.Polities.Count) r.Errors.Add("polities: duplicate ids");

        if (owners == null) r.Errors.Add("owners.yaml is missing");
        else ValidateOwners(r, owners, idSet, regionSet, polityIds);

        // Cuts: source, as_of, precision, ref; OHM licences.
        foreach (var (file, rule) in r.Spec.Rules.Where(x => x.Rule.Cut != null))
        {
            var c = rule.Cut!;
            if (c.Ohm != null && r.Ohm.TryGetValue(c.Ohm, out var shape))
                foreach (var l in shape.Licenses.Where(l => !Licenses.Allowed(l)))
                    r.Errors.Add($"{file} cut {c.Id}: OHM licence '{l}' is not allowed (CC0, CC BY 2.0/3.0/4.0, PD)");
        }
        foreach (var s in r.Lock.Sources.Where(s => s.Kind == "ohm"))
            if (!Licenses.Allowed(s.License))
                r.Errors.Add($"lock {s.Id}: licence '{s.License}' is not allowed (ADR-0009)");

        // World-wide ranges are informational for a partial area.
        r.Info.Add($"provinces: {r.Pieces.Count} (world target 2000-5000, not enforced for a partial area)");
        r.Info.Add($"regions: {r.Spec.Regions.Count} (world target 150-250, not enforced for a partial area)");
    }

    private static void ValidateOwners(BuildResult r, OwnerFile owners, HashSet<string> idSet, HashSet<string> regionSet, HashSet<string> polityIds)
    {
        if (owners.AsOf != "1945-10-01") r.Errors.Add($"owners: as_of must be 1945-10-01, got '{owners.AsOf}'");
        var def = new Dictionary<string, OwnerRow>(StringComparer.Ordinal);
        var exc = new Dictionary<string, OwnerRow>(StringComparer.Ordinal);
        void Check(OwnerRow row, string what)
        {
            if (string.IsNullOrWhiteSpace(row.Source)) r.Errors.Add($"owners {what}: source is required");
            if (!polityIds.Contains(row.Owner)) r.Errors.Add($"owners {what}: unknown owner '{row.Owner}'");
            if (!polityIds.Contains(row.Controller)) r.Errors.Add($"owners {what}: unknown controller '{row.Controller}'");
            if (!Statuses.Contains(row.Status)) r.Errors.Add($"owners {what}: bad status '{row.Status}'");
            if (row.Status == "leased" && row.Owner == row.Controller) r.Errors.Add($"owners {what}: leased needs controller != owner");
        }
        foreach (var d in owners.Defaults)
        {
            if (d.Region == null || !regionSet.Contains(d.Region)) { r.Errors.Add($"owners default: unknown region '{d.Region}'"); continue; }
            if (!def.TryAdd(d.Region, d)) r.Errors.Add($"owners default: region {d.Region} twice");
            Check(d, "region " + d.Region);
        }
        foreach (var e in owners.Exceptions)
        {
            if (e.Province == null || !idSet.Contains(e.Province)) { r.Errors.Add($"owners exception: unknown province '{e.Province}'"); continue; }
            if (!exc.TryAdd(e.Province, e)) r.Errors.Add($"owners exception: province {e.Province} twice");
            Check(e, "province " + e.Province);
        }
        var controllersByRegion = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var p in r.Pieces)
        {
            var region = p.Id[..p.Id.IndexOf('.')];
            OwnerRow? row = exc.GetValueOrDefault(p.Id) ?? def.GetValueOrDefault(region);
            if (row == null) { r.Errors.Add($"owners: province {p.Id} has no record"); continue; }
            r.Owners[p.Id] = new Resolved { Owner = row.Owner, Controller = row.Controller, Status = row.Status };
            if (!controllersByRegion.TryGetValue(region, out var set)) controllersByRegion[region] = set = new SortedSet<string>(StringComparer.Ordinal);
            set.Add(row.Controller);
        }
        foreach (var (reg, set) in controllersByRegion)
            if (set.Count > 1) r.Warnings.Add($"region {reg} has several controllers: {string.Join(", ", set)} (E1 decision needed, ADR-0011 §3)");

        // Regions of an actor: upper bound 15 (#171); the lower bound 5 needs the whole world.
        foreach (var pol in r.Polities.Where(p => p.Actor != null))
        {
            int n = def.Values.Count(d => d.Owner == pol.Id);
            if (n > 15) r.Errors.Add($"actor {pol.Id}: {n} regions as default owner, max 15");
            else r.Info.Add($"actor {pol.Id}: {n} regions in the area (5-15 over the whole world)");
        }
    }
}
