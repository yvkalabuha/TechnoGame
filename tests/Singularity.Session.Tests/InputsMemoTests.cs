using Singularity.Core.Commands;
using Singularity.Core.Industry;
using Singularity.Core.Policy;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>
/// TASK-0103 F4: the memo of the effort allocation and of the tooling plan (keyed by actor, month, Table.Version) is invisible to the state:
/// the hash of every month, with commands between the months, is the same with and without it, and the memo serves the compute of the step.
/// </summary>
public class InputsMemoTests
{
    private static BudgetShares Shares(long r, long c, long d, long p, long q) =>
        new(Fx6.FromRaw(r), Fx6.FromRaw(c), Fx6.FromRaw(d), Fx6.FromRaw(p), Fx6.FromRaw(q));

    private static readonly BudgetShares[] ShareSets =
    [
        Shares(200_000, 200_000, 200_000, 300_000, 100_000),
        Shares(500_000, 100_000, 100_000, 200_000, 100_000),
        Shares(100_000, 100_000, 500_000, 200_000, 100_000),
    ];

    private static List<string> Run(ulong seed, bool memo)
    {
        SliceSession s = SliceTestData.Create(SliceRunConfig.Legacy with { Seed = seed, HashEveryTick = true });
        s.World.MemoizeInputs = memo;
        var hashes = new List<string>();
        int local = 0, playerLocal = 0;
        for (long m = 0; m < 10; m++)
        {
            for (int a = 0; a < 5; a++)
                Assert.True(s.Submit(SourceClass.Harness, 0, local++, new SetBudgetShares(a, ShareSets[(int)((m + a) % ShareSets.Length)])).Accepted);
            if (m % 3 == 0)
            {
                CommandVerdict v = s.Submit(SourceClass.Player, 0, playerLocal++, new SetEffortFocus(1, [47]));
                Assert.True(v.Accepted, v.Message);
            }
            s.StepThroughMonth(m);
            hashes.Add(s.LastHash);
        }
        return hashes;
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    public void Hash_EqualWithAndWithoutMemo(ulong seed) => Assert.Equal(Run(seed, memo: false), Run(seed, memo: true));

    [Fact]
    public void Memo_ServesTheComputeOfTheMonth_AndNotAChangedSnapshot()
    {
        SliceSession s = SliceTestData.Create(SliceRunConfig.Legacy with { Seed = 1 });
        SliceWorld w = s.World;
        s.StepThroughMonth(2);
        long month = 3;
        for (int a = 0; a < w.IndustryLayout.SiteCount; a++)
        {
            Fx6 fresh = w.Industry.RestActor(a, month);
            w.Industry.MemoizeActor(a, month);
            Assert.Equal(fresh, w.Industry.RestActor(a, month));
            Fx6 rest = w.Industry.RestActor(a, month);
            var plan = w.IndustryInputs.Memoize(a, month, rest);
            Assert.Same(plan, w.IndustryInputs.Plan(a, month, rest));
            // Another rest or month is a miss: a fresh plan, not the memo.
            Assert.NotSame(plan, w.IndustryInputs.Plan(a, month, rest + Fx6.One));
            Assert.NotSame(plan, w.IndustryInputs.Plan(a, month + 1, rest));
        }
        // A step changes Table.Version: the memo of the old snapshot is not served.
        var before = w.IndustryInputs.Memoize(0, month, w.Industry.RestActor(0, month));
        s.StepThroughMonth(month);
        Assert.NotSame(before, w.IndustryInputs.Plan(0, month, w.Industry.RestActor(0, month)));
    }
}
