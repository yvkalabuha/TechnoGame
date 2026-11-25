using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Core.Data;

public enum NodeEffectKind
{
    /// <summary>The node is a requirement of a slot alternative; Value = its perf_mult, Previous = the best alternative it is compared with (see <see cref="NodeEffects"/>).</summary>
    AlternativeUnlock,

    /// <summary>The alternative the node is required for needs alloy; Value = alloy_kg.</summary>
    AlloyNeed,

    /// <summary>A PRC node is a process of the class; Value = labour hours per unit.</summary>
    ProcessUnlock,

    /// <summary>A PRC node of tier 5 or more can be run under a mastery programme; Value = the tier.</summary>
    Mastery,

    /// <summary>An APP node opens a product generation; Value = the generation number.</summary>
    Generation,

    /// <summary>The node informs another node (cheaper research); Node = the target, Value = the edge weight.</summary>
    Informs,

    /// <summary>The node is a mandatory requirement to learn another node; Node = that node.</summary>
    RequiredFor,

    /// <summary>The node is one of several alternative ways (an any_of group) to the requirement of another node; Node = that node.</summary>
    AlternativePathFor,

    /// <summary>The node speeds up research (#562): Value = the bonus added to the research speed of the directions in the bit mask <c>MinRank</c> (bit = direction number).</summary>
    ResearchSpeed,
}

/// <summary>
/// One effect of a node, with the data address it was read from (never a hand-written number).
/// <see cref="Together"/>: other requirement groups or manufacturing processes must be known as well (mandatory co-requirements,
/// see <see cref="NodeEffects.Requirements"/>), so a card says "needed for ..., together with ..." and not "opens"; any_of
/// partners are alternatives, not co-requirements. <see cref="Node"/>: the other node of Informs and RequiredFor, else -1.
/// <see cref="MinRank"/>: of a Generation effect, the minimum airframe rank that must also be available.
/// </summary>
public readonly record struct NodeEffect(NodeEffectKind Kind, int ClassRow, int SlotRow, int AltRow, Fx6 Value, Fx6 Previous, bool Together, int Node = -1, int MinRank = 0);

/// <summary>Derives node effects from the loaded tables (NODE_EFFECTS_SCHEMA §4): the numbers live only in the data files.</summary>
public static class NodeEffects
{
    /// <summary>Appends the effects of node <paramref name="nodeRow"/> to <paramref name="into"/> (display path, not the tick path).</summary>
    public static void Derive(GameData data, int nodeRow, List<NodeEffect> into)
    {
        ProductClassTable pc = data.ProductClasses;
        for (int c = 0; c < pc.Count; c++)
        {
            for (int s = pc.SlotStart[c]; s < pc.SlotStart[c + 1]; s++)
            {
                for (int a = pc.AltStart[s]; a < pc.AltStart[s + 1]; a++)
                    AddAlternative(pc, c, s, a, nodeRow, into);
            }
            int p = pc.ProcessRow(c, nodeRow);
            if (p >= 0)
            {
                into.Add(new NodeEffect(NodeEffectKind.ProcessUnlock, c, -1, -1, pc.ProcLabour[p], Fx6.Zero, false));
                if (data.Nodes.Tier[nodeRow] >= MasteryParams.MinTier)
                    into.Add(new NodeEffect(NodeEffectKind.Mastery, c, -1, -1, Fx6.FromInt(data.Nodes.Tier[nodeRow]), Fx6.Zero, false));
            }
            for (int g = pc.GenStart[c]; g < pc.GenStart[c + 1]; g++)
            {
                if (pc.GenApp[g] == nodeRow)
                    into.Add(new NodeEffect(NodeEffectKind.Generation, c, -1, -1, Fx6.FromInt(g - pc.GenStart[c] + 1), Fx6.Zero,
                        pc.GenPlatformMinRank[g] > 1, MinRank: pc.GenPlatformMinRank[g]));
            }
        }

        NodeTable n = data.Nodes;
        for (int k = n.SpeedStart[nodeRow]; k < n.SpeedStart[nodeRow + 1]; k++)
            into.Add(new NodeEffect(NodeEffectKind.ResearchSpeed, -1, -1, -1, n.SpeedBonus[k], Fx6.Zero, false, MinRank: n.SpeedMask[k]));
        for (int e = n.InformsStart[nodeRow]; e < n.InformsStart[nodeRow + 1]; e++)
            into.Add(new NodeEffect(NodeEffectKind.Informs, -1, -1, -1, n.InformsWeight[e], Fx6.Zero, false, n.InformsTo[e]));
        for (int o = 0; o < n.Count; o++)
        {
            for (int g = n.Requires.GroupStart[o]; g < n.Requires.GroupStart[o + 1]; g++)
            {
                ReadOnlySpan<int> members = n.Requires.GroupMembers(g);
                if (members.IndexOf(nodeRow) >= 0)
                    into.Add(new NodeEffect(members.Length > 1 ? NodeEffectKind.AlternativePathFor : NodeEffectKind.RequiredFor, -1, -1, -1,
                        Fx6.Zero, Fx6.Zero, n.Requires.GroupStart[o + 1] - n.Requires.GroupStart[o] > 1, o));
            }
        }
    }

    /// <summary>
    /// Splits the other requirements of the alternative of <paramref name="effect"/> (seen from <paramref name="nodeRow"/>):
    /// <paramref name="alternatives"/> are the other members of the node's own any_of group (any one of them is enough, the node
    /// itself is not needed), <paramref name="coRequired"/> are the members of the other groups (all of them are needed too),
    /// <paramref name="processes"/> are the manufacturing processes of the alternative (all must be known too, DesignRules.Available).
    /// All are appended ascending.
    /// </summary>
    public static void Requirements(GameData data, in NodeEffect effect, int nodeRow, List<int> alternatives, List<int> coRequired, List<int> processes)
    {
        RequirementLists req = data.ProductClasses.AltRequires;
        int altStart = alternatives.Count, coStart = coRequired.Count, procStart = processes.Count;
        foreach (int p in data.ProductClasses.PrcSet(effect.AltRow))
        {
            if (p != nodeRow)
                processes.Add(p);
        }
        processes.Sort(procStart, processes.Count - procStart, null);
        for (int g = req.GroupStart[effect.AltRow]; g < req.GroupStart[effect.AltRow + 1]; g++)
        {
            ReadOnlySpan<int> members = req.GroupMembers(g);
            List<int> target = members.IndexOf(nodeRow) >= 0 ? alternatives : coRequired;
            foreach (int member in members)
            {
                if (member != nodeRow)
                    target.Add(member);
            }
        }
        alternatives.Sort(altStart, alternatives.Count - altStart, null);
        coRequired.Sort(coStart, coRequired.Count - coStart, null);
    }

    // The alternative is compared with the best lower-rank alternative of the same airframe class (af_fbw trades perf for
    // stability, so against af_mach2 it is a loss and shown as such); only when none exists, with the best one of a lower class.
    // Other slots compare with the rank below.
    private static Fx6 BaselinePerf(ProductClassTable pc, int slot, int alt)
    {
        int first = pc.AltStart[slot];
        if (!pc.SlotIsAirframe[slot])
            return alt > first ? pc.AltPerfMult[alt - 1] : Fx6.Zero;
        Fx6 sameClass = Fx6.Zero, lowerClass = Fx6.Zero;
        bool hasSame = false;
        int lowerClassOf = -1;
        for (int b = alt - 1; b >= first; b--)
        {
            int cls = pc.AltAfClass[b];
            if (cls == pc.AltAfClass[alt])
            {
                if (!hasSame || pc.AltPerfMult[b] > sameClass)
                    sameClass = pc.AltPerfMult[b];
                hasSame = true;
            }
            else if (cls < pc.AltAfClass[alt] && (lowerClassOf < 0 || cls == lowerClassOf))
            {
                // the nearest lower class: the first one met going down
                lowerClassOf = cls;
                if (pc.AltPerfMult[b] > lowerClass)
                    lowerClass = pc.AltPerfMult[b];
            }
        }
        return hasSame ? sameClass : lowerClass;
    }

    private static void AddAlternative(ProductClassTable pc, int c, int s, int a, int nodeRow, List<NodeEffect> into)
    {
        RequirementLists req = pc.AltRequires;
        // A second requirement group or any manufacturing process is a mandatory co-requirement.
        bool found = false, together = req.GroupStart[a + 1] - req.GroupStart[a] > 1 || pc.PrcSet(a).Length > 0;
        for (int g = req.GroupStart[a]; g < req.GroupStart[a + 1]; g++)
            found |= req.GroupMembers(g).IndexOf(nodeRow) >= 0;
        if (!found)
            return;
        into.Add(new NodeEffect(NodeEffectKind.AlternativeUnlock, c, s, a, pc.AltPerfMult[a], BaselinePerf(pc, s, a), together));
        if (pc.AltAlloyKg[a] > Fx6.Zero)
            into.Add(new NodeEffect(NodeEffectKind.AlloyNeed, c, s, a, pc.AltAlloyKg[a], Fx6.Zero, together));
    }
}
