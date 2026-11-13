using Singularity.Kernel;
using Xunit.Abstractions;

namespace Singularity.Session.Tests;

/// <summary>
/// P0 sparsity test (layer contract §8 item 1; spec §6.4, RR-0006 §4.14 «двойной счёт обучения»): one stock of the §3 table is shifted by Δ in
/// a copy of the world, one monthly compute runs in both, and every column that changed must be an output the table allows for that stock
/// («влияет на и только на», rules 1, 3, 4): a WHITE list per stock (anything else fails) and a POSITIVE check (the shift is not a no-op:
/// at least one allowed output of the stock's own layer moves). The horizon is one month: effects through next-month mailboxes
/// (reports merged at t+1) are outside the check by construction. A test whose stock shows no effect in a month would be marked weak (the
/// parameter of <c>Check</c>); at present none is.
/// Column families: know.* science (K_d, K_ind, prog, known), ind.* capability (H, M, X, y, v, E, wf), prd.* products, pol./cmd./run. policy.
/// </summary>
public class SparsityTests(ITestOutputHelper output)
{
    private const int WarmMonths = 14;
    private static readonly string[] Know = ["know."];
    private static readonly string[] Ind = ["ind."];
    private static readonly string[] Prd = ["prd."];

    private static SliceSession Warm(ulong seed)
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = seed });
        for (long t = 0; t < WarmMonths; t++)
            s.StepThroughMonth(t);
        return s;
    }

    private static long[] Values(Column c) => c switch
    {
        LongColumn l => [.. l.Current],
        IntColumn i => [.. i.Current.ToArray().Select(v => (long)v)],
        _ => throw new NotSupportedException($"column {c.Name}: unknown column type {c.GetType().Name}"),
    };

    private static Dictionary<string, long[]> Snapshot(SliceSession s) =>
        s.World.Table.Columns.ToDictionary(c => c.Name, Values, StringComparer.Ordinal);

    private static void Shift(SliceSession s, string column, int row, long delta)
    {
        switch (s.World.Table.Columns.First(x => x.Name == column))
        {
            case LongColumn l:
                long[] a = l.Current.ToArray();
                a[row] += delta;
                l.Load(a);
                break;
            case IntColumn i:
                int[] b = i.Current.ToArray();
                b[row] += (int)delta;
                i.Load(b);
                break;
            default:
                throw new NotSupportedException($"column {column}: unknown column type");
        }
    }

    private static bool Starts(string name, IEnumerable<string> prefixes) => prefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal));

    private HashSet<string> Changed(ulong seed, string column, int row, long delta, string? alsoShifted = null)
    {
        SliceSession a = Warm(seed), b = Warm(seed);
        Assert.Equal(a.LastHash, b.LastHash);
        Shift(b, column, row, delta);
        if (alsoShifted is not null)
            Shift(b, alsoShifted, row, delta);   // keeps the product invariant D0 = hidden + found + open
        a.StepThroughMonth(WarmMonths);
        b.StepThroughMonth(WarmMonths);
        var sa = Snapshot(a);
        var sb = Snapshot(b);
        HashSet<string> changed = [.. sa.Where(kv => !kv.Value.AsSpan().SequenceEqual(sb[kv.Key])).Select(kv => kv.Key)];
        changed.Remove(column);
        if (alsoShifted is not null)
            changed.Remove(alsoShifted);
        output.WriteLine($"seed {seed}: shift {column}[{row}] by {delta}: changed {string.Join(", ", changed.OrderBy(x => x, StringComparer.Ordinal))}");
        return changed;
    }

    private static IEnumerable<string> Columns(SliceSession s, string prefix, Func<string, bool>? filter = null) =>
        s.World.Table.Columns.Where(c => c.Name.StartsWith(prefix, StringComparison.Ordinal) && (filter is null || filter(c.Name))).Select(c => c.Name);

    /// <summary>
    /// Shifts each candidate stock (row 0 = USA) in turn. Every candidate: all changed columns are on the white list. At least one candidate:
    /// a column of <paramref name="positive"/> changes (unless <paramref name="weak"/>: documented, then only the white list is checked).
    /// </summary>
    private void Check(ulong seed, IEnumerable<(string Column, long Delta, string? Twin)> candidates, string[] allowed, string[] positive, bool weak = false)
    {
        bool seen = false;
        int n = 0;
        foreach ((string column, long delta, string? twin) in candidates)
        {
            n++;
            HashSet<string> ch = Changed(seed, column, 0, delta, twin);
            Assert.All(ch, name => Assert.True(Starts(name, allowed), $"shift of {column}: foreign output {name} changed (allowed: {string.Join(' ', allowed)})"));
            seen |= ch.Any(name => Starts(name, positive));
        }
        Assert.True(n > 0);
        if (!weak)
            Assert.True(seen, "no allowed output of the stock's own layer moved: the shift is a no-op in this month (weak check)");
    }

    [Theory]
    [InlineData(7UL)]
    [InlineData(11UL)]
    public void ScienceStock_Kd_MovesOnlyScienceColumns(ulong seed)
    {
        // Rule 1: K_d only from science; it must not reach H, M, X, y or the line experience in the same month. Allowed: know.* only.
        SliceSession probe = Warm(seed);
        Check(seed, Columns(probe, "know.K_d.").Take(1).Select(c => (c, Fx6.FromInt(1).Raw, (string?)null)), Know, Know);
    }

    [Theory]
    [InlineData(7UL)]
    [InlineData(11UL)]
    public void ScienceStock_KInd_MovesOnlyScienceColumns(ulong seed)
    {
        SliceSession probe = Warm(seed);
        Check(seed, Columns(probe, "know.K_ind.").Take(1).Select(c => (c, Fx6.FromInt(1).Raw, (string?)null)), Know, Know);
    }

    [Theory]
    [InlineData(7UL)]
    [InlineData(11UL)]
    public void CultureH_MovesOnlyCapabilityAndProductColumns_NeverScience(ulong seed)
    {
        // Rules 1 and 5: culture_H is not an input of K_d / K_ind / progress (the bridge is K_ind via output, next month). Own layer: ind.H spill.
        SliceSession probe = Warm(seed);
        Check(seed, Columns(probe, "ind.H.", n => !n.StartsWith("ind.H.unmet", StringComparison.Ordinal)).Take(1).Select(c => (c, Fx6.FromRaw(500_000).Raw, (string?)null)),
            [.. Ind, .. Prd], ["ind.H."]);
    }

    [Theory]
    [InlineData(7UL)]
    [InlineData(11UL)]
    public void ToolingM_MovesOnlyCapabilityAndProductColumns_NeverScience(ulong seed)
    {
        // A cut of M below the requirement makes c_d = min(H, M) fall: the yield column of the own layer moves. A raise is a no-op while M ≥ H.
        SliceSession probe = Warm(seed);
        Check(seed, Columns(probe, "ind.M.", n => !n.StartsWith("ind.M.unmet", StringComparison.Ordinal)).Select(c => (c, -Fx6.FromInt(9).Raw, (string?)null)),
            [.. Ind, .. Prd], ["ind."]);
    }

    [Theory]
    [InlineData(7UL)]
    [InlineData(11UL)]
    public void ProcX_MovesOnlyCapabilityAndProductColumns_NeverScience(ulong seed)
    {
        SliceSession probe = Warm(seed);
        Check(seed, Columns(probe, "ind.X.").Take(3).Select(c => (c, Fx6.FromInt(1).Raw, (string?)null)), [.. Ind, .. Prd], ["ind.y."]);
    }

    [Theory]
    [InlineData(7UL)]
    [InlineData(11UL)]
    public void LineE_MovesAssemblyCostAndOutputVolume_NeverYieldCultureOrScience(ulong seed)
    {
        // Rule 3: line_E acts only on assembly_hours. Through the cost it can change the affordable output (prd.*) and, as volume, proc_X / v / mr of
        // the processes of the design (rule 5: one volume moves several stocks). It must not change culture H, tooling M, the yield column or science.
        SliceSession probe = Warm(seed);
        Check(seed, Columns(probe, "prd.", n => n.EndsWith(".lineE", StringComparison.Ordinal)).Take(2).Select(c => (c, Fx6.FromInt(500).Raw, (string?)null)),
            [.. Prd, "ind.wf.", "ind.X.", "ind.v.", "ind.mr."], Prd);
    }

    [Theory]
    [InlineData(7UL)]
    [InlineData(11UL)]
    public void HiddenDefects_MoveOnlyProductColumnsAndTheirWorkforceRequest(ulong seed)
    {
        // Rule 4: hidden defects are a separate cause. They enter test hours, hence the engineers' request (ind.wf.*), never yield or culture.
        SliceSession probe = Warm(seed);
        string hidden = Columns(probe, "prd.", n => n.EndsWith(".hidden", StringComparison.Ordinal)).First();
        string twin = hidden[..^".hidden".Length] + ".d0";   // hidden defects move with D0 (invariant of the layer)
        Check(seed, [(hidden, Fx6.FromRaw(100_000).Raw, twin)], [.. Prd, "ind.wf."], Prd);
    }
}
