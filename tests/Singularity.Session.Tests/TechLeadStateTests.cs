using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>Contract §9.1 r13a / T-17 gaps: <c>q</c> is hashed state, and <c>ActorView.Key</c> is <c>KeyOf</c> (F23).</summary>
public class TechLeadStateTests
{
    [Fact]
    public void QColumn_IsInStateHash_AndNonTrivialAfterRun()
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 1, Threads = 1 });
        for (long t = 0; t < 60; t++)
            s.StepThroughMonth(t);
        Table table = s.World.Table;
        var qCols = table.Columns.OfType<LongColumn>().Where(c => c.Name.EndsWith(".q", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(qCols);
        Assert.Contains(qCols, c => c.Current.ToArray().Any(v => v != 0));          // the scenario fleet carries q
        string before = StateHash.ComputeHex(table, SliceWorld.SchemaVersion);
        Assert.Equal(s.LastHash, before);
        LongColumn col = qCols[0];
        long[] saved = col.Current.ToArray();
        long[] flipped = (long[])saved.Clone();
        flipped[0] ^= 1L << 3;                                                       // one cohort-year bit of one row
        col.Load(flipped);
        Assert.NotEqual(before, StateHash.ComputeHex(table, SliceWorld.SchemaVersion));
        col.Load(saved);
        Assert.Equal(before, StateHash.ComputeHex(table, SliceWorld.SchemaVersion));
    }

    [Fact]
    public void ActorViewKey_EqualsTechKey_Seeds1To5()
    {
        foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5 })
        {
            SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = seed, Threads = 1, ViewMode = SliceViewMode.Full });
            for (long t = 0; t < 200; t++)
            {
                s.StepThroughMonth(t);
                SliceView v = s.View;
                for (int a = 0; a < s.World.Scenario.ActorCount; a++)
                    Assert.Equal(s.World.Products.KeyOf(a, t + 1), v.Actors[a].Key);
            }
        }
    }
}
