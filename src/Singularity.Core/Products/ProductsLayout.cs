using System.Collections.Immutable;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Kernel;

namespace Singularity.Core.Products;

/// <summary>Stage of a design slot (decision #67: П/О/С/В; «снят» after the last cohort is written off).</summary>
public enum DesignStage
{
    None = 0,

    /// <summary>П: from the order until the design work <c>W</c> is done.</summary>
    Project = 1,

    /// <summary>О: <c>W</c> done, testing, no series.</summary>
    Prototype = 2,

    /// <summary>С: the line produces; testing and detection go on.</summary>
    Series = 3,

    /// <summary>В: the line is closed, cohorts serve.</summary>
    InService = 4,

    /// <summary>
    /// «снят»: line closed and every cohort written off. Final: no command reopens the slot, and slots are never reused.
    /// </summary>
    Withdrawn = 5,
}

/// <summary>
/// Immutable shape of the product layer for one product class (spec §1.4, §3.3; contract §4.4, §4.6). Everything
/// is resolved from data once: slots and alternatives with their <c>prc_set</c> as process numbers and
/// <c>requires_nodes</c> as node numbers, process costs, airframe classes, generations. A design is a slot index
/// <c>i &lt; max_designs</c> per actor (contract §3.1, G4-8): the lowest free slot, never reused; <c>design_id = i</c> and
/// its production line has the same index. Requester ids (contract §4.6, one id space): skilled requests of line l for
/// cluster c are <c>l·C + c</c> (spec §3.5 A26), then the bureaus, then one defect-fix team per design.
/// </summary>
public sealed class ProductsLayout
{
    /// <summary>Workforce categories of the slice pools (spec §1.1).</summary>
    public const int EngineerCategory = 0;

    private ProductsLayout() { }

    public required string ClassId { get; init; }
    public required int SlotCount { get; init; }

    /// <summary>The slot whose multiplier is the base of <c>perf</c> (engine); never lightened.</summary>
    public required int BaseSlot { get; init; }

    /// <summary>Alternatives of slot s: <c>AltStart[s]..AltStart[s+1]</c>, ascending rank.</summary>
    public required ImmutableArray<int> AltStart { get; init; }
    public required ImmutableArray<string> AltId { get; init; }
    public required ImmutableArray<int> AltRank { get; init; }
    public required ImmutableArray<Fx6> AltPerf { get; init; }

    /// <summary>Engine alternative generation <c>eng_gen</c> (0 outside the engine slot), airframe class, alloy per unit (0 outside the hot slot).</summary>
    public required ImmutableArray<int> AltEngGen { get; init; }
    public required ImmutableArray<int> AltAfClass { get; init; }
    public required ImmutableArray<Fx6> AltAlloyKg { get; init; }

    /// <summary>
    /// Optional fields of spec v0.14 §1.4 (defaults when absent): <c>max_eng_gen</c> (0 = no limit), <c>alloy_free</c>,
    /// <c>upkeep_mult</c> (1), <c>substitute</c>.
    /// </summary>
    public required ImmutableArray<int> AltMaxEngGen { get; init; }
    public required ImmutableArray<bool> AltAlloyFree { get; init; }
    public required ImmutableArray<Fx6> AltUpkeepMult { get; init; }
    public required ImmutableArray<bool> AltSubstitute { get; init; }

    /// <summary>The airframe slot (AF block of R-PS2m) and the alloy-consuming slot (hot section).</summary>
    public required int AirframeSlot { get; init; }
    public required int AlloySlot { get; init; }

    /// <summary>Process numbers of alternative a (ascending): <c>AltPrc[AltPrcStart[a]..AltPrcStart[a+1]]</c>.</summary>
    public required ImmutableArray<int> AltPrcStart { get; init; }
    public required ImmutableArray<int> AltPrc { get; init; }

    /// <summary>Max tier over the alternative's processes, 0 for an empty <c>prc_set</c>.</summary>
    public required ImmutableArray<int> AltTier { get; init; }

    /// <summary>Requirement groups of alternative a (AND of OR-groups of node numbers).</summary>
    public required ImmutableArray<int> AltGroupStart { get; init; }
    public required ImmutableArray<int> GroupMemberStart { get; init; }
    public required ImmutableArray<int> GroupMembers { get; init; }

    /// <summary>Lighten order of the non-base slots (F-PC2).</summary>
    public required ImmutableArray<int> LightenOrder { get; init; }

    /// <summary>Processes of the class by ascending number with their cost inputs, tier and site cluster index.</summary>
    public required ImmutableArray<int> ProcNumber { get; init; }
    public required ImmutableArray<Fx6> ProcN { get; init; }
    public required ImmutableArray<Fx6> ProcLabour { get; init; }
    public required ImmutableArray<Fx6> ProcBaseCost { get; init; }
    public required ImmutableArray<int> ProcTier { get; init; }
    public required ImmutableArray<int> ProcCluster { get; init; }

    /// <summary>Process <c>k</c> of <see cref="ProcNumber"/> belongs to the AF block (airframe PRCs), else to PU.</summary>
    public required ImmutableArray<bool> ProcInAirframe { get; init; }

    public int Generations => GenApp.Length;

    /// <summary>Per generation g at index g − 1: APP node number, display crew, platform threshold (contract §4.4a).</summary>
    public required ImmutableArray<int> GenApp { get; init; }
    public required ImmutableArray<int> CrewReq { get; init; }
    public required ImmutableArray<int> PlatformMinRank { get; init; }

    /// <summary>Fields by airframe class <c>af_class</c> k at index k − 1 (contract §4.4a, product_inputs.md §14.4).</summary>
    public required ImmutableArray<Fx6> HFirst { get; init; }
    public required ImmutableArray<Fx6> HFloor { get; init; }
    public required ImmutableArray<Fx6> MaterialsFloor { get; init; }
    public required ImmutableArray<Fx6> DesignScale { get; init; }
    public required ImmutableArray<Fx6> ProcScaleAf { get; init; }

    /// <summary><c>proc_scale_pu[r]</c> at index r − 1 (R-PS2m).</summary>
    public required ImmutableArray<Fx6> ProcScalePu { get; init; }

    /// <summary><c>c_ref</c> (data): <c>D_ref = d0·c_ref</c> (contract §4.4b).</summary>
    public required int CRef { get; init; }

    /// <summary>Design slots per actor (<c>max_designs</c>, contract §3.1).</summary>
    public required int MaxDesigns { get; init; }

    public required Fx6 HPm { get; init; }
    public required Fx6 WageKh { get; init; }
    public required Fx6 Maintainability { get; init; }
    public required Fx6 RRef { get; init; }

    /// <summary>Clusters of the site (capability layout order); <c>C</c> of the requester ids.</summary>
    public required int ClusterCount { get; init; }
    public required int SkilledCategory { get; init; }

    public int SiteCount => SiteOwner.Length;

    /// <summary>Actor row of each site; the slice has one site per actor (spec §3.5 A27), site row = actor row.</summary>
    public required ImmutableArray<int> SiteOwner { get; init; }

    public required int Bureaus { get; init; }

    /// <summary>Calendar years with a cohort column: <c>StartYear .. StartYear + Years − 1</c>.</summary>
    public required int StartYear { get; init; }
    public required int Years { get; init; }

    /// <summary>Generation of every node by stable number (index = number; −1 for unused numbers).</summary>
    public required ImmutableArray<int> NodeGen { get; init; }

    /// <summary>"The actor knows every node of generations ≤ <paramref name="gen"/>" — the condition of the reference table (product_class.md §2) and of A18.</summary>
    public Func<int, bool> KnownUpTo(int gen) => n => n >= 0 && n < NodeGen.Length && NodeGen[n] >= 0 && NodeGen[n] <= gen;

    public int Designs => MaxDesigns;

    public int SkilledRequester(int line, int cluster) => line * ClusterCount + cluster;

    public int BureauRequester(int bureau) => Designs * ClusterCount + bureau;

    public int FixRequester(int design) => Designs * ClusterCount + Bureaus + design;

    /// <summary>Requester ids used by the layer: the capability layout needs at least this many.</summary>
    public int Requesters => Designs * ClusterCount + Bureaus + Designs;

    public ReadOnlySpan<int> PrcOf(int alt) => AltPrc.AsSpan()[AltPrcStart[alt]..AltPrcStart[alt + 1]];

    /// <summary>Index of a process number in the class tables, or −1.</summary>
    public int ProcIndex(int number)
    {
        int i = ProcNumber.AsSpan().BinarySearch(number);
        return i >= 0 ? i : -1;
    }

    /// <summary>
    /// <c>V_ref</c> (spec §3.5 A18, A29): the lower median over the processes of the «превосходящий» G2 project (every node of
    /// generations ≤ 2 known) of <c>v_p = (r_ref·n_p)·labour_p</c>, labour hours per month. A data-derived constant for <c>KnowledgeParams</c>.
    /// </summary>
    public Fx6 ReferenceVRef()
    {
        Span<int> alts = stackalloc int[SlotCount];
        if (Generations < 2 || !DesignRules.Superior(this, 2, KnownUpTo(2), alts))
            throw new InvalidOperationException("V_ref needs a G2 design of the class.");
        int[] prc = DesignRules.PrcSet(this, alts);
        var v = new Fx6[prc.Length];
        for (int i = 0; i < prc.Length; i++)
        {
            int k = ProcIndex(prc[i]);
            v[i] = RRef * ProcN[k] * ProcLabour[k];
        }
        Array.Sort(v);
        return v[(v.Length - 1) / 2];
    }

    /// <summary>Slot of an alternative.</summary>
    public int SlotOf(int alt)
    {
        for (int s = 0; s < SlotCount; s++)
        {
            if (alt < AltStart[s + 1])
                return s;
        }
        throw new ArgumentOutOfRangeException(nameof(alt));
    }

    /// <summary>
    /// Layout of class <paramref name="classId"/> from loaded data. Process clusters come from the capability layout,
    /// so requester ids and cluster indices agree with it; site owners too.
    /// </summary>
    public static ProductsLayout FromData(NodeTable nodes, ProductClassTable classes, string classId, IndustryLayout industry,
        int bureaus, int startYear, int years, int maxDesigns)
    {
        int c = classes.IndexOf(classId);
        if (c < 0)
            throw new ArgumentException($"Product class '{classId}' is not loaded.", nameof(classId));
        ArgumentOutOfRangeException.ThrowIfLessThan(bureaus, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(years, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDesigns, 1);

        int firstSlot = classes.SlotStart[c], slotCount = classes.SlotStart[c + 1] - firstSlot;
        int baseSlot = -1;
        var altStart = new List<int> { 0 };
        var altId = new List<string>();
        var altRank = new List<int>();
        var altPerf = new List<Fx6>();
        var altEngGen = new List<int>();
        var altAfClass = new List<int>();
        var altAlloy = new List<Fx6>();
        var altMaxEngGen = new List<int>();
        var altAlloyFree = new List<bool>();
        var altUpkeep = new List<Fx6>();
        var altSubstitute = new List<bool>();
        int airframeSlot = -1, alloySlot = -1;
        var altPrcStart = new List<int> { 0 };
        var altPrc = new List<int>();
        var altTier = new List<int>();
        var altGroupStart = new List<int> { 0 };
        var memberStart = new List<int> { 0 };
        var members = new List<int>();
        for (int s = 0; s < slotCount; s++)
        {
            int gs = firstSlot + s;
            if (classes.SlotIsBase[gs])
                baseSlot = s;
            if (classes.SlotIsAirframe[gs])
                airframeSlot = s;
            // The alloy slot is the one whose alternatives carry alloy_kg or alloy_free (the loader guarantees exactly one).
            for (int a = classes.AltStart[gs]; a < classes.AltStart[gs + 1]; a++)
            {
                if (classes.AltAlloyKg[a] > Fx6.Zero || classes.AltAlloyFree[a])
                    alloySlot = s;
            }
            for (int a = classes.AltStart[gs]; a < classes.AltStart[gs + 1]; a++)
            {
                altId.Add(classes.AltId[a]);
                altRank.Add(classes.AltRank[a]);
                altPerf.Add(classes.AltPerfMult[a]);
                altEngGen.Add(classes.AltEngGen[a]);
                altAfClass.Add(classes.AltAfClass[a]);
                altAlloy.Add(classes.AltAlloyKg[a]);
                altMaxEngGen.Add(classes.AltMaxEngGen[a]);
                altAlloyFree.Add(classes.AltAlloyFree[a]);
                altUpkeep.Add(classes.AltUpkeepMult[a]);
                altSubstitute.Add(classes.AltSubstitute[a]);
                int tier = 0;
                var numbers = new List<int>();
                foreach (int row in classes.PrcSet(a))
                {
                    numbers.Add(nodes.Number[row]);
                    tier = Math.Max(tier, nodes.Tier[row]);
                }
                numbers.Sort();
                altPrc.AddRange(numbers);
                altPrcStart.Add(altPrc.Count);
                altTier.Add(tier);
                RequirementLists req = classes.AltRequires;
                for (int g = req.GroupStart[a]; g < req.GroupStart[a + 1]; g++)
                {
                    foreach (int row in req.GroupMembers(g))
                        members.Add(nodes.Number[row]);
                    memberStart.Add(members.Count);
                }
                altGroupStart.Add(memberStart.Count - 1);
            }
            altStart.Add(altId.Count);
        }
        var lighten = new List<int>();
        for (int i = classes.LightenStart[c]; i < classes.LightenStart[c + 1]; i++)
            lighten.Add(classes.LightenOrder[i] - firstSlot);

        var procs = new List<(int Number, Fx6 N, Fx6 Labour, Fx6 Base, int Tier, int Cluster)>();
        for (int i = classes.ProcStart[c]; i < classes.ProcStart[c + 1]; i++)
        {
            int row = classes.ProcNode[i];
            int ir = industry.RowOf(nodes.Number[row]);
            if (ir < 0)
                throw new ArgumentException($"Process {nodes.Id[row]} is not in the capability layout.", nameof(industry));
            procs.Add((nodes.Number[row], classes.ProcN[i], classes.ProcLabour[i], classes.ProcBaseCost[i], nodes.Tier[row], industry.ClusterIndex[ir]));
        }
        procs.Sort((x, y) => x.Number.CompareTo(y.Number));

        var nodeGen = new int[nodes.Number.Max() + 1];
        Array.Fill(nodeGen, -1);
        for (int r = 0; r < nodes.Count; r++)
            nodeGen[nodes.Number[r]] = nodes.Gen[r];

        int g0 = classes.GenStart[c], g1 = classes.GenStart[c + 1];
        int a0 = classes.AfStart[c], a1 = classes.AfStart[c + 1], u0 = classes.PuStart[c], u1 = classes.PuStart[c + 1];
        Fx6[] Af(ImmutableArray<Fx6> col) => [.. Enumerable.Range(a0, a1 - a0).Select(i => col[i])];
        var afPrc = new List<int>();
        for (int a = altStart[airframeSlot]; a < altStart[airframeSlot + 1]; a++)
            afPrc.AddRange(altPrc.Skip(altPrcStart[a]).Take(altPrcStart[a + 1] - altPrcStart[a]));
        return new ProductsLayout
        {
            ClassId = classId, SlotCount = slotCount, BaseSlot = baseSlot,
            AltStart = [.. altStart], AltId = [.. altId], AltRank = [.. altRank], AltPerf = [.. altPerf],
            AltPrcStart = [.. altPrcStart], AltPrc = [.. altPrc], AltTier = [.. altTier],
            AltGroupStart = [.. altGroupStart], GroupMemberStart = [.. memberStart], GroupMembers = [.. members],
            LightenOrder = [.. lighten],
            ProcNumber = [.. procs.Select(p => p.Number)], ProcN = [.. procs.Select(p => p.N)],
            ProcLabour = [.. procs.Select(p => p.Labour)], ProcBaseCost = [.. procs.Select(p => p.Base)],
            ProcTier = [.. procs.Select(p => p.Tier)], ProcCluster = [.. procs.Select(p => p.Cluster)],
            AltEngGen = [.. altEngGen], AltAfClass = [.. altAfClass], AltAlloyKg = [.. altAlloy],
            AltMaxEngGen = [.. altMaxEngGen], AltAlloyFree = [.. altAlloyFree], AltUpkeepMult = [.. altUpkeep], AltSubstitute = [.. altSubstitute],
            AirframeSlot = airframeSlot, AlloySlot = alloySlot,
            ProcInAirframe = [.. procs.Select(p => afPrc.Contains(p.Number))],
            GenApp = [.. Enumerable.Range(g0, g1 - g0).Select(i => nodes.Number[classes.GenApp[i]])],
            CrewReq = [.. Enumerable.Range(g0, g1 - g0).Select(i => classes.GenCrewReq[i])],
            PlatformMinRank = [.. Enumerable.Range(g0, g1 - g0).Select(i => classes.GenPlatformMinRank[i])],
            HFirst = [.. Af(classes.AfHFirst)], HFloor = [.. Af(classes.AfHFloor)],
            MaterialsFloor = [.. Af(classes.AfMaterialsFloor)], DesignScale = [.. Af(classes.AfDesignScale)],
            ProcScaleAf = [.. Af(classes.AfProcScale)],
            ProcScalePu = [.. Enumerable.Range(u0, u1 - u0).Select(i => classes.PuProcScale[i])],
            CRef = classes.CRef[c], MaxDesigns = maxDesigns,
            HPm = classes.HPm[c], WageKh = classes.WageKh[c],
            Maintainability = classes.Maintainability[c], RRef = classes.RRef[c],
            ClusterCount = industry.Clusters.Length, SkilledCategory = industry.SkilledCategory,
            NodeGen = [.. nodeGen], SiteOwner = industry.SiteOwner, Bureaus = bureaus, StartYear = startYear, Years = years,
        };
    }
}
