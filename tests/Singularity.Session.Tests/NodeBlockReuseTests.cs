using Singularity.Core.Commands;
using Singularity.Core.Policy;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>
/// TASK-0103 R1/R2: the node blocks of a view are compact (every array below the large-object limit), equal what a full rebuild reads,
/// are handed out again only for an unchanged state of the same month, and are rebuilt when a column they read changes.
/// </summary>
public class NodeBlockReuseTests
{
    private static BudgetShares Shares(long r, long c, long d, long p, long q) =>
        new(Fx6.FromRaw(r), Fx6.FromRaw(c), Fx6.FromRaw(d), Fx6.FromRaw(p), Fx6.FromRaw(q));

    private static readonly BudgetShares[] ShareSets =
    [
        Shares(200_000, 200_000, 200_000, 300_000, 100_000),
        Shares(500_000, 100_000, 100_000, 200_000, 100_000),
    ];

    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    public void EveryTick_ReusedBlocksEqualAFullRebuild_AndAreReusedOnCommandTicks(ulong seed)
    {
        SliceSession reuse = SliceTestData.Create(SliceRunConfig.Legacy with { Seed = seed });
        SliceSession fresh = SliceTestData.Create(SliceRunConfig.Legacy with { Seed = seed });
        fresh.World.ReuseNodeBlocks = false;
        int local = 0, shared = 0, compared = 0;
        var last = new object?[reuse.View.Actors.Length];
        for (long m = 0; m < 6; m++)
        {
            foreach (SliceSession s in new[] { reuse, fresh })
            {
                for (int a = 0; a < 3; a++)
                    Assert.True(s.Submit(SourceClass.Harness, 0, local + a, new SetBudgetShares(a, ShareSets[(int)((m + a) % 2)])).Accepted);
                Assert.True(s.Submit(SourceClass.Player, 0, (int)m, new SetEffortFocus(1, [47])).Accepted);
                Assert.True(s.Submit(SourceClass.Player, 1, (int)m, new StartSeries(0, s.HandleOf(0, 0), Fx6.FromInt(5), true)).Accepted);
                Assert.True(s.Submit(SourceClass.Player, 2, (int)m, new CloseLine(1, s.HandleOf(1, 0))).Accepted);
            }
            local += 3;
            long end = reuse.World.Calendar.TickOfMonth(m) + 3;
            while (reuse.Tick < end)
            {
                reuse.Step(1);
                fresh.Step(1);
                for (int a = 0; a < reuse.View.Actors.Length; a++)
                {
                    NodeList x = reuse.View.Actors[a].Nodes, y = fresh.View.Actors[a].Nodes;
                    Assert.True(x.Equals(y), $"tick {reuse.Tick} actor {a}");
                    Assert.False(ReferenceEquals(x.BlockIdentity, y.BlockIdentity));
                    if (ReferenceEquals(x.BlockIdentity, last[a]))
                        shared++;
                    last[a] = x.BlockIdentity;
                    compared++;
                }
            }
        }
        Assert.True(shared > 0 && shared < compared, $"reused {shared} of {compared}");
    }

    [Fact]
    public void ColumnLoad_BetweenSnapshotsOfTheSameMonth_RebuildsTheBlock()
    {
        SliceSession s = SliceTestData.Create(SliceRunConfig.Legacy with { Seed = 1 });
        s.StepThroughMonth(2);
        SliceWorld w = s.World;
        ActorView before = ViewBuilder.Snapshot(w, s.Tick, null, 2, null, null).Actors[0];
        ActorView same = ViewBuilder.Snapshot(w, s.Tick, null, 2, null, null).Actors[0];
        Assert.Same(before.Nodes.BlockIdentity, same.Nodes.BlockIdentity);     // nothing changed: the block is handed out again

        int number = w.Graph.Number[0];
        IntColumn known = w.Table.GetInt($"know.known.{number}");
        int[] values = known.Current.ToArray();
        values[0] = values[0] == 0 ? 1 : 0;
        known.Load(values);
        ActorView after = ViewBuilder.Snapshot(w, s.Tick, null, 2, null, null).Actors[0];
        Assert.NotSame(before.Nodes.BlockIdentity, after.Nodes.BlockIdentity);
        Assert.NotEqual(before.Nodes[0].Known, after.Nodes[0].Known);

        // A command column is not read by the blocks: loading it keeps them.
        IntColumn setFlag = w.Table.GetInt("cmd.shares.set");
        setFlag.Load(setFlag.Current.ToArray());
        ActorView afterCmd = ViewBuilder.Snapshot(w, s.Tick, null, 2, null, null).Actors[0];
        Assert.Same(after.Nodes.BlockIdentity, afterCmd.Nodes.BlockIdentity);
    }

    [Fact]
    public void EveryArrayOfABlock_IsBelowTheLargeObjectLimit_AtTheFullScalePoint()
    {
        // The block of an actor in point A (2000 nodes, 651 processes): masks, process views, known-since.
        const int nodes = 2000, processes = 651, limit = 85_000;
        Assert.True((nodes + 63) / 64 * sizeof(ulong) < limit);
        Assert.True(processes * System.Runtime.CompilerServices.Unsafe.SizeOf<ProcessView>() < limit);
        Assert.True(nodes * sizeof(long) < limit);
    }

    [Fact]
    public void NodeList_CreateFromViews_RoundTripsAndEnumerates()
    {
        var pv = new ProcessView(Fx6.One, Fx6.Zero, true, Fx6.One, true, Fx6.Zero);
        NodeView[] views = [new(101, true, false, pv, 5), new(102, false, true, null, -1), new(103, true, true, null, 0)];
        NodeList list = NodeList.Create(views);
        Assert.Equal(3, list.Count);
        Assert.Equal(views, list.ToArray());
        Assert.Equal(views[0], list[0]);
        Assert.True(list.Equals(NodeList.Create(views)));
        Assert.False(default(NodeList).IsPresent);
        Assert.Throws<InvalidOperationException>(() => default(NodeList)[0]);
        Assert.Empty(default(NodeList));
    }

    // TASK-0103 (Codex): a node without a known-since month stays without one in a list where other nodes have it.
    [Fact]
    public void NodeList_MixedKnownSince_KeepsTheMissingValueMissing()
    {
        NodeView[] views = [new(101, true, false, null, null), new(102, true, false, null, 5), new(103, false, false, null, 0), new(104, true, false, null, long.MinValue), new(105, true, false, null, long.MaxValue)];
        NodeList list = NodeList.Create(views);
        Assert.Equal(views, list.ToArray());
        Assert.Null(list[0].KnownSince);
        Assert.Equal(5, list[1].KnownSince);
        Assert.Equal(0, list[2].KnownSince);
        Assert.Equal(long.MinValue, list[3].KnownSince);   // no value of long is reserved as "missing"
    }

    // TASK-0103 (Codex): where the per-actor process array reaches the large-object heap (85 000 bytes): from this many processes on.
    [Fact]
    public void ProcessArray_LeavesTheSmallObjectHeap_AboveTheDocumentedProcessCount()
    {
        const int limit = 85_000, pointA = 651;
        int size = System.Runtime.CompilerServices.Unsafe.SizeOf<ProcessView>();
        int maxBelow = (limit - 1) / size;   // the array header adds a few bytes: this is the bound to within one element
        Assert.Equal(48, size);
        Assert.Equal(1_770, maxBelow);
        Assert.True(pointA * 2 < maxBelow, "point A has a margin of more than two times before the large-object heap");
    }
}
