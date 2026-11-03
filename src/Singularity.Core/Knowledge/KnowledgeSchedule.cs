using System.Collections.Immutable;
using Singularity.Core.Data;
using Singularity.Kernel;

namespace Singularity.Core.Knowledge;

/// <summary>
/// Moments of the full model for the L_K control (slice spec §6.1), from calibration runs. Indexed by
/// stable ids: actor number (scenario order) and node number. Generation g = 1…4.
/// </summary>
public sealed class LkTables
{
    /// <summary><see cref="Delta"/> value for "never" (node censored in more than 50% of seeds).</summary>
    public const int Never = int.MinValue;

    public LkTables(int actorCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(actorCount, 1);
        ActorCount = actorCount;
        PCens = new Fx6[actorCount * 4];
        Mean = new int[actorCount * 4];
        HalfWidth = new int[actorCount * 4];
        Delta = new int[actorCount * 64];
        Array.Fill(Delta, Never);
    }

    public int ActorCount { get; }

    /// <summary>[actor·4 + g − 1]: share of seeds where APP of generation g is unknown by month 383.</summary>
    public Fx6[] PCens { get; }

    /// <summary>[actor·4 + g − 1]: mean <c>t_known</c> of APP_g, months.</summary>
    public int[] Mean { get; }

    /// <summary>[actor·4 + g − 1]: <c>⌊√3·s⌉</c>, months (computed in analysis, §9.2).</summary>
    public int[] HalfWidth { get; }

    /// <summary>[actor·64 + node]: median <c>t_known(n) − t_known(APP_g)</c>, months, or <see cref="Never"/>.</summary>
    public int[] Delta { get; }
}

/// <summary>
/// Month from which a node is known under a knowledge-off control (spec v0.6 §6.1 "Момент открытия"): the
/// node opens in the merge of month <c>max(due, 0)</c>, like a discovery. Both schedules take the max over
/// requirements (an OR-group takes its earliest member; "never" in a requirement makes the node "never"),
/// so a known node always has known requirements (I5). Nodes known at the start are not touched.
/// </summary>
public static class KnowledgeSchedule
{
    public const long Never = long.MaxValue;
    public const long AtStart = long.MinValue;

    /// <summary>L_K0 lags by generation G0…G4, months after the middle of the generation window (§6.1).</summary>
    public static readonly ImmutableArray<int> Lk0Lags = [0, 0, 24, 36, 48];

    /// <summary>
    /// Middles of the generation windows G0…G4 as month indices from 1945-09 (§6.1 v0.6): 1 January of the
    /// middle year, <c>12·(Y − 1945) − 8</c>; G0 and G1 at the start. Due = 0 / 0 / 88 / 208 / 340.
    /// </summary>
    public static readonly ImmutableArray<int> Lk0WindowMid = [0, 0, 64, 172, 292];

    /// <summary>
    /// L_K: per (actor, g) a "never" indicator with probability <c>p_cens</c> and a generation clock
    /// <c>T ~ U[m − hw; m + hw]</c> from stream KNOW at month 0 (§9.1); node due = <c>T + δ</c>.
    /// Result is [actorRow·N + nodeRow].
    /// </summary>
    public static ImmutableArray<long> BuildLk(KnowledgeGraph graph, KnowledgeStart start, LkTables tables, CounterRng know)
    {
        int n = graph.Count;
        var due = new long[start.ActorCount * n];
        var clock = new long[5];
        for (int a = 0; a < start.ActorCount; a++)
        {
            int actor = start.ActorNumber[a];
            if (actor >= tables.ActorCount)
                throw new ArgumentException($"L_K tables have no actor {actor}.", nameof(tables));
            for (int g = 1; g <= 4; g++)
            {
                int k = actor * 4 + g - 1;
                bool never = CounterRng.Chance(KnowledgeRng.ScheduleNever(know, actor, g), tables.PCens[k]);
                clock[g] = never ? Never
                    : CounterRng.UniformInt(KnowledgeRng.ScheduleClock(know, actor, g), tables.Mean[k] - tables.HalfWidth[k], tables.Mean[k] + tables.HalfWidth[k]);
            }
            for (int r = 0; r < n; r++)
            {
                if (start.IsKnown(a, r))
                {
                    due[a * n + r] = AtStart;
                    continue;
                }
                int gen = graph.Gen[r];
                if (gen < 1 || gen > 4)
                    throw new ArgumentException($"Node {graph.Number[r]} (G{gen}) is unknown at the start but L_K has clocks only for G1–G4.", nameof(graph));
                int delta = tables.Delta[actor * 64 + graph.Number[r]];
                due[a * n + r] = clock[gen] == Never || delta == LkTables.Never ? Never : clock[gen] + delta;
            }
            ApplyRequirements(graph, due.AsSpan(a * n, n));
        }
        return ImmutableArray.Create(due);
    }

    /// <summary>
    /// L_K0: node due = window middle[gen] + <see cref="Lk0Lags"/>[gen], the same for all actors. Middles
    /// default to <see cref="Lk0WindowMid"/>; a scenario with another start date passes its own.
    /// </summary>
    public static ImmutableArray<long> BuildLk0(KnowledgeGraph graph, KnowledgeStart start, IReadOnlyList<int>? windowMidMonth = null)
    {
        windowMidMonth ??= Lk0WindowMid;
        if (windowMidMonth.Count != Lk0Lags.Length)
            throw new ArgumentException($"Need {Lk0Lags.Length} window middles (G0…G4).", nameof(windowMidMonth));
        int n = graph.Count;
        var due = new long[start.ActorCount * n];
        for (int a = 0; a < start.ActorCount; a++)
        {
            for (int r = 0; r < n; r++)
            {
                int gen = graph.Gen[r];
                if (gen >= Lk0Lags.Length)
                    throw new ArgumentException($"Node {graph.Number[r]}: no L_K0 lag for G{gen}.", nameof(graph));
                due[a * n + r] = start.IsKnown(a, r) ? AtStart : (long)windowMidMonth[gen] + Lk0Lags[gen];
            }
            ApplyRequirements(graph, due.AsSpan(a * n, n));
        }
        return ImmutableArray.Create(due);
    }

    // due[n] = max(due[n], max over groups of min over members), resolved depth-first; cycles are data errors.
    private static void ApplyRequirements(KnowledgeGraph graph, Span<long> due)
    {
        var state = new byte[graph.Count]; // 0 new, 1 in progress, 2 done
        for (int r = 0; r < graph.Count; r++)
            Resolve(graph, due, state, r);
    }

    private static long Resolve(KnowledgeGraph graph, Span<long> due, byte[] state, int r)
    {
        if (state[r] == 2)
            return due[r];
        if (state[r] == 1)
            throw new ArgumentException($"Requirement cycle through node {graph.Number[r]}.", nameof(graph));
        state[r] = 1;
        if (due[r] != AtStart)
        {
            RequirementLists req = graph.Requires;
            for (int g = req.GroupStart[r]; g < req.GroupStart[r + 1]; g++)
            {
                long earliest = Never;
                foreach (int m in req.GroupMembers(g))
                    earliest = Math.Min(earliest, Resolve(graph, due, state, m));
                due[r] = Math.Max(due[r], earliest);
            }
        }
        state[r] = 2;
        return due[r];
    }
}
