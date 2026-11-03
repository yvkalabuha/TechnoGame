using Singularity.Core.Data.Yaml;
using Singularity.Kernel;

namespace Singularity.Core.Data;

public sealed partial class ScenarioData
{
    internal static ScenarioData Build(YMap map, NodeTable nodes, ProductClassTable classes)
    {
        var m = new MapReader(map);
        string id = m.Str("id");
        if (!ModManifest.IsLocalId(id))
            throw DataLoadException.At(map, $"scenario ID '{id}' must match [a-z][a-z0-9_]*");

        var start = new MapReader(m.Required("start"));
        int year = start.Int("year"), month = start.Int("month");
        start.End();
        if (month is < 1 or > 12)
            throw DataLoadException.At(start.Node, "start.month must be in 1..12");

        Cluster[] clusters = ReadUniqueEnums(m.Seq("clusters"), DataNames.Clusters, "clusters");
        Discipline[] disciplines = ReadUniqueEnums(m.Seq("disciplines"), DataNames.Disciplines, "disciplines");

        var history = new MapReader(m.Required("mastered_history"));
        int sinceMonth = history.Int("since_month"), halfLives = history.Int("proc_x_half_lives");
        history.End();
        if (sinceMonth > 0 || halfLives < 0)
            throw DataLoadException.At(history.Node, "mastered_history: since_month must be at most 0, proc_x_half_lives at least 0");

        Fx6 alloyRef = m.Fixed("alloy_ref_kg");
        if (alloyRef <= Fx6.Zero)
            throw DataLoadException.At(m.Required("alloy_ref_kg"), "alloy_ref_kg must be positive");

        var prov = ReadProvenance(m.Seq("provenance"));
        var actors = new ActorColumns(clusters, disciplines);
        foreach (YNode actorNode in m.Seq("actors").Items)
            actors.Read(actorNode, nodes, classes);
        if (actors.Ids.Count == 0)
            throw DataLoadException.At(map, "a scenario needs at least one actor");
        foreach ((YNode at, string field) in actors.EstimatedRefs)
        {
            if (!prov.Exists(p => p.Field == field))
                throw DataLoadException.At(at, $"estimated field '{field}' has no provenance entry");
        }

        YNode leadNode = m.Required("tech_lead");
        int techLead = actors.Ids.IndexOf(Scalars.Str(leadNode));
        if (techLead < 0)
            throw DataLoadException.At(leadNode, $"tech_lead '{Scalars.Str(leadNode)}' is not an actor of the scenario");
        m.End();

        var data = new ScenarioData
        {
            Id = id, StartYear = year, StartMonth = month,
            Clusters = [.. clusters], Disciplines = [.. disciplines],
            TechLead = techLead, MasteredSinceMonth = sinceMonth, ProcXHalfLives = halfLives, AlloyRefKg = alloyRef,
            ProvField = [.. prov.Select(p => p.Field)], ProvRule = [.. prov.Select(p => p.Rule)],
            ProvEstimated = [.. prov.Select(p => p.Estimated)],
            ProvSourcesStart = [0, .. Prefix(prov.Select(p => p.Sources.Length))],
            ProvSources = [.. prov.SelectMany(p => p.Sources)],
            ActorId = [.. actors.Ids], ActorClass = [.. actors.Classes],
            Gdp = [.. actors.Gdp], GdpYear = [.. actors.GdpYear], GdpPerCapita = [.. actors.GdpPerCapita],
            BudgetIndex = [.. actors.Budget], GrowthPa = [.. actors.Growth],
            CultureH = [.. actors.CultureH], ToolingM = [.. actors.ToolingM],
            Hcap = [.. actors.Hcap], W = [.. actors.W], KD = [.. actors.KD], KInd = [.. actors.KInd],
            TrophyEngineers = [.. actors.Trophy],
            KnownStart = [.. actors.KnownStart], Known = [.. actors.Known],
            MasteredStart = [.. actors.MasteredStart], Mastered = [.. actors.Mastered],
            FleetStart = [.. actors.FleetStart], FleetClass = [.. actors.FleetClass], FleetGen = [.. actors.FleetGen],
            FleetVariant = [.. actors.FleetVariant], FleetCount = [.. actors.FleetCount],
            FleetCohortYear = [.. actors.FleetCohort],
            EstimatedStart = [.. actors.EstimatedStart], Estimated = [.. actors.Estimated],
        };
        data.ComputeHash(nodes, classes);
        return data;
    }

    private sealed record Provenance(string Field, string Rule, string[] Sources, bool Estimated);

    // Spec §1.1: every actor field has a source or an estimation rule; sorted by field name.
    private static List<Provenance> ReadProvenance(YSeq seq)
    {
        var result = new List<Provenance>();
        foreach (YNode item in seq.Items)
        {
            var p = new MapReader(item);
            string field = p.Str("field");
            if (Array.IndexOf(ActorFields, field) < 0)
                throw DataLoadException.At(item, $"provenance names unknown actor field '{field}'");
            if (result.Exists(r => r.Field == field))
                throw DataLoadException.At(item, $"provenance of '{field}' is given twice");
            string rule = p.Str("rule");
            if (!rule.StartsWith("R-", StringComparison.Ordinal))
                throw DataLoadException.At(item, $"rule '{rule}' must be an R-* rule ID");
            string[] sources = [.. p.Seq("sources").Items.Select(Scalars.Str)];
            if (sources.Distinct(StringComparer.Ordinal).Count() != sources.Length)
                throw DataLoadException.At(item, "a source is listed twice");
            result.Add(new Provenance(field, rule, sources, p.Bool("estimated")));
            p.End();
        }
        foreach (string field in ActorFields)
        {
            if (!result.Exists(r => r.Field == field))
                throw DataLoadException.At(seq, $"actor field '{field}' has no source or rule (spec §1.1)");
        }
        result.Sort((a, b) => string.CompareOrdinal(a.Field, b.Field));
        return result;
    }

    private static T[] ReadUniqueEnums<T>(YSeq seq, (string Name, T Value)[] table, string what) where T : struct, Enum
    {
        var result = new List<T>();
        foreach (YNode item in seq.Items)
        {
            T value = Scalars.Enum(item, table);
            if (result.Contains(value))
                throw DataLoadException.At(item, $"{what} lists '{Scalars.Str(item)}' twice");
            result.Add(value);
        }
        if (result.Count == 0)
            throw DataLoadException.At(seq, $"{what} must not be empty");
        return [.. result];
    }

    private static IEnumerable<int> Prefix(IEnumerable<int> lengths)
    {
        int sum = 0;
        foreach (int n in lengths)
            yield return sum += n;
    }
}
