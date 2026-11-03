using System.Collections.Immutable;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Products;
using Singularity.Core.WhyNot;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>
/// T7: <c>why_not</c> (contract §6) on the assembled slice. Setups load columns of a fresh session (a fixture, never inside a tick) and
/// read the snapshot through <see cref="SliceSession.WhyNot"/>.
/// </summary>
public class WhyNotTests
{
    private const int Usa = 0;

    private static SliceSession New(SliceRunConfig? config = null) => SliceTestData.Create(config);

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

    /// <summary>Culture and tooling far above every requirement, experience far above X½: capability stands in nobody's way.</summary>
    private static void StrongCapability(SliceSession s, int row, bool withExperience = true)
    {
        foreach (Column col in s.World.Table.Columns)
        {
            if (col.Name.StartsWith("ind.H.", StringComparison.Ordinal) || col.Name.StartsWith("ind.M.", StringComparison.Ordinal))
                SetLong(s, col.Name, row, Fx6.FromInt(9).Raw);
            else if (col.Name.StartsWith("ind.X.", StringComparison.Ordinal))
                SetLong(s, col.Name, row, withExperience ? Fx6.FromInt(1_000_000).Raw : 0);
        }
    }

    private static int[] Superior(SliceSession s, int row, int gen)
    {
        ProductsLayout l = s.World.ProductsLayout;
        var alts = new int[l.SlotCount];
        bool Knows(int n) => s.World.Knowledge.IsKnown(row, s.World.Graph.RowOf(n));
        Assert.True(DesignRules.Superior(l, gen, Knows, alts));
        return alts;
    }

    /// <summary>A design of the actor in slot 0, written into the columns (stage, platform, alternatives, defects).</summary>
    private static void PutDesign(SliceSession s, int row, int gen, DesignStage stage, Fx6 hidden, Fx6 d0, int stageOMonth = 0)
    {
        int[] alts = Superior(s, row, gen);
        const string n = "prd.d0";
        SetInt(s, n + ".stage", row, (int)stage);
        SetInt(s, n + ".gp", row, gen);
        SetInt(s, n + ".parent", row, -1);
        SetInt(s, n + ".bureau", row, -1);
        SetInt(s, n + ".stageO", row, stageOMonth);
        for (int i = 0; i < alts.Length; i++)
            SetInt(s, n + ".alt." + i, row, alts[i]);
        SetLong(s, n + ".d0", row, d0.Raw);
        SetLong(s, n + ".hidden", row, hidden.Raw);
    }

    private static string Render(IEnumerable<Link> links) =>
        string.Join("\n", links.Select(l => $"{l.Layer}|{l.Cause}|{l.SubjectId}|{l.Have}|{l.Need}|{string.Join(",", l.Levers)}|{l.YieldIfFixed}|{l.EtaMonths}|{l.Process}|{l.Dim}|{l.Limiter}|{l.VariantSlot}"));

    // ---- the four «first layer» scenarios (spec §6.4, contract §6 test)

    [Fact]
    public void Scenario_KnowledgeFirst_ApproachingGenerationUnknown()
    {
        SliceSession s = New();
        IReadOnlyList<Link> links = s.WhyNot(Usa, WhyNotTarget.Requirement(2));
        Link first = Assert.Single(links);
        Assert.Equal(WhyNotLayer.Knowledge, first.Layer);
        Assert.Equal(WhyNotCause.MissingNode, first.Cause);
        Assert.Equal(new[] { Lever.ResearchPriority }, first.Levers.ToArray());
        Assert.NotNull(first.EtaMonths);
        // The same link through the APP node id.
        Assert.Equal(Render(links), Render(s.WhyNot(Usa, WhyNotTarget.App(s.World.ProductsLayout.GenApp[1]))));
    }

    [Fact]
    public void Scenario_CapabilityFirst_KnownGenerationWithWeakCulture()
    {
        SliceSession s = New();
        IReadOnlyList<Link> links = s.WhyNot(Usa, WhyNotTarget.Requirement(1));
        Assert.Equal(WhyNotLayer.Capability, links[0].Layer);
        Assert.Equal(WhyNotCause.CultureH, links[0].Cause);
        Assert.NotNull(links[0].Limiter);
        Assert.True(links[0].Have < links[0].Need);
        Assert.True(links[0].YieldIfFixed >= links[^1].YieldIfFixed || links[^1].Layer != WhyNotLayer.Capability);
        Assert.DoesNotContain(links, l => l.Layer == WhyNotLayer.Knowledge);
    }

    [Fact]
    public void Scenario_ProductFirst_NoProjectWhenCapabilityStands()
    {
        SliceSession s = New();
        StrongCapability(s, Usa);
        IReadOnlyList<Link> links = s.WhyNot(Usa, WhyNotTarget.Requirement(1));
        Link first = Assert.Single(links);
        Assert.Equal(WhyNotLayer.Product, first.Layer);
        Assert.Equal(WhyNotCause.NoDesign, first.Cause);
        Assert.True(first.YieldIfFixed >= WhyNotAnalyzer.LowYield);
    }

    [Fact]
    public void Scenario_EconomyFirst_AdmittedButNotAffordable()
    {
        SliceSession s = New();
        StrongCapability(s, Usa);
        PutDesign(s, Usa, 1, DesignStage.Prototype, Fx6.Zero, Fx6.FromInt(100));
        SetInt(s, "prd.d0.gateOpened", Usa, 1);
        Assert.All(s.WhyNot(Usa, WhyNotTarget.Design(0)), l => Assert.Equal(WhyNotLayer.Economy, l.Layer));
        // The standing upkeep alone exceeds the procurement budget.
        SetLong(s, "prd.upkeepNeed", Usa, Fx6.FromInt(1_000_000_000).Raw);
        IReadOnlyList<Link> links = s.WhyNot(Usa, WhyNotTarget.Design(0));
        Link first = Assert.Single(links);
        Assert.Equal(WhyNotLayer.Economy, first.Layer);
        Assert.Equal(WhyNotCause.NotAffordable, first.Cause);
        Assert.True(first.Have < first.Need);
        Assert.Empty(LeverValidator.Check(links, LeverRegistry.Slice));
    }

    // ---- links

    [Fact]
    public void Engineers_BelowNinetyPercentGranted_IsALink_AtNinetyIsNot()
    {
        SliceSession s = New();
        StrongCapability(s, Usa);
        PutDesign(s, Usa, 1, DesignStage.Project, Fx6.FromInt(5), Fx6.FromInt(5));
        SetInt(s, "prd.d0.bureau", Usa, 0);
        ProductsLayout l = s.World.ProductsLayout;
        string n = $"ind.wf.n.{ProductsLayout.EngineerCategory}.{l.BureauRequester(0)}", g = $"ind.wf.granted.{ProductsLayout.EngineerCategory}.{l.BureauRequester(0)}";
        SetLong(s, n, Usa, 100);
        SetLong(s, g, Usa, 89);
        Link e = Assert.Single(s.WhyNot(Usa, WhyNotTarget.Design(0)), x => x.Cause == WhyNotCause.Engineers);
        Assert.Equal(WhyNotLayer.Product, e.Layer);
        Assert.Equal(Fx6.FromInt(89), e.Have);
        Assert.Equal(Fx6.FromInt(100), e.Need);
        Assert.Equal(new[] { Lever.ReassignWorkforce }, e.Levers.ToArray());
        SetLong(s, g, Usa, 90);
        Assert.DoesNotContain(s.WhyNot(Usa, WhyNotTarget.Design(0)), x => x.Cause == WhyNotCause.Engineers);
    }

    [Fact]
    public void SeriesGate_ClosedGate_ShowsHiddenAgainstLevel_WithEtaAndPlayerOnlyLever()
    {
        SliceSession s = New();
        StrongCapability(s, Usa);
        ProductsSystem p = s.World.Products;
        Fx6 level = new ProductsParams().KGate * p.DRef;
        PutDesign(s, Usa, 1, DesignStage.Prototype, level + Fx6.FromInt(50), level + Fx6.FromInt(60), stageOMonth: 0);
        SetLong(s, "prd.d0.hours", Usa, 0);
        SetInt(s, "prd.d0.gateOpened", Usa, 0);
        Link gate = Assert.Single(s.WhyNot(Usa, WhyNotTarget.Design(0)), x => x.Cause == WhyNotCause.SeriesGate);
        Assert.Equal(level + Fx6.FromInt(50), gate.Have);
        Assert.Equal(level, gate.Need);
        Assert.Equal(new[] { Lever.ForceSeries }, gate.Levers.ToArray());
        Assert.True(LeverRegistry.PlayerOnly(Lever.ForceSeries));
        Assert.True(gate.EtaMonths is > 0);
        // The gate level is met and the minimum tests are done: no link.
        SetLong(s, "prd.d0.hidden", Usa, level.Raw);
        SetInt(s, "prd.d0.gateOpened", Usa, 1);
        Assert.DoesNotContain(s.WhyNot(Usa, WhyNotTarget.Design(0)), x => x.Cause == WhyNotCause.SeriesGate);
    }

    [Fact]
    public void WithdrawnProject_GivesNoLinks_AndDoesNotCountAsAProject()
    {
        SliceSession s = New();
        StrongCapability(s, Usa);
        PutDesign(s, Usa, 1, DesignStage.Withdrawn, Fx6.FromInt(500), Fx6.FromInt(500));
        Assert.Empty(s.WhyNot(Usa, WhyNotTarget.Design(0)));
        Link first = Assert.Single(s.WhyNot(Usa, WhyNotTarget.Requirement(1)));
        Assert.Equal(WhyNotCause.NoDesign, first.Cause);
    }

    [Fact]
    public void EmptySlot_GivesNoLinks()
    {
        Assert.Empty(New().WhyNot(Usa, WhyNotTarget.Design(3)));
    }

    [Fact]
    public void RampLink_ExperienceBelowThePointWhereRampReachesTheThreshold()
    {
        SliceSession s = New();
        StrongCapability(s, Usa, withExperience: false);
        IReadOnlyList<Link> links = s.WhyNot(Usa, WhyNotTarget.Requirement(1));
        Link ramp = links.First(l => l.Cause == WhyNotCause.Ramp);
        Assert.Equal(WhyNotLayer.Capability, ramp.Layer);
        Assert.Null(ramp.Dim);
        Assert.Equal(Fx6.Zero, ramp.Have);
        Assert.Contains(Lever.Licence, ramp.Levers);
        Assert.Null(ramp.EtaMonths);
        // At the need, ramp is at the yield threshold (to rounding).
        Fx6 xHalf = s.World.Industry.Yield(Usa, s.World.IndustryLayout.RowOf(ramp.Process), 0).XHalfEff;
        Fx6 atNeed = IndustryMath.Ramp(ramp.Need, xHalf, s.World.IndustryParameters.Lambda0);
        Assert.True(atNeed >= WhyNotAnalyzer.LowYield - Fx6.FromRaw(100) && atNeed <= WhyNotAnalyzer.LowYield + Fx6.FromRaw(100));
        Assert.Empty(LeverValidator.Check(links, LeverRegistry.Slice));
    }

    [Fact]
    public void AltUnavailable_ApproachedGenerationWithKnownAppButMissingComponents()
    {
        SliceSession s = New();
        int app = s.World.ProductsLayout.GenApp[1];
        SetInt(s, $"know.known.{app}", Usa, 1);
        IReadOnlyList<Link> links = s.WhyNot(Usa, WhyNotTarget.Requirement(2));
        Assert.Equal(WhyNotCause.AltUnavailable, links[0].Cause);
        Assert.Equal(WhyNotLayer.Knowledge, links[0].Layer);
        Assert.Equal(new[] { Lever.ResearchPriority }, links[0].Levers.ToArray());
    }

    [Fact]
    public void KnowledgeEta_IsTheCeilingOfRemainingProgressOverTheSnapshotRate()
    {
        SliceSession s = New();
        Link link = Assert.Single(s.WhyNot(Usa, WhyNotTarget.Requirement(2)));
        int row = s.World.Graph.RowOf(link.SubjectId);
        Fx6 rate = s.World.Knowledge.ProgressRate(Usa, row, 0);
        Assert.True(rate > Fx6.Zero);
        long eta = link.EtaMonths!.Value;
        Assert.True(Fx6.FromRaw(rate.Raw * eta) >= link.Need - link.Have || rate.Raw * eta >= (link.Need - link.Have).Raw);
        Assert.True(rate.Raw * (eta - 1) < (link.Need - link.Have).Raw);
    }

    // ---- properties of the list

    [Fact]
    public void Limiter_IsNeverNone_WhenYieldIsBelowThreshold_AtHSource()
    {
        SliceSession s = New();
        for (long month = 0; month < 36; month += 12)
        {
            for (int a = 0; a < s.World.Scenario.ActorCount; a++)
            {
                for (int g = 1; g <= s.World.ProductsLayout.Generations; g++)
                {
                    foreach (Link l in s.WhyNot(a, WhyNotTarget.Requirement(g)).Where(l => l.Layer == WhyNotLayer.Capability && l.Cause == WhyNotCause.CultureH))
                        Assert.True(l.Limiter is not null and not LimiterKind.None, $"actor {a} gen {g} process {l.Process}");
                }
            }
            s.StepThroughMonth(month + 11);
        }
    }

    [Fact]
    public void List_IsSortedByTheContractOrder()
    {
        SliceSession s = New();
        for (int a = 0; a < s.World.Scenario.ActorCount; a++)
        {
            for (int g = 1; g <= s.World.ProductsLayout.Generations; g++)
            {
                IReadOnlyList<Link> links = s.WhyNot(a, WhyNotTarget.Requirement(g));
                for (int i = 1; i < links.Count; i++)
                    Assert.True(Link.Compare(links[i - 1], links[i]) <= 0);
            }
        }
    }

    [Fact]
    public void Compare_LayerThenYieldDescendingThenEtaNullLastThenSubject()
    {
        Link Make(WhyNotLayer layer, long y, int? eta, int subject) =>
            new(layer, WhyNotCause.MissingNode, subject, Fx6.Zero, Fx6.One, [], Fx6.FromRaw(y), eta);
        Link[] shuffled =
        [
            Make(WhyNotLayer.Product, 9, 1, 1),
            Make(WhyNotLayer.Capability, 5, null, 2),
            Make(WhyNotLayer.Capability, 5, 7, 9),
            Make(WhyNotLayer.Capability, 8, 9, 3),
            Make(WhyNotLayer.Capability, 5, 7, 4),
            Make(WhyNotLayer.Knowledge, 0, null, 5),
        ];
        var sorted = shuffled.ToList();
        sorted.Sort(Link.Compare);
        Assert.Equal([5, 3, 4, 9, 2, 1], sorted.Select(l => l.SubjectId));
        var reversed = shuffled.Reverse().ToList();
        reversed.Sort(Link.Compare);
        Assert.Equal(sorted.Select(l => l.SubjectId), reversed.Select(l => l.SubjectId));
    }

    // ---- the validator (spec §5.6 d)

    [Fact]
    public void Validator_AcceptsRealLists_OnEveryActorAndGeneration()
    {
        SliceSession s = New();
        var all = new List<Link>();
        for (int a = 0; a < s.World.Scenario.ActorCount; a++)
        {
            for (int g = 1; g <= s.World.ProductsLayout.Generations; g++)
                all.AddRange(s.WhyNot(a, WhyNotTarget.Requirement(g)));
        }
        Assert.NotEmpty(all);
        Assert.Empty(LeverValidator.Check(all, LeverRegistry.Slice));
    }

    [Fact]
    public void Validator_FlagsADisabledLever_AMismatchedLever_AndAMissingLever()
    {
        Link know = new(WhyNotLayer.Knowledge, WhyNotCause.MissingNode, 1, Fx6.Zero, Fx6.One, [Lever.ResearchPriority], Fx6.Zero, 3);
        Assert.Empty(LeverValidator.Check([know], LeverRegistry.Slice));
        Assert.Single(LeverValidator.Check([know], new LeverRegistry([Lever.TrainingSeries])), p => p.Contains("not enabled"));
        Link wrong = know with { Levers = [Lever.ReassignWorkforce] };
        Assert.Contains(LeverValidator.Check([wrong], LeverRegistry.Slice), p => p.Contains("not offered"));
        Link missing = know with { Levers = ImmutableArray<Lever>.Empty };
        Assert.Contains(LeverValidator.Check([missing], LeverRegistry.Slice), p => p.Contains("missing"));
        Assert.Empty(LeverValidator.Check([missing], new LeverRegistry([Lever.TrainingSeries])));
    }

    [Fact]
    public void Levers_FollowTheBuild_ProcessDevelopmentOffTheEffortZeroAblation()
    {
        SliceSession s = New(new SliceRunConfig { Seed = 7, Channels = SliceChannels.EffortZero });
        var all = new List<Link>();
        for (int a = 0; a < s.World.Scenario.ActorCount; a++)
        {
            for (int g = 1; g <= s.World.ProductsLayout.Generations; g++)
                all.AddRange(s.WhyNot(a, WhyNotTarget.Requirement(g)));
        }
        Assert.DoesNotContain(all, l => l.Levers.Contains(Lever.ProcessDevelopment));
    }

    // ---- purity and determinism

    [Fact]
    public void WhyNot_ChangesNothing_AndRepeats()
    {
        SliceSession s = New();
        s.StepThroughMonth(13);
        string hash = StateHash.ComputeHex(s.World.Table, SliceWorld.SchemaVersion, s.Tick);
        string first = Render(s.WhyNot(Usa, WhyNotTarget.Requirement(1)));
        string second = Render(s.WhyNot(Usa, WhyNotTarget.Requirement(1)));
        Assert.Equal(first, second);
        Assert.Equal(hash, StateHash.ComputeHex(s.World.Table, SliceWorld.SchemaVersion, s.Tick));
        Assert.Equal(hash, s.LastHash);
    }

    [Fact]
    public void WhyNot_SameForThreadsAndChunks_AndWithOrWithoutEarlierCalls()
    {
        string Run(int threads, int chunk, bool probe)
        {
            SliceSession s = New(new SliceRunConfig { Seed = 11, Threads = threads, ChunkSize = chunk });
            var sb = new System.Text.StringBuilder();
            for (int m = 0; m < 3; m++)
            {
                s.StepThroughMonth(m * 4);
                if (probe)
                    s.WhyNot(1, WhyNotTarget.Requirement(2));
                for (int a = 0; a < s.World.Scenario.ActorCount; a++)
                {
                    for (int g = 1; g <= 4; g++)
                        sb.AppendLine(Render(s.WhyNot(a, WhyNotTarget.Requirement(g))));
                }
            }
            return sb.ToString() + s.LastHash;
        }

        string baseline = Run(1, 1, false);
        Assert.Equal(baseline, Run(4, 1, false));
        Assert.Equal(baseline, Run(3, 2, true));
        Assert.Equal(baseline, Run(8, 3, true));
    }

    [Fact]
    public void CurrentRequirement_IsTheNewestKnownGenerationNotFielded()
    {
        SliceSession s = New();
        for (int a = 0; a < s.World.Scenario.ActorCount; a++)
            Assert.Null(s.CurrentRequirement(a));
        int app = s.World.ProductsLayout.GenApp[1];
        SetInt(s, $"know.known.{app}", Usa, 1);
        Assert.Equal(WhyNotTarget.Requirement(2), s.CurrentRequirement(Usa));
    }

    [Fact]
    public void Targets_OutOfRange_AreRejected()
    {
        SliceSession s = New();
        Assert.Throws<ArgumentOutOfRangeException>(() => s.WhyNot(Usa, WhyNotTarget.Requirement(0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => s.WhyNot(Usa, WhyNotTarget.Design(99)));
        Assert.Throws<ArgumentException>(() => s.WhyNot(Usa, WhyNotTarget.App(1)));
    }
}
