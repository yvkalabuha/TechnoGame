using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Linguini.Syntax.Ast;
using Linguini.Syntax.Parser;
using Singularity.Core.Data;

namespace Singularity.Localization;

/// <summary>A mod that may carry <c>locale/&lt;lang&gt;/*.ftl</c>. Mods are given in load order (dependencies first).</summary>
public sealed record ModLocaleSource(string ModId, string Directory, ImmutableArray<string> Dependencies)
{
    public static ImmutableArray<ModLocaleSource> From(GameData data) =>
        [.. data.Mods.Select(m => new ModLocaleSource(m.Id, m.Directory, [.. m.Dependencies.Select(d => d.Id)]))];
}

/// <summary>One message or term after conflict resolution.</summary>
public sealed class LocaleEntity
{
    /// <summary>Id as written in files: terms start with '-'.</summary>
    public required string Id { get; init; }
    public required KeyKind Kind { get; init; }
    public required IEntry Ast { get; init; }
    /// <summary>Normalised source text (no comments, LF, no trailing blanks): the staleness hash input.</summary>
    public required string Text { get; init; }
    public required string Mod { get; init; }
    public required string File { get; init; }
    public bool IsTerm => Ast is AstTerm;
}

/// <summary>
/// All locales of all mods (ADR-0018 §2, §3). Reads <c>locale/&lt;lang&gt;/</c> of every mod, resolves conflicts
/// at the AST level (as ADR-0010 for objects) and keeps one entity per id and language. Pure text: no game state.
/// </summary>
public sealed partial class LocaleCatalog
{
    public const string SourceLanguage = "en";
    public const string SidecarName = "_sources.tsv";
    public const string LocaleDirName = ModLoader.LocaleDirectory;

    readonly SortedDictionary<string, SortedDictionary<string, LocaleEntity>> _entities = new(StringComparer.Ordinal);
    // (mod, lang) -> id -> hash from _sources.tsv.
    readonly SortedDictionary<(string Mod, string Lang), SortedDictionary<string, string>> _sidecars = new();

    LocaleCatalog() { }

    public IReadOnlyList<string> Languages => [.. _entities.Keys];

    public IReadOnlyDictionary<string, LocaleEntity> Entities(string lang) =>
        _entities.TryGetValue(lang, out var e) ? e : new SortedDictionary<string, LocaleEntity>();

    /// <summary>Stamped source hashes of one mod and language (empty if there is no sidecar).</summary>
    public IReadOnlyDictionary<string, string> Sidecar(string mod, string lang) =>
        _sidecars.TryGetValue((mod, lang), out var s) ? s : new SortedDictionary<string, string>();

    public bool Has(string lang, string id) => _entities.TryGetValue(lang, out var e) && e.ContainsKey(id);

    /// <summary>
    /// Loads the locales of the mods of <paramref name="data"/> and applies the runtime rules of ADR-0018 §3.2, §3.4:
    /// orphans (data-backed ids without a data object) are skipped, and L1, L2, L3 problems and stale
    /// translations are reported through <paramref name="warn"/>. Syntax and grammar errors still throw.
    /// </summary>
    public static LocaleCatalog Load(GameData data, Action<string>? warn = null) =>
        Load(ModLocaleSource.From(data), data, warn);

    /// <summary>As <see cref="Load(GameData, Action{string})"/>, for an explicit list of locale sources.</summary>
    public static LocaleCatalog Load(IReadOnlyList<ModLocaleSource> mods, GameData data, Action<string>? warn)
    {
        LocaleCatalog catalog = Load(mods);
        SortedDictionary<string, KeyKind> expected = ExpectedKeys.Of(data);
        foreach (string lang in catalog.Languages)
        {
            var orphans = catalog._entities[lang].Values.Where(e => LocaleChecks.IsDataBacked(e.Kind) && !expected.ContainsKey(e.Id)).Select(e => e.Id).ToList();
            foreach (string id in orphans)
            {
                catalog._entities[lang].Remove(id);
                warn?.Invoke($"L2 {lang}: orphan '{id}' has no data object, skipped");
            }
        }
        if (warn is null)
            return catalog;
        foreach (string p in LocaleChecks.Completeness(catalog, data).Concat(LocaleChecks.Variables(catalog)))
            warn(p);
        foreach (string lang in catalog.Languages)
        {
            foreach (string id in Staleness.Stale(catalog, lang))
                warn($"stale translation '{id}' ({lang}): source text changed or no stamp in {SidecarName}");
        }
        return catalog;
    }

    public static LocaleCatalog Load(IReadOnlyList<ModLocaleSource> mods)
    {
        var catalog = new LocaleCatalog();
        // id -> mods that already defined or replaced it, per language.
        var touched = new Dictionary<(string Lang, string Id), List<string>>();
        var byId = mods.ToDictionary(m => m.ModId, StringComparer.Ordinal);

        foreach (ModLocaleSource mod in mods)
        {
            string root = Path.Combine(mod.Directory, LocaleDirName);
            if (!Directory.Exists(root))
                continue;
            HashSet<string> ancestors = Transitive(mod, byId);
            string[] langDirs = Directory.GetDirectories(root);
            Array.Sort(langDirs, StringComparer.Ordinal);
            foreach (string langDir in langDirs)
            {
                string lang = Path.GetFileName(langDir);
                if (!IsLanguageTag(lang))
                    throw new LocaleLoadException($"{langDir}: '{lang}' is not a BCP 47 language tag");
                catalog.LoadLanguage(mod, ancestors, lang, langDir, touched);
            }
        }
        return catalog;
    }

    void LoadLanguage(ModLocaleSource mod, HashSet<string> ancestors, string lang, string dir,
        Dictionary<(string Lang, string Id), List<string>> touched)
    {
        if (Directory.GetDirectories(dir).Length > 0)
            throw new LocaleLoadException($"{dir}: sub-directories are not allowed in locale/<lang>/");
        string[] files = Directory.GetFiles(dir);
        Array.Sort(files, StringComparer.Ordinal);
        var seenInMod = new HashSet<string>(StringComparer.Ordinal);
        if (!_entities.TryGetValue(lang, out var table))
            _entities[lang] = table = new SortedDictionary<string, LocaleEntity>(StringComparer.Ordinal);

        foreach (string path in files)
        {
            string name = Path.GetFileName(path);
            if (name == SidecarName)
            {
                _sidecars[(mod.ModId, lang)] = ReadSidecar(path);
                continue;
            }
            if (Path.GetExtension(path) != ".ftl")
                throw new LocaleLoadException($"{path}: only *.ftl and {SidecarName} are allowed in locale/<lang>/");

            foreach (LocaleEntity e in ParseFile(path, mod.ModId))
            {
                if (!seenInMod.Add(e.Id))
                    throw new LocaleLoadException($"{path}: id '{e.Id}' is defined twice in mod '{mod.ModId}' ({lang})");
                var slot = (lang, e.Id);
                if (!touched.TryGetValue(slot, out var history))
                {
                    touched[slot] = [mod.ModId];
                    table[e.Id] = e;
                    continue;
                }
                // Replacement: the mod must (transitively) depend on every mod that already changed this id.
                foreach (string earlier in history)
                {
                    if (earlier != mod.ModId && !ancestors.Contains(earlier))
                        throw new LocaleLoadException(
                            $"{path}: '{e.Id}' ({lang}) is already defined by mod '{earlier}', and mod '{mod.ModId}' does not depend on it");
                }
                history.Add(mod.ModId);
                table[e.Id] = e;
            }
        }
    }

    static List<LocaleEntity> ParseFile(string path, string mod)
    {
        string text = File.ReadAllText(path);
        Resource resource = LinguiniParser.FromFragment(text).Parse();
        if (resource.Errors.Count > 0)
            throw new LocaleLoadException($"{path}: Fluent syntax error: {resource.Errors[0]}");
        List<(string Id, string Text)> raw = FtlText.Split(text);
        List<IEntry> entries = [.. resource.Entries.Where(e => e is AstMessage or AstTerm)];
        if (raw.Count != entries.Count)
            throw new LocaleLoadException($"{path}: cannot match {raw.Count} source entries with {entries.Count} parsed entries");

        var result = new List<LocaleEntity>(entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            IEntry entry = entries[i];
            string id = AstUtil.IdOf(entry);
            if (raw[i].Id != id)
                throw new LocaleLoadException($"{path}: entry '{id}' does not match source text '{raw[i].Id}'");
            result.Add(new LocaleEntity { Id = id, Kind = Validate(path, entry, id), Ast = entry, Text = raw[i].Text, Mod = mod, File = path });
        }
        return result;
    }

    // Grammar of ids, term/message kind and attribute names (ADR-0018 §1, §3.1).
    static KeyKind Validate(string path, IEntry entry, string id)
    {
        if (!FluentIds.TryParse(id, out ParsedKey key))
            throw new LocaleLoadException($"{path}: id '{id}' does not match <kind>-<seg>(-<seg>)* with a known kind");
        bool isTerm = entry is AstTerm;
        if (isTerm != FluentIds.IsTermKind(key.Kind) || isTerm != id.StartsWith('-'))
            throw new LocaleLoadException($"{path}: '{id}' must be a term exactly for actor, country and region");
        foreach ((string? attr, _) in AstUtil.Patterns(entry))
        {
            if (attr is not null && !FluentIds.IsAllowedAttribute(key.Kind, attr))
                throw new LocaleLoadException($"{path}: unknown attribute '.{attr}' for '{id}'");
        }
        return key.Kind;
    }

    static SortedDictionary<string, string> ReadSidecar(string path)
    {
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0)
                continue;
            string[] cols = line.Split('\t');
            if (cols.Length != 2 || cols[0].Length == 0 || cols[1].Length != 16)
                throw new LocaleLoadException($"{path}: bad line '{line}' (expected <id>\\t<16 hex>)");
            if (!map.TryAdd(cols[0], cols[1]))
                throw new LocaleLoadException($"{path}: id '{cols[0]}' appears twice");
        }
        return map;
    }

    static HashSet<string> Transitive(ModLocaleSource mod, Dictionary<string, ModLocaleSource> byId)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>(mod.Dependencies);
        while (stack.Count > 0)
        {
            string id = stack.Pop();
            if (!result.Add(id) || !byId.TryGetValue(id, out ModLocaleSource? m))
                continue;
            foreach (string d in m.Dependencies)
                stack.Push(d);
        }
        return result;
    }

    [GeneratedRegex("^[a-z]{2,3}(-[A-Za-z0-9]{2,8})*$")]
    private static partial Regex LanguageTag();

    public static bool IsLanguageTag(string s) => LanguageTag().IsMatch(s);

    /// <summary>Adds the pseudo-locale (see <see cref="PseudoLocale"/>) as a normal language.</summary>
    internal void Add(string lang, IEnumerable<LocaleEntity> entities)
    {
        var table = new SortedDictionary<string, LocaleEntity>(StringComparer.Ordinal);
        foreach (LocaleEntity e in entities)
            table[e.Id] = e;
        _entities[lang] = table;
    }
}
