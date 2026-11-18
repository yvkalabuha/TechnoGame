using System.Globalization;
using System.Text;
using Linguini.Bundle;
using Linguini.Shared.Types.Bundle;

namespace Singularity.Localization;

/// <summary>
/// Deterministic number text (ADR-0018 §3.3, erratum #304): decimal arithmetic and invariant digits only,
/// separators come from the locale messages `fmt-number-*`. No CultureInfo formatting, so the output
/// does not depend on the operating system or the ICU version.
/// </summary>
static class Numbers
{
    /// <summary>Shown instead of a number that is NaN, infinite or outside the decimal range.</summary>
    public const string BadNumber = "[[number]]";

    public static bool TryDecimal(double value, out decimal result)
    {
        if (double.IsFinite(value) && Math.Abs(value) < 7.9e28)
        {
            result = (decimal)value;
            return true;
        }
        result = 0m;
        return false;
    }

    public static int IntArg(IDictionary<string, IFluentType> named, string key, int def) =>
        named.TryGetValue(key, out IFluentType? v) && v is FluentNumber fn ? (int)fn.Value : def;

    public static string StrArg(IDictionary<string, IFluentType> named, string key, string def) =>
        named.TryGetValue(key, out IFluentType? v) ? v.AsString() : def;

    /// <summary>NUMBER($x, minimumFractionDigits, maximumFractionDigits, useGrouping, style, notation).</summary>
    public static IFluentType Function(IReadBundle b, IList<IFluentType> args, IDictionary<string, IFluentType> named)
    {
        if (args.Count > 0 && args[0] is FluentString bad && ((IFluentType)bad).AsString() == BadNumber)
            return bad; // an argument the facade already replaced by the marker
        if (args.Count == 0 || args[0] is not FluentNumber num)
            return new FluentString("{NUMBER?}");
        int minF = IntArg(named, "minimumFractionDigits", 0);
        int maxF = Math.Max(minF, IntArg(named, "maximumFractionDigits", 3));
        bool grouping = StrArg(named, "useGrouping", "true") != "false";
        string style = StrArg(named, "style", "decimal");
        string notation = StrArg(named, "notation", "standard");
        if (!TryDecimal(num.Value, out decimal d)) // double -> decimal keeps 15 significant digits: deterministic
            return new FluentString(BadNumber);
        if (style == "percent")
            d *= 100m;

        if (notation == "compact")
            return new FluentString(Compact(b, d, style == "percent"));

        string canonical = Canonical(d, minF, maxF);
        // A plain grouped decimal keeps its visible fraction digits for plural selection.
        if (style == "decimal" && grouping)
            return Wrap(b, canonical);
        // Percent and ungrouped output are display strings: they cannot be plural selectors (erratum #304).
        string text = Display(b, canonical, grouping);
        return new FluentString(style == "percent" ? Msg(b, "fmt-percent", ("n", new FluentString(text))) : text);
    }

    /// <summary>Compact form: thousand, million, billion by the locale suffix messages; one fraction digit at most.</summary>
    static string Compact(IReadBundle b, decimal d, bool percent)
    {
        decimal abs = Math.Abs(d);
        (decimal div, string? suffix) = abs >= 1_000_000_000m ? (1_000_000_000m, "fmt-compact-billion")
            : abs >= 1_000_000m ? (1_000_000m, "fmt-compact-million")
            : abs >= 1_000m ? (1_000m, "fmt-compact-thousand")
            : (1m, null);
        string text = Display(b, Canonical(d / div, 0, 1), false);
        if (suffix is not null)
            text = Msg(b, suffix, ("n", new FluentString(text)));
        return percent ? Msg(b, "fmt-percent", ("n", new FluentString(text))) : text;
    }

    /// <summary>Invariant text of a rounded decimal with minF..maxF fraction digits (no culture involved).</summary>
    public static string Canonical(decimal d, int minF, int maxF)
    {
        d = decimal.Round(d, maxF, MidpointRounding.AwayFromZero);
        string s = d.ToString("F" + maxF.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        int dot = s.IndexOf('.');
        if (dot < 0)
            return s;
        int end = s.Length;
        while (end > dot + 1 + minF && s[end - 1] == '0')
            end--;
        if (end == dot + 1)
            end = dot;
        return s[..end];
    }

    public static FormattedNumber Wrap(IReadBundle b, string canonical) =>
        new(FluentNumber.FromString(canonical), Display(b, canonical, true));

    /// <summary>Wraps a bare numeric argument so that `{ $n }` prints the locale text and keeps its plural operands.</summary>
    public static FormattedNumber Wrap(IReadBundle b, decimal value, int minF = 0, int maxF = 3) =>
        Wrap(b, Canonical(value, minF, maxF));

    /// <summary>Applies locale separators to an invariant decimal text. Pure string work.</summary>
    public static string Display(IReadBundle b, string canonical, bool grouping)
    {
        bool neg = canonical.StartsWith('-');
        if (neg)
            canonical = canonical[1..];
        int dot = canonical.IndexOf('.');
        string ip = dot < 0 ? canonical : canonical[..dot];
        string fp = dot < 0 ? "" : canonical[(dot + 1)..];
        var sb = new StringBuilder();
        if (neg)
            sb.Append(Sep(b, "fmt-number-minus"));
        if (grouping && ip.Length > 3)
        {
            string g = Sep(b, "fmt-number-group");
            for (int i = 0; i < ip.Length; i++)
            {
                if (i > 0 && (ip.Length - i) % 3 == 0)
                    sb.Append(g);
                sb.Append(ip[i]);
            }
        }
        else
        {
            sb.Append(ip);
        }
        if (fp.Length > 0)
            sb.Append(Sep(b, "fmt-number-decimal")).Append(fp);
        return sb.ToString();
    }

    /// <summary>DATE($date, format: "dmy" | "my" | "y"): the layout is the message `fmt-date-&lt;format&gt;`.</summary>
    public static IFluentType DateFunction(IReadBundle b, IList<IFluentType> args, IDictionary<string, IFluentType> named)
    {
        if (args.Count == 0 || args[0] is not GameDate gd)
            return new FluentString("{DATE?}");
        string id = "fmt-date-" + StrArg(named, "format", "dmy");
        // Day and year are strings (no grouping); the month is a number for the month selector.
        return new FluentString(Msg(b, id,
            ("day", new FluentString(gd.Day.ToString(CultureInfo.InvariantCulture))),
            ("month", (FluentNumber)(double)gd.Month),
            ("year", new FluentString(gd.Year.ToString(CultureInfo.InvariantCulture)))));
    }

    // A missing separator message shows as the missing-key marker, like any other missing key.
    static string Sep(IReadBundle b, string id) => Msg(b, id);

    static string Msg(IReadBundle b, string id, params (string, IFluentType)[] args)
    {
        var d = args.ToDictionary(x => x.Item1, x => x.Item2);
        return b.TryGetMessage(id, d, out _, out string? s) ? s : $"[[{id}]]";
    }
}
