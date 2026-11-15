using Singularity.Core.Policy;

namespace Singularity.Session.Tests;

/// <summary>HYP-0004 sketch round 2: the experimental PILOT_CHOICE switch of the AI (off in main; the goldens of main are pinned by the other tests).</summary>
public class PilotChoiceTests
{
    private static readonly ulong[] Seeds = [1, 2, 3, 4, 5];

    private static string HashOf(ulong seed, PilotChoice pilot, int threads, int months)
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = seed, Threads = threads, PilotChoice = pilot });
        for (long t = 0; t < months; t++)
            s.StepThroughMonth(t);
        return s.LastHash;
    }

    [Fact]
    public void Off_IsTheDefaultConfig_AndCarriesNoColumn()
    {
        Assert.Equal(PilotChoice.Off, new SliceRunConfig().PilotChoice);
        Assert.Equal(HashOf(1, PilotChoice.Off, 2, 40), HashOf(1, PilotChoice.Off, 2, 40));
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 1, Threads = 1 });
        Assert.DoesNotContain("run.pilot", s.World.Table.Columns.Select(c => c.Name));
    }

    [Fact]
    public void On_ChangesTheDigest_AndIsPartOfTheRunColumns()
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 1, Threads = 1, PilotChoice = PilotChoice.On });
        Assert.Contains("run.pilot", s.World.Table.Columns.Select(c => c.Name));
        int changed = 0;
        foreach (ulong seed in Seeds)
            changed += HashOf(seed, PilotChoice.Off, 2, 384) != HashOf(seed, PilotChoice.On, 2, 384) ? 1 : 0;
        Assert.True(changed == Seeds.Length, $"{changed} of {Seeds.Length} seeds changed");
    }

    [Fact]
    public void On_ChangesTheCompositionsTheAiChooses_NotOnlyTheRunColumn()
    {
        int differing = 0;
        foreach (ulong seed in Seeds)
        {
            var designs = new List<string>[2];
            for (int i = 0; i < 2; i++)
            {
                SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = seed, Threads = 2, PilotChoice = i == 0 ? PilotChoice.Off : PilotChoice.On });
                for (long t = 0; t < 384; t++)
                    s.StepThroughMonth(t);
                SliceWorld w = s.World;
                designs[i] = [];
                for (int a = 0; a < w.Scenario.ActorCount; a++)
                {
                    for (int d = 0; d < w.ProductsLayout.Designs; d++)
                        designs[i].Add($"{a}/{d}:{w.Products.Stage(a, d)}:{string.Join(',', w.Products.Alternatives(a, d))}");
                }
            }
            differing += designs[0].SequenceEqual(designs[1]) ? 0 : 1;
        }
        Assert.True(differing > 0, "the pilot choice never changed a composition in 5 seeds");
    }

    [Theory]
    [InlineData(PilotChoice.On)]
    [InlineData(PilotChoice.OnAdmitted)]
    public void L1_L2_SameSeedSameHash_AcrossRunsAndThreads(PilotChoice pilot)
    {
        string a = HashOf(3, pilot, 1, 384);
        Assert.Equal(a, HashOf(3, pilot, 1, 384));
        Assert.Equal(a, HashOf(3, pilot, 4, 384));
    }
}
