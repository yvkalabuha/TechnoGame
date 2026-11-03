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
        var hpm = new List<Fx6>();
        var wage = new List<Fx6>();
        var maintainability = new List<Fx6>();
        var rRef = new List<Fx6>();
        var procStart = new List<int> { 0 };
        var procNode = new List<int>();
        var procN = new List<Fx6>();
        var procLabour = new List<Fx6>();
        var procBase = new List<Fx6>();
        var genStart = new List<int> { 0 };
        var genApp = new List<int>();
        var genHFirst = new List<Fx6>();
        var genHFloor = new List<Fx6>();
        var genMaterials = new List<Fx6>();
        var genAlloy = new List<Fx6>();
        var genCrew = new List<int>();
        var genProcScale = new List<Fx6>();
        var genDesignScale = new List<Fx6>();
        var genBureau = new List<int>();

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

            hpm.Add(Positive(m, "h_pm"));
            wage.Add(m.Fixed("wage_kh"));
            if (wage[^1] < Fx6.Zero)
                throw DataLoadException.At(m.Required("wage_kh"), "wage_kh must not be negative");
            maintainability.Add(Positive(m, "maintainability"));
            rRef.Add(Positive(m, "r_ref"));
            ReadProcesses(m.Seq("processes"), nodes, ResolveNode, [.. prc.Skip(prcStart[altStart[firstSlot]]).Distinct().Order()],
                procNode, procN, procLabour, procBase);
            procStart.Add(procNode.Count);
            ReadGenerations(m.Seq("generations"), nodes, ResolveNode, genApp, genHFirst, genHFloor, genMaterials, genAlloy, genCrew,
                genProcScale, genDesignScale, genBureau);
            genStart.Add(genApp.Count);
            m.End();
        }

        return new ProductClassTable
        {
            ClassId = [.. classIds], SlotStart = [.. slotStart],
            LightenStart = [.. lightenStart], LightenOrder = [.. lightenOrder],
            SlotId = [.. slotId], SlotClass = [.. slotClass], SlotIsBase = [.. slotIsBase], AltStart = [.. altStart],
            AltId = [.. altId], AltSlot = [.. altSlot], AltRank = [.. altRank], AltPerfMult = [.. altPerf],
            PrcStart = [.. prcStart], Prc = [.. prc], AltRequires = RequirementLists.Build(altRequires),
            HPm = [.. hpm], WageKh = [.. wage], Maintainability = [.. maintainability],
            RRef = [.. rRef], ProcStart = [.. procStart], ProcNode = [.. procNode], ProcN = [.. procN],
            ProcLabour = [.. procLabour], ProcBaseCost = [.. procBase], GenStart = [.. genStart], GenApp = [.. genApp],
            GenHFirst = [.. genHFirst], GenHFloor = [.. genHFloor], GenMaterialsFloor = [.. genMaterials],
            GenAlloyKg = [.. genAlloy], GenCrewReq = [.. genCrew],
            GenProcScale = [.. genProcScale], GenDesignScale = [.. genDesignScale], GenBureauEngineers = [.. genBureau],
        };
    }

    private static Fx6 Positive(MapReader m, string key)
    {
        Fx6 v = m.Fixed(key);
        return v > Fx6.Zero ? v : throw DataLoadException.At(m.Required(key), $"{key} must be positive");
    }

    // One cost row per PRC used by any alternative of the class, and none for other nodes; rows sorted by node row.
    private static void ReadProcesses(YSeq seq, NodeTable nodes, Func<YNode, string, int> resolve, int[] used,
        List<int> node, List<Fx6> n, List<Fx6> labour, List<Fx6> baseCost)
    {
        var rows = new List<(int Node, Fx6 N, Fx6 Labour, Fx6 Base)>();
        foreach (YNode item in seq.Items)
        {
            var p = new MapReader(item);
            YNode at = p.Required("prc");
            int row = resolve(at, Scalars.Str(at));
            if (Array.BinarySearch(used, row) < 0)
                throw DataLoadException.At(at, $"processes: '{nodes.Id[row]}' is in no prc_set of the class");
            if (rows.Exists(r => r.Node == row))
                throw DataLoadException.At(at, $"processes: '{nodes.Id[row]}' is listed twice");
            rows.Add((row, Positive(p, "n_p"), Positive(p, "labour_hours"), Positive(p, "base_cost")));
            p.End();
        }
        foreach (int row in used)
        {
            if (!rows.Exists(r => r.Node == row))
                throw DataLoadException.At(seq, $"processes: no cost row for '{nodes.Id[row]}'");
        }
        rows.Sort((a, b) => a.Node.CompareTo(b.Node));
        foreach (var r in rows)
        {
            node.Add(r.Node);
            n.Add(r.N);
            labour.Add(r.Labour);
            baseCost.Add(r.Base);
        }
    }

    // Generations 1..G in order, each with an APP node of that generation.
    private static void ReadGenerations(YSeq seq, NodeTable nodes, Func<YNode, string, int> resolve, List<int> app,
        List<Fx6> hFirst, List<Fx6> hFloor, List<Fx6> materials, List<Fx6> alloy, List<int> crew, List<Fx6> procScale, List<Fx6> designScale, List<int> bureau)
    {
        int expected = 1;
        foreach (YNode item in seq.Items)
        {
            var g = new MapReader(item);
            int gen = g.Int("gen");
            if (gen != expected++)
                throw DataLoadException.At(item, $"generations must be listed as 1, 2, ... in order; found {gen}");
            YNode at = g.Required("app");
            int row = resolve(at, Scalars.Str(at));
            if (nodes.Type[row] != NodeType.APP || nodes.Gen[row] != gen)
                throw DataLoadException.At(at, $"'{nodes.Id[row]}' is not an APP node of generation {gen}");
            Fx6 first = Positive(g, "h_first"), floor = Positive(g, "h_floor");
            if (floor > first)
                throw DataLoadException.At(item, "h_floor must not exceed h_first");
            Fx6 mat = g.Fixed("materials_floor");
            if (mat < Fx6.Zero)
                throw DataLoadException.At(item, "materials_floor must not be negative");
            int crewReq = g.Int("crew_req");
            if (crewReq < 1)
                throw DataLoadException.At(item, "crew_req must be at least 1");
            app.Add(row);
            hFirst.Add(first);
            hFloor.Add(floor);
            materials.Add(mat);
            alloy.Add(Positive(g, "alloy_kg"));
            crew.Add(crewReq);
            procScale.Add(Positive(g, "proc_scale"));
            designScale.Add(Positive(g, "design_scale"));
            int engineers = g.Int("bureau_engineers");
            if (engineers < 1)
                throw DataLoadException.At(item, "bureau_engineers must be at least 1");
            bureau.Add(engineers);
            g.End();
        }
        if (expected == 1)
            throw DataLoadException.At(seq, "a product class needs at least one generation");
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
