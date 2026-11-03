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
    /// A slot without an available alternative: the link is the cheapest missing node over all its alternatives (contract §6 v2.4:
    /// an unavailable alternative is the same KNOWLEDGE link).
    /// </summary>
    private void AddSlotMissing(List<Link> links, int actor, int slot, int gen, long month)
    {
        var rows = new List<int>();
        for (int a = _pl.AltStart[slot]; a < _pl.AltStart[slot + 1]; a++)
        {
            if (Counts(slot, a, gen))
                AddAltTargets(rows, actor, a);
        }
        AddNearestMissing(links, actor, rows, WhyNotCause.AltUnavailable, month);
    }

    private void AddAltMissing(List<Link> links, int actor, int alt, long month)
    {
        var rows = new List<int>();
        AddAltTargets(rows, actor, alt);
        AddNearestMissing(links, actor, rows, WhyNotCause.AltUnavailable, month);
    }

    // Rows to learn for an alternative: the members of every requires-group without a known member, and its unknown processes.
    private void AddAltTargets(List<int> rows, int actor, int alt)
    {
        for (int g = _pl.AltGroupStart[alt]; g < _pl.AltGroupStart[alt + 1]; g++)
        {
            bool any = false;
            for (int m = _pl.GroupMemberStart[g]; m < _pl.GroupMemberStart[g + 1]; m++)
                any |= Knows(actor, _pl.GroupMembers[m]);
            if (any)
                continue;
            for (int m = _pl.GroupMemberStart[g]; m < _pl.GroupMemberStart[g + 1]; m++)
                rows.Add(_kg.RowOf(_pl.GroupMembers[m]));
        }
        foreach (int p in _pl.PrcOf(alt))
        {
            if (!Knows(actor, p))
                rows.Add(_kg.RowOf(p));
        }
    }

    /// <summary>
    /// «Ближайший недостающий узел по цене» (contract §6): among the unknown nodes of the requires-closure of <paramref name="startRows"/>
    /// whose own requirements are known (the actor's frontier), the one with the least money left, <c>(need − prog)·c_eff</c>; ties by node
    /// number. A hazard node counts up to <c>θ_start</c> (where its discovery chance begins) and has no <c>eta</c> beyond it: the
    /// discovery is a draw.
    /// </summary>
    private void AddNearestMissing(List<Link> links, int actor, List<int> startRows, WhyNotCause cause, long month)
    {
        var seen = new bool[_kg.Count];
        var stack = new Stack<int>();
        foreach (int r in startRows)
        {
            if (r >= 0 && !_k.IsKnown(actor, r))
                stack.Push(r);
        }
        while (stack.Count > 0)
        {
            int r = stack.Pop();
            if (seen[r])
                continue;
            seen[r] = true;
            for (int g = _kg.Requires.GroupStart[r]; g < _kg.Requires.GroupStart[r + 1]; g++)
            {
                bool any = false;
                foreach (int m in _kg.Requires.GroupMembers(g))
                    any |= _k.IsKnown(actor, m);
                if (any)
                    continue;
                foreach (int m in _kg.Requires.GroupMembers(g))
                {
                    if (!seen[m])
                        stack.Push(m);
                }
            }
        }

        int best = -1;
        Fx6 bestCost = Fx6.Zero;
        foreach (int r in _kg.Order)
        {
            if (!seen[r] || !_k.InFrontier(actor, r))
                continue;
            Fx6 left = Max(Fx6.Zero, NeedOf(r) - _k.Prog(actor, r));
            Fx6 cost = left * _k.CEff(actor, r);
            if (best < 0 || cost < bestCost)
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

    private Fx6 NeedOf(int row) => _kg.IsHazard[row] ? _kp.ThetaStart : Fx6.One;
}
