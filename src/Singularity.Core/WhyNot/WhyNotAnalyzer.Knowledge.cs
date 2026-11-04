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
        NodeClosure closure = Closure(actor);
        for (int pass = 0; pass < 2; pass++)
        {
            int best = -1;
            Fx6 bestCost = Fx6.Zero;
            bool[]? bestSet = null;
            for (int a = _pl.AltStart[slot]; a < _pl.AltStart[slot + 1]; a++)
            {
                if (!Counts(slot, a, gen) || pass == 0 && slot != _pl.BaseSlot && engine >= 0 && !DesignRules.EngineAllows(_pl, a, engine))
                    continue;
                bool[] set = ClosureOfAlt(actor, closure, a);
                Fx6 cost = closure.Cost(set);
                if (best < 0 || NodeClosure.BetterAlternative(cost, _pl.AltRank[a], _pl.AltId[a], bestCost, _pl.AltRank[best], _pl.AltId[best]))
                {
                    best = a;
                    bestCost = cost;
                    bestSet = set;
                }
            }
            if (best >= 0)
            {
                AddNearestInClosure(links, actor, closure, bestSet!, WhyNotCause.AltUnavailable, month);
                return;
            }
        }
    }

    private void AddAltMissing(List<Link> links, int actor, int alt, long month)
    {
        NodeClosure closure = Closure(actor);
        AddNearestInClosure(links, actor, closure, ClosureOfAlt(actor, closure, alt), WhyNotCause.AltUnavailable, month);
    }

    // The closure machinery of spec §5.6 over the actor's snapshot (rows of the knowledge graph).
    private NodeClosure Closure(int actor) => new(_kg.Count, r => _k.IsKnown(actor, r),
        r =>
        {
            var groups = new int[_kg.Requires.GroupStart[r + 1] - _kg.Requires.GroupStart[r]][];
            for (int g = 0; g < groups.Length; g++)
                groups[g] = [.. _kg.Requires.GroupMembers(_kg.Requires.GroupStart[r] + g).ToArray()];
            return groups;
        }, r => Left(actor, r), r => _kg.Number[r]);

    private bool[] ClosureOfAlt(int actor, NodeClosure closure, int alt)
    {
        var groups = new List<int[]>();
        for (int g = _pl.AltGroupStart[alt]; g < _pl.AltGroupStart[alt + 1]; g++)
        {
            var rows = new List<int>();
            for (int m = _pl.GroupMemberStart[g]; m < _pl.GroupMemberStart[g + 1]; m++)
                rows.Add(_kg.RowOf(_pl.GroupMembers[m]));
            groups.Add([.. rows]);
        }
        return closure.OfRequirements([.. groups], _pl.PrcOf(alt).ToArray().Select(p => _kg.RowOf(p)));
    }

    private Fx6 Left(int actor, int row) => Max(Fx6.Zero, NeedOf(row) - _k.Prog(actor, row)) * _k.CEff(actor, row);

    /// <summary>
    /// «Ближайший недостающий узел по цене» (contract §6, spec §5.6 W1): among the nodes of the given closure that are on the actor's
    /// frontier, the one with the least money left, <c>(need − prog)·c_eff</c>; ties by node number. A hazard node counts up to
    /// <c>θ_start</c> (where its discovery chance begins) and has no <c>eta</c> beyond it: the discovery is a draw.
    /// </summary>
    private void AddNearestInClosure(List<Link> links, int actor, NodeClosure closure, bool[] set, WhyNotCause cause, long month)
    {
        int best = closure.Nearest(set, r => _k.InFrontier(actor, r));
        if (best < 0)
            return;

        Fx6 have = _k.Prog(actor, best), need = NeedOf(best);
        int? eta = _kg.IsHazard[best] && have >= need ? null : Eta(have, need, _k.ProgressRate(actor, best, month));
        links.Add(new Link(WhyNotLayer.Knowledge, cause, _kg.Number[best], have, need, Offer(Lever.ResearchPriority), Fx6.Zero, eta));
    }

    // APP target: the closure of the node itself.
    private void AddNearestMissing(List<Link> links, int actor, int row, WhyNotCause cause, long month)
    {
        NodeClosure closure = Closure(actor);
        AddNearestInClosure(links, actor, closure, closure.OfNode(row), cause, month);
    }

    private Fx6 NeedOf(int row) => _kg.IsHazard[row] ? _kp.ThetaStart : Fx6.One;
}
