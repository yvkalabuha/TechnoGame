using Linguini.Bundle;
using Linguini.Bundle.Errors;
using Linguini.Bundle.Types;
using Linguini.Shared.Types.Bundle;

namespace Singularity.Localization;

/// <summary>
/// The facade the client calls (ADR-0018 §5). Fallback chain: selected language, then <c>en</c>, then the marker
/// <c>[[id]]</c> with one warning per id. All text is read from the loaded bundles; nothing here touches game state.
/// A call after construction only reads frozen bundles, so it is safe from many threads (spike S1 (д)).
/// </summary>
public sealed class Loc
{
    readonly Dictionary<string, FrozenBundle> _bundles = new(StringComparer.Ordinal);
    readonly Action<string> _warn;
    readonly HashSet<string> _warned = new(StringComparer.Ordinal);
    readonly object _warnLock = new();
    string _language;

    public Loc(LocaleCatalog catalog, string language, Action<string>? warn = null)
    {
        _warn = warn ?? (_ => { });
        // The source language first: the others use it for NAME and GENDER fallbacks.
        FrozenBundle? source = null;
        foreach (string lang in catalog.Languages.OrderBy(l => l != LocaleCatalog.SourceLanguage).ThenBy(l => l, StringComparer.Ordinal))
        {
            FrozenBundle b = BundleFactory.Build(lang, catalog.Entities(lang).Values, () => source);
            _bundles[lang] = b;
            if (lang == LocaleCatalog.SourceLanguage)
                source = b;
        }
        if (!_bundles.ContainsKey(language) && language != LocaleCatalog.SourceLanguage)
            throw new ArgumentException($"language '{language}' is not loaded", nameof(language));
        _language = language;
    }

    public string Language => _language;
    public IEnumerable<string> Languages => _bundles.Keys.Order(StringComparer.Ordinal);

    /// <summary>Raised after <see cref="SetLanguage"/> changed the language; the UI must re-read its text.</summary>
    public event Action? LanguageChanged;

    public void SetLanguage(string language)
    {
        if (!_bundles.ContainsKey(language))
            throw new ArgumentException($"language '{language}' is not loaded", nameof(language));
        if (language == _language)
            return;
        _language = language;
        LanguageChanged?.Invoke();
    }

    /// <summary>Bundle of one language (tests and tools).</summary>
    internal FrozenBundle? BundleOf(string lang) => _bundles.GetValueOrDefault(lang);

    /// <summary>Value of a message.</summary>
    public string T(string id, params (string Name, object Value)[] args) => Resolve(id, null, args);

    /// <summary>One field of an entity: <c>A("node-s8-eng-turbojet_g1", Field.Anchor)</c>.</summary>
    public string A(string id, Field field, params (string Name, object Value)[] args) =>
        Resolve(id, FieldNames.Attribute(field), args);

    /// <summary>
    /// Name of a term entity (actor, country, region) in a case; other fields read the attribute.
    /// <paramref name="case"/> is ignored where the language has no such case.
    /// </summary>
    public string Name(DataRef who, Field field = Field.Name, string @case = "nom")
    {
        string attribute = field == Field.Name ? "" : FieldNames.Attribute(field);
        foreach (string lang in Chain())
        {
            FrozenBundle b = _bundles[lang];
            if (!b.TryGetAstTerm(who.TermId, out var term))
                continue;
            // Terms have no messages: the value (Field.Name) and the attributes are read from the term itself.
            Linguini.Syntax.Ast.Pattern? pattern = field == Field.Name
                ? term.Value
                : term.Attributes.FirstOrDefault(a => a.Id.ToString() == attribute)?.Value;
            if (pattern is null)
                continue;
            var vars = new Dictionary<string, IFluentType> { ["case"] = new FluentString(@case) };
            string s = b.FormatPattern(pattern, vars, out IList<FluentError>? errors);
            if (errors is { Count: > 0 })
                Warn($"-{who.TermId}: {errors[0]}");
            return s;
        }
        return Missing(field == Field.Name ? $"-{who.TermId}" : $"-{who.TermId}.{attribute}");
    }

    /// <summary>A number in the locale text (separators from `fmt-number-*`).</summary>
    public string Number(double value, int minFractionDigits = 0, int maxFractionDigits = 3) =>
        Numbers.TryDecimal(value, out decimal d)
            ? Numbers.Display(Current, Numbers.Canonical(d, minFractionDigits, maxFractionDigits), true)
            : Numbers.BadNumber;

    public string Date(GameDate date, string format = "dmy") =>
        Numbers.DateFunction(Current, [date], new Dictionary<string, IFluentType> { ["format"] = new FluentString(format) }).AsString();

    FrozenBundle Current => _bundles[_language];

    IEnumerable<string> Chain()
    {
        yield return _language;
        if (_language != LocaleCatalog.SourceLanguage && _bundles.ContainsKey(LocaleCatalog.SourceLanguage))
            yield return LocaleCatalog.SourceLanguage;
    }

    string Resolve(string id, string? attribute, (string Name, object Value)[] args)
    {
        string full = attribute is null ? id : $"{id}.{attribute}";
        foreach (string lang in Chain())
        {
            FrozenBundle b = _bundles[lang];
            if (!b.TryGetAstMessage(id, out var message))
                continue;
            bool exists = attribute is null
                ? message.Value is not null
                : message.Attributes.Any(a => a.Id.ToString() == attribute);
            if (!exists)
                continue;
            IDictionary<string, IFluentType> map = Convert(b, args);
            IList<FluentError>? errors;
            string? text;
            bool ok = b.TryGetMessage(id, attribute, map, out errors, out text);
            if (!ok)
            {
                Warn($"{full} ({lang}): {errors?.FirstOrDefault()}");
                return Marker(full);
            }
            return text!;
        }
        return Missing(full);
    }

    static Dictionary<string, IFluentType> Convert(FrozenBundle b, (string Name, object Value)[] args)
    {
        var map = new Dictionary<string, IFluentType>(args.Length, StringComparer.Ordinal);
        foreach ((string name, object value) in args)
        {
            map[name] = value switch
            {
                IFluentType t => t,
                string s => new FluentString(s),
                int i => Numbers.Wrap(b, (decimal)i),
                long l => Numbers.Wrap(b, (decimal)l),
                double d => Numbers.TryDecimal(d, out decimal dd) ? Numbers.Wrap(b, dd) : new FluentString(Numbers.BadNumber),
                decimal m => Numbers.Wrap(b, m),
                _ => throw new ArgumentException($"argument '{name}' has unsupported type {value.GetType().Name}"),
            };
        }
        return map;
    }

    string Missing(string full)
    {
        Warn($"missing text '{full}'");
        return Marker(full);
    }

    static string Marker(string full) => $"[[{full}]]";

    void Warn(string message)
    {
        bool first;
        lock (_warnLock)
            first = _warned.Add(message);
        if (first)
            _warn(message);
    }
}
