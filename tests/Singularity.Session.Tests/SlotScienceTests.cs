using Singularity.Core.Commands;
using Singularity.Core.Institutes;
using Singularity.Core.Knowledge;
using Singularity.Kernel;
using Singularity.TestSupport;

namespace Singularity.Session.Tests;

/// <summary>TASK-0187: the slot science model (SCIENCE_SLOTS.md S1–S14, #561–#576) on the real slice.</summary>
public class SlotScienceTests
{
    private static SliceSession Make(ulong seed = 3, int? player = null, int threads = 1, bool slots = true) =>
        SliceTestData.Create(new SliceRunConfig { Seed = seed, PlayerActor = player, Threads = threads, ScienceSlots = slots });

    private static int[] Resolved(SliceSession s, int actor, long month)
    {
        InstituteSystem inst = s.World.Institutes!;
        var node = new int[inst.Layout.SlotCount(actor)];
        inst.Resolve(actor, month, node, new byte[node.Length]);
        return node;
    }

    private static int[] Frontier(SliceSession s, int actor) =>
        [.. Enumerable.Range(0, s.World.Graph.Count).Where(r => s.World.Knowledge.InFrontier(actor, r)).Select(r => s.World.Graph.Number[r])];

    private static long[] Progress(SliceSession s, int actor) =>
        [.. Enumerable.Range(0, s.World.Graph.Count).Select(r => s.World.Knowledge.Prog(actor, r).Raw)];

    [Fact]
    public void ARunHasNoProgressWithoutASlot_OneNodePerSlot_AndTheMoneyBalances_S1_S2_S3_S5()
    {
        SliceSession s = Make();
        SliceWorld w = s.World;
        var inputs = new KnowledgeInputsAdapter(w);
        int working = 0;
        for (long t = 0; t < 72; t++)
        {
            int actors = w.Scenario.ActorCount;
            var before = new long[actors][];
            var resolved = new int[actors][];
            var budget = new Fx6[actors];
            for (int a = 0; a < actors; a++)
            {
                before[a] = Progress(s, a);
                resolved[a] = Resolved(s, a, t);
                budget[a] = inputs.ResearchBudget(a, t);
                int[] used = [.. resolved[a].Where(n => n > 0)];
                Assert.Equal(used.Length, used.Distinct().Count());   // S2: a node in at most one slot
            }
            s.StepThroughMonth(t);
            for (int a = 0; a < actors; a++)
            {
                long[] after = Progress(s, a);
                for (int r = 0; r < after.Length; r++)
                {
                    Assert.True(after[r] >= before[a][r], $"month {t} actor {a} node row {r}: progress fell (S5)");
                    if (after[r] > before[a][r])
                    {
                        working++;
                        Assert.Contains(w.Graph.Number[r], resolved[a]);   // S1: only a node of a working slot moves
                    }
                }
                // S3: the research money is what the slots spent plus what returned to the budget.
                Assert.Equal(budget[a].Raw, (w.Knowledge.LastSpent(a) + w.Knowledge.ReturnedToBudget(a)).Raw);
            }
        }
        Assert.True(working > 100, "the slots did research");
    }

    [Fact]
    public void FreeSlotsOfTheAi_StayEmptyOnlyWhenTheFrontierHasNothingToTake_Liveness()
    {
        SliceSession s = Make(5);
        SliceWorld w = s.World;
        int idleWithWork = 0, slotMonths = 0;
        for (long t = 0; t < 96; t++)
        {
            for (int a = 0; a < w.Scenario.ActorCount; a++)
            {
                int[] node = Resolved(s, a, t);
                int[] free = [.. Frontier(s, a).Where(n => !node.Contains(n) && !w.Knowledge.Released(a, w.Graph.RowOf(n)))];
                slotMonths += node.Count(n => n >= 0);
                idleWithWork += free.Length > 0 ? node.Count(n => n == 0) : 0;
            }
            s.StepThroughMonth(t);
        }
        Assert.True(slotMonths > 0);
        Assert.Equal(0, idleWithWork);
    }

    [Fact]
    public void Hashes_AreEqualAcrossThreads_AndAfterSaveAndLoad_S6()
    {
        SliceSession one = Make(7, threads: 1), two = Make(7, threads: 2), four = Make(7, threads: 4);
        SliceSession loaded = Make(7);
        for (long t = 0; t < 60; t++)
        {
            one.StepThroughMonth(t);
            two.StepThroughMonth(t);
            four.StepThroughMonth(t);
            HashAssert.Equal(one.LastHash, two.LastHash);
            HashAssert.Equal(one.LastHash, four.LastHash);
        }
        using var snapshot = new MemoryStream();
        one.WriteSnapshot(snapshot);
        loaded.LoadSnapshot(new MemoryStream(snapshot.ToArray()));
        for (long t = 60; t < 84; t++)
        {
            one.StepThroughMonth(t);
            loaded.StepThroughMonth(t);
            HashAssert.Equal(one.LastHash, loaded.LastHash);
        }
    }

    [Fact]
    public void TheSharesModel_HasNoSlotColumns_S11()
    {
        SliceSession off = Make(slots: false);
        Assert.False(off.World.SlotsOn);
        Assert.DoesNotContain(off.World.Table.Columns, c => c.Name.StartsWith("inst.act", StringComparison.Ordinal) || c.Name.StartsWith("know.sprog", StringComparison.Ordinal)
            || c.Name.StartsWith("inst.q.", StringComparison.Ordinal) || c.Name == "run.slots");
        Assert.Contains(Make().World.Table.Columns, c => c.Name == "run.slots");
        Assert.NotEqual(Make(slots: false).LastHash, Make().LastHash);
    }

    [Fact]
    public void ThePlayersList_TakesTheActiveNodeAndFiveQueued_RefusesTheSeventhAndATakenNode_AndUnassignReplaces()
    {
        // FRA stands with two institutes: slot 1 has no list and takes the next unstarted node of the queue of slot 0 (O-089c).
        SliceSession s = Make(2, player: Make().World.Scenario.ActorIndex("FRA"));
        SliceWorld w = s.World;
        int fra = w.Config.PlayerActor!.Value;
        int[] f = Frontier(s, fra);
        Assert.True(f.Length >= 8, "the frontier is wide enough");
        Assert.All(Resolved(s, fra, 0), n => Assert.True(n <= 0));   // the player's institutes wait for the player
        int n0 = 0;
        for (int i = 0; i < 7; i++)
            Assert.True(s.Submit(SourceClass.Player, 0, n0++, new AssignNode(fra, 0, f[i])).Accepted);
        Assert.True(s.Submit(SourceClass.Player, 0, n0++, new AssignNode(fra, 1, f[2])).Accepted);   // the form is fine; the state refuses
        s.StepThroughMonth(0);
        Assert.Contains(s.Outcomes, o => o.Contains("rejected: QueueFull"));
        Assert.Contains(s.Outcomes, o => o.Contains("rejected: NodeTaken"));
        Span<int> entries = stackalloc int[6];
        Assert.Equal(6, w.SlotOrders!.Entries(new InstituteHandle(fra, 0), entries));
        Assert.Equal(f[0], entries[0]);
        int[] work = Resolved(s, fra, 1);
        Assert.Equal([f[0], f[1]], work[..2]);   // the other slots of FRA are candidates, not built
        for (long t = 1; t < 4; t++)
            s.StepThroughMonth(t);
        long progress = w.Knowledge.Prog(fra, w.Graph.RowOf(f[0])).Raw;
        Assert.True(progress > 0, "the active node moves");
        Assert.True(Enumerable.Range(2, 4).All(i => w.Knowledge.Prog(fra, w.Graph.RowOf(f[i])) == Fx6.Zero), "the queued nodes no slot works on do not (S1)");
        // Unassigning the active node replaces it: the next free entry works, the progress stays with the node (S5).
        Assert.True(s.Submit(SourceClass.Player, 0, n0++, new Unassign(fra, 0, f[0])).Accepted);
        s.StepThroughMonth(4);
        Assert.Equal(f[2], Resolved(s, fra, 5)[0]);
        Assert.Equal(progress, w.Knowledge.Prog(fra, w.Graph.RowOf(f[0])).Raw);
        // Reordering the queue is free and changes who comes next.
        Assert.True(s.Submit(SourceClass.Player, 0, n0++, new ReorderQueue(fra, 0, [f[1], f[5], f[4], f[3], f[2]])).Accepted);
        s.StepThroughMonth(5);
        Assert.DoesNotContain(s.Outcomes, o => o.Contains("rejected: NotInList"));
        Assert.Equal(5, w.SlotOrders.Entries(new InstituteHandle(fra, 0), entries));
        Assert.Equal(f[5], entries[1]);
    }

    [Fact]
    public void AFreeInstituteTakesAnUnstartedNodeOfAnotherQueue_O089c()
    {
        SliceSession s = Make(2, player: 0);
        int[] f = Frontier(s, 0);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new AssignNode(0, 0, f[0])).Accepted);
        Assert.True(s.Submit(SourceClass.Player, 0, 1, new AssignNode(0, 0, f[1])).Accepted);
        s.StepThroughMonth(0);
        int[] node = Resolved(s, 0, 1);
        Assert.Equal(f[0], node[0]);
        Assert.Equal(f[1], node[1]);   // slot 1 has no list: it took the queued node of slot 0
        Span<int> entries = stackalloc int[6];
        Assert.Equal(2, s.World.SlotOrders!.Entries(new InstituteHandle(0, 0), entries));   // the list still holds both: the borrowed node is only worked elsewhere
    }

    [Fact]
    public void Focus_SpeedsTheNodeUpAndCostsMore_ItCannotBeChangedAtOnce_AndLevelZeroIsOff()
    {
        SliceSession plain = Make(2, player: 0), focused = Make(2, player: 0);
        int[] f = Frontier(plain, 0);
        foreach (SliceSession s in new[] { plain, focused })
            Assert.True(s.Submit(SourceClass.Player, 0, 0, new AssignNode(0, 0, f[0])).Accepted);
        Assert.True(focused.Submit(SourceClass.Player, 0, 1, new SetFocus(0, 0, 2, false)).Accepted);
        plain.StepThroughMonth(0);
        focused.StepThroughMonth(0);
        Assert.DoesNotContain(focused.Outcomes, o => o.Contains("rejected"));
        plain.StepThroughMonth(1);
        focused.StepThroughMonth(1);
        int row = plain.World.Graph.RowOf(f[0]);
        KnowledgeNodeStep a = plain.World.Knowledge.LastStep(0, row), b = focused.World.Knowledge.LastStep(0, row);
        Assert.True(b.DProg > a.DProg, "the focus makes progress faster");
        Assert.True(b.Alloc > a.Alloc, "and the money rate higher");
        // s = 1.5 and s^0.6 on the price: the ask grows by s^1.6 (O-089a), the speed by s.
        Assert.InRange(b.DProg.Raw, a.DProg.Raw * 3 / 2 - 5, a.DProg.Raw * 3 / 2 + 5);
        Assert.True(focused.Submit(SourceClass.Player, 0, 2, new SetFocus(0, 0, 1, false)).Accepted);
        focused.StepThroughMonth(2);
        Assert.Contains(focused.Outcomes, o => o.Contains("rejected: FocusTooSoon"));
        Assert.True(focused.Submit(SourceClass.Player, 0, 3, new SetFocus(0, 0, 0, false)).Accepted);
        focused.StepThroughMonth(3);
        plain.StepThroughMonth(2);
        plain.StepThroughMonth(3);
        Assert.Equal(plain.World.Knowledge.LastStep(0, row).DProg, focused.World.Knowledge.LastStep(0, row).DProg);
    }

    [Fact]
    public void ANodeWithoutOverlapGoesAtBaseSpeed_AnOverlappingOneIsFasterWithinTheCap_NoPenalty_572()
    {
        SliceSession s = Make();
        SliceWorld w = s.World;
        InstituteSystem inst = w.Institutes!;
        bool noOverlap = false, overlap = false;
        for (int a = 0; a < w.Scenario.ActorCount; a++)
        {
            for (int k = 0; k < inst.Layout.StartingCount(a); k++)
            {
                var h = new InstituteHandle(a, k);
                for (int r = 0; r < w.Graph.Count; r++)
                {
                    Fx6 m = inst.InstituteMultiplier(h, r);
                    Assert.True(m >= Fx6.One && m <= Fx6.One + new InstituteParams().LevelBonus * Fx6.FromInt(4), "m_inst is within 1 .. 1 + 4·bonus (S10)");
                    if (inst.Overlap(h, r) == Fx6.Zero)
                    {
                        noOverlap = true;
                        Assert.Equal(Fx6.One, m);
                    }
                    else
                    {
                        overlap = true;
                        Assert.True(m > Fx6.One);
                    }
                }
            }
        }
        Assert.True(noOverlap && overlap);
    }

    [Fact]
    public void TheExperienceOfAnInstitute_GrowsWithTheProgressItMade_O089d()
    {
        SliceSession s = Make(4);
        SliceWorld w = s.World;
        InstituteSystem inst = w.Institutes!;
        int dirs = inst.Layout.DirectionNames.Length;
        long Total(int a)
        {
            long sum = 0;
            for (int k = 0; k < inst.Layout.SlotCount(a); k++)
            {
                for (int d = 0; d < dirs; d++)
                    sum += inst.DirExperience(new InstituteHandle(a, k), d);
            }
            return sum;
        }
        long start = Total(0);
        for (long t = 0; t < 120; t++)
            s.StepThroughMonth(t);
        Assert.True(Total(0) > start, "ten years of work add experience");
    }

    [Fact]
    public void TheAiRaisesItsFocus_OnlyWhenBehindInAirPower_AndAnAirPowerIndexExistsEveryMonth()
    {
        SliceSession s = Make(6);
        for (long t = 0; t < 24; t++)
        {
            s.StepThroughMonth(t);
            foreach (ActorView av in s.View.Actors)
            {
                Assert.True(av.FleetValue.HasValue, "MPI every month");
                Assert.NotNull(av.Slots);
                Assert.Equal(av.FleetValue, av.Slots!.Mpi);
            }
        }
    }
}
