using System.Collections.Immutable;
using Singularity.Core.Commands;
using Singularity.Core.Knowledge;
using Singularity.Session;
using Singularity.Core.Data;
using Singularity.Core.Policy;
using Singularity.Core.Institutes;
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
            var knownBefore = new bool[actors][];
            var resolved = new int[actors][];
            var budget = new Fx6[actors];
            for (int a = 0; a < actors; a++)
            {
                before[a] = Progress(s, a);
                knownBefore[a] = [.. Enumerable.Range(0, w.Graph.Count).Select(r => w.Knowledge.IsKnown(a, r))];
                resolved[a] = Resolved(s, a, t);
                budget[a] = inputs.ResearchBudget(a, t);
                int[] used = [.. resolved[a].Where(n => n > 0)];
                Assert.Equal(used.Length, used.Distinct().Count());   // S2: a node in at most one slot
            }
            s.StepThroughMonth(t);
            for (int a = 0; a < actors; a++)
            {
                long[] after = Progress(s, a);
                // S4: a node that became known has every requirement group satisfied.
                for (int r = 0; r < w.Graph.Count; r++)
                {
                    if (!w.Knowledge.IsKnown(a, r) || knownBefore[a][r])
                        continue;
                    for (int g = w.Graph.Requires.GroupStart[r]; g < w.Graph.Requires.GroupStart[r + 1]; g++)
                        Assert.Contains(true, w.Graph.Requires.GroupMembers(g).ToArray().Select(m => w.Knowledge.IsKnown(a, m)));
                }
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
        SliceSession one = Make(7, threads: 1), two = Make(7, threads: 2), eight = Make(7, threads: 8);
        SliceSession loaded = Make(7);
        for (long t = 0; t < 60; t++)
        {
            one.StepThroughMonth(t);
            two.StepThroughMonth(t);
            eight.StepThroughMonth(t);
            HashAssert.Equal(one.LastHash, two.LastHash);
            HashAssert.Equal(one.LastHash, eight.LastHash);
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
        // S11 is pinned by the goldens of every test on SliceRunConfig.Legacy (unchanged); this one shows that slots off adds no slot columns.
        SliceSession off = Make(slots: false);
        Assert.False(off.World.SlotsOn);
        Assert.DoesNotContain(off.World.Table.Columns, c => c.Name.StartsWith("inst.act", StringComparison.Ordinal) || c.Name.StartsWith("know.sprog", StringComparison.Ordinal)
            || c.Name.StartsWith("inst.q.", StringComparison.Ordinal) || c.Name == "run.slots");
        Assert.Contains(Make().World.Table.Columns, c => c.Name == "run.slots");
        HashAssert.NotEqual(Make(slots: false).LastHash, Make().LastHash);
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
            Assert.True(s.Submit(SourceClass.Player, 0, n0++, new AssignNode(fra, 0, f[i], Acknowledged: true)).Accepted);
        Assert.True(s.Submit(SourceClass.Player, 0, n0++, new AssignNode(fra, 1, f[2], Acknowledged: true)).Accepted);   // the form is fine; the state refuses
        s.StepThroughMonth(0);
        Assert.Contains(s.Outcomes, o => o.Contains("rejected: QueueFull"));
        Assert.Contains(s.Outcomes, o => o.Contains("rejected: NodeTaken"));
        Span<int> entries = stackalloc int[6];
        // Slot 1 took f1 from the queue of slot 0, and the node moved to its list (§4.1).
        Assert.Equal(5, w.SlotOrders!.Entries(new InstituteHandle(fra, 0), entries));
        Assert.Equal(f[0], entries[0]);
        Assert.Equal(1, w.SlotOrders.Entries(new InstituteHandle(fra, 1), entries));
        Assert.Equal(f[1], entries[0]);
        w.SlotOrders.Entries(new InstituteHandle(fra, 0), entries);
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
        Assert.True(s.Submit(SourceClass.Player, 0, n0++, new ReorderQueue(fra, 0, [f[2], f[5], f[4], f[3]])).Accepted);
        s.StepThroughMonth(5);
        Assert.DoesNotContain(s.Outcomes, o => o.Contains("rejected: NotInList"));
        Assert.Equal(4, w.SlotOrders.Entries(new InstituteHandle(fra, 0), entries));
        Assert.Equal(f[5], entries[1]);
    }

    [Fact]
    public void AFreeInstituteTakesAnUnstartedNodeOfAnotherQueue_AndTheNodeLeavesThatQueue_O089c_41()
    {
        SliceSession s = Make(2, player: 0);
        int[] f = Frontier(s, 0);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new AssignNode(0, 0, f[0], Acknowledged: true)).Accepted);
        Assert.True(s.Submit(SourceClass.Player, 0, 1, new AssignNode(0, 0, f[1], Acknowledged: true)).Accepted);
        s.StepThroughMonth(0);
        int[] node = Resolved(s, 0, 1);
        Assert.Equal(f[0], node[0]);
        Assert.Equal(f[1], node[1]);   // slot 1 has no list: it took the queued node of slot 0
        Span<int> entries = stackalloc int[6];
        var donor = new InstituteHandle(0, 0);
        var worker = new InstituteHandle(0, 1);
        // §4.1: the donor's queue lost it, the worker's list holds it: the donor can no longer stop the worker by unassigning, the worker can.
        Assert.Equal(1, s.World.SlotOrders!.Entries(donor, entries));
        Assert.Equal(f[0], entries[0]);
        Assert.Equal(1, s.World.SlotOrders.Entries(worker, entries));
        Assert.Equal(f[1], entries[0]);
        Assert.True(s.Submit(SourceClass.Player, 0, 2, new Unassign(0, 0, f[1])).Accepted);
        Assert.True(s.Submit(SourceClass.Player, 0, 3, new Unassign(0, 1, f[1])).Accepted);
        s.StepThroughMonth(1);
        Assert.Single(s.Outcomes, o => o.Contains("rejected: NotInList"));   // the donor's one
        Assert.Equal(0, s.World.SlotOrders.Entries(worker, entries));
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
                    Assert.True(m >= Fx6.One && m <= Fx6.One + new InstituteParams().LevelBonus * Fx6.FromInt(5), "m_inst is within 1 .. 1 + 5·bonus (S10)");
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

    // ---- TASK-0187 part 2: queries, plans, confirmation, reserve focus, research_speed, notices, surplus, AI building

    private static BudgetShares LessProcurement(SliceWorld w, int actor)
    {
        BudgetShares s = w.SharesOf(actor);
        return new BudgetShares(s.Research, s.Capability, s.Effort, s.Procurement / Fx6.FromInt(2), s.Quality, s.Security);
    }

    [Fact]
    public void FocusPaidFromTheReserve_TakesTheSurchargeFromTheReserve_AndNotFromTheResearchMoney()
    {
        SliceSession plain = Make(2, player: 0), paid = Make(2, player: 0);
        foreach (SliceSession s in new[] { plain, paid })
            Assert.True(s.Submit(SourceClass.Player, 0, 0, new SetBudgetShares(0, LessProcurement(s.World, 0))).Accepted);
        for (long t = 0; t < 6; t++)
        {
            plain.StepThroughMonth(t);
            paid.StepThroughMonth(t);
        }
        Assert.True(paid.World.Reserve!.Balance(0) > Fx6.Zero, "the reserve has money");
        int[] f = Frontier(plain, 0);
        foreach (SliceSession s in new[] { plain, paid })
            Assert.True(s.Submit(SourceClass.Player, 0, 1, new AssignNode(0, 0, f[0], Acknowledged: true)).Accepted);
        Assert.True(paid.Submit(SourceClass.Player, 0, 2, new SetFocus(0, 0, 2, true)).Accepted);
        var inputs = new KnowledgeInputsAdapter(paid.World);
        for (long t = 6; t < 14; t++)
        {
            Fx6 budget = inputs.ResearchBudget(0, t);
            plain.StepThroughMonth(t);
            paid.StepThroughMonth(t);
            Assert.Equal(budget.Raw, (paid.World.Knowledge.LastSpent(0) + paid.World.Knowledge.ReturnedToBudget(0)).Raw);   // S3: the flow balances without the reserve part
        }
        Assert.DoesNotContain(paid.Outcomes, o => o.Contains("rejected"));
        Assert.True(paid.World.Reserve.Balance(0) < plain.World.Reserve!.Balance(0), "the focus was paid from the reserve");
        int row = plain.World.Graph.RowOf(f[0]);
        Assert.True(paid.World.Knowledge.Prog(0, row) > plain.World.Knowledge.Prog(0, row), "and made the node faster");
    }

    [Fact]
    public void AStartThatDropsTheCoverageBelow60Percent_NeedsConfirmationAtSubmit_AStartWhenItIsBelowAlreadyGoesWithAWarning_O096()
    {
        SliceSession s = Make(2, player: 0);
        BudgetShares sh = s.World.SharesOf(0);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new SetBudgetShares(0, new BudgetShares(Fx6.Zero, sh.Capability, sh.Effort, sh.Procurement, sh.Quality, sh.Security))).Accepted);
        s.StepThroughMonth(0);
        int[] f = Frontier(s, 0);
        CommandVerdict asked = s.Submit(SourceClass.Player, 0, 1, new AssignNode(0, 0, f[0]));
        Assert.Equal(CommandRefusal.NeedsConfirmation, asked.Reason);   // nothing was queued
        Assert.True(s.Submit(SourceClass.Player, 0, 1, new AssignNode(0, 0, f[0], Acknowledged: true)).Accepted);
        s.StepThroughMonth(1);
        Assert.Equal(f[0], Resolved(s, 0, 2)[0]);
        // The coverage is below 60 % now: the next start only warns.
        CommandVerdict second = s.Submit(SourceClass.Player, 0, 2, new AssignNode(0, 1, f[1]));
        Assert.True(second.Accepted);
        Assert.Contains("covers only", second.Message);
    }

    [Fact]
    public void ApplyQueues_ReplacesTheListsOfTheNamedInstitutes_AndRefusesARepeatedOrAKnownNode()
    {
        SliceSession s = Make(2, player: Make().World.Scenario.ActorIndex("FRA"));
        int fra = s.World.Config.PlayerActor!.Value;
        int[] f = Frontier(s, fra);
        var plan = ImmutableArray.Create(new QueuePlan(0, [f[0], f[1]]), new QueuePlan(1, [f[2], f[3]]));
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new ApplyQueues(fra, plan)).Accepted);
        Assert.True(s.Submit(SourceClass.Player, 0, 1, new ApplyQueues(fra, [new QueuePlan(0, [f[4], f[4]])])).Accepted);   // the form is fine; the state refuses
        s.StepThroughMonth(0);
        Assert.Contains(s.Outcomes, o => o.Contains("rejected: NodeTaken"));
        Span<int> entries = stackalloc int[6];
        Assert.Equal(2, s.World.SlotOrders!.Entries(new InstituteHandle(fra, 1), entries));
        Assert.Equal(f[2], entries[0]);
        Assert.Equal(new[] { f[0], f[2] }, Resolved(s, fra, 1)[..2]);
    }

    [Fact]
    public void CancelBuild_StopsTheProject_AndTheMoneyAlreadyPaidStaysSpent()
    {
        SliceSession s = Make(2, player: Make().World.Scenario.ActorIndex("SUN"));
        int sun = s.World.Config.PlayerActor!.Value;
        InstituteSystem inst = s.World.Institutes!;
        int slot = Enumerable.Range(0, inst.Layout.SlotCount(sun)).First(k => !inst.Standing(new InstituteHandle(sun, k)));
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new BuildInstitute(sun, slot)).Accepted);
        s.StepThroughMonth(0);
        s.StepThroughMonth(1);
        Assert.True(inst.Building(new InstituteHandle(sun, slot)), "the project runs");
        Assert.True(s.Submit(SourceClass.Player, 0, 1, new CancelBuild(sun, slot)).Accepted);
        s.StepThroughMonth(2);
        s.StepThroughMonth(3);
        Assert.DoesNotContain(s.Outcomes, o => o.Contains("rejected"));
        Assert.False(inst.Building(new InstituteHandle(sun, slot)));
        Assert.Equal(Fx6.One, inst.BudgetFactor(sun, 4));   // nothing is taken from the research money any more
        Assert.True(s.Submit(SourceClass.Player, 0, 2, new CancelBuild(sun, slot)).Accepted);
        s.StepThroughMonth(4);
        Assert.Contains(s.Outcomes, o => o.Contains("rejected: BuildRefused"));
    }

    [Fact]
    public void TheQueries_ShowFitFocusPathAndALayoutTheGameDoesNotStart()
    {
        SliceSession s = Make(2, player: 0);
        s.StepThroughMonth(0);
        int[] f = Frontier(s, 0);
        ImmutableArray<InstituteFitView> fit = s.FitInstitutes(0, f[0]);
        Assert.Equal(s.World.Institutes!.Layout.SlotCount(0), fit.Length);
        Assert.All(fit.Where(x => x.Standing), x => Assert.True(x.Speed >= Fx6.One && x.Months > Fx6.Zero));
        Assert.All(fit.Where(x => x.NoOverlap), x => Assert.Equal(Fx6.One, x.Speed));
        ImmutableArray<FocusQuoteView> q = s.FocusQuote(0, f[0], 0);
        Assert.Equal(3, q.Length);
        Assert.True(q[0].Months > q[1].Months && q[1].Months > q[2].Months, "focus is faster");
        Assert.True(q[0].Price < q[1].Price && q[1].Price < q[2].Price, "and dearer for the whole node");
        // The path to a later generation is a chain of unknown nodes ending in the goal; its time is a range.
        PathView path = s.PathToGeneration(0, 3)!;
        Assert.NotEmpty(path.Nodes);
        Assert.Equal(s.World.ProductsLayout.GenApp[2], path.Nodes[^1].Node);
        Assert.True(path.MonthsMin > Fx6.Zero && path.MonthsMin <= path.MonthsMax);
        ImmutableArray<QueuePlan> plan = s.SuggestQueues(0, s.World.ProductsLayout.GenApp[2]);
        Assert.NotEmpty(plan);
        Assert.Equal(plan.SelectMany(p => p.Nodes).Count(), plan.SelectMany(p => p.Nodes).Distinct().Count());
        Assert.All(Resolved(s, 0, 1), n => Assert.True(n <= 0));   // reading started nothing
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new ApplyQueues(0, plan)).Accepted);
        s.StepThroughMonth(1);
        Assert.DoesNotContain(s.Outcomes, o => o.Contains("rejected"));
        Assert.Contains(Resolved(s, 0, 2), n => n > 0);
    }

    [Fact]
    public void TheFreeInstituteNotice_ComesAtTheStartAndAtMostOnceAYear_S12()
    {
        SliceSession s = Make(2, player: 0);
        var months = new List<long>();
        long last = -1;
        for (long t = 0; t < 40; t++)
        {
            s.StepThroughMonth(t);
            long n = s.View.Actors[0].Slots!.FreeNoticeMonth;
            if (n != last)
            {
                months.Add(n);
                last = n;
            }
        }
        Assert.Equal(0, months[0]);
        for (int i = 1; i < months.Count; i++)
            Assert.True(months[i] - months[i - 1] >= 12, "at most one notice a year");
        Assert.True(months.Count >= 3, "the player is reminded while the institutes stay free");
    }

    [Fact]
    public void AnIdleInstitute_IsNeverBetterThanAWorkingOne_S13()
    {
        SliceSession working = Make(2, player: 0), idle = Make(2, player: 0);
        int[] f = Frontier(working, 0);
        for (int i = 0; i < 6; i++)
            Assert.True(working.Submit(SourceClass.Player, 0, i, new AssignNode(0, i, f[i], Acknowledged: true)).Accepted);
        for (long t = 0; t < 60; t++)
        {
            working.StepThroughMonth(t);
            idle.StepThroughMonth(t);
            // The same research money in both: what the institutes do not ask comes back, it is not made.
            Assert.Equal((working.World.Knowledge.LastSpent(0) + working.World.Knowledge.ReturnedToBudget(0)).Raw, (idle.World.Knowledge.LastSpent(0) + idle.World.Knowledge.ReturnedToBudget(0)).Raw);
            Assert.True(idle.World.Knowledge.ReturnedToBudget(0) >= working.World.Knowledge.ReturnedToBudget(0));
        }
        Assert.True(working.World.Knowledge.KnownCount(0) > idle.World.Knowledge.KnownCount(0), "working opens nodes, idling opens none");
        // The surplus goes to procurement, never to the reserve (O-089b): the reserve of the idle actor is not larger.
        Assert.True(idle.World.Reserve!.Balance(0) <= working.World.Reserve!.Balance(0));
        Assert.Equal(Fx6.Zero, working.World.SurplusToBudget(0) - working.World.Knowledge.ReturnedToBudget(0));
    }

    [Fact]
    public void TheAiGeneralBuildingRule_NeedsMoreNodesOnThePathThanInstitutes_SoWithTheNarrowTreeItBuildsOnTheHistoricalDate()
    {
        // SCIENCE_SLOTS §8: busy for a year is not enough, the path must hold more nodes than there are institutes. With the 99 nodes the USA path is 4–5 wide against 6 institutes,
        // so the rule stays silent and DARPA is built on its historical date; it fires when the tree widens (TASK-0188).
        SliceSession s = Make(3);
        int usa = s.World.Scenario.ActorIndex("USA");
        InstituteSystem inst = s.World.Institutes!;
        var darpa = new InstituteHandle(usa, Enumerable.Range(0, inst.Layout.SlotCount(usa)).Single(k => inst.Layout.IdOf(new InstituteHandle(usa, k)) == "usa_darpa"));
        long historical = inst.Layout.FirstMonthOf(inst.Layout.RefYear(darpa)) - inst.BuildMonths(darpa);
        long started = -1;
        for (long t = 0; t < 120 && started < 0; t++)
        {
            s.StepThroughMonth(t);
            if (t == 20)
                Assert.True(inst.BusyMonths(usa) >= 12, "the busy counter runs");
            started = inst.BuildStart(darpa);
        }
        Assert.Equal(historical, started);
    }

    [Fact]
    public void TheResearchFactor_IsOnePlusTheKnownBonuses_WeightedByDirection_AndStopsAtTheCap_S9()
    {
        SliceSession s = Make(2);
        SliceWorld w = s.World;
        NodeTable nodes = w.Data.Nodes;
        Fx6 cap = new SlotParams().ResearchSpeedCap, main = new SlotParams().MainWeight;
        int above = 0;
        for (long t = 0; t < 180; t++)
        {
            s.StepThroughMonth(t);
            if (t % 30 != 29)
                continue;
            for (int a = 0; a < w.Scenario.ActorCount; a++)
            {
                for (int r = 0; r < w.Graph.Count; r++)
                {
                    // Brute force from the data: bonus per direction of the node, the main direction 0.5 and the rest sharing 0.5; an effect for all directions counts whole.
                    var dirs = w.Graph.Directions[r];
                    Fx6 all = Fx6.Zero, sum = Fx6.Zero;
                    var perDir = new Fx6[dirs.Length];
                    for (int e = 0; e < nodes.Count; e++)
                    {
                        if (!w.Knowledge.IsKnown(a, e))
                            continue;
                        for (int k = nodes.SpeedStart[e]; k < nodes.SpeedStart[e + 1]; k++)
                        {
                            if (nodes.SpeedMask[k] == ResearchSpeedEffects.AllMask)
                                all += nodes.SpeedBonus[k];
                            else
                                for (int i = 0; i < dirs.Length; i++)
                                    perDir[i] += (nodes.SpeedMask[k] & (1 << dirs[i])) != 0 ? nodes.SpeedBonus[k] : Fx6.Zero;
                        }
                    }
                    sum = all;
                    for (int i = 0; i < dirs.Length; i++)
                        sum += SlotMath.DirectionWeight(i, dirs.Length, main) * perDir[i];
                    Fx6 expected = Fx6.One + (sum > cap ? cap : sum);
                    Assert.Equal(expected, w.Knowledge.ResearchFactor(a, r));
                    above += expected > Fx6.One ? 1 : 0;
                }
            }
        }
        Assert.True(above > 0, "the speed nodes of the data act once an actor knows them");
    }

    [Fact]
    public void TheNewCommands_RoundTripThroughTheLog()
    {
        SliceCommand[] commands =
        [
            new AssignNode(1, 2, 33, true), new AssignNode(1, 2, 33), new Unassign(0, 1, 5), new ReorderQueue(0, 1, [3, 1, 2]), new SetFocus(2, 0, 2, true),
            new CancelBuild(3, 4), new ApplyQueues(0, [new QueuePlan(0, [1, 2]), new QueuePlan(3, [])]),
        ];
        for (int i = 0; i < commands.Length; i++)
        {
            var logged = new LoggedCommand(new CommandKey(7, SourceClass.Player, 0, i), commands[i]);
            LoggedCommand back = CommandCodec.Decode(CommandCodec.Encode(logged));
            Assert.Equal(CommandCodec.Encode(logged), CommandCodec.Encode(back));
            Assert.Equal(commands[i].Kind, back.Command.Kind);
        }
    }

    [Fact]
    public void ThePathHasASecondTier_ForTheGenerationAfter_OnlyInTheSlotModel()
    {
        SliceSession slots = Make(7), shares = Make(7, slots: false);
        var tierSlots = new byte[slots.World.Graph.Count];
        var tierShares = new byte[shares.World.Graph.Count];
        new InstituteInputsAdapter(slots.World).GoalPath(0, 0, tierSlots);
        new InstituteInputsAdapter(shares.World).GoalPath(0, 0, tierShares);
        Assert.Equal(tierShares.Count(t => t == 1), tierSlots.Count(t => t == 1));
        Assert.DoesNotContain(tierShares, t => t == 2);
        Assert.Contains(tierSlots, t => t == 2);
        for (int r = 0; r < tierShares.Length; r++)
        {
            if (tierShares[r] == 1)
                Assert.Equal(1, tierSlots[r]);
        }
    }

    [Fact]
    public void FocusOffThenOnBeforeSixMonths_IsFocusTooSoon_ButOffIsFreeAtAnyTime()
    {
        SliceSession s = Make(2, player: 0);
        int[] f = Frontier(s, 0);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new AssignNode(0, 0, f[0], Acknowledged: true)).Accepted);
        Assert.True(s.Submit(SourceClass.Player, 0, 1, new SetFocus(0, 0, 2, false)).Accepted);
        s.StepThroughMonth(0);
        Assert.True(s.Submit(SourceClass.Player, 0, 2, new SetFocus(0, 0, 0, false)).Accepted);   // off at once
        s.StepThroughMonth(1);
        Assert.DoesNotContain(s.Outcomes, o => o.Contains("rejected"));
        Assert.True(s.Submit(SourceClass.Player, 0, 3, new SetFocus(0, 0, 1, false)).Accepted);   // on again: the period runs from the off
        s.StepThroughMonth(2);
        Assert.Contains(s.Outcomes, o => o.Contains("rejected: FocusTooSoon"));
        for (long t = 3; t < 8; t++)
            s.StepThroughMonth(t);
        Assert.True(s.Submit(SourceClass.Player, 0, 4, new SetFocus(0, 0, 1, false)).Accepted);
        s.StepThroughMonth(8);
        Assert.Equal(1, s.World.SlotOrders!.FocusLevel(new InstituteHandle(0, 0)));
    }

    [Fact]
    public void ApplyQueues_WithAnInstituteTwice_IsRefused()
    {
        SliceSession s = Make(2, player: 0);
        int[] f = Frontier(s, 0);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new ApplyQueues(0, [new QueuePlan(0, [f[0]]), new QueuePlan(0, [f[1]])])).Accepted);   // the form is fine; the state refuses
        s.StepThroughMonth(0);
        Assert.Contains(s.Outcomes, o => o.Contains("rejected: NotInList") && o.Contains("twice"));
        Span<int> entries = stackalloc int[6];
        Assert.Equal(0, s.World.SlotOrders!.Entries(new InstituteHandle(0, 0), entries));
    }

    [Fact]
    public void TwoStartsSentInOneMonth_AreJudgedTogether_O096()
    {
        SliceSession s = Make(2, player: 0);
        SliceWorld w = s.World;
        var inputs = new KnowledgeInputsAdapter(w);
        BudgetShares sh = w.SharesOf(0);
        BudgetShares With(Fx6 research) => new(research, sh.Capability, sh.Effort, sh.Procurement, sh.Quality, sh.Security);
        // Find the research money that covers one node of the first institute exactly: start tiny, read the coverage of one start, scale up.
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new SetBudgetShares(0, With(sh.Research / Fx6.FromInt(200)))).Accepted);
        s.StepThroughMonth(0);
        int[] f = Frontier(s, 0);
        var inst = w.Institutes!;
        SlotWork Work(int slot, int number) { int row = w.Graph.RowOf(number); return new SlotWork(slot, row, inst.InstituteMultiplier(new InstituteHandle(0, slot), row), 0); }
        Fx6 s1 = inputs.ResearchBudget(0, 1), c1 = w.Knowledge.CoverageWith(0, 1, [Work(0, f[0])]);
        Assert.True(c1 < Fx6.One && c1 > Fx6.Zero);
        Fx6 ask0 = s1 / c1;
        Assert.True(s.Submit(SourceClass.Player, 0, 1, new SetBudgetShares(0, With(sh.Research / Fx6.FromInt(200) * (ask0 / s1)))).Accepted);
        s.StepThroughMonth(1);
        Fx6 one = w.Knowledge.CoverageWith(0, 2, [Work(0, f[0])]);
        Fx6 two = w.Knowledge.CoverageWith(0, 2, [Work(0, f[0]), Work(1, f[1])]);
        Assert.True(one >= new SlotParams().UnderfundedBelow && two < new SlotParams().UnderfundedBelow, $"the setup: one start covers {one}, two cover {two}");
        Assert.True(s.Submit(SourceClass.Player, 0, 2, new AssignNode(0, 0, f[0])).Accepted);
        CommandVerdict second = s.Submit(SourceClass.Player, 0, 3, new AssignNode(0, 1, f[1]));
        Assert.Equal(CommandRefusal.NeedsConfirmation, second.Reason);
    }

    [Fact]
    public void TheAirPowerIndexOfTheAi_IsReadFromAColumnRefreshedOncePerMonth_NotComputedInTheStep()
    {
        SliceSession s = Make(6);
        SliceWorld w = s.World;
        Assert.Contains(w.Table.Columns, c => c.Name == "inst.mpi");
        for (long t = 0; t < 30; t++)
        {
            for (int a = 0; a < w.Scenario.ActorCount; a++)
                Assert.Equal(w.AirPower(a, t).Raw, w.Mpi(a).Raw);   // the column holds what the fleet value of the month is
            s.StepThroughMonth(t);
        }
        Assert.Contains(Enumerable.Range(0, w.Scenario.ActorCount).Select(a => w.Mpi(a)), v => v > Fx6.Zero);
        HashAssert.NotEqual(Make(6, slots: false).LastHash, s.LastHash);
    }

    [Fact]
    public void UnassigningAFocusedNode_EndsItsFocus_ALaterAssignmentStartsWithoutIt_42()
    {
        SliceSession s = Make(2, player: 0);
        int[] f = Frontier(s, 0);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new AssignNode(0, 0, f[0], Acknowledged: true)).Accepted);
        Assert.True(s.Submit(SourceClass.Player, 0, 1, new SetFocus(0, 0, 2, true)).Accepted);
        s.StepThroughMonth(0);
        Assert.Equal(2, s.World.SlotOrders!.FocusLevel(new InstituteHandle(0, 0)));
        Assert.True(s.Submit(SourceClass.Player, 0, 2, new Unassign(0, 0, f[0])).Accepted);
        s.StepThroughMonth(1);
        Assert.Equal(0, s.World.SlotOrders.FocusLevel(new InstituteHandle(0, 0)));
        Assert.True(s.Submit(SourceClass.Player, 0, 3, new AssignNode(0, 0, f[0], Acknowledged: true)).Accepted);
        s.StepThroughMonth(2);
        var work = new SlotWork[16];
        int n = s.World.Institutes!.Work(0, 3, work);
        Assert.Contains(work[..n], w => w.NodeRow == s.World.Graph.RowOf(f[0]));
        Assert.All(work[..n], w => Assert.Equal(0, w.Focus));
        Assert.All(work[..n], w => Assert.False(w.FromReserve));
    }

    [Fact]
    public void AResearchShareCutSentBeforeAnUnacknowledgedStart_ChangesTheJudgement_ThePlanIsRecheckedAtApplication_43()
    {
        SliceSession s = Make(2, player: 0);
        BudgetShares sh = s.World.SharesOf(0);
        int[] f = Frontier(s, 0);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new SetBudgetShares(0, new BudgetShares(sh.Research / Fx6.FromInt(500), sh.Capability, sh.Effort, sh.Procurement, sh.Quality, sh.Security))).Accepted);
        // With the old budget the start covers everything, so Submit lets it through; the applied cut would take it below 60 %.
        Assert.True(s.Submit(SourceClass.Player, 0, 1, new AssignNode(0, 0, f[0])).Accepted);
        s.StepThroughMonth(0);
        Assert.Contains(s.Outcomes, o => o.Contains("rejected: NeedsConfirmation"));
        Span<int> entries = stackalloc int[6];
        Assert.Equal(0, s.World.SlotOrders!.Entries(new InstituteHandle(0, 0), entries));
    }

    [Fact]
    public void ResearchMoneyIsNeverAboveTheShareMinusProjectsAndUpkeep_NoCapacityBonus_S3_44()
    {
        SliceSession s = Make(3);
        SliceWorld w = s.World;
        var inputs = new KnowledgeInputsAdapter(w);
        bool built = false;
        for (long t = 0; t < 175; t++)
        {
            for (int a = 0; a < w.Scenario.ActorCount; a++)
            {
                Assert.True(w.Institutes!.BudgetFactor(a, t) <= Fx6.One, $"month {t} actor {a}: the research money is above the share");
                Fx6 share = w.SharesOf(a).Research * w.Policy.Budget(a);
                Assert.True(inputs.ResearchBudget(a, t) <= share, $"month {t} actor {a}");
                for (int k = 0; k < w.Institutes.Layout.SlotCount(a); k++)
                    built |= w.Institutes.BuiltByProject(new InstituteHandle(a, k));
            }
            s.StepThroughMonth(t);
        }
        Assert.True(built, "an institute built by a project stood in the run, so a capacity bonus would have shown");
    }

    private static List<int> FreshFrontier(SliceSession s, int actor, Random rnd, int count)
    {
        List<int> f = [.. Frontier(s, actor)];
        var picked = new List<int>();
        while (picked.Count < count && f.Count > 0)
        {
            int i = rnd.Next(f.Count);
            picked.Add(f[i]);
            f.RemoveAt(i);
        }
        return picked;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void IdlingIsNeverBetterThanWorking_RandomStates_ConservationOfTheCommonBudget_S13(int seed)
    {
        var rnd = new Random(seed);
        int actor = rnd.Next(2) == 0 ? 0 : Make().World.Scenario.ActorIndex("SUN");
        SliceSession working = Make((ulong)seed, player: actor), idle = Make((ulong)seed, player: actor);
        int slots = working.World.Institutes!.Layout.StartingCount(actor);
        List<int> nodes = FreshFrontier(working, actor, rnd, 1 + rnd.Next(slots));
        for (int i = 0; i < nodes.Count; i++)
            Assert.True(working.Submit(SourceClass.Player, 0, i, new AssignNode(actor, i, nodes[i], Acknowledged: true)).Accepted);
        Fx6 gain = Fx6.Zero;
        for (long t = 0; t < 36; t++)
        {
            working.StepThroughMonth(t);
            idle.StepThroughMonth(t);
            KnowledgeSystem kw = working.World.Knowledge, ki = idle.World.Knowledge;
            // The same research money; what is not asked comes back to the common budget, nothing is made or lost.
            Assert.Equal((kw.LastSpent(actor) + kw.ReturnedToBudget(actor)).Raw, (ki.LastSpent(actor) + ki.ReturnedToBudget(actor)).Raw);
            // The idle actor's procurement is larger by exactly what the working one spent on research this month.
            Fx6 extra = idle.World.ProcurementBase(actor) - working.World.ProcurementBase(actor);
            Assert.Equal((kw.LastSpent(actor) - ki.LastSpent(actor)).Raw, extra.Raw);
            gain += extra;
        }
        Fx6 progress(SliceSession s) { Fx6 p = Fx6.Zero; for (int r = 0; r < s.World.Graph.Count; r++) p += s.World.Knowledge.Prog(actor, r); return p; }
        Assert.True(progress(working) > progress(idle), "what the money was spent on: progress the idle institutes do not have");
        Assert.True(working.World.Knowledge.KnownCount(actor) >= idle.World.Knowledge.KnownCount(actor));
        Assert.True(gain >= Fx6.Zero);
    }

    [Fact]
    public void ConflictingCommandsOfOneMonth_AreOrderedByTheirKeys_NotByArrival_S14()
    {
        SliceSession first = Make(2, player: 0), second = Make(2, player: 0);
        int[] f = Frontier(first, 0);
        // The same two commands on one node, sent in opposite orders of arrival: the key (source id before local number) decides, the first key wins.
        Assert.True(first.Submit(SourceClass.Player, 1, 0, new AssignNode(0, 1, f[0], Acknowledged: true)).Accepted);
        Assert.True(first.Submit(SourceClass.Player, 0, 0, new AssignNode(0, 0, f[0], Acknowledged: true)).Accepted);
        Assert.True(second.Submit(SourceClass.Player, 0, 0, new AssignNode(0, 0, f[0], Acknowledged: true)).Accepted);
        Assert.True(second.Submit(SourceClass.Player, 1, 0, new AssignNode(0, 1, f[0], Acknowledged: true)).Accepted);
        first.StepThroughMonth(0);
        second.StepThroughMonth(0);
        HashAssert.Equal(first.LastHash, second.LastHash);
        Assert.Equal(f[0], Resolved(first, 0, 1)[0]);
        Assert.Contains(first.Outcomes, o => o.Contains("rejected: NodeTaken"));
        // Turned round (the other slot has the first key), the other slot wins.
        SliceSession turned = Make(2, player: 0);
        Assert.True(turned.Submit(SourceClass.Player, 0, 0, new AssignNode(0, 1, f[0], Acknowledged: true)).Accepted);
        Assert.True(turned.Submit(SourceClass.Player, 1, 0, new AssignNode(0, 0, f[0], Acknowledged: true)).Accepted);
        turned.StepThroughMonth(0);
        Assert.Equal(f[0], Resolved(turned, 0, 1)[1]);
    }

    [Fact]
    public void ThePlayersListsAndFocus_SurviveSaveAndLoad_AndContinueIdentically_S6()
    {
        SliceSession one = Make(7, player: 0), loaded = Make(7, player: 0);
        int[] f = Frontier(one, 0);
        for (int i = 0; i < 4; i++)
            Assert.True(one.Submit(SourceClass.Player, 0, i, new AssignNode(0, 0, f[i], Acknowledged: true)).Accepted);
        Assert.True(one.Submit(SourceClass.Player, 0, 4, new SetFocus(0, 0, 2, true)).Accepted);
        for (long t = 0; t < 20; t++)
            one.StepThroughMonth(t);
        using var snapshot = new MemoryStream();
        one.WriteSnapshot(snapshot);
        loaded.LoadSnapshot(new MemoryStream(snapshot.ToArray()));
        Span<int> a = stackalloc int[6], b = stackalloc int[6];
        for (int k = 0; k < 3; k++)
        {
            int na = one.World.SlotOrders!.Entries(new InstituteHandle(0, k), a), nb = loaded.World.SlotOrders!.Entries(new InstituteHandle(0, k), b);
            Assert.Equal(na, nb);
            Assert.True(a[..na].SequenceEqual(b[..nb]));
            Assert.Equal(one.World.SlotOrders.FocusLevel(new InstituteHandle(0, k)), loaded.World.SlotOrders.FocusLevel(new InstituteHandle(0, k)));
        }
        for (long t = 20; t < 40; t++)
        {
            one.StepThroughMonth(t);
            loaded.StepThroughMonth(t);
            HashAssert.Equal(one.LastHash, loaded.LastHash);
        }
    }

    [Fact]
    public void ThePlannerQueries_OfAnotherActor_AreHiddenWithIntelligence_47()
    {
        SliceSession s = Make(2, player: 0);
        s.StepThroughMonth(0);
        Assert.True(s.World.Intel is not null && s.World.Config.PlayerActor == 0);
        int goal = s.World.ProductsLayout.GenApp[2];
        int[] f = Frontier(s, 1);
        Assert.Empty(s.FitInstitutes(1, f[0]));
        Assert.Empty(s.FocusQuote(1, f[0], 0));
        Assert.Null(s.PathToGoal(1, goal));
        Assert.Empty(s.SuggestQueues(1, goal));
        Assert.NotEmpty(s.FitInstitutes(0, Frontier(s, 0)[0]));
        Assert.NotNull(s.PathToGoal(0, goal));
    }

    [Fact]
    public void ThePickOfAnAlternativeGroup_IsTheQuickestMember_EvenWhenItsNumberIsHigher_ATieGoesToTheLowerNumber_48()
    {
        int[] numbers = [5, 9, 7];
        Fx6 F(int v) => Fx6.FromInt(v);
        // The old rule kept the lower number unless the higher one was both lower in number and no slower: a faster, higher-numbered member could never win.
        Assert.Equal(1, SlotPlanner.PickQuickest([0, 1], m => m == 0 ? F(4) : F(2), m => numbers[m]));
        Assert.Equal(2, SlotPlanner.PickQuickest([0, 1, 2], m => F(new[] { 3, 2, 2 }[m]), m => numbers[m]));   // a tie between members 1 and 2: the lower number (7)
        Assert.Equal(0, SlotPlanner.PickQuickest([0, 1], m => F(2), m => numbers[m]));
    }

    [Fact]
    public void TheClosureOfAGoal_TakesTheQuickestMemberOfEveryUnknownAlternativeGroup_48()
    {
        SliceSession s = Make(2, player: 0);
        SliceWorld w = s.World;
        var req = w.Graph.Requires;
        int checkedGroups = 0;
        for (int r = 0; r < w.Graph.Count; r++)
        {
            for (int g = req.GroupStart[r]; g < req.GroupStart[r + 1]; g++)
            {
                int[] members = [.. req.GroupMembers(g).ToArray()];
                if (members.Length < 2 || members.Any(m => w.Knowledge.IsKnown(0, m)))
                    continue;
                int quickest = SlotPlanner.PickQuickest(members, m => w.Knowledge.EffectiveMonths(0, m, 1), m => w.Graph.Number[m]);
                Assert.Contains(quickest, SlotPlanner.Closure(w, 0, r, 1));
                checkedGroups++;
            }
        }
        Assert.True(checkedGroups > 0, "the data has groups of alternatives");
    }

    [Fact]
    public void OneCreditNeverGivesAnInstituteAFifthDirection_S10_49()
    {
        SliceSession s = Make(1, player: 0);
        SliceWorld w = s.World;
        InstituteSystem inst = w.Institutes!;
        InstituteLayout l = inst.Layout;
        // An institute with three directions and a node that has two directions it lacks: put both just below their first level, with a fraction that tips over at the first credit.
        for (int k = 0; k < l.StartingCount(0); k++)
        {
            var h = new InstituteHandle(0, k);
            if (inst.DirectionCount(h) != 3)
                continue;
            foreach (int number in Frontier(s, 0))
            {
                int row = w.Graph.RowOf(number);
                int[] missing = [.. w.Graph.Directions[row].Where(d => inst.DirLevel(h, d) == 0)];
                if (missing.Length < 2)
                    continue;
                foreach (int d in missing.Take(2))
                {
                    int[] dx = w.Table.GetInt($"inst.dx.{k}.{l.DirectionNames[d]}").Current.ToArray();
                    dx[0] = InstituteMath.Threshold(1, l.DirNodesIn[d], inst.Parameters) - 1;
                    w.Table.GetInt($"inst.dx.{k}.{l.DirectionNames[d]}").Load(dx);
                    long[] xf = w.Table.GetLong($"inst.xf.{k}.{l.DirectionNames[d]}").Current.ToArray();
                    xf[0] = Fx6.One.Raw - 1;
                    w.Table.GetLong($"inst.xf.{k}.{l.DirectionNames[d]}").Load(xf);
                }
                Assert.Equal(3, inst.DirectionCount(h));
                Assert.True(s.Submit(SourceClass.Player, 0, 0, new AssignNode(0, k, number, Acknowledged: true)).Accepted);
                for (long t = 0; t < 4; t++)
                    s.StepThroughMonth(t);
                Assert.Equal(4, inst.DirectionCount(h));   // one of the two crossed, the other is held back at the limit
                return;
            }
        }
        Assert.Fail("no institute with three directions and a node with two directions it lacks");
    }

    [Fact]
    public void TheAdviserFillsTheFreeInstitutesOfThePlayer_AndWithoutItNothingIsResearched_O099()
    {
        SliceSession idle = Make(2, player: 0), adviser = Make(2, player: 0);
        int standing = Enumerable.Range(0, idle.World.Institutes!.Layout.SlotCount(0)).Count(k => idle.World.Institutes.Standing(new InstituteHandle(0, k)));
        ImmutableArray<AssignNode> starts = adviser.AdviserStarts(0);
        Assert.Equal(standing, starts.Length);
        Assert.Equal(starts.Length, starts.Select(x => x.Node).Distinct().Count());
        Assert.Empty(adviser.AdviserStarts(1));   // not the player's actor
        for (int i = 0; i < starts.Length; i++)
            Assert.True(adviser.Submit(SourceClass.Player, 0, i, starts[i]).Accepted);
        for (long t = 0; t < 12; t++)
        {
            idle.StepThroughMonth(t);
            adviser.StepThroughMonth(t);
        }
        Assert.DoesNotContain(adviser.Outcomes, o => o.Contains("rejected"));
        Fx6 Progress0(SliceSession s) { Fx6 p = Fx6.Zero; for (int r = 0; r < s.World.Graph.Count; r++) p += s.World.Knowledge.Prog(0, r); return p; }
        Assert.Equal(Fx6.Zero, Progress0(idle));   // the lists of the player start empty: no start, no research
        Assert.True(Progress0(adviser) > Fx6.Zero);
        Assert.Empty(adviser.AdviserStarts(0).Where(x => Resolved(adviser, 0, 13)[x.Slot] != 0));   // nothing is proposed for a busy institute
    }

    [Fact]
    public void AnInstituteWithAFullBlockedList_DoesNotBorrowASeventhNode_S8_52()
    {
        SliceSession s = Make(2, player: 0);
        SliceWorld w = s.World;
        int[] frontier = Frontier(s, 0);
        int[] blocked = [.. Enumerable.Range(0, w.Graph.Count).Where(r => !w.Knowledge.IsKnown(0, r) && !w.Knowledge.InFrontier(0, r)).Select(r => w.Graph.Number[r]).Take(6)];
        Assert.Equal(6, blocked.Length);
        for (int i = 0; i < 6; i++)
            Assert.True(s.Submit(SourceClass.Player, 0, i, new AssignNode(0, 1, blocked[i], Acknowledged: true)).Accepted);
        Assert.True(s.Submit(SourceClass.Player, 0, 6, new AssignNode(0, 0, frontier[0], Acknowledged: true)).Accepted);
        Assert.True(s.Submit(SourceClass.Player, 0, 7, new AssignNode(0, 0, frontier[1], Acknowledged: true)).Accepted);
        s.StepThroughMonth(0);
        Assert.DoesNotContain(s.Outcomes, o => o.Contains("rejected"));
        int[] node = Resolved(s, 0, 1);
        Assert.Equal(0, node[1]);   // slot 1 is full and blocked: it stays free instead of taking a seventh node
        Span<int> entries = stackalloc int[8];
        for (int k = 0; k < w.Institutes!.Layout.StartingCount(0); k++)
            Assert.InRange(w.SlotOrders!.Entries(new InstituteHandle(0, k), entries), 0, 6);
        Assert.Equal(6, w.SlotOrders!.Entries(new InstituteHandle(0, 1), entries));
        Assert.Equal(frontier[1], node.Skip(2).First(n => n > 0));   // another free institute took the queued node, and it moved into that institute's list
    }

    private static void CalibrateOneStartBut_NotTwo(SliceSession s, out int[] f)
    {
        SliceWorld w = s.World;
        var inputs = new KnowledgeInputsAdapter(w);
        BudgetShares sh = w.SharesOf(0);
        BudgetShares With(Fx6 research) => new(research, sh.Capability, sh.Effort, sh.Procurement, sh.Quality, sh.Security);
        Assert.True(s.Submit(SourceClass.Player, 0, 100, new SetBudgetShares(0, With(sh.Research / Fx6.FromInt(200)))).Accepted);
        s.StepThroughMonth(0);
        f = Frontier(s, 0);
        InstituteSystem inst = w.Institutes!;
        SlotWork Work(int slot, int number) { int row = w.Graph.RowOf(number); return new SlotWork(slot, row, inst.InstituteMultiplier(new InstituteHandle(0, slot), row), 0); }
        Fx6 s1 = inputs.ResearchBudget(0, 1), c1 = w.Knowledge.CoverageWith(0, 1, [Work(0, f[0])]);
        Fx6 ask0 = s1 / c1;
        Assert.True(s.Submit(SourceClass.Player, 0, 101, new SetBudgetShares(0, With(sh.Research / Fx6.FromInt(200) * (ask0 / s1)))).Accepted);
        s.StepThroughMonth(1);
        Assert.True(w.Knowledge.CoverageWith(0, 2, [Work(0, f[0])]) >= new SlotParams().UnderfundedBelow && w.Knowledge.CoverageWith(0, 2, [Work(0, f[0]), Work(1, f[1])]) < new SlotParams().UnderfundedBelow);
    }

    [Fact]
    public void TheFinalStateOfTwoStartsThatTogetherCrossTheLine_DoesNotDependOnArrival_S14_53()
    {
        SliceSession ab = Make(2, player: 0), ba = Make(2, player: 0);
        CalibrateOneStartBut_NotTwo(ab, out int[] f);
        CalibrateOneStartBut_NotTwo(ba, out _);
        // Fixed keys: id 0 (slot 0) sorts before id 1 (slot 1). Neither command acknowledges.
        var a = new AssignNode(0, 0, f[0]);
        var b = new AssignNode(0, 1, f[1]);
        Assert.True(ab.Submit(SourceClass.Player, 0, 200, a).Accepted);
        ab.Submit(SourceClass.Player, 1, 0, b);   // refused here or at application, depending on who arrives first
        ba.Submit(SourceClass.Player, 1, 0, b);
        Assert.True(ba.Submit(SourceClass.Player, 0, 200, a).Accepted);
        ab.StepThroughMonth(2);
        ba.StepThroughMonth(2);
        HashAssert.Equal(ab.LastHash, ba.LastHash);
        Assert.Equal(f[0], Resolved(ab, 0, 3)[0]);     // the first key starts
        Assert.NotEqual(f[1], Resolved(ab, 0, 3)[1]);  // the second would take the coverage below 60 %: it waits for a confirmation, in either order of arrival
        Assert.Equal(f[0], Resolved(ba, 0, 3)[0]);
        Assert.NotEqual(f[1], Resolved(ba, 0, 3)[1]);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(11)]
    [InlineData(13)]
    [InlineData(29)]
    [InlineData(101)]
    public void AReserveFocusThatTheReserveCoversOnlyInRawUnits_TakesExactlyWhatLeavesTheResearchBill_S3_55(long rawBalance)
    {
        SliceSession flow = Make(2, player: 0), reserve = Make(2, player: 0);
        int[] f = Frontier(flow, 0);
        foreach (SliceSession s in new[] { flow, reserve })
        {
            Assert.True(s.Submit(SourceClass.Player, 0, 0, new AssignNode(0, 0, f[0], Acknowledged: true)).Accepted);
            Assert.True(s.Submit(SourceClass.Player, 0, 1, new SetFocus(0, 0, 2, s == reserve)).Accepted);
            s.StepThroughMonth(0);
            // The reserve holds a few raw units only: the share of the surcharge it can pay rounds slot by slot.
            long[] balance = s.World.Table.GetLong("res.balance").Current.ToArray();
            balance[0] = rawBalance;
            s.World.Table.GetLong("res.balance").Load(balance);
        }
        Fx6 debit = reserve.World.Knowledge.ProgrammeSpend(0, 1);
        Assert.True(debit > Fx6.Zero && debit.Raw <= rawBalance);
        flow.StepThroughMonth(1);
        reserve.StepThroughMonth(1);
        Assert.Equal(Fx6.One, reserve.World.Knowledge.Coverage(0));
        Assert.Equal(Fx6.One, flow.World.Knowledge.Coverage(0));
        // What the reserve pays is what the research bill lost, to the last raw unit.
        Assert.Equal(debit.Raw, (flow.World.Knowledge.LastSpent(0) - reserve.World.Knowledge.LastSpent(0)).Raw);
    }

    [Fact]
    public void TheFreeNoticeThrottle_LetsOneNoticeThroughPer25SecondsOfRealTime_S12()
    {
        var t = new FreeNoticeThrottle();
        Assert.False(t.ShouldPause(-1, 0));            // no notice
        Assert.True(t.ShouldPause(0, 1_000));          // the first notice at the start
        Assert.False(t.ShouldPause(0, 40_000));        // the same notice again
        Assert.False(t.ShouldPause(12, 20_000));       // a new one, 19 s later: dropped
        Assert.True(t.ShouldPause(24, 26_001));        // a new one after 25 s
        Assert.False(t.ShouldPause(36, 40_000));
        Assert.True(t.ShouldPause(48, 51_001));
    }

    [Fact]
    public void ThePathMinimum_IsTheLongestChain_NotTheWaitForAFreeInstitute_56()
    {
        Fx6 F(int v) => Fx6.FromInt(v);
        // A(10) and B(10) fill two institutes, C(1) waits for one, D(1) needs C: the chain C-D is 2 months, the greedy schedule ends at 12.
        (Fx6 min, Fx6 max, int end) = SlotPlanner.Schedule([F(10), F(10), F(1), F(1)], [[], [], [], [2]], 2);
        Assert.Equal(F(12), max);
        Assert.Equal(F(11), min);   // the 22 months of work over two institutes, above the longest chain (10)
        Assert.True(end is 0 or 1, "the longest chain is a single 10-month node");
        Assert.True(min < max, "the range does not collapse to the schedule");
        (Fx6 m2, _, _) = SlotPlanner.Schedule([F(10), F(1), F(1)], [[], [], [1]], 3);
        Assert.Equal(F(10), m2);
    }
}
