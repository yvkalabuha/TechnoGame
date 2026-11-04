using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Policy;
using Singularity.Core.WhyNot;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>
/// Erratum F25 (contract §6 r7, spec §11 T6d п. 6): blocking links (<c>g_d = 0</c>) come first within the layer; with <c>H ≤ r_d − 1</c> the cause is the
/// culture even if <c>M &lt; H</c>, and the <c>M</c> link of the same cell follows it; <c>M ≤ r_d − 1 &lt; H</c> is a blocking <c>M</c> link as before.
/// </summary>
public class WhyNotF25Tests
{
    private static void SetLong(SliceSession s, string name, int row, long value)
    {
        LongColumn c = s.World.Table.GetLong(name);
        long[] all = c.Current.ToArray();
        all[row] = value;
        c.Load(all);
    }

    private static void SetInt(SliceSession s, string name, int row, int value)
    {
        IntColumn c = s.World.Table.GetInt(name);
        int[] all = c.Current.ToArray();
        all[row] = value;
        c.Load(all);
    }

    // Every cell of the actor is strong and every node known, then one cell is set to (H, M): the links of the requirement of G2 are those of that cell.
    private static (SliceSession S, int Process, Dimension Dim, int Cluster, Fx6 Req) Fixture(Func<Fx6, (Fx6 H, Fx6 M)> cell)
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 7 });
        const int usa = 0;
        foreach (Column col in s.World.Table.Columns)
        {
            if (col.Name.StartsWith("know.known.", StringComparison.Ordinal))
                SetInt(s, col.Name, usa, 1);
        }
        Link target = s.WhyNot(usa, WhyNotTarget.Requirement(2)).First(l => l.Layer == WhyNotLayer.Capability && l.Cause == WhyNotCause.CultureH && l.Dim is not null);
        foreach (Column col in s.World.Table.Columns)
        {
            if (col.Name.StartsWith("ind.H.", StringComparison.Ordinal) || col.Name.StartsWith("ind.M.", StringComparison.Ordinal))
                SetLong(s, col.Name, usa, Fx6.FromInt(9).Raw);
            else if (col.Name.StartsWith("ind.X.", StringComparison.Ordinal))
                SetLong(s, col.Name, usa, Fx6.FromInt(1_000_000).Raw);
        }
        var il = s.World.IndustryLayout;
        int row = il.RowOf(target.Process), cluster = il.ClusterIndex[row], d = (int)target.Dim!.Value;
        Fx6 req = il.Req[row][d];
        (Fx6 h, Fx6 m) = cell(req);
        string[] hCols = [.. s.World.Table.Columns.Where(c => c.Name.StartsWith("ind.H.", StringComparison.Ordinal) && !c.Name.StartsWith("ind.H.unmet", StringComparison.Ordinal)).Select(c => c.Name)];
        string[] mCols = [.. s.World.Table.Columns.Where(c => c.Name.StartsWith("ind.M.", StringComparison.Ordinal)).Select(c => c.Name)];
        SetLong(s, hCols[cluster * Dims3.Count + d], usa, h.Raw);
        SetLong(s, mCols[cluster * Dims3.Count + d], usa, m.Raw);
        return (s, target.Process, target.Dim!.Value, cluster, req);
    }

    [Fact]
    public void BothBelowRMinus1_AndMBelowH_TheCauseIsTheCulture_AndTheMLinkFollowsIt()
    {
        var (s, _, _, _, _) = Fixture(req => (req - Fx6.FromRaw(1_500_000), req - Fx6.FromRaw(2_000_000)));
        List<Link> cap = [.. s.WhyNot(0, WhyNotTarget.Requirement(2)).Where(l => l.Layer == WhyNotLayer.Capability)];
        Assert.True(cap.Count >= 2);
        Assert.True(cap[0].Blocking && cap[0].Cause == WhyNotCause.CultureH && !cap[0].Companion);
        Assert.True(cap[1].Companion && cap[1].Cause is WhyNotCause.ToolingM or WhyNotCause.ToolingPoolExhausted);
        Assert.Equal((cap[0].Process, cap[0].Dim), (cap[1].Process, cap[1].Dim));
        // Every companion stands right behind a host of its own cell.
        List<Link> all = [.. s.WhyNot(0, WhyNotTarget.Requirement(2))];
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].Companion)
                Assert.True(all[i - 1].Blocking && all[i - 1].Process == all[i].Process && all[i - 1].Dim == all[i].Dim);
        }
        // Blocking links come before every other link of the layer.
        int firstOpen = cap.FindIndex(l => !l.Blocking);
        if (firstOpen >= 0)
            Assert.DoesNotContain(cap.Skip(firstOpen), l => l.Blocking);
    }

    [Fact]
    public void MBelowRMinus1_HAbove_IsTheBlockingMLinkAsBefore_WithoutACompanion()
    {
        var (s, _, _, _, _) = Fixture(req => (req + Fx6.One, req - Fx6.FromRaw(1_500_000)));
        List<Link> cap = [.. s.WhyNot(0, WhyNotTarget.Requirement(2)).Where(l => l.Layer == WhyNotLayer.Capability)];
        Assert.True(cap[0].Blocking && cap[0].Cause is WhyNotCause.ToolingM or WhyNotCause.ToolingPoolExhausted);
        Assert.DoesNotContain(cap, l => l.Companion);
    }

    [Fact]
    public void HJustAboveRMinus1_IsNotBlockedByTheCulture_TheSourceIsDecidedByA5()
    {
        var (s, _, _, _, _) = Fixture(req => (req - Fx6.FromRaw(900_000), req - Fx6.FromRaw(1_500_000)));
        List<Link> cap = [.. s.WhyNot(0, WhyNotTarget.Requirement(2)).Where(l => l.Layer == WhyNotLayer.Capability)];
        Assert.True(cap[0].Blocking);                                           // M ≤ r − 1: g_d = 0
        Assert.Equal(WhyNotCause.ToolingM, cap[0].Cause is WhyNotCause.ToolingPoolExhausted ? WhyNotCause.ToolingM : cap[0].Cause);
        Assert.DoesNotContain(cap, l => l.Companion);
    }

    // FRA seed 7, month 348 on the data of D1 (the T-fleet world: T6D_OFF) — the state of the diagnosis fra-swe-zero-diagnosis.md §4: the cause of the zero
    // output was listed last; after F25 the first CAPABILITY link is the culture of process 54 in quality_control.
    [Fact]
    public void FraSeed7_Month348_FirstCapabilityLinkIsCultureHOfProcess54_QualityControl()
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 7, T6dOff = true });
        int fra = s.World.Scenario.ActorIndex("FRA");
        s.StepThroughMonth(347);
        WhyNotTarget? target = s.CurrentRequirement(fra);
        Assert.NotNull(target);
        Link first = s.WhyNot(fra, target!.Value).First(l => l.Layer == WhyNotLayer.Capability);
        Assert.Equal((WhyNotCause.CultureH, 54, (Dimension?)Dimension.QualityControl), (first.Cause, first.Process, first.Dim));
    }
}
