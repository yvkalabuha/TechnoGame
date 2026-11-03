using System.Collections.Immutable;
using Singularity.Kernel;

namespace Singularity.Core.Data;

/// <summary>
/// Product classes (slice spec §1.4) as three linked SoA tables: classes, slots, alternatives.
/// Classes are ordered by ID; slots keep data order inside a class; alternatives ascend by rank
/// inside a slot. Ranges: class c owns slots <c>SlotStart[c]..SlotStart[c+1]</c>; slot s owns
/// alternatives <c>AltStart[s]..AltStart[s+1]</c>; alternative a uses PRC node rows
/// <c>Prc[PrcStart[a]..PrcStart[a+1]]</c> (ascending) and needs <see cref="AltRequires"/>.
/// </summary>
public sealed partial class ProductClassTable
{
    public int Count => ClassId.Length;

    public required ImmutableArray<string> ClassId { get; init; }
    public required ImmutableArray<int> SlotStart { get; init; }

    /// <summary>Lighten order of class c (F-PC2): global slot indices in <c>LightenOrder[LightenStart[c]..LightenStart[c+1]]</c>.</summary>
    public required ImmutableArray<int> LightenStart { get; init; }
    public required ImmutableArray<int> LightenOrder { get; init; }

    public required ImmutableArray<string> SlotId { get; init; }
    public required ImmutableArray<int> SlotClass { get; init; }
    public required ImmutableArray<bool> SlotIsBase { get; init; }

    /// <summary>R-PS2m block of the slot: true = AF (airframe, exactly one per class), false = PU.</summary>
    public required ImmutableArray<bool> SlotIsAirframe { get; init; }
    public required ImmutableArray<int> AltStart { get; init; }

    public required ImmutableArray<string> AltId { get; init; }
    public required ImmutableArray<int> AltSlot { get; init; }
    public required ImmutableArray<int> AltRank { get; init; }
    public required ImmutableArray<Fx6> AltPerfMult { get; init; }

    /// <summary><c>eng_gen</c> of an engine alternative (1..G_max), 0 for other slots (contract §4.4a, G4-9).</summary>
    public required ImmutableArray<int> AltEngGen { get; init; }

    /// <summary><c>af_class</c> of an airframe alternative, 0 for other slots.</summary>
    public required ImmutableArray<int> AltAfClass { get; init; }

    /// <summary><c>alloy_kg</c> of an alternative of the alloy slot (hot section), zero for other slots (R-AK2, A23).</summary>
    public required ImmutableArray<Fx6> AltAlloyKg { get; init; }
    public required ImmutableArray<int> PrcStart { get; init; }
    public required ImmutableArray<int> Prc { get; init; }
    public required RequirementLists AltRequires { get; init; }

    // ---- product-layer inputs per class (product_inputs.md; spec §3.3, contract §4.4)

    /// <summary>Hours per person-month (R-HPM).</summary>
    public required ImmutableArray<Fx6> HPm { get; init; }

    /// <summary>Full labour rate in index per 1000 hours (R-WAGE, F-PI1).</summary>
    public required ImmutableArray<Fx6> WageKh { get; init; }
    public required ImmutableArray<Fx6> Maintainability { get; init; }

    /// <summary>Rate of the reference line P0 (spec §3.5 A18), units per month.</summary>
    public required ImmutableArray<Fx6> RRef { get; init; }

    /// <summary>Process cost rows of class c: <c>ProcStart[c]..ProcStart[c+1]</c>, ascending node row.</summary>
    public required ImmutableArray<int> ProcStart { get; init; }
    public required ImmutableArray<int> ProcNode { get; init; }
    public required ImmutableArray<Fx6> ProcN { get; init; }
    public required ImmutableArray<Fx6> ProcLabour { get; init; }
    public required ImmutableArray<Fx6> ProcBaseCost { get; init; }

    /// <summary><c>c_ref</c>: complexity of the reference superior G2 project; <c>D_ref = d0·c_ref</c> (contract §4.4b).</summary>
    public required ImmutableArray<int> CRef { get; init; }

    /// <summary>R-PS2m <c>proc_scale_pu[r]</c> rows of class c: <c>PuStart[c]..PuStart[c+1]</c>, row i is rank i + 1.</summary>
    public required ImmutableArray<int> PuStart { get; init; }
    public required ImmutableArray<Fx6> PuProcScale { get; init; }

    /// <summary>Airframe-class rows of class c: <c>AfStart[c]..AfStart[c+1]</c>, row i is <c>af_class</c> i + 1 (contract §4.4a).</summary>
    public required ImmutableArray<int> AfStart { get; init; }
    public required ImmutableArray<Fx6> AfHFirst { get; init; }
    public required ImmutableArray<Fx6> AfHFloor { get; init; }
    public required ImmutableArray<Fx6> AfMaterialsFloor { get; init; }
    public required ImmutableArray<Fx6> AfDesignScale { get; init; }
    public required ImmutableArray<Fx6> AfProcScale { get; init; }

    /// <summary>Generation rows of class c: <c>GenStart[c]..GenStart[c+1]</c>, row i is generation i + 1.</summary>
    public required ImmutableArray<int> GenStart { get; init; }
    public required ImmutableArray<int> GenApp { get; init; }
    public required ImmutableArray<int> GenCrewReq { get; init; }

    /// <summary><c>platform_min_rank[g]</c>: minimum airframe rank of platform generation g (contract §4.4a).</summary>
    public required ImmutableArray<int> GenPlatformMinRank { get; init; }

    /// <summary>Process cost row of a PRC node row in class c, or -1.</summary>
    public int ProcessRow(int classRow, int nodeRow)
    {
        for (int i = ProcStart[classRow]; i < ProcStart[classRow + 1]; i++)
        {
            if (ProcNode[i] == nodeRow)
                return i;
        }
        return -1;
    }

    /// <summary>Row of a class ID, or -1 (classes are sorted by ID).</summary>
    public int IndexOf(string classId)
    {
        int i = ClassId.AsSpan().BinarySearch(classId, StringComparer.Ordinal);
        return i >= 0 ? i : -1;
    }

    /// <summary>Global alternative index of (class, slot ID, alternative ID), or -1.</summary>
    public int AlternativeIndex(int classRow, string slotId, string altId)
    {
        for (int s = SlotStart[classRow]; s < SlotStart[classRow + 1]; s++)
        {
            if (SlotId[s] != slotId)
                continue;
            for (int a = AltStart[s]; a < AltStart[s + 1]; a++)
            {
                if (AltId[a] == altId)
                    return a;
            }
        }
        return -1;
    }

    public ReadOnlySpan<int> PrcSet(int alt) => Prc.AsSpan()[PrcStart[alt]..PrcStart[alt + 1]];

    internal void WriteCanonical(CanonicalWriter w, NodeTable nodes)
    {
        w.Str("product_classes");
        w.Int(Count);
        for (int c = 0; c < Count; c++)
        {
            w.Str(ClassId[c]);
            w.Int(LightenStart[c + 1] - LightenStart[c]);
            for (int i = LightenStart[c]; i < LightenStart[c + 1]; i++)
                w.Str(SlotId[LightenOrder[i]]);
            w.Int(SlotStart[c + 1] - SlotStart[c]);
            for (int s = SlotStart[c]; s < SlotStart[c + 1]; s++)
            {
                w.Str(SlotId[s]);
                w.Bool(SlotIsBase[s]);
                w.Bool(SlotIsAirframe[s]);
                w.Int(AltStart[s + 1] - AltStart[s]);
                for (int a = AltStart[s]; a < AltStart[s + 1]; a++)
                {
                    w.Str(AltId[a]);
                    w.Int(AltRank[a]);
                    w.Fixed(AltPerfMult[a]);
                    w.Int(AltEngGen[a]);
                    w.Int(AltAfClass[a]);
                    w.Fixed(AltAlloyKg[a]);
                    w.Int(PrcStart[a + 1] - PrcStart[a]);
                    foreach (int p in PrcSet(a))
                        w.Str(nodes.Id[p]);
                    AltRequires.WriteCanonical(w, a, nodes);
                }
            }
            w.Fixed(HPm[c]);
            w.Fixed(WageKh[c]);
            w.Fixed(Maintainability[c]);
            w.Fixed(RRef[c]);
            w.Int(ProcStart[c + 1] - ProcStart[c]);
            for (int i = ProcStart[c]; i < ProcStart[c + 1]; i++)
            {
                w.Str(nodes.Id[ProcNode[i]]);
                w.Fixed(ProcN[i]);
                w.Fixed(ProcLabour[i]);
                w.Fixed(ProcBaseCost[i]);
            }
            w.Int(CRef[c]);
            w.Int(PuStart[c + 1] - PuStart[c]);
            for (int i = PuStart[c]; i < PuStart[c + 1]; i++)
                w.Fixed(PuProcScale[i]);
            w.Int(AfStart[c + 1] - AfStart[c]);
            for (int i = AfStart[c]; i < AfStart[c + 1]; i++)
            {
                w.Fixed(AfHFirst[i]);
                w.Fixed(AfHFloor[i]);
                w.Fixed(AfMaterialsFloor[i]);
                w.Fixed(AfDesignScale[i]);
                w.Fixed(AfProcScale[i]);
            }
            w.Int(GenStart[c + 1] - GenStart[c]);
            for (int i = GenStart[c]; i < GenStart[c + 1]; i++)
            {
                w.Str(nodes.Id[GenApp[i]]);
                w.Int(GenCrewReq[i]);
                w.Int(GenPlatformMinRank[i]);
            }
        }
    }
}
