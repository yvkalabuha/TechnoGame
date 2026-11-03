using System.Collections.Immutable;
using Singularity.Kernel;

namespace Singularity.Core.Data;

/// <summary>
/// One start scenario (slice spec §1.1, LAYER_CONTRACT §9). Actors form an SoA table in data
/// order: the order is the actor index of RNG keys (spec §3.5 A7), so it is never re-sorted.
/// Per-cluster and per-discipline values are flattened: <c>CultureH[(a·C + c)·3 + d]</c>,
/// <c>ToolingM[a·C + c]</c>, <c>KD[a·D + k]</c>, where c indexes <see cref="Clusters"/>,
/// k indexes <see cref="Disciplines"/> and d is <see cref="Dimension"/>.
/// <see cref="Hash"/> is the scenario_hash of spec §1.1: SHA-256 of the canonical form below.
/// </summary>
public sealed partial class ScenarioData
{
    /// <summary>Actor fields that need a source or an estimation rule (spec §1.1).</summary>
    public static readonly string[] ActorFields =
    [
        "gdp_1945", "gdppc_1945", "budget_index", "growth_pa", "culture_h", "tooling_m", "hcap", "w",
        "known", "k_d", "k_ind", "mastered", "fleet", "trophy_engineers",
    ];

    public required string Id { get; init; }
    public required int StartYear { get; init; }
    public required int StartMonth { get; init; }
    public required ImmutableArray<Cluster> Clusters { get; init; }
    public required ImmutableArray<Discipline> Disciplines { get; init; }
    public required int TechLead { get; init; }
    public required int MasteredSinceMonth { get; init; }
    public required int ProcXHalfLives { get; init; }

    // Provenance rows, sorted by field name.
    public required ImmutableArray<string> ProvField { get; init; }
    public required ImmutableArray<string> ProvRule { get; init; }
    public required ImmutableArray<bool> ProvEstimated { get; init; }
    public required ImmutableArray<int> ProvSourcesStart { get; init; }
    public required ImmutableArray<string> ProvSources { get; init; }

    public int ActorCount => ActorId.Length;
    public required ImmutableArray<string> ActorId { get; init; }
    public required ImmutableArray<ActorClass> ActorClass { get; init; }
    public required ImmutableArray<Fx6> Gdp { get; init; }
    public required ImmutableArray<int> GdpYear { get; init; }
    public required ImmutableArray<Fx6> GdpPerCapita { get; init; }
    public required ImmutableArray<Fx6> BudgetIndex { get; init; }
    public required ImmutableArray<Fx6> GrowthPa { get; init; }
    public required ImmutableArray<Fx6> CultureH { get; init; }
    public required ImmutableArray<Fx6> ToolingM { get; init; }
    public required ImmutableArray<Fx6> Hcap { get; init; }
    public required ImmutableArray<Fx6> W { get; init; }
    public required ImmutableArray<Fx6> KD { get; init; }
    public required ImmutableArray<Fx6> KInd { get; init; }
    public required ImmutableArray<int> TrophyEngineers { get; init; }

    /// <summary>Known node rows of actor a: <c>Known[KnownStart[a]..KnownStart[a+1]]</c>, ascending.</summary>
    public required ImmutableArray<int> KnownStart { get; init; }
    public required ImmutableArray<int> Known { get; init; }
    public required ImmutableArray<int> MasteredStart { get; init; }
    public required ImmutableArray<int> Mastered { get; init; }

    /// <summary>Start fleets of actor a: rows <c>FleetStart[a]..FleetStart[a+1]</c>, data order.</summary>
    public required ImmutableArray<int> FleetStart { get; init; }
    public required ImmutableArray<int> FleetClass { get; init; }
    public required ImmutableArray<int> FleetGen { get; init; }
    public required ImmutableArray<DesignVariant> FleetVariant { get; init; }
    public required ImmutableArray<int> FleetCount { get; init; }
    public required ImmutableArray<int> FleetCohortYear { get; init; }

    /// <summary>Per-actor estimated fields (sorted): <c>Estimated[EstimatedStart[a]..EstimatedStart[a+1]]</c>.</summary>
    public required ImmutableArray<int> EstimatedStart { get; init; }
    public required ImmutableArray<string> Estimated { get; init; }

    /// <summary>scenario_hash (spec §1.1), lower-case hex SHA-256.</summary>
    public string Hash { get; private set; } = "";

    public int ActorIndex(string actorId) => ActorId.IndexOf(actorId);

    public ReadOnlySpan<int> KnownOf(int actor) => Known.AsSpan()[KnownStart[actor]..KnownStart[actor + 1]];

    public ReadOnlySpan<int> MasteredOf(int actor) => Mastered.AsSpan()[MasteredStart[actor]..MasteredStart[actor + 1]];

    public Fx6 CultureOf(int actor, Cluster cluster, Dimension dim) =>
        CultureH[(actor * Clusters.Length + ClusterIndex(cluster)) * 3 + (int)dim];

    public Fx6 ToolingOf(int actor, Cluster cluster) => ToolingM[actor * Clusters.Length + ClusterIndex(cluster)];

    public Fx6 KDOf(int actor, Discipline discipline)
    {
        int k = Disciplines.IndexOf(discipline);
        if (k < 0)
            throw new ArgumentOutOfRangeException(nameof(discipline), discipline, "Discipline is not in the scenario.");
        return KD[actor * Disciplines.Length + k];
    }

    private int ClusterIndex(Cluster cluster)
    {
        int c = Clusters.IndexOf(cluster);
        return c >= 0 ? c : throw new ArgumentOutOfRangeException(nameof(cluster), cluster, "Cluster is not in the scenario.");
    }
}
