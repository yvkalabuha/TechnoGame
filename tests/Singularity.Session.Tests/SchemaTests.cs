namespace Singularity.Session.Tests;

/// <summary>ADR-0013 п. 2: the column layout is a schema; a change without a version bump fails here.</summary>
public class SchemaTests
{
    // Pair (version, fingerprint). Change both together, in one commit, with the reason. v2 (T6b): pol.qp.*, pol.train, prd.tbSpent, one more requester (QP_REQ).
    private const int PinnedVersion = 2;
    private const string PinnedFingerprint = "1051e23120566ce6f5d9d6a7e09f93ae4429590d8b753746f294e47d5ffa8fc0";

    [Fact]
    public void ColumnLayout_MatchesThePinnedSchemaVersion()
    {
        SliceWorld w = SliceTestData.Create().World;
        Assert.True(PinnedFingerprint == w.SchemaFingerprint(),
            $"column layout changed (fingerprint {w.SchemaFingerprint()}): raise SliceWorld.SchemaVersion (now {SliceWorld.SchemaVersion}) and re-pin");
        Assert.Equal(PinnedVersion, SliceWorld.SchemaVersion);
    }

    [Fact]
    public void Columns_AreInSystemOrder_KnowledgeIndustryProductsPolicyCommandsRun()
    {
        string[] names = [.. SliceTestData.Create().World.Table.Columns.Select(c => c.Name)];
        int First(string prefix) => Array.FindIndex(names, n => n.StartsWith(prefix, StringComparison.Ordinal));
        Assert.True(First("know.") < First("ind.") && First("ind.") < First("prd.") && First("prd.") < First("pol.")
            && First("pol.") < First("cmd.") && First("cmd.") < First("run."));
        Assert.Equal(names.Length, names.Distinct().Count());
    }

    [Fact]
    public void RunConfig_IsPartOfTheState()
    {
        string h(SliceRunConfig c) => SliceTestData.Create(c).LastHash;
        string main = h(new SliceRunConfig { Seed = 5 });
        Assert.NotEqual(main, h(new SliceRunConfig { Seed = 5, FixE = true }));
        Assert.NotEqual(main, h(new SliceRunConfig { Seed = 5, Channels = SliceChannels.Sigma }));
        Assert.NotEqual(main, h(new SliceRunConfig { Seed = 5, Start = StartVariant.Symmetric }));
        Assert.Equal(main, h(new SliceRunConfig { Seed = 5, Threads = 8, ChunkSize = 3 }));   // execution options are not state
    }
}
