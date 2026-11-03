using Singularity.Kernel;

namespace Singularity.Core.Data;

public sealed partial class ScenarioData
{
    /// <summary>
    /// Canonical form of the whole scenario table, fields in declaration order, references as IDs.
    /// Changing any start value, rule, source or the actor order changes the scenario_hash.
    /// </summary>
    internal void WriteCanonical(CanonicalWriter w, NodeTable nodes, ProductClassTable classes)
    {
        w.Str("scenario/1");
        w.Str(Id);
        w.Int(StartYear);
        w.Int(StartMonth);
        w.Int(Clusters.Length);
        foreach (Cluster c in Clusters)
            w.Str(DataNames.NameOf(DataNames.Clusters, c));
        w.Int(Disciplines.Length);
        foreach (Discipline d in Disciplines)
            w.Str(DataNames.NameOf(DataNames.Disciplines, d));
        w.Str(ActorId[TechLead]);
        w.Int(MasteredSinceMonth);
        w.Int(ProcXHalfLives);
        w.Fixed(AlloyRefKg);

        w.Int(ProvField.Length);
        for (int p = 0; p < ProvField.Length; p++)
        {
            w.Str(ProvField[p]);
            w.Str(ProvRule[p]);
            w.Bool(ProvEstimated[p]);
            w.Int(ProvSourcesStart[p + 1] - ProvSourcesStart[p]);
            for (int s = ProvSourcesStart[p]; s < ProvSourcesStart[p + 1]; s++)
                w.Str(ProvSources[s]);
        }

        w.Int(ActorCount);
        for (int a = 0; a < ActorCount; a++)
        {
            w.Str(ActorId[a]);
            w.Str(DataNames.NameOf(DataNames.ActorClasses, ActorClass[a]));
            w.Fixed(Gdp[a]);
            w.Int(GdpYear[a]);
            w.Fixed(GdpPerCapita[a]);
            w.Fixed(BudgetIndex[a]);
            w.Fixed(GrowthPa[a]);
            for (int i = 0; i < Clusters.Length * 3; i++)
                w.Fixed(CultureH[a * Clusters.Length * 3 + i]);
            for (int i = 0; i < Clusters.Length * 3; i++)
                w.Fixed(ToolingM[a * Clusters.Length * 3 + i]);
            w.Fixed(Hcap[a]);
            w.Fixed(W[a]);
            w.Int(KnownStart[a + 1] - KnownStart[a]);
            foreach (int n in KnownOf(a))
                w.Str(nodes.Id[n]);
            for (int i = 0; i < Disciplines.Length; i++)
                w.Fixed(KD[a * Disciplines.Length + i]);
            w.Fixed(KInd[a]);
            w.Int(MasteredStart[a + 1] - MasteredStart[a]);
            foreach (int n in MasteredOf(a))
                w.Str(nodes.Id[n]);
            w.Int(FleetStart[a + 1] - FleetStart[a]);
            for (int f = FleetStart[a]; f < FleetStart[a + 1]; f++)
            {
                w.Str(classes.ClassId[FleetClass[f]]);
                w.Int(FleetGen[f]);
                w.Str(DataNames.NameOf(DataNames.Variants, FleetVariant[f]));
                w.Int(FleetCount[f]);
                w.Int(FleetCohortYear[f]);
            }
            w.Int(TrophyEngineers[a]);
            w.Int(EstimatedStart[a + 1] - EstimatedStart[a]);
            for (int e = EstimatedStart[a]; e < EstimatedStart[a + 1]; e++)
                w.Str(Estimated[e]);
        }
    }

    internal void ComputeHash(NodeTable nodes, ProductClassTable classes)
    {
        using var w = new CanonicalWriter();
        WriteCanonical(w, nodes, classes);
        Hash = w.FinishHex();
    }
}
