using System.Text.RegularExpressions;

namespace Singularity.MapBuilder;

/// <summary>Validator of ADR-0011 §2.10 for the selected area. World-wide count ranges are reported, not enforced, when the area is partial.</summary>
internal static class Validator
{
    private static readonly string[] Confidences = { "H", "M", "L" };
    private static readonly string[] Statuses = { "core", "colony", "protectorate", "mandate", "leased", "unclaimed" };
    private static readonly Regex PolityId = new("^[A-Z][A-Z0-9_]*$", RegexOptions.Compiled);

    public static void Run(BuildResult r, List<Atom> atoms, SortedSet<string> adm0, OwnerFile? owners, BuildOptions o)
    {
        var area = r.Spec.Area;
        var ids = r.Pieces.Select(p => p.Id).ToList();
        var idSet = new HashSet<string>(ids, StringComparer.Ordinal);
        // Territories without geometry (ADR-0020 §1.4) are rows of the province table: they join regions but have no polygon.
        var territoryIds = r.Spec.Territories.Select(t => t.Id).ToList();
        foreach (var t in territoryIds)
        {
            if (!Ids.ValidProvinceId(t)) r.Errors.Add($"territory '{t}': bad id format");
            if (idSet.Contains(t)) r.Errors.Add($"territory '{t}': duplicates a province id");
        }
        if (territoryIds.Distinct(StringComparer.Ordinal).Count() != territoryIds.Count) r.Errors.Add("territories: duplicate ids");
        foreach (var t in r.Spec.Territories)
            if (string.IsNullOrWhiteSpace(t.Ref)) r.Errors.Add($"territory {t.Id}: ref is required");
        var allRows = ids.Concat(territoryIds).ToList();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < ids.Count; i++) index[ids[i]] = i;

        // Regions: declared, capital inside, every province's region declared.
        var regionIds = r.Spec.Regions.Select(x => x.Id).ToList();
        if (regionIds.Distinct(StringComparer.Ordinal).Count() != regionIds.Count) r.Errors.Add("regions: duplicate ids");
        var regionSet = regionIds.ToHashSet(StringComparer.Ordinal);
        foreach (var id in allRows)
        {
            var reg = id[..id.IndexOf('.')];
            if (!regionSet.Contains(reg)) r.Errors.Add($"province or territory {id}: region '{reg}' is not declared");
        }
        foreach (var reg in r.Spec.Regions)
        {
            if (!Ids.ValidRegionId(reg.Id)) r.Errors.Add($"region '{reg.Id}': bad format");
            if (!allRows.Contains(reg.Capital) || !reg.Capital.StartsWith(reg.Id + ".", StringComparison.Ordinal))
                r.Errors.Add($"region {reg.Id}: capital '{reg.Capital}' is not one of its provinces or territories");
            if (!allRows.Any(i => i.StartsWith(reg.Id + ".", StringComparison.Ordinal))) r.Errors.Add($"region {reg.Id}: no provinces");
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
        var geometric = ids.Select(i => i[..i.IndexOf('.')]).ToHashSet(StringComparer.Ordinal);
        foreach (var reg in r.Spec.Regions)
        {
            if (!geometric.Contains(reg.Id)) continue;   // territory-only region: no polygon, no land graph
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
        else ValidateOwners(r, owners, idSet, regionSet, polityIds, r.Spec.GreatPowers);

        // Cuts: source, as_of, precision, ref; OHM licences.
        foreach (var (file, rule) in r.Spec.Rules.Where(x => x.Rule.Cut != null))
        {
            var c = rule.Cut!;
            if (c.Ohm != null && r.Ohm.TryGetValue(c.Ohm, out var shape))
                foreach (var l in shape.Licenses.Where(l => !Licenses.Allowed(l)))
                    r.Errors.Add($"{file} cut {c.Id}: OHM licence '{l}' is not allowed (CC0, CC BY 2.0/3.0/4.0, PD)");
        }
        foreach (var s in r.Lock.Sources.Where(s => s.Kind == "ohm"))
        {
            // The lock licence is the '+'-joined set of licences found in the snapshot (relation and member ways).
            foreach (var l in s.License.Split('+').Where(l => !Licenses.Allowed(l)))
                r.Errors.Add($"lock {s.Id}: licence '{l}' is not allowed (ADR-0009)");
            if (r.Ohm.TryGetValue(s.Id, out var snap) && string.Join("+", snap.Licenses) != s.License)
                r.Errors.Add($"lock {s.Id}: licence '{s.License}' differs from the snapshot licences '{string.Join("+", snap.Licenses)}'");
        }

        CheckEvents(r, territoryIds);

        // World ranges (ADR-0011 §1: provinces 2000-5000, regions 150-400, ADR-0022). Outside the range is a warning for the owner, not an error (ADR-0020 §15).
        int provinceRows = r.Pieces.Count + territoryIds.Count;
        r.Info.Add($"provinces: {r.Pieces.Count} with geometry + {territoryIds.Count} territories = {provinceRows} rows (world target 2000-5000)");
        r.Info.Add($"regions: {r.Spec.Regions.Count} (world target 150-400, ADR-0022)");
        if (area.World)
        {
            CheckRegionCount(r, r.Spec.Regions.Count);
            if (provinceRows is < 2000 or > 5000) r.Warnings.Add($"provinces: {provinceRows} rows are outside the ADR-0011 range 2000-5000");
        }
    }

    /// <summary>ADR-0022: world region count 150-400; 400 is a provisional warning threshold, not a build error.</summary>
    internal static void CheckRegionCount(BuildResult r, int n)
    {
        if (n > 400) r.Warnings.Add($"regions: {n} exceed the ADR-0022 provisional limit of 400 (report to the owner)");
        if (n < 150) r.Warnings.Add($"regions: {n} are below the ADR-0022 minimum of 150");
    }

    /// <summary>
    /// Event-line CI (ADR-0020 §2.9, §3.13, #350): every region lies wholly inside one set of an event or wholly outside all its sets,
    /// and two sets of one event never share a region.
    /// </summary>
    internal static void CheckEvents(BuildResult r, List<string> territoryIds)
    {
        bool Pat(string pat, string id) => pat.EndsWith('*') ? id.StartsWith(pat[..^1], StringComparison.Ordinal) : id == pat;
        bool Matches(EventSet s, string id, Piece? p)
        {
            if (s.ExceptIds.Any(x => Pat(x, id))) return false;
            if (p != null && (s.ExceptCodes.Any(c => p.Src.Contains(c)) || s.ExceptCuts.Any(c => p.Sides.Contains(c)))) return false;
            if (p != null && (s.Adm0.Contains(p.Adm0) || s.Codes.Any(c => p.Src.Contains(c)) || s.Cuts.Any(c => p.Sides.Contains(c)))) return true;
            return s.Ids.Any(x => Pat(x, id));
        }
        var rows = r.Pieces.Select(p => (Id: p.Id, P: (Piece?)p)).Concat(territoryIds.Select(t => (Id: t, P: (Piece?)null))).ToList();
        int drawn = 0;
        foreach (var ev in r.Spec.Events.OrderBy(e => e.Id, StringComparer.Ordinal))
        {
            if (ev.NotDrawn != null) { r.Warnings.Add($"pending_map: event line not drawn: {ev.Id} ({ev.Rr}, {ev.Date}): {ev.NotDrawn}"); continue; }
            if (ev.Sets.Count == 0 || ev.Rr == "" || ev.Date == "") { r.Errors.Add($"event {ev.Id}: sets, rr and date are required"); continue; }
            var setOf = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int si = 0; si < ev.Sets.Count; si++)
            {
                int n = 0;
                foreach (var (id, p) in rows)
                    if (Matches(ev.Sets[si], id, p))
                    {
                        if (setOf.TryGetValue(id, out var other) && other != si) r.Errors.Add($"event {ev.Id}: {id} is in two sets");
                        setOf[id] = si; n++;
                    }
                if (n == 0) r.Errors.Add($"event {ev.Id}: set '{ev.Sets[si].Name}' matches nothing");
            }
            foreach (var g in rows.GroupBy(x => x.Id[..x.Id.IndexOf('.')]).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var cls = g.Select(x => setOf.GetValueOrDefault(x.Id, -1)).Distinct().OrderBy(x => x).ToList();
                if (cls.Count > 1)
                    r.Errors.Add($"event line {ev.Id} ({ev.Rr}, {ev.Date}): region {g.Key} crosses the line: " +
                        string.Join("; ", cls.Select(c => (c < 0 ? "outside" : ev.Sets[c].Name) + " = " + string.Join(",", g.Where(x => setOf.GetValueOrDefault(x.Id, -1) == c).Select(x => x.Id).Take(4)))));
            }
            drawn++;
        }
        r.Info.Add($"event lines checked against region borders: {drawn}");
    }

    internal static void ValidateOwners(BuildResult r, OwnerFile owners, HashSet<string> idSet, HashSet<string> regionSet, HashSet<string> polityIds, GreatPowerFile? greatPowers = null)
    {
        if (owners.AsOf != "1945-10-01") r.Errors.Add($"owners: as_of must be 1945-10-01, got '{owners.AsOf}'");
        var def = new Dictionary<string, OwnerRow>(StringComparer.Ordinal);
        var exc = new Dictionary<string, OwnerRow>(StringComparer.Ordinal);
        void Check(OwnerRow row, string what)
        {
            if (string.IsNullOrWhiteSpace(row.Source)) r.Errors.Add($"owners {what}: source is required");
            if (!Confidences.Contains(row.Confidence)) r.Errors.Add($"owners {what}: confidence must be H, M or L, got '{row.Confidence}'");
            if (row.Status == "unclaimed")
            {
                if (row.Owner != null) r.Errors.Add($"owners {what}: unclaimed needs owner null, got '{row.Owner}'");
            }
            else if (row.Owner == null) r.Errors.Add($"owners {what}: owner is null, only status unclaimed allows that");
            else if (!polityIds.Contains(row.Owner)) r.Errors.Add($"owners {what}: unknown owner '{row.Owner}'");
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
            if (row == null)
            {
                r.Errors.Add($"owners: province {p.Id} has no record");
                continue;
            }
            r.Owners[p.Id] = new Resolved { Owner = row.Owner ?? "", Controller = row.Controller, Status = row.Status };
            if (!controllersByRegion.TryGetValue(region, out var set)) controllersByRegion[region] = set = new SortedSet<string>(StringComparer.Ordinal);
            set.Add(row.Controller);
        }
        // Every region has a default record, also the regions made of territories only (T4).
        foreach (var reg in regionSet.OrderBy(x => x, StringComparer.Ordinal))
            if (!def.ContainsKey(reg)) r.Errors.Add($"owners: region {reg} has no default record");
        foreach (var (reg, set) in controllersByRegion)
            if (set.Count > 1) r.Warnings.Add($"region {reg} has several controllers: {string.Join(", ", set)} (E1 decision needed, ADR-0011 §3)");

        // One status per region (ADR-0022 §2.8): the quota counts regions, so all provinces of a region must agree.
        var statusOf = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var reg in regionSet.OrderBy(x => x, StringComparer.Ordinal))
        {
            var provs = r.Pieces.Where(p => p.Id[..p.Id.IndexOf('.')] == reg && r.Owners.ContainsKey(p.Id)).Select(p => (p.Id, r.Owners[p.Id].Status)).ToList();
            var statuses = provs.Select(x => x.Status).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (statuses.Count > 1)
                r.Errors.Add($"owners: region {reg} has several statuses: " + string.Join("; ", statuses.Select(st => st + " = " + string.Join(",", provs.Where(x => x.Status == st).Select(x => x.Id)))));
            if (statuses.Count > 0) statusOf[reg] = statuses[0];
            else if (def.TryGetValue(reg, out var d0)) statusOf[reg] = d0.Status;
        }

        // Great-power quota (ADR-0022 §2, #171): only the listed actors, only regions with status core, t0 validator only.
        if (greatPowers == null) return;
        var exceptions = new Dictionary<string, GreatPowerException>(StringComparer.Ordinal);
        foreach (var e in greatPowers.Exceptions)
        {
            if (string.IsNullOrWhiteSpace(e.Reason)) r.Errors.Add($"great_powers: exception {e.Id} needs a reason");
            exceptions[e.Id] = e;
        }
        foreach (var id in greatPowers.Ids)
        {
            if (!polityIds.Contains(id)) { r.Errors.Add($"great_powers: unknown id '{id}'"); continue; }
            var regs = def.Values.Where(d => d.Owner == id && d.Region != null).Select(d => d.Region!).ToList();
            int Count(string st) => regs.Count(x => statusOf.GetValueOrDefault(x) == st);
            int core = Count("core");
            r.Info.Add($"actor {id}: core {core}, colony {Count("colony")}, protectorate {Count("protectorate")}, mandate {Count("mandate")}, leased {Count("leased")}");
            if (!r.Spec.Area.World) continue;
            int max = exceptions.TryGetValue(id, out var ex) ? ex.MaxCore : 15;
            if (core > max) r.Warnings.Add($"actor {id}: {core} core regions exceed the #171 maximum of {max}" + (max != 15 ? " (named exception)" : "") + " (question to the owner)");
            if (core < 5) r.Warnings.Add($"actor {id}: {core} core regions are below the #171 minimum of 5");
        }
    }
}
