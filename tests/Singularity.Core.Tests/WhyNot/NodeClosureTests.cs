using Singularity.Core.WhyNot;
using Singularity.Kernel;

namespace Singularity.Core.Tests.WhyNot;

/// <summary>Spec §5.6 (v0.15, W1) on small synthetic graphs: closures, group choice, alternative choice, ties.</summary>
public class NodeClosureTests
{
    // Rows 0…n−1; requires[r] = groups of members; left[r] = money left; known[r]; number[r] = row + 100 unless given.
    private static NodeClosure Make(int[][][] requires, long[] left, bool[]? known = null, int[]? number = null) =>
        new(requires.Length, r => known?[r] ?? false, r => requires[r], r => Fx6.FromInt((int)left[r]), r => number?[r] ?? r + 100);

    private static bool[] Set(int n, params int[] rows)
    {
        var s = new bool[n];
        foreach (int r in rows)
            s[r] = true;
        return s;
    }

    private static bool Frontier(NodeClosure c, int[][][] requires, bool[] known, int r) =>
        !known[r] && requires[r].All(g => g.Any(m => known[m]));

    [Fact]
    public void AlternativeWithOneNodeX_BeatsAnAlternativeWithACheapFrontierNodeYAndAnExpensiveZBehindIt()
    {
        // 0 = X (left 10), 1 = Y (left 1, frontier), 2 = Z (left 20, requires Y). A needs X; B needs Z.
        int[][][] requires = [[], [], [[1]]];
        bool[] known = [false, false, false];
        NodeClosure c = Make(requires, [10, 1, 20], known);
        bool[] a = c.OfRequirements([], [0]), b = c.OfRequirements([], [2]);
        Assert.Equal(Fx6.FromInt(10), c.Cost(a));
        Assert.Equal(Fx6.FromInt(21), c.Cost(b));
        Assert.Equal(b, Set(3, 1, 2));
        Assert.True(NodeClosure.BetterAlternative(c.Cost(a), 1, "a", c.Cost(b), 1, "b"));
        Assert.Equal(0, c.Nearest(a, r => Frontier(c, requires, known, r)));
        // The pre-W1 rule: the nearest frontier node over the union of both would be Y.
        Assert.Equal(1, c.Nearest(Set(3, 0, 1, 2), r => Frontier(c, requires, known, r)));
    }

    [Fact]
    public void Ties_EqualCost_HigherRankThenSmallerAltIdOrdinal_ThenSmallerNodeNumber()
    {
        Fx6 cost = Fx6.FromInt(5);
        Assert.True(NodeClosure.BetterAlternative(cost, 3, "z", cost, 2, "a"));        // rank first
        Assert.False(NodeClosure.BetterAlternative(cost, 2, "a", cost, 3, "z"));
        Assert.True(NodeClosure.BetterAlternative(cost, 2, "comp_axial", cost, 2, "comp_centrifugal"));   // then alt_id, ordinal
        Assert.False(NodeClosure.BetterAlternative(cost, 2, "comp_centrifugal", cost, 2, "comp_axial"));
        Assert.True(NodeClosure.BetterAlternative(cost, 2, "B", cost, 2, "a"));          // ordinal: upper case before lower case
        Assert.False(NodeClosure.BetterAlternative(cost, 2, "a", cost, 2, "a"));
        Assert.True(NodeClosure.BetterAlternative(Fx6.FromInt(4), 1, "z", cost, 9, "a")); // a lower cost beats a higher rank

        // Nodes with equal left: the smaller node number, whatever the row order.
        int[][][] requires = [[], []];
        NodeClosure c = Make(requires, [3, 3], number: [50, 7]);
        Assert.Equal(1, c.Nearest(Set(2, 0, 1), _ => true));
        Assert.Equal(1, c.Choose([0, 1]));
    }

    [Fact]
    public void ADiamond_CommonAncestorIsCountedOnce()
    {
        // 0 = A (left 4); 1 = B and 2 = C both require A; 3 = D requires B and C (two groups).
        int[][][] requires = [[], [[0]], [[0]], [[1], [2]]];
        NodeClosure c = Make(requires, [4, 2, 3, 5]);
        bool[] cl = c.OfNode(3);
        Assert.Equal(Set(4, 0, 1, 2, 3), cl);
        Assert.Equal(Fx6.FromInt(4 + 2 + 3 + 5), c.Cost(cl));
    }

    [Fact]
    public void AnyOfGroup_TakesTheMemberWithTheSmallerClosureCost_AKnownMemberEmptiesTheGroup()
    {
        // 0 = P (left 1, requires Q = 1 with left 9); 2 = R (left 6). Group {P, R}: closure(P) costs 10, closure(R) costs 6.
        int[][][] requires = [[[1]], [], []];
        NodeClosure c = Make(requires, [1, 9, 6], number: [10, 11, 5]);
        Assert.Equal(2, c.Choose([0, 2]));
        Assert.Equal(Set(3, 2), c.OfRequirements([[0, 2]], []));
        var known = new NodeClosure(3, r => r == 2, r => requires[r], r => Fx6.FromInt(1), r => r);
        Assert.Equal(Set(3), known.OfRequirements([[0, 2]], []));
        // Equal cost: the smaller node number.
        NodeClosure tie = Make([[], []], [4, 4], number: [30, 20]);
        Assert.Equal(1, tie.Choose([0, 1]));
    }

    [Fact]
    public void KnownNodesAreNotInAClosure_AndTwoCallsGiveOneResult()
    {
        int[][][] requires = [[], [[0]], [[1]]];
        bool[] known = [true, false, false];
        NodeClosure c = Make(requires, [0, 2, 3], known);
        Assert.Equal(Set(3, 1, 2), c.OfNode(2));
        Assert.Equal(c.OfNode(2), c.OfNode(2));
        Assert.DoesNotContain(true, c.OfNode(0));
        Assert.Equal(1, c.Nearest(c.OfNode(2), r => Frontier(c, requires, known, r)));
    }
}
