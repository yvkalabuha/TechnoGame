using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Singularity.Lab;

/// <summary>
/// Canonical JSON form used for <c>manifest_hash</c>:
/// object keys sorted by ordinal UTF-16 order, no whitespace, duplicate keys rejected,
/// strings re-escaped by System.Text.Json's default encoder, numbers normalised as decimals
/// (<c>1</c>, <c>1.0</c>, <c>1e0</c> → <c>1</c>; <c>-0</c> → <c>0</c>). Numbers outside the
/// System.Decimal range are rejected rather than rounded.
/// Not RFC 8785 (JCS): JCS formats numbers as doubles, which would round 64-bit seeds.
/// </summary>
public static class CanonicalJson
{
    private static readonly JsonDocumentOptions Options = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 64,
    };

    public static string Canonicalize(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json, Options);
        return Canonicalize(doc.RootElement);
    }

    public static string Canonicalize(JsonElement element)
    {
        var sb = new StringBuilder();
        Write(element, sb, "$");
        return sb.ToString();
    }

    public static string Sha256Hex(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>Normalised decimal text of a JSON number token.</summary>
    public static string CanonicalNumber(string raw, string path)
    {
        if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value))
            throw new ManifestException($"{path}: number {raw} is outside the supported decimal range.");
        return NormalizeDecimal(value);
    }

    public static string NormalizeDecimal(decimal value)
    {
        // decimal.ToString never uses exponent notation; it keeps the scale ("1.50"), so trim it.
        string text = value.ToString(CultureInfo.InvariantCulture);
        if (text.Contains('.'))
            text = text.TrimEnd('0').TrimEnd('.');
        return text == "-0" ? "0" : text;
    }

    private static void Write(JsonElement e, StringBuilder sb, string path)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                var props = new List<(string Name, JsonElement Value)>();
                foreach (JsonProperty p in e.EnumerateObject())
                    props.Add((p.Name, p.Value));
                props.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                for (int i = 1; i < props.Count; i++)
                {
                    if (string.Equals(props[i].Name, props[i - 1].Name, StringComparison.Ordinal))
                        throw new ManifestException($"{path}: duplicate key \"{props[i].Name}\".");
                }
                sb.Append('{');
                for (int i = 0; i < props.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(JsonSerializer.Serialize(props[i].Name)).Append(':');
                    Write(props[i].Value, sb, $"{path}.{props[i].Name}");
                }
                sb.Append('}');
                break;
            case JsonValueKind.Array:
                sb.Append('[');
                int index = 0;
                foreach (JsonElement item in e.EnumerateArray())
                {
                    if (index > 0) sb.Append(',');
                    Write(item, sb, $"{path}[{index}]");
                    index++;
                }
                sb.Append(']');
                break;
            case JsonValueKind.String:
                sb.Append(JsonSerializer.Serialize(e.GetString()));
                break;
            case JsonValueKind.Number:
                sb.Append(CanonicalNumber(e.GetRawText(), path));
                break;
            case JsonValueKind.True:
                sb.Append("true");
                break;
            case JsonValueKind.False:
                sb.Append("false");
                break;
            case JsonValueKind.Null:
                sb.Append("null");
                break;
            default:
                throw new ManifestException($"{path}: unsupported JSON value kind {e.ValueKind}.");
        }
    }
}

public sealed class ManifestException : Exception
{
    public ManifestException(string message) : base(message) { }
}
