using Singularity.Core.Institutes;
using Singularity.Kernel;
using Singularity.Session;
using Singularity.TestSupport;
using Xunit;

namespace Singularity.Session.Tests;

/// <summary>TASK-0199 (#589): the era price curve D and the AI rule «build an institute on a surplus».</summary>
public class EraPriceSurplusTests
{
    private static readonly SlotParams Off = new() { EraPrice = [], SurplusBuildFromYear = int.MaxValue };

    private static SliceSession Make(SlotParams? p, ulong seed = 3) =>
        SliceTestData.Create(new SliceRunConfig { Seed = seed, SlotParameters = p });

    [Fact]
    public void TheCurve_IsOneUpTo1990_InterpolatesLinearly_AndReachesSixteenIn2030()
    {
        Fx6[] t = new SlotParams().EraFactors()!;
        Assert.Equal(Fx6.One, t[1945 - 1945]);
        Assert.Equal(Fx6.One, t[1990 - 1945]);
        Assert.Equal(Fx6.FromRaw(1_500_000), t[1995 - 1945]);
        Assert.Equal(Fx6.FromInt(2), t[2000 - 1945]);
        Assert.Equal(Fx6.FromInt(16), t[2030 - 1945]);
        Assert.Equal(Fx6.FromInt(16), t[2100 - 1945]);
        for (int i = 1; i < t.Length; i++)
            Assert.True(t[i] >= t[i - 1]);
        Assert.Null(new SlotParams { EraPrice = [] }.EraFactors());
    }

    [Fact]
    public void ThePrice_ReadsTheCurveAtTheEarlierOfNodeYearAndCalendarYear_AndIsUntouchedBeforeTheCurve()
    {
        // The test data holds no node of the 1990s, so the curve is moved into its years: ×1 up to 1950, ×2 in 1960, ×10 in 2100.
        var curve = new SlotParams { EraPrice = [new(1950, Fx6.One), new(1960, Fx6.FromInt(2)), new(2100, Fx6.FromInt(10))] };
        SliceSession on = Make(curve), off = Make(Off);
        var g = on.World.Graph;
        int late = -1, mid = -1;
        for (int r = 0; r < g.Count; r++)
        {
            if (late < 0 && g.AnchorYear[r] >= 1961)
                late = r;
            if (mid < 0 && g.AnchorYear[r] is >= 1951 and <= 1959)
                mid = r;
        }
        Assert.True(late >= 0, "the data holds a node of 1961 or later");
        Fx6[] table = curve.EraFactors()!;
        long origin = on.World.Knowledge.CalendarOriginMonthsForTest;
        long At(int year) => 12L * (year - 1945) - origin;
        // Calendar year 1955, a late node: factor of 1955.
        Assert.Equal(off.World.Knowledge.CEff(0, late, At(1955)) * table[1955 - 1945], on.World.Knowledge.CEff(0, late, At(1955)));
        // Before 1990 the price is the old one, for every node.
        for (int r = 0; r < g.Count; r += 7)
            Assert.Equal(off.World.Knowledge.CEff(0, r, At(1949)), on.World.Knowledge.CEff(0, r, At(1949)));
        Assert.True(mid >= 0, "the data holds a node of 1951..1959");
        {
            // A node of the 1990s priced in 1990 is priced at its own year.
            Fx6 f = table[g.AnchorYear[mid] - 1945];
            Assert.Equal(off.World.Knowledge.CEff(0, mid, At(1990)) * f, on.World.Knowledge.CEff(0, mid, At(1990)));
        }
    }

    [Fact]
    public void TheWorldBefore1990_IsBitIdenticalWithAndWithoutTheCurveAndTheRule()
    {
        SliceSession on = Make(null), off = Make(Off);
        HashAssert.Equal(off.LastHash, on.LastHash, "start");
        for (long t = 0; t < 530; t++)
        {
            on.StepThroughMonth(t);
            off.StepThroughMonth(t);
            HashAssert.Equal(off.LastHash, on.LastHash, $"month {t}");
        }
        Assert.True(on.World.Calendar.DateOf(on.Tick).Year < 1990);
    }

    [Fact]
    public void TheSurplusRule_StartsAnInstituteEarlier_WhenTheAverageReturnIsAboveTheShare_AndNeverWithoutIt()
    {
        // The rule is moved into the 1940s so that a short run shows it: a candidate (DARPA, historical 1958) is started ahead of its date.
        var rule = new SlotParams { EraPrice = [], SurplusBuildFromYear = 1945, SurplusBuildShare = Fx6.FromRaw(1) };
        var never = new SlotParams { EraPrice = [], SurplusBuildFromYear = 1945, SurplusBuildShare = Fx6.FromInt(2) };   // a share cannot exceed 1
        SliceSession a = Make(rule), b = Make(never), c = Make(Off);
        Assert.Equal(DarpaStart(c, 120), DarpaStart(b, 120));
        long early = DarpaStart(a, 120);
        Assert.True(early >= 0 && early < DarpaStart(c, 0), $"started {early}");
    }

    private static long DarpaStart(SliceSession s, long months)
    {
        InstituteSystem inst = s.World.Institutes!;
        int usa = s.World.Scenario.ActorIndex("USA");
        var darpa = new InstituteHandle(usa, Enumerable.Range(0, inst.Layout.SlotCount(usa)).Single(k => inst.Layout.IdOf(new InstituteHandle(usa, k)) == "usa_darpa"));
        if (months == 0)
            return inst.Layout.FirstMonthOf(inst.Layout.RefYear(darpa)) - inst.BuildMonths(darpa);
        for (long t = 0; t < months * 5; t++)
        {
            s.StepThroughMonth(t);
            if (inst.BuildStart(darpa) >= 0)
                return inst.BuildStart(darpa);
        }
        return -1;
    }

    [Fact]
    public void ASlotParameterOverride_ChangesTheFingerprint_AndTheDefaultDoesNot()
    {
        var plain = new SliceRunConfig { Seed = 3 };
        Assert.False(plain.SnapshotFingerprint().AsSpan().SequenceEqual((plain with { SlotParameters = new SlotParams { EraPrice = [] } }).SnapshotFingerprint()));
        Assert.True(plain.SnapshotFingerprint().AsSpan().SequenceEqual((plain with { SlotParameters = null }).SnapshotFingerprint()));
    }

    [Fact]
    public void ARunWithTheCurveAndTheRule_IsTheSameOnOneAndTwoThreads()
    {
        var p = new SlotParams { SurplusBuildFromYear = 1945, SurplusBuildShare = Fx6.FromRaw(1) };
        SliceSession one = SliceTestData.Create(new SliceRunConfig { Seed = 5, Threads = 1, SlotParameters = p });
        SliceSession two = SliceTestData.Create(new SliceRunConfig { Seed = 5, Threads = 2, SlotParameters = p });
        for (long t = 0; t < 150; t++)
        {
            one.StepThroughMonth(t);
            two.StepThroughMonth(t);
        }
        HashAssert.Equal(one.LastHash, two.LastHash, "threads 1 and 2");
    }
}
