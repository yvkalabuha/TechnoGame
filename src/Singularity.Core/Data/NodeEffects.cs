using Singularity.Kernel;

namespace Singularity.Core.Data;

public enum NodeEffectKind
{
    /// <summary>The node (alone or in an any_of group) opens a slot alternative; Value = its perf_mult, Previous = the rank below.</summary>
    AlternativeUnlock,

    /// <summary>The alternative the node opens needs alloy; Value = alloy_kg.</summary>
    AlloyNeed,

    /// <summary>A PRC node is a process of the class; Value = labour hours per unit.</summary>
    ProcessUnlock,

    /// <summary>An APP node opens a product generation; Value = the generation number.</summary>
    Generation,
}

/// <summary>One effect of a node, with the data address it was read from (never a hand-written number).</summary>
public readonly record struct NodeEffect(NodeEffectKind Kind, int ClassRow, int SlotRow, int AltRow, Fx6 Value, Fx6 Previous, bool Shared);

/// <summary>Derives node effects from <see cref="ProductClassTable"/> (NODE_EFFECTS_SCHEMA §4): the numbers live only in product_classes.yaml.</summary>
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
                into.Add(new NodeEffect(NodeEffectKind.ProcessUnlock, c, -1, -1, pc.ProcLabour[p], Fx6.Zero, false));
            for (int g = pc.GenStart[c]; g < pc.GenStart[c + 1]; g++)
            {
                if (pc.GenApp[g] == nodeRow)
                    into.Add(new NodeEffect(NodeEffectKind.Generation, c, -1, -1, Fx6.FromInt(g - pc.GenStart[c] + 1), Fx6.Zero, false));
            }
        }
    }

    private static void AddAlternative(ProductClassTable pc, int c, int s, int a, int nodeRow, List<NodeEffect> into)
    {
        RequirementLists req = pc.AltRequires;
        bool shared = false, found = false;
        for (int g = req.GroupStart[a]; g < req.GroupStart[a + 1]; g++)
        {
            ReadOnlySpan<int> members = req.GroupMembers(g);
            if (members.IndexOf(nodeRow) < 0)
                continue;
            found = true;
            shared = members.Length > 1;
        }
        if (!found)
            return;
        Fx6 previous = a > pc.AltStart[s] ? pc.AltPerfMult[a - 1] : Fx6.Zero;
        into.Add(new NodeEffect(NodeEffectKind.AlternativeUnlock, c, s, a, pc.AltPerfMult[a], previous, shared));
        if (pc.AltAlloyKg[a] > Fx6.Zero)
            into.Add(new NodeEffect(NodeEffectKind.AlloyNeed, c, s, a, pc.AltAlloyKg[a], Fx6.Zero, shared));
    }
}
