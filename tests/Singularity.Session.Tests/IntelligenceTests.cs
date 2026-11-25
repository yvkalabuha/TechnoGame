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
        // Re-recorded in TASK-0180 (#555): requires of 11 early G5/G6 nodes (62, 63, 82, 83, 86, 90, 91, 92, 95, 97, 99) changed, so knowledge opens later and every trajectory moves; no schema change.
        HashAssert.Equal("e2868ee75c0d7d493f95dbe3d9a69850c7b3bba6d04ef7ed981d584416ead4bb", ColumnProjection.Hash(s, _ => true));
        // Re-recorded in TASK-0182: schema v15, new column pol.flt.gFront; old columns unchanged (ColumnProjection hash not changed).
        HashAssert.Equal("6c5d83b7e1750e2338143e375520fe1ed5fb289ef7534adfc8d37364428e5b24", s.LastHash);
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
    public void Exposure_FollowsTheDrawOfTheNetworkKeyedByActorIds_AndIsTheSameOnEveryThreadCount()
    {
        SliceSession s = Make();
        CounterRng events = s.World.Streams.Events;
        IntelParams p = Intel(s).Parameters;
        var ids = s.World.Scenario.ActorId;
        int exposures = 0;
        for (long m = 0; m < 240; m++)
        {
            int rows = s.World.Table.RowCount;
            var expected = new bool[rows, rows];
            var lostInc = new bool[rows, rows];
            var risk = new Fx6[rows, rows];
            var before = new int[rows, rows];
            for (int o = 0; o < rows; o++)
            {
                for (int t = 0; t < rows; t++)
                {
                    if (o == t)
                        continue;
                    before[o, t] = Intel(s).Lost(o, t);
                    int key = IntelRng.NetworkKeys(ids)[o * rows + t];   // the stable ids, not the rows
                    if (Intel(s).Turned(o, t))
                    {
                        risk[o, t] = p.TurnDetectPerYear;
                        expected[o, t] = CounterRng.Chance(events.NextU64(m, IntelSystem.SystemTurn, key, 1), IntelMath.MonthlyFromYearly(p.TurnDetectPerYear));
                        lostInc[o, t] = expected[o, t];
                    }
                    else
                    {
                        bool active = Intel(s).ModeAgainst(o, t) != IntelMode.None && Intel(s).Network(o, t) > Fx6.Zero;
                        risk[o, t] = active ? Intel(s).HazardPerYear(o, t) : Fx6.Zero;
                        Fx6 hm = IntelMath.MonthlyFromYearly(risk[o, t]);
                        int dl = Intel(s).Deadline(o, t);
                        // The deadline holds also for a network whose target was dropped from the plan.
                        expected[o, t] = (active && CounterRng.Chance(events.NextU64(m, IntelSystem.SystemExpose, key, 0), hm)) || (dl != 0 && dl - 1 <= m && Intel(s).Network(o, t) > Fx6.Zero);
                        bool turnsNow = expected[o, t] && CounterRng.Chance(events.NextU64(m, IntelSystem.SystemTurn, key, 0), p.TurnOnExposure);
                        lostInc[o, t] = expected[o, t] && !turnsNow;   // a secret turn is not a loss its owner knows of
                    }
                }
            }
            s.StepThroughMonth(m);
            for (int o = 0; o < rows; o++)
            {
                for (int t = 0; t < rows; t++)
                {
                    if (o == t)
                        continue;
                    Assert.Equal(lostInc[o, t] ? 1 : 0, Intel(s).Lost(o, t) - before[o, t]);
                    if (!expected[o, t])
                        continue;
                    exposures++;
                    // The post-event report keeps the month and the risk the network had before the event (#367).
                    ExposureRecord rec = Intel(s).LastExposure(o, t)!.Value;
                    Assert.Equal((m, risk[o, t]), (rec.Month, rec.RiskPerYear));
                }
            }
        }
        Assert.True(exposures > 0, "no network was exposed in twenty years: the test would prove nothing");

        // L2: the thread count and the chunking change nothing (state hash).
        SliceSession one = Make(threads: 1, chunk: 1), many = Make(threads: 4, chunk: 2);
        for (long m = 0; m < 240; m++)
        {
            one.StepThroughMonth(m);
            many.StepThroughMonth(m);
        }
        HashAssert.Equal(one.LastHash, many.LastHash);
        HashAssert.Equal(s.LastHash, one.LastHash);
    }

    [Fact]
    public void NetworkKey_IsAPinnedFunctionOfTheTwoActorIds()
    {
        Assert.Equal(IntelRng.NetworkKey("USA", "SUN"), IntelRng.NetworkKey("USA", "SUN"));
        Assert.NotEqual(IntelRng.NetworkKey("USA", "SUN"), IntelRng.NetworkKey("SUN", "USA"));
        Assert.Equal(1048665604, IntelRng.NetworkKey("USA", "SUN"));   // pinned: a change of the function re-keys every exposure
        
        var ids = SliceTestData.Data.Scenario("jet_slice_1945").ActorId;
        var keys = ids.SelectMany(o => ids.Where(t => t != o).Select(t => IntelRng.NetworkKey(o, t))).ToArray();
        Assert.Equal(keys.Length, keys.Distinct().Count());
    }

    [Fact]
    public void Hazard_FollowsMaturity_AYoungNetworkIsHardToSee()
    {
        IntelParams p = SliceTestData.Data.Intelligence!.Params;
        Assert.Equal(Fx6.Zero, IntelMath.HazardPerYear(p, Fx6.One, Fx6.Zero, Fx6.One, Fx6.Zero));
        Fx6 young = IntelMath.HazardPerYear(p, Fx6.One, Fx6.FromRaw(200_000), Fx6.One, Fx6.Zero), mature = IntelMath.HazardPerYear(p, Fx6.One, Fx6.FromRaw(800_000), Fx6.One, Fx6.Zero);
        Assert.True(young > Fx6.Zero && young < mature);
        Assert.Equal(p.HazardCapPerYear, IntelMath.HazardPerYear(p with { HazardBasePerYear = Fx6.One }, Fx6.One, Fx6.One, Fx6.One, Fx6.One));
    }

    // The core mod with some intelligence values replaced.
    private static Singularity.Core.Data.GameData DataWith(params (string From, string To)[] replacements)
    {
        string dir = Path.Combine(Path.GetTempPath(), "singularity-intel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string core = Path.Combine(SliceTestData.RepoRoot, "mods", "core");
        foreach (string f in Directory.GetFiles(core, "*.yaml"))
        {
            string text = File.ReadAllText(f);
            if (Path.GetFileName(f) == "intelligence.yaml")
            {
                foreach ((string from, string to) in replacements)
                {
                    Assert.Contains(from, text);
                    text = text.Replace(from, to);
                }
            }
            File.WriteAllText(Path.Combine(dir, Path.GetFileName(f)), text);
        }
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
    public void Turned_AnExposedNetworkCanBeTurned_ItStealsNothing_AndIsFoundOutLater()
    {
        // Every exposure turns the network; the draws are the real ones.
        var config = new SliceRunConfig { Seed = 7, Intelligence = true };
        SliceSession s = SliceSession.Create(DataWith(("turn_on_exposure: 0.30", "turn_on_exposure: 1.00"), ("hazard_base_per_year: 0.20", "hazard_base_per_year: 1.00")), config);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, Plan(s, Sun, 30_000, T(Usa, IntelMode.Steal))).Accepted);
        bool sawTurned = false, sawFoundOut = false;
        long turnedAt = -1, foundAt = -1;
        for (long m = 0; m < 240; m++)
        {
            bool wasTurned = Intel(s).Turned(Sun, Usa);
            int lost = Intel(s).Lost(Sun, Usa), caught = Intel(s).CaughtOf(Sun, Usa);
            s.StepThroughMonth(m);
            // The view of a step shows the state before it: the events of month m-1 are in the inputs of month m.
            if (turnedAt == m - 1 && turnedAt >= 0)
            {
                Assert.Contains(s.View.Inputs!.Actors[Usa].Intel!.CaughtAtMe, c => c.Foreign == Sun && c.Outcome == ExposureOutcome.Turned);   // the victim is told
                Assert.DoesNotContain(s.View.Inputs!.Actors[Sun].Intel!.Exposures, e => e.Target == Usa && e.Month == turnedAt);              // the owner is not
            }
            if (foundAt == m - 1 && foundAt >= 0)
                Assert.Contains(s.View.Inputs!.Actors[Sun].Intel!.Exposures, e => e.Target == Usa && e.Month == foundAt && e.RiskPerYear == Intel(s).Parameters.TurnDetectPerYear);
            bool turned = Intel(s).Turned(Sun, Usa);
            if (turned)
            {
                sawTurned = true;
                for (int r = 0; r < s.World.Graph.Count; r++)
                    Assert.Equal(Fx6.Zero, Intel(s).StealShare(Sun, r));   // a turned network steals nothing
                if (!wasTurned)
                {
                    Assert.Equal(ExposureOutcome.Turned, Intel(s).LastExposure(Sun, Usa)!.Value.Outcome);
                    Assert.Equal(lost, Intel(s).Lost(Sun, Usa));          // the owner's loss counter does not move at a secret turn
                    Assert.Equal(caught + 1, Intel(s).CaughtOf(Sun, Usa)); // the target counts the catch
                    turnedAt = m;
                }
            }
            if (wasTurned && !turned)
            {
                sawFoundOut = true;
                foundAt = m;
                Assert.Equal(lost + 1, Intel(s).Lost(Sun, Usa));
                Assert.Equal(caught, Intel(s).CaughtOf(Sun, Usa));   // finding out a turned network is not a second catch
                Assert.Equal(ExposureOutcome.Lost, Intel(s).LastExposure(Sun, Usa)!.Value.Outcome);
            }
        }
        Assert.True(sawTurned && sawFoundOut, $"turned {sawTurned}, found out {sawFoundOut}");
    }

    [Fact]
    public void Loader_RefusesATheftCeilingOfOne_WhichCouldZeroCEff()
    {
        Assert.Throws<Singularity.Core.Data.DataLoadException>(() => DataWith(("s_max: 0.70", "s_max: 1.00")));
        Assert.Throws<Singularity.Core.Data.DataLoadException>(() => DataWith(("s_max: 0.70", "s_max: 1.50")));
    }

    [Fact]
    public void Snapshot_OfAnotherIntelligenceParameterSet_IsRefused()
    {
        var config = new SliceRunConfig { Seed = 7, Intelligence = true };
        SliceSession a = SliceSession.Create(SliceTestData.Data, config);
        for (long m = 0; m < 3; m++)
            a.StepThroughMonth(m);
        var snap = new MemoryStream();
        a.WriteSnapshot(snap);
        SliceSession same = SliceSession.Create(SliceTestData.Data, config);
        same.LoadSnapshot(new MemoryStream(snap.ToArray()));   // the same parameters load
        SliceSession other = SliceSession.Create(DataWith(("sigma: 1.50", "sigma: 0.00")), config);
        Assert.ThrowsAny<Exception>(() => other.LoadSnapshot(new MemoryStream(snap.ToArray())));
    }

    [Fact]
    public void HiddenEvents_AreEmittedAboveTheThreshold_AndTheOrdinaryViewDoesNotLeakThem()
    {
        // GBR watches USA and SUN with a full network; the ordinary view is the one of GBR (PlayerActor).
        var config = new SliceRunConfig { Seed = 7, Intelligence = true, PlayerActor = Gbr };
        SliceSession s = SliceSession.Create(SliceTestData.Data, config);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, Plan(s, Gbr, 30_000, T(Usa, IntelMode.Observe), T(Sun, IntelMode.Observe))).Accepted);
        IntelParams p = Intel(s).Parameters;
        int emitted = 0, activeSeen = 0;
        for (long m = 0; m < 240; m++)
        {
            s.StepThroughMonth(m);
            ActorView[] actors = [.. s.View.Actors];
            IntelTargetView[] cards = [.. s.View.Inputs!.Actors[Gbr].Intel!.Targets];
            foreach (IntelTargetView card in cards)
            {
                if (!card.EstimateVisible || card.Confidence * p.SensLicence < p.ConfVisible)
                    Assert.Empty(card.HiddenEvents);
                if (card.HiddenEvents.Length == 1)
                {
                    emitted++;
                    Assert.True(s.World.LicenceActive(card.Target));
                    Assert.Equal(card.Confidence * p.SensLicence, card.HiddenEvents[0].Confidence);
                    Assert.True(card.HiddenEvents[0].Confidence >= p.ConfVisible);
                }
            }
            foreach (ActorView a in actors)
            {
                if (a.Actor == Gbr)
                {
                    Assert.Equal(s.World.LicenceActive(Gbr), a.Licence.Active);   // one's own licence is not hidden
                    continue;
                }
                activeSeen += s.World.LicenceActive(a.Actor) ? 1 : 0;
                // The view of the actors is the state after the step: the gate is the same function of it (the cards above are of the step before).
                IntelEstimate e = Intel(s).Estimate(Gbr, a.Actor);
                bool seen = e.Visible && e.Confidence * p.SensLicence >= p.ConfVisible && s.World.LicenceActive(a.Actor);
                Assert.Equal(seen, a.Licence.Active);   // a foreign licence reaches the ordinary view only through intelligence
                if (!seen)
                    Assert.True(a.Licence.Seller < 0 && a.Licence.Gen == 0, "a foreign licence leaked through the ordinary view");
            }
        }
        Assert.True(activeSeen > 0, "no foreign licence in twenty years: the fixture proves nothing");
        Assert.True(emitted > 0, "intelligence never emitted a hidden event although foreign licences existed");
    }

    [Fact]
    public void PlayerView_DoesNotCarryTheKnowledgeOrTheIntelligenceOfOtherActors()
    {
        SliceSession s = SliceSession.Create(SliceTestData.Data, new SliceRunConfig { Seed = 7, Intelligence = true, PlayerActor = Gbr });
        for (long m = 0; m < 30; m++)
            s.StepThroughMonth(m);
        for (int a = 0; a < s.World.Table.RowCount; a++)
        {
            ActorView view = s.View.Actors[a];
            IntelView? card = s.View.Inputs!.Actors[a].Intel;
            if (a == Gbr)
            {
                Assert.NotNull(card);
                Assert.True(view.Nodes.IsPresent);
                Assert.Equal(Enumerable.Range(0, s.World.Graph.Count).Count(r => !s.World.Knowledge.IsKnown(a, r)), view.UnknownNodes);
            }
            else
            {
                Assert.Null(card);                      // the cards (hidden events, who caught whom) of other actors are theirs
                Assert.False(view.Nodes.IsPresent);     // no node block of a rival
                Assert.InRange(view.UnknownNodes, 0, 1); // "some left", not the exact count the estimate gives as a range
            }
        }
    }

    [Fact]
    public void WithSecurity_KeepsTheSumAndNeverGoesNegative_AndTheRefusalCodesStayPut()
    {
        var shares = new BudgetShares(Fx6.FromRaw(350_000), Fx6.FromRaw(120_000), Fx6.FromRaw(80_000), Fx6.FromRaw(400_000), Fx6.FromRaw(50_000));
        BudgetShares fromQuality = shares.WithSecurity(Fx6.FromRaw(20_000));
        Assert.Equal((Fx6.FromRaw(30_000), Fx6.FromRaw(400_000), Fx6.FromRaw(20_000)), (fromQuality.Quality, fromQuality.Procurement, fromQuality.Security));
        BudgetShares fromBoth = (shares with { Quality = Fx6.Zero }).WithSecurity(Fx6.FromRaw(20_000));
        Assert.Equal(Fx6.FromRaw(380_000), fromBoth.Procurement);
        BudgetShares clamped = new BudgetShares(Fx6.FromRaw(990_000), Fx6.Zero, Fx6.Zero, Fx6.FromRaw(5_000), Fx6.FromRaw(5_000)).WithSecurity(Fx6.FromRaw(50_000));
        Assert.True(clamped.Procurement >= Fx6.Zero && clamped.Quality >= Fx6.Zero);
        Assert.Equal(Fx6.One, clamped.Sum);
        Assert.Equal(shares, shares.WithSecurity(Fx6.Zero));   // a run without intelligence is untouched
        // Public refusal codes of earlier builds are not renumbered; the new ones come after them.
        Assert.Equal(26, (int)CommandRefusal.ProgrammeNotFundable);
        Assert.Equal(27, (int)CommandRefusal.IntelOff);
        Assert.Equal(28, (int)CommandRefusal.BadTargets);
    }

    [Fact]
    public void Trace_PastTheThreshold_MakesTheExposureCertainWithinTwelveMonths()
    {
        // Turning off, so that every exposure is a loss; the hazard is the real one, only the base is raised so that traces reach the threshold.
        SliceSession s = SliceSession.Create(DataWith(("turn_on_exposure: 0.30", "turn_on_exposure: 0.00"), ("hazard_base_per_year: 0.20", "hazard_base_per_year: 1.00")), new SliceRunConfig { Seed = 7, Intelligence = true });
        Assert.True(s.Submit(SourceClass.Player, 0, 0, Plan(s, Sun, 30_000, T(Usa, IntelMode.Steal), T(Gbr, IntelMode.Observe))).Accepted);
        int rows = s.World.Table.RowCount, windows = 0;
        var due = new long[rows, rows];
        var lost = new int[rows, rows];
        for (long m = 0; m < 240; m++)
        {
            s.StepThroughMonth(m);
            // The player drops the observed target for good: an exposure already scheduled on it must still come.
            if (m == 40)
                Assert.True(s.Submit(SourceClass.Player, 0, 1, Plan(s, Sun, 30_000, T(Usa, IntelMode.Steal))).Accepted);
            for (int o = 0; o < rows; o++)
            {
                for (int t = 0; t < rows; t++)
                {
                    if (o == t)
                        continue;
                    if (Intel(s).Lost(o, t) > lost[o, t])
                    {
                        lost[o, t] = Intel(s).Lost(o, t);
                        due[o, t] = 0;
                    }
                    int dl = Intel(s).Deadline(o, t);
                    if (dl != 0 && due[o, t] == 0)
                    {
                        due[o, t] = dl - 1;
                        windows++;
                        Assert.InRange(dl - 1 - m, 1, 12);   // set at month m: the exposure is due in 1 to 12 months
                    }
                    Assert.True(due[o, t] == 0 || m <= due[o, t], $"network {o}>{t} was not exposed by its deadline {due[o, t]} (month {m})");
                }
            }
        }
        Assert.True(windows > 0, "no trace passed the threshold: the test proves nothing");
    }

    [Fact]
    public void TheftOutsideTheDirectionOfTheNetwork_KeepsATenth()
    {
        SliceSession s = Make();
        IntelParams p = Intel(s).Parameters;
        Assert.True(s.Submit(SourceClass.Player, 0, 0, Plan(s, Sun, 30_000, T(Usa, IntelMode.Steal))).Accepted);
        int on = 0, off = 0;
        for (long m = 0; m < 96; m++)
        {
            s.StepThroughMonth(m);
            int dir = Intel(s).Direction(Sun, Usa);
            if (dir < 0)
                continue;
            Fx6 full = IntelMath.StealShare(p, Intel(s).Network(Sun, Usa), Intel(s).Maturity(Sun, Usa));
            for (int r = 0; r < s.World.Graph.Count; r++)
            {
                if (!s.World.Knowledge.IsKnown(Usa, r))
                    continue;
                bool inDir = s.World.Graph.Discipline[r] == dir;
                Assert.Equal(inDir ? full : full * p.LeakOffDir, Intel(s).StealShare(Sun, r));
                on += inDir && full > Fx6.Zero ? 1 : 0;
                off += !inDir && full > Fx6.Zero ? 1 : 0;
            }
        }
        Assert.True(on > 0 && off > 0, $"both sides of the direction must occur (on {on}, off {off})");
    }

    [Fact]
    public void NetworkKeys_NeverCollide_AndDoNotDependOnTheRowOrder()
    {
        // AEM>EAY and AKF>ZJJ are valid ids with the same 32-bit hash: the keys are made distinct, by the ids only.
        Assert.Equal(IntelRng.NetworkKey("AEM", "EAY"), IntelRng.NetworkKey("AKF", "ZJJ"));
        System.Collections.Immutable.ImmutableArray<string> ids = ["AEM", "EAY", "AKF", "ZJJ"], shuffled = ["ZJJ", "AKF", "EAY", "AEM"];
        int[] a = IntelRng.NetworkKeys(ids), b = IntelRng.NetworkKeys(shuffled);
        Assert.Equal(a.Length, a.Distinct().Count());
        for (int o = 0; o < 4; o++)
        {
            for (int t = 0; t < 4; t++)
                Assert.Equal(a[o * 4 + t], b[(3 - o) * 4 + (3 - t)]);
        }
        // Without a collision the key is the plain hash of the pair.
        System.Collections.Immutable.ImmutableArray<string> plain = ["USA", "SUN", "GBR"];
        Assert.Equal(IntelRng.NetworkKey("USA", "SUN"), IntelRng.NetworkKeys(plain)[1]);
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
