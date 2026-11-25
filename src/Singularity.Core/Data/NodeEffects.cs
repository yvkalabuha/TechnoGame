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

    /// <summary>The node is required to learn another node; Node = that node.</summary>
    RequiredFor,
}

/// <summary>
/// One effect of a node, with the data address it was read from (never a hand-written number).
/// <see cref="Together"/>: the node is one of several requirements (other groups, or an any_of group), so a card says
/// "needed for ..., together with ..." and not "opens". <see cref="Node"/>: the other node of Informs and RequiredFor, else -1.
/// </summary>
public readonly record struct NodeEffect(NodeEffectKind Kind, int ClassRow, int SlotRow, int AltRow, Fx6 Value, Fx6 Previous, bool Together, int Node = -1);

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
                    into.Add(new NodeEffect(NodeEffectKind.Generation, c, -1, -1, Fx6.FromInt(g - pc.GenStart[c] + 1), Fx6.Zero, false));
            }
        }

        NodeTable n = data.Nodes;
        for (int e = n.InformsStart[nodeRow]; e < n.InformsStart[nodeRow + 1]; e++)
            into.Add(new NodeEffect(NodeEffectKind.Informs, -1, -1, -1, n.InformsWeight[e], Fx6.Zero, false, n.InformsTo[e]));
        for (int o = 0; o < n.Count; o++)
        {
            for (int g = n.Requires.GroupStart[o]; g < n.Requires.GroupStart[o + 1]; g++)
            {
                ReadOnlySpan<int> members = n.Requires.GroupMembers(g);
                if (members.IndexOf(nodeRow) >= 0)
                    into.Add(new NodeEffect(NodeEffectKind.RequiredFor, -1, -1, -1, Fx6.Zero, Fx6.Zero,
                        members.Length > 1 || n.Requires.GroupStart[o + 1] - n.Requires.GroupStart[o] > 1, o));
            }
        }
    }

    /// <summary>
    /// Appends the other nodes the alternative of <paramref name="effect"/> needs besides <paramref name="nodeRow"/>
    /// (members of every requirement group, any_of partners included), ascending.
    /// </summary>
    public static void CoRequirements(GameData data, in NodeEffect effect, int nodeRow, List<int> into)
    {
        RequirementLists req = data.ProductClasses.AltRequires;
        int start = into.Count;
        for (int g = req.GroupStart[effect.AltRow]; g < req.GroupStart[effect.AltRow + 1]; g++)
        {
            foreach (int member in req.GroupMembers(g))
            {
                if (member != nodeRow)
                    into.Add(member);
            }
        }
        into.Sort(start, into.Count - start, null);
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
        bool found = false, together = req.GroupStart[a + 1] - req.GroupStart[a] > 1;
        for (int g = req.GroupStart[a]; g < req.GroupStart[a + 1]; g++)
        {
            ReadOnlySpan<int> members = req.GroupMembers(g);
            if (members.IndexOf(nodeRow) < 0)
                continue;
            found = true;
            together |= members.Length > 1;
        }
        if (!found)
            return;
        into.Add(new NodeEffect(NodeEffectKind.AlternativeUnlock, c, s, a, pc.AltPerfMult[a], BaselinePerf(pc, s, a), together));
        if (pc.AltAlloyKg[a] > Fx6.Zero)
            into.Add(new NodeEffect(NodeEffectKind.AlloyNeed, c, s, a, pc.AltAlloyKg[a], Fx6.Zero, together));
    }
}
