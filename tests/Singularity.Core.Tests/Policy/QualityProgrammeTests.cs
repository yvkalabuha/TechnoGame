using System.Collections.Immutable;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Policy;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Policy;

/// <summary>Spec §3.5 A41: the quality programme (state, reserve_workforce, the P2 trigger, 84 months) and the training-series target of P1.</summary>
public class QualityProgrammeTests
{
    private const int Bureau = 0, QpReq = 2;

    private sealed class PolicyIn : IPolicyInputs
    {
        public Func<long, bool> Below = _ => false;
        public int Newest = -1;
        public BottleneckLimit? Limit;
        public int NewestSeriesDesign(int actorRow, long month) => Newest;
        public BottleneckLimit? Bottleneck(int actorRow, int design, long month) => Limit;
        public bool SeriesBelowQuality(int actorRow, Fx6 threshold, long month) => Below(month);
    }

    private sealed class IndIn(long avail) : IIndustryInputs
    {
        public Fx6 EffortBudget(int actorRow, long month) => Fx6.Zero;
        public int EffortFocus(int actorRow, int k, long month) => -1;
        public bool Planned(int siteRow, int processRow, long month) => false;
        public Fx6 PlannedVolume(int siteRow, int processRow, long month) => Fx6.Zero;
        public bool KnowsProcess(int siteRow, int processRow, long month) => true;
        public Fx6 W(int siteRow, int cluster, long month) => Fx6.One;
        public Fx6 Hcap(int siteRow, Dimension dim, long month) => Fx6.FromInt(5);
        public bool QualityProgramme(int siteRow, long month) => false;
        public Fx6 ToolingInvest(int siteRow, int cluster, Dimension dim, long month, Fx6 restActor) => Fx6.Zero;
        public Fx6 LicenceShare(int siteRow, int processRow, long month) => Fx6.Zero;
        public long WorkforceAvailable(int siteRow, int category, long month) => avail;
        public Fx6 AlloyCap(int siteRow, long month) => Fx6.Zero;
        public Fx6 AlloyDeficit(int siteRow, long month) => Fx6.Zero;
        public long KnownSince(int siteRow, int processRow, long month) => -1;
    }

    // A bureau that asks for engineers every month, under the lowest requester id.
    private sealed class BureauAsk(IndustryMailbox box, long n) : ITickSystem
    {
        public int SystemId => 301;
        public Cadence Cadence => Cadence.Monthly;
        public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd) => box.ReserveWorkforce(chunk, new WorkforceRequest(0, 0, Bureau, n));
        public void Merge(in TickContext context, int chunkCount) { }
        public void Post(in TickContext context) { }
    }

    private sealed class Rig
    {
        public Rig(PolicyProfile profile, PolicyIn inputs, long avail = 1_000_000, long bureauAsk = 0)
        {
            var layout = IndustryLayout.Create([Cluster.MET],
                [new IndustryProcessSpec(5, Cluster.MET, 0, 3, new Dims3(Fx6.FromInt(3), Fx6.FromInt(3), Fx6.FromInt(3)))],
                siteOwner: [0], workforceCategories: 2, skilledCategory: 1, requesters: 3);
            var table = new Table(1);
            Calendar = new TickCalendar(new CivilDate(1945, 9, 1));
            Industry = new IndustrySystem(table, layout, new IndustryStart(layout), new IndustryParams(), new IndIn(avail), 1, new V23CultureRule());
            IndustryMailbox box = Industry.CreateMailbox(1, Cadence.Monthly);
            Policy = new PolicySystem(table, new PolicyParams(), [0], [Fx6.One], [Fx6.Zero], new CounterRng(1), inputs, Industry.CreateMailbox(1, Cadence.Monthly),
                QpReq, [(int)profile]);
            Policy.Initialize();
            ITickSystem[] systems = bureauAsk > 0 ? [Industry, new BureauAsk(box, bureauAsk), Policy] : [Industry, Policy];
            Runner = new TickRunner(table, 1, systems, 1, 1, Calendar);
        }

        public TickCalendar Calendar { get; }
        public IndustrySystem Industry { get; }
        public PolicySystem Policy { get; }
        public TickRunner Runner { get; }

        public void RunThroughMonth(long month)
        {
            long last = Calendar.TickOfMonth(month);
            while (Runner.Tick <= last)
                Runner.RunTick();
        }
    }

    [Fact]
    public void P1_StartsAtT0_AsksForEngineersEveryMonth_AndEndsAfter84Months()
    {
        var rig = new Rig(PolicyProfile.Quality, new PolicyIn());
        PolicySystem p = rig.Policy;
        Assert.True(p.QualityProgrammeActive(0));          // in force already in month 0 (A8)
        Assert.Equal(400, p.QualityEngineers(0));          // ⌊0,02·⌊20 000·1⌋⌋
        for (long t = 0; t < 84; t++)
        {
            rig.RunThroughMonth(t);
            Assert.Equal(400, rig.Industry.Granted(0, 0, QpReq));
            Assert.Equal(t < 83, p.QualityProgrammeActive(0));   // after step t the column holds month t+1
        }
        rig.RunThroughMonth(84);
        Assert.False(p.QualityProgrammeActive(0));
        Assert.Equal(0, rig.Industry.Granted(0, 0, QpReq));      // no request any more
        Assert.Equal(new PolicyParams().Shares(PolicyProfile.Quality).Quality, p.Shares(0).Quality); // the 0,05 item stays in the profile, unspent
    }

    [Fact]
    public void BureausOutrankTheProgrammeInRounding_AndAPartialGrantIsVisible()
    {
        var rig = new Rig(PolicyProfile.Quality, new PolicyIn(), avail: 400 + 700, bureauAsk: 700);
        rig.RunThroughMonth(0);
        Assert.Equal(700, rig.Industry.Granted(0, 0, Bureau));
        Assert.Equal(400, rig.Industry.Granted(0, 0, QpReq));
        // Short pool: Σ asks 1 100 for 1 000 people: ⌊n·avail/Σ⌋ for both, the single leftover person goes to the lower id (the bureau).
        var tight = new Rig(PolicyProfile.Quality, new PolicyIn(), avail: 1_000, bureauAsk: 700);
        tight.RunThroughMonth(0);
        Assert.Equal(636 + 1, tight.Industry.Granted(0, 0, Bureau));   // ⌊700·1000/1100⌋ = 636 and the leftover
        Assert.Equal(363, tight.Industry.Granted(0, 0, QpReq));        // ⌊400·1000/1100⌋ = 363 < qp_engineers
    }

    [Fact]
    public void P2_StartsAfter24ConsecutiveMonths_AtTheAnnualStep_ReturnsSharesAfter84Months()
    {
        var rig = new Rig(PolicyProfile.Mass, new PolicyIn { Below = _ => true });
        PolicySystem p = rig.Policy;
        PolicyParams pp = new();
        Assert.False(p.QualityProgrammeActive(0));
        for (long t = 0; t < 24; t++)
        {
            rig.RunThroughMonth(t);
            Assert.False(p.QualityProgrammeActive(0), $"month {t}");
        }
        Assert.Equal(24, p.QualityTriggerCount(0));
        rig.RunThroughMonth(24);                           // the annual step: the programme acts from month 25
        Assert.True(p.QualityProgrammeActive(0));
        Assert.Equal(25, p.QualityProgrammeStart(0));
        Assert.Equal(pp.MassProgrammeShares, p.Shares(0));
        Assert.Equal(Fx6.FromRaw(450_000), p.Shares(0).Procurement);
        Assert.Equal(Fx6.One, p.Shares(0).Sum);
        for (long t = 25; t < 108; t++)
        {
            rig.RunThroughMonth(t);
            Assert.True(p.QualityProgrammeActive(0), $"month {t}");
        }
        rig.RunThroughMonth(108);                          // month 109 = start + 84: the programme is over, the shares are back
        Assert.False(p.QualityProgrammeActive(0));
        Assert.Equal(pp.Shares(PolicyProfile.Mass), p.Shares(0));
        Assert.Equal(Fx6.Zero, p.Shares(0).Quality);
    }

    [Fact]
    public void P2_CounterResetsOnAGap_AndStartWaitsForTheAnnualStep()
    {
        var rig = new Rig(PolicyProfile.Mass, new PolicyIn { Below = t => t != 10 });
        for (long t = 0; t <= 35; t++)
        {
            rig.RunThroughMonth(t);
            Assert.False(rig.Policy.QualityProgrammeActive(0), $"month {t}");
        }
        Assert.Equal(25, rig.Policy.QualityTriggerCount(0));   // months 11…35
        rig.RunThroughMonth(36);
        Assert.True(rig.Policy.QualityProgrammeActive(0));
        Assert.Equal(37, rig.Policy.QualityProgrammeStart(0));
        Assert.Equal(0, rig.Policy.QualityTriggerCount(0));
    }

    [Fact]
    public void P1AndP3_DoNotStartAProgrammeFromTheP2Trigger()
    {
        var rig = new Rig(PolicyProfile.Adaptive, new PolicyIn { Below = _ => true });
        for (long t = 0; t <= 48; t++)
            rig.RunThroughMonth(t);
        Assert.False(rig.Policy.QualityProgrammeActive(0));
        Assert.Equal(0, rig.Policy.QualityTriggerCount(0));
    }

    [Fact]
    public void TrainingTarget_OnlyForP1_OnlyWhenTheBottleneckIsHAndLimitedByVolumeOrLab()
    {
        BottleneckLimit Ok(LimiterKind k, BottleneckSource s = BottleneckSource.H) => new(s, k);
        (PolicyProfile Profile, BottleneckLimit? Limit, int Expect)[] cases =
        [
            (PolicyProfile.Quality, Ok(LimiterKind.Volume), 3),
            (PolicyProfile.Quality, Ok(LimiterKind.LabCeiling), 3),
            (PolicyProfile.Quality, Ok(LimiterKind.Effort), -1),
            (PolicyProfile.Quality, Ok(LimiterKind.Tooling), -1),
            (PolicyProfile.Quality, Ok(LimiterKind.Volume, BottleneckSource.M), -1),
            (PolicyProfile.Quality, null, -1),
            (PolicyProfile.Mass, Ok(LimiterKind.Volume), -1),
            (PolicyProfile.Adaptive, Ok(LimiterKind.Volume), -1),   // P3 needs why_not (T6d)
        ];
        foreach ((PolicyProfile profile, BottleneckLimit? limit, int expect) in cases)
        {
            var rig = new Rig(profile, new PolicyIn { Newest = 3, Limit = limit });
            Assert.Equal(-1, rig.Policy.TrainingTarget(0));
            rig.RunThroughMonth(0);
            Assert.Equal(expect, rig.Policy.TrainingTarget(0));
        }
        var none = new Rig(PolicyProfile.Quality, new PolicyIn { Newest = -1, Limit = Ok(LimiterKind.Volume) });
        none.RunThroughMonth(0);
        Assert.Equal(-1, none.Policy.TrainingTarget(0));
    }
}
