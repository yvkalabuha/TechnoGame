using System.Globalization;
using System.Text.Json;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Tests.Knowledge;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Industry;

/// <summary>
/// lab/reference/jet_slice/out/trajectory.json bit for bit with knowledge and capability coupled (reference D4,
/// D7, D9): <c>culture_H</c>, <c>tooling_M</c>, <c>proc_X</c>, <c>proc_v</c>, <c>proc_yield</c>, the merge journal
/// of <c>report_output</c> and the culture details every month, plus the whole knowledge state.
/// </summary>
public class IndustryReferenceTrajectoryTests
{
    private static readonly string[] DimNames = ["precision", "purity", "quality_control"];

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 1)]
    [InlineData(8, 3)]
    public void CoupledDailyRun_MatchesReference_EveryMonth(int threads, int chunkSize)
    {
        var run = new JetIndustryRun(threads, chunkSize);
        for (long t = 0; t < JetReference.RowCount; t++)
        {
            run.RunThroughMonth(t);
            JsonElement row = JetReference.Row(t);
            AssertIndustryMonth(run, site: 0, row, t);
            KnowledgeReferenceTrajectoryTests.AssertMonth(run.Graph, run.Knowledge, 0, row, t);
        }
    }

    [Fact]
    public void PermutedProcessStorage_StillMatchesReference()
    {
        var run = new JetIndustryRun(processOrder: [1, 0]);
        Assert.Equal(6, run.Layout.Number[0]);
        for (long t = 0; t < JetReference.RowCount; t++)
        {
            run.RunThroughMonth(t);
            AssertIndustryMonth(run, 0, JetReference.Row(t), t);
        }
    }

    [Fact]
    public void OutputLag_FirstOutputOfPrc4AtMonth17_EntersCultureAt18()
    {
        // D9 (contract §4.0): output of month t reaches culture_H in compute t+1.
        var run = new JetIndustryRun();
        run.RunThroughMonth(16);
        Assert.DoesNotContain(run.Industry.LastReports, r => r.ProcessNumber == 4);
        run.RunThroughMonth(17);
        Assert.Contains(run.Industry.LastReports, r => r.ProcessNumber == 4);
        int met = Array.IndexOf(JetIndustryRun.Clusters, Cluster.MET);
        Fx6 v6 = run.Industry.ProcV(0, run.Layout.RowOf(6)), v4 = run.Industry.ProcV(0, run.Layout.RowOf(4));
        Assert.Equal(v6, run.Industry.LastCulture(0, met, Dimension.Precision).VC); // compute 17 used month 16: only PRC 6
        run.RunThroughMonth(18);
        Assert.Equal(v6 + v4, run.Industry.LastCulture(0, met, Dimension.Precision).VC);
    }

    [Fact]
    public void L1_TwoRuns_SameHashEveryMonth()
    {
        var a = new JetIndustryRun();
        var b = new JetIndustryRun();
        for (long t = 0; t < JetReference.RowCount; t++)
        {
            a.RunThroughMonth(t);
            b.RunThroughMonth(t);
            Assert.Equal(StateHash.ComputeHex(a.Table, 1, a.Runner.Tick), StateHash.ComputeHex(b.Table, 1, b.Runner.Tick));
        }
    }

    [Fact]
    public void L2_ThreeSites_SameHashForThreadCounts1_4_8()
    {
        // Three actors with one site each interact through known_elsewhere and world_lead; chunk size 1 gives
        // three chunks, so 4 and 8 threads really run them in parallel.
        int[] actors = [0, 1, 3];
        var runs = new[] { new JetIndustryRun(1, 1, actors), new JetIndustryRun(4, 1, actors), new JetIndustryRun(8, 1, actors), new JetIndustryRun(4, 2, actors) };
        foreach (JetIndustryRun r in runs)
            r.Lines.Requests = (t, s) => [new WorkforceRequest(s, 1, (s + 1) % 3, 4), new WorkforceRequest(s, 1, s, 3 + (int)(t % 5))];
        for (long t = 0; t < JetReference.RowCount; t++)
        {
            string? first = null;
            foreach (JetIndustryRun r in runs)
            {
                r.RunThroughMonth(t);
                string h = StateHash.ComputeHex(r.Table, 1, r.Runner.Tick);
                first ??= h;
                Assert.Equal(first, h);
            }
        }
    }

    internal static void AssertIndustryMonth(JetIndustryRun run, int site, JsonElement row, long t)
    {
        IndustrySystem ind = run.Industry;
        IndustryLayout l = run.Layout;
        JsonElement after = row.GetProperty("state_after");
        for (int c = 0; c < l.Clusters.Length; c++)
        {
            for (int d = 0; d < 3; d++)
            {
                string key = $"{l.Clusters[c]}.{DimNames[d]}";
                Assert.True(after.GetProperty("culture_H").GetProperty(key).GetInt64() == ind.CultureH(site, c, (Dimension)d).Raw, $"t={t} H {key}");
                Assert.True(after.GetProperty("tooling_M").GetProperty(key).GetInt64() == ind.ToolingM(site, c, (Dimension)d).Raw, $"t={t} M {key}");
                JsonElement det = row.GetProperty("culture_detail").GetProperty(key);
                CultureStep step = ind.LastCulture(site, c, (Dimension)d);
                Assert.Equal(det.GetProperty("V_c").GetInt64(), step.VC.Raw);
                Assert.Equal(det.GetProperty("dH_plus").GetInt64(), step.DHPlus.Raw);
                Assert.Equal(det.GetProperty("dH_minus").GetInt64(), step.DHMinus.Raw);
            }
        }
        for (int r = 0; r < l.ProcessCount; r++)
        {
            string p = l.Number[r].ToString(CultureInfo.InvariantCulture);
            Assert.True(after.GetProperty("proc_X").GetProperty(p).GetInt64() == ind.ProcX(site, r).Raw, $"t={t} X {p}");
            Assert.True(after.GetProperty("proc_v").GetProperty(p).GetInt64() == ind.ProcV(site, r).Raw, $"t={t} v {p}");
            Assert.True(after.GetProperty("proc_yield").GetProperty(p).GetInt64() == ind.ProcYield(site, r).Raw, $"t={t} y {p}");
            Assert.Equal(row.GetProperty("yield_used").GetProperty(p).GetInt64(), ind.ProcYield(site, r).Raw);
        }
        var expected = row.GetProperty("merge_report_output").EnumerateArray()
            .Select(x => (x[0].GetInt32(), x[1].GetInt32(), x[2].GetInt64(), x[3].GetInt64())).ToList();
        var actual = ind.LastReports.Where(x => x.Site == site).Select(x => (x.Site, x.ProcessNumber, x.Units.Raw, x.LabourHours.Raw)).ToList();
        Assert.Equal(expected, actual);
    }
}
