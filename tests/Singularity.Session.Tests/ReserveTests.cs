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
        }
        HashAssert.Equal(ColumnProjection.Hash(off, _ => true), ColumnProjection.Hash(on, n => !ColumnProjection.IsReserveColumn(n)), "an old column moved");
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
        for (long t = 1; t < 24; t++)
        {
            Fx6 before = Balance(s);
            Fx6 inflow = s.World.SharesOf(0).Reserve * s.World.Policy.Budget(0);
            Assert.Equal(Fx6.FromRaw(500_000), s.World.SharesOf(0).Reserve);
            s.StepThroughMonth(t);
            Fx6 after = before + inflow;
            Assert.Equal(after - after * leak, Balance(s));   // I3, exact
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
        // I3: what left is exactly the amount (before the leak of the month).
        Fx6 leak = a.World.Reserve!.Parameters.LeakPerMonth;
        Assert.True(Balance(b) - Balance(a) > Fx6.Zero);
        Assert.InRange((Balance(b) - Balance(a)).Raw, (amount - amount * leak).Raw - 2, (amount - amount * leak).Raw + 2);

        // Second programme on the same node: diminishing return 1/(1+k), k = 1.
        Fx6 half = a.World.Knowledge.ProgrammeGain(0, row, month + 1, amount);
        Assert.Equal(amount / a.World.Knowledge.CEff(0, row, month + 1) / Fx6.FromInt(2), half);
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
