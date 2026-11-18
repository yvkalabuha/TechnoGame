using Linguini.Bundle;
using Linguini.Bundle.Errors;
using Linguini.Bundle.Types;
using Linguini.Shared.Types;
using Linguini.Shared.Types.Bundle;
using Linguini.Syntax.Ast;
using PluralRulesGenerated;
using Singularity.Core.Data;

namespace Singularity.Localization;

/// <summary>
/// Completeness and quality checks of ADR-0018 §3.2 (L1-L4, L6). Each returns problem texts; empty means clean.
/// For `core` the tests demand an empty list; for other mods the runtime only logs the problems.
/// </summary>
public static class LocaleChecks
{
    static readonly string[] BaseLanguages = ["en", "ru"];

    /// <summary>L1: required fields exist in EN and RU. L2: ids of data-backed kinds without a data object.</summary>
    public static List<string> Completeness(LocaleCatalog catalog, GameData data)
    {
        var problems = new List<string>();
        SortedDictionary<string, KeyKind> expected = ExpectedKeys.Of(data);
        foreach (string lang in BaseLanguages)
        {
            IReadOnlyDictionary<string, LocaleEntity> have = catalog.Entities(lang);
            foreach ((string id, KeyKind kind) in expected)
            {
                if (!have.TryGetValue(id, out LocaleEntity? e))
                {
                    problems.Add($"L1 {lang}: missing '{id}'");
                    continue;
                }
                var attrs = AstUtil.Patterns(e.Ast).Where(p => p.Attribute is not null).Select(p => p.Attribute!).ToHashSet(StringComparer.Ordinal);
                foreach (string req in FluentIds.RequiredAttributes(kind))
                {
                    if (!attrs.Contains(req))
                        problems.Add($"L1 {lang}: '{id}' lacks .{req}");
                }
                if (FluentIds.HasValue(kind) && !AstUtil.Patterns(e.Ast).Any(p => p.Attribute is null))
                    problems.Add($"L1 {lang}: '{id}' lacks a value");
                if (kind == KeyKind.Enum && e.Ast is Linguini.Syntax.Ast.AstMessage { Value: null })
                    problems.Add($"L1 {lang}: '{id}' lacks a value");
            }
        }
        // Russian terms (actors, countries, regions) are written for all six cases.
        string[] cases = ["nom", "gen", "dat", "acc", "ins", "loc"];
        foreach ((string id, LocaleEntity e) in catalog.Entities("ru"))
        {
            if (e.Ast is not AstTerm term)
                continue;
            var select = AstUtil.Selects(term.Value).FirstOrDefault(s => s.Selector is VariableReference { Id: var v } && v.ToString() == "case");
            string[] have = select is null ? [] : [.. select.Variants.Select(v => v.Key.ToString())];
            foreach (string c in cases.Except(have, StringComparer.Ordinal))
                problems.Add($"L1 ru: '{id}' lacks case [{c}] in $case");
        }
        // An optional attribute set in one base language is required in the other.
        foreach ((string id, LocaleEntity en) in catalog.Entities("en"))
        {
            if (!catalog.Entities("ru").TryGetValue(id, out LocaleEntity? ru))
                continue;
            var a = Attributes(en);
            var b = Attributes(ru);
            foreach (string x in a.Except(b, StringComparer.Ordinal))
                problems.Add($"L1 ru: '{id}' lacks .{x} that en has");
            foreach (string x in b.Except(a, StringComparer.Ordinal))
                problems.Add($"L1 en: '{id}' lacks .{x} that ru has");
        }
        foreach (string lang in catalog.Languages)
        {
            foreach ((string id, LocaleEntity e) in catalog.Entities(lang))
            {
                if (IsDataBacked(e.Kind) && !expected.ContainsKey(id))
                    problems.Add($"L2 {lang}: orphan '{id}' has no data object");
            }
        }
        return problems;
    }

    static SortedSet<string> Attributes(LocaleEntity e) =>
        new(AstUtil.Patterns(e.Ast).Where(p => p.Attribute is not null).Select(p => p.Attribute!), StringComparer.Ordinal);

    internal static bool IsDataBacked(KeyKind k) =>
        k is KeyKind.Node or KeyKind.ProductClass or KeyKind.Slot or KeyKind.Alternative or KeyKind.Scenario or KeyKind.Actor
            or KeyKind.Cluster or KeyKind.Discipline or KeyKind.NodeType or KeyKind.Enum;

    /// <summary>L3: the same variables and functions in every language of an entity (selectors may differ).</summary>
    public static List<string> Variables(LocaleCatalog catalog)
    {
        var problems = new List<string>();
        foreach ((string id, LocaleEntity en) in catalog.Entities(LocaleCatalog.SourceLanguage))
        {
            foreach (string lang in catalog.Languages)
            {
                if (lang == LocaleCatalog.SourceLanguage || !catalog.Entities(lang).TryGetValue(id, out LocaleEntity? other))
                    continue;
                var a = Usage(en);
                var b = Usage(other);
                foreach (string slot in a.Keys.Union(b.Keys, StringComparer.Ordinal))
                {
                    string ua = a.GetValueOrDefault(slot, "");
                    string ub = b.GetValueOrDefault(slot, "");
                    if (ua != ub)
                        problems.Add($"L3 {lang}: '{id}'{slot} uses [{ub}] but en uses [{ua}]");
                }
            }
        }
        return problems;
    }

    static SortedDictionary<string, string> Usage(LocaleEntity e)
    {
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach ((string? attr, Pattern p) in AstUtil.Patterns(e.Ast))
        {
            var vars = new SortedSet<string>(StringComparer.Ordinal);
            var funcs = new SortedSet<string>(StringComparer.Ordinal);
            AstUtil.CollectUsage(p, vars, funcs, e.IsTerm);
            map[attr is null ? "" : "." + attr] = string.Join(',', vars.Select(v => "$" + v).Concat(funcs.Select(f => f + "()")));
        }
        return map;
    }

    /// <summary>
    /// L4: plural selectors have `*[other]` and only categories that exist for the language, and every
    /// such string formats for 0..200 and for fractions without error or marker.
    /// </summary>
    public static List<string> Plurals(LocaleCatalog catalog, Loc loc)
    {
        var problems = new List<string>();
        foreach (string lang in catalog.Languages)
        {
            if (lang == PseudoLocale.Language)
                continue;
            HashSet<string> categories = CategoriesOf(lang);
            foreach ((string id, LocaleEntity e) in catalog.Entities(lang))
            {
                foreach ((string? attr, Pattern p) in AstUtil.Patterns(e.Ast))
                {
                    var pluralVars = new SortedSet<string>(StringComparer.Ordinal);
                    foreach (SelectExpression s in AstUtil.Selects(p))
                    {
                        if (!s.Variants.Any(v => v.Type == VariantType.Identifier && AstUtil.IsPluralCategory(v.Key.ToString())))
                            continue;
                        string? name = AstUtil.SelectorVariable(s.Selector);
                        if (name is null)
                            continue;
                        pluralVars.Add(name);
                        Variant def = s.Variants.First(v => v.IsDefault);
                        if (def.Key.ToString() != "other")
                            problems.Add($"L4 {lang}: '{id}{Dot(attr)}' default variant of ${name} must be *[other]");
                        foreach (Variant v in s.Variants)
                        {
                            string key = v.Key.ToString();
                            if (v.Type == VariantType.Identifier && AstUtil.IsPluralCategory(key) && !categories.Contains(key))
                                problems.Add($"L4 {lang}: '{id}{Dot(attr)}' category [{key}] does not exist in {lang}");
                        }
                    }
                    if (pluralVars.Count > 0)
                        FormatAll(loc, lang, id, attr, e, pluralVars, problems);
                }
            }
        }
        return problems;
    }

    static string Dot(string? attr) => attr is null ? "" : "." + attr;

    static HashSet<string> CategoriesOf(string lang)
    {
        var set = new HashSet<string>(StringComparer.Ordinal) { "other" };
        Func<PluralOperands, PluralCategory> f = RuleTable.GetPluralFunc(BundleFactory.PluralCulture(lang).Split('-')[0], RuleType.Cardinal);
        foreach (string n in ProbeNumbers())
        {
            if (n.TryPluralOperands(out PluralOperands? ops) && ops is not null)
                set.Add(f(ops).ToString().ToLowerInvariant());
        }
        return set;
    }

    static IEnumerable<string> ProbeNumbers()
    {
        for (int i = 0; i <= 200; i++)
            yield return i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (string f in new[] { "0.5", "1.0", "1.5", "2.5", "5.5", "11.5", "21.5", "0.1", "100.5", "1000000" })
            yield return f;
    }

    static void FormatAll(Loc loc, string lang, string id, string? attr, LocaleEntity e, SortedSet<string> pluralVars, List<string> problems)
    {
        var other = new SortedSet<string>(StringComparer.Ordinal);
        var funcs = new SortedSet<string>(StringComparer.Ordinal);
        foreach ((_, Pattern p) in AstUtil.Patterns(e.Ast))
            AstUtil.CollectUsage(p, other, funcs, e.IsTerm);
        foreach ((_, Pattern p) in AstUtil.Patterns(e.Ast))
            foreach (SelectExpression s in AstUtil.Selects(p))
                if (AstUtil.SelectorVariable(s.Selector) is { } sv)
                    other.Add(sv);
        other.ExceptWith(pluralVars);

        var bundle = loc.BundleOf(lang)!;
        foreach (string n in ProbeNumbers())
        {
            var args = new Dictionary<string, IFluentType>(StringComparer.Ordinal);
            foreach (string v in pluralVars)
                args[v] = Numbers.Wrap(bundle, n);
            foreach (string v in other)
                args[v] = Dummy(v, funcs);
            IList<FluentError>? errors;
            string? text;
            if (!bundle.TryGetMessage(id.TrimStart('-'), attr, args, out errors, out text) || text is null || text.Contains("[["))
            {
                // Terms are not formatted through TryGetMessage; they have no plural selectors in practice.
                if (!e.IsTerm)
                    problems.Add($"L4 {lang}: '{id}{Dot(attr)}' fails for n={n}: {errors?.FirstOrDefault()}");
                return;
            }
        }
    }

    // Placeholder values for variables that do not drive plural forms.
    static IFluentType Dummy(string name, SortedSet<string> funcs) =>
        name.Contains("date", StringComparison.OrdinalIgnoreCase) ? new GameDate(1945, 2, 9)
        : name is "actor" or "buyer" or "seller" or "who" or "country" ? new DataRef("actor-SUN")
        : new FluentString("x");

    /// <summary>L6: length budgets by the role of the string (§13.2) and the data `.short` / state-word limits.</summary>
    public static List<string> Budgets(LocaleCatalog catalog)
    {
        var problems = new List<string>();
        var nameLength = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string lang in BaseLanguages)
        {
            int max = 0;
            foreach ((string id, LocaleEntity e) in catalog.Entities(lang))
            {
                if (e.Kind != KeyKind.Actor)
                    continue;
                int len = 0;
                foreach ((string? attr, Pattern p) in AstUtil.Patterns(e.Ast))
                {
                    if (attr is null || attr == "short")
                        len = attr == "short" ? AstUtil.MaxLength(p, 0) : Math.Max(len, AstUtil.MaxLength(p, 0));
                }
                max = Math.Max(max, len);
            }
            nameLength[lang] = max;
        }

        foreach ((string id, LocaleEntity en) in catalog.Entities("en"))
        {
            catalog.Entities("ru").TryGetValue(id, out LocaleEntity? ru);
            if (en.Kind == KeyKind.Ui && RoleOf(id) is { } role && LengthBudgets.OfRole(role) is { } budget)
            {
                int longest = Longest(en, ru, null, nameLength);
                if (LengthBudgets.Fits(longest, budget))
                    continue;
                int shortLongest = Longest(en, ru, "short", nameLength);
                if (shortLongest < 0)
                    problems.Add($"L6: '{id}' needs ceil(1.3*{longest}) <= {budget} and has no .short in en and ru");
                else if (!LengthBudgets.Fits(shortLongest, budget))
                    problems.Add($"L6: '{id}.short' needs ceil(1.3*{shortLongest}) <= {budget}");
            }
            else if (en.Kind != KeyKind.Ui)
            {
                int? shortEn = LengthOf(en, "short", nameLength["en"]);
                int? shortRu = ru is null ? null : LengthOf(ru, "short", nameLength["ru"]);
                if (shortEn > LengthBudgets.DataShort || shortRu > LengthBudgets.DataShort)
                    problems.Add($"L6: '{id}.short' is longer than {LengthBudgets.DataShort} (en {shortEn}, ru {shortRu})");
            }
            if (id.StartsWith("enum-state_word-", StringComparison.Ordinal))
            {
                int? e1 = LengthOf(en, null, nameLength["en"]);
                int? r1 = ru is null ? null : LengthOf(ru, null, nameLength["ru"]);
                if (e1 > LengthBudgets.StateWordEn)
                    problems.Add($"L6: '{id}' en is {e1} > {LengthBudgets.StateWordEn}");
                if (r1 > LengthBudgets.StateWordRu)
                    problems.Add($"L6: '{id}' ru is {r1} > {LengthBudgets.StateWordRu}");
            }
        }
        return problems;
    }

    static string? RoleOf(string id)
    {
        int cut = id.LastIndexOf('-');
        return cut > 0 ? id[(cut + 1)..] : null;
    }

    // Longest of the en and ru versions of a field; -1 if either lacks the attribute.
    static int Longest(LocaleEntity en, LocaleEntity? ru, string? attr, Dictionary<string, int> nameLength)
    {
        int? a = LengthOf(en, attr, nameLength["en"]);
        int? b = ru is null ? a : LengthOf(ru, attr, nameLength["ru"]);
        return a is null || b is null ? -1 : Math.Max(a.Value, b.Value);
    }

    static int? LengthOf(LocaleEntity e, string? attr, int nameLength)
    {
        foreach ((string? a, Pattern p) in AstUtil.Patterns(e.Ast))
        {
            if (a == attr)
                return AstUtil.MaxLength(p, nameLength);
        }
        return null;
    }
}
