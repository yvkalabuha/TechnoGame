using System.Text;

namespace Singularity.Localization;

/// <summary>
/// Stale-translation tracking (ADR-0018 §3.4). The source language is <c>en</c>. A non-source entity is
/// stale when its mod's <c>_sources.tsv</c> has no line for it or the stamped hash differs from the hash of the
/// current EN entity. Pontoon and Weblate rewrite comments in .ftl but never touch the sidecar.
/// </summary>
public static class Staleness
{
    /// <summary>Hash of an entity as stamped: 16 hex of SHA-256 over its normalised source text.</summary>
    public static string HashOf(LocaleEntity source) => FtlText.Hash(source.Text);

    /// <summary>Ids of <paramref name="lang"/> whose translation is stale or has no stamp, in ordinal order.</summary>
    public static IReadOnlyList<string> Stale(LocaleCatalog catalog, string lang)
    {
        var stale = new List<string>();
        if (lang == LocaleCatalog.SourceLanguage)
            return stale;
        IReadOnlyDictionary<string, LocaleEntity> source = catalog.Entities(LocaleCatalog.SourceLanguage);
        foreach ((string id, LocaleEntity e) in catalog.Entities(lang))
        {
            if (!source.TryGetValue(id, out LocaleEntity? en))
                continue; // no source to compare with: reported as an orphan elsewhere
            if (!catalog.Sidecar(e.Mod, lang).TryGetValue(id, out string? stamped) || stamped != HashOf(en))
                stale.Add(id);
        }
        return stale;
    }

    /// <summary>
    /// Stamps the current EN hashes of <paramref name="ids"/> (all entities of the language when empty) into the
    /// sidecars of the mods that define them. Called after a human or an agent reviewed the translation.
    /// </summary>
    public static void Stamp(LocaleCatalog catalog, string lang, IReadOnlyCollection<string> ids)
    {
        IReadOnlyDictionary<string, LocaleEntity> source = catalog.Entities(LocaleCatalog.SourceLanguage);
        var perFile = new SortedDictionary<string, (SortedDictionary<string, string> Lines, string Mod)>(StringComparer.Ordinal);
        foreach ((string id, LocaleEntity e) in catalog.Entities(lang))
        {
            if (ids.Count > 0 && !ids.Contains(id))
                continue;
            if (!source.TryGetValue(id, out LocaleEntity? en))
                throw new LocaleLoadException($"cannot stamp '{id}' ({lang}): no source entity in '{LocaleCatalog.SourceLanguage}'");
            string path = Path.Combine(Path.GetDirectoryName(e.File)!, LocaleCatalog.SidecarName);
            if (!perFile.TryGetValue(path, out var slot))
            {
                var lines = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach ((string k, string v) in catalog.Sidecar(e.Mod, lang))
                    lines[k] = v;
                perFile[path] = slot = (lines, e.Mod);
            }
            slot.Lines[id] = HashOf(en);
        }
        foreach ((string path, var slot) in perFile)
        {
            var sb = new StringBuilder();
            foreach ((string id, string hash) in slot.Lines)
                sb.Append(id).Append('\t').Append(hash).Append('\n');
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
