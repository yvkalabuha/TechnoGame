using System.Text;
using Linguini.Syntax.Ast;

namespace Singularity.Localization.Tests;

/// <summary>ADR-0018 §3.5: every character of every string exists in the client fonts that will draw it.</summary>
public class GlyphCoverageTests
{
    static readonly Lazy<Dictionary<string, FontCmap>> Fonts = new(() => new[] { "GolosText", "Jura", "PTMono", "Literata" }
        .ToDictionary(n => n, n => new FontCmap(Path.Combine(TestKit.FontDir, n + ".ttf"))));

    static LocaleCatalog Catalog => TestKit.Catalog;

    // All literal text of an entity: text elements, string literals and select keys excluded (keys are not shown).
    static IEnumerable<string> Texts(LocaleEntity e)
    {
        foreach ((_, Pattern p) in AstUtil.Patterns(e.Ast))
            foreach (string t in Texts(p))
                yield return t;
    }

    static IEnumerable<string> Texts(Pattern p)
    {
        foreach (IPatternElement el in p.Elements)
        {
            switch (el)
            {
                case TextLiteral t:
                    yield return t.ToString();
                    break;
                case Placeable { Expression: SelectExpression s }:
                    foreach (Variant v in s.Variants)
                        foreach (string t in Texts(v.Value))
                            yield return t;
                    break;
                case Placeable { Expression: TextLiteral lit }:
                    yield return lit.ToString();
                    break;
            }
        }
    }

    static List<string> Missing(IEnumerable<LocaleEntity> entities, params string[] fonts)
    {
        var problems = new SortedSet<string>(StringComparer.Ordinal);
        foreach (LocaleEntity e in entities)
        {
            foreach (string text in Texts(e))
            {
                foreach (Rune r in text.EnumerateRunes())
                {
                    foreach (string font in fonts)
                    {
                        if (!Fonts.Value[font].Has(r.Value))
                            problems.Add($"{font}: U+{r.Value:X4} '{r}' in {e.Id}");
                    }
                }
            }
        }
        return [.. problems];
    }

    static IEnumerable<LocaleEntity> Of(string lang, Func<LocaleEntity, bool> pick) => Catalog.Entities(lang).Values.Where(pick);

    [Fact]
    public void CmapReader_FindsKnownGlyphs()
    {
        Assert.True(Fonts.Value["GolosText"].Has('A'));
        Assert.True(Fonts.Value["GolosText"].Has('Ж'));
        Assert.False(Fonts.Value["GolosText"].Has(0x4E2D));   // a CJK ideograph is not in a Latin/Cyrillic font
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    public void InterfaceText_IsInGolosAndJura(string lang) =>
        Assert.Empty(Missing(Of(lang, e => e.Kind is not (KeyKind.Fmt or KeyKind.Date or KeyKind.Actor or KeyKind.Country or KeyKind.Region)), "GolosText", "Jura"));

    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    public void FormatStrings_AreInMono(string lang) =>
        Assert.Empty(Missing(Of(lang, e => e.Kind is KeyKind.Fmt or KeyKind.Date), "PTMono"));

    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    public void MapLabels_AreInLiterata_AndInTheInterfaceFonts(string lang) =>
        Assert.Empty(Missing(Of(lang, e => e.Kind is KeyKind.Actor or KeyKind.Country or KeyKind.Region), "Literata", "GolosText", "Jura"));

    [Fact]
    public void DigitsAndSeparatorsOfNumbersAndDates_AreInMono()
    {
        foreach (char c in "0123456789,.\u00A0\u2212%-")
            Assert.True(Fonts.Value["PTMono"].Has(c), $"PTMono: U+{(int)c:X4}");
    }

    [Fact]
    public void SpecialSigns_AreInAllFonts()
    {
        // Marker [[…]], no-break space, true minus and the brackets of the pseudo-locale.
        foreach (string font in new[] { "GolosText", "Jura", "PTMono", "Literata" })
            foreach (char c in "[] −!~")
                Assert.True(Fonts.Value[font].Has(c), $"{font}: U+{(int)c:X4}");
    }

    [Fact]
    public void PseudoLocale_UsesOnlyCoveredCharacters_AndItsBracketsAreTheFallbackPair()
    {
        // ⟦ ⟧ are not in the fonts, so the ADR fallback [! !] is used; if a font gains them this assertion tells us.
        foreach (string font in new[] { "GolosText", "Jura" })
            Assert.False(Fonts.Value[font].Has(0x27E6) && Fonts.Value[font].Has(0x27E7), $"{font} now has ⟦⟧: switch PseudoLocale to them");
        LocaleCatalog fresh = LocaleCatalog.Load(ModLocaleSource.From(TestKit.Data));
        PseudoLocale.AddTo(fresh);
        Assert.Empty(Missing(fresh.Entities("xx").Values.Where(e => e.Kind is not (KeyKind.Actor or KeyKind.Country or KeyKind.Region)), "GolosText", "Jura"));
    }
}
