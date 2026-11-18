using Singularity.Core.Policy;
using Singularity.Core.Products;

namespace Singularity.Session.Tests;

/// <summary>TASK-0099: L2 over a run that crosses the first slot reuse (about month 265), for several thread counts and chunk sizes.</summary>
public class SlotReuseL2Tests
{
    private const long Months = 290;

    [Fact]
    public void L2_ThroughTheFirstSlotReuse_SameHashAcrossThreadsAndChunkSizes()
    {
        (int Threads, int Chunk)[] cfg = [(1, 1), (4, 8), (8, 32), (3, 1)];
        SliceSession[] runs = [.. cfg.Select(c => SliceTestData.Create(new SliceRunConfig { Seed = 1, Threads = c.Threads, ChunkSize = c.Chunk }))];
        bool reused = false;
        for (long t = 0; t < Months; t++)
        {
            foreach (SliceSession s in runs)
                s.StepThroughMonth(t);
            if (t % 5 == 4 || t >= 255)
                Assert.All(runs, r => HashAssert.Equal(runs[0].LastHash, r.LastHash, $"month {t}: hash differs"));
            SliceWorld w = runs[0].World;
            for (int a = 0; a < w.Scenario.ActorCount && !reused; a++)
            {
                for (int d = 0; d < w.ProductsLayout.Designs; d++)
                    reused |= w.Products.Generation(a, d) >= 2;
            }
        }
        Assert.True(reused, "the run never reused a slot: the test does not cross the reuse");
    }

    // Explains the TASK-0099 re-recording of the full-hash goldens (F17b ADMIT/YPROD x5, ProductAiSession, LinkedRun, ViewTests,
    // CalibrationParams): each is taken before the first slot reuse, so only the new columns and SchemaVersion 9 changed the hash;
    // the trajectory of the old columns is pinned by the projection tests (T6d, FleetAblation, ToolingUnmet).
    [Fact]
    public void NoSlotIsReused_BeforeTheMonthOfAnyRewrittenGolden()
    {
        foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5 })
        {
            foreach (LineCloseRule rule in new[] { LineCloseRule.Admit, LineCloseRule.YProd })
            {
                SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = seed, Threads = 2, LineClose = rule });
                for (long t = 0; t < 150; t++)
                    s.StepThroughMonth(t);
                Assert.False(ColumnProjection.SlotReused(s), $"F17b seed {seed} {rule}");
            }
        }
        SliceSession ai = SliceTestData.Create(new SliceRunConfig { Seed = 7, Threads = 1, ChunkSize = 1 });
        for (long t = 0; t < 150; t++)
            ai.StepThroughMonth(t);
        Assert.False(ColumnProjection.SlotReused(ai), "ProductAiSession golden, seed 7");
        SliceSession linked = SliceTestData.Create(new SliceRunConfig { Seed = 7, ProductAi = false });
        var player = new PlayerScript(linked);
        for (long t = 0; t < 60; t++)
        {
            player.Issue(t);
            linked.StepThroughMonth(t);
        }
        Assert.False(ColumnProjection.SlotReused(linked), "LinkedRun golden, seed 7");
    }
}
