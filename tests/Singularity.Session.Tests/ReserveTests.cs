using Singularity.Core.Commands;
using Singularity.Core.Policy;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>TASK-0150: the reserve account, shares at most 1, FundProgramme; neutrality against the build without the reserve.</summary>
public class ReserveTests
{
    private static SliceSession Make(bool reserve, int threads = 1) =>
        SliceTestData.Create(SliceRunConfig.Legacy with { Seed = 7, Reserve = reserve, Threads = threads });

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
        SliceSession main = SliceTestData.Create(SliceRunConfig.Legacy with { Seed = 7 });
        for (long t = 0; t < 36; t++)
            main.StepThroughMonth(t);
        HashAssert.Equal(main.LastHash, off.LastHash, "Reserve = false is not the run of main");
        Assert.Equal((SliceRunConfig.Legacy with { Seed = 7 }).SnapshotFingerprint(), (SliceRunConfig.Legacy with { Seed = 7, Reserve = false }).SnapshotFingerprint());
        Assert.NotEqual((SliceRunConfig.Legacy with { Seed = 7 }).SnapshotFingerprint(), (SliceRunConfig.Legacy with { Seed = 7, Reserve = true }).SnapshotFingerprint());
        for (int a = 0; a < on.World.Table.RowCount; a++)
            Assert.Equal(Fx6.Zero, on.World.Reserve!.Balance(a));
        Assert.DoesNotContain(off.World.Table.Columns, c => ColumnProjection.IsReserveColumn(c.Name));
    }

    // Recorded from main before TASK-0150 (e011cbb), the same run (seed 7, 36 months, default config): the hash over ALL columns and the state hash.
    // Re-recorded in TASK-0180 (#555): requires of 11 early G5/G6 nodes (62, 63, 82, 83, 86, 90, 91, 92, 95, 97, 99) changed, so knowledge opens later and every trajectory moves; no schema change.
    private const string MainAllColumns = "63c89962438a26fe43798ac43b173a0b12af0a4489a38a8d695dc17f48d85163";
    // Re-recorded in TASK-0182: schema v15, new column pol.flt.gFront; old columns unchanged (ColumnProjection hash not changed).
    private const string MainStateHash = "df4899cc12d3d0d66b8de9f7e2fb0b4a83dfae7911ba3509702ec88fe5d42fa7";

    [Fact]
    public void FlagOff_IsBitIdenticalToMainBeforeTheTask_AndFlagOnEqualsItOnTheOldColumns()
    {
        foreach (bool reserve in new[] { false, true })
        {
            SliceSession s = Make(reserve);
            for (long t = 0; t < 36; t++)
                s.StepThroughMonth(t);
            HashAssert.Equal(MainAllColumns, ColumnProjection.Hash(s, n => !ColumnProjection.IsReserveColumn(n)));
            if (!reserve)
                HashAssert.Equal(MainStateHash, s.LastHash);
        }
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
    public void QualityShare_WithTheReserve_IsValidAndGoesToTheReserve()
    {
        SliceSession s = Make(true);
        // A full-budget split that keeps a share on the unread quality item stays valid; that share is kept in the reserve, not lost.
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new SetBudgetShares(0, Shares(300_000, 150_000, 100_000, 400_000, 50_000))).Accepted);
        s.StepThroughMonth(0);
        Assert.Equal(Fx6.FromRaw(50_000), s.World.ReserveShare(0));
        Assert.Equal(Fx6.FromRaw(50_000) * s.World.BudgetAtStart[0], Balance(s));
        Assert.Equal(Fx6.Zero, s.World.ReserveShare(1));   // an AI actor feeds nothing
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
        SliceSession sched = SliceTestData.CreateLegacy(SliceRunConfig.Legacy with { Seed = 7, Reserve = true, Layer = LayerControl.Lk0 });
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
    public void FundProgramme_CannotSpendMoneyTheLeakHasTaken_BalanceNeverGoesNegative()
    {
        SliceSession s = Make(true);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, SetHalf()).Accepted);
        s.StepThroughMonth(0);
        s.StepThroughMonth(1);
        int row = FrontierNode(s);
        Fx6 balance = Balance(s), spendable = s.World.Reserve!.Spendable(0);
        Assert.True(spendable < balance && spendable > Fx6.Zero);
        // The whole balance is more than the month lets a programme take: refused, nothing is taken.
        Assert.True(s.Submit(SourceClass.Player, 0, 1, new FundProgramme(0, s.World.Graph.Number[row], balance)).Accepted);
        s.StepThroughMonth(2);
        Assert.Contains(s.Outcomes, o => o.Contains("rejected: ProgrammeNotFundable") && o.Contains("after its leak"));
        // Exactly the spendable amount: allowed (if the node can take it), and the balance after the month is the inflow alone, never negative.
        Fx6 b2 = Balance(s), sp2 = s.World.Reserve.Spendable(0);
        Fx6 cap = s.World.Knowledge.ProgrammeCapacity(0, row, 3);
        Fx6 amount = sp2 < cap ? sp2 : cap;
        Fx6 inflow = s.World.SharesOf(0).Reserve * s.World.Policy.Budget(0);
        Assert.True(s.Submit(SourceClass.Player, 0, 2, new FundProgramme(0, s.World.Graph.Number[row], amount)).Accepted);
        s.StepThroughMonth(3);
        Assert.Equal(b2 - b2 * s.World.Reserve.Parameters.LeakPerMonth + inflow - amount, Balance(s));
        Assert.True(Balance(s) >= Fx6.Zero);
    }

    [Fact]
    public void LicenceMoney_I1AndI2_HoldWithTheReserveOnAndProgrammesRunning()
    {
        SliceSession s = Make(true);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, SetHalf()).Accepted);
        int licenceMonths = 0;
        for (long t = 0; t < 120; t++)
        {
            s.StepThroughMonth(t);
            Fx6 sum = Fx6.Zero;
            for (int a = 0; a < s.World.Table.RowCount; a++)
            {
                Fx6 flow = s.World.Policy.LicenceFlow(a);
                sum += flow;
                // I2: the procurement item moves by exactly the flow (what the buyer pays is what the seller gets).
                Assert.Equal(flow, s.World.ProcurementBudget(a) - s.World.ProcurementBase(a));
                Assert.True(Balance(s) >= Fx6.Zero);
            }
            Assert.Equal(Fx6.Zero, sum);   // I1
            if (Enumerable.Range(0, s.World.Table.RowCount).Any(a => s.World.Policy.LicenceFlow(a) != Fx6.Zero))
                licenceMonths++;
        }
        Assert.True(licenceMonths > 0, "no licence deal in 120 months: the invariants were not exercised");
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
        SliceSession replay = SliceSession.Replay(SliceTestData.Data, SliceRunConfig.Legacy with { Seed = 7, Reserve = true }, log, Make(true).World.Calendar.TickOfMonth(23) + 1);
        HashAssert.Equal(h1, replay.LastHash, "L1: the replay differs");
    }

    [Fact]
    public void Codec_RoundTripsFundProgramme()
    {
        var logged = new LoggedCommand(new CommandKey(5, SourceClass.Player, 0, 3), new FundProgramme(0, 47, Fx6.FromRaw(1_234_567)));
        Assert.Equal(logged.Command, CommandCodec.Decode(CommandCodec.Encode(logged)).Command);
    }
}
