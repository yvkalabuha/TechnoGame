using Singularity.Core.Events;
using Singularity.Core.Knowledge;
using Singularity.Core.Policy;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>Spec §9.1: every stream draws with exactly the key (system, tick, entity, n) of the table, and seeds follow FIX_*.</summary>
public class StreamKeyTests
{
    private static readonly CounterRng Rng = new(12345);

    [Fact]
    public void Know_UsesSystem1_Tick_ActorTimes64PlusNode_N0()
    {
        Assert.Equal(Rng.NextU64(17, 1, 3 * 64 + 21, 0), KnowledgeRng.Hazard(Rng, 17, 3, 21));
    }

    [Fact]
    public void KnowLk_UsesSystem1_Tick0_ActorTimes64_NPlusG()
    {
        Assert.Equal(Rng.NextU64(0, 1, 2 * 64, 1 + 3), KnowledgeRng.ScheduleClock(Rng, 2, 3));
        Assert.Equal(Rng.NextU64(0, 1, 2 * 64, 5 + 3), KnowledgeRng.ScheduleNever(Rng, 2, 3));
    }

    [Fact]
    public void PrioJ_UsesSystem2_Tick12Y_ActorTimes64PlusNode_N0()
    {
        ulong u = Rng.NextU64(36, 2, 4 * 64 + 9, 0);
        Assert.Equal(Fx6.FromRaw(CounterRng.UniformInt(u, 500_000, 1_500_000)), KnowledgeRng.J(Rng, 3, 4, 9));
    }

    [Fact]
    public void EvtDis_UsesSystem3_Tick_Actor_N0Onset_N1Duration()
    {
        Assert.Equal(Rng.NextU64(50, 3, 2, 0), EventRng.DisruptionOnset(Rng, 50, 2));
        Assert.Equal(Rng.NextU64(50, 3, 2, 1), EventRng.DisruptionDuration(Rng, 50, 2));
    }

    [Fact]
    public void EvtLic_UsesSystem4_Tick_SellerTimes8PlusBuyer_N0()
    {
        Assert.Equal(Rng.NextU64(24, 4, 2 * 8 + 4, 0), EventRng.LicenceOffer(Rng, 24, 2, 4));
    }

    [Fact]
    public void Ai_UsesSystem5_Tick0_Actor_N0_AndFloor3U()
    {
        for (int a = 0; a < 5; a++)
        {
            ulong u = Rng.NextU64(0, 5, a, 0);
            Assert.Equal((PolicyProfile)(int)(((u >> 11) * 3) >> 53), PolicyRng.Profile(Rng, a));
        }
    }

    [Fact]
    public void Start_UsesSystem6_Tick0_FieldIndex_N0_AndRangeFormula()
    {
        for (int i = 0; i < 40; i++)
        {
            ulong r = Rng.NextU64(0, 6, i, 0) >> 11;
            long expected = 800_000 + (long)(((UInt128)r * 400_001) >> 53);
            Assert.Equal(expected, PolicyRng.StartMultiplier(Rng, i).Raw);
        }
    }

    [Fact]
    public void Seeds_FollowFixFlags()
    {
        SliceStreams none = SliceStreams.Create(new SliceRunConfig { Seed = 9 });
        Assert.Equal(9UL, none.Know.Seed);
        Assert.Equal(9UL, none.Events.Seed);
        Assert.Equal(9UL, none.Ai.Seed);
        Assert.Equal(9UL, none.Start.Seed);

        SliceStreams k = SliceStreams.Create(new SliceRunConfig { Seed = 9, FixK = true });
        Assert.Equal((SliceRunConfig.FixedSeed, 9UL, 9UL, 9UL), (k.Know.Seed, k.Events.Seed, k.Ai.Seed, k.Start.Seed));
        SliceStreams e = SliceStreams.Create(new SliceRunConfig { Seed = 9, FixE = true });
        Assert.Equal((9UL, SliceRunConfig.FixedSeed, 9UL, 9UL), (e.Know.Seed, e.Events.Seed, e.Ai.Seed, e.Start.Seed));
        SliceStreams a = SliceStreams.Create(new SliceRunConfig { Seed = 9, FixA = true });
        Assert.Equal((9UL, 9UL, SliceRunConfig.FixedSeed, 9UL), (a.Know.Seed, a.Events.Seed, a.Ai.Seed, a.Start.Seed));
        Assert.Equal(0x5EEDUL, SliceRunConfig.FixedSeed);
    }

    [Fact]
    public void Profile_IsUniformEnough_OverManyKeys()
    {
        var counts = new int[3];
        for (ulong seed = 1; seed <= 3000; seed++)
            counts[(int)PolicyRng.Profile(new CounterRng(seed), 0)]++;
        Assert.All(counts, c => Assert.InRange(c, 850, 1150));
    }
}
