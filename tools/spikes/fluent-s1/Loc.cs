using System.Globalization;
using System.Text;
using Linguini.Bundle;
using Linguini.Bundle.Builder;
using Linguini.Bundle.Errors;
using Linguini.Bundle.Types;
using Linguini.Shared.Types.Bundle;
using Linguini.Syntax.Ast;

namespace FluentS1;

/// <summary>Builds a FrozenBundle with deterministic NUMBER/DATE/NAME/GENDER functions.</summary>
public static class Loc
{
    /// <summary>Set after freezing: the functions reach the FrozenBundle that actually formats.</summary>
    sealed class Holder { public IReadBundle? Bundle; }

    public static FrozenBundle Build(string lang, string ftl, bool useIsolating)
    {
        var holder = new Holder();
        var bundle = FluentBundle.MakeUnchecked(new FluentBundleOption
        {
            Locales = { lang },
            UseIsolating = useIsolating,
        });
        bundle.AddFunctionOverriding("NUMBER", (a, n) => Number(holder.Bundle!, a, n));
        bundle.AddFunctionOverriding("DATE", (a, n) => Date(holder.Bundle!, a, n));
        bundle.AddFunctionOverriding("NAME", (a, n) => Name(holder.Bundle!, a, n));
        bundle.AddFunctionOverriding("GENDER", (a, n) => Gender(holder.Bundle!, a));
        if (!bundle.AddResource(ftl, out var errors)) throw new InvalidOperationException(string.Join("; ", errors));
        var frozen = bundle.ToFrozenBundle();
        holder.Bundle = frozen;
        return frozen;
    }

    public static string Msg(IReadBundle b, string id, params (string, IFluentType)[] args)
    {
        var d = args.ToDictionary(x => x.Item1, x => x.Item2);
        return b.TryGetMessage(id, d, out var errs, out var s) ? s : $"[[{id}]] {string.Join(";", errs)}";
    }

    static string Sep(IReadBundle b, string id)
    {
        if (!b.TryGetMessage(id, new Dictionary<string, IFluentType>(), out _, out var s)) throw new InvalidOperationException(id);
        return s;
    }

    static int IntArg(IDictionary<string, IFluentType> n, string key, int def) =>
        n.TryGetValue(key, out var v) && v is FluentNumber fn ? (int)fn.Value : def;

    static string StrArg(IDictionary<string, IFluentType> n, string key, string def) =>
        n.TryGetValue(key, out var v) ? v.AsString() : def;

    static IFluentType Number(IReadBundle b, IList<IFluentType> args, IDictionary<string, IFluentType> named)
    {
        if (args.Count == 0 || args[0] is not FluentNumber num) return new FluentString("{NUMBER?}");
        var minF = IntArg(named, "minimumFractionDigits", 0);
        var maxF = Math.Max(minF, IntArg(named, "maximumFractionDigits", 3));
        var grouping = StrArg(named, "useGrouping", "true") != "false";
        var percent = StrArg(named, "style", "decimal") == "percent";
        var d = (decimal)num.Value; // double -> decimal rounds to 15 significant digits: deterministic
        if (percent) d *= 100m;
        var canonical = Canonical(d, minF, maxF);
        // Plain decimal: a FluentNumber parsed from the canonical text keeps the visible fraction digits,
        // so Linguini's CLDR matching sees the formatted operands. Display text comes from FormatterFunc.
        if (!percent && grouping) return WithText(b, canonical);
        // Percent and ungrouped output are display strings: they cannot be plural selectors.
        var text = Display(b, canonical, grouping);
        return new FluentString(percent ? Msg(b, "fmt-percent", ("n", new FluentString(text))) : text);
    }

    /// <summary>Invariant text of a rounded decimal with minF..maxF fraction digits (no culture involved).</summary>
    static string Canonical(decimal d, int minF, int maxF)
    {
        d = decimal.Round(d, maxF, MidpointRounding.AwayFromZero);
        var s = d.ToString("F" + maxF, CultureInfo.InvariantCulture);
        var dot = s.IndexOf('.');
        if (dot < 0) return s;
        var end = s.Length;
        while (end > dot + 1 + minF && s[end - 1] == '0') end--;
        if (end == dot + 1) end = dot;
        return s[..end];
    }

    static FormattedNumber WithText(IReadBundle b, string canonical) =>
        new(FluentNumber.FromString(canonical), Display(b, canonical, true));

    /// <summary>Facade helper: wraps a numeric argument so a bare `{ $n }` prints the locale text.</summary>
    public static IFluentType Num(IReadBundle b, double value, int minF = 0, int maxF = 3) =>
        WithText(b, Canonical((decimal)value, minF, maxF));

    /// <summary>Applies locale separators to an invariant decimal text. Pure string work.</summary>
    public static string Display(IReadBundle b, string canonical, bool grouping)
    {
        var neg = canonical.StartsWith('-');
        if (neg) canonical = canonical[1..];
        var dot = canonical.IndexOf('.');
        var ip = dot < 0 ? canonical : canonical[..dot];
        var fp = dot < 0 ? "" : canonical[(dot + 1)..];
        var sb = new StringBuilder();
        if (neg) sb.Append(Sep(b, "fmt-number-minus"));
        if (grouping && ip.Length > 3)
        {
            var g = Sep(b, "fmt-number-group");
            for (var i = 0; i < ip.Length; i++)
            {
                if (i > 0 && (ip.Length - i) % 3 == 0) sb.Append(g);
                sb.Append(ip[i]);
            }
        }
        else sb.Append(ip);
        if (fp.Length > 0) sb.Append(Sep(b, "fmt-number-decimal")).Append(fp);
        return sb.ToString();
    }

    public static string Format(IReadBundle b, double value, int minF = 0, int maxF = 3) =>
        Display(b, Canonical((decimal)value, minF, maxF), true);

    static IFluentType Date(IReadBundle b, IList<IFluentType> args, IDictionary<string, IFluentType> named)
    {
        if (args.Count == 0 || args[0] is not GameDate gd) return new FluentString("{DATE?}");
        var id = "fmt-date-" + StrArg(named, "format", "dmy");
        // Day and year are passed as strings: no number formatting, no grouping.
        return new FluentString(Msg(b, id,
            ("day", new FluentString(gd.Day.ToString(CultureInfo.InvariantCulture))),
            ("month", (FluentNumber)(double)gd.Month),
            ("year", new FluentString(gd.Year.ToString(CultureInfo.InvariantCulture)))));
    }

    /// <summary>Type of the bundle NAME saw (proof that functions reach the FrozenBundle).</summary>
    public static string? SeenBundleType;

    static IFluentType Name(IReadBundle b, IList<IFluentType> args, IDictionary<string, IFluentType> named)
    {
        SeenBundleType ??= b.GetType().Name;
        if (args.Count == 0 || args[0] is not DataRef r) return new FluentString("{NAME?}");
        if (!b.TryGetAstTerm(r.TermId, out var term)) return new FluentString($"[[{r.TermId}]]");
        var caseName = StrArg(named, "case", "nom");
        // Attribute-per-case form (`.ins = ...`) wins if present; otherwise format the term value with $case.
        foreach (var at in term.Attributes)
            if (at.Id.ToString() == caseName)
                return new FluentString(b.FormatPattern(at.Value, new Dictionary<string, IFluentType>(), out _));
        var vars = new Dictionary<string, IFluentType> { ["case"] = new FluentString(caseName) };
        return new FluentString(b.FormatPattern(term.Value, vars, out _));
    }

    static IFluentType Gender(IReadBundle b, IList<IFluentType> args)
    {
        if (args.Count == 0 || args[0] is not DataRef r || !b.TryGetAstTerm(r.TermId, out var term))
            return new FluentString("masc");
        foreach (var at in term.Attributes)
            if (at.Id.ToString() == "gender")
                return new FluentString(b.FormatPattern(at.Value, new Dictionary<string, IFluentType>(), out _));
        return new FluentString("masc");
    }
}
