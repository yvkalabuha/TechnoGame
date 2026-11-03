using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Industry;

/// <summary>
/// Contract §8.4 "2 site" fixture as a unit test of the layer (spec §3.5 A27): actor A owns sites 0 and 1,
/// actor B owns site 2; one PRC. Site 2 never produces; its precision culture starts below H_floor.
/// </summary>
public class IndustryTwoSiteTests
{
    private const int P = 5; // process number
    private static readonly Fx6 Labour0 = Fx6.FromInt(100), Labour1 = Fx6.FromInt(30);

    private sealed class Inputs : IIndustryInputs
    {
        public bool KnowsProcess(int siteRow, int processRow, long month) => true;

        public Fx6 W(int siteRow, int cluster, long month) => Fx6.One;

        public Fx6 Hcap(int siteRow, Dimension dim, long month) => Fx6.FromInt(5);

        public bool QualityProgramme(int siteRow, long month) => false;

        public Fx6 ToolingInvest(int siteRow, int cluster, Dimension dim, long month) => Fx6.Zero;

        public Fx6 LicenceShare(int siteRow, int processRow, long month) => Fx6.Zero;

        // Different pools per site: site 0 is short, site 1 is not.
        public long WorkforceAvailable(int siteRow, int category, long month) => siteRow == 0 ? 5 : 100;

        public Fx6 AlloyCap(int siteRow, long month) => Fx6.Zero;

        public Fx6 AlloyDeficit(int siteRow, long month) => Fx6.Zero;

        public long KnownSince(int siteRow, int processRow, long month) => -1;
    }

    /// <summary>Month 0: only site 1 runs P; months 1…11 both A sites run it. Workforce asks on sites 0 and 1 in month 0.</summary>
    private sealed class Lines(IndustryMailbox box, bool reversed) : ITickSystem
    {
        public int SystemId => 301;

        public Cadence Cadence => Cadence.Monthly;

        public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd)
        {
            if (rowStart != 0)
                return;
            long t = context.Calendar!.MonthIndex(context.Tick);
            if (t >= 12)
                return; // tooling wears without investment: lines stop before the yield reaches 0
            var reports = new List<OutputReport> { new(1, P, Fx6.One, Labour1) };
            if (t > 0)
                reports.Add(new OutputReport(0, P, Fx6.FromInt(2), Labour0));
            var requests = new List<WorkforceRequest>();
            if (t == 0)
            {
                for (int site = 0; site < 2; site++)
                {
                    requests.Add(new WorkforceRequest(site, 1, 2, 4));
                    requests.Add(new WorkforceRequest(site, 1, 0, 6));
                }
            }
            if (reversed)
            {
                reports.Reverse();
                requests.Reverse();
            }
            foreach (OutputReport r in reports)
                box.ReportOutput(chunk, r);
            foreach (WorkforceRequest q in requests)
                box.ReserveWorkforce(chunk, q);
        }

        public void Merge(in TickContext context, int chunkCount) { }

        public void Post(in TickContext context) { }
    }

    private sealed class Rig
    {
        public Rig(bool reversed = false, int threads = 1)
        {
            Layout = IndustryLayout.Create([Cluster.MET],
                [new IndustryProcessSpec(P, Cluster.MET, 0, 3, new Dims3(Fx6.FromInt(3), Fx6.FromInt(3), Fx6.FromRaw(2_500_000)))],
                siteOwner: [0, 0, 1], workforceCategories: 2, skilledCategory: 1, requesters: 3);
            Start = new IndustryStart(Layout);
            for (int s = 0; s < 3; s++)
            {
                Fx6[] h = [s == 2 ? Fx6.FromRaw(2_500_000) : Fx6.FromInt(4), Fx6.FromRaw(3_800_000), Fx6.FromRaw(3_700_000)];
                for (int d = 0; d < 3; d++)
                {
                    Start.H[Start.Cell(s, 0, d)] = h[d];
                    Start.M[Start.Cell(s, 0, d)] = h[d] + Fx6.FromRaw(200_000);
                }
                Start.X[s] = Fx6.FromInt(10_000);
            }
            Table = new Table(3);
            Calendar = new TickCalendar(new CivilDate(1945, 9, 1));
            int chunks = TickRunner.ChunkCountFor(3, 1);
            System = new IndustrySystem(Table, Layout, Start, new IndustryParams(), new Inputs(), chunks, new V23CultureRule());
            Runner = new TickRunner(Table, 7, [System, new Lines(System.CreateMailbox(chunks, Cadence.Monthly), reversed)], 1, threads, Calendar);
        }

        public IndustryLayout Layout { get; }
        public IndustryStart Start { get; }
        public Table Table { get; }
        public TickCalendar Calendar { get; }
        public IndustrySystem System { get; }
        public TickRunner Runner { get; }

        public void RunThroughMonth(long month)
        {
            long last = Calendar.TickOfMonth(month);
            while (Runner.Tick <= last)
                Runner.RunTick();
        }

        public string Hash => StateHash.ComputeHex(Table, 1, Runner.Tick);
    }

    [Fact]
    public void WorldLead_OwnSecondSiteDoesNotCount_OtherActorDoes()
    {
        var rig = new Rig();
        rig.RunThroughMonth(0);
        Assert.True(rig.System.ProcYield(1, 0) >= Fx6.FromRaw(700_000));
        Assert.False(rig.System.WorldLead(0, 0)); // A: its own site 1 ran p
        Assert.False(rig.System.WorldLead(1, 0));
        Assert.True(rig.System.WorldLead(2, 0));  // B: another actor ran p with yield ≥ 0.7
    }

    [Fact]
    public void DisciplineVolume_SumsBothSitesOfTheActor()
    {
        var rig = new Rig();
        rig.RunThroughMonth(1);
        Assert.Equal(Labour0 + Labour1, rig.System.DisciplineVolume(0, 0));
        Assert.Equal(Fx6.Zero, rig.System.DisciplineVolume(1, 0));
    }

    [Fact]
    public void ShuffledMessages_SameStateEveryMonth()
    {
        var a = new Rig();
        var b = new Rig(reversed: true, threads: 4);
        for (long t = 0; t < 6; t++)
        {
            a.RunThroughMonth(t);
            b.RunThroughMonth(t);
            Assert.Equal(a.Hash, b.Hash);
            Assert.Equal(a.System.LastReports, b.System.LastReports);
        }
    }

    [Fact]
    public void Workforce_SeparatePoolsPerSite_GrantedVisibleNextMonth()
    {
        var rig = new Rig();
        Assert.Equal(0, rig.System.Granted(0, 1, 0));
        rig.RunThroughMonth(0);
        // Site 0 pool 5 for 10 asked: floor 3 (id 0), 2 (id 2), nothing left. Site 1 pool 100: everything.
        Assert.Equal((3L, 2L), (rig.System.Granted(0, 1, 0), rig.System.Granted(0, 1, 2)));
        Assert.Equal((6L, 4L), (rig.System.Granted(1, 1, 0), rig.System.Granted(1, 1, 2)));
        Assert.Equal(0, rig.System.Granted(2, 1, 0));
        rig.RunThroughMonth(1);
        Assert.Equal(0, rig.System.Granted(1, 1, 0)); // no request in month 1
    }

    [Fact]
    public void IdleSite_FallsToHFloorButNotBelow_BelowFloorStays()
    {
        var rig = new Rig();
        Fx6 floor = Fx6.FromRaw(600_000) * Fx6.FromInt(5); // φ_floor·Hcap = 3
        Fx6 prev = rig.System.CultureH(2, 0, Dimension.Purity);
        for (long t = 0; t < 240; t++)
        {
            rig.RunThroughMonth(t);
            Fx6 h = rig.System.CultureH(2, 0, Dimension.Purity);
            Assert.True(h <= prev && h >= floor, $"t={t} H={h}");
            prev = h;
            Assert.Equal(Fx6.FromRaw(2_500_000), rig.System.CultureH(2, 0, Dimension.Precision)); // below floor: no fall, no rise
        }
        Assert.True(prev < Fx6.FromRaw(3_800_000) - Fx6.FromRaw(300_000));
    }

    [Fact]
    public void TwoRuns_SameHash()
    {
        var a = new Rig();
        var b = new Rig();
        a.RunThroughMonth(11);
        b.RunThroughMonth(11);
        Assert.Equal(a.Hash, b.Hash);
    }
}
