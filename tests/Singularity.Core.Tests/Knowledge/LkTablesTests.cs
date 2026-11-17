using System.Collections.Immutable;
using Singularity.Core.Data;
using Singularity.Core.Knowledge;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Knowledge;

/// <summary>TASK-0061 (DEBT-0034): BuildLk refuses nodes beyond the tables; L_K with two actors and node number 1000.</summary>
public class LkTablesTests
{
    private static KnowledgeGraph Graph(params int[] numbers) => KnowledgeGraph.Create(
        numbers.Select(n => new KnowledgeNodeSpec(n, NodeType.FND, 1, 0, false, Fx6.FromInt(1000), [])).ToList(), ["prop"]);

    [Fact]
    public void BuildLk_RefusesNodeAboveTablesMax()
    {
        KnowledgeGraph g = Graph(1, 1000);
        var tables = new LkTables(1, 999);
        var e = Assert.Throws<ArgumentException>(() =>
            KnowledgeSchedule.BuildLk(g, new KnowledgeStart([0], g.Count, 1), tables, new CounterRng(1)));
        Assert.Contains("1000", e.Message);
        Assert.Contains("999", e.Message);
    }

    [Fact]
    public void BuildLk_TwoActors_Node1000_UsesEachActorsOwnClockAndDelta()
    {
        KnowledgeGraph g = Graph(1000);
        var tables = new LkTables(2, 1000); // PCens 0, half-width 0: the clock is the mean
        tables.Mean[0 * 4 + 0] = 10;
        tables.Mean[1 * 4 + 0] = 20;
        tables.Delta[tables.DeltaIndex(0, 1000)] = 3;
        tables.Delta[tables.DeltaIndex(1, 1000)] = 7;
        ImmutableArray<long> due = KnowledgeSchedule.BuildLk(g, new KnowledgeStart([0, 1], g.Count, 1), tables, new CounterRng(5));
        Assert.Equal([13L, 27L], due.ToArray());

        tables.Delta[tables.DeltaIndex(1, 1000)] = LkTables.Never; // another actor's δ never leaks across
        due = KnowledgeSchedule.BuildLk(g, new KnowledgeStart([0, 1], g.Count, 1), tables, new CounterRng(5));
        Assert.Equal([13L, KnowledgeSchedule.Never], due.ToArray());
    }
}
