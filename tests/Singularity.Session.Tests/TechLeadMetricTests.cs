using System.Security.Cryptography;
using System.Text;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>Contract §9.1 r13a (#247), run level: <c>fielded ⊆ fielded_L</c>, the same first entry in one run, and the leadership text is deterministic (L1/L2).</summary>
public class TechLeadMetricTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly ulong[] Seeds = [1, 2, 3, 5, 8, 13];

    [Fact]
    public void FieldedL_IsSupersetOfFielded_AndFirstEntryIsTheSameInOneRun()
    {
        int entries = 0, superOnly = 0;
        foreach (ulong seed in Seeds)
        {
            SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = seed, Threads = 2 });
            SliceWorld w = s.World;
            int n = w.Scenario.ActorCount, designs = w.ProductsLayout.Designs;
            var firstL = new long[n, designs];
            var firstR6 = new long[n, designs];
            for (int a = 0; a < n; a++)
                for (int d = 0; d < designs; d++)
                    firstL[a, d] = firstR6[a, d] = -1;
            for (long t = 0; t < 384; t++)
            {
                s.StepThroughMonth(t);
                for (int a = 0; a < n; a++)
                {
                    for (int d = 0; d < designs; d++)
                    {
                        bool r6 = w.Products.FieldedMastered(a, d, t + 1), l = w.Products.FieldedL(a, d, t + 1);
                        Assert.True(!r6 || l, $"seed {seed} month {t} actor {a} design {d}: fielded without fielded_L");
                        if (r6 && firstR6[a, d] < 0)
                            firstR6[a, d] = t;
                        if (l && firstL[a, d] < 0)
                            firstL[a, d] = t;
                        superOnly += l && !r6 ? 1 : 0;
                    }
                }
            }
            for (int a = 0; a < n; a++)
            {
                for (int d = 0; d < designs; d++)
                {
                    Assert.True(firstL[a, d] == firstR6[a, d], $"seed {seed} actor {a} design {d}: first entry fielded_L {firstL[a, d]} vs fielded {firstR6[a, d]}");
                    entries += firstL[a, d] >= 0 ? 1 : 0;
                }
            }
        }
        output.WriteLine($"{entries} first entries compared; {superOnly} (month, actor, design) cells in fielded_L outside fielded");
        Assert.True(entries > 0, "no design ever entered the fleet in service: the test is vacuous");
    }

    // The leadership text of a run: key (G, T, N, P), fielded_L set and G_front of every actor and month.
    private static string LeadershipDigest(ulong seed, int threads, int months)
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = seed, Threads = threads, ChunkSize = threads == 1 ? 1 : 2 });
        SliceWorld w = s.World;
        var sb = new StringBuilder();
        for (long t = 0; t < months; t++)
        {
            s.StepThroughMonth(t);
            sb.Append(t).Append(' ').Append(w.Products.GFront(t + 1)).Append('\n');
            for (int a = 0; a < w.Scenario.ActorCount; a++)
            {
                TechKey k = w.Products.KeyOf(a, t + 1);
                sb.Append(a).Append(',').Append(k.G).Append(',').Append(k.T).Append(',').Append(k.N).Append(',').Append(k.P.Raw).Append(':');
                for (int d = 0; d < w.ProductsLayout.Designs; d++)
                    sb.Append(w.Products.FieldedL(a, d, t + 1) ? '1' : '0');
                sb.Append('\n');
            }
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    [Fact]
    public void LeadershipCsv_IsIdentical_TwoRunsAndOneVsFourThreads()
    {
        string a = LeadershipDigest(3, 1, 240);
        Assert.Equal(a, LeadershipDigest(3, 1, 240));
        Assert.Equal(a, LeadershipDigest(3, 4, 240));
    }

    // The scenario fleet gets q at t0 (contract r13a): every cohort of a design whose processes the actor has mastered at t0, and no other.
    [Fact]
    public void ScenarioFleet_HasQAtT0_ExactlyWhereTheProcessesAreMastered()
    {
        SliceWorld w = SliceTestData.Create(new SliceRunConfig { Seed = 1 }).World;
        int withQ = 0, withoutQ = 0;
        for (int a = 0; a < w.Scenario.ActorCount; a++)
        {
            for (int d = 0; d < w.ProductsLayout.Designs; d++)
            {
                if (w.Products.Stage(a, d) == DesignStage.None)
                    continue;
                bool mastered = w.Products.PrcSet(a, d).All(p => w.Products.Mastered(a, p, 0));
                for (int y = 0; y < w.ProductsLayout.Years; y++)
                {
                    bool has = w.Products.Cohort(a, d, w.ProductsLayout.StartYear + y) > Fx6.Zero;
                    Assert.Equal(has && mastered, w.Products.CohortMastered(a, d, y));
                    withQ += has && mastered ? 1 : 0;
                    withoutQ += has && !mastered ? 1 : 0;
                }
            }
        }
        Assert.True(withQ > 0, "no scenario cohort of a mastered design: the test is vacuous");
    }
}
