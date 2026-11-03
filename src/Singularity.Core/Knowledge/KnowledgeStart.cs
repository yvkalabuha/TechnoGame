using System.Collections.Immutable;
using Singularity.Core.Data;
using Singularity.Kernel;

namespace Singularity.Core.Knowledge;

/// <summary>
/// Start state of the knowledge layer by storage row. <see cref="ActorNumber"/> maps an actor row to
/// its stable number (scenario order, §3.5 A7) used in RNG keys; rows may be stored in any order.
/// </summary>
public sealed class KnowledgeStart
{
    public KnowledgeStart(ImmutableArray<int> actorNumber, int nodeCount, int disciplineCount)
    {
        ActorNumber = actorNumber;
        NodeCount = nodeCount;
        DisciplineCount = disciplineCount;
        Known = new bool[actorNumber.Length * nodeCount];
        KD = new Fx6[actorNumber.Length * disciplineCount];
        KInd = new Fx6[actorNumber.Length * disciplineCount];
        for (int i = 0; i < actorNumber.Length; i++)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(actorNumber[i]);
            if (actorNumber.IndexOf(actorNumber[i]) != i)
                throw new ArgumentException($"Actor number {actorNumber[i]} is used twice.", nameof(actorNumber));
        }
    }

    public int ActorCount => ActorNumber.Length;
    public int NodeCount { get; }
    public int DisciplineCount { get; }
    public ImmutableArray<int> ActorNumber { get; }

    /// <summary>[actorRow·N + nodeRow].</summary>
    public bool[] Known { get; }

    /// <summary>[actorRow·D + discipline].</summary>
    public Fx6[] KD { get; }

    /// <summary>[actorRow·D + discipline].</summary>
    public Fx6[] KInd { get; }

    public bool IsKnown(int actorRow, int nodeRow) => Known[actorRow * NodeCount + nodeRow];

    /// <summary>
    /// Start from a scenario; the graph must come from <see cref="KnowledgeGraph.FromData"/> over the same
    /// node table (same rows) and the scenario disciplines. Actor row = actor number = scenario order.
    /// </summary>
    public static KnowledgeStart FromScenario(ScenarioData scenario, KnowledgeGraph graph)
    {
        int d = scenario.Disciplines.Length;
        if (graph.DisciplineNames.Length != d)
            throw new ArgumentException("Graph disciplines differ from the scenario.", nameof(graph));
        var start = new KnowledgeStart([.. Enumerable.Range(0, scenario.ActorCount)], graph.Count, d);
        for (int a = 0; a < scenario.ActorCount; a++)
        {
            foreach (int row in scenario.KnownOf(a))
                start.Known[a * graph.Count + row] = true;
            for (int k = 0; k < d; k++)
            {
                start.KD[a * d + k] = scenario.KDOf(a, scenario.Disciplines[k]);
                start.KInd[a * d + k] = scenario.KInd[a]; // one k_ind per actor in the scenario, copied to every discipline (§3.5 A17)
            }
        }
        return start;
    }
}
