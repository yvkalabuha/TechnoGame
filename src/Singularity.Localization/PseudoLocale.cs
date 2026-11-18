using System.Globalization;
using System.Text;
using Linguini.Syntax.Ast;

namespace Singularity.Localization;

/// <summary>
/// The pseudo-locale <c>xx</c> (ADR-0018 §3.6): built from the AST of the longer of the EN and RU versions.
/// Text grows by 30%, accents mark every Latin vowel and each pattern is wrapped in brackets, so untranslated or
/// clipped text stands out. Variables, functions, selectors, `fmt-*` and `date-*` entities and `.gender` are not touched.
/// </summary>
public static class PseudoLocale
{
    public const string Language = "xx";

    /// <summary>Brackets of a pattern. ⟦ ⟧ are not in the client fonts, so the ADR fallback is used (checked by a test).</summary>
    public const string Open = "[!";
    public const string Close = "!]";

    const char Pad = '~';

    /// <summary>Adds <c>xx</c> to a catalog loaded from files.</summary>
    public static void AddTo(LocaleCatalog catalog)
    {
        IReadOnlyDictionary<string, LocaleEntity> en = catalog.Entities("en"), ru = catalog.Entities("ru");
        var ids = new SortedSet<string>(en.Keys, StringComparer.Ordinal);
        ids.UnionWith(ru.Keys);
        var result = new List<LocaleEntity>();
        foreach (string id in ids)
        {
            en.TryGetValue(id, out LocaleEntity? e);
            ru.TryGetValue(id, out LocaleEntity? r);
            result.Add(Build(e, r));
        }
        catalog.Add(Language, result);
    }

    // Per field (the value and every attribute) the longer of the EN and RU patterns is taken, then transformed.
    static LocaleEntity Build(LocaleEntity? en, LocaleEntity? ru)
    {
        LocaleEntity first = en ?? ru!;
        // Machine-readable entities keep their text (separators, month names): the source language is used as is.
        if (first.Kind is KeyKind.Fmt or KeyKind.Date)
            return Copy(first, first.Ast);

        var slots = new SortedDictionary<string, Pattern>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (LocaleEntity? e in new[] { en, ru })
        {
            if (e is null)
                continue;
            foreach ((string? attr, Pattern p) in AstUtil.Patterns(e.Ast))
            {
                string slot = attr ?? "";
                if (!slots.TryGetValue(slot, out Pattern? have))
                    order.Add(slot);
                // Genders are not text: take the one of the richer (Russian) grammar.
                if (have is null || slot == "gender" || AstUtil.MaxLength(p, 8) > AstUtil.MaxLength(have, 8))
                    slots[slot] = p;
            }
        }
        Pattern? value = slots.GetValueOrDefault("");
        // The gender of a term is a selector value, not text: it stays as it is.
        List<Linguini.Syntax.Ast.Attribute> attrs = [.. order.Where(s => s != "").Select(s =>
            new Linguini.Syntax.Ast.Attribute(s, s == "gender" ? slots[s] : Wrap(slots[s])))];
        IEntry ast = first.Ast switch
        {
            AstTerm t => new AstTerm(t.Id, Wrap(value!), attrs, t.Location, null),
            AstMessage m => new AstMessage(m.Id, value is null ? null : Wrap(value), attrs, m.Location, null),
            _ => first.Ast,
        };
        return Copy(first, ast);
    }

    static LocaleEntity Copy(LocaleEntity e, IEntry ast) =>
        new() { Id = e.Id, Kind = e.Kind, Ast = ast, Text = e.Text, Mod = e.Mod, File = e.File };

    // Brackets around the whole pattern, then accents and padding inside text parts.
    static Pattern Wrap(Pattern p)
    {
        int len = AstUtil.MaxLength(p, 0);
        var elements = new List<IPatternElement> { new TextLiteral(Open) };
        elements.AddRange(Rewrite(p.Elements));
        elements.Add(new TextLiteral(new string(Pad, (len * 3 + 9) / 10) + Close)); // +30% of the static text
        return new Pattern(elements);
    }

    static IEnumerable<IPatternElement> Rewrite(List<IPatternElement> elements)
    {
        foreach (IPatternElement e in elements)
        {
            yield return e switch
            {
                TextLiteral t => new TextLiteral(Accent(t.ToString())),
                Placeable { Expression: SelectExpression s } => new Placeable(new SelectExpression(s.Selector,
                    [.. s.Variants.Select(v => new Variant(v.Type, v.Key, new Pattern([.. Rewrite(v.Value.Elements)]), v.IsDefault))])),
                _ => e,
            };
        }
    }

    // Latin vowels get an accent. Other letters stay, so Cyrillic and every glyph remain in the client fonts.
    static string Accent(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            sb.Append(c switch
            {
                'a' => 'á', 'e' => 'é', 'i' => 'í', 'o' => 'ó', 'u' => 'ú',
                'A' => 'Á', 'E' => 'É', 'I' => 'Í', 'O' => 'Ó', 'U' => 'Ú',
                _ => c,
            });
        }
        return sb.ToString();
    }
}
