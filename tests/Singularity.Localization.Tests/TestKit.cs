using System.Collections.Immutable;
using Singularity.Core.Data;

namespace Singularity.Localization.Tests;

/// <summary>Repository paths, the real core catalog and throw-away mod directories.</summary>
internal sealed class TestKit : IDisposable
{
    public static string RepoRoot { get; } = FindRepoRoot();
    public static string CoreDir => Path.Combine(RepoRoot, "mods", "core");
    public static string FontDir => Path.Combine(RepoRoot, "src", "Singularity.Client.Spike", "assets", "fonts");

    static readonly Lazy<GameData> CoreData = new(() => ModLoader.Load(CoreDir));
    static readonly Lazy<LocaleCatalog> CoreCatalog = new(() => LocaleCatalog.Load(ModLocaleSource.From(Data))); // raw: the tests must see orphans
    public static GameData Data => CoreData.Value;
    public static LocaleCatalog Catalog => CoreCatalog.Value;

    readonly string _root = Path.Combine(Path.GetTempPath(), "singularity-loc-tests", Guid.NewGuid().ToString("N"));

    public TestKit() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    public string Root => _root;

    /// <summary>A mod directory with locale files: (lang, file name, text).</summary>
    public ModLocaleSource Mod(string id, string[] dependencies, params (string Lang, string File, string Text)[] files)
    {
        string dir = Path.Combine(_root, id);
        foreach ((string lang, string file, string text) in files)
        {
            string d = Path.Combine(dir, "locale", lang);
            Directory.CreateDirectory(d);
            File.WriteAllText(Path.Combine(d, file), text);
        }
        Directory.CreateDirectory(dir);
        return new ModLocaleSource(id, dir, [.. dependencies]);
    }

    /// <summary>A one-mod catalog from ftl texts of languages en and ru.</summary>
    public LocaleCatalog Single(string en, string? ru = null)
    {
        var files = new List<(string, string, string)> { ("en", "t.ftl", en) };
        if (ru is not null)
            files.Add(("ru", "t.ftl", ru));
        return LocaleCatalog.Load([Mod("t" + Guid.NewGuid().ToString("N")[..6], [], [.. files])]);
    }

    /// <summary>A Loc over a one-mod catalog that also carries the core number and date formats.</summary>
    public Loc LocWithFormats(string en, string ru, string language, List<string>? warnings = null)
    {
        string coreEnFmt = File.ReadAllText(Path.Combine(CoreDir, "locale", "en", "fmt.ftl"));
        string coreRuFmt = File.ReadAllText(Path.Combine(CoreDir, "locale", "ru", "fmt.ftl"));
        ModLocaleSource mod = Mod("f" + Guid.NewGuid().ToString("N")[..6], [],
            ("en", "fmt.ftl", coreEnFmt), ("ru", "fmt.ftl", coreRuFmt), ("en", "t.ftl", en), ("ru", "t.ftl", ru));
        return new Loc(LocaleCatalog.Load([mod]), language, warnings is null ? null : warnings.Add);
    }

    static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Singularity.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Singularity.sln not found above " + AppContext.BaseDirectory);
    }
}
