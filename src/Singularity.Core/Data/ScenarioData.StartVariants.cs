using System.Collections.Immutable;
using Singularity.Kernel;

namespace Singularity.Core.Data;

public sealed partial class ScenarioData
{
    /// <summary>The scenario actors that <c>START_SYM</c> makes equal (spec §6.5).</summary>
    public static readonly string[] SymmetricActors = ["USA", "GBR", "SUN"];

    // Estimated fields that get no multiplier: node sets never change, tooling_m and hcap are derived from culture_h by a rule (spec §6.5).
    private static readonly string[] NoMultiplier = ["known", "mastered", "tooling_m", "hcap"];

    /// <summary>
    /// <c>START_PERT</c> (spec §6.5, v0.12): every field with <c>estimated: true</c> (scenario-wide in the provenance, or listed by the actor) is
    /// multiplied by its own multiplier from <paramref name="multiplier"/>(fieldIndex), the 0-based number of the (actor, field)
    /// pair among such pairs in canonical order (actors in data order, fields in the order of <see cref="ActorFields"/>). Fixed-point fields are floored products,
    /// integer fields <c>⌊value·m⌋</c>. <c>tooling_m</c> and <c>hcap</c> keep their offset to the perturbed <c>culture_h</c>
    /// (the rule, not a multiplier). Known nodes, mastered history and class do not change.
    /// </summary>
    public ScenarioData Perturbed(Func<int, Fx6> multiplier, NodeTable nodes, ProductClassTable classes)
    {
        StartFields f = StartFields.Of(this);
        int index = 0;
        for (int a = 0; a < ActorCount; a++)
        {
            // Canonical order of the scenario: ActorFields (spec §6.5); an estimated field Scale does not know throws.
            foreach (string field in ActorFields)
            {
                if (IsEstimated(a, field) && Array.IndexOf(NoMultiplier, field) < 0)
                    Scale(f, a, field, multiplier(index++));
            }
        }
        for (int a = 0; a < ActorCount; a++)
            DeriveByRule(f, a);
        return Rebuild(f, nodes, classes);
    }

    /// <summary>
    /// <c>START_SYM</c> (spec §6.5): USA, GBR and SUN get one start. Fixed-point fields are the mean of the three (floor of the sum
    /// over 3), integer fields <c>⌊Σ/3⌋</c>; known nodes, mastered history and fleets are USA's; <c>tooling_m</c> and <c>hcap</c> keep
    /// USA's offsets to the mean <c>culture_h</c> (the rule of the original, as in <c>START_PERT</c>).
    /// </summary>
    public ScenarioData Symmetrized(NodeTable nodes, ProductClassTable classes)
    {
        int[] rows = [.. SymmetricActors.Select(ActorIndex)];
        if (rows.Any(r => r < 0))
            throw new InvalidOperationException("START_SYM needs the actors USA, GBR and SUN in the scenario.");
        StartFields f = StartFields.Of(this);
        int usa = rows[0];
        int c3 = Clusters.Length * 3, d = Disciplines.Length;
        var offsetM = new Fx6[c3];
        for (int i = 0; i < c3; i++)
            offsetM[i] = f.ToolingM[usa * c3 + i] - f.CultureH[usa * c3 + i];
        Fx6 offsetHcap = f.Hcap[usa] - MaxOf(f.CultureH, usa, c3);

        Fx6 Mean(Fx6[] v, int per, int i) => Fx6.FromRaw(rows.Sum(r => v[r * per + i].Raw) / 3);
        var gdp = Mean(f.Gdp, 1, 0);
        var gdppc = Mean(f.GdpPerCapita, 1, 0);
        var budget = Mean(f.Budget, 1, 0);
        var growth = Mean(f.Growth, 1, 0);
        var w = Mean(f.W, 1, 0);
        var kind = Mean(f.KInd, 1, 0);
        int gdpYear = (int)(rows.Sum(r => (long)f.GdpYear[r]) / 3);
        int trophy = (int)(rows.Sum(r => (long)f.Trophy[r]) / 3);
        var cultureMean = new Fx6[c3];
        for (int i = 0; i < c3; i++)
            cultureMean[i] = Mean(f.CultureH, c3, i);
        var kdMean = new Fx6[d];
        for (int i = 0; i < d; i++)
            kdMean[i] = Mean(f.KD, d, i);

        Fx6 hcapMean = MaxOf(cultureMean, 0, c3) + offsetHcap;
        foreach (int r in rows)
        {
            f.Gdp[r] = gdp;
            f.GdpPerCapita[r] = gdppc;
            f.Budget[r] = budget;
            f.Growth[r] = growth;
            f.W[r] = w;
            f.KInd[r] = kind;
            f.GdpYear[r] = gdpYear;
            f.Trophy[r] = trophy;
            f.Hcap[r] = hcapMean;
            for (int i = 0; i < c3; i++)
            {
                f.CultureH[r * c3 + i] = cultureMean[i];
                f.ToolingM[r * c3 + i] = cultureMean[i] + offsetM[i];
            }
            for (int i = 0; i < d; i++)
                f.KD[r * d + i] = kdMean[i];
        }
        return RebuildSymmetric(f, rows, nodes, classes);
    }

    private bool IsEstimated(int actor, string field)
    {
        int p = ProvField.IndexOf(field);
        if (p >= 0 && ProvEstimated[p])
            return true;
        for (int e = EstimatedStart[actor]; e < EstimatedStart[actor + 1]; e++)
        {
            if (Estimated[e] == field)
                return true;
        }
        return false;
    }

    private void Scale(StartFields f, int a, string field, Fx6 m)
    {
        int c3 = Clusters.Length * 3, d = Disciplines.Length;
        switch (field)
        {
            case "budget_index": f.Budget[a] *= m; break;
            case "culture_h":
                for (int i = 0; i < c3; i++)
                    f.CultureH[a * c3 + i] *= m;
                break;
            case "fleet":
                for (int x = FleetStart[a]; x < FleetStart[a + 1]; x++)
                    f.FleetCount[x] = (int)((long)f.FleetCount[x] * m.Raw / Fx6.Scale);
                break;
            case "gdp_1945": f.Gdp[a] *= m; break;
            case "gdppc_1945": f.GdpPerCapita[a] *= m; break;
            case "growth_pa": f.Growth[a] *= m; break;
            case "k_d":
                for (int i = 0; i < d; i++)
                    f.KD[a * d + i] *= m;
                break;
            case "k_ind": f.KInd[a] *= m; break;
            case "w": f.W[a] *= m; break;
            default: throw new ArgumentOutOfRangeException(nameof(field), field, null);
        }
    }

    // tooling_m = culture_h + offset, hcap = max culture_h + offset (R-M, R-HC): the original offsets applied to the new culture.
    private void DeriveByRule(StartFields f, int a)
    {
        int c3 = Clusters.Length * 3;
        for (int i = 0; i < c3; i++)
            f.ToolingM[a * c3 + i] = f.CultureH[a * c3 + i] + (ToolingM[a * c3 + i] - CultureH[a * c3 + i]);
        f.Hcap[a] = MaxOf(f.CultureH, a, c3) + (Hcap[a] - MaxOf(CultureH, a, c3));
    }

    private static Fx6 MaxOf(IReadOnlyList<Fx6> v, int actor, int c3)
    {
        Fx6 max = v[actor * c3];
        for (int i = 1; i < c3; i++)
        {
            if (v[actor * c3 + i] > max)
                max = v[actor * c3 + i];
        }
        return max;
    }

    private ScenarioData Rebuild(StartFields f, NodeTable nodes, ProductClassTable classes) => RebuildWith(f, Known, KnownStart, Mastered, MasteredStart,
        FleetStart, FleetClass, FleetGen, FleetVariant, [.. f.FleetCount], FleetCohortYear, nodes, classes);

    // START_SYM: the three actors share USA's known nodes, mastered history and fleet rows.
    private ScenarioData RebuildSymmetric(StartFields f, int[] rows, NodeTable nodes, ProductClassTable classes)
    {
        int usa = rows[0];
        var known = new List<int>();
        var knownStart = new List<int> { 0 };
        var mastered = new List<int>();
        var masteredStart = new List<int> { 0 };
        var fleetStart = new List<int> { 0 };
        var fleetClass = new List<int>();
        var fleetGen = new List<int>();
        var fleetVariant = new List<DesignVariant>();
        var fleetCount = new List<int>();
        var fleetCohort = new List<int>();
        for (int a = 0; a < ActorCount; a++)
        {
            int src = Array.IndexOf(rows, a) >= 0 ? usa : a;
            known.AddRange(KnownOf(src).ToArray());
            knownStart.Add(known.Count);
            mastered.AddRange(MasteredOf(src).ToArray());
            masteredStart.Add(mastered.Count);
            for (int x = FleetStart[src]; x < FleetStart[src + 1]; x++)
            {
                fleetClass.Add(FleetClass[x]);
                fleetGen.Add(FleetGen[x]);
                fleetVariant.Add(FleetVariant[x]);
                fleetCount.Add(FleetCount[x]);
                fleetCohort.Add(FleetCohortYear[x]);
            }
            fleetStart.Add(fleetClass.Count);
        }
        return RebuildWith(f, [.. known], [.. knownStart], [.. mastered], [.. masteredStart], [.. fleetStart], [.. fleetClass], [.. fleetGen],
            [.. fleetVariant], [.. fleetCount], [.. fleetCohort], nodes, classes);
    }

    private ScenarioData RebuildWith(StartFields f, ImmutableArray<int> known, ImmutableArray<int> knownStart, ImmutableArray<int> mastered,
        ImmutableArray<int> masteredStart, ImmutableArray<int> fleetStart, ImmutableArray<int> fleetClass, ImmutableArray<int> fleetGen,
        ImmutableArray<DesignVariant> fleetVariant, ImmutableArray<int> fleetCount, ImmutableArray<int> fleetCohort, NodeTable nodes, ProductClassTable classes)
    {
        var data = new ScenarioData
        {
            Id = Id, StartYear = StartYear, StartMonth = StartMonth, Clusters = Clusters, Disciplines = Disciplines,
            TechLead = TechLead, MasteredSinceMonth = MasteredSinceMonth, ProcXHalfLives = ProcXHalfLives, AlloyRefKg = AlloyRefKg,
            MaxDesigns = MaxDesigns,
            ProvField = ProvField, ProvRule = ProvRule, ProvEstimated = ProvEstimated, ProvSourcesStart = ProvSourcesStart, ProvSources = ProvSources,
            ActorId = ActorId, ActorClass = ActorClass,
            Gdp = [.. f.Gdp], GdpYear = [.. f.GdpYear], GdpPerCapita = [.. f.GdpPerCapita], BudgetIndex = [.. f.Budget], GrowthPa = [.. f.Growth],
            CultureH = [.. f.CultureH], ToolingM = [.. f.ToolingM], Hcap = [.. f.Hcap], W = [.. f.W], KD = [.. f.KD], KInd = [.. f.KInd],
            TrophyEngineers = [.. f.Trophy], Exposure = Exposure,
            KnownStart = knownStart, Known = known, MasteredStart = masteredStart, Mastered = mastered,
            FleetStart = fleetStart, FleetClass = fleetClass, FleetGen = fleetGen, FleetVariant = fleetVariant, FleetCount = fleetCount,
            FleetCohortYear = fleetCohort,
            EstimatedStart = EstimatedStart, Estimated = Estimated,
        };
        data.ComputeHash(nodes, classes);
        return data;
    }

    /// <summary>Mutable copy of the numeric start fields while a variant is built.</summary>
    private sealed class StartFields
    {
        public required Fx6[] Gdp, GdpPerCapita, Budget, Growth, CultureH, ToolingM, Hcap, W, KD, KInd;
        public required int[] GdpYear, Trophy, FleetCount;

        public static StartFields Of(ScenarioData s) => new()
        {
            Gdp = [.. s.Gdp], GdpPerCapita = [.. s.GdpPerCapita], Budget = [.. s.BudgetIndex], Growth = [.. s.GrowthPa],
            CultureH = [.. s.CultureH], ToolingM = [.. s.ToolingM], Hcap = [.. s.Hcap], W = [.. s.W], KD = [.. s.KD], KInd = [.. s.KInd],
            GdpYear = [.. s.GdpYear], Trophy = [.. s.TrophyEngineers], FleetCount = [.. s.FleetCount],
        };
    }
}
