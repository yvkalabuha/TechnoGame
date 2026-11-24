using Singularity.Core.Commands;
using Singularity.Core.Policy;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>TASK-0150: the reserve account, shares at most 1, FundProgramme; neutrality against the build without the reserve.</summary>
public class ReserveTests
{
    private static SliceSession Make(bool reserve, int threads = 1) =>
        SliceTestData.Create(new SliceRunConfig { Seed = 7, Reserve = reserve, Threads = threads });

    private static BudgetShares Shares(long r, long c, long d, long p, long q) =>
        new(Fx6.FromRaw(r), Fx6.FromRaw(c), Fx6.FromRaw(d), Fx6.FromRaw(p), Fx6.FromRaw(q));

    // Half of the budget goes to the reserve.
    private static readonly BudgetShares Half = Shares(150_000, 100_000, 50_000, 200_000, 0);

    private static SetBudgetShares SetHalf() => new(0, Half);

    private static int FrontierNode(SliceSession s)
    {
        for (int r = 0; r < s.World.Graph.Count; r++)
        {
            if (s.World.Knowledge.InFrontier(0, r) && !s.World.Graph.IsHazard[r])
                return r;
        }
        throw new InvalidOperationException("no frontier node");
    }

    private static Fx6 Balance(SliceSession s) => s.World.Reserve!.Balance(0);

    [Fact]
    public void Neutral_SharesSumToOne_NoProgrammes_OldColumnsEqualTheRunWithoutReserve()
    {
        SliceSession off = Make(false), on = Make(true);
        for (long t = 0; t < 36; t++)
        {
            off.StepThroughMonth(t);
            on.StepThroughMonth(t);
            if (t % 6 == 5)
                HashAssert.Equal(ColumnProjection.Hash(off, _ => true), ColumnProjection.Hash(on, n => !ColumnProjection.IsReserveColumn(n)), $"an old column moved by month {t}");
        }
        // The run with the flag off is the run of main: the default config and Reserve = false share one fingerprint and the very same hash.
        SliceSession main = SliceTestData.Create(new SliceRunConfig { Seed = 7 });
        for (long t = 0; t < 36; t++)
            main.StepThroughMonth(t);
        HashAssert.Equal(main.LastHash, off.LastHash, "Reserve = false is not the run of main");
        Assert.Equal(new SliceRunConfig { Seed = 7 }.SnapshotFingerprint(), new SliceRunConfig { Seed = 7, Reserve = false }.SnapshotFingerprint());
        Assert.NotEqual(new SliceRunConfig { Seed = 7 }.SnapshotFingerprint(), new SliceRunConfig { Seed = 7, Reserve = true }.SnapshotFingerprint());
        for (int a = 0; a < on.World.Table.RowCount; a++)
            Assert.Equal(Fx6.Zero, on.World.Reserve!.Balance(a));
        Assert.DoesNotContain(off.World.Table.Columns, c => ColumnProjection.IsReserveColumn(c.Name));
    }

    [Fact]
    public void Validator_AcceptsLessThanOneOnlyWithTheReserve()
    {
        BudgetShares under = Half, over = Shares(300_000, 300_000, 300_000, 100_001, 0);
        Assert.Equal(CommandRefusal.BadShares, Make(false).Submit(SourceClass.Player, 0, 0, new SetBudgetShares(0, under)).Reason);
        SliceSession s = Make(true);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new SetBudgetShares(0, under)).Accepted);
        Assert.Equal(CommandRefusal.BadShares, s.Submit(SourceClass.Player, 0, 1, new SetBudgetShares(0, over)).Reason);
        Assert.Equal(CommandRefusal.BadShares, s.Submit(SourceClass.Player, 0, 2, new SetBudgetShares(0, Shares(1_100_000, -100_000, 0, 0, 0))).Reason);
    }

    [Fact]
    public void Account_AddsTheRemainderAndLeaksEveryMonth()
    {
        SliceSession s = Make(true);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, SetHalf()).Accepted);
        Fx6 leak = s.World.Reserve!.Parameters.LeakPerMonth;
        s.StepThroughMonth(0);
        // A first deposit does not leak in its month (I3 order): the account holds exactly the remainder of the opening budget.
        Assert.Equal(Fx6.FromRaw(500_000) * s.World.BudgetAtStart[0], Balance(s));
        for (long t = 1; t < 24; t++)
        {
            Fx6 before = Balance(s);
            Fx6 inflow = s.World.SharesOf(0).Reserve * s.World.Policy.Budget(0);
            Assert.Equal(Fx6.FromRaw(500_000), s.World.SharesOf(0).Reserve);
            s.StepThroughMonth(t);
            Assert.Equal(before - before * leak + inflow, Balance(s));   // I3, exact
            Assert.True(Balance(s) > Fx6.Zero);
        }
        // About 10 % a year: twelve months of leak keep 0.9 of a sum within a rounding of the constant.
        Fx6 kept = Fx6.One;
        for (int i = 0; i < 12; i++)
            kept = kept - kept * leak;
        Assert.InRange(kept.Raw, 899_900, 900_100);
    }

    [Fact]
    public void FundProgramme_AddsProgressByTheSameCEffAndPaysFromTheReserve()
    {
        SliceSession a = Make(true), b = Make(true);
        foreach (SliceSession s in new[] { a, b })
        {
            Assert.True(s.Submit(SourceClass.Player, 0, 0, SetHalf()).Accepted);
            for (long t = 0; t < 12; t++)
                s.StepThroughMonth(t);
        }
        int row = FrontierNode(a);
        int number = a.World.Graph.Number[row];
        Fx6 amount = Balance(a) / Fx6.FromInt(20);
        long month = 12;
        Fx6 gain = a.World.Knowledge.ProgrammeGain(0, row, month, amount);
        Assert.True(gain > Fx6.Zero);
        Assert.Equal(amount / a.World.Knowledge.CEff(0, row, month), gain);   // first programme: f = 1
        Assert.True(a.Submit(SourceClass.Player, 0, 1, new FundProgramme(0, number, amount)).Accepted);
        a.StepThroughMonth(month);
        b.StepThroughMonth(month);
        Assert.Contains(a.Outcomes, o => o.Contains("accepted"));
        Assert.Equal(gain, a.World.Knowledge.Prog(0, row) - b.World.Knowledge.Prog(0, row));
        Assert.Equal(1, a.World.Knowledge.ProgrammesOn(0, row));
        Assert.Equal(0, b.World.Knowledge.ProgrammesOn(0, row));
        // I3: what left is exactly the amount (the spend of the month does not leak).
        Assert.InRange((Balance(b) - Balance(a)).Raw, amount.Raw - 2, amount.Raw + 2);

        // Second programme on the same node: diminishing return 1/(1+k), k = 1.
        Fx6 half = a.World.Knowledge.ProgrammeGain(0, row, month + 1, amount);
        Assert.Equal(amount / a.World.Knowledge.CEff(0, row, month + 1) / Fx6.FromInt(2), half);
    }

    [Fact]
    public void FundProgramme_NearTheCeiling_SpendsNoMoreThanItGives()
    {
        SliceSession a = Make(true), b = Make(true);
        foreach (SliceSession s in new[] { a, b })
        {
            Assert.True(s.Submit(SourceClass.Player, 0, 0, SetHalf()).Accepted);
            for (long t = 0; t < 12; t++)
                s.StepThroughMonth(t);
        }
        int row = FrontierNode(a);
        Fx6 cap = a.World.Knowledge.ProgrammeCapacity(0, row, 12);
        Fx6 amount = cap * Fx6.FromInt(2);   // twice what the node can take
        Assert.True(cap > Fx6.Zero && Balance(a) > amount, $"setup: cap {cap}, balance {Balance(a)}");
        Assert.True(a.Submit(SourceClass.Player, 0, 1, new FundProgramme(0, a.World.Graph.Number[row], amount)).Accepted);
        a.StepThroughMonth(12);
        b.StepThroughMonth(12);
        Assert.InRange((Balance(b) - Balance(a)).Raw, cap.Raw - 2, cap.Raw + 2);   // only the capacity left the account
        Assert.True(a.World.Knowledge.IsKnown(0, row) || a.World.Knowledge.Prog(0, row) <= Fx6.One);
        Assert.True(a.World.Knowledge.IsKnown(0, row) || a.World.Knowledge.Prog(0, row) > b.World.Knowledge.Prog(0, row));
    }

    [Fact]
    public void Validator_WithTheReserve_RefusesAShareOnTheQualityItem()
    {
        SliceSession s = Make(true);
        Assert.Equal(CommandRefusal.BadShares, s.Submit(SourceClass.Player, 0, 0, new SetBudgetShares(0, Shares(150_000, 100_000, 50_000, 200_000, 10_000))).Reason);
    }

    [Fact]
    public void Validator_OutOfRangeShares_AreRefusedNotThrown()
    {
        foreach (bool reserve in new[] { false, true })
        {
            SliceSession s = Make(reserve);
            long big = long.MaxValue / 2;
            Assert.Equal(CommandRefusal.BadShares, s.Submit(SourceClass.Player, 0, 0, new SetBudgetShares(0, Shares(big, big, big, big, big))).Reason);
            Assert.Equal(CommandRefusal.BadShares, s.Submit(SourceClass.Player, 0, 1, new SetBudgetShares(0, Shares(long.MinValue, long.MaxValue, 0, 0, 0))).Reason);
        }
    }

    [Fact]
    public void FundProgramme_TooSmallToMoveTheNode_TakesNothingAndIsRefused()
    {
        SliceSession s = Make(true);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, SetHalf()).Accepted);
        for (long t = 0; t < 12; t++)
            s.StepThroughMonth(t);
        int row = FrontierNode(s);
        Assert.Equal(Fx6.Zero, s.World.Knowledge.ProgrammeGain(0, row, 12, Fx6.FromRaw(1)));
        Assert.True(s.Submit(SourceClass.Player, 0, 1, new FundProgramme(0, s.World.Graph.Number[row], Fx6.FromRaw(1))).Accepted);
        s.StepThroughMonth(12);
        Assert.Contains(s.Outcomes, o => o.Contains("rejected: ProgrammeNotFundable") && o.Contains("too small"));
        Assert.Equal(0, s.World.Knowledge.ProgrammesOn(0, row));
    }

    [Fact]
    public void FundProgramme_IsRefusedWhereKnowledgeIsAScheduleAndNeverCrossesOne()
    {
        SliceSession sched = SliceTestData.CreateLegacy(new SliceRunConfig { Seed = 7, Reserve = true, Layer = LayerControl.Lk0 });
        Assert.Equal(CommandRefusal.ReserveOff, sched.Submit(SourceClass.Player, 0, 0, new FundProgramme(0, 1, Fx6.One)).Reason);
        // Every frontier node of every actor, plain or hazard: the capacity leaves the node at or below 1.
        SliceSession s = Make(true);
        for (long t = 0; t < 6; t++)
            s.StepThroughMonth(t);
        for (int a = 0; a < s.World.Table.RowCount; a++)
        {
            for (int r = 0; r < s.World.Graph.Count; r++)
            {
                if (!s.World.Knowledge.InFrontier(a, r))
                    continue;
                Fx6 cap = s.World.Knowledge.ProgrammeCapacity(a, r, 6);
                Fx6 basis = s.World.Knowledge.Prog(a, r) + s.World.Knowledge.ProgressRate(a, r, 6);
                Assert.True(basis + s.World.Knowledge.ProgrammeGain(a, r, 6, cap) <= (basis > Fx6.One ? basis : Fx6.One), $"actor {a} node {r}");
            }
        }
    }

    [Fact]
    public void MonthlyLeak_ComesFromTheYearlyRateInOnePlace()
    {
        Assert.Equal(Fx6.FromRaw(8_742), new Singularity.Core.Reserve.ReserveParams().LeakPerMonth);
    }

    [Fact]
    public void FundProgramme_IsRefusedWithoutReserveAndBeyondTheBalance()
    {
        Assert.Equal(CommandRefusal.ReserveOff, Make(false).Submit(SourceClass.Player, 0, 0, new FundProgramme(0, 1, Fx6.One)).Reason);
        SliceSession s = Make(true);
        Assert.Equal(CommandRefusal.BadAmount, s.Submit(SourceClass.Player, 0, 0, new FundProgramme(0, s.World.Graph.Number[FrontierNode(s)], Fx6.Zero)).Reason);
        Assert.Equal(CommandRefusal.BadNode, s.Submit(SourceClass.Player, 0, 1, new FundProgramme(0, 9999, Fx6.One)).Reason);
        // Empty reserve: accepted by the form, refused at application with a reason, the money is not taken and the node does not move.
        Assert.True(s.Submit(SourceClass.Player, 0, 2, new FundProgramme(0, s.World.Graph.Number[FrontierNode(s)], Fx6.FromInt(1))).Accepted);
        s.StepThroughMonth(0);
        Assert.Contains(s.Outcomes, o => o.Contains("rejected: ProgrammeNotFundable"));
        Assert.Equal(0, s.World.Knowledge.ProgrammesOn(0, FrontierNode(s)));
    }

    [Fact]
    public void Programmes_ReplayAndThreadCount_GiveTheSameHash()
    {
        string Run(int threads, out string log)
        {
            SliceSession s = Make(true, threads);
            Assert.True(s.Submit(SourceClass.Player, 0, 0, SetHalf()).Accepted);
            for (long t = 0; t < 24; t++)
            {
                if (t is 10 or 11 or 16)
                    Assert.True(s.Submit(SourceClass.Player, 0, (int)t + 1, new FundProgramme(0, s.World.Graph.Number[FrontierNode(s)], Fx6.FromInt(1))).Accepted);
                s.StepThroughMonth(t);
            }
            log = s.CommandLog;
            return s.LastHash;
        }
        string h1 = Run(1, out string log), h2 = Run(2, out _);
        HashAssert.Equal(h1, h2, "L2: thread count changed the state");
        Assert.Contains("fund_programme", log);
        SliceSession replay = SliceSession.Replay(SliceTestData.Data, new SliceRunConfig { Seed = 7, Reserve = true }, log, Make(true).World.Calendar.TickOfMonth(23) + 1);
        HashAssert.Equal(h1, replay.LastHash, "L1: the replay differs");
    }

    [Fact]
    public void Codec_RoundTripsFundProgramme()
    {
        var logged = new LoggedCommand(new CommandKey(5, SourceClass.Player, 0, 3), new FundProgramme(0, 47, Fx6.FromRaw(1_234_567)));
        Assert.Equal(logged.Command, CommandCodec.Decode(CommandCodec.Encode(logged)).Command);
    }
}
