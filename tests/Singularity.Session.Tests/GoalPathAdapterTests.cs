using Singularity.Core.Knowledge;

namespace Singularity.Session.Tests;

/// <summary>#559: the path of the real world to the next product generation (the adapter of the AI institute goals).</summary>
public class GoalPathAdapterTests
{
    private static byte[] Path(SliceSession s, int actor)
    {
        var tier = new byte[s.World.Graph.Count];
        new InstituteInputsAdapter(s.World).GoalPath(actor, 0, tier);
        return tier;
    }

    [Fact]
    public void Path_MarksOnlyUnknownNodesAndBothMembersOfAnAnyOfGroup()
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 7 });
        KnowledgeGraph g = s.World.Graph;
        bool sawAnyOf = false;
        // Walk the run until some actor has an unsatisfied any_of group on its path (the early path has none).
        for (long m = 0; m < 400 && !sawAnyOf; m += 12)
        {
            if (m > 0)
                s.StepThroughMonth(m);
            for (int a = 0; a < s.World.Scenario.ActorCount; a++)
            {
                byte[] tier = Path(s, a);
                for (int r = 0; r < g.Count; r++)
                {
                    if (tier[r] == 0)
                        continue;
                    Assert.Equal(1, tier[r]);
                    Assert.False(s.World.Knowledge.IsKnown(a, r));
                    for (int grp = g.Requires.GroupStart[r]; grp < g.Requires.GroupStart[r + 1]; grp++)
                    {
                        int[] members = [.. g.Requires.GroupMembers(grp)];
                        if (members.Any(x => s.World.Knowledge.IsKnown(a, x)))
                            continue;
                        sawAnyOf |= members.Length > 1;
                        // An unsatisfied group: every unknown member is on the path (any of them would satisfy it).
                        Assert.All(members, x => Assert.NotEqual(0, tier[x]));
                    }
                }
            }
        }
        Assert.True(sawAnyOf, "no unsatisfied any_of group on a path: the data cannot show the rule");
    }

    [Fact]
    public void Path_IsAPureFunctionOfTheSnapshot()
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 7 });
        Assert.Equal(Path(s, 0), Path(s, 0));
    }

    [Fact]
    public void AiInstitutesTakeOnlyTierOneNodesOfTheSnapshotOfThePickMonth()
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 7 });
        int actors = s.World.Scenario.ActorCount;
        byte[][] tiers = [.. Enumerable.Range(0, actors).Select(a => Path(s, a))];   // the snapshot the picks of month 0 read
        s.StepThroughMonth(0);
        var inst = s.World.Institutes!;
        int checkedGoals = 0;
        for (int a = 0; a < actors; a++)
        {
            for (int k = 0; k < inst.Layout.SlotCount(a); k++)
            {
                int n = inst.GoalNode(new Singularity.Core.Institutes.InstituteHandle(a, k));
                if (n == 0)
                    continue;
                checkedGoals++;
                Assert.Equal(1, tiers[a][s.World.Graph.RowOf(n)]);
            }
        }
        Assert.True(checkedGoals > 0);
    }

    [Fact]
    public void ASlotIsFreeOnlyWhenNoFreeTierOneNodeIsInTheFrontier()
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 7 });
        var inst = s.World.Institutes!;
        int freeSlots = 0;
        for (int a = 0; a < s.World.Scenario.ActorCount; a++)
        {
            byte[] tier = Path(s, a);
            var goals = new int[inst.Layout.SlotCount(a)];
            inst.Propose(a, 0, goals, new bool[goals.Length]);
            int[] frontierOnPath = [.. Enumerable.Range(0, s.World.Graph.Count).Where(r => tier[r] == 1 && s.World.Knowledge.InFrontier(a, r)).Select(r => s.World.Graph.Number[r])];
            int[] proposed = [.. goals.Where(g => g != 0)];
            Assert.All(proposed, g => Assert.Contains(g, frontierOnPath));
            Assert.Equal(proposed.Length, proposed.Distinct().Count());
            int standingFree = Enumerable.Range(0, goals.Length).Count(k => inst.Standing(new Singularity.Core.Institutes.InstituteHandle(a, k)) && inst.GoalNode(new Singularity.Core.Institutes.InstituteHandle(a, k)) == 0 && goals[k] == 0);
            if (standingFree > 0)
            {
                freeSlots += standingFree;
                Assert.Equal(frontierOnPath.Length, proposed.Length);   // every node of the path in the frontier is taken
            }
        }
        Assert.True(freeSlots > 0, "no free slot at the start: the check is not exercised");
    }
}
