using Singularity.Core.Science.ToyFunnel;
using Singularity.Kernel;

namespace Singularity.Core.Tests.ToyFunnel;

/// <summary>
/// Spec §7 on 200 seeds. The model itself throws on a violation (checked every tick); these tests
/// also re-check the observable invariants from outside so a silently disabled check would show up.
/// </summary>
public class ToyFunnelInvariantTests
{
    public static TheoryData<string, double> Points() => new()
    {
        { "A", 0.25 }, { "A", 1.0 }, { "B", 0.25 }, { "B", 1.0 }, { "C", 0.25 }, { "C", 1.0 },
    };

    [Theory]
    [MemberData(nameof(Points))]
    public void Hold_On200Seeds(string model, double nu)
    {
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var previous = new long[6][];
            ToyRun.Result r = ToyRun.Run(ToyRun.MainSeries(model, nu), seed, afterTick: (m, tick) => CheckTick(m, tick, previous));
            CheckMilestones(r);
            CheckYearly(r);
        }
    }

    private static void CheckTick(ToyFunnelModel m, long tick, long[][] previous)
    {
        Table table = m.Table;
        for (int i = 0; i < table.RowCount; i++)
        {
            long[] now =
            [
                table.GetLong("K").Current[i], table.GetLong("A").Current[i], table.GetLong("T").Current[i],
                table.GetLong("P").Current[i], table.GetLong("R").Current[i], table.GetLong("E").Current[i],
                table.GetInt("B").Current[i],
            ];
            for (int s = 0; s < 6; s++)
                Assert.InRange(now[s], 0, (1L << 53) - 1);
            if (previous[i] is { } before)
            {
                Assert.True(now[0] >= before[0], $"K decreased at tick {tick}, country {i}");
                Assert.True(now[6] >= before[6], $"B decreased at tick {tick}, country {i}");
                if (m.Config.Variant == ToyFunnelVariant.B)
                    Assert.True(now[0] == now[1] && now[1] == now[2], "B: K = A = T = tau");
                if (m.Config.Variant == ToyFunnelVariant.C)
                    Assert.Equal(now[1], now[2]);
                if (m.Config.Variant == ToyFunnelVariant.A)
                {
                    long lo = Math.Min(before[2], before[1]) - 1, hi = Math.Max(before[2], before[1]) + 1;
                    Assert.InRange(now[2], lo, hi);
                }
            }
            previous[i] = now;
        }
    }

    private static void CheckMilestones(ToyRun.Result r)
    {
        // Rows: country-major, j = 0..23; columns m_K, m_A, m_T are monotone in j and never skip a step.
        List<string[]> rows = r.Metrics.Tables[ToyFunnelModel.MilestonesTable];
        Assert.Equal(6 * 24, rows.Count);
        for (int k = 0; k < rows.Count; k++)
        {
            if (k % 24 == 0)
                continue;
            for (int col = 3; col <= 5; col++)
            {
                string prev = rows[k - 1][col], cur = rows[k][col];
                if (cur.Length == 0)
                    continue;
                Assert.NotEqual("", prev);
                Assert.True(long.Parse(cur) >= long.Parse(prev), "milestones must be monotone in j");
            }
        }
    }

    private static void CheckYearly(ToyRun.Result r)
    {
        foreach (string[] row in r.Metrics.Tables[ToyFunnelModel.YearlyTable])
        {
            Assert.True(ToyRun.Raw(row[9]) > 0, "Y_year must be positive");
            Assert.InRange(int.Parse(row[11]), 0, 12);
            if (r.Model.Config.Variant != ToyFunnelVariant.A)
                Assert.Equal("0", row[11]); // no engineering in the controls
        }
    }

    [Fact]
    public void BrokenParameters_RaiseInvariantViolation()
    {
        // rho0 = 100 makes staff overshoot below zero within two months.
        var values = ToyRun.Values("A", 0.25);
        int index = values.FindIndex(kv => kv.Key == "rho0");
        values[index] = new("rho0", new Singularity.Core.Simulation.ParamValue(Singularity.Core.Simulation.ParamKind.Number, "100"));
        var e = Assert.Throws<ToyFunnelInvariantException>(() => ToyRun.Run(new(values), seed: 1, ticks: 24));
        Assert.Contains("negative stock", e.Message);
        Assert.InRange(e.Tick, 0, 23);
    }

    [Fact]
    public void ZeroNoise_HasNoBreakthroughs()
    {
        ToyRun.Result r = ToyRun.Run(ToyRun.Params("A", 0.0), seed: 5);
        Assert.All(r.Metrics.Tables[ToyFunnelModel.YearlyTable], row => Assert.Equal("0", row[10]));
        Assert.All(r.Metrics.Tables[ToyFunnelModel.YearlyTable], row => Assert.Equal("0", row[12]));
    }
}
