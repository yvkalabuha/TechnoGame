using Singularity.Core.Data.Yaml;
using Singularity.Kernel;

namespace Singularity.Core.Data;

public sealed partial class NodeTable
{
    /// <summary>One node's fields as read from YAML, before references are resolved.</summary>
    private sealed class RawNode
    {
        public required YMap Map;
        public required string Id;
        public required YSeq Requires;
        public YSeq? Informs;
        public int Number, Gen, Tier, AnchorYear;
        public NodeType Type;
        public Discipline Discipline;
        public Cluster Cluster;
        public Hazard Hazard;
        public readonly Fx6[] Req = new Fx6[3];
        public bool AnchorBefore, AnchorEstimated;
        public string AnchorEvent = "", AnchorSource = "";

        public static RawNode Read(YMap map)
        {
            var m = new MapReader(map);
            var n = new RawNode { Map = map, Id = m.Str("id"), Requires = m.Seq("requires") };
            if (!IsNodeId(n.Id))
                throw DataLoadException.At(map, $"node ID '{n.Id}' must look like sN.type.name (e.g. s8.eng.turbojet_g1)");
            n.Number = m.Int("number");
            if (n.Number < 1)
                throw DataLoadException.At(map, "node number must be at least 1 (0 is reserved, spec §3.5 A7)");
            n.Type = m.Enum("type", DataNames.NodeTypes);
            if (!n.Id.Contains($".{n.Type.ToString().ToLowerInvariant()}.", StringComparison.Ordinal))
                throw DataLoadException.At(map, $"node ID '{n.Id}' does not match its type {n.Type}");
            n.Gen = m.Int("gen");
            if (n.Gen is < 0 or > 9)
                throw DataLoadException.At(map, "gen must be in 0..9");
            n.Discipline = m.Enum("discipline", DataNames.Disciplines);
            n.Cluster = m.Enum("cluster", DataNames.Clusters);
            n.Hazard = m.Enum("hazard", DataNames.Hazards);
            n.Informs = m.Optional("informs") is {} inf ? Scalars.Seq(inf) : null;
            ReadCapability(m, n);
            ReadAnchor(m.Required("anchor"), n);
            m.End();
            return n;
        }

        private static void ReadCapability(MapReader m, RawNode n)
        {
            YNode? tier = m.Optional("tier");
            YNode? req = m.Optional("capability_req");
            if (n.Type != NodeType.PRC)
            {
                if (tier is not null || req is not null)
                    throw DataLoadException.At(n.Map, $"only PRC nodes have tier and capability_req ('{n.Id}' is {n.Type})");
                return;
            }
            if (tier is null || req is null)
                throw DataLoadException.At(n.Map, $"PRC node '{n.Id}' needs tier and capability_req");
            n.Tier = Scalars.Int(tier);
            if (n.Tier is < 1 or > 9)
                throw DataLoadException.At(tier, "tier must be in 1..9");

            // Spec §1.3 (RR-0006 §4.3): key dimensions need k..k+0.5, other needed ones at least k-1.
            Fx6 k = Fx6.FromInt(n.Tier), half = Fx6.FromRaw(Fx6.Scale / 2);
            var reqMap = new MapReader(req);
            bool hasKey = false;
            foreach ((string name, Dimension dim) in DataNames.Dimensions)
            {
                if (reqMap.Optional(name) is not {} value)
                    continue;
                Fx6 v = Scalars.Fixed(value);
                if (v < k - Fx6.One || v > k + half)
                    throw DataLoadException.At(value, $"capability_req.{name} = {v} is outside [tier - 1, tier + 0.5] for tier {n.Tier}");
                hasKey |= v >= k;
                n.Req[(int)dim] = v;
            }
            reqMap.End();
            if (!hasKey)
                throw DataLoadException.At(req, $"capability_req of '{n.Id}' has no key dimension (at least tier {n.Tier})");
        }

        private static void ReadAnchor(YNode node, RawNode n)
        {
            var a = new MapReader(node);
            n.AnchorYear = a.Int("year");
            n.AnchorBefore = a.Optional("before") is {} b && Scalars.Bool(b);
            n.AnchorEvent = a.Str("event");
            n.AnchorSource = a.Str("source");
            n.AnchorEstimated = a.Bool("estimated");
            a.End();
        }

        private static bool IsNodeId(string id)
        {
            string[] parts = id.Split('.');
            return parts.Length == 3 && parts[0].Length > 1 && parts[0][0] == 's' && parts[0][1..].All(char.IsAsciiDigit) &&
                   parts[2].Length > 0 && parts[2].All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_');
        }
    }
}
