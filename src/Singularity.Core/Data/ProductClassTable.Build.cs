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
        var slotIsAf = new List<bool>();
        var altStart = new List<int> { 0 };
        var altId = new List<string>();
        var altSlot = new List<int>();
        var altRank = new List<int>();
        var altPerf = new List<Fx6>();
        var altEngGen = new List<int>();
        var altAfClass = new List<int>();
        var altAlloy = new List<Fx6>();
        var altMaxEngGen = new List<int>();
        var altAlloyFree = new List<bool>();
        var altUpkeep = new List<Fx6>();
        var altSubstitute = new List<bool>();
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
        var cRef = new List<int>();
        var puStart = new List<int> { 0 };
        var puScale = new List<Fx6>();
        var afStart = new List<int> { 0 };
        var afHFirst = new List<Fx6>();
        var afHFloor = new List<Fx6>();
        var afMaterials = new List<Fx6>();
        var afDesignScale = new List<Fx6>();
        var afProcScale = new List<Fx6>();
        var genStart = new List<int> { 0 };
        var genApp = new List<int>();
        var genCrew = new List<int>();
        var genPlatform = new List<int>();

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
            int baseSlots = 0, afSlots = 0;

            foreach (YNode slotNode in m.Seq("slots").Items)
            {
                var s = new MapReader(slotNode);
                string sid = s.Str("id");
                if (slotId.Skip(firstSlot).Contains(sid))
                    throw DataLoadException.At(slotNode, $"duplicate slot '{sid}' in '{id}'");
                bool isBase = s.Bool("base");
                baseSlots += isBase ? 1 : 0;
                string block = s.Str("block");
                if (block is not ("pu" or "af"))
                    throw DataLoadException.At(slotNode, $"slot '{sid}': block must be 'pu' or 'af'");
                bool isAf = block == "af";
                if (isBase && isAf)
                    throw DataLoadException.At(slotNode, "the base (engine) slot belongs to the PU block");
                afSlots += isAf ? 1 : 0;
                int slotRow = slotId.Count;
                slotId.Add(sid);
                slotClass.Add(classRow);
                slotIsBase.Add(isBase);
                slotIsAf.Add(isAf);

                var alts = new List<Alt>();
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
                    altEngGen.Add(alts[i].EngGen);
                    altAfClass.Add(alts[i].AfClass);
                    altAlloy.Add(alts[i].AlloyKg);
                    altMaxEngGen.Add(alts[i].MaxEngGen);
                    altAlloyFree.Add(alts[i].AlloyFree);
                    altUpkeep.Add(alts[i].UpkeepMult);
                    altSubstitute.Add(alts[i].Substitute);
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
            if (afSlots != 1)
                throw DataLoadException.At(map, $"product class '{id}' needs exactly one airframe (block af) slot, found {afSlots}");
            CheckAlloySlot(map, id, firstSlot, slotId.Count, altStart, altAlloy, altAlloyFree);
            CheckSubstitutes(map, id, firstSlot, slotId.Count, altStart, slotIsBase, altRank, altSubstitute);
            CheckBlocksDisjoint(map, id, firstSlot, slotId.Count, altStart, slotIsAf, prcStart, prc);

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
            int afCount = ReadAfClasses(m.Seq("af_classes"), afHFirst, afHFloor, afMaterials, afDesignScale, afProcScale);
            afStart.Add(afHFirst.Count);
            ReadPuRanks(m.Seq("pu_ranks"), puScale);
            puStart.Add(puScale.Count);
            cRef.Add(m.Int("c_ref"));
            if (cRef[^1] < 1)
                throw DataLoadException.At(m.Required("c_ref"), "c_ref must be at least 1");
            int firstGen = genApp.Count;
            ReadGenerations(m.Seq("generations"), nodes, ResolveNode, genApp, genCrew, genPlatform);
            genStart.Add(genApp.Count);
            CheckClassRefs(map, id, firstSlot, slotId.Count, altStart, slotIsAf, slotIsBase, altEngGen, altAfClass, altRank,
                afCount, puScale.Count - puStart[^2], genApp.Count - firstGen);
            CheckMaxEngGen(map, id, firstSlot, slotId.Count, altStart, slotIsBase, altMaxEngGen, genApp.Count - firstGen);
            m.End();
        }

        return new ProductClassTable
        {
            ClassId = [.. classIds], SlotStart = [.. slotStart],
            LightenStart = [.. lightenStart], LightenOrder = [.. lightenOrder],
            SlotId = [.. slotId], SlotClass = [.. slotClass], SlotIsBase = [.. slotIsBase], SlotIsAirframe = [.. slotIsAf], AltStart = [.. altStart],
            AltId = [.. altId], AltSlot = [.. altSlot], AltRank = [.. altRank], AltPerfMult = [.. altPerf],
            AltEngGen = [.. altEngGen], AltAfClass = [.. altAfClass], AltAlloyKg = [.. altAlloy],
            AltMaxEngGen = [.. altMaxEngGen], AltAlloyFree = [.. altAlloyFree], AltUpkeepMult = [.. altUpkeep], AltSubstitute = [.. altSubstitute],
            PrcStart = [.. prcStart], Prc = [.. prc], AltRequires = RequirementLists.Build(altRequires),
            HPm = [.. hpm], WageKh = [.. wage], Maintainability = [.. maintainability],
            RRef = [.. rRef], ProcStart = [.. procStart], ProcNode = [.. procNode], ProcN = [.. procN],
            ProcLabour = [.. procLabour], ProcBaseCost = [.. procBase],
            CRef = [.. cRef], PuStart = [.. puStart], PuProcScale = [.. puScale],
            AfStart = [.. afStart], AfHFirst = [.. afHFirst], AfHFloor = [.. afHFloor], AfMaterialsFloor = [.. afMaterials],
            AfDesignScale = [.. afDesignScale], AfProcScale = [.. afProcScale],
            GenStart = [.. genStart], GenApp = [.. genApp], GenCrewReq = [.. genCrew], GenPlatformMinRank = [.. genPlatform],
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
        List<int> crew, List<int> platformMinRank)
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
            int crewReq = g.Int("crew_req");
            if (crewReq < 1)
                throw DataLoadException.At(item, "crew_req must be at least 1");
            int minRank = g.Int("platform_min_rank");
            if (minRank < 1)
                throw DataLoadException.At(item, "platform_min_rank must be at least 1");
            app.Add(row);
            crew.Add(crewReq);
            platformMinRank.Add(minRank);
            g.End();
        }
        if (expected == 1)
            throw DataLoadException.At(seq, "a product class needs at least one generation");
    }

    // Airframe classes 1..K in order (contract §4.4a, product_inputs.md §14.4).
    private static int ReadAfClasses(YSeq seq, List<Fx6> hFirst, List<Fx6> hFloor, List<Fx6> materials, List<Fx6> designScale,
        List<Fx6> procScale)
    {
        int expected = 1;
        foreach (YNode item in seq.Items)
        {
            var a = new MapReader(item);
            int k = a.Int("af_class");
            if (k != expected++)
                throw DataLoadException.At(item, $"af_classes must be listed as 1, 2, ... in order; found {k}");
            Fx6 first = Positive(a, "h_first"), floor = Positive(a, "h_floor");
            if (floor > first)
                throw DataLoadException.At(item, "h_floor must not exceed h_first");
            Fx6 mat = a.Fixed("materials_floor");
            if (mat < Fx6.Zero)
                throw DataLoadException.At(item, "materials_floor must not be negative");
            hFirst.Add(first);
            hFloor.Add(floor);
            materials.Add(mat);
            designScale.Add(Positive(a, "design_scale"));
            procScale.Add(Positive(a, "proc_scale_af"));
            a.End();
        }
        return expected - 1;
    }

    private static void ReadPuRanks(YSeq seq, List<Fx6> scale)
    {
        int expected = 1;
        foreach (YNode item in seq.Items)
        {
            var r = new MapReader(item);
            int rank = r.Int("rank");
            if (rank != expected++)
                throw DataLoadException.At(item, $"pu_ranks must be listed as 1, 2, ... in order; found {rank}");
            scale.Add(Positive(r, "proc_scale_pu"));
            r.End();
        }
    }

    // Exactly one alloy slot (spec v0.14 §1.4, rule 8): every alternative has alloy_kg > 0 or alloy_free, at least one has alloy_kg > 0;
    // the other slots have neither field. The slot is found by the fields, not by the first alternative (a substitute of rank 0 comes first).
    private static void CheckAlloySlot(YMap at, string classId, int firstSlot, int endSlot, List<int> altStart, List<Fx6> alloy,
        List<bool> alloyFree)
    {
        int slots = 0;
        for (int s = firstSlot; s < endSlot; s++)
        {
            int kg = 0, free = 0, n = altStart[s + 1] - altStart[s];
            for (int a = altStart[s]; a < altStart[s + 1]; a++)
            {
                kg += alloy[a] > Fx6.Zero ? 1 : 0;
                free += alloyFree[a] ? 1 : 0;
            }
            if (kg + free == 0)
                continue;
            if (kg + free != n)
                throw DataLoadException.At(at, $"class '{classId}': alloy_kg or alloy_free must be on every alternative of the alloy slot or on none");
            if (kg == 0)
                throw DataLoadException.At(at, $"class '{classId}': an alloy slot needs at least one alternative with alloy_kg (alloy_free alone is not an alloy slot)");
            slots++;
        }
        if (slots != 1)
            throw DataLoadException.At(at, $"class '{classId}' needs exactly one alloy slot, found {slots}");
    }

    // substitute (spec v0.14 §1.4): not in the base slot; the slot keeps a non-substitute alternative; every substitute ranks strictly below all others.
    private static void CheckSubstitutes(YMap at, string classId, int firstSlot, int endSlot, List<int> altStart, List<bool> slotIsBase,
        List<int> rank, List<bool> substitute)
    {
        for (int s = firstSlot; s < endSlot; s++)
        {
            int minOther = int.MaxValue, maxSub = int.MinValue;
            for (int a = altStart[s]; a < altStart[s + 1]; a++)
            {
                if (substitute[a])
                    maxSub = Math.Max(maxSub, rank[a]);
                else
                    minOther = Math.Min(minOther, rank[a]);
            }
            if (maxSub == int.MinValue)
                continue;
            if (slotIsBase[s])
                throw DataLoadException.At(at, $"class '{classId}': substitute is not allowed in the engine (base) slot");
            if (minOther == int.MaxValue)
                throw DataLoadException.At(at, $"class '{classId}': a slot with a substitute needs a non-substitute alternative");
            if (maxSub >= minOther)
                throw DataLoadException.At(at, $"class '{classId}': a substitute must rank strictly below every non-substitute alternative of its slot");
        }
    }

    // max_eng_gen (spec v0.14 §1.4): 1..G_max, not in the engine (base) slot.
    private static void CheckMaxEngGen(YMap at, string classId, int firstSlot, int endSlot, List<int> altStart, List<bool> slotIsBase,
        List<int> maxEngGen, int generations)
    {
        for (int s = firstSlot; s < endSlot; s++)
        {
            for (int a = altStart[s]; a < altStart[s + 1]; a++)
            {
                if (maxEngGen[a] == 0)
                    continue;
                if (slotIsBase[s])
                    throw DataLoadException.At(at, $"class '{classId}': max_eng_gen is not allowed in the engine (base) slot");
                if (maxEngGen[a] > generations)
                    throw DataLoadException.At(at, $"class '{classId}': max_eng_gen must be in 1..{generations} (G_max)");
            }
        }
    }

    // R-PS2m (contract §4.4a): the PRCs of the PU block and of the AF block do not overlap.
    private static void CheckBlocksDisjoint(YMap at, string classId, int firstSlot, int endSlot, List<int> altStart,
        List<bool> slotIsAf, List<int> prcStart, List<int> prc)
    {
        var pu = new List<int>();
        var af = new List<int>();
        for (int s = firstSlot; s < endSlot; s++)
        {
            for (int a = altStart[s]; a < altStart[s + 1]; a++)
            {
                for (int i = prcStart[a]; i < prcStart[a + 1]; i++)
                    (slotIsAf[s] ? af : pu).Add(prc[i]);
            }
        }
        foreach (int row in af)
        {
            if (pu.Contains(row))
                throw DataLoadException.At(at, $"class '{classId}': a PRC is in both the PU and the AF block (R-PS2m needs disjoint blocks)");
        }
    }

    // eng_gen in 1..G_max, af_class in 1..K, pu_ranks cover the PU ranks (contract §4.4a validator).
    private static void CheckClassRefs(YMap at, string classId, int firstSlot, int endSlot, List<int> altStart, List<bool> slotIsAf,
        List<bool> slotIsBase, List<int> engGen, List<int> afClass, List<int> rank, int afClasses, int puRanks, int generations)
    {
        int maxRank = 1;
        for (int s = firstSlot; s < endSlot; s++)
        {
            for (int a = altStart[s]; a < altStart[s + 1]; a++)
            {
                if (slotIsBase[s] && (engGen[a] < 1 || engGen[a] > generations))
                    throw DataLoadException.At(at, $"class '{classId}': eng_gen of an engine alternative must be in 1..{generations} (G_max)");
                if (!slotIsBase[s] && engGen[a] != 0)
                    throw DataLoadException.At(at, $"class '{classId}': eng_gen is only allowed in the engine (base) slot");
                if (slotIsAf[s] && (afClass[a] < 1 || afClass[a] > afClasses))
                    throw DataLoadException.At(at, $"class '{classId}': af_class of an airframe alternative must be in 1..{afClasses}");
                if (!slotIsAf[s] && afClass[a] != 0)
                    throw DataLoadException.At(at, $"class '{classId}': af_class is only allowed in the airframe slot");
                if (!slotIsAf[s])
                    maxRank = Math.Max(maxRank, rank[a]);
            }
        }
        if (puRanks < maxRank)
            throw DataLoadException.At(at, $"class '{classId}': pu_ranks must cover ranks 1..{maxRank} of the PU slots");
    }

    private readonly record struct Alt(int Rank, string Id, Fx6 Perf, int EngGen, int AfClass, Fx6 AlloyKg, int MaxEngGen, bool AlloyFree,
        Fx6 UpkeepMult, bool Substitute, int[] Prc, List<int[]> Req, YNode At);

    private static Alt ReadAlternative(YNode node, NodeTable nodes, Func<YNode, string, int> resolve)
    {
        var a = new MapReader(node);
        string id = a.Str("id");
        if (!ModManifest.IsLocalId(id))
            throw DataLoadException.At(node, $"alternative ID '{id}' must match [a-z][a-z0-9_]*");
        int rank = a.Int("rank");
        bool substitute = a.Optional("substitute") is { } sn && Scalars.Bool(sn);
        if (rank < (substitute ? 0 : 1))
            throw DataLoadException.At(node, substitute ? "rank must not be negative" : "rank must be at least 1 (rank 0 is for a substitute only)");
        Fx6 perf = a.Fixed("perf_mult");
        if (perf <= Fx6.Zero)
            throw DataLoadException.At(node, "perf_mult must be positive");
        // Presence is checked against the slot roles after the class is read (CheckClassRefs): a wrong slot is then reported
        // by its own message, not by a missing field.
        int engGen = a.Optional("eng_gen") is null ? 0 : a.Int("eng_gen");
        int afClass = a.Optional("af_class") is null ? 0 : a.Int("af_class");
        Fx6 alloy = a.Optional("alloy_kg") is null ? Fx6.Zero : Positive(a, "alloy_kg");
        bool alloyFree = a.Optional("alloy_free") is { } fn && Scalars.Bool(fn);
        if (alloyFree && alloy > Fx6.Zero)
            throw DataLoadException.At(node, "alloy_free and alloy_kg are mutually exclusive");
        int maxEngGen = a.Optional("max_eng_gen") is null ? 0 : a.Int("max_eng_gen");
        if (a.Optional("max_eng_gen") is not null && maxEngGen < 1)
            throw DataLoadException.At(node, "max_eng_gen must be at least 1");
        Fx6 upkeepMult = a.Optional("upkeep_mult") is null ? Fx6.One : Positive(a, "upkeep_mult");

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
        return new Alt(rank, id, perf, engGen, afClass, alloy, maxEngGen, alloyFree, upkeepMult, substitute, [.. prcRows], req, node);
    }
}
