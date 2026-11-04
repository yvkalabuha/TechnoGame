using Singularity.Core.Data;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Core.WhyNot;

public sealed partial class WhyNotAnalyzer
{
    // The alternatives of slot s that count for requirement generation g: the airframe must meet platform_min_rank[g] (contract §4.4a).
    private bool Counts(int slot, int alt, int gen) => slot != _pl.AirframeSlot || DesignRules.MeetsPlatform(_pl, alt, gen);

    private bool HasAvailable(int actor, int slot, int gen)
    {
        for (int a = _pl.AltStart[slot]; a < _pl.AltStart[slot + 1]; a++)
        {
            if (Counts(slot, a, gen) && DesignRules.Available(_pl, a, n => Knows(actor, n)))
                return true;
        }
        return false;
    }

    /// <summary>
    /// A slot without an available alternative (spec §5.6 v0.15, W1): the alternative of the slot with the least closure cost is chosen
    /// (ties: higher rank, then the alternative id) and the link is the nearest missing node inside its closure only. An alternative
    /// that <c>max_eng_gen</c> rules out at the highest known engine is not considered (that is the link <c>ENGINE_GEN_LIMIT</c>); if none is left
    /// all alternatives are.
    /// </summary>
    private void AddSlotMissing(List<Link> links, int actor, int slot, int gen, long month)
    {
        int engine = -1;
        for (int a = _pl.AltStart[_pl.BaseSlot + 1] - 1; a >= _pl.AltStart[_pl.BaseSlot] && engine < 0; a--)
        {
            if (DesignRules.Available(_pl, a, n => Knows(actor, n)))
                engine = a;
        }
        var memo = new bool[_kg.Count][];
        for (int pass = 0; pass < 2; pass++)
        {
            int best = -1;
            Fx6 bestCost = Fx6.Zero;
            bool[]? bestClosure = null;
            for (int a = _pl.AltStart[slot]; a < _pl.AltStart[slot + 1]; a++)
            {
                if (!Counts(slot, a, gen) || pass == 0 && slot != _pl.BaseSlot && engine >= 0 && !DesignRules.EngineAllows(_pl, a, engine))
                    continue;
                bool[] closure = ClosureOfAlt(actor, a, memo);
                Fx6 cost = ClosureCost(actor, closure);
                if (best < 0 || cost < bestCost || cost == bestCost && _pl.AltRank[a] > _pl.AltRank[best])
                {
                    best = a;
                    bestCost = cost;
                    bestClosure = closure;
                }
            }
            if (best >= 0)
            {
                AddNearestInClosure(links, actor, bestClosure!, WhyNotCause.AltUnavailable, month);
                return;
            }
        }
    }

    private void AddAltMissing(List<Link> links, int actor, int alt, long month) =>
        AddNearestInClosure(links, actor, ClosureOfAlt(actor, alt, new bool[_kg.Count][]), WhyNotCause.AltUnavailable, month);

    // cl(alt): the closures of the chosen member of every requires_nodes group without a known member, and of every unknown process.
    private bool[] ClosureOfAlt(int actor, int alt, bool[][] memo)
    {
        var set = new bool[_kg.Count];
        for (int g = _pl.AltGroupStart[alt]; g < _pl.AltGroupStart[alt + 1]; g++)
        {
            var rows = new List<int>();
            bool any = false;
            for (int m = _pl.GroupMemberStart[g]; m < _pl.GroupMemberStart[g + 1]; m++)
            {
                any |= Knows(actor, _pl.GroupMembers[m]);
                rows.Add(_kg.RowOf(_pl.GroupMembers[m]));
            }
            if (!any)
                Union(set, ClosureOfNode(actor, ChooseMember(actor, rows, memo), memo));
        }
        foreach (int p in _pl.PrcOf(alt))
        {
            if (!Knows(actor, p))
                Union(set, ClosureOfNode(actor, _kg.RowOf(p), memo));
        }
        return set;
    }

    // cl(n): empty for a known node, else n and the closure of the chosen member of every requires-group without a known member.
    private bool[] ClosureOfNode(int actor, int row, bool[][] memo)
    {
        if (memo[row] is { } done)
            return done;
        var set = new bool[_kg.Count];
        if (!_k.IsKnown(actor, row))
        {
            set[row] = true;
            for (int g = _kg.Requires.GroupStart[row]; g < _kg.Requires.GroupStart[row + 1]; g++)
            {
                var rows = new List<int>();
                bool any = false;
                foreach (int m in _kg.Requires.GroupMembers(g))
                {
                    any |= _k.IsKnown(actor, m);
                    rows.Add(m);
                }
                if (!any)
                    Union(set, ClosureOfNode(actor, ChooseMember(actor, rows, memo), memo));
            }
        }
        memo[row] = set;
        return set;
    }

    // The member of a group with the least closure cost; ties by node number.
    private int ChooseMember(int actor, List<int> rows, bool[][] memo)
    {
        int best = -1;
        Fx6 bestCost = Fx6.Zero;
        foreach (int m in rows)
        {
            Fx6 cost = ClosureCost(actor, ClosureOfNode(actor, m, memo));
            if (best < 0 || cost < bestCost || cost == bestCost && _kg.Number[m] < _kg.Number[best])
            {
                best = m;
                bestCost = cost;
            }
        }
        return best;
    }

    private static void Union(bool[] into, bool[] other)
    {
        for (int i = 0; i < into.Length; i++)
            into[i] |= other[i];
    }

    // cost(S) = Σ left(n): money left to learn each node of the set once.
    private Fx6 ClosureCost(int actor, bool[] closure)
    {
        Fx6 sum = Fx6.Zero;
        for (int r = 0; r < closure.Length; r++)
        {
            if (closure[r])
                sum += Left(actor, r);
        }
        return sum;
    }

    private Fx6 Left(int actor, int row) => Max(Fx6.Zero, NeedOf(row) - _k.Prog(actor, row)) * _k.CEff(actor, row);

    /// <summary>
    /// «Ближайший недостающий узел по цене» (contract §6, spec §5.6 W1): among the nodes of the given closure that are on the actor's
    /// frontier, the one with the least money left, <c>(need − prog)·c_eff</c>; ties by node number. A hazard node counts up to
    /// <c>θ_start</c> (where its discovery chance begins) and has no <c>eta</c> beyond it: the discovery is a draw.
    /// </summary>
    private void AddNearestInClosure(List<Link> links, int actor, bool[] closure, WhyNotCause cause, long month)
    {
        int best = -1;
        Fx6 bestCost = Fx6.Zero;
        for (int r = 0; r < closure.Length; r++)
        {
            if (!closure[r] || !_k.InFrontier(actor, r))
                continue;
            Fx6 cost = Left(actor, r);
            if (best < 0 || cost < bestCost || cost == bestCost && _kg.Number[r] < _kg.Number[best])
            {
                best = r;
                bestCost = cost;
            }
        }
        if (best < 0)
            return;

        Fx6 have = _k.Prog(actor, best), need = NeedOf(best);
        int? eta = _kg.IsHazard[best] && have >= need ? null : Eta(have, need, _k.ProgressRate(actor, best, month));
        links.Add(new Link(WhyNotLayer.Knowledge, cause, _kg.Number[best], have, need, Offer(Lever.ResearchPriority), Fx6.Zero, eta));
    }

    // APP target: the closure of the node itself.
    private void AddNearestMissing(List<Link> links, int actor, int row, WhyNotCause cause, long month) =>
        AddNearestInClosure(links, actor, ClosureOfNode(actor, row, new bool[_kg.Count][]), cause, month);

    private Fx6 NeedOf(int row) => _kg.IsHazard[row] ? _kp.ThetaStart : Fx6.One;
}
