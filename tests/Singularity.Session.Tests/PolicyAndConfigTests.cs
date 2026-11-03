using Singularity.Core.Industry;
using Singularity.Core.Knowledge;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

public class PolicyAndConfigTests
{
    private static SliceSession Make(ulong seed, Func<SliceRunConfig, SliceRunConfig>? edit = null) =>
        SliceTestData.Create(edit?.Invoke(new SliceRunConfig { Seed = seed }) ?? new SliceRunConfig { Seed = seed });

    private static PolicyProfile[] Profiles(SliceSession s) =>
        [.. Enumerable.Range(0, 5).Select(a => s.World.Policy.Profile(a))];

    // ---- shares

    [Fact]
    public void SharesOfEveryProfile_SumToOne_AndEqualTheSpecTable()
    {
        var p = new PolicyParams();
        p.Validate();
        Assert.Equal([350_000, 120_000, 80_000, 400_000, 50_000], Raws(p.Shares(PolicyProfile.Quality)));
        Assert.Equal([200_000, 180_000, 120_000, 500_000, 0], Raws(p.Shares(PolicyProfile.Mass)));
        Assert.Equal([280_000, 150_000, 100_000, 420_000, 50_000], Raws(p.Shares(PolicyProfile.Adaptive)));
        foreach (PolicyProfile profile in Enum.GetValues<PolicyProfile>())
            Assert.Equal(Fx6.One, p.Shares(profile).Sum);
    }

    private static long[] Raws(BudgetShares s) => [s.Research.Raw, s.Capability.Raw, s.Effort.Raw, s.Procurement.Raw, s.Quality.Raw];

    [Fact]
    public void ShareValidator_RejectsSumNotOne_AndNegative()
    {
        Assert.False(new BudgetShares(Fx6.FromRaw(500_000), Fx6.FromRaw(500_000), Fx6.FromRaw(1), Fx6.Zero, Fx6.Zero).IsValid);
        Assert.False(new BudgetShares(Fx6.FromRaw(1_100_000), Fx6.FromRaw(-100_000), Fx6.Zero, Fx6.Zero, Fx6.Zero).IsValid);
        Assert.True(new BudgetShares(Fx6.One, Fx6.Zero, Fx6.Zero, Fx6.Zero, Fx6.Zero).IsValid);
        var bad = new PolicyParams { SharesByProfile = [new(Fx6.One, Fx6.One, Fx6.Zero, Fx6.Zero, Fx6.Zero), new(Fx6.One, Fx6.Zero, Fx6.Zero, Fx6.Zero, Fx6.Zero), new(Fx6.One, Fx6.Zero, Fx6.Zero, Fx6.Zero, Fx6.Zero)] };
        Assert.Throws<ArgumentException>(bad.Validate);
    }

    [Fact]
    public void AiSharesInTheWorld_EqualTheProfileTable_FromTick0()
    {
        SliceSession s = Make(3);
        var p = new PolicyParams();
        for (int a = 0; a < 5; a++)
            Assert.Equal(p.Shares(s.World.Policy.Profile(a)), s.World.SharesOf(a));
    }

    // ---- priorities

    [Fact]
    public void Priority_FollowsTheDoctrine_StarEngineIsTimesTwoNotFour()
    {
        Fx6 two = Fx6.FromInt(2);
        Assert.Equal(two, PolicyRules.Priority(PolicyProfile.Quality, Singularity.Core.Data.NodeType.ENG, Singularity.Core.Data.Hazard.Key));
        Assert.Equal(two, PolicyRules.Priority(PolicyProfile.Quality, Singularity.Core.Data.NodeType.ENG, Singularity.Core.Data.Hazard.None));
        Assert.Equal(two, PolicyRules.Priority(PolicyProfile.Quality, Singularity.Core.Data.NodeType.PRC, Singularity.Core.Data.Hazard.Key));
        Assert.Equal(Fx6.One, PolicyRules.Priority(PolicyProfile.Quality, Singularity.Core.Data.NodeType.PRC, Singularity.Core.Data.Hazard.Hz));
        Assert.Equal(two, PolicyRules.Priority(PolicyProfile.Mass, Singularity.Core.Data.NodeType.PRC, Singularity.Core.Data.Hazard.None));
        Assert.Equal(Fx6.One, PolicyRules.Priority(PolicyProfile.Mass, Singularity.Core.Data.NodeType.ENG, Singularity.Core.Data.Hazard.Key));
        Assert.Equal(Fx6.One, PolicyRules.Priority(PolicyProfile.Adaptive, Singularity.Core.Data.NodeType.ENG, Singularity.Core.Data.Hazard.Key));
    }

    // ---- budget and A8

    [Fact]
    public void Budget_StartsAtTheScenarioIndex_AndGrowsByA12Rounding()
    {
        SliceSession s = Make(1);
        var scenario = s.World.Scenario;
        Fx6[] expected = [.. scenario.BudgetIndex];
        for (int a = 0; a < 5; a++)
            Assert.Equal(expected[a], s.World.Policy.Budget(a));
        for (long m = 0; m < 30; m++)
        {
            s.StepThroughMonth(m);
            for (int a = 0; a < 5; a++)
            {
                expected[a] = BudgetMath.Next(expected[a], scenario.GrowthPa[a]);
                Assert.Equal(expected[a], s.World.Policy.Budget(a));
            }
        }
        // g_m is floored once per actor, then one floored product (A12).
        Fx6 g = Fx6.FromRaw(29_900);
        Assert.Equal(Fx6.FromRaw(1_000_000) * (Fx6.One + Fx6.FromRaw(29_900 / 12)), BudgetMath.Next(Fx6.One, g));
    }

    [Fact]
    public void A8_DecisionsOfTick0_ExistBeforeTick0_AndAreNotRepeated()
    {
        SliceSession s = Make(5);
        PolicySystem p = s.World.Policy;
        // Initialisation phase: the decisions are in the state before any tick runs.
        Assert.All(Enumerable.Range(0, 5), a => Assert.Equal(1, p.Rounds(a)));
        Assert.All(Enumerable.Range(0, 5), a => Assert.True(p.Shares(a).IsValid));
        PolicyProfile[] before = Profiles(s);
        s.StepThroughMonth(0);                           // compute of tick 0 does not decide again
        Assert.All(Enumerable.Range(0, 5), a => Assert.Equal(1, p.Rounds(a)));
        for (long m = 1; m <= 11; m++)
            s.StepThroughMonth(m);
        Assert.All(Enumerable.Range(0, 5), a => Assert.Equal(1, p.Rounds(a)));
        s.StepThroughMonth(12);                          // the first annual step: t mod 12 = 0, t > 0
        Assert.All(Enumerable.Range(0, 5), a => Assert.Equal(2, p.Rounds(a)));
        s.StepThroughMonth(24);
        Assert.All(Enumerable.Range(0, 5), a => Assert.Equal(3, p.Rounds(a)));
        Assert.Equal(before, Profiles(s));               // the profile is a doctrine: it never changes
    }

    [Fact]
    public void A8_ProfileKey_UsesTick0_SoTheYearOfStartDoesNotMatter()
    {
        SliceSession s = Make(11);
        var ai = new CounterRng(11);
        for (int a = 0; a < 5; a++)
            Assert.Equal(PolicyRng.Profile(ai, a), s.World.Policy.Profile(a));
    }

    // ---- FIX_*

    [Fact]
    public void FixAll_GivesOneTrajectory_ForSeeds1To5()
    {
        string[]? first = null;
        for (ulong seed = 1; seed <= 5; seed++)
        {
            SliceSession s = Make(seed, c => SliceRunConfig.FixAll(c.Seed));
            var hashes = new List<string>();
            for (long m = 0; m < 30; m++)
            {
                s.StepThroughMonth(m);
                hashes.Add(s.LastHash);
            }
            first ??= [.. hashes];
            Assert.Equal(first, hashes);
        }
    }

    [Fact]
    public void FixA_ProfilesDoNotDependOnSeed_ButTheRestDoes()
    {
        var hashes = new List<string>();
        PolicyProfile[]? profiles = null;
        for (ulong seed = 1; seed <= 6; seed++)
        {
            SliceSession s = Make(seed, c => c with { FixA = true });
            profiles ??= Profiles(s);
            Assert.Equal(profiles, Profiles(s));
            for (long m = 0; m < 40; m++)
                s.StepThroughMonth(m);
            hashes.Add(s.LastHash);
        }
        Assert.True(hashes.Distinct().Count() > 1, "the K stream still follows the seed under FIX_A");
    }

    private static string KnownSets(SliceSession s) => string.Join(",", Enumerable.Range(0, 5)
        .Select(a => string.Concat(Enumerable.Range(0, s.World.Graph.Count).Select(r => s.World.Knowledge.IsKnown(a, r) ? '1' : '0'))));

    [Fact]
    public void FixK_WithFixA_KnowledgeDoesNotDependOnSeed_AndWithoutFixKItDoes()
    {
        var fixedK = new HashSet<string>();
        var freeK = new HashSet<string>();
        for (ulong seed = 1; seed <= 8; seed++)
        {
            SliceSession a = Make(seed, c => c with { FixK = true, FixA = true });
            SliceSession b = Make(seed, c => c with { FixA = true });
            for (long m = 0; m < 60; m++)
            {
                a.StepThroughMonth(m);
                b.StepThroughMonth(m);
            }
            fixedK.Add(KnownSets(a));
            freeK.Add(KnownSets(b));
        }
        Assert.Single(fixedK);
        Assert.True(freeK.Count > 1);
    }

    [Fact]
    public void FixE_ChangesOnlyTheEventStreamSeed()
    {
        SliceSession a = Make(1, c => c with { FixE = true });
        SliceSession b = Make(2, c => c with { FixE = true });
        Assert.Equal(SliceRunConfig.FixedSeed, a.World.Streams.Events.Seed);
        Assert.Equal(a.World.Streams.Events.Seed, b.World.Streams.Events.Seed);
        Assert.NotEqual(a.World.Streams.Know.Seed, b.World.Streams.Know.Seed);
    }

    // ---- factorial cells (§6.6)

    [Fact]
    public void ProfileCell_IsNineUPlusThreeGPlusS_AndFraSweMatchMain()
    {
        for (int cell = 0; cell < 27; cell++)
        {
            int u = cell / 9, g = cell / 3 % 3, sv = cell % 3;
            Assert.Equal(cell, 9 * u + 3 * g + sv);
            SliceSession s = Make(21, c => c with { ProfileCell = cell });
            SliceSession main = Make(21);
            PolicyProfile[] p = Profiles(s);
            Assert.Equal(((PolicyProfile)u, (PolicyProfile)g, (PolicyProfile)sv), (p[0], p[1], p[2]));
            Assert.Equal((Profiles(main)[3], Profiles(main)[4]), (p[3], p[4]));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => PolicyRules.ProfilesOfCell(27));
    }

    [Fact]
    public void ProfileCell_EntersTheStateHash()
    {
        Assert.NotEqual(Make(4, c => c with { ProfileCell = 0 }).LastHash, Make(4, c => c with { ProfileCell = 13 }).LastHash);
    }

    // ---- channel switches (§6.2)

    [Theory]
    [InlineData(SliceChannels.Alpha, "Alpha")]
    [InlineData(SliceChannels.Ramp, "Lambda0")]
    [InlineData(SliceChannels.B, "LearningQ10")]
    [InlineData(SliceChannels.Eta, "Eta")]
    [InlineData(SliceChannels.KCatch, "KCatch")]
    [InlineData(SliceChannels.Sigma, "Sigma")]
    [InlineData(SliceChannels.KDiff, "KDiff")]
    [InlineData(SliceChannels.EffortZero, "EFull")]
    [InlineData(SliceChannels.KTr, "KTr")]
    [InlineData(SliceChannels.KQp, "KQp")]
    public void ChannelSwitch_ChangesExactlyItsOwnParameter(SliceChannels channel, string property)
    {
        var (k0, i0, p0) = (new KnowledgeParams(), new IndustryParams(), new ProductsParams());
        var (k1, i1, p1) = SliceWorld.ApplyChannels(channel, k0, i0, p0);
        string[] changed = [.. Diff(k0, k1), .. Diff(i0, i1), .. Diff(p0, p1)];
        Assert.Equal([property], changed);
    }

    [Fact]
    public void ChannelSwitch_None_ChangesNothing_AndR0ChangesOnlyTheRule()
    {
        var (k0, i0, p0) = (new KnowledgeParams(), new IndustryParams(), new ProductsParams());
        var (k1, i1, p1) = SliceWorld.ApplyChannels(SliceChannels.None, k0, i0, p0);
        Assert.Empty(Diff(k0, k1).Concat(Diff(i0, i1)).Concat(Diff(p0, p1)));
        var (k2, i2, p2) = SliceWorld.ApplyChannels(SliceChannels.CultureR0, k0, i0, p0);
        Assert.Empty(Diff(k0, k2).Concat(Diff(i0, i2)).Concat(Diff(p0, p2)));
        Assert.IsType<CbCultureRule>(Make(1).World.Industry.Rule);
        Assert.IsType<V23CultureRule>(Make(1, c => c with { Channels = SliceChannels.CultureR0 }).World.Industry.Rule);
    }

    [Fact]
    public void ChannelSwitches_AllRunOneMonth_AndTheWorldCarriesTheParameter()
    {
        foreach (SliceChannels channel in Enum.GetValues<SliceChannels>().Where(c => c != SliceChannels.None))
        {
            SliceSession s = Make(2, c => c with { Channels = channel });
            s.StepThroughMonth(2);
            Assert.NotEqual(Make(2).LastHash, s.LastHash);   // the switch is part of the state (run.channels)
        }
        SliceWorld w = Make(2, c => c with { Channels = SliceChannels.Alpha | SliceChannels.Eta }).World;
        Assert.Equal(Fx6.Zero, w.IndustryParameters.Alpha);
        Assert.Equal(Fx6.Zero, w.KnowledgeParameters.Eta);
        Assert.NotEqual(Fx6.Zero, w.IndustryParameters.KCatch);
    }

    [Fact]
    public void EffortZero_LeavesTheWholeItemDInRest()
    {
        SliceSession s = Make(2, c => c with { Channels = SliceChannels.EffortZero });
        for (long m = 0; m < 6; m++)
        {
            Fx6[] d = [.. Enumerable.Range(0, 5).Select(a => s.World.SharesOf(a).Effort * s.World.Policy.Budget(a))];
            s.StepThroughMonth(m);
            for (int a = 0; a < 5; a++)
            {
                Assert.Equal(d[a], s.World.Industry.LastRest(a));
                for (int r = 0; r < s.World.IndustryLayout.ProcessCount; r++)
                    Assert.Equal(Fx6.Zero, s.World.Industry.ProcE(a, r));
            }
        }
    }

    [Fact]
    public void BZero_MeansNoAssemblyLearning()
    {
        // b = 0: assembly_hours(E) = h_first for every E (q10_b = 0).
        Fx6 hFirst = Fx6.FromInt(5000), hFloor = Fx6.FromInt(1000);
        Assert.Equal(hFirst, ProductsMath.AssemblyHours(Fx6.FromInt(100), hFirst, hFloor, 0));
        Assert.True(ProductsMath.AssemblyHours(Fx6.FromInt(100), hFirst, hFloor, 330) < hFirst);
        new ProductsParams { LearningQ10 = 0 }.Validate();
        Assert.Throws<ArgumentException>(() => new ProductsParams { LearningQ10 = -1 }.Validate());
    }

    // Names of public instance properties whose value differs (records of Fx6, ints and arrays).
    private static IEnumerable<string> Diff<T>(T a, T b)
    {
        foreach (var prop in typeof(T).GetProperties().Where(p => p.GetIndexParameters().Length == 0).OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            object? x = prop.GetValue(a), y = prop.GetValue(b);
            bool same = x is System.Collections.IEnumerable ex and not string
                ? ex.Cast<object>().SequenceEqual(((System.Collections.IEnumerable)y!).Cast<object>())
                : Equals(x, y);
            if (!same)
                yield return prop.Name;
        }
    }
}
