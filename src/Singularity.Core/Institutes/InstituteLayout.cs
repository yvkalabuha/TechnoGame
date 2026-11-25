using System.Collections.Immutable;
using Singularity.Core.Data;
using Singularity.Core.Knowledge;
using Singularity.Kernel;

namespace Singularity.Core.Institutes;

/// <summary>
/// Which institutes an actor has and how they are stored: rows grouped by actor, the slot of an institute is its position among those of its country
/// (the institutes standing at the start first, then the build candidates, each in ID order). An institute is addressed by <see cref="InstituteHandle"/>
/// (actor row, slot), never by a bare column index. Institutes cannot be closed or merged (rev 4 §8), so the slot is stable and needs no generation.
/// </summary>
public sealed class InstituteLayout
{
    private readonly ImmutableArray<int> _slotCount, _startingCount, _startExp, _instituteRow, _startDirLevel;
    private readonly ImmutableArray<bool> _small;

    private InstituteLayout(int actors, int maxSlots, ImmutableArray<string> disciplines, ImmutableArray<int> nodesIn, ImmutableArray<int> slotCount,
        ImmutableArray<int> startingCount, ImmutableArray<int> instituteRow, ImmutableArray<int> startExp, ImmutableArray<bool> small, InstituteTable table,
        int startYear, int startMonth, ImmutableArray<string> directionNames, ImmutableArray<int> dirNodesIn, ImmutableArray<int> startDirLevel)
    {
        DirectionNames = directionNames;
        DirNodesIn = dirNodesIn;
        _startDirLevel = startDirLevel;
        ActorCount = actors;
        MaxSlots = maxSlots;
        DisciplineNames = disciplines;
        NodesIn = nodesIn;
        _slotCount = slotCount;
        _startingCount = startingCount;
        _instituteRow = instituteRow;
        _startExp = startExp;
        _small = small;
        Table = table;
        StartYear = startYear;
        StartMonth = startMonth;
    }

    public int ActorCount { get; }

    /// <summary>The largest number of institutes of one actor, candidates included: the width of the column families.</summary>
    public int MaxSlots { get; }

    public ImmutableArray<string> DisciplineNames { get; }

    /// <summary>Nodes of the graph per discipline (<c>N_d</c> of the thresholds).</summary>
    public ImmutableArray<int> NodesIn { get; }

    public InstituteTable Table { get; }

    /// <summary>The directions of the slot model (the graph's: those of the data, or the disciplines when the data has none) and the nodes in each.</summary>
    public ImmutableArray<string> DirectionNames { get; }

    public ImmutableArray<int> DirNodesIn { get; }

    /// <summary>Level 0..4 of the institute in the direction when it stands at the start or opens (levels of the data above 4 count as 4).</summary>
    public int StartDirLevel(InstituteHandle h, int direction) => _startDirLevel[(h.Actor * MaxSlots + h.Slot) * DirectionNames.Length + direction];

    /// <summary>Calendar year and month (1..12) of month index 0.</summary>
    public int StartYear { get; }

    public int StartMonth { get; }

    /// <summary>Slots of the actor: the institutes standing at the start and the build candidates.</summary>
    public int SlotCount(int actor) => _slotCount[actor];

    /// <summary>Institutes standing at the start of the scenario (<c>founded ≤ start year</c>): the first slots.</summary>
    public int StartingCount(int actor) => _startingCount[actor];

    public ImmutableArray<int> SlotCounts => _slotCount;

    /// <summary>Row of the institute in the data table.</summary>
    public int DataRow(InstituteHandle h) => _instituteRow[h.Actor * MaxSlots + h.Slot];

    public string IdOf(InstituteHandle h) => Table.Id(DataRow(h));

    /// <summary>Sum of the levels of the institute (the size of its project).</summary>
    public int TotalLevels(InstituteHandle h)
    {
        int row = DataRow(h), sum = 0;
        for (int k = 0; k < Table.AreaCount(row); k++)
            sum += Table.AreaLevel(row, k);
        return sum;
    }

    public int RefYear(InstituteHandle h) => Table.RefYear(DataRow(h));

    /// <summary>The country is small: its project may have at most <see cref="InstituteParams.SmallCountryLevelCap"/> levels.</summary>
    public bool IsSmallCountry(int actor) => _small[actor];

    /// <summary>Calendar year of a month index.</summary>
    public int YearOf(long month) => StartYear + (int)((StartMonth - 1 + month) / 12);

    /// <summary>First month index of a calendar year (negative before the start).</summary>
    public long FirstMonthOf(int year) => (year - StartYear) * 12L - (StartMonth - 1);

    /// <summary>Experience of the institute in the area when it stands: the threshold of its level of the data (at the start, or at the opening of a project).</summary>
    public int StartExperience(InstituteHandle h, int discipline) => _startExp[(h.Actor * MaxSlots + h.Slot) * DisciplineNames.Length + discipline];

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
        var standing = new List<int>[actors];
        var candidates = new List<int>[actors];
        for (int a = 0; a < actors; a++)
        {
            standing[a] = [];
            candidates[a] = [];
        }
        for (int i = 0; i < table.Count; i++)
        {
            int a = scenario.ActorIndex(table.Country(i));
            if (a < 0)
                continue;   // the table is shared by scenarios: a country outside this one has no slots here
            // Founded not after the start: the year decides, and within the start year the month when the data has it (a row with a year only stands).
            bool stands = table.Founded(i) < scenario.StartYear
                || (table.Founded(i) == scenario.StartYear && (table.FoundedMonth(i) == 0 || table.FoundedMonth(i) <= scenario.StartMonth));
            (stands ? standing : candidates)[a].Add(i);   // table rows are in ID order
        }
        int max = 0;
        for (int a = 0; a < actors; a++)
            max = Math.Max(max, standing[a].Count + candidates[a].Count);
        // A shared table may have no institute standing at this scenario's start: the scenario still loads (only candidates, or no slots).
        var slotCount = new int[actors];
        var startingCount = new int[actors];
        var rows = new int[actors * max];
        Array.Fill(rows, -1);
        var startExp = new int[actors * max * d];
        for (int a = 0; a < actors; a++)
        {
            List<int> all = standing[a].Concat(candidates[a]).ToList();
            slotCount[a] = all.Count;
            startingCount[a] = standing[a].Count;
            for (int k = 0; k < all.Count; k++)
            {
                int row = all[k];
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
        // Directions of the slot model: those of the data when the graph has them, else the disciplines (a level of an area is the level of the direction of that name).
        int dn = graph.DirectionNames.Length;
        bool dirData = !graph.DirectionNames.SequenceEqual(graph.DisciplineNames);
        var dirNodesIn = new int[dn];
        for (int r = 0; r < graph.Count; r++)
        {
            foreach (int dir in graph.Directions[r])
                dirNodesIn[dir]++;
        }
        var startDirLevel = new int[actors * max * dn];
        for (int a = 0; a < actors; a++)
        {
            for (int k = 0; k < slotCount[a]; k++)
            {
                int row = rows[a * max + k];
                int at = (a * max + k) * dn;
                if (dirData)
                {
                    for (int j = 0; j < table.DirectionCount(row); j++)
                        startDirLevel[at + table.DirectionAt(row, j)] = Math.Min(table.DirectionLevel(row, j), InstituteTable.MaxLevel);
                }
                else
                {
                    for (int j = 0; j < table.AreaCount(row); j++)
                        startDirLevel[at + graph.DisciplineNames.IndexOf(table.AreaDiscipline(row, j))] = table.AreaLevel(row, j);
                }
            }
        }
        Fx6 largest = Fx6.Zero;
        foreach (Fx6 b in scenario.BudgetIndex)
            largest = b > largest ? b : largest;
        var small = new bool[actors];
        for (int a = 0; a < actors; a++)
            small[a] = scenario.BudgetIndex[a] <= largest * p.SmallCountryShare;
        return new InstituteLayout(actors, max, graph.DisciplineNames, ImmutableArray.Create(nodesIn), ImmutableArray.Create(slotCount), ImmutableArray.Create(startingCount),
            ImmutableArray.Create(rows), ImmutableArray.Create(startExp), ImmutableArray.Create(small), table, scenario.StartYear, scenario.StartMonth,
            graph.DirectionNames, ImmutableArray.Create(dirNodesIn), ImmutableArray.Create(startDirLevel));
    }
}

/// <summary>An institute of an actor: the actor row and the slot in the layout of that actor.</summary>
public readonly record struct InstituteHandle(int Actor, int Slot);
