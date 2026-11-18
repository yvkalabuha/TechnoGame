using System.Collections.Immutable;
using System.Reflection;
using Singularity.Core.Commands;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Policy;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>
/// TASK-0103 step 2 (test review): memo (F4) and node-block reuse (R1/R2) are invisible. L2 over threads x chunk sizes with the switches ON and OFF
/// across monthly and yearly ticks with commands; every published view with reuse equals a full rebuild across Load, FullCopyMode and command-only
/// ticks; the memo never serves a value that differs from a fresh compute after any column write.
/// </summary>
public class MemoReuseDeterminismTests
{
    private static BudgetShares Shares(long r, long c, long d, long p, long q) =>
        new(Fx6.FromRaw(r), Fx6.FromRaw(c), Fx6.FromRaw(d), Fx6.FromRaw(p), Fx6.FromRaw(q));

    private static readonly BudgetShares[] ShareSets =
    [
        Shares(200_000, 200_000, 200_000, 300_000, 100_000),
        Shares(500_000, 100_000, 100_000, 200_000, 100_000),
        Shares(100_000, 100_000, 500_000, 200_000, 100_000),
    ];

    private static void Commands(SliceSession s, long m, ref int local, ref int player)
    {
        for (int a = 0; a < 5; a++)
            Assert.True(s.Submit(SourceClass.Harness, 0, local++, new SetBudgetShares(a, ShareSets[(int)((m + a) % ShareSets.Length)])).Accepted);
        if (m % 3 == 0)
            Assert.True(s.Submit(SourceClass.Player, 0, player++, new SetEffortFocus(1, [47])).Accepted);
    }

    // Scalar (non-collection) properties of the view of an actor plus its nodes: what a reader sees, compared by value.
    private static string Signature(ActorView v)
    {
        var sb = new System.Text.StringBuilder();
        foreach (PropertyInfo p in typeof(ActorView).GetProperties())
        {
            if (p.Name == nameof(ActorView.Nodes) || p.PropertyType.IsArray || (p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(ImmutableArray<>)))
                continue;
            if (p.PropertyType.IsValueType || p.PropertyType == typeof(string))
                sb.Append(p.Name).Append('=').Append(p.GetValue(v)).Append(';');
        }
        return sb.ToString();
    }

    private static void AssertViewsEqual(SliceSession a, SliceSession b, string where)
    {
        Assert.Equal(a.View.Actors.Length, b.View.Actors.Length);
        for (int i = 0; i < a.View.Actors.Length; i++)
        {
            Assert.True(a.View.Actors[i].Nodes.Equals(b.View.Actors[i].Nodes), $"{where}: nodes of actor {i}");
            Assert.Equal(Signature(b.View.Actors[i]), Signature(a.View.Actors[i]));
        }
    }

    [Fact]
    public void L2_ThreadsAndChunks_MemoAndReuseOnVsOff_SameHashesAcrossMonthlyAndYearlyTicks()
    {
        (int T, int C)[] cfg = [(1, 1), (2, 8), (4, 32), (8, 1), (16, 8), (16, 32)];
        var runs = new List<(SliceSession s, bool on, int local, int player)>();
        foreach ((int t, int c) in cfg)
        {
            foreach (bool on in new[] { true, false })
            {
                SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 3, Threads = t, ChunkSize = c });
                s.World.MemoizeInputs = on;
                s.World.ReuseNodeBlocks = on;
                runs.Add((s, on, 0, 0));
            }
        }
        for (long m = 0; m < 26; m++)
        {
            for (int i = 0; i < runs.Count; i++)
            {
                var r = runs[i];
                Commands(r.s, m, ref r.local, ref r.player);
                runs[i] = r;
                r.s.StepThroughMonth(m);
            }
            string reference = runs[0].s.LastHash;
            for (int i = 1; i < runs.Count; i++)
            {
                HashAssert.Equal(reference, runs[i].s.LastHash, $"month {m} run {i}");
                AssertViewsEqual(runs[0].s, runs[i].s, $"month {m} run {i}");
            }
        }
    }

    [Fact]
    public void Views_WithReuse_EqualFullRebuild_AcrossFullCopyMode_CommandOnlyTicks_AndLoad()
    {
        SliceSession reuse = SliceTestData.Create(new SliceRunConfig { Seed = 5 });
        SliceSession fresh = SliceTestData.Create(new SliceRunConfig { Seed = 5 });
        fresh.World.ReuseNodeBlocks = false;
        reuse.World.Table.FullCopyMode = true;      // reuse run in full-copy mode: versions bump for every column, blocks must still equal
        int local = 0, player = 0, local2 = 0, player2 = 0;
        for (long m = 0; m < 8; m++)
        {
            Commands(reuse, m, ref local, ref player);
            Commands(fresh, m, ref local2, ref player2);
            long end = reuse.World.Calendar.TickOfMonth(m) + 4;
            while (reuse.Tick < end)
            {
                reuse.Step(1);
                fresh.Step(1);
                AssertViewsEqual(reuse, fresh, $"tick {reuse.Tick}");
            }
            if (m == 3)
                reuse.World.Table.FullCopyMode = false;
        }
        // Rewind (save/load path): restore columns of an earlier tick into the reuse session and into a fresh-rebuild twin.
        SliceSession a = SliceTestData.Create(new SliceRunConfig { Seed = 5 });
        SliceSession b = SliceTestData.Create(new SliceRunConfig { Seed = 5 });
        b.World.ReuseNodeBlocks = false;
        a.StepThroughMonth(3); b.StepThroughMonth(3);
        var longs = new Dictionary<string, long[]>();
        var ints = new Dictionary<string, int[]>();
        foreach (Column c in a.World.Table.Columns)
        {
            if (c is LongColumn l) longs[c.Name] = l.Current.ToArray();
            else if (c is IntColumn n) ints[c.Name] = n.Current.ToArray();
        }
        _ = a.View; _ = b.View;
        for (long m = 4; m < 7; m++) { a.StepThroughMonth(m); b.StepThroughMonth(m); }
        foreach (SliceSession s in new[] { a, b })
        {
            foreach (var kv in longs) s.World.Table.GetLong(kv.Key).Load(kv.Value);
            foreach (var kv in ints) s.World.Table.GetInt(kv.Key).Load(kv.Value);
        }
        // The tick cannot move back: snapshot the loaded (rewound) state directly, twice, then step on.
        for (int k = 0; k < 2; k++)
        {
            var va = ViewBuilder.Snapshot(a.World, a.Tick, null, 6, null, null);
            var vb = ViewBuilder.Snapshot(b.World, b.Tick, null, 6, null, null);
            for (int i = 0; i < va.Actors.Length; i++)
                Assert.True(va.Actors[i].Nodes.Equals(vb.Actors[i].Nodes), $"loaded state, actor {i}");
        }
        a.StepThroughMonth(7); b.StepThroughMonth(7);
        AssertViewsEqual(a, b, "after load and step");
        HashAssert.Equal(b.LastHash, a.LastHash, "after load");
    }

    private static string Outcome(Func<string> f)
    {
        try { return f(); } catch (Exception e) { return "EX:" + e.GetType().Name; }
    }

    private static string Probe(SliceSession s, long month)
    {
        SliceWorld w = s.World;
        var sb = new System.Text.StringBuilder();
        for (int a = 0; a < w.Scenario.ActorCount; a++)
        {
            Fx6 rest = w.Industry.RestActor(a, month);
            ToolingPlanResult plan = w.IndustryInputs.Plan(a, month, rest);
            sb.Append(rest.Raw).Append('|').Append(plan.TrainingBudget.Raw);
            foreach (int site in w.IndustryLayout.SitesOf(a))
            {
                for (int c = 0; c < w.IndustryLayout.Clusters.Length; c++)
                {
                    foreach (Dimension d in Enum.GetValues<Dimension>())
                        sb.Append(',').Append(plan.Applied(site, c, d).Raw).Append('/').Append(plan.Unmet(site, c, d).Raw);
                }
            }
            sb.Append(';');
        }
        return sb.ToString();
    }

    [Fact]
    public void Memo_NeverServesAStaleValue_AfterAnyColumnWrite()
    {
        SliceSession memo = SliceTestData.Create(new SliceRunConfig { Seed = 2 });
        SliceSession oracle = SliceTestData.Create(new SliceRunConfig { Seed = 2 });
        oracle.World.MemoizeInputs = false;
        memo.StepThroughMonth(5); oracle.StepThroughMonth(5);
        long month = 6;
        int differing = 0, trials = 0;
        var rng = new Random(12345);
        foreach (Column c in memo.World.Table.Columns)
        {
            if (c.Length == 0) continue;
            // Fill the memo of the month for every actor on the current snapshot (what the view build does), then write the column.
            for (int a = 0; a < memo.World.Scenario.ActorCount; a++)
            {
                memo.World.Industry.MemoizeActor(a, month);
                memo.World.IndustryInputs.Memoize(a, month, memo.World.Industry.RestActor(a, month));
            }
            string baseline = Outcome(() => Probe(oracle, month));
            Assert.Equal(baseline, Outcome(() => Probe(memo, month)));
            int idx = rng.Next(c.Length);
            if (c is LongColumn l)
            {
                long[] orig = l.Current.ToArray(), mod = (long[])orig.Clone();
                mod[idx] += 1_000_000 + rng.Next(5_000_000);
                foreach (var col in new[] { memo.World.Table.GetLong(c.Name), oracle.World.Table.GetLong(c.Name) }) col.Load(mod);
                string expected = Outcome(() => Probe(oracle, month)), got = Outcome(() => Probe(memo, month));
                Assert.True(expected == got, $"stale memo after write of {c.Name}[{idx}]");
                if (expected != baseline) differing++;
                memo.World.Table.GetLong(c.Name).Load(orig); oracle.World.Table.GetLong(c.Name).Load(orig);
            }
            else if (c is IntColumn n)
            {
                int[] orig = n.Current.ToArray(), mod = (int[])orig.Clone();
                mod[idx] += 1 + rng.Next(3);
                foreach (var col in new[] { memo.World.Table.GetInt(c.Name), oracle.World.Table.GetInt(c.Name) }) col.Load(mod);
                string expected = Outcome(() => Probe(oracle, month)), got = Outcome(() => Probe(memo, month));
                Assert.True(expected == got, $"stale memo after write of {c.Name}[{idx}]");
                if (expected != baseline) differing++;
                memo.World.Table.GetInt(c.Name).Load(orig); oracle.World.Table.GetInt(c.Name).Load(orig);
            }
            else continue;
            trials++;
        }
        Assert.True(trials > 50);
        Assert.True(differing > 0, "no column write changed the allocation or the plan: the property is vacuous");
    }
}
