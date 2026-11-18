namespace Singularity.Localization.Tests;

/// <summary>ADR-0018 §2, §3.1: loading, mod conflicts, loud errors, the fallback chain and the missing-key marker.</summary>
public class LoaderTests : IDisposable
{
    readonly TestKit _kit = new();
    public void Dispose() => _kit.Dispose();

    const string Core = "ui-hello = Hello\nui-bye = Bye\n";

    [Fact]
    public void TwoIndependentMods_OverridingTheSameKey_Fail()
    {
        ModLocaleSource core = _kit.Mod("core", [], ("en", "a.ftl", Core));
        ModLocaleSource a = _kit.Mod("a", ["core"], ("en", "a.ftl", "ui-hello = A\n"));
        ModLocaleSource b = _kit.Mod("b", ["core"], ("en", "a.ftl", "ui-hello = B\n"));
        var ex = Assert.Throws<LocaleLoadException>(() => LocaleCatalog.Load([core, a, b]));
        Assert.Contains("'ui-hello'", ex.Message);
        Assert.Contains("does not depend", ex.Message);
    }

    [Fact]
    public void DependentMod_ReplacesTheWholeEntity()
    {
        ModLocaleSource core = _kit.Mod("core", [], ("en", "a.ftl", Core));
        ModLocaleSource a = _kit.Mod("a", ["core"], ("en", "a.ftl", "ui-hello = A\n"));
        ModLocaleSource b = _kit.Mod("b", ["a"], ("en", "a.ftl", "ui-hello = B\n"));   // transitively depends on core too
        LocaleCatalog c = LocaleCatalog.Load([core, a, b]);
        Assert.Equal("b", c.Entities("en")["ui-hello"].Mod);
        Assert.Equal("core", c.Entities("en")["ui-bye"].Mod);
    }

    [Fact]
    public void ModThatSkipsAnIntermediateOverride_Fails()
    {
        // c depends on core only, but a already replaced the key: c must depend on a as well.
        ModLocaleSource core = _kit.Mod("core", [], ("en", "a.ftl", Core));
        ModLocaleSource a = _kit.Mod("a", ["core"], ("en", "a.ftl", "ui-hello = A\n"));
        ModLocaleSource c = _kit.Mod("c", ["core"], ("en", "a.ftl", "ui-hello = C\n"));
        Assert.Throws<LocaleLoadException>(() => LocaleCatalog.Load([core, a, c]));
    }

    [Fact]
    public void ConflictsAreResolvedPerLanguage()
    {
        ModLocaleSource core = _kit.Mod("core", [], ("en", "a.ftl", Core), ("ru", "a.ftl", "ui-hello = Привет\n"));
        ModLocaleSource a = _kit.Mod("a", ["core"], ("ru", "a.ftl", "ui-hello = Здравствуйте\n"));
        ModLocaleSource b = _kit.Mod("b", ["core"], ("en", "a.ftl", "ui-hello = B\n"));
        LocaleCatalog c = LocaleCatalog.Load([core, a, b]);   // a changes ru, b changes en: no clash
        Assert.Equal("a", c.Entities("ru")["ui-hello"].Mod);
        Assert.Equal("b", c.Entities("en")["ui-hello"].Mod);
    }

    [Fact]
    public void DuplicateIdInOneMod_AcrossFiles_Fails()
    {
        ModLocaleSource m = _kit.Mod("m", [], ("en", "a.ftl", "ui-x = 1\n"), ("en", "b.ftl", "ui-x = 2\n"));
        var ex = Assert.Throws<LocaleLoadException>(() => LocaleCatalog.Load([m]));
        Assert.Contains("defined twice", ex.Message);
    }

    [Fact]
    public void NewLanguageMod_NeedsNoDataChange()
    {
        ModLocaleSource core = _kit.Mod("core", [], ("en", "a.ftl", Core));
        ModLocaleSource de = _kit.Mod("lang_de", ["core"], ("de", "a.ftl", "ui-hello = Hallo\n"));
        LocaleCatalog c = LocaleCatalog.Load([core, de]);
        Assert.Equal(["de", "en"], c.Languages);
    }

    [Theory]
    [InlineData("ui-x = {", "syntax")]
    [InlineData("foo-x = a\n", "known kind")]
    [InlineData("-ui-x = a\n", "term")]
    [InlineData("-node-s8-a = a\n", "term")]
    [InlineData("actor-SUN = a\n", "term")]
    [InlineData("node-s8-eng-x =\n    .name = A\n    .title = B\n", "unknown attribute")]
    [InlineData("ui-x = a\nui-y = {\n", "syntax")]
    public void InvalidFiles_FailLoudly(string text, string expected)
    {
        ModLocaleSource m = _kit.Mod("m", [], ("en", "a.ftl", text));
        var ex = Assert.Throws<LocaleLoadException>(() => LocaleCatalog.Load([m]));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void BadLanguageTag_AndStrayFiles_Fail()
    {
        Assert.Throws<LocaleLoadException>(() => LocaleCatalog.Load([_kit.Mod("a", [], ("EN_us", "a.ftl", "ui-x = 1\n"))]));
        Assert.Throws<LocaleLoadException>(() => LocaleCatalog.Load([_kit.Mod("b", [], ("en", "notes.txt", "x"))]));
        Assert.Throws<LocaleLoadException>(() => LocaleCatalog.Load([_kit.Mod("c", [], ("en", "a.ftl", "ui-x = 1\n"), ("en", "data.csv", "x"))]));
    }

    [Fact]
    public void SidecarIsAccepted_AndMalformedSidecarFails()
    {
        ModLocaleSource ok = _kit.Mod("ok", [], ("en", "a.ftl", "ui-x = 1\n"), ("ru", "_sources.tsv", "ui-x\t0123456789abcdef\n"));
        Assert.Equal("0123456789abcdef", LocaleCatalog.Load([ok]).Sidecar("ok", "ru")["ui-x"]);
        ModLocaleSource bad = _kit.Mod("bad", [], ("ru", "_sources.tsv", "ui-x\tshort\n"));
        Assert.Throws<LocaleLoadException>(() => LocaleCatalog.Load([bad]));
    }

    [Fact]
    public void FilesLoadInOrdinalOrder_AndCommentsAreIgnored()
    {
        ModLocaleSource m = _kit.Mod("m", [], ("en", "b.ftl", "# note\nui-b = B\n"), ("en", "a.ftl", "## group\nui-a = A\n"));
        LocaleCatalog c = LocaleCatalog.Load([m]);
        Assert.Equal(["ui-a", "ui-b"], c.Entities("en").Keys);
        Assert.EndsWith("a.ftl", c.Entities("en")["ui-a"].File);
    }

    // ---- facade: fallback chain and marker ----

    [Fact]
    public void FallbackChain_SelectedThenEnglishThenMarker_WithOneWarningPerId()
    {
        var warnings = new List<string>();
        LocaleCatalog c = _kit.Single("ui-both = Both\nui-only_en = Only EN\n", "ui-both = Оба\n");
        var loc = new Loc(c, "ru", warnings.Add);
        Assert.Equal("Оба", loc.T("ui-both"));
        Assert.Equal("Only EN", loc.T("ui-only_en"));                 // falls back to en
        Assert.Equal("[[ui-nowhere]]", loc.T("ui-nowhere"));
        Assert.Equal("[[ui-nowhere]]", loc.T("ui-nowhere"));
        Assert.Single(warnings, w => w.Contains("ui-nowhere", StringComparison.Ordinal));
        Assert.Equal("[[ui-both.short]]", loc.A("ui-both", Field.Short));   // missing attribute
    }

    [Fact]
    public void SetLanguage_RaisesLanguageChanged_AndRejectsUnknown()
    {
        var loc = new Loc(_kit.Single("ui-a = A\n", "ui-a = Б\n"), "en");
        int raised = 0;
        loc.LanguageChanged += () => raised++;
        loc.SetLanguage("ru");
        loc.SetLanguage("ru");                       // no change, no event
        Assert.Equal(1, raised);
        Assert.Equal("Б", loc.T("ui-a"));
        Assert.Throws<ArgumentException>(() => loc.SetLanguage("de"));
    }

    [Fact]
    public void NoGlobalState_TwoLocsDoNotInterfere()
    {
        LocaleCatalog c = _kit.Single("ui-a = A\n", "ui-a = Б\n");
        var en = new Loc(c, "en");
        var ru = new Loc(c, "ru");
        Assert.Equal("A", en.T("ui-a"));
        Assert.Equal("Б", ru.T("ui-a"));
    }

    const string Plural = "ui-n = { $n ->\n    [one] a\n   *[other] b\n}\n";

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("de")]
    public void KnownLanguageTags_LoadAndFormatPlurals(string lang)
    {
        ModLocaleSource m = _kit.Mod("m", [], ("en", "a.ftl", Plural), (lang, "a.ftl", Plural));
        var loc = new Loc(LocaleCatalog.Load([m]), lang);
        Assert.Equal("b", loc.T("ui-n", ("n", 5)));
    }

    [Fact]
    public void TagWithoutPluralRules_NeverFailsAtFormatTime()
    {
        // Either Linguini knows the tag (then it formats), or loading fails loudly: an IndexOutOfRange at format time is a bug.
        ModLocaleSource m = _kit.Mod("m", [], ("en", "a.ftl", Plural), ("zz", "a.ftl", Plural));
        LocaleCatalog c = LocaleCatalog.Load([m]);
        Loc? loc = null;
        try { loc = new Loc(c, "zz"); }
        catch (LocaleLoadException ex) { Assert.Contains("no CLDR plural rules", ex.Message, StringComparison.Ordinal); }
        if (loc is not null)
            Assert.Equal("b", loc.T("ui-n", ("n", 5)));
    }

    [Fact]
    public void ParallelMisses_WarnOncePerId()
    {
        var warnings = new System.Collections.Concurrent.ConcurrentBag<string>();
        var loc = new Loc(_kit.Single("ui-a = A\n"), "en", warnings.Add);
        Parallel.For(0, 64, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            for (int rep = 0; rep < 200; rep++)
                loc.T("ui-missing-" + (rep % 20));
        });
        Assert.Equal(20, warnings.Count);
        Assert.Equal(20, warnings.Distinct().Count());
    }
}
