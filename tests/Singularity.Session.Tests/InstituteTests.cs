using Singularity.Core.Commands;
using Singularity.Core.Institutes;
using Singularity.Core.Knowledge;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>TASK-0151: research priorities through institutes (slots, money first, level bonus, experience, the AI under the same rules).</summary>
public class InstituteTests
{
    private static SliceSession Make(bool institutes, int threads = 1) =>
        SliceTestData.Create(new SliceRunConfig { Seed = 7, Institutes = institutes, Threads = threads });

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
        Assert.Equal(6, l.SlotCount(actors.ToList().IndexOf("USA")));
        Assert.Equal(6, l.SlotCount(actors.ToList().IndexOf("SUN")));
        Assert.Equal(3, l.SlotCount(actors.ToList().IndexOf("GBR")));
        Assert.Equal(3, l.SlotCount(actors.ToList().IndexOf("FRA")));
        Assert.Equal(4, l.SlotCount(actors.ToList().IndexOf("SWE")));
        for (int a = 0; a < l.ActorCount; a++)
        {
            for (int k = 0; k < l.SlotCount(a); k++)
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
                if (k > Fx6.One)
                    Assert.True(Fx6.One - k * group / wsum >= P.Rho - Fx6.FromRaw(2), $"w {w} g {g} kappa {k}");
            }
        }
    }

    [Fact]
    public void Command_ChecksSlotNodeAndNodeStillUnknown()
    {
        SliceSession s = Make(true);
        int node = s.World.Graph.Number[Frontier(s, 0)[0]];
        Assert.Equal(CommandRefusal.BadSlot, s.Submit(SourceClass.Player, 0, 0, new SetPriorityNode(0, 6, node)).Reason);
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
    public void Goals_FromTheAi_FillEverySlotWithDistinctFrontierNodes()
    {
        SliceSession s = Make(true);
        s.StepThroughMonth(0);
        s.StepThroughMonth(1);
        InstituteSystem inst = s.World.Institutes!;
        for (int a = 0; a < inst.Layout.ActorCount; a++)
        {
            var goals = new List<int>();
            for (int k = 0; k < inst.Layout.SlotCount(a); k++)
                goals.Add(inst.GoalNode(new InstituteHandle(a, k)));
            Assert.All(goals, g => Assert.NotEqual(0, g));
            Assert.Equal(goals.Count, goals.Distinct().Count());
            Assert.All(goals, g => Assert.False(s.World.Knowledge.IsKnown(a, s.World.Graph.RowOf(g))));
        }
    }

    [Fact]
    public void Experience_GrowsByOneForACompletedGoalAndLevelsNeverFall()
    {
        SliceSession s = Make(true);
        InstituteSystem inst = s.World.Institutes!;
        int d = inst.Layout.DisciplineNames.Length;
        int Total(int a) => Enumerable.Range(0, inst.Layout.SlotCount(a)).Sum(k => Enumerable.Range(0, d).Sum(x => inst.Experience(new InstituteHandle(a, k), x)));
        int start = Total(0);
        var levels = new int[inst.Layout.SlotCount(0) * d];
        int discovered0 = Enumerable.Range(0, s.World.Graph.Count).Count(r => s.World.Knowledge.IsKnown(0, r));
        for (long t = 0; t < 180; t++)
        {
            s.StepThroughMonth(t);
            for (int k = 0; k < inst.Layout.SlotCount(0); k++)
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
            for (long t = 0; t < 30; t++)
            {
                if (t == 2)
                    Assert.True(s.Submit(SourceClass.Player, 0, 1, new SetPriorityNode(0, 2, node)).Accepted);
                s.StepThroughMonth(t);
            }
            log = s.CommandLog;
            return s.LastHash;
        }
        string h1 = Run(1, out string log), h2 = Run(4, out _);
        HashAssert.Equal(h1, h2, "L2: thread count changed the state");
        Assert.Contains("set_priority_node", log);
        SliceSession replay = SliceSession.Replay(SliceTestData.Data, new SliceRunConfig { Seed = 7, Institutes = true }, log, Make(true).World.Calendar.TickOfMonth(29) + 1);
        HashAssert.Equal(h1, replay.LastHash, "L1: the replay differs");
    }

    [Fact]
    public void DataIdentity_TheInstitutesDoNotChangeTheDataHash_ButHaveTheirOwn()
    {
        Assert.NotEmpty(SliceTestData.Data.Institutes.Hash);
        Assert.Equal(0, SliceTestData.LegacyData.Institutes.Count);
    }
}
