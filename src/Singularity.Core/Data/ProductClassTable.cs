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
    public required ImmutableArray<int> AltStart { get; init; }

    public required ImmutableArray<string> AltId { get; init; }
    public required ImmutableArray<int> AltSlot { get; init; }
    public required ImmutableArray<int> AltRank { get; init; }
    public required ImmutableArray<Fx6> AltPerfMult { get; init; }
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

    /// <summary>Generation rows of class c: <c>GenStart[c]..GenStart[c+1]</c>, row i is generation i + 1.</summary>
    public required ImmutableArray<int> GenStart { get; init; }
    public required ImmutableArray<int> GenApp { get; init; }
    public required ImmutableArray<Fx6> GenHFirst { get; init; }
    public required ImmutableArray<Fx6> GenHFloor { get; init; }
    public required ImmutableArray<Fx6> GenMaterialsFloor { get; init; }
    public required ImmutableArray<Fx6> GenAlloyKg { get; init; }
    public required ImmutableArray<int> GenCrewReq { get; init; }

    /// <summary>R-PS: multiplier of the process part of <c>unit_cost</c> of generation g.</summary>
    public required ImmutableArray<Fx6> GenProcScale { get; init; }

    /// <summary>R-DS: multiplier of design work <c>W</c> of generation g.</summary>
    public required ImmutableArray<Fx6> GenDesignScale { get; init; }
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
                w.Int(AltStart[s + 1] - AltStart[s]);
                for (int a = AltStart[s]; a < AltStart[s + 1]; a++)
                {
                    w.Str(AltId[a]);
                    w.Int(AltRank[a]);
                    w.Fixed(AltPerfMult[a]);
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
            w.Int(GenStart[c + 1] - GenStart[c]);
            for (int i = GenStart[c]; i < GenStart[c + 1]; i++)
            {
                w.Str(nodes.Id[GenApp[i]]);
                w.Fixed(GenHFirst[i]);
                w.Fixed(GenHFloor[i]);
                w.Fixed(GenMaterialsFloor[i]);
                w.Fixed(GenAlloyKg[i]);
                w.Int(GenCrewReq[i]);
                w.Fixed(GenProcScale[i]);
                w.Fixed(GenDesignScale[i]);
            }
        }
    }
}
