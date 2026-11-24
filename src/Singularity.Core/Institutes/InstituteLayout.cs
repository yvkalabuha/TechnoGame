using System.Collections.Immutable;
using Singularity.Core.Data;
using Singularity.Core.Knowledge;

namespace Singularity.Core.Institutes;

/// <summary>
/// Which institutes an actor has and how they are stored: rows grouped by actor, the slot of an institute is its position in the ID order of its country.
/// An institute is addressed by <see cref="InstituteHandle"/> (actor row, slot), never by a bare column index. Institutes cannot be closed or merged
/// (rev 4 §8), so the slot of an institute is stable and needs no generation.
/// </summary>
public sealed class InstituteLayout
{
    private readonly ImmutableArray<int> _slotCount, _startExp;
    private readonly ImmutableArray<int> _instituteRow;

    private InstituteLayout(int actors, int maxSlots, ImmutableArray<string> disciplines, ImmutableArray<int> nodesIn, ImmutableArray<int> slotCount,
        ImmutableArray<int> instituteRow, ImmutableArray<int> startExp, InstituteTable table)
    {
        ActorCount = actors;
        MaxSlots = maxSlots;
        DisciplineNames = disciplines;
        NodesIn = nodesIn;
        _slotCount = slotCount;
        _instituteRow = instituteRow;
        _startExp = startExp;
        Table = table;
    }

    public int ActorCount { get; }

    /// <summary>The largest number of institutes of one actor: the width of the column families.</summary>
    public int MaxSlots { get; }

    public ImmutableArray<string> DisciplineNames { get; }

    /// <summary>Nodes of the graph per discipline (<c>N_d</c> of the thresholds).</summary>
    public ImmutableArray<int> NodesIn { get; }

    public InstituteTable Table { get; }

    public int SlotCount(int actor) => _slotCount[actor];

    public ImmutableArray<int> SlotCounts => _slotCount;

    /// <summary>Row of the institute in the data table.</summary>
    public int DataRow(InstituteHandle h) => _instituteRow[h.Actor * MaxSlots + h.Slot];

    public string IdOf(InstituteHandle h) => Table.Id(DataRow(h));

    /// <summary>Experience of the institute in the area at the start: the threshold of its level of the data.</summary>
    public int StartExperience(InstituteHandle h, int discipline) => _startExp[(h.Actor * MaxSlots + h.Slot) * DisciplineNames.Length + discipline];

    /// <summary>Institutes standing at the start of the scenario (<c>founded ≤ start year</c>), grouped by actor.</summary>
    public static InstituteLayout FromData(InstituteTable table, ScenarioData scenario, KnowledgeGraph graph, InstituteParams p)
    {
        int actors = scenario.ActorCount;
        int d = graph.DisciplineNames.Length;
        var nodesIn = new int[d];
        for (int r = 0; r < graph.Count; r++)
        {
            if (graph.Discipline[r] >= 0)
                nodesIn[graph.Discipline[r]]++;
        }
        var perActor = new List<int>[actors];
        for (int a = 0; a < actors; a++)
            perActor[a] = [];
        for (int i = 0; i < table.Count; i++)
        {
            if (table.Founded(i) > scenario.StartYear)
                continue;
            int a = scenario.ActorIndex(table.Country(i));
            if (a < 0)
                throw new ArgumentException($"Institute '{table.Id(i)}': country {table.Country(i)} is not an actor of the scenario.", nameof(table));
            perActor[a].Add(i);   // table rows are in ID order
        }
        int max = 0;
        foreach (List<int> l in perActor)
            max = Math.Max(max, l.Count);
        if (max == 0)
            throw new ArgumentException("No institute stands at the start of the scenario.", nameof(table));
        var slotCount = new int[actors];
        var rows = new int[actors * max];
        Array.Fill(rows, -1);
        var startExp = new int[actors * max * d];
        for (int a = 0; a < actors; a++)
        {
            slotCount[a] = perActor[a].Count;
            for (int k = 0; k < perActor[a].Count; k++)
            {
                int row = perActor[a][k];
                rows[a * max + k] = row;
                for (int j = 0; j < table.AreaCount(row); j++)
                {
                    int disc = graph.DisciplineNames.IndexOf(table.AreaDiscipline(row, j));
                    if (disc < 0)
                        throw new ArgumentException($"Institute '{table.Id(row)}': discipline {table.AreaDiscipline(row, j)} is not in the scenario.", nameof(table));
                    startExp[(a * max + k) * d + disc] = InstituteMath.Threshold(table.AreaLevel(row, j), nodesIn[disc], p);
                }
            }
        }
        return new InstituteLayout(actors, max, graph.DisciplineNames, ImmutableArray.Create(nodesIn), ImmutableArray.Create(slotCount), ImmutableArray.Create(rows), ImmutableArray.Create(startExp), table);
    }
}

/// <summary>An institute of an actor: the actor row and the slot in the layout of that actor.</summary>
public readonly record struct InstituteHandle(int Actor, int Slot);
