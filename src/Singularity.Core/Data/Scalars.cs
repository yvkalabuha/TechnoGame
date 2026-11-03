using Singularity.Core.Data.Yaml;
using Singularity.Kernel;

namespace Singularity.Core.Data;

/// <summary>
/// Typed reads of scalars. Numbers go text → integer → <see cref="Fx6"/> without any binary
/// floating point (ADR-0003 R1); more than 6 fractional digits is an error, never rounded.
/// </summary>
internal static class Scalars
{
    public static string Str(YNode node)
    {
        YScalar s = Scalar(node);
        if (s.Kind != ScalarKind.String)
            throw DataLoadException.At(node, $"'{s.Value}' is a {s.Kind.ToString().ToLowerInvariant()} here; quote it to use it as text");
        if (s.Value.Length == 0)
            throw DataLoadException.At(node, "empty string");
        return s.Value;
    }

    public static bool Bool(YNode node)
    {
        YScalar s = Scalar(node);
        if (s.Kind != ScalarKind.Bool)
            throw DataLoadException.At(node, $"expected true or false, found '{s.Value}'");
        return s.Value == "true";
    }

    public static Fx6 Fixed(YNode node)
    {
        YScalar s = Scalar(node);
        if (s.Kind != ScalarKind.Number)
            throw DataLoadException.At(node, $"expected a plain number, found '{s.Value}'");
        return ParseFixed(s.Value) ?? throw DataLoadException.At(node, $"'{s.Value}' has more than 6 fractional digits or does not fit Fx6");
    }

    public static int Int(YNode node)
    {
        YScalar s = Scalar(node);
        if (s.Kind != ScalarKind.Number || s.Value.Contains('.'))
            throw DataLoadException.At(node, $"expected an integer, found '{s.Value}'");
        if (!int.TryParse(s.Value, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int value))
            throw DataLoadException.At(node, $"integer '{s.Value}' is out of range");
        return value;
    }

    public static T Enum<T>(YNode node, (string Name, T Value)[] table) where T : struct, System.Enum
    {
        string text = Str(node);
        foreach ((string name, T value) in table)
        {
            if (string.Equals(name, text, StringComparison.Ordinal))
                return value;
        }
        throw DataLoadException.At(node, $"unknown value '{text}'; expected one of: {string.Join(", ", table.Select(t => t.Name))}");
    }

    public static YSeq Seq(YNode node) =>
        node as YSeq ?? throw DataLoadException.At(node, "expected a list");

    public static YMap Map(YNode node) =>
        node as YMap ?? throw DataLoadException.At(node, "expected a mapping");

    /// <summary>Exact decimal text (plain-decimal grammar) to Fx6; null if not representable.</summary>
    public static Fx6? ParseFixed(string text)
    {
        if (!StrictYaml.IsPlainDecimal(text))
            return null;
        bool negative = text[0] == '-';
        string digits = negative ? text[1..] : text;
        int dot = digits.IndexOf('.');
        string whole = dot < 0 ? digits : digits[..dot];
        string fraction = dot < 0 ? "" : digits[(dot + 1)..];
        if (fraction.Length > 6)
            return null;
        try
        {
            long raw = checked(ParseDigits(whole) * Fx6.Scale + ParseDigits(fraction.PadRight(6, '0')));
            return Fx6.FromRaw(negative ? -raw : raw);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static long ParseDigits(string digits)
    {
        long value = 0;
        foreach (char c in digits)
            value = checked(value * 10 + (c - '0'));
        return value;
    }

    private static YScalar Scalar(YNode node) =>
        node as YScalar ?? throw DataLoadException.At(node, "expected a scalar value");
}

/// <summary>
/// Reads a mapping field by field and rejects fields nobody asked for (unknown or misspelt keys
/// are load errors, as with <c>additionalProperties: false</c> in the schemas).
/// </summary>
internal sealed class MapReader
{
    private readonly YMap _map;
    private readonly bool[] _used;

    public MapReader(YNode node)
    {
        _map = Scalars.Map(node);
        _used = new bool[_map.Entries.Count];
    }

    public YMap Node => _map;

    public YNode? Optional(string key)
    {
        for (int i = 0; i < _map.Entries.Count; i++)
        {
            if (string.Equals(_map.Entries[i].Key, key, StringComparison.Ordinal))
            {
                _used[i] = true;
                return _map.Entries[i].Value;
            }
        }
        return null;
    }

    public YNode Required(string key) =>
        Optional(key) ?? throw DataLoadException.At(_map, $"missing field '{key}'");

    public string Str(string key) => Scalars.Str(Required(key));
    public bool Bool(string key) => Scalars.Bool(Required(key));
    public int Int(string key) => Scalars.Int(Required(key));
    public Fx6 Fixed(string key) => Scalars.Fixed(Required(key));
    public YSeq Seq(string key) => Scalars.Seq(Required(key));
    public T Enum<T>(string key, (string Name, T Value)[] table) where T : struct, System.Enum => Scalars.Enum(Required(key), table);

    /// <summary>Call after reading: any field not read is unknown.</summary>
    public void End()
    {
        for (int i = 0; i < _used.Length; i++)
        {
            if (!_used[i])
                throw DataLoadException.At(_map.Entries[i].Value, $"unknown field '{_map.Entries[i].Key}'");
        }
    }
}
