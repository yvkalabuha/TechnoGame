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

    /// <summary>«снят»: line closed and every cohort written off.</summary>
    Withdrawn = 5,
}

/// <summary>
/// Immutable shape of the product layer for one product class (spec §1.4, §3.3; contract §4.4, §4.6). Everything
/// is resolved from data once: slots and alternatives with their <c>prc_set</c> as process numbers and
/// <c>requires_nodes</c> as node numbers, process costs, generations. A design slot is a stable id from data:
/// <c>design_id = (gen − 1)·2 + variant</c>, one per (generation, variant) and actor; its production line has the
/// same index. Requester ids (contract §4.6, one id space): skilled requests of line l for cluster c are
/// <c>l·C + c</c> (spec §3.5 A26), then the bureaus, then one defect-fix team per design.
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

    public int Generations => GenApp.Length;

    /// <summary>Per generation g at index g − 1: APP node number and the design inputs.</summary>
    public required ImmutableArray<int> GenApp { get; init; }
    public required ImmutableArray<Fx6> HFirst { get; init; }
    public required ImmutableArray<Fx6> HFloor { get; init; }
    public required ImmutableArray<Fx6> MaterialsFloor { get; init; }
    public required ImmutableArray<Fx6> AlloyKg { get; init; }
    public required ImmutableArray<int> CrewReq { get; init; }
    public required ImmutableArray<Fx6> ProcScale { get; init; }
    public required ImmutableArray<Fx6> DesignScale { get; init; }

    public required Fx6 HPm { get; init; }
    public required Fx6 WageKh { get; init; }
    public required int BureauEngineers { get; init; }
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

    public int Designs => 2 * Generations;

    public static int DesignId(int gen, DesignVariant variant) => (gen - 1) * 2 + (int)variant;

    public static int GenOf(int design) => design / 2 + 1;

    public static DesignVariant VariantOf(int design) => (DesignVariant)(design % 2);

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
        int bureaus, int startYear, int years)
    {
        int c = classes.IndexOf(classId);
        if (c < 0)
            throw new ArgumentException($"Product class '{classId}' is not loaded.", nameof(classId));
        ArgumentOutOfRangeException.ThrowIfLessThan(bureaus, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(years, 1);

        int firstSlot = classes.SlotStart[c], slotCount = classes.SlotStart[c + 1] - firstSlot;
        int baseSlot = -1;
        var altStart = new List<int> { 0 };
        var altId = new List<string>();
        var altRank = new List<int>();
        var altPerf = new List<Fx6>();
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
            for (int a = classes.AltStart[gs]; a < classes.AltStart[gs + 1]; a++)
            {
                altId.Add(classes.AltId[a]);
                altRank.Add(classes.AltRank[a]);
                altPerf.Add(classes.AltPerfMult[a]);
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

        int g0 = classes.GenStart[c], g1 = classes.GenStart[c + 1];
        Fx6[] Gen(ImmutableArray<Fx6> col) => [.. Enumerable.Range(g0, g1 - g0).Select(i => col[i])];
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
            GenApp = [.. Enumerable.Range(g0, g1 - g0).Select(i => nodes.Number[classes.GenApp[i]])],
            HFirst = [.. Gen(classes.GenHFirst)], HFloor = [.. Gen(classes.GenHFloor)],
            MaterialsFloor = [.. Gen(classes.GenMaterialsFloor)], AlloyKg = [.. Gen(classes.GenAlloyKg)],
            CrewReq = [.. Enumerable.Range(g0, g1 - g0).Select(i => classes.GenCrewReq[i])],
            ProcScale = [.. Gen(classes.GenProcScale)], DesignScale = [.. Gen(classes.GenDesignScale)],
            HPm = classes.HPm[c], WageKh = classes.WageKh[c], BureauEngineers = classes.BureauEngineers[c],
            Maintainability = classes.Maintainability[c], RRef = classes.RRef[c],
            ClusterCount = industry.Clusters.Length, SkilledCategory = industry.SkilledCategory,
            SiteOwner = industry.SiteOwner, Bureaus = bureaus, StartYear = startYear, Years = years,
        };
    }
}
