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
        for (int a = 0; a < s.World.Scenario.ActorCount; a++)
        {
            byte[] tier = Path(s, a);
            Assert.Contains(tier, t => t != 0);
            for (int r = 0; r < g.Count; r++)
            {
                if (tier[r] == 0)
                    continue;
                Assert.False(s.World.Knowledge.IsKnown(a, r));
                for (int grp = g.Requires.GroupStart[r]; grp < g.Requires.GroupStart[r + 1]; grp++)
                {
                    int[] members = [.. g.Requires.GroupMembers(grp)];
                    if (members.Any(m => s.World.Knowledge.IsKnown(a, m)))
                        continue;
                    sawAnyOf |= members.Length > 1;
                    // An unsatisfied group: every unknown member is on the path (any of them would satisfy it).
                    Assert.All(members, m => Assert.NotEqual(0, tier[m]));
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
    public void AiInstitutesTakeOnlyPathNodes()
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 7 });
        s.StepThroughMonth(1);
        var inst = s.World.Institutes!;
        int checkedGoals = 0;
        for (int a = 0; a < s.World.Scenario.ActorCount; a++)
        {
            byte[] tier = Path(s, a);
            for (int k = 0; k < inst.Layout.SlotCount(a); k++)
            {
                int n = inst.GoalNode(new Singularity.Core.Institutes.InstituteHandle(a, k));
                if (n == 0)
                    continue;
                checkedGoals++;
                Assert.NotEqual(0, tier[s.World.Graph.RowOf(n)]);
            }
        }
        Assert.True(checkedGoals > 0);
    }
}
