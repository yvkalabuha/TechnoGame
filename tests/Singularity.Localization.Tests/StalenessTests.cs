namespace Singularity.Localization.Tests;

/// <summary>ADR-0018 §3.4: stale-translation tracking through the _sources.tsv sidecar.</summary>
public class StalenessTests : IDisposable
{
    readonly TestKit _kit = new();
    public void Dispose() => _kit.Dispose();

    ModLocaleSource Mod(string enText, string ruText, string? sidecar = null)
    {
        var files = new List<(string, string, string)> { ("en", "a.ftl", enText), ("ru", "a.ftl", ruText) };
        if (sidecar is not null)
            files.Add(("ru", "_sources.tsv", sidecar));
        return _kit.Mod("m" + Guid.NewGuid().ToString("N")[..6], [], [.. files]);
    }

    [Fact]
    public void RussianCore_HasNoStaleTranslations()
    {
        // For core this is a test error (ADR-0018 §3.4): after changing EN, review RU and stamp.
        Assert.Empty(Staleness.Stale(TestKit.Catalog, "ru"));
    }

    [Fact]
    public void NoSidecar_MeansEverythingIsStale()
    {
        LocaleCatalog c = LocaleCatalog.Load([Mod("ui-a = A\nui-b = B\n", "ui-a = А\nui-b = Б\n")]);
        Assert.Equal(["ui-a", "ui-b"], Staleness.Stale(c, "ru"));
    }

    [Fact]
    public void StampThenChangeEnglish_MarksOnlyThatEntityStale()
    {
        ModLocaleSource mod = Mod("ui-a = A\nui-b = B\n", "ui-a = А\nui-b = Б\n");
        Staleness.Stamp(LocaleCatalog.Load([mod]), "ru", []);
        Assert.Empty(Staleness.Stale(LocaleCatalog.Load([mod]), "ru"));

        File.WriteAllText(Path.Combine(mod.Directory, "locale", "en", "a.ftl"), "ui-a = A changed\nui-b = B\n");
        Assert.Equal(["ui-a"], Staleness.Stale(LocaleCatalog.Load([mod]), "ru"));

        Staleness.Stamp(LocaleCatalog.Load([mod]), "ru", ["ui-a"]);
        Assert.Empty(Staleness.Stale(LocaleCatalog.Load([mod]), "ru"));
    }

    [Fact]
    public void NewEntityWithoutALine_IsStale()
    {
        ModLocaleSource mod = Mod("ui-a = A\n", "ui-a = А\n");
        Staleness.Stamp(LocaleCatalog.Load([mod]), "ru", []);
        File.WriteAllText(Path.Combine(mod.Directory, "locale", "en", "a.ftl"), "ui-a = A\nui-c = C\n");
        File.WriteAllText(Path.Combine(mod.Directory, "locale", "ru", "a.ftl"), "ui-a = А\nui-c = Ц\n");
        Assert.Equal(["ui-c"], Staleness.Stale(LocaleCatalog.Load([mod]), "ru"));
    }

    [Fact]
    public void Hash_IgnoresCommentsWhitespaceAndLineEnds_ButNotText()
    {
        string Hash(string en) => Staleness.HashOf(LocaleCatalog.Load([Mod(en, "ui-a = А\n")]).Entities("en")["ui-a"]);
        string plain = Hash("ui-a =\n    A\n    .short = B\n");
        Assert.Equal(plain, Hash("# comment\nui-a =\n    A   \n    .short = B\n\n\n"));
        Assert.Equal(plain, Hash("ui-a =\r\n    A\r\n    .short = B\r\n"));
        Assert.NotEqual(plain, Hash("ui-a =\n    A\n    .short = C\n"));
        Assert.Equal(16, plain.Length);
    }

    [Fact]
    public void Stamp_WritesSortedLines_AndKeepsOtherLanguagesUntouched()
    {
        ModLocaleSource mod = Mod("ui-b = B\nui-a = A\n", "ui-b = Б\nui-a = А\n");
        Staleness.Stamp(LocaleCatalog.Load([mod]), "ru", []);
        string[] lines = File.ReadAllLines(Path.Combine(mod.Directory, "locale", "ru", "_sources.tsv"));
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("ui-a\t", lines[0], StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(mod.Directory, "locale", "en", "_sources.tsv")));
    }

    [Fact]
    public void SourceLanguage_IsNeverStale() =>
        Assert.Empty(Staleness.Stale(LocaleCatalog.Load([Mod("ui-a = A\n", "ui-a = А\n")]), "en"));
}
