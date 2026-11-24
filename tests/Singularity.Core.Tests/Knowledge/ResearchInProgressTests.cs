using Singularity.Kernel;

namespace Singularity.Core.Tests.Knowledge;

/// <summary>ResearchInProgress (TASK-0119): the order, the cut at the buffer length and the guard, against a brute-force read of the same state.</summary>
public class ResearchInProgressTests
{
    [Fact]
    public void Rows_AreTheFrontierWithProgress_NearestToCompletionFirst_TiesByRow()
    {
        var run = new JetReference.Run();
        run.RunThroughMonth(12);
        var sys = run.System;
        for (int actor = 0; actor < 1; actor++)
        {
            var all = new List<(int Row, Fx6 Share)>();
            for (int r = 0; r < run.Graph.Count; r++)
            {
                Fx6 prog = sys.Prog(actor, r);
                if (prog > Fx6.Zero && sys.InFrontier(actor, r))
                    all.Add((r, prog / sys.NeedOf(r)));
            }
            Assert.NotEmpty(all);
            var expected = all.OrderByDescending(e => e.Share.Raw).ThenBy(e => e.Row).Select(e => e.Row).ToArray();
            foreach (int len in new[] { 1, 2, 3, 50 })
            {
                Span<int> rows = new int[len];
                int n = sys.ResearchInProgress(actor, rows);
                Assert.Equal(Math.Min(len, expected.Length), n);
                Assert.Equal(expected.Take(n), rows[..n].ToArray());
            }
        }
    }

    [Fact]
    public void ABufferOverTheLimit_IsRefused()
    {
        var run = new JetReference.Run();
        Assert.Throws<ArgumentOutOfRangeException>(() => run.System.ResearchInProgress(0, new int[KnowledgeSystemLimit + 1]));
    }

    private const int KnowledgeSystemLimit = Singularity.Core.Knowledge.KnowledgeSystem.MaxResearchRows;
}
