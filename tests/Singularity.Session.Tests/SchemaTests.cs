namespace Singularity.Session.Tests;

/// <summary>ADR-0013 п. 2: the column layout is a schema; a change without a version bump fails here.</summary>
public class SchemaTests
{
    // Pair (version, fingerprint). Change both together, in one commit, with the reason. v2 (T6b): pol.qp.*, pol.train, prd.tbSpent, one more requester (QP_REQ). v3 (T6c, contract r4 #128): prd.d*.cancelled / idleC / builtAny, one more rejection counter (BAD_CANCEL), cancel_design shares the cmd.dsn slots. v4 (T6c product AI): pol.dsn.*, pol.start.*, pol.close.*, pol.line.*, pol.rep.g*, run.ai. v5 (T-fleet, contract r6): prd.d*.retired / sold / quotaBase / soldY / lineBind, prd.saleIncome / saleCap / saleY / extTotal, nine more rejection counters (BAD_RETIRE, BAD_SELL), ind.mr.* (mastered_run), cmd.flt.*, pol.flt.*, run.fleet.
    private const int PinnedVersion = 5;
    private const string PinnedFingerprint = "39a36a45b2a0b878c17039d70262b7a00d20ec06c03b85980939031f54a9e107";

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
