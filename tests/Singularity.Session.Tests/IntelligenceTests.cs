using Singularity.Core.Commands;
using Singularity.Core.Intelligence;
using Singularity.Core.Knowledge;
using Singularity.Core.Policy;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>TASK-0160: intelligence v1: the seventh share, targets and modes, networks, exposure, the theft channel in c_eff, estimates; neutrality with the flag off.</summary>
public class IntelligenceTests
{
    private const int Usa = 0, Gbr = 1, Sun = 2;
    private static readonly Fx6 KDiff = new KnowledgeParams().KDiff;

    private static SliceSession Make(bool reserve = false, int threads = 1, int chunk = 1, ulong seed = 7) =>
        SliceTestData.Create(new SliceRunConfig { Seed = seed, Intelligence = true, Reserve = reserve, Threads = threads, ChunkSize = chunk });

    private static IntelTarget T(int target, IntelMode mode) => new(target, mode);

    private static IntelSystem Intel(SliceSession s) => s.World.Intel!;

    // The AI split with a chosen security item taken out of procurement.
    private static BudgetShares WithSecurity(SliceSession s, int actor, long raw)
    {
        BudgetShares b = s.World.SharesOf(actor);
        return b with { Procurement = b.Procurement - (Fx6.FromRaw(raw) - b.Security), Security = Fx6.FromRaw(raw) };
    }

    private static SetBudgetShares Plan(SliceSession s, int actor, long security, params IntelTarget[] targets) =>
        new(actor, WithSecurity(s, actor, security), [.. targets]);

    [Fact]
    public void FlagOff_HasNoIntelligenceColumns_AndTheFingerprintOfMain()
    {
        SliceSession off = SliceTestData.Create(new SliceRunConfig { Seed = 7, Intelligence = false });
        Assert.Null(off.World.Intel);
        Assert.DoesNotContain(off.World.Table.Columns, c => ColumnProjection.IsIntelColumn(c.Name));
        Assert.Equal(new SliceRunConfig { Seed = 7 }.SnapshotFingerprint(), new SliceRunConfig { Seed = 7, Intelligence = false }.SnapshotFingerprint());
        Assert.NotEqual(new SliceRunConfig { Seed = 7 }.SnapshotFingerprint(), new SliceRunConfig { Seed = 7, Intelligence = true }.SnapshotFingerprint());
        // The data object is outside the data hash: loading it did not move the identity of the data (the main-fixture hashes are pinned in ReserveTests).
        Assert.NotNull(SliceTestData.Data.Intelligence);
        Assert.Null(SliceTestData.LegacyData.Intelligence);
    }

    [Fact]
    public void FlagOff_RunEqualsTheRunOfMain_ByTheSharedPinnedHash()
    {
        // The same pinned values as ReserveTests (recorded on main before TASK-0150): the build with the seventh share and the theft term is still that run.
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 7 });
        for (long t = 0; t < 36; t++)
            s.StepThroughMonth(t);
        HashAssert.Equal("748ecfba48b81b37c6039ce7a8dbd831fd64f94ada40853fb9746930db3873b5", ColumnProjection.Hash(s, _ => true));
        HashAssert.Equal("3c313261526ef0bc80dec30ff4c363c313c1f239f7e08d6ecd4f946a57ab5174", s.LastHash);
    }

    [Fact]
    public void Validator_RefusesSecurityAndTargetsWithoutTheFlag_AndMalformedPlans()
    {
        SliceSession off = SliceTestData.Create(new SliceRunConfig { Seed = 7 });
        BudgetShares b = off.World.SharesOf(0);
        Assert.Equal(CommandRefusal.IntelOff, off.Submit(SourceClass.Player, 0, 0, new SetBudgetShares(0, b with { Security = Fx6.FromRaw(10_000), Procurement = b.Procurement - Fx6.FromRaw(10_000) })).Reason);
        Assert.Equal(CommandRefusal.IntelOff, off.Submit(SourceClass.Player, 0, 1, new SetBudgetShares(0, b, [new IntelTarget(1, IntelMode.Observe)])).Reason);

        SliceSession on = Make();
        Assert.Equal(CommandRefusal.BadTargets, on.Submit(SourceClass.Player, 0, 0, Plan(on, 0, 20_000, T(1, IntelMode.Steal), T(2, IntelMode.Steal), T(3, IntelMode.Steal), T(4, IntelMode.Steal))).Reason);
        Assert.Equal(CommandRefusal.BadTargets, on.Submit(SourceClass.Player, 0, 1, Plan(on, 0, 20_000, T(1, IntelMode.Steal), T(1, IntelMode.Observe))).Reason);
        Assert.Equal(CommandRefusal.BadTargets, on.Submit(SourceClass.Player, 0, 2, Plan(on, 0, 20_000, T(0, IntelMode.Observe))).Reason);
        Assert.Equal(CommandRefusal.BadTargets, on.Submit(SourceClass.Player, 0, 3, Plan(on, 0, 20_000, T(9, IntelMode.Observe))).Reason);
        Assert.Equal(CommandRefusal.BadTargets, on.Submit(SourceClass.Player, 0, 4, Plan(on, 0, 20_000, T(1, IntelMode.None))).Reason);
        // A seventh item that leaves the sum above 1 is a bad split, as before.
        BudgetShares full = on.World.SharesOf(0);
        Assert.Equal(CommandRefusal.BadShares, on.Submit(SourceClass.Player, 0, 5, new SetBudgetShares(0, full with { Security = Fx6.FromRaw(full.Security.Raw + 1) })).Reason);
        Assert.True(on.Submit(SourceClass.Player, 0, 6, Plan(on, 0, 30_000, T(1, IntelMode.Steal), T(2, IntelMode.Observe), T(3, IntelMode.FalseTrails))).Accepted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SecurityShare_WorksWithAndWithoutTheReserve(bool reserve)
    {
        SliceSession s = Make(reserve);
        // The AI item is the normal preset, carved out of the unread quality item first, so every split still sums to 1.
        for (int a = 0; a < s.World.Table.RowCount; a++)
        {
            Assert.Equal(Intel(s).Parameters.PresetNormal, s.World.SharesOf(a).Security);
            Assert.Equal(Fx6.One, s.World.SharesOf(a).Sum);
            Assert.Equal(SecurityWord.Normal, Intel(s).SecurityWordOf(s.World.SharesOf(a).Security));
        }
        // The player's: the strong preset out of procurement, or with the reserve a split that leaves a remainder.
        BudgetShares b = s.World.SharesOf(Usa);
        BudgetShares mine = reserve ? b with { Procurement = b.Procurement - Fx6.FromRaw(110_000), Security = Fx6.FromRaw(30_000) } : WithSecurity(s, Usa, 30_000);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new SetBudgetShares(Usa, mine)).Accepted);
        s.StepThroughMonth(0);
        Assert.Equal(Fx6.FromRaw(30_000), s.World.SharesOf(Usa).Security);
        Assert.Equal(SecurityWord.Strong, Intel(s).SecurityWordOf(Fx6.FromRaw(30_000)));
        if (reserve)
        {
            Assert.Equal(Fx6.FromRaw(100_000), s.World.SharesOf(Usa).Reserve);   // the seventh item is not part of the remainder
            Assert.True(s.World.Reserve!.Balance(Usa) > Fx6.Zero);
        }
        Assert.Equal(s.World.SharesOf(Usa).Security, s.View.Inputs!.Actors[Usa].Intel!.Security);
    }

    [Fact]
    public void KnowledgeMath_TheftIsOneMax_NotASum_AndNeutralWhenSmall()
    {
        Fx6 cMoney = Fx6.FromInt(1000), kDiff = Fx6.FromRaw(500_000), one = Fx6.One, kK = Fx6.FromRaw(200_000), kmix = Fx6.FromRaw(300_000), kRef = Fx6.FromInt(2);
        Fx6 old = KnowledgeMath.CEff(cMoney, kDiff, true, kK, kmix, kRef, Fx6.FromRaw(100_000));
        Assert.Equal(old, KnowledgeMath.CEff(cMoney, kDiff, true, kK, kmix, kRef, Fx6.FromRaw(100_000), Fx6.Zero));
        Assert.Equal(old, KnowledgeMath.CEff(cMoney, kDiff, true, kK, kmix, kRef, Fx6.FromRaw(100_000), kDiff));                 // s_steal <= k_diff: the old value
        Assert.Equal(old, KnowledgeMath.CEff(cMoney, kDiff, true, kK, kmix, kRef, Fx6.FromRaw(100_000), Fx6.FromRaw(400_000)));
        Fx6 stolen = KnowledgeMath.CEff(cMoney, kDiff, true, kK, kmix, kRef, Fx6.FromRaw(100_000), Fx6.FromRaw(700_000));
        Fx6 den = (one + kK * kmix / kRef) * (one + Fx6.FromRaw(100_000));
        Assert.Equal(cMoney * (one - Fx6.FromRaw(700_000)) / den, stolen);                                                         // the floor of the node cost is 0.3 c_money, no sum with k_diff
        Assert.True(stolen < old);
    }

    [Fact]
    public void StealShare_NeverExceedsSMax_AndIsZeroOutsideTheStealMode()
    {
        IntelParams p = SliceTestData.Data.Intelligence!.Params;
        Assert.Equal(p.SMax, IntelMath.StealShare(p with { Sigma = Fx6.One }, Fx6.One, Fx6.One));
        Assert.Equal(p.SMax, IntelMath.StealShare(p, Fx6.One, Fx6.One));
        Assert.Equal(p.Sigma * Fx6.FromRaw(400_000) * Fx6.FromRaw(500_000), IntelMath.StealShare(p, Fx6.FromRaw(400_000), Fx6.FromRaw(500_000)));   // below the cap: sigma·N·A

        // In a run: the player steals from the leader with the strong security item; the discount is bounded by s_max in every month and node,
        // is zero for nodes the target does not know and for a target in another mode.
        SliceSession s = Make();
        Assert.True(s.Submit(SourceClass.Player, 0, 0, Plan(s, Sun, 30_000, T(Usa, IntelMode.Steal))).Accepted);
        Fx6 peak = Fx6.Zero;
        for (long t = 0; t < 96; t++)
        {
            s.StepThroughMonth(t);
            for (int r = 0; r < s.World.Graph.Count; r++)
            {
                Fx6 steal = Intel(s).StealShare(Sun, r);
                Assert.True(steal <= p.SMax, $"month {t}: s_steal {steal} above s_max");
                if (!s.World.Knowledge.IsKnown(Usa, r))
                    Assert.Equal(Fx6.Zero, steal);
                peak = IntelMath.Max(peak, steal);
            }
        }
        Assert.True(peak > KDiff, $"the network never matured above k_diff: peak {peak}");
    }

    // The core mod with sigma = 0: the networks and the exposure draws are those of the real data, the theft channel is closed.
    private static Singularity.Core.Data.GameData DataWithoutTheft()
    {
        string dir = Path.Combine(Path.GetTempPath(), "singularity-intel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string core = Path.Combine(SliceTestData.RepoRoot, "mods", "core");
        foreach (string f in Directory.GetFiles(core, "*.yaml"))
            File.WriteAllText(Path.Combine(dir, Path.GetFileName(f)), Path.GetFileName(f) == "intelligence.yaml" ? File.ReadAllText(f).Replace("sigma: 1.50", "sigma: 0.00") : File.ReadAllText(f));
        File.Copy(Path.Combine(core, "mod.yaml"), Path.Combine(dir, "mod.yaml"), true);
        try
        {
            return Singularity.Core.Data.ModLoader.Load(dir);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Theft_LetsTheThiefOpenNodesTheTargetKnowsEarlier_ThroughCEffOnly()
    {
        var config = new SliceRunConfig { Seed = 7, Intelligence = true };
        SliceSession theft = SliceSession.Create(SliceTestData.Data, config), none = SliceSession.Create(DataWithoutTheft(), config);
        foreach (SliceSession s in new[] { theft, none })
            Assert.True(s.Submit(SourceClass.Player, 0, 0, Plan(s, Sun, 30_000, T(Usa, IntelMode.Steal))).Accepted);
        long discounted = 0;
        for (long m = 0; m < 120; m++)
        {
            theft.StepThroughMonth(m);
            none.StepThroughMonth(m);
            for (int r = 0; r < theft.World.Graph.Count; r++)
            {
                Assert.Equal(Fx6.Zero, Intel(none).StealShare(Sun, r));
                discounted += Intel(theft).StealShare(Sun, r) > KDiff && theft.World.Knowledge.InFrontier(Sun, r) ? 1 : 0;
            }
        }
        Assert.True(discounted > 0, "the thief never had a frontier node with a discount above k_diff: the channel is dead at these parameters");
        Fx6 ProgSum(SliceSession s) { Fx6 x = Fx6.Zero; for (int r = 0; r < s.World.Graph.Count; r++) x += s.World.Knowledge.Prog(Sun, r); return x; }
        Assert.True(theft.World.Knowledge.KnownCount(Sun) >= none.World.Knowledge.KnownCount(Sun));
        Assert.True(ProgSum(theft) > ProgSum(none), $"theft added no progress: {ProgSum(theft)} vs {ProgSum(none)}");
        // Nothing but the cost moved: the mastery columns of the thief are produced by the same rules (no mastery from stolen knowledge, #47, #104).
        Assert.Equal(none.World.Table.Columns.Count, theft.World.Table.Columns.Count);
    }

    [Fact]
    public void Exposure_FollowsTheDrawOfTheNetwork_AndIsTheSameOnEveryThreadCount()
    {
        SliceSession s = Make();
        CounterRng events = s.World.Streams.Events;
        int exposures = 0;
        for (long m = 0; m < 144; m++)
        {
            int rows = s.World.Table.RowCount;
            var expected = new bool[rows, rows];
            var before = new int[rows, rows];
            for (int o = 0; o < rows; o++)
            {
                for (int t = 0; t < rows; t++)
                {
                    if (o == t)
                        continue;
                    before[o, t] = Intel(s).Lost(o, t);
                    Fx6 hm = IntelMath.MonthlyFromYearly(Intel(s).HazardPerYear(o, t));
                    expected[o, t] = hm > Fx6.Zero && CounterRng.Chance(events.NextU64(m, IntelSystem.SystemExpose, o * (1 << 16) + t, 0), hm);
                }
            }
            s.StepThroughMonth(m);
            for (int o = 0; o < rows; o++)
            {
                for (int t = 0; t < rows; t++)
                {
                    if (o == t)
                        continue;
                    Assert.Equal(expected[o, t] ? 1 : 0, Intel(s).Lost(o, t) - before[o, t]);
                    exposures += expected[o, t] ? 1 : 0;
                }
            }
        }
        Assert.True(exposures > 0, "no network was exposed in twelve years: the test would prove nothing");

        // L2: the thread count and the chunking change nothing (state hash and the exposure counts).
        SliceSession one = Make(threads: 1, chunk: 1), many = Make(threads: 4, chunk: 2);
        for (long m = 0; m < 144; m++)
        {
            one.StepThroughMonth(m);
            many.StepThroughMonth(m);
        }
        HashAssert.Equal(one.LastHash, many.LastHash);
        HashAssert.Equal(s.LastHash, one.LastHash);
    }

    [Fact]
    public void Exposure_NeverRaisesTheYearlyRiskAboveTheCap_AndLossesAreTheDataFractions()
    {
        SliceSession s = Make();
        IntelParams p = Intel(s).Parameters;
        Assert.True(s.Submit(SourceClass.Player, 0, 0, Plan(s, Usa, 30_000, T(Gbr, IntelMode.Steal))).Accepted);
        for (long m = 0; m < 120; m++)
        {
            for (int o = 0; o < s.World.Table.RowCount; o++)
            {
                for (int t = 0; t < s.World.Table.RowCount; t++)
                {
                    if (o != t)
                        Assert.True(Intel(s).HazardPerYear(o, t) <= p.HazardCapPerYear);
                }
            }
            int lostBefore = Intel(s).Lost(Usa, Gbr);
            s.StepThroughMonth(m);
            if (Intel(s).Lost(Usa, Gbr) > lostBefore)
                {
                Assert.True(Intel(s).Network(Usa, Gbr) <= p.LossKeepN && Intel(s).Maturity(Usa, Gbr) <= p.LossKeepA, "an exposed network keeps at most the data fractions of a full one");
                Assert.Equal(Fx6.Zero, Intel(s).Trace(Usa, Gbr));
            }
        }
    }

    [Fact]
    public void Replay_OfALogWithSecurityAndTargets_GivesTheSameHash()
    {
        SliceSession s = Make();
        Assert.True(s.Submit(SourceClass.Player, 0, 0, Plan(s, Sun, 30_000, T(Usa, IntelMode.Steal), T(Gbr, IntelMode.FalseTrails))).Accepted);
        for (long m = 0; m < 30; m++)
            s.StepThroughMonth(m);
        Assert.True(s.Submit(SourceClass.Player, 0, 1, Plan(s, Sun, 10_000)).Accepted);   // an empty plan is "no targets", not "keep"
        for (long m = 30; m < 40; m++)
            s.StepThroughMonth(m);
        Assert.Contains("\"targets\"", s.CommandLog);
        SliceSession replay = SliceSession.Replay(SliceTestData.Data, s.World.Config, s.CommandLog, s.Tick);
        HashAssert.Equal(s.LastHash, replay.LastHash);
        Assert.False(Intel(s).Plan(Sun, 0, out _, out _));
        Assert.Equal(SecurityWord.Low, Intel(s).SecurityWordOf(s.World.SharesOf(Sun).Security));

        // The line round-trips, and the old five-share line has neither key.
        LoggedCommand logged = Assert.Single(CommandCodec.DecodeLog(CommandCodec.EncodeLog([new LoggedCommand(new CommandKey(5, SourceClass.Player, 0, 0), Plan(s, Sun, 20_000, T(Usa, IntelMode.Observe)))])));
        var back = Assert.IsType<SetBudgetShares>(logged.Command);
        Assert.Equal(Fx6.FromRaw(20_000), back.Shares.Security);
        Assert.Equal(new[] { new IntelTarget(Usa, IntelMode.Observe) }, back.Targets.ToArray());
        string plain = CommandCodec.EncodeLog([new LoggedCommand(new CommandKey(5, SourceClass.Player, 0, 0), new SetBudgetShares(0, new BudgetShares(Fx6.FromRaw(1), Fx6.Zero, Fx6.Zero, Fx6.FromRaw(999_999), Fx6.Zero)))]);
        Assert.DoesNotContain("security", plain);
        Assert.DoesNotContain("targets", plain);
    }

    [Fact]
    public void Ai_SameRules_BehindActorsStealFromTheLeadersAndTheLeaderObserves()
    {
        SliceSession s = Make();
        for (long m = 0; m < 14; m++)
            s.StepThroughMonth(m);
        int stealers = 0;
        for (int a = 0; a < s.World.Table.RowCount; a++)
        {
            int mine = s.World.Knowledge.KnownCount(a);
            for (int k = 0; k < IntelParams.MaxTargets; k++)
            {
                if (!Intel(s).Plan(a, k, out int t, out IntelMode mode))
                    continue;
                Assert.NotEqual(a, t);
                int theirs = s.World.Knowledge.KnownCount(t);
                // The rule: steal exactly from those who know more, observe the strongest rival when nobody does.
                Assert.Equal(theirs > mine ? IntelMode.Steal : IntelMode.Observe, mode);
                stealers += mode == IntelMode.Steal ? 1 : 0;
            }
            Assert.True(Intel(s).Plan(a, 0, out _, out _), $"actor {a} has no plan");
        }
        Assert.True(stealers > 0);
        // The AI pays for it with the normal security item, and its networks grow.
        int rows = s.World.Table.RowCount;
        Assert.Contains(Enumerable.Range(0, rows * rows), i => i / rows != i % rows && Intel(s).Network(i / rows, i % rows) > Fx6.Zero);
    }

    [Fact]
    public void Estimates_AreRanges_AroundTheTruth_WithAConfidenceThatGrowsWithMaturity()
    {
        SliceSession s = Make();
        Assert.True(s.Submit(SourceClass.Player, 0, 0, Plan(s, Gbr, 30_000, T(Usa, IntelMode.Observe))).Accepted);
        IntelEstimate early = default;
        for (long m = 0; m < 96; m++)
        {
            s.StepThroughMonth(m);
            if (m == 6)
                early = Intel(s).Estimate(Gbr, Usa);
        }
        IntelEstimate late = Intel(s).Estimate(Gbr, Usa);
        Assert.False(early.Visible);   // a young network: no estimate layer
        Assert.True(late.Visible);
        Assert.True(late.Confidence > early.Confidence);
        int truth = s.World.Knowledge.KnownCount(Usa);
        Assert.InRange(truth, late.Low, late.High);
        Assert.True(late.High > late.Low, "a range, never the exact value");
        Assert.False(late.Distorted);
        // The view carries the range as a lead and the word of the confidence, never the exact count.
        IntelTargetView card = s.View.Inputs!.Actors[Gbr].Intel!.Targets.Single(x => x.Target == Usa);
        Assert.True(card.EstimateVisible);
        int own = s.World.Knowledge.KnownCount(Gbr);
        Assert.Equal((late.Low - own, late.High - own), (card.LeadLow, card.LeadHigh));
    }

    [Fact]
    public void FalseTrails_CorruptTheObserversSources_AndShowAsDistortedData()
    {
        SliceSession clean = Make(), fooled = Make();
        // GBR observes USA with a full network for years; in the second run USA runs false trails against GBR.
        foreach (SliceSession s in new[] { clean, fooled })
            Assert.True(s.Submit(SourceClass.Player, 0, 0, Plan(s, Gbr, 30_000, T(Usa, IntelMode.Observe))).Accepted);
        Assert.True(fooled.Submit(SourceClass.Player, 1, 0, Plan(fooled, Usa, 30_000, T(Gbr, IntelMode.FalseTrails))).Accepted);
        for (long m = 0; m < 96; m++)
        {
            clean.StepThroughMonth(m);
            fooled.StepThroughMonth(m);
        }
        IntelEstimate a = Intel(clean).Estimate(Gbr, Usa), b = Intel(fooled).Estimate(Gbr, Usa);
        Assert.False(a.Distorted);
        Assert.True(Intel(fooled).Network(Usa, Gbr) > Fx6.Zero);
        // Either the corrupted sources were dropped (fewer sources, lower confidence) or the centre is shifted and the data is flagged.
        Assert.True(b.Distorted || b.Sources < a.Sources || !b.Visible, $"false trails left the estimate untouched: {a} vs {b}");
    }

    [Fact]
    public void CounterIntelligence_CutsAForeignNetwork_AndRaisesItsHazard()
    {
        SliceSession weak = Make(), strong = Make();
        // SUN builds the same network against USA in both runs; USA spends the strong preset on security only in the second.
        foreach (SliceSession s in new[] { weak, strong })
            Assert.True(s.Submit(SourceClass.Player, 0, 0, Plan(s, Sun, 30_000, T(Usa, IntelMode.Observe))).Accepted);
        Assert.True(weak.Submit(SourceClass.Player, 1, 0, Plan(weak, Usa, 10_000)).Accepted);
        Assert.True(strong.Submit(SourceClass.Player, 1, 0, Plan(strong, Usa, 30_000)).Accepted);
        Assert.True(Intel(strong).CiEff(Usa) < Fx6.One || Intel(strong).Parameters.CiShare * Fx6.FromRaw(30_000) / Intel(strong).Parameters.CiShareRef >= Fx6.One);
        for (long m = 0; m < 60; m++)
        {
            weak.StepThroughMonth(m);
            strong.StepThroughMonth(m);
        }
        Assert.True(Intel(strong).CiEff(Usa) > Intel(weak).CiEff(Usa));
        Assert.True(Intel(strong).Network(Sun, Usa) < Intel(weak).Network(Sun, Usa), "counter-intelligence did not slow the foreign network");
    }
}
