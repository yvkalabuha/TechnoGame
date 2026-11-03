using Singularity.Core.Data.Yaml;
using Singularity.Kernel;

namespace Singularity.Core.Data;

public sealed partial class ProductClassTable
{
    internal static ProductClassTable Build(List<(string Id, YMap Map)> objects, NodeTable nodes)
    {
        var classIds = new List<string>();
        var slotStart = new List<int> { 0 };
        var lightenStart = new List<int> { 0 };
        var lightenOrder = new List<int>();
        var slotId = new List<string>();
        var slotClass = new List<int>();
        var slotIsBase = new List<bool>();
        var altStart = new List<int> { 0 };
        var altId = new List<string>();
        var altSlot = new List<int>();
        var altRank = new List<int>();
        var altPerf = new List<Fx6>();
        var prcStart = new List<int> { 0 };
        var prc = new List<int>();
        var altRequires = new List<List<int[]>>();

        int ResolveNode(YNode at, string id) =>
            nodes.IndexOf(id) is var row and >= 0 ? row : throw DataLoadException.At(at, $"reference to unknown node '{id}'");

        // Objects arrive sorted by ID, so class rows are sorted by ID.
        foreach ((string id, YMap map) in objects)
        {
            var m = new MapReader(map);
            m.Str("id");
            if (!ModManifest.IsLocalId(id))
                throw DataLoadException.At(map, $"product class ID '{id}' must match [a-z][a-z0-9_]*");
            if (m.Str("axis") != "perf")
                throw DataLoadException.At(map, "axis must be 'perf' (slice spec §1.4)");
            int classRow = classIds.Count;
            classIds.Add(id);
            int firstSlot = slotId.Count;
            int baseSlots = 0;

            foreach (YNode slotNode in m.Seq("slots").Items)
            {
                var s = new MapReader(slotNode);
                string sid = s.Str("id");
                if (slotId.Skip(firstSlot).Contains(sid))
                    throw DataLoadException.At(slotNode, $"duplicate slot '{sid}' in '{id}'");
                bool isBase = s.Bool("base");
                baseSlots += isBase ? 1 : 0;
                int slotRow = slotId.Count;
                slotId.Add(sid);
                slotClass.Add(classRow);
                slotIsBase.Add(isBase);

                var alts = new List<(int Rank, string Id, Fx6 Perf, int[] Prc, List<int[]> Req, YNode At)>();
                foreach (YNode altNode in s.Seq("alternatives").Items)
                    alts.Add(ReadAlternative(altNode, nodes, ResolveNode));
                s.End();
                if (alts.Count == 0)
                    throw DataLoadException.At(slotNode, $"slot '{sid}' has no alternatives");
                alts.Sort((a, b) => a.Rank.CompareTo(b.Rank));
                for (int i = 0; i < alts.Count; i++)
                {
                    if (i > 0 && alts[i].Rank == alts[i - 1].Rank)
                        throw DataLoadException.At(alts[i].At, $"duplicate rank {alts[i].Rank} in slot '{sid}'");
                    if (altId.Skip(altStart[firstSlot]).Contains(alts[i].Id))
                        throw DataLoadException.At(alts[i].At, $"duplicate alternative '{alts[i].Id}' in '{id}'");
                    altId.Add(alts[i].Id);
                    altSlot.Add(slotRow);
                    altRank.Add(alts[i].Rank);
                    altPerf.Add(alts[i].Perf);
                    prc.AddRange(alts[i].Prc);
                    prcStart.Add(prc.Count);
                    altRequires.Add(alts[i].Req);
                }
                altStart.Add(altId.Count);
            }
            slotStart.Add(slotId.Count);
            if (slotId.Count == firstSlot)
                throw DataLoadException.At(map, $"product class '{id}' has no slots");
            if (baseSlots != 1)
                throw DataLoadException.At(map, $"product class '{id}' needs exactly one base slot, found {baseSlots}");

            // F-PC2: lighten_order lists every non-base slot exactly once.
            YSeq order = m.Seq("lighten_order");
            foreach (YNode item in order.Items)
            {
                string name = Scalars.Str(item);
                int slot = slotId.IndexOf(name, firstSlot);
                if (slot < 0)
                    throw DataLoadException.At(item, $"lighten_order names unknown slot '{name}'");
                if (slotIsBase[slot] || lightenOrder.Skip(lightenStart[^1]).Contains(slot))
                    throw DataLoadException.At(item, $"lighten_order: slot '{name}' is the base slot or listed twice");
                lightenOrder.Add(slot);
            }
            if (lightenOrder.Count - lightenStart[^1] != slotId.Count - firstSlot - 1)
                throw DataLoadException.At(order, "lighten_order must list every non-base slot");
            lightenStart.Add(lightenOrder.Count);
            m.End();
        }

        return new ProductClassTable
        {
            ClassId = [.. classIds], SlotStart = [.. slotStart],
            LightenStart = [.. lightenStart], LightenOrder = [.. lightenOrder],
            SlotId = [.. slotId], SlotClass = [.. slotClass], SlotIsBase = [.. slotIsBase], AltStart = [.. altStart],
            AltId = [.. altId], AltSlot = [.. altSlot], AltRank = [.. altRank], AltPerfMult = [.. altPerf],
            PrcStart = [.. prcStart], Prc = [.. prc], AltRequires = RequirementLists.Build(altRequires),
        };
    }

    private static (int Rank, string Id, Fx6 Perf, int[] Prc, List<int[]> Req, YNode At) ReadAlternative(
        YNode node, NodeTable nodes, Func<YNode, string, int> resolve)
    {
        var a = new MapReader(node);
        string id = a.Str("id");
        if (!ModManifest.IsLocalId(id))
            throw DataLoadException.At(node, $"alternative ID '{id}' must match [a-z][a-z0-9_]*");
        int rank = a.Int("rank");
        if (rank < 1)
            throw DataLoadException.At(node, "rank must be at least 1");
        Fx6 perf = a.Fixed("perf_mult");
        if (perf <= Fx6.Zero)
            throw DataLoadException.At(node, "perf_mult must be positive");

        var prcRows = new List<int>();
        foreach (YNode item in a.Seq("prc_set").Items)
        {
            int row = resolve(item, Scalars.Str(item));
            if (nodes.Type[row] != NodeType.PRC)
                throw DataLoadException.At(item, $"prc_set member '{nodes.Id[row]}' is not a PRC node");
            if (prcRows.Contains(row))
                throw DataLoadException.At(item, $"prc_set lists '{nodes.Id[row]}' twice");
            prcRows.Add(row);
        }
        prcRows.Sort();
        List<int[]> req = RequirementLists.ReadGroups(a.Seq("requires_nodes"), resolve);
        a.End();
        return (rank, id, perf, [.. prcRows], req, node);
    }
}
