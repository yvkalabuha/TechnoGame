using System;
using System.IO;
using System.Linq;
using Singularity.Client.Spike;
using Singularity.Core.Data;
using Singularity.Session;
using Xunit;

namespace Singularity.Client.Audio.Tests;

/// <summary>TASK-0130: what the client subscribes the session to (the played and the shown actor) and that a change shows in the view at once, also while paused.</summary>
public class ViewSubscriptionSyncTests
{
    private static readonly GameData Data = ModLoader.Load(Path.Combine(RepoRoot(), "mods", "core"));

    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Singularity.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Singularity.sln not found above the test binaries.");
    }

    private static SliceSession Paused()
    {
        SliceSession s = SliceSession.Create(Data, new SliceRunConfig { Seed = 7, ViewScope = ViewScope.Subscribed });
        s.StepThroughMonth(2);
        return s;
    }

    private static int[] Present(SliceSession s) => Enumerable.Range(0, s.View.Actors.Length).Where(a => s.View.Actors[a].Nodes.IsPresent).ToArray();

    [Fact]
    public void Sync_WhilePaused_ShowsThePlayedAndTheShownActor_InTheSameTickWithTheSameHash()
    {
        SliceSession s = Paused();
        string hash = s.LastHash;
        long tick = s.Tick;
        SliceView before = s.View;
        var sync = new ViewSubscriptionSync();
        Assert.Equal(3, sync.Sync(s, 1, 3));
        Assert.Equal(new[] { 1, 3 }, Present(s));
        Assert.Equal(tick, s.View.Tick);
        Assert.Equal(before.Tick, s.View.Tick);
        HashAssert.Equal(before.StateHash, s.View.StateHash);
        HashAssert.Equal(hash, s.LastHash);
        Assert.Equal(tick, s.Tick);
    }

    [Fact]
    public void Sync_WhenTheShownActorChanges_KeepsThePlayedOne_AndDropsTheOldShown()
    {
        SliceSession s = Paused();
        var sync = new ViewSubscriptionSync();
        sync.Sync(s, 1, 3);
        sync.Sync(s, 1, 4);
        Assert.Equal(new[] { 1, 4 }, Present(s));
        sync.Sync(s, 1, 1);                      // the played actor shown: one block
        Assert.Equal(new[] { 1 }, Present(s));
    }

    [Fact]
    public void Stale_IsFalseAfterSync_TrueAfterAChange_AndNoRebuildHappensWhenNothingChanged()
    {
        SliceSession s = Paused();
        var sync = new ViewSubscriptionSync();
        Assert.True(sync.Stale(1, 3));
        sync.Sync(s, 1, 3);
        Assert.False(sync.Stale(1, 3));
        Assert.True(sync.Stale(1, 2));
        Assert.True(sync.Stale(0, 3));
        SliceView view = s.View;
        sync.Sync(s, 1, 3);
        Assert.Same(view, s.View);
    }
}
