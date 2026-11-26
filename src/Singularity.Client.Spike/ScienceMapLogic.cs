#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Singularity.Core.Commands;
using Singularity.Core.Data;
using Singularity.Kernel;
using Singularity.Session;

namespace Singularity.Client.Spike;

public enum SlotKind { Free, Busy, Building, Unavailable }

/// <summary>One card of the institute strip (spec tech-map-stage1 §3). Numbers are those of the view, only rounded for the eye.</summary>
public readonly record struct StripSlot(int Slot, SlotKind Kind, string Id, int FocusLevel, int Queued, int Active, int ProgressMonths, int MonthsLeft, int BuildDone, int BuildLeft);

/// <summary>A direction tag of a node against one institute: the level is the institute's level in that direction (0 = none).</summary>
public readonly record struct DirPart(string Tag, int Direction, bool Main, int Level);

/// <summary>Overlap of a node with one institute: «2 of 3: PRP 4 · MET 3».</summary>
public readonly record struct OverlapInfo(int Matching, int Total, ImmutableArray<DirPart> Parts);

public enum AssignWhy { None, Known, NoInstitute, NotInFrontier, AlreadyAssigned, QueueFull }

/// <summary>Why «Research in an institute» is off (spec §4.3); <see cref="Arg"/> is a slot (AlreadyAssigned) or a node number (NotInFrontier: the first missing requirement; 0 = unknown).</summary>
public readonly record struct AssignState(AssignWhy Why, int Arg = 0) { public bool Enabled => Why == AssignWhy.None; }

/// <summary>A row of the focus selector: level, months and the extra price over level 0, as the session quotes them.</summary>
public readonly record struct FocusRow(int Level, int Months, double Extra);

/// <summary>Pure rules of the tech map stage 1 (TASK-0192): no Godot, no simulation maths; the session gives the numbers, this only chooses what to show and how.</summary>
public static class ScienceMapLogic
{
    public const int StripSlots = 6;
    public const int QueueMax = 5;
    public const int FocusMax = 2;

    static readonly string[] Tags = { "AER", "PRP", "MET", "MAT", "CHM", "STR", "MFG", "ELE", "SEN", "CTL", "NUC", "ORD", "SPC" };

    /// <summary>Three-letter tag of a direction number (order of <see cref="Directions.Ids"/>); "???" outside it.</summary>
    public static string Tag(int direction) => (uint)direction < (uint)Tags.Length ? Tags[direction] : "???";

    static int Ceil(Fx6 v) => v <= Fx6.Zero ? 0 : (int)((v.Raw + Fx6.Scale - 1) / Fx6.Scale);

    /// <summary>The state of an institute for the strip. <paramref name="month"/> is the month of the view.</summary>
    public static StripSlot Strip(InstituteView v, long month)
    {
        int queued = v.Queue.IsDefault ? 0 : v.Queue.Length;
        if (v.Standing)
        {
            SlotKind kind = v.Active > 0 ? SlotKind.Busy : SlotKind.Free;
            int left = Ceil(v.MonthsLeft);
            int done = v.Active > 0 && v.ActiveProgress > Fx6.Zero && v.MonthsLeft > Fx6.Zero ? Ceil(v.ActiveProgress * v.MonthsLeft / (Fx6.One - v.ActiveProgress)) : 0;
            return new StripSlot(v.Slot, kind, v.Id, v.FocusLevel, queued, v.Active, done, left, 0, 0);
        }
        if (v.BuildStart >= 0)
        {
            int total = v.BuildMonths;
            int done = (int)Math.Clamp(month - v.BuildStart, 0, total);
            return new StripSlot(v.Slot, SlotKind.Building, v.Id, 0, 0, 0, 0, 0, done, total - done);
        }
        return new StripSlot(v.Slot, SlotKind.Unavailable, v.Id, 0, 0, 0, 0, 0, 0, 0);
    }

    /// <summary>The window of cards of a page: the strip shows six, the rest is paged, never squeezed.</summary>
    public static (int First, int Count) Page(int total, int page)
    {
        int pages = Math.Max(1, (total + StripSlots - 1) / StripSlots);
        int p = Math.Clamp(page, 0, pages - 1);
        return (p * StripSlots, Math.Min(StripSlots, Math.Max(0, total - p * StripSlots)));
    }

    public static int PageCount(int total) => Math.Max(1, (total + StripSlots - 1) / StripSlots);

    /// <summary>Directions of a node (main first) against the levels an institute has (<see cref="InstituteView.DirLevels"/>, empty = none).</summary>
    public static OverlapInfo Overlap(NodeTable nodes, int row, ImmutableArray<int> dirLevels)
    {
        int n = nodes.DirectionCount(row), match = 0;
        var parts = ImmutableArray.CreateBuilder<DirPart>(n);
        for (int k = 0; k < n; k++)
        {
            int d = nodes.DirectionAt(row, k);
            int lvl = !dirLevels.IsDefaultOrEmpty && d < dirLevels.Length ? dirLevels[d] : 0;
            if (lvl > 0) match++;
            parts.Add(new DirPart(Tag(d), d, k == 0, lvl));
        }
        return new OverlapInfo(match, n, parts.MoveToImmutable());
    }

    /// <summary>The fit rows ordered for the table: standing institutes first, quickest first, the lower slot on a tie. Returns slot indexes.</summary>
    public static int[] FitOrder(ImmutableArray<InstituteFitView> fit)
    {
        var idx = new List<int>();
        for (int i = 0; i < fit.Length; i++) if (fit[i].Standing) idx.Add(i);
        idx.Sort((a, b) => fit[b].Speed != fit[a].Speed ? fit[b].Speed.CompareTo(fit[a].Speed) : fit[a].Slot.CompareTo(fit[b].Slot));
        var slots = new int[idx.Count];
        for (int i = 0; i < slots.Length; i++) slots[i] = fit[idx[i]].Slot;
        return slots;
    }

    /// <summary>
    /// State of «Research in an institute» for a node. <paramref name="heldBy"/> is the slot whose list holds the node (−1 none); <paramref name="missing"/> the first unmet requirement
    /// (0 = none known). The target is the first of <paramref name="order"/> that can take the node: a free institute, else one whose queue has room.
    /// </summary>
    public static AssignState CanAssign(bool known, bool inFrontier, int missing, int heldBy, ImmutableArray<InstituteView> institutes, int[] order, out int target)
    {
        target = -1;
        if (known) return new AssignState(AssignWhy.Known);
        if (heldBy >= 0) return new AssignState(AssignWhy.AlreadyAssigned, heldBy);
        if (!inFrontier) return new AssignState(AssignWhy.NotInFrontier, missing);
        if (order.Length == 0) return new AssignState(AssignWhy.NoInstitute);
        foreach (int s in order)
            if (institutes[s].Standing && institutes[s].Active == 0) { target = s; return default; }
        foreach (int s in order)
            if (institutes[s].Standing && institutes[s].Queue.Length < QueueMax) { target = s; return default; }
        return new AssignState(AssignWhy.QueueFull);
    }

    /// <summary>The three focus rows of a quote (level, whole months, extra price over «no focus»).</summary>
    public static FocusRow[] FocusRows(ImmutableArray<FocusQuoteView> quote)
    {
        if (quote.IsDefaultOrEmpty) return Array.Empty<FocusRow>();
        var rows = new FocusRow[quote.Length];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = new FocusRow(quote[i].Level, Ceil(quote[i].Months), i == 0 ? 0 : (quote[i].Price - quote[0].Price).ToDouble());
        return rows;
    }

    /// <summary>Military value word key of a node, or null when the data gives none (the line is not drawn).</summary>
    public static string? MilKey(MilValue v) => v switch { MilValue.Direct => "mil.direct", MilValue.Prep => "mil.prep", MilValue.Base => "mil.base", _ => null };

    /// <summary>Summary of a path for the banner: number of nodes, sum of their prices, whole months of the range.</summary>
    public readonly record struct PathSummary(int Nodes, double Price, int MonthsMin, int MonthsMax);

    public static PathSummary Summary(PathView p)
    {
        Fx6 price = Fx6.Zero;
        foreach (var n in p.Nodes) price += n.Price;
        return new PathSummary(p.Nodes.Length, price.ToDouble(), Ceil(p.MonthsMin), Ceil(p.MonthsMax));
    }

    /// <summary>One line of the layout the player is asked to confirm.</summary>
    public readonly record struct PlanLine(int Node, int Number, int Months, double Price, bool WaitsForPrevious);

    /// <summary>Most direction tags shown: four on a node card (the data gives 2..4 per node), three on a strip card.</summary>
    public const int NodeDirsShown = 4, StripDirsShown = 3;

    /// <summary>The first <paramref name="max"/> items of a list of tags; the rest are dropped, never squeezed.</summary>
    public static List<T> Cap<T>(IEnumerable<T> items, int max)
    {
        var r = new List<T>(max);
        foreach (var x in items) { if (r.Count >= max) break; r.Add(x); }
        return r;
    }

    /// <summary>Whole months of an institute's own fit for a node (FitInstitutes, the assigned institute), or −1 when the fit has no such institute.</summary>
    public static int MonthsAt(ImmutableArray<InstituteFitView> fit, int slot)
    {
        if (!fit.IsDefault) foreach (var f in fit) if (f.Slot == slot && f.Standing) return Ceil(f.Months);
        return -1;
    }

    /// <summary>Comparison of two nodes A and B (price and months from the same quotes): who is cheaper and quicker and by how much; winner 0 = equal.</summary>
    public readonly record struct Comparison(int PriceWinner, double PriceDiff, int MonthsWinner, int MonthsDiff);

    public static Comparison Compare(double priceA, int monthsA, double priceB, int monthsB) =>
        new(priceA < priceB ? 1 : priceB < priceA ? 2 : 0, Math.Abs(priceA - priceB), monthsA < monthsB ? 1 : monthsB < monthsA ? 2 : 0, Math.Abs(monthsA - monthsB));

    /// <summary>The suggested plan as groups per institute with their totals; a line waits when its node needs a node earlier in the path. The months of a line are those of the institute it stands in (<paramref name="pathFit"/>, aligned with the path nodes), else those of the best one.</summary>
    public static List<(int Slot, List<PlanLine> Lines, int Months, double Price)> PlanGroups(ImmutableArray<QueuePlan> plan, PathView path, ImmutableArray<ImmutableArray<InstituteFitView>> pathFit = default)
    {
        var res = new List<(int, List<PlanLine>, int, double)>();
        foreach (var q in plan)
        {
            var lines = new List<PlanLine>(); int months = 0; double price = 0; int pos = 1;
            foreach (int node in q.Nodes)
            {
                int at = -1;
                for (int i = 0; i < path.Nodes.Length; i++) if (path.Nodes[i].Node == node) at = i;
                if (at < 0) continue;
                var pn = path.Nodes[at];
                int own = !pathFit.IsDefault && at < pathFit.Length ? MonthsAt(pathFit[at], q.Slot) : -1;
                int m = own >= 0 ? own : Ceil(pn.Months);
                lines.Add(new PlanLine(node, at + 1, m, pn.Price.ToDouble(), !pn.InFrontier || pos > 1));
                months += m; price += pn.Price.ToDouble(); pos++;
            }
            res.Add((q.Slot, lines, months, price));
        }
        return res;
    }

    /// <summary>The queue after moving the entry at <paramref name="index"/> by <paramref name="delta"/> places; null when it cannot move (a ReorderQueue is not sent).</summary>
    public static int[]? Move(int[] queue, int index, int delta)
    {
        int to = index + delta;
        if ((uint)index >= (uint)queue.Length || (uint)to >= (uint)queue.Length || delta == 0) return null;
        var r = (int[])queue.Clone();
        (r[index], r[to]) = (r[to], r[index]);
        return r;
    }

    /// <summary>The plan with one node moved to another institute's list (the end), or removed (toSlot −1). Lists without nodes stay in the plan so that ApplyQueues clears them.</summary>
    public static ImmutableArray<QueuePlan> Reassign(ImmutableArray<QueuePlan> plan, int node, int toSlot, int queueMax = QueueMax)
    {
        var lists = new SortedDictionary<int, List<int>>();
        foreach (var q in plan) { var l = new List<int>(); foreach (int n in q.Nodes) if (n != node) l.Add(n); lists[q.Slot] = l; }
        if (toSlot >= 0)
        {
            if (!lists.TryGetValue(toSlot, out var dst)) lists[toSlot] = dst = new List<int>();
            if (dst.Count <= queueMax) dst.Add(node);
        }
        var b = ImmutableArray.CreateBuilder<QueuePlan>(lists.Count);
        foreach (var kv in lists) b.Add(new QueuePlan(kv.Key, ImmutableArray.CreateRange(kv.Value)));
        return b.MoveToImmutable();
    }
}
