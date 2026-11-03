using Singularity.Core.Data.Yaml;

namespace Singularity.Core.Data;

/// <summary>
/// Raw objects of one kind keyed by ID, after define/override/patch/delete (ADR-0010).
/// Ordered by ID (ordinal), so later stages see a deterministic order (ADR-0007).
/// Conflict rule: a mod may change an existing object only if it depends (transitively) on
/// every mod that touched the object before; otherwise the result would depend on an arbitrary
/// load order. A mod may touch an ID only once.
/// </summary>
internal sealed class ObjectStore(string kind)
{
    private sealed class Entry
    {
        public YMap? Map;   // null: deleted
        public readonly List<string> TouchedBy = new();
    }

    private readonly SortedDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public string Kind { get; } = kind;

    public void Define(YMap map, string id, ModContext mod)
    {
        if (_entries.TryGetValue(id, out Entry? existing))
        {
            string what = existing.Map is null ? "was deleted by" : "is already defined by";
            throw DataLoadException.At(map, $"duplicate {Kind} ID '{id}': it {what} mod '{existing.TouchedBy[^1]}'");
        }
        var entry = new Entry { Map = map };
        entry.TouchedBy.Add(mod.Id);
        _entries.Add(id, entry);
    }

    public void Override(YMap map, string id, ModContext mod) => Touch(map, id, mod, "override").Map = map;

    public void Patch(YMap patch, string id, ModContext mod)
    {
        Entry entry = Touch(patch, id, mod, "patch");
        if (patch.Entries.Count < 2)
            throw DataLoadException.At(patch, $"patch of '{id}' changes nothing");
        entry.Map = Merge(entry.Map!, patch);
    }

    public void Delete(YNode at, string id, ModContext mod) => Touch(at, id, mod, "delete").Map = null;

    /// <summary>Live objects in ID order.</summary>
    public List<(string Id, YMap Map)> Objects()
    {
        var result = new List<(string, YMap)>();
        foreach (KeyValuePair<string, Entry> pair in _entries)
        {
            if (pair.Value.Map is {} map)
                result.Add((pair.Key, map));
        }
        return result;
    }

    private Entry Touch(YNode at, string id, ModContext mod, string op)
    {
        if (!_entries.TryGetValue(id, out Entry? entry) || entry.Map is null)
            throw DataLoadException.At(at, $"{op} of unknown {Kind} ID '{id}'");
        foreach (string previous in entry.TouchedBy)
        {
            if (previous == mod.Id)
                throw DataLoadException.At(at, $"{Kind} ID '{id}' is changed twice in mod '{mod.Id}'");
            if (!mod.DependsOn(previous))
                throw DataLoadException.At(at, $"conflict on {Kind} ID '{id}': mod '{mod.Id}' changes it but does not depend on mod '{previous}'");
        }
        entry.TouchedBy.Add(mod.Id);
        return entry;
    }

    // Shallow merge: a patched top-level field replaces the old value as a whole.
    private static YMap Merge(YMap target, YMap patch)
    {
        var entries = new List<KeyValuePair<string, YNode>>(target.Entries.Count + patch.Entries.Count);
        foreach (KeyValuePair<string, YNode> old in target.Entries)
            entries.Add(patch.Find(old.Key) is {} replaced ? new(old.Key, replaced) : old);
        foreach (KeyValuePair<string, YNode> added in patch.Entries)
        {
            if (target.Find(added.Key) is null)
                entries.Add(added);
        }
        return new YMap(patch.File, patch.Line, entries);
    }
}

/// <summary>The mod being applied and its transitive dependencies.</summary>
internal sealed class ModContext(string id, SortedSet<string> transitiveDependencies)
{
    public string Id { get; } = id;

    public bool DependsOn(string modId) => transitiveDependencies.Contains(modId);
}
