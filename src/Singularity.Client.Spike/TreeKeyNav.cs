using System;

namespace Singularity.Client.Spike;

/// <summary>Keyboard walk over the tree nodes (Godot-free, TASK-0132): Tab goes through the nodes in the given order, arrows to the nearest node in a direction.</summary>
public static class TreeKeyNav
{
    /// <summary>The next row of `order` after `cur` (step +1 or -1) that is `ok`; the first or last one when `cur` is not in the order. -1 when the end is passed (focus leaves the tree).</summary>
    public static int Step(ReadOnlySpan<int> order, int cur, int step, Func<int, bool> ok)
    {
        int at = order.IndexOf(cur);
        for (int k = at < 0 ? (step > 0 ? 0 : order.Length - 1) : at + step; k >= 0 && k < order.Length; k += step)
            if (ok(order[k])) return order[k];
        return -1;
    }

    /// <summary>The nearest `ok` node in the direction (dx, dy): it must lie ahead (a cone of about 63 degrees) and a sideways offset costs twice as much as a forward one. -1 when none.</summary>
    public static int Direction(ReadOnlySpan<float> xs, ReadOnlySpan<float> ys, int cur, float dx, float dy, Func<int, bool> ok)
    {
        int best = -1; float bestScore = float.MaxValue;
        for (int i = 0; i < xs.Length; i++)
        {
            if (i == cur || !ok(i)) continue;
            float vx = xs[i] - xs[cur], vy = ys[i] - ys[cur];
            float along = vx * dx + vy * dy, side = MathF.Abs(vx * dy - vy * dx);
            if (along <= 0f || side > along * 2f) continue;
            float score = along + 2f * side;
            if (score < bestScore) { bestScore = score; best = i; }
        }
        return best;
    }
}
