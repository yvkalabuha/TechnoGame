using System.Globalization;
using Linguini.Bundle;
using Linguini.Bundle.Builder;
using Linguini.Bundle.Types;
using Linguini.Shared.Types.Bundle;
using Linguini.Syntax.Ast;
using Linguini.Syntax.Parser.Error;

namespace Singularity.Localization;

/// <summary>Builds the frozen Linguini bundle of one language with the deterministic functions of ADR-0018 §3.3, §1.</summary>
static class BundleFactory
{
    // The functions reach the frozen bundle that actually formats (it exists only after freezing).
    sealed class Holder
    {
        public IReadBundle? Self;
        public IReadBundle? Fallback;
    }

    /// <summary>
    /// The culture whose CLDR plural rules a language uses. The pseudo-locale takes the Russian rules (the richest
    /// pair of base languages), because Linguini throws for a tag it has no plural table for. Any other tag it
    /// cannot handle is a loud load error, found here and not at the first formatted number.
    /// </summary>
    internal static string PluralCulture(string lang)
    {
        string culture = lang == PseudoLocale.Language ? "ru" : lang;
        try
        {
            FluentBundle probe = FluentBundle.MakeUnchecked(new FluentBundleOption { Locales = { culture } });
            probe.AddResource("t = { $n ->\n    [one] a\n   *[other] b\n}\n", out _);
            probe.TryGetMessage("t", new Dictionary<string, IFluentType> { ["n"] = FluentNumber.FromString("1") }, out _, out _);
        }
        catch (Exception e) when (e is IndexOutOfRangeException or CultureNotFoundException)
        {
            throw new LocaleLoadException($"language '{lang}' has no CLDR plural rules or no ICU culture (Linguini: {e.GetType().Name})");
        }
        return culture;
    }

    /// <param name="fallback">Source-language bundle used by NAME and GENDER when the term is missing in <paramref name="lang"/>.</param>
    /// <param name="sourceEntities">
    /// Entities of the source language. Its `fmt-*` and `date-*` messages that <paramref name="lang"/> lacks are added to the
    /// bundle, so each piece of a date or number format (pattern, month name per case, separator) falls back on its own:
    /// a pattern refers to month names inside the bundle that formats it.
    /// </param>
    public static FrozenBundle Build(string lang, IEnumerable<LocaleEntity> entities, IEnumerable<LocaleEntity> sourceEntities, Func<IReadBundle?> fallback)
    {
        var holder = new Holder();
        FluentBundle bundle = FluentBundle.MakeUnchecked(new FluentBundleOption
        {
            Locales = { PluralCulture(lang) },
            UseIsolating = false, // RTL is out of scope; isolation marks would render as missing glyphs
        });
        bundle.AddFunctionOverriding("NUMBER", (a, n) => Numbers.Function(holder.Self!, a, n));
        bundle.AddFunctionOverriding("DATE", (a, n) => Numbers.DateFunction(holder.Self!, a, n));
        bundle.AddFunctionOverriding("NAME", (a, n) => Name(holder, a, n));
        bundle.AddFunctionOverriding("GENDER", (a, _) => Gender(holder, a));

        var entries = new List<IEntry>();
        var own = new HashSet<string>(StringComparer.Ordinal);
        foreach (LocaleEntity e in entities)
        {
            entries.Add(e.Ast);
            own.Add(e.Id);
        }
        foreach (LocaleEntity e in sourceEntities)
        {
            if ((e.Id.StartsWith("fmt-", StringComparison.Ordinal) || e.Id.StartsWith("date-", StringComparison.Ordinal)) && !own.Contains(e.Id))
                entries.Add(e.Ast);
        }
        if (!bundle.AddResource(new Resource(entries, new List<ParseError>()), out var errors))
            throw new LocaleLoadException($"{lang}: {string.Join("; ", errors)}");
        FrozenBundle frozen = bundle.ToFrozenBundle();
        holder.Self = frozen;
        holder.Fallback = fallback();
        return frozen;
    }

    static bool TryTerm(Holder h, string id, out IReadBundle owner, out AstTerm term)
    {
        if (h.Self!.TryGetAstTerm(id, out AstTerm? t))
        {
            owner = h.Self;
            term = t;
            return true;
        }
        if (h.Fallback is { } fb && fb.TryGetAstTerm(id, out t))
        {
            owner = fb;
            term = t;
            return true;
        }
        owner = h.Self;
        term = null!;
        return false;
    }

    static IFluentType Name(Holder h, IList<IFluentType> args, IDictionary<string, IFluentType> named)
    {
        if (args.Count == 0 || args[0] is not DataRef r)
            return new FluentString("{NAME?}");
        if (!TryTerm(h, r.TermId, out IReadBundle owner, out AstTerm term))
            return new FluentString($"[[-{r.TermId}]]");
        string caseName = Numbers.StrArg(named, "case", "nom");
        var vars = new Dictionary<string, IFluentType> { ["case"] = new FluentString(caseName) };
        return new FluentString(owner.FormatPattern(term.Value, vars, out _));
    }

    static IFluentType Gender(Holder h, IList<IFluentType> args)
    {
        if (args.Count == 0 || args[0] is not DataRef r || !TryTerm(h, r.TermId, out IReadBundle owner, out AstTerm term))
            return new FluentString("masc");
        foreach (Linguini.Syntax.Ast.Attribute a in term.Attributes)
        {
            if (a.Id.ToString() == "gender")
                return new FluentString(owner.FormatPattern(a.Value, new Dictionary<string, IFluentType>(), out _));
        }
        return new FluentString("masc");
    }
}
