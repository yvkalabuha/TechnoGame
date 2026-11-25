using Singularity.Core.Commands;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>
/// TASK-0130 (ADR-0025): with <see cref="ViewScope.Subscribed"/> the views carry the node blocks of the subscribed actors only; the state, the hash and the
/// subscribed actors' views do not depend on the scope or on the subscription.
/// </summary>
public class ViewSubscriptionTests
{
    private static SliceRunConfig Cfg(ViewScope scope, bool probe = false, bool everyTick = false) =>
        new() { Seed = 7, ViewScope = scope, DailyTickProbe = probe, HashEveryTick = everyTick };

    private static SliceSession Make(ViewScope scope, bool probe = false, bool everyTick = false) => SliceTestData.Create(Cfg(scope, probe, everyTick));

    private static int[] Subscribed(SliceView v) => [.. Enumerable.Range(0, v.Actors.Length).Where(a => v.Actors[a].Nodes.IsPresent)];

    private static string DumpWithout(ActorView a) => ViewSupport.Dump(a with { Nodes = default });

    [Fact]
    public void DefaultScopeIsAll_EveryActorHasNodes_AndSubscribingIsRefused()
    {
        Assert.Equal(ViewScope.All, SliceRunConfig.Legacy.ViewScope);
        SliceSession s = Make(ViewScope.All);
        Assert.All(s.View.Actors, a => Assert.True(a.Nodes.IsPresent && a.Nodes.Count > 0));
        Assert.Throws<InvalidOperationException>(() => s.SetViewSubscription([0]));
    }

    [Fact]
    public void Subscribed_UnsubscribedActorsAreAbsent_TheIndexerThrows_AndTheCacheHoldsNoBlockOfThem()
    {
        SliceSession s = Make(ViewScope.Subscribed);
        Assert.Empty(Subscribed(s.View));            // nobody is subscribed until asked
        s.SetViewSubscription([1, 3]);
        s.RebuildView();
        s.Step(1);
        s.StepThroughMonth(1);
        Assert.Equal([1, 3], Subscribed(s.View));
        for (int a = 0; a < s.View.Actors.Length; a++)
        {
            NodeList n = s.View.Actors[a].Nodes;
            Assert.Equal(a is 1 or 3, s.World.NodeBlocks.TryGet(a, out _));
            if (n.IsPresent)
                continue;
            Assert.Empty(n);
            Assert.Equal("actor not subscribed", Assert.Throws<InvalidOperationException>(() => n[0]).Message);
        }
        s.SetViewSubscription([3]);
        s.RebuildView();
        Assert.Equal([3], Subscribed(s.View));
        Assert.False(s.World.NodeBlocks.TryGet(1, out _));
    }

    [Fact]
    public void SetViewSubscription_RejectsAnActorOutOfRange_AndOffTheOwnerThread()
    {
        SliceSession s = Make(ViewScope.Subscribed);
        Assert.Throws<ArgumentOutOfRangeException>(() => s.SetViewSubscription([s.View.Actors.Length]));
        Assert.Throws<ArgumentOutOfRangeException>(() => s.SetViewSubscription([-1]));
        Exception? e = null;
        var t = new Thread(() => e = Record.Exception(() => s.SetViewSubscription([0])));
        t.Start();
        t.Join();
        Assert.IsType<InvalidOperationException>(e);
    }

    [Fact]
    public void NodeListEquality_TwoAbsentAreEqual_AbsentAndPresentAreNot()
    {
        SliceSession s = Make(ViewScope.Subscribed);
        s.SetViewSubscription([0]);
        s.RebuildView();
        NodeList present = s.View.Actors[0].Nodes, absent = s.View.Actors[1].Nodes, absent2 = s.View.Actors[2].Nodes;
        Assert.Equal(absent, absent2);
        Assert.NotEqual(absent, present);
        Assert.NotEqual(present, absent);
    }

    [Fact]
    public void ChangeBetweenTicks_TheNextPublicationCarriesTheNewActor_AlsoOnTheF7aPath()
    {
        // The daily probe publishes every day; mid-month such a tick takes the F7a path (the actors of the last view are handed out again).
        SliceSession s = Make(ViewScope.Subscribed, probe: true);
        s.SetViewSubscription([0]);
        s.RebuildView();
        s.StepThroughMonth(2);
        s.Step(2);
        ImmutableActors before = new(s.View);
        s.Step(1);
        Assert.True(before.SameActors(s.View), "the days after the month must take the F7a path, or the test proves nothing");
        s.SetViewSubscription([2]);
        s.Step(1);
        Assert.Equal([2], Subscribed(s.View));
        Assert.False(s.World.NodeBlocks.TryGet(0, out _));
    }

    private readonly record struct ImmutableActors(SliceView View)
    {
        public bool SameActors(SliceView other) => View.Actors.Equals(other.Actors);
    }

    [Fact]
    public void RebuildOnPause_GivesTheSameTickHashAndMonth_AndDoesNotTouchTheState()
    {
        SliceSession s = Make(ViewScope.Subscribed);
        s.SetViewSubscription([0]);
        s.RebuildView();
        s.StepThroughMonth(3);
        SliceView before = s.View;
        string hash = s.LastHash;
        long tick = s.Tick;
        s.SetViewSubscription([4]);
        Assert.Same(before, s.View);                 // nothing is published before the rebuild
        s.RebuildView();
        SliceView after = s.View;
        Assert.NotSame(before, after);
        Assert.Equal([4], Subscribed(after));
        Assert.Equal((before.Tick, before.Date, before.Month), (after.Tick, after.Date, after.Month));
        HashAssert.Equal(before.StateHash, after.StateHash);
        Assert.Same(before.Inputs, after.Inputs);
        Assert.Same(before.Step, after.Step);
        Assert.Equal(tick, s.Tick);
        HashAssert.Equal(hash, s.LastHash);
    }

    [Fact]
    public void SubscribedActors_HaveTheViewOfScopeAll_EveryDay_WithCommands()
    {
        SliceSession all = Make(ViewScope.All, probe: true), sub = Make(ViewScope.Subscribed, probe: true);
        sub.SetViewSubscription([1, 2]);
        sub.RebuildView();
        for (int day = 0; day < 75; day++)
        {
            if (day % 20 == 5)
            {
                foreach (SliceSession s in new[] { all, sub })
                    Assert.True(s.Submit(SourceClass.Player, 0, day, new SetEffortFocus(1, [47])).Accepted);
            }
            if (day == 40)
            {
                sub.SetViewSubscription([2, 3]);
                sub.RebuildView();
            }
            all.Step(1);
            sub.Step(1);
            int[] on = day < 40 ? [1, 2] : [2, 3];
            Assert.Equal(on, Subscribed(sub.View));
            Assert.Equal(all.View.Tick, sub.View.Tick);
            for (int a = 0; a < all.View.Actors.Length; a++)
            {
                Assert.Equal(DumpWithout(all.View.Actors[a]), DumpWithout(sub.View.Actors[a]));
                if (sub.View.Actors[a].Nodes.IsPresent)
                    Assert.Equal(ViewSupport.Dump(all.View.Actors[a].Nodes), ViewSupport.Dump(sub.View.Actors[a].Nodes));
            }
        }
    }

    [Fact]
    public void StateHash_EveryDay_DoesNotDependOnScopeSubscriptionOrSkipViews()
    {
        SliceSession all = Make(ViewScope.All, everyTick: true);
        SliceSession sub = Make(ViewScope.Subscribed, everyTick: true);
        SliceSession skip = Make(ViewScope.Subscribed, everyTick: true);
        skip.SkipViews = true;
        HashAssert.Equal(all.LastHash, sub.LastHash);
        for (int day = 0; day < 100; day++)
        {
            if (day % 13 == 0)
            {
                sub.SetViewSubscription([day % 5, (day + 2) % 5]);
                if (day % 26 == 0)
                    sub.RebuildView();
            }
            foreach (SliceSession s in new[] { all, sub, skip })
                s.Step(1);
            HashAssert.Equal(all.LastHash, sub.LastHash);
            HashAssert.Equal(all.LastHash, skip.LastHash);
        }
    }

    [Fact]
    public void LoadedSnapshot_WithTheSameSubscription_ContinuesAsTheUninterruptedRun()
    {
        SliceSession first = Make(ViewScope.Subscribed);
        first.SetViewSubscription([1]);
        first.StepThroughMonth(2);
        using var m = new MemoryStream();
        first.WriteSnapshot(m);

        SliceSession loaded = Make(ViewScope.Subscribed);
        loaded.SetViewSubscription([1]);                 // after a load the client subscribes before the first view
        loaded.LoadSnapshot(new MemoryStream(m.ToArray()));
        Assert.Equal([1], Subscribed(loaded.View));
        for (int i = 0; i < 70; i++)
        {
            first.Step(1);
            loaded.Step(1);
        }
        first.StepThroughMonth(5);
        loaded.StepThroughMonth(5);
        Assert.Equal(first.Tick, loaded.Tick);
        Assert.Equal(ViewSupport.Dump(first.View), ViewSupport.Dump(loaded.View));
    }

    [Fact]
    public void SnapshotBytes_GrowWithTheSubscribedActorsOnly()
    {
        SliceSession s = Make(ViewScope.All);
        s.StepThroughMonth(3);
        SliceWorld w = s.World;
        w.ReuseNodeBlocks = false;
        int n = s.View.Actors.Length;
        long Bytes(bool[]? sub)
        {
            ViewBuilder.Snapshot(w, s.Tick, null, 3, null, null, sub);   // warm up
            long before = GC.GetAllocatedBytesForCurrentThread();
            ViewBuilder.Snapshot(w, s.Tick, null, 3, null, null, sub);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        bool[] Set(params int[] on)
        {
            var b = new bool[n];
            foreach (int a in on)
                b[a] = true;
            return b;
        }
        long none = Bytes(Set()), one = Bytes(Set(0)), two = Bytes(Set(0, 1)), everyone = Bytes(null);
        long block = one - none;
        Assert.InRange(block, 1, long.MaxValue);
        Assert.InRange(two - none, block * 2 - block / 20, block * 2 + block / 20);
        Assert.InRange(everyone - none, block * n - block * n / 20, block * n + block * n / 20);
        Assert.True(two < everyone);
    }

    [Fact]
    public void GameLogicAndAi_DoNotReadViews()
    {
        // ADR-0025 п. 10: views are an observation. Core, Kernel and the adapters that feed the systems name no view type.
        string root = SliceTestData.RepoRoot;
        string[] roots = [Path.Combine(root, "src", "Singularity.Core"), Path.Combine(root, "src", "Singularity.Kernel"), Path.Combine(root, "src", "Singularity.Session", "Slice"), Path.Combine(root, "src", "Singularity.Session", "Arms")];
        string[] words = ["SliceView", "ActorView", "NodeList", "NodeView", "ViewBuilder", "MonthInputsView"];
        var offenders = new List<string>();
        foreach (string file in roots.SelectMany(r => Directory.EnumerateFiles(r, "*.cs", SearchOption.AllDirectories)).Order(StringComparer.Ordinal))
        {
            if (Path.GetFileName(file).StartsWith("SliceRunConfig", StringComparison.Ordinal))
                continue;   // the run options name ViewMode and ViewScope; they are not logic
            foreach (string line in File.ReadLines(file))
            {
                string code = line.TrimStart();
                if (!code.StartsWith("//", StringComparison.Ordinal) && words.Any(w => code.Contains(w, StringComparison.Ordinal)))
                    offenders.Add($"{Path.GetFileName(file)}: {code}");
            }
        }
        Assert.Empty(offenders);
    }

    [Fact]
    public void RebuildAfterUnpublishedTicks_IsBuiltFromTheStateNow_NotLabelledWithTheOldTick()
    {
        SliceSession s = Make(ViewScope.Subscribed);
        s.StepThroughMonth(3);
        long published = s.View.Tick;
        s.Step(2);                                   // mid-month days without commands publish nothing
        Assert.Equal(published, s.View.Tick);
        s.SetViewSubscription([1]);
        s.RebuildView();
        Assert.Equal(s.Tick, s.View.Tick);
        Assert.Null(s.View.StateHash);
        Assert.NotNull(s.View.Inputs);               // the budget panel keeps what the last monthly step read; the record carries its own month
        Assert.Equal(s.View.Month, s.View.Inputs!.Month);
        Assert.Null(s.View.Step);
        Assert.True(s.View.Actors[1].Nodes.IsPresent);
    }

    [Fact]
    public void ClientReaders_OtherThanTheTreeAndTheAudio_DoNotReadTheNodesOfAnActor()
    {
        // ADR-0025 открытый вопрос: ActorPanel, SignalField and the rest never read ActorView.Nodes; only TechTree (shown actor) and AudioDirector (played actor) do.
        string dir = Path.Combine(SliceTestData.RepoRoot, "src", "Singularity.Client.Spike");
        var offenders = new List<string>();
        foreach (string f in Directory.EnumerateFiles(dir, "*.cs").Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(f);
            if (name is "TechTree.cs" or "AudioDirector.cs")
                continue;
            foreach (string line in File.ReadLines(f))
            {
                string code = line.TrimStart();
                if (!code.StartsWith("//", StringComparison.Ordinal) && code.Contains("Actors[", StringComparison.Ordinal) && code.Contains(".Nodes", StringComparison.Ordinal))
                    offenders.Add($"{name}: {code}");
            }
        }
        Assert.Empty(offenders);
    }

    [Fact]
    public void NodeListEquality_AnAbsentListIsNotAnEmptyPresentOne()
    {
        NodeList empty = NodeList.Create([]);
        Assert.True(empty.IsPresent);
        Assert.False(default(NodeList).Equals(empty));
        Assert.False(empty.Equals(default(NodeList)));
        Assert.True(default(NodeList) != empty);
        Assert.NotEqual(default(NodeList).GetHashCode(), empty.GetHashCode());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void L2_SubscribedHashes_EqualScopeAll_AcrossThreadsAndChunkSizes(bool everyTick)
    {
        // The matrix of HashScheduleTests, for Subscribed views with a subscription that changes: the hash of every day equals that of ViewScope.All.
        List<string?> Days(SliceRunConfig config, bool changeSubscription)
        {
            SliceSession s = SliceTestData.Create(config);
            var hashes = new List<string?>();
            if (changeSubscription)
            {
                s.SetViewSubscription([1]);
                s.RebuildView();
            }
            for (int day = 0; day < 65; day++)
            {
                if (changeSubscription && day == 30)
                {
                    s.SetViewSubscription([2, 4]);
                    s.RebuildView();
                }
                s.Step(1);
                hashes.Add(s.View.StateHash);
            }
            hashes.Add(s.LastHash);
            return hashes;
        }

        SliceRunConfig Cfg(ViewScope scope, int threads, int chunk) =>
            new() { Seed = 7, Threads = threads, ChunkSize = chunk, HashEveryTick = everyTick, DailyTickProbe = everyTick, ViewScope = scope };

        List<string?> reference = Days(Cfg(ViewScope.All, 1, 1), false);
        foreach (int threads in new[] { 1, 4, 8, 16 })
        {
            foreach (int chunk in new[] { 1, 8, 32 })
            {
                List<string?> got = Days(Cfg(ViewScope.Subscribed, threads, chunk), true);
                Assert.Equal(reference.Count, got.Count);
                for (int i = 0; i < got.Count; i++)
                {
                    if (reference[i] is null && got[i] is null)
                        continue;   // a day that takes no hash in this mode (HashCheckpoints only); the last entry and every hashed day are compared
                    HashAssert.Equal(reference[i], got[i], $"threads {threads}, chunk {chunk}, index {i}");
                }
            }
        }
        Assert.Contains(reference, h => h is not null);
    }

    // The body of the method whose signature starts at `start`: from the signature to the brace that closes it (counted; strings and comments are not special-cased,
    // the scanned methods have no braces in them), or to the end of the line for an expression-bodied method.
    private static string MethodBody(string text, int start)
    {
        int open = text.IndexOf('{', start), arrow = text.IndexOf("=>", start, StringComparison.Ordinal);
        if (arrow >= 0 && (open < 0 || arrow < open))
            return text[start..text.IndexOf(";\n", arrow, StringComparison.Ordinal)];
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '{')
                depth++;
            else if (text[i] == '}' && --depth == 0)
                return text[start..(i + 1)];
        }
        throw new InvalidOperationException("unbalanced braces");
    }

    [Fact]
    public void MethodBodyScanner_SeesPastABlankLine_AndStopsAtTheClosingBrace()
    {
        const string text = "void A()\n    {\n        if (x)\n        {\n            F();\n        }\n\n        _ = _view;\n    }\n\n    void B()\n    {\n        G();\n    }\n";
        string a = MethodBody(text, 0);
        Assert.Contains("_view", a);
        Assert.DoesNotContain("G()", a);
        Assert.Equal("void B()\n    {\n        G();\n    }", MethodBody(text, text.IndexOf("void B", StringComparison.Ordinal)));
        Assert.Equal("int C() => 1", MethodBody("int C() => 1;\nint D() => 2;\n", 0));
    }

    [Fact]
    public void CommandLogic_OfTheSession_DoesNotReadViews()
    {
        // ADR-0025 п. 10 for SliceSession.cs: Submit, the application of commands and their bookkeeping name no view; only the publication code does.
        string text = File.ReadAllText(Path.Combine(SliceTestData.RepoRoot, "src", "Singularity.Session", "SliceSession.cs")).ReplaceLineEndings("\n");
        string[] methods = ["public CommandVerdict Submit(", "private bool ApplyPending(", "private void ReconcileOutcomes(", "private void MarkStale(", "private static DesignHandle? HandleOf(", "private bool KnowsProcess("];
        string[] words = ["_view", "SliceView", "ActorView", "NodeList", "NodeView", "ViewBuilder", "MonthInputsView", ".View", "_actorsMid", "_subscribed"];
        foreach (string signature in methods)
        {
            int start = text.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, $"method not found: {signature}");
            string body = MethodBody(text, start);
            Assert.True(body.Length > 80, $"scan of {signature} is vacuous");
            string code = string.Join('\n', body.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
            Assert.Empty(words.Where(w => code.Contains(w, StringComparison.Ordinal)).Select(w => $"{signature} names {w}"));
        }
    }
}
