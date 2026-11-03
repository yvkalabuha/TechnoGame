using System.Text.Json;
using Singularity.Core.Knowledge;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Knowledge;

/// <summary>
/// Knowledge part of lab/reference/jet_slice/out/trajectory.json, bit for bit (TASK-0014, reference D4, D8, D9):
/// the C# run in daily ticks gives the reference month steps at day ticks d(t).
/// </summary>
public class KnowledgeReferenceTrajectoryTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 1)]
    [InlineData(8, 3)]
    public void DailyRun_MatchesReference_EveryMonth(int threads, int chunkSize)
    {
        var run = new JetReference.Run(threads, chunkSize);
        for (long t = 0; t < JetReference.RowCount; t++)
        {
            run.RunThroughMonth(t);
            JsonElement row = JetReference.Row(t);
            Assert.Equal(row.GetProperty("day_tick").GetInt64(), run.Calendar.TickOfMonth(t));
            AssertMonth(run, actorRow: 0, row, t);
        }
    }

    [Fact]
    public void IdleDays_ChangeNothing()
    {
        // 706 of the 730 days run no system: the table must be identical before and after them.
        var run = new JetReference.Run();
        int idle = 0;
        string before = StateHash.ComputeHex(run.Table, 1, 0);
        for (long d = 0; d < run.Calendar.TickOfMonth(JetReference.RowCount); d++)
        {
            bool due = run.Calendar.IsDue(Cadence.Monthly, d);
            run.Runner.RunTick();
            string after = StateHash.ComputeHex(run.Table, 1, 0); // tick fixed: the table alone is compared
            if (!due)
            {
                Assert.Equal(before, after);
                idle++;
            }
            before = after;
        }
        Assert.Equal(JetReference.Trajectory.GetProperty("calendar").GetProperty("idle_days").GetInt32(), idle);
    }

    [Fact]
    public void PermutedNodeStorage_StillMatchesReference()
    {
        var run = new JetReference.Run(nodeOrder: [4, 2, 5, 0, 3, 1]);
        for (long t = 0; t < JetReference.RowCount; t++)
        {
            run.RunThroughMonth(t);
            AssertMonth(run, actorRow: 0, JetReference.Row(t), t);
        }
    }

    [Fact]
    public void PermutedActorAndNodeStorage_SameResultsByStableIds()
    {
        // RNG keys use actor and node numbers, not storage rows: three interacting actors (known_elsewhere)
        // stored in two different orders give identical values and draws for every (actor, node).
        var a = new JetReference.Run(actorNumbers: [0, 1, 3], threads: 4, chunkSize: 1);
        var b = new JetReference.Run(nodeOrder: [5, 3, 1, 0, 4, 2], actorNumbers: [3, 0, 1], threads: 1, chunkSize: 2);
        bool sawHazardDraw = false, sawKnownElsewhereEffect = false;
        for (long t = 0; t < JetReference.RowCount; t++)
        {
            a.RunThroughMonth(t);
            b.RunThroughMonth(t);
            Assert.Equal(a.System.LastDiscoveries, b.System.LastDiscoveries);
            for (int ra = 0; ra < 3; ra++)
            {
                int rb = Array.IndexOf(b.Start.ActorNumber, a.Start.ActorNumber[ra]);
                Assert.Equal(a.System.LastSpent(ra), b.System.LastSpent(rb));
                for (int d = 0; d < 2; d++)
                {
                    Assert.Equal(a.System.KD(ra, d), b.System.KD(rb, d));
                    Assert.Equal(a.System.KInd(ra, d), b.System.KInd(rb, d));
                }
                for (int na = 0; na < a.Graph.Count; na++)
                {
                    int nb = b.Graph.RowOf(a.Graph.Number[na]);
                    Assert.Equal(a.System.IsKnown(ra, na), b.System.IsKnown(rb, nb));
                    Assert.Equal(a.System.Prog(ra, na), b.System.Prog(rb, nb));
                    KnowledgeNodeStep sa = a.System.LastStep(ra, na);
                    Assert.Equal(sa, b.System.LastStep(rb, nb));
                    sawHazardDraw |= sa.HazardActive;
                }
            }
            JsonElement row = JetReference.Row(t);
            foreach (JsonElement k in row.GetProperty("knowledge").EnumerateArray())
            {
                // The same fixture alone (one actor) has c_eff without the k_diff discount.
                int r = a.Graph.RowOf(k.GetProperty("node").GetInt32());
                sawKnownElsewhereEffect |= a.System.LastStep(0, r).InFrontier && a.System.LastStep(0, r).CEff.Raw != k.GetProperty("c_eff").GetInt64();
            }
        }
        Assert.True(sawHazardDraw);
        Assert.True(sawKnownElsewhereEffect);
    }

    internal static void AssertMonth(JetReference.Run run, int actorRow, JsonElement row, long t)
    {
        KnowledgeGraph g = run.Graph;
        KnowledgeSystem s = run.System;
        JsonElement after = row.GetProperty("state_after");

        {
            foreach (JsonElement k in row.GetProperty("knowledge").EnumerateArray())
            {
                int r = g.RowOf(k.GetProperty("node").GetInt32());
                KnowledgeNodeStep step = s.LastStep(actorRow, r);
                Assert.True(step.InFrontier, $"t={t} node {g.Number[r]}");
                Assert.Equal(k.GetProperty("w").GetInt64(), step.W.Raw);
                Assert.Equal(k.GetProperty("alloc").GetInt64(), step.Alloc.Raw);
                Assert.Equal(k.GetProperty("Kmix").GetInt64(), step.Kmix.Raw);
                Assert.Equal(k.GetProperty("c_eff").GetInt64(), step.CEff.Raw);
                Assert.Equal(k.GetProperty("dprog").GetInt64(), step.DProg.Raw);
                Assert.Equal(k.GetProperty("prog_new").GetInt64(), step.ProgNew.Raw);
                if (k.TryGetProperty("hazard", out JsonElement hz))
                {
                    Assert.Equal(hz.GetProperty("h").GetInt64(), step.H.Raw);
                    Assert.Equal(hz.GetProperty("opened").GetBoolean(), step.Opened);
                    JsonElement u = hz.GetProperty("u");
                    Assert.Equal(u.ValueKind != JsonValueKind.Null, step.HazardActive);
                    if (step.HazardActive)
                        Assert.Equal(u.GetUInt64(), step.U);
                }
            }
            Assert.Equal(row.GetProperty("spent_R").GetInt64(), s.LastSpent(actorRow).Raw);
            var expected = row.GetProperty("merge_discoveries").EnumerateArray()
                .Select(d => (d[0].GetInt32(), d[1].GetInt32())).ToList();
            Assert.Equal(expected, s.LastDiscoveries);
        }

        var known = after.GetProperty("known").EnumerateArray().Select(x => x.GetInt32()).ToHashSet();
        for (int r = 0; r < g.Count; r++)
            Assert.True(known.Contains(g.Number[r]) == s.IsKnown(actorRow, r), $"t={t} known {g.Number[r]}");
        foreach (JsonProperty p in after.GetProperty("prog").EnumerateObject())
            Assert.Equal(p.Value.GetInt64(), s.Prog(actorRow, g.RowOf(int.Parse(p.Name, System.Globalization.CultureInfo.InvariantCulture))).Raw);
        for (int d = 0; d < JetReference.Disciplines.Length; d++)
        {
            Assert.Equal(after.GetProperty("K_d").GetProperty(JetReference.Disciplines[d]).GetInt64(), s.KD(actorRow, d).Raw);
            Assert.Equal(after.GetProperty("K_ind").GetProperty(JetReference.Disciplines[d]).GetInt64(), s.KInd(actorRow, d).Raw);
        }
    }
}
