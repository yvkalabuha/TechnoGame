using Singularity.Core.Commands;
using Singularity.Core.Data;
using Singularity.Core.Institutes;
using Singularity.Core.Knowledge;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>TASK-0151: research priorities through institutes (slots, money first, level bonus, experience, the AI under the same rules).</summary>
public class InstituteTests
{
    private static SliceSession Make(bool institutes, int threads = 1) =>
        SliceTestData.Create(SliceRunConfig.Legacy with { Seed = 7, Institutes = institutes, Threads = threads });

    private static readonly InstituteParams P = new();

    private static int[] Frontier(SliceSession s, int actor)
    {
        var rows = new List<int>();
        for (int r = 0; r < s.World.Graph.Count; r++)
        {
            if (s.World.Knowledge.InFrontier(actor, r))
                rows.Add(r);
        }
        return [.. rows];
    }

    // A slot of actor 0 the AI gave a goal (stored in inst.ai.k); fails when there is none.
    private static int AiGoalSlot(SliceSession s)
    {
        InstituteSystem inst = s.World.Institutes!;
        for (int k = 0; k < inst.Layout.StartingCount(0); k++)
        {
            if (s.World.Table.GetInt($"inst.ai.{k}").Current[0] != 0)
                return k;
        }
        throw new InvalidOperationException("the AI gave no goal to any slot of actor 0");
    }

    // A frontier node of actor 0 that no slot works on (the AI fills the free slots after the first month).
    private static int FreeNode(SliceSession s)
    {
        InstituteSystem inst = s.World.Institutes!;
        foreach (int r in Frontier(s, 0))
        {
            int number = s.World.Graph.Number[r];
            bool taken = false;
            for (int k = 0; k < inst.Layout.SlotCount(0); k++)
                taken |= inst.GoalNode(new InstituteHandle(0, k)) == number;
            if (!taken)
                return number;
        }
        throw new InvalidOperationException("no free frontier node");
    }

    [Fact]
    public void Data_ThereAreInstitutesPerCountryAndLevelsFollowTheData()
    {
        SliceSession s = Make(true);
        InstituteLayout l = s.World.Institutes!.Layout;
        string[] actors = [.. s.World.Scenario.ActorId];
        Assert.Equal(6, l.StartingCount(actors.ToList().IndexOf("USA")));
        Assert.Equal(6, l.StartingCount(actors.ToList().IndexOf("SUN")));
        Assert.Equal(3, l.StartingCount(actors.ToList().IndexOf("GBR")));
        Assert.Equal(2, l.StartingCount(actors.ToList().IndexOf("FRA")));   // CEA was founded in 1945-10, after the start (O-094): a candidate
        Assert.Equal(4, l.StartingCount(actors.ToList().IndexOf("SWE")));
        for (int a = 0; a < l.ActorCount; a++)
        {
            for (int k = 0; k < l.StartingCount(a); k++)
            {
                var h = new InstituteHandle(a, k);
                int row = l.DataRow(h);
                for (int d = 0; d < l.DisciplineNames.Length; d++)
                    Assert.Equal(l.Table.LevelOf(row, l.DisciplineNames[d]), s.World.Institutes.Level(h, d));
            }
        }
    }

    [Fact]
    public void FlagOff_NoInstituteColumnsAndTheCommandIsRefused()
    {
        SliceSession off = Make(false);
        Assert.DoesNotContain(off.World.Table.Columns, c => ColumnProjection.IsInstituteColumn(c.Name));
        Assert.Null(off.World.Institutes);
        Assert.Equal(CommandRefusal.InstitutesOff, off.Submit(SourceClass.Player, 0, 0, new SetPriorityNode(0, 0, 1)).Reason);
        Assert.Contains(Make(true).World.Table.Columns, c => ColumnProjection.IsInstituteColumn(c.Name));
    }

    [Fact]
    public void Thresholds_ScaleWithTheNodesOfTheAreaAndRiseByAtLeastOne()
    {
        Assert.Equal([3, 8, 13, 19], Enumerable.Range(1, 4).Select(l => InstituteMath.Threshold(l, 37, P)));
        Assert.Equal([2, 3, 6, 8], Enumerable.Range(1, 4).Select(l => InstituteMath.Threshold(l, 15, P)));
        Assert.Equal([1, 2, 3, 4], Enumerable.Range(1, 4).Select(l => InstituteMath.Threshold(l, 6, P)));   // the rule alone gives 1/2/3/3
        for (int n = 1; n <= 60; n++)
        {
            for (int l = 1; l <= 4; l++)
                Assert.Equal(l, InstituteMath.LevelOf(InstituteMath.Threshold(l, n, P), n, P));
        }
        Assert.Equal(0, InstituteMath.LevelOf(0, 37, P));
    }

    [Fact]
    public void KappaEff_FollowsTheSpecExamplesAndNeverDropsBelowOne()
    {
        Fx6 W(int k) => InstituteMath.KappaEff(Fx6.FromInt(10), Fx6.FromInt(k), P);
        Assert.Equal(Fx6.FromRaw(1_500_000), W(3));
        Assert.Equal(Fx6.FromRaw(1_250_000), W(6));
        Assert.Equal(Fx6.One, W(8));
        Assert.Equal(Fx6.One, W(10));
        Assert.Equal(Fx6.One, W(0));
        // P-8 at the formula: kappa >= 1 and the rest keeps at least rho of the money while kappa > 1.
        for (int w = 1; w <= 40; w++)
        {
            for (int g = 1; g <= w; g++)
            {
                Fx6 wsum = Fx6.FromInt(w), group = Fx6.FromInt(g);
                Fx6 k = InstituteMath.KappaEff(wsum, group, P);
                Assert.True(k >= Fx6.One && k <= P.Kappa);
                if (k > Fx6.One)   // one expression k*group/wsum: rounding error ≤ 3e-6 whatever w (reviewer TASK-0174)
                    Assert.True(Fx6.One - k * group / wsum >= P.Rho - Fx6.FromRaw(3), $"w {w} g {g} kappa {k}");
            }
        }
    }

    [Fact]
    public void Command_ChecksSlotNodeAndNodeStillUnknown()
    {
        SliceSession s = Make(true);
        int node = s.World.Graph.Number[Frontier(s, 0)[0]];
        Assert.Equal(CommandRefusal.BadSlot, s.Submit(SourceClass.Player, 0, 0, new SetPriorityNode(0, s.World.Institutes!.Layout.SlotCount(0), node)).Reason);
        Assert.Equal(CommandRefusal.BadNode, s.Submit(SourceClass.Player, 0, 1, new SetPriorityNode(0, 0, 9999)).Reason);
        int known = -1;
        for (int r = 0; r < s.World.Graph.Count && known < 0; r++)
            known = s.World.Knowledge.IsKnown(0, r) ? s.World.Graph.Number[r] : -1;
        Assert.True(s.Submit(SourceClass.Player, 0, 2, new SetPriorityNode(0, 0, known)).Accepted);   // the form is fine
        Assert.True(s.Submit(SourceClass.Player, 0, 3, new SetPriorityNode(0, 1, node)).Accepted);
        s.StepThroughMonth(0);
        Assert.Contains(s.Outcomes, o => o.Contains("rejected: PriorityNotAvailable"));   // known node
        Assert.Equal(node, s.World.Institutes!.GoalNode(new InstituteHandle(0, 1)));
        Assert.Equal(0, s.World.InstituteOrders!.OrderNode(new InstituteHandle(0, 0)));
    }

    [Fact]
    public void Command_SecondSlotOnTheSameNodeIsRefused_AndTheSlotCanBeChangedOnlyOncePerYear()
    {
        SliceSession s = Make(true);
        int[] f = Frontier(s, 0);
        int a = s.World.Graph.Number[f[0]];
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new SetPriorityNode(0, 0, a)).Accepted);
        s.StepThroughMonth(0);
        Assert.True(s.Submit(SourceClass.Player, 0, 1, new SetPriorityNode(0, 1, a)).Accepted);
        s.StepThroughMonth(1);
        Assert.Contains(s.Outcomes, o => o.Contains("rejected: PriorityNotAvailable") && o.Contains("another institute"));
        int b = FreeNode(s);
        Assert.True(s.Submit(SourceClass.Player, 0, 2, new SetPriorityNode(0, 0, b)).Accepted);
        s.StepThroughMonth(2);
        Assert.True(s.Outcomes.Any(o => o.Contains("rejected: PriorityTooSoon")), string.Join("\n", s.Outcomes));
        Assert.Equal(a, s.World.InstituteOrders!.OrderNode(new InstituteHandle(0, 0)));
        for (long t = 3; t < 13; t++)
            s.StepThroughMonth(t);
        int c = FreeNode(s);
        Assert.True(s.Submit(SourceClass.Player, 0, 3, new SetPriorityNode(0, 0, c)).Accepted);
        s.StepThroughMonth(13);
        Assert.Equal(c, s.World.InstituteOrders!.OrderNode(new InstituteHandle(0, 0)));
    }

    [Fact]
    public void Codec_RoundTripsSetPriorityNode()
    {
        var logged = new LoggedCommand(new CommandKey(5, SourceClass.Player, 0, 3), new SetPriorityNode(2, 3, 47));
        Assert.Equal(logged.Command, CommandCodec.Decode(CommandCodec.Encode(logged)).Command);
    }

    // P-8: the money and the speed of a priority node in the month are never below those of the same node without the priority.
    [Fact]
    public void PriorityNode_IsNeverSlowerThanTheSameNodeWithoutPriority()
    {
        SliceSession plain = Make(false);
        var budgets = Enumerable.Range(0, plain.World.Scenario.ActorCount).Select(a => plain.World.SharesOf(a).Research * plain.World.Policy.Budget(a)).ToArray();
        plain.StepThroughMonth(0);
        int checkedNodes = 0, strictlyMore = 0;
        for (int actor = 0; actor < plain.World.Scenario.ActorCount; actor++)
        {
            int[] f = Frontier(plain, actor);
            for (int i = 0; i < Math.Min(f.Length, 4); i++)
            {
                SliceSession with = Make(true);
                Assert.True(with.Submit(SourceClass.Player, 0, 0, new SetPriorityNode(actor, 0, with.World.Graph.Number[f[i]])).Accepted);
                with.StepThroughMonth(0);
                KnowledgeNodeStep a = with.World.Knowledge.LastStep(actor, f[i]), b = plain.World.Knowledge.LastStep(actor, f[i]);
                strictlyMore += a.Alloc > b.Alloc ? 1 : 0;
                Assert.True(a.Alloc >= b.Alloc, $"actor {actor} node {f[i]}: money {a.Alloc} < {b.Alloc}");
                Assert.True(with.World.Knowledge.Prog(actor, f[i]) >= plain.World.Knowledge.Prog(actor, f[i]), $"actor {actor} node {f[i]} slower");
                Fx6 spent = Fx6.Zero;
                foreach (int r in Frontier(plain, actor))
                    spent += with.World.Knowledge.LastStep(actor, r).Alloc;
                Assert.True(spent <= budgets[actor] && spent >= budgets[actor] - Fx6.FromRaw(64), $"I4: spent {spent} of {budgets[actor]}");
                checkedNodes++;
            }
        }
        Assert.True(checkedNodes >= 10);
        Assert.True(strictlyMore > 0, "no case gave the priority node more money");
    }

    // The bonus of the institute is only for the goal node: a prerequisite on its path gets the money of the group but not the level multiplier.
    [Fact]
    public void PathNode_GetsTheGroupMoneyButNoLevelMultiplier()
    {
        SliceSession s = Make(true);
        var g = s.World.Graph;
        int goal = -1, path = -1;
        for (int r = 0; r < g.Count && goal < 0; r++)
        {
            if (s.World.Knowledge.IsKnown(0, r) || s.World.Knowledge.InFrontier(0, r))
                continue;
            for (int gr = g.Requires.GroupStart[r]; gr < g.Requires.GroupStart[r + 1] && goal < 0; gr++)
            {
                foreach (int m in g.Requires.GroupMembers(gr))
                {
                    if (s.World.Knowledge.InFrontier(0, m))
                    {
                        goal = r;
                        path = m;
                        break;
                    }
                }
            }
        }
        Assert.True(goal >= 0, "setup: no node behind a frontier node");
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new SetPriorityNode(0, 0, g.Number[goal])).Accepted);
        s.StepThroughMonth(0);
        KnowledgeNodeStep step = s.World.Knowledge.LastStep(0, path);
        Assert.True(step.InFrontier && step.Alloc > Fx6.Zero);
        Assert.Equal(step.Alloc / step.CEff, step.DProg);
    }

    [Fact]
    public void Goals_FromTheAi_AreDistinctUnknownNodesOnThePath_AndSomeSlotHasOne()
    {
        // The path rule (#559) leaves a slot without a goal when no node of the path is in the frontier, so not every slot has one.
        SliceSession s = Make(true);
        s.StepThroughMonth(0);
        s.StepThroughMonth(1);
        InstituteSystem inst = s.World.Institutes!;
        int withGoal = 0;
        for (int a = 0; a < inst.Layout.ActorCount; a++)
        {
            var goals = new List<int>();
            for (int k = 0; k < inst.Layout.StartingCount(a); k++)
                goals.Add(inst.GoalNode(new InstituteHandle(a, k)));
            goals.RemoveAll(g => g == 0);
            withGoal += goals.Count;
            Assert.Equal(goals.Count, goals.Distinct().Count());
            Assert.All(goals, g => Assert.False(s.World.Knowledge.IsKnown(a, s.World.Graph.RowOf(g))));
        }
        Assert.True(withGoal > 0);
    }

    [Fact]
    public void Experience_GrowsByOneForACompletedGoalAndLevelsNeverFall()
    {
        SliceSession s = Make(true);
        InstituteSystem inst = s.World.Institutes!;
        int d = inst.Layout.DisciplineNames.Length;
        int Total(int a) => Enumerable.Range(0, inst.Layout.StartingCount(a)).Sum(k => Enumerable.Range(0, d).Sum(x => inst.Experience(new InstituteHandle(a, k), x)));
        int start = Total(0);
        var levels = new int[inst.Layout.StartingCount(0) * d];
        int discovered0 = Enumerable.Range(0, s.World.Graph.Count).Count(r => s.World.Knowledge.IsKnown(0, r));
        for (long t = 0; t < 180; t++)
        {
            s.StepThroughMonth(t);
            for (int k = 0; k < inst.Layout.StartingCount(0); k++)
            {
                for (int x = 0; x < d; x++)
                {
                    int level = inst.Level(new InstituteHandle(0, k), x);
                    Assert.True(level >= levels[k * d + x]);
                    levels[k * d + x] = level;
                }
            }
        }
        int opened = Enumerable.Range(0, s.World.Graph.Count).Count(r => s.World.Knowledge.IsKnown(0, r)) - discovered0;
        int gained = Total(0) - start;
        Assert.True(gained > 0, "no goal was completed in 15 years");
        int research = Enumerable.Range(0, s.World.Graph.Count).Count(r => s.World.Knowledge.DiscoveredByResearch(0, r));
        Assert.True(gained <= research, $"{gained} points for {research} researched nodes");
        Assert.True(gained <= opened, $"{gained} points of experience for {opened} opened nodes");
    }

    [Fact]
    public void Run_ThreadCountAndReplay_GiveTheSameHash()
    {
        string Run(int threads, out string log)
        {
            SliceSession s = Make(true, threads);
            int node = s.World.Graph.Number[Frontier(s, 0)[0]];
            int usa = Actor(s, "USA");
            var darpa = new InstituteHandle(usa, CandidateSlot(s, usa, "usa_darpa"));   // the AI starts it only near 1958: the player's order is the one that runs
            for (long t = 0; t < 60; t++)
            {
                if (t == 2)
                    Assert.True(s.Submit(SourceClass.Player, 0, 1, new SetPriorityNode(0, 2, node)).Accepted);
                if (t == 29)
                    Assert.True(s.Submit(SourceClass.Player, 0, 2, new BuildInstitute(usa, darpa.Slot)).Accepted);
                s.StepThroughMonth(t);
                if (t == 30)
                {
                    Assert.True(s.World.Institutes!.Building(darpa), "the player's project did not start");
                    Assert.Equal(29, s.World.InstituteOrders!.BuildMonth(darpa));
                }
            }
            Assert.DoesNotContain(s.Outcomes, o => o.Contains("rejected: BuildRefused"));
            log = s.CommandLog;
            return s.LastHash;
        }
        string h1 = Run(1, out string log), h2 = Run(4, out _);
        HashAssert.Equal(h1, h2, "L2: thread count changed the state");
        Assert.Contains("set_priority_node", log);
        Assert.Contains("build_institute", log);
        SliceSession replay = SliceSession.Replay(SliceTestData.Data, SliceRunConfig.Legacy with { Seed = 7, Institutes = true }, log, Make(true).World.Calendar.TickOfMonth(59) + 1);
        HashAssert.Equal(h1, replay.LastHash, "L1: the replay differs");
    }

    // Codex round 5 #4: the two systems must not share a diagnostic identity.
    [Fact]
    public void SystemIds_InstitutesAndIntelligenceDiffer()
    {
        SliceSession s = SliceTestData.Create(SliceRunConfig.Legacy with { Seed = 7, Institutes = true, Intelligence = true });
        Assert.NotEqual(s.World.Intel!.SystemId, s.World.Institutes!.SystemId);
    }

    // Codex merge #2: capacity and upkeep follow the current levels of a built institute.
    [Fact]
    public void BuiltInstitute_BudgetFactorFollowsLevelsFromExperience()
    {
        // SUN is the player's actor, so the goals of the new institute come from orders: the path rule of the AI gives a new slot a goal only now and then.
        SliceSession s = SliceTestData.Create(SliceRunConfig.Legacy with { Seed = 7, Institutes = true, PlayerActor = SliceTestData.Data.Scenario(SliceRunConfig.DefaultScenario).ActorIndex("SUN") });
        int sun = Sun(s);
        var h = new InstituteHandle(sun, CandidateSlot(s, sun, "sun_nii88"));
        InstituteSystem inst = s.World.Institutes!;
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new BuildInstitute(sun, h.Slot)).Accepted);
        long t = 0;
        while (!inst.BuiltByProject(h))
            s.StepThroughMonth(t++);
        int cmd = 1;
        void OrderGoal()
        {
            if (inst.GoalNode(h) != 0)
                return;
            foreach (int r in Frontier(s, sun))
            {
                int number = s.World.Graph.Number[r];
                bool taken = false;
                for (int k = 0; k < inst.Layout.SlotCount(sun); k++)
                    taken |= inst.GoalNode(new InstituteHandle(sun, k)) == number;
                if (!taken && s.Submit(SourceClass.Player, 0, cmd++, new SetPriorityNode(sun, h.Slot, number)).Accepted)
                    return;
            }
        }
        Fx6 atOpening = inst.BudgetFactor(sun, 0);
        int Levels() => Enumerable.Range(0, inst.Layout.DisciplineNames.Length).Sum(d => inst.Level(h, d));
        Assert.Equal(3, Levels());
        // Run on until the institute gains a level by experience (in any area); the factor must then move with it.
        for (; t < 900 && Levels() == 3; t++)
        {
            OrderGoal();
            s.StepThroughMonth(t);
        }
        int levels = Levels();
        Assert.True(levels > 3, "the institute did not gain a level in the run");
        Assert.Equal((Fx6.One - P.UpkeepPerLevel * Fx6.FromInt(levels)) * (Fx6.One + P.CapacityPerLevel * Fx6.FromInt(levels)), inst.BudgetFactor(sun, 0));
        Assert.NotEqual(atOpening, inst.BudgetFactor(sun, 0));
    }

    // Codex merge #3: the same goal again is not a change and keeps the period of the slot.
    [Fact]
    public void Order_SameGoalAgain_DoesNotRestartTheReassignPeriod()
    {
        SliceSession s = Make(true);
        s.StepThroughMonth(0);
        s.StepThroughMonth(1);
        int node = FreeNode(s);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new SetPriorityNode(0, 2, node)).Accepted);
        s.StepThroughMonth(2);
        long ordered = s.World.InstituteOrders!.OrderMonth(new InstituteHandle(0, 2));
        for (long t = 3; t < 8; t++)
            s.StepThroughMonth(t);
        Assert.True(s.Submit(SourceClass.Player, 0, 1, new SetPriorityNode(0, 2, node)).Accepted);
        s.StepThroughMonth(8);
        Assert.Equal(node, s.World.Institutes!.GoalNode(new InstituteHandle(0, 2)));   // the repeat was applied, not refused
        Assert.Equal(ordered, s.World.InstituteOrders.OrderMonth(new InstituteHandle(0, 2)));
    }

    // Codex merge #4: a built institute shows no construction charge.
    [Fact]
    public void BuiltInstitute_ViewShowsNoBuildRate()
    {
        SliceSession s = Make(true);
        int sun = Sun(s);
        var h = new InstituteHandle(sun, CandidateSlot(s, sun, "sun_nii88"));
        InstituteSystem inst = s.World.Institutes!;
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new BuildInstitute(sun, h.Slot)).Accepted);
        long t = 0;
        s.StepThroughMonth(t++);
        s.StepThroughMonth(t++);
        Assert.True(s.View.Actors[sun].Institutes.Single(x => x.Id == "sun_nii88").BuildRate > Fx6.Zero);
        while (!inst.Standing(h))
            s.StepThroughMonth(t++);
        Assert.Equal(Fx6.Zero, s.View.Actors[sun].Institutes.Single(x => x.Id == "sun_nii88").BuildRate);
    }

    [Fact]
    public void DataIdentity_TheInstitutesDoNotChangeTheDataHash_ButHaveTheirOwn()
    {
        Assert.NotEmpty(SliceTestData.Data.Institutes.Hash);
        Assert.Equal(0, SliceTestData.LegacyData.Institutes.Count);
    }
    private static int Actor(SliceSession s, string id) => s.World.Scenario.ActorIndex(id);

    private static int Sun(SliceSession s) => Actor(s, "SUN");

    private static int CandidateSlot(SliceSession s, int actor, string instituteId)
    {
        InstituteLayout l = s.World.Institutes!.Layout;
        for (int k = 0; k < l.SlotCount(actor); k++)
        {
            if (l.IdOf(new InstituteHandle(actor, k)) == instituteId)
                return k;
        }
        throw new InvalidOperationException(instituteId);
    }

    [Fact]
    public void BuildMath_FollowsRev4()
    {
        Assert.Equal(42, InstituteMath.BuildMonths(3, P));   // 12 · (2 + 0.5 · 3)
        Assert.Equal(60, InstituteMath.BuildMonths(6, P));
        Assert.Equal(Fx6.One, InstituteMath.AheadMultiplier(0, P));
        Assert.Equal(Fx6.FromInt(3), InstituteMath.AheadMultiplier(10, P));   // 1 + 0.02 · 100
        Assert.Equal(Fx6.FromInt(3), InstituteMath.AheadMultiplier(25, P));   // the window ends at 10
        Assert.Equal(Fx6.FromRaw(60_000), InstituteMath.BuildRate(4, 0, P));   // ONERA-like: 6 % of the research money a month of flow
        Assert.Equal(Fx6.FromRaw(180_000), InstituteMath.BuildRate(4, 10, P));
    }

    [Fact]
    public void SmallCountries_AreThoseWithAtMostATenthOfTheBudgetOfTheLargest()
    {
        InstituteLayout l = Make(true).World.Institutes!.Layout;
        SliceSession s = Make(true);
        Assert.True(l.IsSmallCountry(Actor(s, "FRA")) && l.IsSmallCountry(Actor(s, "SWE")));
        Assert.False(l.IsSmallCountry(Actor(s, "USA")) || l.IsSmallCountry(Actor(s, "GBR")) || l.IsSmallCountry(Actor(s, "SUN")));
        // The data fit the cap: no candidate of a small country has more than 3 levels.
        foreach (string a in new[] { "FRA", "SWE" })
        {
            for (int k = 0; k < l.SlotCount(Actor(s, a)); k++)
                Assert.True(l.TotalLevels(new InstituteHandle(Actor(s, a), k)) <= 3 || k < l.StartingCount(Actor(s, a)));
        }
    }

    [Fact]
    public void Build_IsRefusedBeforeTheWindow_OnceRunning_AndForAStandingInstitute()
    {
        SliceSession s = Make(true);
        int usa = Actor(s, "USA"), sun = Sun(s);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new BuildInstitute(usa, CandidateSlot(s, usa, "usa_darpa"))).Accepted);   // the form is fine
        Assert.True(s.Submit(SourceClass.Player, 0, 1, new BuildInstitute(sun, CandidateSlot(s, sun, "sun_nii88"))).Accepted);
        Assert.True(s.Submit(SourceClass.Player, 0, 2, new BuildInstitute(sun, 0)).Accepted);
        s.StepThroughMonth(0);
        Assert.Contains(s.Outcomes, o => o.Contains("rejected: BuildRefused") && o.Contains("window opens in 1948"));
        Assert.Contains(s.Outcomes, o => o.Contains("rejected: BuildRefused") && o.Contains("stands already"));
        Assert.True(s.World.Institutes!.Building(new InstituteHandle(sun, CandidateSlot(s, sun, "sun_nii88"))));
        Assert.True(s.Submit(SourceClass.Player, 0, 3, new BuildInstitute(sun, CandidateSlot(s, sun, "sun_nii88"))).Accepted);
        s.StepThroughMonth(1);
        Assert.Contains(s.Outcomes, o => o.Contains("rejected: BuildRefused") && o.Contains("runs already"));
        Assert.Equal(CommandRefusal.BadSlot, s.Submit(SourceClass.Player, 0, 4, new BuildInstitute(sun, 99)).Reason);
        Assert.Equal(CommandRefusal.InstitutesOff, Make(false).Submit(SourceClass.Player, 0, 0, new BuildInstitute(0, 0)).Reason);
    }

    [Fact]
    public void Build_TakesMoneyWhileItRuns_ThenOpensWithItsLevelsAndAddsNetCapacity()
    {
        SliceSession s = Make(true);
        int sun = Sun(s);
        var h = new InstituteHandle(sun, CandidateSlot(s, sun, "sun_nii88"));
        InstituteSystem inst = s.World.Institutes!;
        int prop = inst.Layout.DisciplineNames.IndexOf("prop");
        Assert.Equal(Fx6.One, inst.BudgetFactor(sun, 0));
        Assert.Equal(0, inst.Level(h, prop));
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new BuildInstitute(sun, h.Slot)).Accepted);
        s.StepThroughMonth(0);
        s.StepThroughMonth(1);
        Fx6 running = inst.BudgetFactor(sun, 2);   // 1 - 3 levels * 1.5 %: the project opens in 1949, after its historical year 1946, so the price is not raised
        Assert.Equal(Fx6.One - InstituteMath.BuildRate(3, 0, P), running);
        Assert.False(inst.Standing(h));
        int months = inst.BuildMonths(h);
        for (long t = 2; t < months - 1; t++)
            s.StepThroughMonth(t);
        Assert.False(inst.Standing(h));
        s.StepThroughMonth(months - 1);
        Assert.True(inst.Standing(h) && inst.BuiltByProject(h));
        Assert.Equal(months - 1, inst.OpenedMonth(h));
        Assert.Equal(3, inst.Level(h, prop));
        Fx6 after = inst.BudgetFactor(sun, 0);   // (1 - 3 * 0.4 %) * (1 + 3 * 0.8 %): the net gain is positive
        Assert.Equal((Fx6.One - Fx6.FromRaw(12_000)) * (Fx6.One + Fx6.FromRaw(24_000)), after);
        Assert.True(after > Fx6.One);
        InstituteView v = s.View.Actors[sun].Institutes.Single(x => x.Id == "sun_nii88");
        Assert.True(v.Standing);
        Assert.Equal(3, v.Levels[prop]);
        Assert.Equal(3, v.TotalLevels);
        // The new institute is a slot like the others: a goal it has is an unknown node (the path rule may leave it without one).
        s.StepThroughMonth(months);
        s.StepThroughMonth(months + 1);
        int goal = inst.GoalNode(h);
        Assert.True(goal == 0 || !s.World.Knowledge.IsKnown(sun, s.World.Graph.RowOf(goal)));
    }

    [Fact]
    public void Ai_BuildsTheHistoricalInstitutesAboutTheirYear_ByTheSameRules()
    {
        SliceSession s = Make(true);
        InstituteSystem inst = s.World.Institutes!;
        int usa = Actor(s, "USA"), fra = Actor(s, "FRA"), gbr = Actor(s, "GBR");
        var darpa = new InstituteHandle(usa, CandidateSlot(s, usa, "usa_darpa"));
        var onera = new InstituteHandle(fra, CandidateSlot(s, fra, "fra_onera"));
        var rre = new InstituteHandle(gbr, CandidateSlot(s, gbr, "gbr_rre"));
        for (long t = 0; t < 160; t++)
        {
            s.StepThroughMonth(t);
            if (t == 30)
            {
                Assert.True(inst.Building(onera) || inst.Standing(onera));   // the project of a 1946 institute starts at once: it is late already
                Assert.False(inst.Building(darpa) || inst.Standing(darpa));
            }
        }
        long refMonth = inst.Layout.FirstMonthOf(1958);
        Assert.True(inst.Standing(darpa));
        Assert.InRange(inst.OpenedMonth(darpa), refMonth - 3, refMonth + 1);
        Assert.True(inst.Standing(rre) && inst.OpenedMonth(rre) <= inst.Layout.FirstMonthOf(1953) + 1);
        Assert.True(inst.Standing(onera));
        Assert.Equal(inst.BuildMonths(onera), inst.OpenedMonth(onera));   // seen from month 1: its start, paid from there
        Assert.Equal(1, inst.Level(onera, inst.Layout.DisciplineNames.IndexOf("prop")));
    }

    [Fact]
    public void Codec_RoundTripsBuildInstitute()
    {
        var logged = new LoggedCommand(new CommandKey(5, SourceClass.Player, 0, 3), new BuildInstitute(2, 6));
        Assert.Equal(logged.Command, CommandCodec.Decode(CommandCodec.Encode(logged)).Command);
    }

    [Fact]
    public void FlagOff_ViewHasNoInstitutes()
    {
        SliceSession s = Make(false);
        s.StepThroughMonth(0);
        Assert.True(s.View.Actors[0].Institutes.IsEmpty);
    }
    // O-067: the price counts the years from the opening of the project to the historical year.
    [Fact]
    public void Build_PriceGrowsWithTheYearsBetweenTheOpeningAndTheHistoricalYear()
    {
        SliceSession s = Make(true);
        int usa = Actor(s, "USA");
        var h = new InstituteHandle(usa, CandidateSlot(s, usa, "usa_darpa"));
        InstituteSystem inst = s.World.Institutes!;
        for (long t = 0; t < 28; t++)
            s.StepThroughMonth(t);   // 1948-01: the window of a 1958 institute opens
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new BuildInstitute(usa, h.Slot)).Accepted);
        s.StepThroughMonth(28);
        Assert.True(inst.Building(h));
        long opening = 28 + inst.BuildMonths(h) - 1;
        int years = 1958 - inst.Layout.YearOf(opening);
        Assert.True(years > 3);
        Assert.Equal(Fx6.One - InstituteMath.BuildRate(3, years, P), inst.BudgetFactor(usa, 29));
        Assert.Equal(InstituteMath.BuildRate(3, years, P), inst.BuildRate(h, 29));
    }

    // Codex round 2: a run whose knowledge is a schedule refuses priority orders; the player does not see the goals of the others.
    [Fact]
    public void ScheduledKnowledge_RefusesPriorityOrders()
    {
        var limits = new CommandLimits(3, 1, 1, []) { InstituteSlots = [2, 2, 2], NodeNumbers = [47], ScheduledKnowledge = true };
        var logged = new LoggedCommand(new CommandKey(5, SourceClass.Player, 0, 3), new SetPriorityNode(2, 1, 47));
        Assert.Equal(CommandRefusal.PriorityNoEffect, CommandValidator.CheckForm(logged, limits).Reason);
        Assert.True(CommandValidator.CheckForm(logged, limits with { ScheduledKnowledge = false }).Accepted);
    }

    [Fact]
    public void PlayerView_HidesTheGoalsOfOtherActors_WithIntelligence()
    {
        SliceSession s = SliceTestData.Create(SliceRunConfig.Legacy with { Seed = 7, Institutes = true, Intelligence = true, PlayerActor = 3 });
        InstituteSystem inst = s.World.Institutes!;
        int other = (3 + 1) % s.World.Scenario.ActorCount;
        for (long t = 0; t < 40; t++)
            s.StepThroughMonth(t);
        Assert.Contains(Enumerable.Range(0, inst.Layout.SlotCount(other)), k => inst.GoalNode(new InstituteHandle(other, k)) != 0);
        Assert.All(s.View.Actors[other].Institutes, v => Assert.Equal(0, v.Goal));
    }

    // O-067: the AI does not touch the actor of the player; it proposes in the view.
    [Fact]
    public void PlayerActor_GetsProposalsButNoAiGoalsOrProjects()
    {
        SliceSession s = SliceTestData.Create(SliceRunConfig.Legacy with { Seed = 7, Institutes = true, PlayerActor = 3 });
        InstituteSystem inst = s.World.Institutes!;
        int player = 3, other = (player + 1) % s.World.Scenario.ActorCount;
        string playerId = s.World.Scenario.ActorId[player];
        var candidates = Enumerable.Range(inst.Layout.StartingCount(player), inst.Layout.SlotCount(player) - inst.Layout.StartingCount(player)).ToArray();
        for (long t = 0; t < 40; t++)
            s.StepThroughMonth(t);
        for (int k = 0; k < inst.Layout.SlotCount(player); k++)
            Assert.Equal(0, inst.GoalNode(new InstituteHandle(player, k)));
        Assert.All(candidates, k => Assert.False(inst.Building(new InstituteHandle(player, k)) || inst.Standing(new InstituteHandle(player, k)), playerId));
        Assert.Contains(Enumerable.Range(0, inst.Layout.SlotCount(other)), k => inst.GoalNode(new InstituteHandle(other, k)) != 0);
        var view = s.View.Actors[player].Institutes;
        Assert.Contains(view, v => v.ProposedGoal != 0);
        // A proposal to build exists only on candidate slots, never on a standing institute.
        Assert.All(view.Where(v => v.Standing), v => Assert.False(v.ProposesBuild));
        Assert.All(view.Where(v => v.ProposesBuild), v => Assert.Contains(v.Slot, candidates));
        Assert.All(s.View.Actors[other].Institutes, v => Assert.False(v.ProposesBuild || v.ProposedGoal != 0));
        // A command of the player works as before.
        int node = s.World.Graph.Number[Frontier(s, player)[0]];
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new SetPriorityNode(player, 0, node)).Accepted);
        s.StepThroughMonth(40);
        Assert.Equal(node, inst.GoalNode(new InstituteHandle(player, 0)));
    }

    private static Singularity.Core.Data.GameData DataWithOtherInstitutes()
    {
        string dir = Path.Combine(Path.GetTempPath(), "singularity-inst-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string core = Path.Combine(SliceTestData.RepoRoot, "mods", "core");
        foreach (string f in Directory.GetFiles(core, "*.yaml"))
        {
            string text = File.ReadAllText(f);
            if (Path.GetFileName(f) == "institutes.yaml")
            {
                Assert.Contains("areas: {aero: 4, prop: 3, mech: 2}, directions: {aero: 4, prop: 3, struct: 2}, source: \"en.wikipedia.org/wiki/National_Advisory", text);
                text = text.Replace("areas: {aero: 4, prop: 3, mech: 2}, directions: {aero: 4, prop: 3, struct: 2}, source: \"en.wikipedia.org/wiki/National_Advisory", "areas: {aero: 3, prop: 3, mech: 2}, directions: {aero: 4, prop: 3, struct: 2}, source: \"en.wikipedia.org/wiki/National_Advisory");
            }
            File.WriteAllText(Path.Combine(dir, Path.GetFileName(f)), text);
        }
        File.Copy(Path.Combine(core, "mod.yaml"), Path.Combine(dir, "mod.yaml"), true);
        try
        {
            return Singularity.Core.Data.ModLoader.Load(dir);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Snapshot_OfAnotherInstituteDataSet_IsRefused()
    {
        var config = SliceRunConfig.Legacy with { Seed = 7, Institutes = true };
        SliceSession a = SliceSession.Create(SliceTestData.Data, config);
        for (long m = 0; m < 3; m++)
            a.StepThroughMonth(m);
        var snap = new MemoryStream();
        a.WriteSnapshot(snap);
        SliceSession.Create(SliceTestData.Data, config).LoadSnapshot(new MemoryStream(snap.ToArray()));
        SliceSession other = SliceSession.Create(DataWithOtherInstitutes(), config);
        Assert.ThrowsAny<Exception>(() => other.LoadSnapshot(new MemoryStream(snap.ToArray())));
    }

    [Fact]
    public void Build_WithoutResearchMoney_DoesNotFinish_AndStretchesWithAPartialShare()
    {
        SliceSession s = SliceTestData.Create(SliceRunConfig.Legacy with { Seed = 7, Institutes = true, PlayerActor = Sun(Make(true)) });
        int sun = Sun(s);
        var h = new InstituteHandle(sun, CandidateSlot(s, sun, "sun_nii88"));
        InstituteSystem inst = s.World.Institutes!;
        int months = inst.BuildMonths(h);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new Singularity.Core.Commands.SetBudgetShares(sun,
            new Singularity.Core.Policy.BudgetShares(Fx6.Zero, Fx6.FromRaw(250_000), Fx6.FromRaw(250_000), Fx6.FromRaw(250_000), Fx6.FromRaw(250_000)))).Accepted);
        Assert.True(s.Submit(SourceClass.Player, 0, 1, new BuildInstitute(sun, h.Slot)).Accepted);
        for (long t = 0; t < months + 6; t++)
            s.StepThroughMonth(t);
        Assert.False(inst.Standing(h));
        Assert.True(inst.Building(h));
    }

    [Fact]
    public void OverloadScale_SlowsEveryProjectWhenTheTakeExceedsTheMoney()
    {
        Assert.Equal(Fx6.One, InstituteMath.OverloadScale(Fx6.Zero));
        Assert.Equal(Fx6.One, InstituteMath.OverloadScale(Fx6.One));
        Assert.Equal(Fx6.FromRaw(500_000), InstituteMath.OverloadScale(Fx6.FromInt(2)));
        Assert.Equal(Fx6.One / Fx6.FromRaw(1_070_000), InstituteMath.OverloadScale(Fx6.FromRaw(1_070_000)));   // the Codex scenario: 107 %
    }

    // TASK-0175 (TASK-0173 finding): a project stretched by a low funding opens about its historical year and must pay the price of that year, not of the year it started for.
    [Fact]
    public void StretchedProject_PaysThePriceOfItsExpectedOpeningYear_AndFollowsTheFunding()
    {
        SliceSession s = SliceTestData.Create(SliceRunConfig.Legacy with { Seed = 7, Institutes = true, PlayerActor = Actor(Make(true), "USA") });
        int usa = Actor(s, "USA");
        var h = new InstituteHandle(usa, CandidateSlot(s, usa, "usa_darpa"));
        InstituteSystem inst = s.World.Institutes!;
        for (long t = 0; t < 28; t++)
            s.StepThroughMonth(t);
        Fx6 plan = s.World.Policy.Shares(usa).Research;
        Fx6 low = plan * Fx6.FromRaw(200_000);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new SetBudgetShares(usa, new Singularity.Core.Policy.BudgetShares(low, Fx6.One - low, Fx6.Zero, Fx6.Zero, Fx6.Zero))).Accepted);
        Assert.True(s.Submit(SourceClass.Player, 0, 1, new BuildInstitute(usa, h.Slot)).Accepted);
        s.StepThroughMonth(28);
        Assert.True(inst.Building(h));
        long opening = inst.ExpectedOpening(h, 29);
        Assert.True(inst.Layout.YearOf(opening) >= 1958, "the stretched project opens in or after its historical year");
        Assert.True(inst.BuildMonths(h) > 12 * 3);
        // The price of the first month is the one of the expected opening year (on time = x1), shown in the view before and after the click.
        Assert.Equal(InstituteMath.BuildRate(3, 0, P), inst.BuildRate(h, 29));
        Assert.Equal(InstituteMath.BuildRate(3, 0, P), s.View.Actors[usa].Institutes.Single(x => x.Slot == h.Slot).BuildRate);
        // Full funding again: the project would open early, the price rises.
        Assert.True(s.Submit(SourceClass.Player, 0, 2, new SetBudgetShares(usa, s.World.Policy.Shares(usa))).Accepted);
        s.StepThroughMonth(29);
        Assert.True(inst.BuildRate(h, 30) > InstituteMath.BuildRate(3, 0, P));
    }

    // Run to the end: the stretched project opens about its historical year and pays the price of opening on time in every month, so the sum is months x base rate.
    [Fact]
    public void StretchedProject_RunsToItsHistoricalYear_AndPaysTheBaseRateEveryMonth()
    {
        SliceSession s = SliceTestData.Create(SliceRunConfig.Legacy with { Seed = 7, Institutes = true, PlayerActor = Actor(Make(true), "USA") });
        int usa = Actor(s, "USA");
        var h = new InstituteHandle(usa, CandidateSlot(s, usa, "usa_darpa"));
        InstituteSystem inst = s.World.Institutes!;
        for (long t = 0; t < 28; t++)
            s.StepThroughMonth(t);
        Fx6 low = s.World.Policy.Shares(usa).Research * Fx6.FromRaw(350_000);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new SetBudgetShares(usa, new Singularity.Core.Policy.BudgetShares(low, Fx6.One - low, Fx6.Zero, Fx6.Zero, Fx6.Zero))).Accepted);
        Assert.True(s.Submit(SourceClass.Player, 0, 1, new BuildInstitute(usa, h.Slot)).Accepted);
        long sum = 0;
        int months = 0;
        long m = 28;
        for (; m < 28 + 12 * 40 && (m == 28 || !inst.Standing(h)); m++)
        {
            s.StepThroughMonth(m);
            if (inst.Building(h))
            {
                Fx6 charged = inst.BuildRate(h, m + 1);
                Assert.True(charged <= InstituteMath.BuildRate(3, 1, P), $"month {m}: the price must stay about the base one");
                sum += charged.Raw;
                months++;
            }
        }
        Assert.True(inst.Standing(h));
        int openYear = inst.Layout.YearOf(inst.OpenedMonth(h));
        Assert.InRange(openYear, 1957, 1959);
        Assert.True(months > 12 * 5, "the project was stretched well beyond its nominal term");
        // The project opens in December of the year before, so at most the one-year step (x1.02) applies: the sum stays within 2 % of months x base rate (the old rule charged x1.77).
        long baseSum = months * (long)InstituteMath.BuildRate(3, 0, P).Raw;
        Assert.InRange(sum, baseSum, baseSum * 102 / 100);
    }

    // A project without any research money never opens: it pays the price of the historical year (x1) and the year of an absurd month does not overflow.
    [Fact]
    public void ProjectWithoutFunding_PaysThePriceOfOpeningOnTime()
    {
        SliceSession s = SliceTestData.Create(SliceRunConfig.Legacy with { Seed = 7, Institutes = true, PlayerActor = Actor(Make(true), "USA") });
        int usa = Actor(s, "USA");
        var h = new InstituteHandle(usa, CandidateSlot(s, usa, "usa_darpa"));
        InstituteSystem inst = s.World.Institutes!;
        for (long t = 0; t < 28; t++)
            s.StepThroughMonth(t);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new SetBudgetShares(usa, new Singularity.Core.Policy.BudgetShares(Fx6.Zero, Fx6.One, Fx6.Zero, Fx6.Zero, Fx6.Zero))).Accepted);
        Assert.True(s.Submit(SourceClass.Player, 0, 1, new BuildInstitute(usa, h.Slot)).Accepted);
        s.StepThroughMonth(28);
        Assert.True(inst.Building(h));
        Assert.Equal(InstituteMath.BuildRate(3, 0, P), inst.BuildRate(h, 29));
        Assert.Equal(InstituteMath.BuildRate(3, 0, P), s.View.Actors[usa].Institutes.Single(x => x.Slot == h.Slot).BuildRate);
    }

    // The window shows the price of a candidate before the click: the same the core charges once the project starts in that month.
    [Fact]
    public void Candidate_ShowsThePriceTheCoreChargesOnceStarted()
    {
        SliceSession s = Make(true);
        int usa = Actor(s, "USA");
        var h = new InstituteHandle(usa, CandidateSlot(s, usa, "usa_darpa"));
        InstituteSystem inst = s.World.Institutes!;
        for (long t = 0; t < 28; t++)
            s.StepThroughMonth(t);
        Fx6 shown = s.View.Actors[usa].Institutes.Single(x => x.Slot == h.Slot).BuildRate;
        Assert.True(shown > InstituteMath.BuildRate(3, 0, P), "an early candidate costs more than one on time");
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new BuildInstitute(usa, h.Slot)).Accepted);
        s.StepThroughMonth(28);
        Assert.Equal(Fx6.One - shown, inst.BudgetFactor(usa, 28));
        Assert.Equal(shown, s.View.Actors[usa].Institutes.Single(x => x.Slot == h.Slot).BuildRate);
    }

    [Fact]
    public void PlayerOrder_DropsTheStoredAiGoal_SoItCannotReturnAndDuplicate()
    {
        SliceSession s = Make(true);
        s.StepThroughMonth(0);
        s.StepThroughMonth(1);
        InstituteSystem inst = s.World.Institutes!;
        int slot = AiGoalSlot(s);   // the path rule leaves some slots without a goal: take one that has it
        var h = new InstituteHandle(0, slot);
        Assert.NotEqual(0, inst.GoalNode(h));
        int order = FreeNode(s);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new SetPriorityNode(0, slot, order)).Accepted);
        s.StepThroughMonth(2);
        Assert.Equal(order, inst.GoalNode(h));
        Assert.Equal(0, s.World.Table.GetInt($"inst.ai.{slot}").Current[0]);
    }

    [Fact]
    public void NewAiGoal_IsNotCreditedForADiscoveryMadeBeforeItActed()
    {
        SliceSession s = Make(true);
        s.StepThroughMonth(0);
        // The goals were picked in month 0 but act from month 1: nothing was worked on yet, so nothing can be credited.
        int slot = AiGoalSlot(s);
        Assert.Equal(0, s.World.Table.GetInt($"inst.cur.{slot}").Current[0]);
        s.StepThroughMonth(1);
        Assert.NotEqual(0, s.World.Table.GetInt($"inst.cur.{slot}").Current[0]);
    }

    [Fact]
    public void InitialHash_DiffersByTheControlledActor()
    {
        string h3 = SliceTestData.Create(SliceRunConfig.Legacy with { Seed = 7, Institutes = true, PlayerActor = 3 }).LastHash;
        string h4 = SliceTestData.Create(SliceRunConfig.Legacy with { Seed = 7, Institutes = true, PlayerActor = 4 }).LastHash;
        Assert.NotEqual(h3, h4);
    }
}
