using System.Collections.Immutable;
using Singularity.Kernel;
using Singularity.TestSupport;

namespace Singularity.Session.Tests;

/// <summary>TASK-0147: SliceRunConfig.ResearchWeightByDiscipline — default and all-ones are the old trajectory; a real weight changes it; bad input refused.</summary>
public class DisciplineWeightTests
{
    private static SliceRunConfig Cfg() => HorizonConfigs.Config("Main", 1);

    private static string Digest(SliceSession s) => ColumnProjection.Hash(s, n => !n.StartsWith("run.", StringComparison.Ordinal));

    private static ImmutableArray<DisciplineWeight> W(params (string, long)[] w) => [.. w.Select(x => new DisciplineWeight(x.Item1, Fx6.FromRaw(x.Item2)))];

    [Fact]
    public void Default_HasNoColumn_AndAllOnesGiveTheSameTrajectory()
    {
        SliceSession off = ViewSupport.Run(Cfg(), 200);
        Assert.DoesNotContain(off.World.Table.Columns, c => c.Name.StartsWith("run.k.disc_weight", StringComparison.Ordinal));
        SliceSession ones = ViewSupport.Run(Cfg() with { ResearchWeightByDiscipline = W(("aero", 1_000_000), ("prop", 1_000_000), ("metal", 1_000_000)) }, 200);
        Assert.Contains(ones.World.Table.Columns, c => c.Name == "run.k.disc_weight.prop");
        HashAssert.Equal(Digest(off), Digest(ones));
    }

    [Fact]
    public void ARealWeight_ChangesTheTrajectory_AndIsDeterministic()
    {
        var cfg = Cfg() with { ResearchWeightByDiscipline = W(("prop", 3_000_000)) };
        string a = Digest(ViewSupport.Run(cfg, 200));
        HashAssert.Equal(a, Digest(ViewSupport.Run(cfg with { Threads = 1, ChunkSize = 1 }, 200)));
        HashAssert.NotEqual(Digest(ViewSupport.Run(Cfg(), 200)), a);
    }

    [Fact]
    public void Fingerprint_DiffersOnlyWhenSet()
    {
        Assert.NotEqual(Cfg().SnapshotFingerprint(), (Cfg() with { ResearchWeightByDiscipline = W(("prop", 1_150_000)) }).SnapshotFingerprint());
        Assert.NotEqual((Cfg() with { ResearchWeightByDiscipline = W(("prop", 1_150_000)) }).SnapshotFingerprint(),
            (Cfg() with { ResearchWeightByDiscipline = W(("prop", 1_160_000)) }).SnapshotFingerprint());
    }

    [Theory]
    [InlineData("nope", 1_000_000)]
    [InlineData("prop", 0)]
    [InlineData("prop", -1)]
    public void BadEntry_Throws(string name, long raw) =>
        Assert.Throws<ArgumentException>(() => SliceTestData.Create(Cfg() with { ResearchWeightByDiscipline = W((name, raw)) }));

    [Fact]
    public void DuplicateDiscipline_Throws() =>
        Assert.Throws<ArgumentException>(() => SliceTestData.Create(Cfg() with { ResearchWeightByDiscipline = W(("prop", 1_000_000), ("prop", 2_000_000)) }));
}
