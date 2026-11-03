using System.Collections.Immutable;
using Singularity.Core.Data.Yaml;

namespace Singularity.Core.Data;

/// <summary>
/// Loads mods (ADR-0010) into immutable tables. Order is fully determined by the data:
/// mods in dependency order with ties broken by mod ID (ordinal), files in manifest order,
/// objects in file order; tables are then sorted by stable keys (ADR-0007).
/// Every inconsistency is a <see cref="DataLoadException"/>.
/// </summary>
public static class ModLoader
{
    public const string NodeKind = "node";
    public const string ProductClassKind = "product_class";
    public const string ScenarioKind = "scenario";

    public static GameData Load(params string[] modDirectories)
    {
        ImmutableArray<ModManifest> mods = ResolveOrder(modDirectories.Select(ModManifest.Read).ToList());

        var nodes = new ObjectStore(NodeKind);
        var classes = new ObjectStore(ProductClassKind);
        var scenarios = new ObjectStore(ScenarioKind);
        foreach (ModManifest mod in mods)
        {
            CheckUnlistedFiles(mod);
            var context = new ModContext(mod.Id, TransitiveDependencies(mod, mods));
            foreach (string file in mod.Files)
            {
                string path = Path.Combine(mod.Directory, file);
                if (!File.Exists(path))
                    throw new DataLoadException($"{path}: file listed in {mod.Id}/mod.yaml does not exist");
                YNode root = StrictYaml.Parse(File.ReadAllText(path), path);
                ApplyFile(root, context, nodes, classes, scenarios);
            }
        }

        NodeTable nodeTable = NodeTable.Build(nodes.Objects());
        ProductClassTable classTable = ProductClassTable.Build(classes.Objects(), nodeTable);
        ImmutableArray<ScenarioData> scenarioList =
            [.. scenarios.Objects().Select(o => ScenarioData.Build(o.Map, nodeTable, classTable))];
        return new GameData(mods, nodeTable, classTable, scenarioList);
    }

    private static void ApplyFile(YNode root, ModContext mod, ObjectStore nodes, ObjectStore classes, ObjectStore scenarios)
    {
        var file = new MapReader(root);
        YNode kindNode = file.Required("kind");
        ObjectStore store = Scalars.Str(kindNode) switch
        {
            NodeKind => nodes,
            ProductClassKind => classes,
            ScenarioKind => scenarios,
            var other => throw DataLoadException.At(kindNode, $"unknown kind '{other}'"),
        };

        // Operation order inside a file is fixed: define, override, patch, delete.
        if (file.Optional("define") is {} define)
            foreach (YNode item in Scalars.Seq(define).Items)
                store.Define(Scalars.Map(item), ObjectId(item), mod);
        if (file.Optional("override") is {} over)
            foreach (YNode item in Scalars.Seq(over).Items)
                store.Override(Scalars.Map(item), ObjectId(item), mod);
        if (file.Optional("patch") is {} patch)
            foreach (YNode item in Scalars.Seq(patch).Items)
                store.Patch(Scalars.Map(item), ObjectId(item), mod);
        if (file.Optional("delete") is {} delete)
            foreach (YNode item in Scalars.Seq(delete).Items)
                store.Delete(item, Scalars.Str(item), mod);
        file.End();
    }

    private static string ObjectId(YNode item)
    {
        YNode id = Scalars.Map(item).Find("id") ?? throw DataLoadException.At(item, "missing field 'id'");
        return Scalars.Str(id);
    }

    /// <summary>Topological order (Kahn); among ready mods the smallest ID goes first.</summary>
    private static ImmutableArray<ModManifest> ResolveOrder(List<ModManifest> manifests)
    {
        var byId = new SortedDictionary<string, ModManifest>(StringComparer.Ordinal);
        foreach (ModManifest m in manifests)
        {
            if (!byId.TryAdd(m.Id, m))
                throw new DataLoadException($"{m.Directory}: duplicate mod ID '{m.Id}'");
        }
        foreach (ModManifest m in manifests)
        {
            foreach (ModDependency dep in m.Dependencies)
            {
                if (!byId.TryGetValue(dep.Id, out ModManifest? target))
                    throw new DataLoadException($"{m.Directory}: mod '{m.Id}' depends on missing mod '{dep.Id}'");
                if (!dep.Accepts(target.Version))
                    throw new DataLoadException($"{m.Directory}: mod '{m.Id}' needs '{dep.Id}' {Range(dep)}, found {target.Version}");
            }
        }

        var order = ImmutableArray.CreateBuilder<ModManifest>(manifests.Count);
        var placed = new SortedSet<string>(StringComparer.Ordinal);
        while (order.Count < manifests.Count)
        {
            ModManifest? next = null;
            foreach (ModManifest candidate in byId.Values)
            {
                if (!placed.Contains(candidate.Id) && candidate.Dependencies.All(d => placed.Contains(d.Id)))
                {
                    next = candidate;
                    break;
                }
            }
            if (next is null)
                throw new DataLoadException("mod dependency cycle among: " +
                    string.Join(", ", byId.Keys.Where(id => !placed.Contains(id))));
            placed.Add(next.Id);
            order.Add(next);
        }
        return order.MoveToImmutable();
    }

    private static SortedSet<string> TransitiveDependencies(ModManifest mod, ImmutableArray<ModManifest> ordered)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>(mod.Dependencies.Select(d => d.Id));
        while (stack.Count > 0)
        {
            string id = stack.Pop();
            if (!result.Add(id))
                continue;
            foreach (ModManifest m in ordered)
            {
                if (m.Id == id)
                    foreach (ModDependency d in m.Dependencies)
                        stack.Push(d.Id);
            }
        }
        return result;
    }

    // A data file present but not listed would be silently ignored; make it loud.
    // Only the .yaml extension is valid; a *.yml file is always an error (likely a typo).
    private static void CheckUnlistedFiles(ModManifest mod)
    {
        // Exact extension check: on Windows the pattern "*.yml" would also match *.yaml.
        string[] yml = [.. Directory.GetFiles(mod.Directory).Where(f => Path.GetExtension(f) == ".yml")];
        Array.Sort(yml, StringComparer.Ordinal);
        if (yml.Length > 0)
            throw new DataLoadException($"{yml[0]}: mod files must use the .yaml extension");

        string[] present = Directory.GetFiles(mod.Directory, "*.yaml");
        Array.Sort(present, StringComparer.Ordinal);
        foreach (string path in present)
        {
            string name = Path.GetFileName(path);
            if (name != ModManifest.FileName && !mod.Files.Contains(name))
                throw new DataLoadException($"{path}: data file is not listed in {mod.Id}/mod.yaml");
        }
    }

    private static string Range(ModDependency dep) =>
        dep.Below is {} below ? $"in [{dep.Min}, {below})" : $">= {dep.Min}";
}
