using Singularity.Core.Data.Yaml;
using Singularity.Kernel;

namespace Singularity.Core.Data;

public sealed partial class ScenarioData
{
    /// <summary>Accumulates actor columns in data order and checks each actor.</summary>
    private sealed class ActorColumns(Cluster[] clusters, Discipline[] disciplines)
    {
        public readonly List<string> Ids = new();
        public readonly List<ActorClass> Classes = new();
        public readonly List<Fx6> Gdp = new(), GdpPerCapita = new(), Budget = new(), Growth = new();
        public readonly List<Fx6> CultureH = new(), ToolingM = new(), Hcap = new(), W = new(), KD = new(), KInd = new();
        public readonly List<int> GdpYear = new(), Trophy = new();
        public readonly List<int> KnownStart = new() { 0 }, Known = new(), MasteredStart = new() { 0 }, Mastered = new();
        public readonly List<int> FleetStart = new() { 0 }, FleetClass = new(), FleetGen = new(), FleetCount = new(), FleetCohort = new();
        public readonly List<DesignVariant> FleetVariant = new();
        public readonly List<int> EstimatedStart = new() { 0 };
        public readonly List<string> Estimated = new();
        public readonly List<(YNode At, string Field)> EstimatedRefs = new();

        public void Read(YNode node, NodeTable nodes, ProductClassTable classes)
        {
            var m = new MapReader(node);
            string id = m.Str("id");
            if (id.Length != 3 || !id.All(char.IsAsciiLetterUpper))
                throw DataLoadException.At(node, $"actor ID '{id}' must be three upper-case letters (ISO 3166 style)");
            if (Ids.Contains(id))
                throw DataLoadException.At(node, $"duplicate actor ID '{id}'");
            Ids.Add(id);
            Classes.Add(m.Enum("class", DataNames.ActorClasses));
            Gdp.Add(Positive(m, "gdp_1945"));
            GdpYear.Add(m.Int("gdp_year"));
            GdpPerCapita.Add(Positive(m, "gdppc_1945"));
            Budget.Add(Positive(m, "budget_index"));
            Growth.Add(m.Fixed("growth_pa"));
            ReadByDims(m.Required("culture_h"), CultureH, "culture_h");
            ReadByDims(m.Required("tooling_m"), ToolingM, "tooling_m"); // per cell (spec §3.5 A20)
            Hcap.Add(NonNegative(m, "hcap"));
            W.Add(NonNegative(m, "w"));
            ReadKeyed(m.Required("k_d"), disciplines, DataNames.Disciplines, KD, "k_d");
            KInd.Add(NonNegative(m, "k_ind"));

            bool[] known = ReadNodeSet(m.Seq("known"), nodes, Known, KnownStart, "known");
            // Start knowledge is closed under requires (TASK-0011 check; F-SC1).
            for (int r = 0; r < nodes.Count; r++)
            {
                if (known[r] && !nodes.Requires.IsSatisfied(r, known))
                    throw DataLoadException.At(node, $"actor {id} knows '{nodes.Id[r]}' but not its requires");
            }
            bool[] mastered = ReadNodeSet(m.Seq("mastered"), nodes, Mastered, MasteredStart, "mastered");
            for (int r = 0; r < nodes.Count; r++)
            {
                if (mastered[r] && (!known[r] || nodes.Type[r] != NodeType.PRC))
                    throw DataLoadException.At(node, $"actor {id}: mastered '{nodes.Id[r]}' must be a known PRC node");
            }

            ReadFleet(m.Seq("fleet"), classes);
            int trophy = m.Int("trophy_engineers");
            if (trophy < 0)
                throw DataLoadException.At(node, "trophy_engineers must not be negative");
            Trophy.Add(trophy);
            ReadEstimated(m.Seq("estimated"));
            m.End();
        }

        private void ReadByDims(YNode node, List<Fx6> target, string what)
        {
            var map = new MapReader(node);
            foreach (Cluster c in clusters)
            {
                string name = DataNames.NameOf(DataNames.Clusters, c);
                var dims = new MapReader(map.Optional(name) ?? throw DataLoadException.At(node, $"{what} is missing cluster {name}"));
                foreach ((string dimName, Dimension _) in DataNames.Dimensions)
                    target.Add(NonNegative(dims, dimName));
                dims.End();
            }
            map.End();
        }

        private static void ReadKeyed<T>(YNode node, T[] keys, (string Name, T Value)[] names, List<Fx6> target, string what)
            where T : struct, Enum
        {
            var map = new MapReader(node);
            foreach (T key in keys)
            {
                string name = DataNames.NameOf(names, key);
                target.Add(Scalars.Fixed(map.Optional(name) ?? throw DataLoadException.At(node, $"{what} is missing {name}")));
                if (target[^1] < Fx6.Zero)
                    throw DataLoadException.At(node, $"{what}.{name} must not be negative");
            }
            map.End();
        }

        private static bool[] ReadNodeSet(YSeq seq, NodeTable nodes, List<int> target, List<int> starts, string what)
        {
            bool[] set = new bool[nodes.Count];
            foreach (YNode item in seq.Items)
            {
                string nodeId = Scalars.Str(item);
                int row = nodes.IndexOf(nodeId);
                if (row < 0)
                    throw DataLoadException.At(item, $"{what}: reference to unknown node '{nodeId}'");
                if (set[row])
                    throw DataLoadException.At(item, $"{what} lists '{nodeId}' twice");
                set[row] = true;
            }
            for (int r = 0; r < set.Length; r++)
            {
                if (set[r])
                    target.Add(r);
            }
            starts.Add(target.Count);
            return set;
        }

        private void ReadFleet(YSeq seq, ProductClassTable classes)
        {
            foreach (YNode item in seq.Items)
            {
                var f = new MapReader(item);
                YNode classNode = f.Required("product_class");
                int row = classes.IndexOf(Scalars.Str(classNode));
                if (row < 0)
                    throw DataLoadException.At(classNode, $"fleet: unknown product class '{Scalars.Str(classNode)}'");
                FleetClass.Add(row);
                FleetGen.Add(f.Int("gen"));
                FleetVariant.Add(f.Enum("variant", DataNames.Variants));
                int count = f.Int("count");
                if (count < 1)
                    throw DataLoadException.At(item, "fleet count must be at least 1");
                FleetCount.Add(count);
                FleetCohort.Add(f.Int("cohort_year"));
                f.End();
            }
            FleetStart.Add(FleetClass.Count);
        }

        private void ReadEstimated(YSeq seq)
        {
            var fields = new List<string>();
            foreach (YNode item in seq.Items)
            {
                string field = Scalars.Str(item);
                if (Array.IndexOf(ActorFields, field) < 0 || fields.Contains(field))
                    throw DataLoadException.At(item, $"estimated: '{field}' is not an actor field or is listed twice");
                fields.Add(field);
                EstimatedRefs.Add((item, field));
            }
            fields.Sort(StringComparer.Ordinal);
            Estimated.AddRange(fields);
            EstimatedStart.Add(Estimated.Count);
        }

        private static Fx6 Positive(MapReader m, string key)
        {
            Fx6 v = m.Fixed(key);
            return v > Fx6.Zero ? v : throw DataLoadException.At(m.Required(key), $"{key} must be positive");
        }

        private static Fx6 NonNegative(MapReader m, string key)
        {
            Fx6 v = m.Fixed(key);
            return v >= Fx6.Zero ? v : throw DataLoadException.At(m.Required(key), $"{key} must not be negative");
        }
    }
}
