using Singularity.Core.Commands;
using Singularity.Core.Policy;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>
/// Properties of the assembled slice beyond the unit tests (test-engineer review of TASK-0022): journal order under any interleaving
/// of sources, replay equivalence across threads and mid-run points, AI never forces, START_PERT bounds, START_SYM equality and the
/// independence of the FIX_* switches. The pseudo-random choices use a seeded <see cref="Random"/> of the test itself, not the sim.
/// </summary>
public class AssembledSliceProperties
{
    private static BudgetShares Shares(long r, long c, long d, long p, long q) =>
        new(Fx6.FromRaw(r), Fx6.FromRaw(c), Fx6.FromRaw(d), Fx6.FromRaw(p), Fx6.FromRaw(q));

    private static readonly BudgetShares[] ShareSets =
    [
        Shares(200_000, 200_000, 200_000, 300_000, 100_000),
        Shares(500_000, 100_000, 100_000, 200_000, 100_000),
        Shares(100_000, 100_000, 500_000, 200_000, 100_000),
        Shares(100_000, 500_000, 100_000, 200_000, 100_000),
    ];

    private sealed record Item(long Tick, SourceClass Source, int Id, int Local, SliceCommand Command);

    /// <summary>A script: every source emits its commands in local_n order; ticks are mixed (monthly and not).</summary>
    private static List<List<Item>> Script(int seed)
    {
        var rnd = new Random(seed);
        (SourceClass, int)[] sources = [(SourceClass.Player, 0), (SourceClass.Player, 1), (SourceClass.Harness, 0), (SourceClass.Harness, 3), (SourceClass.Ai, 2)];
        var local = new int[sources.Length];
        var byTick = new List<List<Item>>();
        long tick = 0;
        for (int step = 0; step < 14; step++)
        {
            var items = new List<Item>();
            int n = rnd.Next(2, 7);
            for (int i = 0; i < n; i++)
            {
                int s = rnd.Next(sources.Length);
                int actor = rnd.Next(5);
                SliceCommand c = rnd.Next(3) switch
                {
                    0 => new SetBudgetShares(actor, ShareSets[rnd.Next(ShareSets.Length)]),
                    1 => new StartSeries(actor, rnd.Next(3), Fx6.FromInt(rnd.Next(1, 40)), sources[s].Item1 != SourceClass.Ai && rnd.Next(2) == 0),
                    _ => new CloseLine(actor, rnd.Next(3)),
                };
                items.Add(new Item(tick, sources[s].Item1, sources[s].Item2, local[s]++, c));
            }
            byTick.Add(items);
            tick += rnd.Next(1, 3) == 1 ? 31 : 17;   // both month-aligned-ish and odd ticks
        }
        return byTick;
    }

    private static SliceSession RunScript(List<List<Item>> script, int permSeed, SliceRunConfig config, out List<string> verdicts)
    {
        SliceSession s = SliceSession.Create(SliceTestData.Data, config);
        var perm = new Random(permSeed);
        verdicts = [];
        foreach (List<Item> items in script)
        {
            s.StepTo(items[0].Tick);
            // Any interleaving of the sources, each source in its own local_n order.
            var queues = items.GroupBy(i => (i.Source, i.Id)).Select(g => new Queue<Item>(g.OrderBy(i => i.Local))).ToList();
            while (queues.Count > 0)
            {
                int q = permSeed == 0 ? 0 : perm.Next(queues.Count);
                Item it = queues[q].Dequeue();
                if (queues[q].Count == 0)
                    queues.RemoveAt(q);
                CommandVerdict v = s.Submit(it.Source, it.Id, it.Local, it.Command);
                verdicts.Add($"{it.Source}/{it.Id}/{it.Local}:{(v.Accepted ? "ok" : v.Reason.ToString())}");
            }
        }
        return s;
    }

    [Fact]
    public void Journal_IsTheSame_UnderAnyInterleavingOfSources_OverManyTicks()
    {
        for (int scriptSeed = 1; scriptSeed <= 4; scriptSeed++)
        {
            List<List<Item>> script = Script(scriptSeed);
            var config = new SliceRunConfig { Seed = 5 };
            SliceSession reference = RunScript(script, 0, config, out List<string> refVerdicts);
            reference.StepTo(reference.Tick + 40);
            Assert.True(reference.CommandLog.Length > 0);
            for (int perm = 1; perm <= 6; perm++)
            {
                SliceSession s = RunScript(script, perm, config, out List<string> verdicts);
                s.StepTo(reference.Tick);
                // Verdicts depend only on (source, local_n): the multiset of outcomes is the same in any order.
                Assert.Equal(refVerdicts.Order().ToList(), verdicts.Order().ToList());
                Assert.Equal(reference.CommandLog, s.CommandLog);
                Assert.Equal(reference.LastHash, s.LastHash);
            }
        }
    }

    [Fact]
    public void Journal_KeysAreStrictlyIncreasing_LocalNGrowsPerSource_AndAiNeverForces()
    {
        for (int scriptSeed = 1; scriptSeed <= 6; scriptSeed++)
        {
            SliceSession s = RunScript(Script(scriptSeed), scriptSeed, new SliceRunConfig { Seed = 2 }, out _);
            s.Step(40);
            List<LoggedCommand> log = CommandCodec.DecodeLog(s.CommandLog);
            Assert.NotEmpty(log);
            for (int i = 1; i < log.Count; i++)
                Assert.True(log[i - 1].Key < log[i].Key, $"script {scriptSeed}: line {i} breaks the key order");
            var last = new Dictionary<(SourceClass, int), int>();
            foreach (LoggedCommand c in log)
            {
                if (last.TryGetValue((c.Key.Source, c.Key.SourceId), out int prev))
                    Assert.True(c.Key.LocalN > prev);
                last[(c.Key.Source, c.Key.SourceId)] = c.Key.LocalN;
                if (c.Key.Source == SourceClass.Ai)
                    Assert.False(c.Command is StartSeries { Force: true });
                Assert.Equal(c, CommandCodec.Decode(CommandCodec.Encode(c)));
            }
            // Every accepted submission was applied; none is left unlogged or duplicated.
            Assert.Equal(log.Count, s.Outcomes.Count);
        }
    }

    [Fact]
    public void Replay_GivesTheSameHash_OnOtherThreads_AtOddTicks_AndAtAPrefix()
    {
        List<List<Item>> script = Script(9);
        SliceSession original = RunScript(script, 3, new SliceRunConfig { Seed = 21, Threads = 1 }, out _);
        long mid = original.Tick;
        string midHash = original.LastHash;
        string midLog = original.CommandLog;
        original.StepTo(mid + 200);

        foreach ((int threads, int chunk) in new[] { (1, 1), (4, 1), (8, 3) })
        {
            var cfg = new SliceRunConfig { Seed = 21, Threads = threads, ChunkSize = chunk };
            SliceSession full = SliceSession.Replay(SliceTestData.Data, cfg, original.CommandLog, original.Tick);
            Assert.Equal(original.LastHash, full.LastHash);
            Assert.Equal(original.CommandLog, full.CommandLog);
            // "Save at mid": the log prefix replayed to the same tick gives the hash seen then.
            SliceSession prefix = SliceSession.Replay(SliceTestData.Data, cfg, midLog, mid);
            Assert.Equal(midHash, prefix.LastHash);
        }
        // Replaying a replay is a fixed point.
        SliceSession again = SliceSession.Replay(SliceTestData.Data, new SliceRunConfig { Seed = 21 }, original.CommandLog, original.Tick);
        Assert.Equal(original.CommandLog, SliceSession.Replay(SliceTestData.Data, new SliceRunConfig { Seed = 21 }, again.CommandLog, again.Tick).CommandLog);
    }

    [Fact]
    public void Replay_WithAnotherSeed_DoesNotReproduceTheHash()
    {
        SliceSession original = RunScript(Script(2), 1, new SliceRunConfig { Seed = 21 }, out _);
        original.Step(60);
        SliceSession other = SliceSession.Replay(SliceTestData.Data, new SliceRunConfig { Seed = 22 }, original.CommandLog, original.Tick);
        Assert.NotEqual(original.LastHash, other.LastHash);
    }

    [Fact]
    public void ScriptedRun_IsDeterministic_AcrossThreads()
    {
        List<List<Item>> script = Script(5);
        string? hash = null;
        foreach (int threads in new[] { 1, 4, 8 })
        {
            SliceSession s = RunScript(script, 2, new SliceRunConfig { Seed = 13, Threads = threads, ChunkSize = threads }, out _);
            s.Step(120);
            hash ??= s.LastHash;
            Assert.Equal(hash, s.LastHash);
        }
    }

    // ---- START_PERT / START_SYM

    [Fact]
    public void StartPert_Multipliers_StayIn08To12_ForManySeeds_AndAreDeterministic()
    {
        var all = new List<long>();
        for (ulong seed = 1; seed <= 300; seed++)
        {
            for (int i = 0; i < 60; i++)
            {
                long a = PolicyRng.StartMultiplier(new CounterRng(seed), i).Raw;
                Assert.InRange(a, 800_000, 1_200_000);
                Assert.Equal(a, PolicyRng.StartMultiplier(new CounterRng(seed), i).Raw);
                all.Add(a);
            }
        }
        // Not degenerate: both halves of the interval are reached and the mean is near 1.
        Assert.Contains(all, x => x < 820_000);
        Assert.Contains(all, x => x > 1_180_000);
        Assert.InRange(all.Average(), 990_000, 1_010_000);
    }

    [Fact]
    public void StartPert_DoesNotMutateTheLoadedScenario_AndFixAllDoesNotFixIt()
    {
        string before = SliceTestData.Data.Scenario("jet_slice_1945").Hash;
        SliceSession a = SliceTestData.Create(SliceRunConfig.FixAll(1) with { Start = StartVariant.Perturbed });
        SliceSession b = SliceTestData.Create(SliceRunConfig.FixAll(2) with { Start = StartVariant.Perturbed });
        Assert.Equal(before, SliceTestData.Data.Scenario("jet_slice_1945").Hash);
        // START has its own seed (the run seed): with every FIX_* set the perturbed start still follows the seed.
        Assert.NotEqual(a.World.Scenario.Hash, b.World.Scenario.Hash);
        Assert.Equal(SliceRunConfig.FixedSeed, a.World.Streams.Know.Seed);
        Assert.Equal(1UL, a.World.Streams.Start.Seed);
    }

    [Fact]
    public void StartSym_IsSeedIndependent_AndEqualInTheWorld_FromTheStartAndAfterRun()
    {
        var cfg = new SliceRunConfig { Seed = 1, Start = StartVariant.Symmetric, ProfileCell = 0 };
        SliceSession a = SliceTestData.Create(cfg);
        SliceSession b = SliceTestData.Create(cfg with { Seed = 2 });
        Assert.Equal(a.World.Scenario.Hash, b.World.Scenario.Hash);
        int usa = a.World.Scenario.ActorIndex("USA"), gbr = a.World.Scenario.ActorIndex("GBR"), sun = a.World.Scenario.ActorIndex("SUN");
        Assert.Equal(a.World.BudgetAtStart[usa], a.World.BudgetAtStart[gbr]);
        Assert.Equal(a.World.BudgetAtStart[usa], a.World.BudgetAtStart[sun]);
        for (int r = 0; r < a.World.Graph.Count; r++)
        {
            Assert.Equal(a.World.Knowledge.IsKnown(usa, r), a.World.Knowledge.IsKnown(gbr, r));
            Assert.Equal(a.World.Knowledge.IsKnown(usa, r), a.World.Knowledge.IsKnown(sun, r));
        }
        // Same profile cell: the three trajectories keep equal budgets at every month (no stream touches the budget).
        a.StepThroughMonth(23);
        Assert.Equal(a.World.Policy.Budget(usa), a.World.Policy.Budget(gbr));
        Assert.Equal(a.World.Policy.Budget(usa), a.World.Policy.Budget(sun));
    }

    // ---- FIX_* independence

    public static TheoryData<bool, bool, bool> FixCombos()
    {
        var d = new TheoryData<bool, bool, bool>();
        for (int m = 0; m < 8; m++)
            d.Add((m & 1) != 0, (m & 2) != 0, (m & 4) != 0);
        return d;
    }

    [Theory]
    [MemberData(nameof(FixCombos))]
    public void FixFlags_SwitchExactlyTheirStreams(bool fixK, bool fixE, bool fixA)
    {
        const ulong seed = 77;
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = seed, FixK = fixK, FixE = fixE, FixA = fixA });
        SliceStreams st = s.World.Streams;
        Assert.Equal(fixK ? SliceRunConfig.FixedSeed : seed, st.Know.Seed);
        Assert.Equal(fixE ? SliceRunConfig.FixedSeed : seed, st.Events.Seed);
        Assert.Equal(fixA ? SliceRunConfig.FixedSeed : seed, st.Ai.Seed);
        Assert.Equal(seed, st.Start.Seed);
    }

    [Fact]
    public void FixK_And_FixE_DoNotTouchTheProfiles_FixA_DoesNot_TouchKnowledgeSeed()
    {
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var free = SliceTestData.Create(new SliceRunConfig { Seed = seed });
            foreach (SliceRunConfig c in new[] { new SliceRunConfig { Seed = seed, FixK = true }, new SliceRunConfig { Seed = seed, FixE = true } })
            {
                SliceSession s = SliceTestData.Create(c);
                for (int a = 0; a < 5; a++)
                    Assert.Equal(free.World.Policy.Profile(a), s.World.Policy.Profile(a));
            }
        }
    }

    [Fact]
    public void FixA_Fixes_TheProfiles_ToOneSet_AcrossAllSeeds()
    {
        PolicyProfile[]? first = null;
        for (ulong seed = 1; seed <= 30; seed++)
        {
            SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = seed, FixA = true });
            PolicyProfile[] p = [.. Enumerable.Range(0, 5).Select(a => s.World.Policy.Profile(a))];
            first ??= p;
            Assert.Equal(first, p);
        }
    }
}
