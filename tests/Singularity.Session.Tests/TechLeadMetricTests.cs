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
            SliceSession s = SliceTestData.Create(SliceRunConfig.Legacy with { Seed = seed, Threads = 2 });
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
        SliceSession s = SliceTestData.Create(SliceRunConfig.Legacy with { Seed = seed, Threads = threads, ChunkSize = threads == 1 ? 1 : 2 });
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
        SliceWorld w = SliceTestData.Create(SliceRunConfig.Legacy with { Seed = 1 }).World;
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

    // Codex finding: the fleet forecast uses the rule of FieldedL on the counts after Take (oldest first), not the total left.
    [Fact]
    public void RetireForecast_FieldedAfter_CountsOnlyUnitsOfCohortsWithQ()
    {
        SliceSession s = SliceTestData.Create(SliceRunConfig.Legacy with { Seed = 1 });
        SliceWorld w = s.World;
        s.StepThroughMonth(100);                                            // the empty design slots get alternatives, as in FleetRuleTests
        const int usa = 0;
        void SetLong(string name, long value)
        {
            Singularity.Kernel.LongColumn c = w.Table.GetLong(name);
            long[] all = c.Current.ToArray();
            all[usa] = value;
            c.Load(all);
        }
        for (int y = 0; y < w.ProductsLayout.Years; y++)
            SetLong($"prd.d0.cohort.{w.ProductsLayout.StartYear + y}", y == 0 || y == 5 ? Fx6.FromInt(50).Raw : 0);   // 50 old units (q) and 50 newer ones (no q)
        SetLong("prd.d0.q", 1);
        foreach (Singularity.Kernel.Column col in w.Table.Columns.Where(c => c.Name.StartsWith("ind.mr.", StringComparison.Ordinal)).ToList())
        {
            Singularity.Kernel.IntColumn ic = w.Table.GetInt(col.Name);
            int[] all = ic.Current.ToArray();
            all[usa] = 0;                                                   // nothing is mastered now
            ic.Load(all);
        }
        Assert.False(w.Products.FieldedMastered(usa, 0, 101));
        Assert.True(w.Products.FieldedL(usa, 0, 101));                        // n_Q = 50
        FleetForecast oldest = w.Products.RetireForecast(usa, 0, 50, 101);    // Take removes the oldest 50: the qualifying ones
        Assert.True(oldest.FieldedBefore);
        Assert.False(oldest.FieldedAfter);                                  // 50 units are left, but none of them has q
        Assert.False(w.Products.RetireForecast(usa, 0, 1, 101).FieldedAfter); // 49 qualifying units left
    }
}
