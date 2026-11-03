using Singularity.Kernel;

namespace Singularity.Core.Data;

public sealed partial class NodeTable
{
    /// <summary>Cross-node rules: slice spec §1.3, RR-0008 §3, LAYER_CONTRACT §2.</summary>
    private void Validate(List<RawNode> raw)
    {
        for (int r = 0; r < Count; r++)
        {
            for (int g = Requires.GroupStart[r]; g < Requires.GroupStart[r + 1]; g++)
            {
                foreach (int parent in Requires.GroupMembers(g))
                {
                    if (parent == r)
                        throw DataLoadException.At(raw[r].Map, $"node '{Id[r]}' requires itself");
                    if (Gen[parent] > Gen[r])
                        throw DataLoadException.At(raw[r].Map, $"node '{Id[r]}' (G{Gen[r]}) requires '{Id[parent]}' of a later generation G{Gen[parent]}");
                }
            }
            for (int e = InformsStart[r]; e < InformsStart[r + 1]; e++)
            {
                int to = InformsTo[e];
                if (Type[r] != NodeType.FND || Type[to] is not (NodeType.ENG or NodeType.PRC) || Discipline[to] != Discipline[r])
                    throw DataLoadException.At(raw[r].Map, $"informs edge from '{Id[r]}' to '{Id[to]}' must go from FND to ENG/PRC of the same discipline");
                if (InformsWeight[e] <= Fx6.Zero || InformsWeight[e] > Fx6.One)
                    throw DataLoadException.At(raw[r].Map, $"informs weight {InformsWeight[e]} must be in (0, 1]");
            }
        }
        CheckAcyclic(raw);
    }

    private void CheckAcyclic(List<RawNode> raw)
    {
        // 0 = unvisited, 1 = on the current path, 2 = done. Iterative DFS in row order.
        byte[] state = new byte[Count];
        for (int start = 0; start < Count; start++)
        {
            if (state[start] != 0)
                continue;
            var stack = new Stack<(int Row, int NextMember)>();
            stack.Push((start, Requires.MemberStart[Requires.GroupStart[start]]));
            state[start] = 1;
            while (stack.Count > 0)
            {
                (int row, int next) = stack.Pop();
                int end = Requires.MemberStart[Requires.GroupStart[row + 1]];
                if (next == end)
                {
                    state[row] = 2;
                    continue;
                }
                stack.Push((row, next + 1));
                int parent = Requires.Members[next];
                if (state[parent] == 1)
                    throw DataLoadException.At(raw[row].Map, $"requires cycle through '{Id[row]}' and '{Id[parent]}'");
                if (state[parent] == 0)
                {
                    state[parent] = 1;
                    stack.Push((parent, Requires.MemberStart[Requires.GroupStart[parent]]));
                }
            }
        }
    }

    // Enums are written by their data names, so reordering a C# enum does not change DataHash.
    internal void WriteCanonical(CanonicalWriter w)
    {
        w.Str("nodes");
        w.Int(Count);
        for (int r = 0; r < Count; r++)
        {
            w.Str(Id[r]);
            w.Int(Number[r]);
            w.Str(DataNames.NameOf(DataNames.NodeTypes, Type[r]));
            w.Int(Gen[r]);
            w.Str(DataNames.NameOf(DataNames.Disciplines, Discipline[r]));
            w.Str(DataNames.NameOf(DataNames.Clusters, Cluster[r]));
            w.Int(Tier[r]);
            w.Fixed(ReqPrecision[r]);
            w.Fixed(ReqPurity[r]);
            w.Fixed(ReqQualityControl[r]);
            w.Str(DataNames.NameOf(DataNames.Hazards, Hazard[r]));
            Requires.WriteCanonical(w, r, this);
            w.Int(InformsStart[r + 1] - InformsStart[r]);
            for (int e = InformsStart[r]; e < InformsStart[r + 1]; e++)
            {
                w.Str(Id[InformsTo[e]]);
                w.Fixed(InformsWeight[e]);
            }
            w.Int(AnchorYear[r]);
            w.Bool(AnchorBefore[r]);
            w.Str(AnchorEvent[r]);
            w.Str(AnchorSource[r]);
            w.Bool(AnchorEstimated[r]);
        }
    }
}
