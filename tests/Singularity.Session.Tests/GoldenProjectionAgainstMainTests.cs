using Singularity.Core.Policy;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>
/// TASK-0115 (review round 3): the goldens re-recorded for the end-to-end handle are compared with the trajectory of the build BEFORE the task
/// (base commit c5e7e98, main at the start of the branch). The pinned values are the <see cref="ColumnProjection.Hash"/> of ALL columns of that build,
/// taken at the same month of the same runs; here the same hash is taken over the columns of this build without the new design-reference
/// <c>.gen</c> columns (<see cref="ColumnProjection.IsDesignRefGenColumn"/>) and must be equal: the trajectory of every older column (v10 and before,
/// also the slot-generation and novelty columns) is bit for bit the old one, so only the new columns changed the re-recorded full hashes.
/// </summary>
public class GoldenProjectionAgainstMainTests
{
    private static string Old(SliceSession s) => ColumnProjection.Hash(s, n => !ColumnProjection.IsDesignRefGenColumn(n) && !ColumnProjection.IsHorizonColumn(n));

    // F17b runs of seeds 1…5 after month 149 (threads 2): ADMIT with tooling V0, YPROD with the default target.
    private static readonly string[] AdmitOld =
    [
        "03ead5c0321486648ba2328cc6f62806ee76348fd8d1962114b73f2c31448e82",
        "647fd1e97ab396a30bdb8719b5e5c27458a16636a732033c0ad75868cfc3b877",
        "f7e380e2598e98e3865e6aad6cb13a365d2699e32c7a1fed923241a36a978257",
        "b8dece15a4316dd7492b43be22832b9d5cf74b9dc036d1a2e044616c7680295c",
        "59e1cdc9d5df20b4d240818dbf743d602f99bb336f3a297ba6738c3ca6759400",
    ];

    private static readonly string[] YProdOld =
    [
        "29f78bade371352edfffea9141864a9a268f5f1208757a31bfaa52c1c9f28442",
        "73bb5c1fa312ad92980cfb4cdd087884189f8cf26f43e0c965adee19bf65b7eb",
        "d5df96a2741094bc659071d0a82c8bdf350c1c70d4dfa3a31d2a69cb5cbdb1ba",
        "a316e69ab5fa2044a106d75ec024b628af37bfef5b4f8c7e7b4a78e1e7a9f8ac",
        "97ec4600f7b27c1cc1336d7ab11037e97c7d9139a3391e52735f85513c8c8cab",
    ];

    private const string AiSeed7Old = "6edcaf762ef78065fece3fa1117dff598fcaf8e6d968f6ff76258228e7d20f9e";
    private const string LinkedSeed7Old = "c57ee4b6d3570661ba845bc6d5f56f7bfe35f022930d6f2d54e7b324594773e5";

    [Fact]
    public void F17bGoldens_OldColumns_EqualTheBuildBeforeTheTask()
    {
        for (int i = 0; i < 5; i++)
        {
            foreach (LineCloseRule rule in new[] { LineCloseRule.Admit, LineCloseRule.YProd })
            {
                ToolingTargetSpec target = rule == LineCloseRule.Admit ? ToolingTargetSpec.Parse("V0") : new ToolingTargetSpec();
                SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = (ulong)i + 1, Threads = 2, LineClose = rule, ToolingTarget = target });
                for (long t = 0; t < 150; t++)
                    s.StepThroughMonth(t);
                string expected = rule == LineCloseRule.Admit ? AdmitOld[i] : YProdOld[i];
                Assert.True(expected == Old(s), $"seed {i + 1} {rule}: an old column differs from the build before TASK-0115");
            }
        }
    }

    [Fact]
    public void ProductAiGolden_OldColumns_EqualTheBuildBeforeTheTask()
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 7, Threads = 1, ChunkSize = 1 });
        for (long t = 0; t < 150; t++)
            s.StepThroughMonth(t);
        Assert.True(AiSeed7Old == Old(s), "an old column of the ProductAiSession golden differs from the build before TASK-0115");
    }

    [Fact]
    public void LinkedRunGolden_OldColumns_EqualTheBuildBeforeTheTask()
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 7, ProductAi = false });
        var player = new PlayerScript(s);
        for (long t = 0; t < 60; t++)
        {
            player.Issue(t);
            s.StepThroughMonth(t);
        }
        Assert.True(LinkedSeed7Old == Old(s), "an old column of the LinkedRun golden differs from the build before TASK-0115");
    }
}
