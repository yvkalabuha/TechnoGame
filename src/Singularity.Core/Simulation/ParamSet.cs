using System.Globalization;

namespace Singularity.Core.Simulation;

public enum ParamKind { Number, String, Bool, Json }

/// <summary>
/// One run parameter, kept as canonical text so models read numbers exactly
/// (no double round-trip): Number → normalised decimal, String → raw string,
/// Bool → "true"/"false", Json → canonical JSON of an array/object.
/// The host builds values from its input format (the Lab: from manifest JSON).
/// </summary>
public readonly record struct ParamValue(ParamKind Kind, string Text)
{
    /// <summary>Text for CSV/run.json labels.</summary>
    public override string ToString() => Text;
}

/// <summary>A missing, mistyped or out-of-range model parameter.</summary>
public sealed class ParamException : Exception
{
    public ParamException(string message) : base(message) { }
}

/// <summary>
/// Parameters of one run, ordered by key (ordinal) so every enumeration is deterministic.
/// Records which keys a model read, so unused parameters can be reported.
/// </summary>
public sealed class ParamSet
{
    private readonly SortedDictionary<string, ParamValue> _values;
    private readonly SortedSet<string> _read = new(StringComparer.Ordinal);

    public ParamSet(IEnumerable<KeyValuePair<string, ParamValue>> values)
    {
        _values = new SortedDictionary<string, ParamValue>(StringComparer.Ordinal);
        foreach (var kv in values)
            _values.Add(kv.Key, kv.Value);
    }

    /// <summary>All parameters in ordinal key order.</summary>
    public IEnumerable<KeyValuePair<string, ParamValue>> All => _values;

    public bool Contains(string key) => _values.ContainsKey(key);

    public IReadOnlyList<string> Unread() => _values.Keys.Where(k => !_read.Contains(k)).ToList();

    public string GetString(string key, string? fallback = null) =>
        TryGet(key, out ParamValue v) ? Expect(key, v, ParamKind.String).Text
        : fallback ?? throw Missing(key);

    public bool GetBool(string key, bool? fallback = null) =>
        TryGet(key, out ParamValue v) ? Expect(key, v, ParamKind.Bool).Text == "true"
        : fallback ?? throw Missing(key);

    public long GetLong(string key, long? fallback = null)
    {
        if (!TryGet(key, out ParamValue v))
            return fallback ?? throw Missing(key);
        if (!long.TryParse(Expect(key, v, ParamKind.Number).Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long result))
            throw new ParamException($"Parameter '{key}' = {v.Text} is not a 64-bit integer.");
        return result;
    }

    /// <summary>Fixed-point raw value (×10^6). More than 6 decimals is an error, never rounded.</summary>
    public long GetFx6Raw(string key, long? fallbackRaw = null)
    {
        if (!TryGet(key, out ParamValue v))
            return fallbackRaw ?? throw Missing(key);
        return ParseFx6Raw(Expect(key, v, ParamKind.Number).Text, key);
    }

    /// <summary>
    /// Nearest double to the decimal text (IEEE-correct parsing), the same value Python's float()
    /// gives. Only for behavioural coefficients that the model spec defines as doubles (ADR-0003).
    /// </summary>
    public double GetDouble(string key, double? fallback = null)
    {
        if (!TryGet(key, out ParamValue v))
            return fallback ?? throw Missing(key);
        double result = double.Parse(Expect(key, v, ParamKind.Number).Text, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (!double.IsFinite(result))
            throw new ParamException($"Parameter '{key}' = {v.Text} is outside the double range.");
        return result;
    }

    /// <summary>Canonical JSON of an array/object parameter; models parse it themselves.</summary>
    public string GetJson(string key) =>
        TryGet(key, out ParamValue v) ? Expect(key, v, ParamKind.Json).Text : throw Missing(key);

    public static long ParseFx6Raw(string decimalText, string key)
    {
        decimal d = decimal.Parse(decimalText, NumberStyles.Float, CultureInfo.InvariantCulture);
        decimal scaled = d * 1_000_000m;
        if (scaled != decimal.Truncate(scaled))
            throw new ParamException($"Parameter '{key}' = {decimalText} has more than 6 decimals (Fx6).");
        if (scaled < long.MinValue || scaled > long.MaxValue)
            throw new ParamException($"Parameter '{key}' = {decimalText} is outside the Fx6 range.");
        return (long)scaled;
    }

    private bool TryGet(string key, out ParamValue value)
    {
        _read.Add(key);
        return _values.TryGetValue(key, out value);
    }

    private static ParamValue Expect(string key, ParamValue v, ParamKind kind) =>
        v.Kind == kind ? v : throw new ParamException($"Parameter '{key}' must be {kind}, got {v.Kind} ({v.Text}).");

    private static ParamException Missing(string key) => new($"Required parameter '{key}' is missing.");
}
