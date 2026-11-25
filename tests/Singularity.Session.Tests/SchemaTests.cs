using Singularity.Core.Policy;

namespace Singularity.Session.Tests;

/// <summary>ADR-0013 п. 2: the column layout is a schema; a change without a version bump fails here.</summary>
public class SchemaTests
{
    // Pair (version, fingerprint). Change both together, in one commit, with the reason. v2 (T6b): pol.qp.*, pol.train, prd.tbSpent, one more requester (QP_REQ). v3 (T6c, contract r4 #128): prd.d*.cancelled / idleC / builtAny, one more rejection counter (BAD_CANCEL), cancel_design shares the cmd.dsn slots. v4 (T6c product AI): pol.dsn.*, pol.start.*, pol.close.*, pol.line.*, pol.rep.g*, run.ai. v5 (T-fleet, contract r6): prd.d*.retired / sold / quotaBase / soldY / lineBind, prd.saleIncome / saleCap / saleY / extTotal, nine more rejection counters (BAD_RETIRE, BAD_SELL), ind.mr.* (mastered_run), cmd.flt.*, pol.flt.*, run.fleet. v6 (F33, contract r8, TASK-0032): ind.unmet.c.d (tool_unmet). v7 (T6d, contract r7/r8, TASK-0033): pol.lic.* (gen, seller, mask, start, offer, flow, the queue q.* and the BAD_LICENCE counters), pol.dis.* (start, end), run.t6d. v8 (TASK-0058 step 5, contract r13a #247): prd.d*.q (q[design, y] as a bit mask of cohort years). v9 (TASK-0099, ADR-0005 p. 3): prd.d*.generation, prd.d*.parentGen, prd.lp.g*.gen, the StaleHandle rejection counter. v10 (TASK-0110, DEBT-0054): prd.altReached.* (per actor, alternative reached О). v11 (TASK-0115, ADR-0005 p. 3, DEBT-0052): the generation of every design reference of the commands and the AI: cmd.dsn.*.gen, cmd.flt.*.gen, cmd.series.*.gen, cmd.close.*.gen, pol.dsn.*.gen, pol.flt.*.gen, pol.start.*.gen, pol.close.*.gen. v12 (TASK-0121, #423): cohort years to 2030 (prd.d*.cohort.1978…2030) and the second block of the q mask (prd.d*.q.1). v13 (TASK-0129, #431): prd.d*.replCredit, the replacement credit of the fleet ageing. v14 (TASK-0134, #438-#440): prd.prog.p* (state of the mastery programme per tier ≥ 5 process), pol.prog.p* (the AI's programme starts), cmd.prog.p* (the player's), pol.below.* (months a line stayed below y_prod), prd.rejected.BadProgramme. v15 (TASK-0182): pol.flt.gFront (the G_front the AI's fleet sale was priced at).
    private const int PinnedVersion = 15;
    // Re-recorded in TASK-0182: schema v15, new column pol.flt.gFront.
    // Re-recorded in TASK-0134 (train 2: Hcap(t), mastery): schema v14, the new columns listed above.
    // Re-recorded in TASK-0133 (train 2: G5/G6 data + fleet ageing): nodes 62-81 and the G5/G6 alternatives change the column sizes (81 nodes, six generations); SchemaVersion stays 13; reason and list in the task file.
    // Re-recorded in TASK-0147 (train 2): nodes 82-99 (G5/G6 rev 3.1 chains) and the ahead-of-time cost defaults k 0.02, pow 2, max 10 (#465) change every trajectory on the current data; explanation: docs/simulation/golden-rerecord-train2.md.
    private const string PinnedFingerprint = "4b4057ed7a73e33705ee8e6fe17d461fe65774773e099a8bffeed0d719a224b3";

    [Fact]
    public void ColumnLayout_MatchesThePinnedSchemaVersion()
    {
        SliceWorld w = SliceTestData.Create(SliceRunConfig.Legacy with { ToolingTarget = ToolingTargetSpec.Parse("V0") }).World;   // V0 adds no run.tooling columns
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
        string main = h(SliceRunConfig.Legacy with { Seed = 5 });
        Assert.NotEqual(main, h(SliceRunConfig.Legacy with { Seed = 5, FixE = true }));
        Assert.NotEqual(main, h(SliceRunConfig.Legacy with { Seed = 5, Channels = SliceChannels.Sigma }));
        Assert.NotEqual(main, h(SliceRunConfig.Legacy with { Seed = 5, Start = StartVariant.Symmetric }));
        Assert.Equal(main, h(SliceRunConfig.Legacy with { Seed = 5, Threads = 8, ChunkSize = 3 }));   // execution options are not state
    }
}
