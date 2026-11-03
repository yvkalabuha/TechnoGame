using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Singularity.Core.Data.Yaml;

/// <summary>
/// Reads the strict YAML subset of ADR-0010 from the YamlDotNet event stream. No deserializer is
/// used, so no value is typed implicitly: scalars keep their text and a <see cref="ScalarKind"/>.
/// Rejected loudly: anchors, aliases, merge keys, tags, several documents, non-scalar or duplicate
/// keys, empty/null values, YAML 1.1 implicit booleans (yes/no/on/off/y/n...) and plain scalars
/// that look numeric but are not plain decimals (1e5, 0x1F, 1_000, .inf, 1945-09...). Such text
/// must be quoted.
/// </summary>
public static class StrictYaml
{
    // YAML 1.1 booleans and null forms. Only the lower-case true/false are accepted as booleans.
    private static readonly string[] ImplicitBoolOrNull =
    [
        "y", "Y", "yes", "Yes", "YES", "n", "N", "no", "No", "NO",
        "on", "On", "ON", "off", "Off", "OFF",
        "True", "TRUE", "False", "FALSE",
        "~", "null", "Null", "NULL",
    ];

    public static YNode Parse(string text, string file)
    {
        var parser = new Parser(new StringReader(text));
        try
        {
            Next<StreamStart>(parser, file);
            ParsingEvent first = Next(parser, file);
            if (first is not DocumentStart)
                throw new DataLoadException($"{file}: empty document");

            YNode root = Read(parser, Next(parser, file), file);

            Next<DocumentEnd>(parser, file);
            if (Next(parser, file) is not StreamEnd)
                throw new DataLoadException($"{file}: more than one YAML document");
            return root;
        }
        catch (YamlException e)
        {
            throw new DataLoadException(
                string.Create(CultureInfo.InvariantCulture, $"{file}:{e.Start.Line}: YAML syntax error: {e.Message}"), e);
        }
    }

    /// <summary>Kind of a plain scalar; throws for implicit booleans, nulls and ambiguous numbers.</summary>
    public static ScalarKind ClassifyPlain(string value, string where)
    {
        if (value is "true" or "false")
            return ScalarKind.Bool;
        if (IsPlainDecimal(value))
            return ScalarKind.Number;
        if (value.Length == 0)
            throw new DataLoadException($"{where}: empty value is not allowed");
        if (Array.IndexOf(ImplicitBoolOrNull, value) >= 0)
            throw new DataLoadException($"{where}: implicit bool/null '{value}' is not allowed; write true/false or quote the text");
        if (LooksNumeric(value))
            throw new DataLoadException($"{where}: ambiguous plain scalar '{value}'; write a plain decimal or quote the text");
        return ScalarKind.String;
    }

    /// <summary><c>-?(0|[1-9][0-9]*)(.[0-9]+)?</c></summary>
    public static bool IsPlainDecimal(string s)
    {
        int i = s.StartsWith('-') ? 1 : 0;
        int intStart = i;
        while (i < s.Length && char.IsAsciiDigit(s[i]))
            i++;
        int intLength = i - intStart;
        if (intLength == 0 || (intLength > 1 && s[intStart] == '0'))
            return false;
        if (i == s.Length)
            return true;
        if (s[i] != '.')
            return false;
        int fracStart = ++i;
        while (i < s.Length && char.IsAsciiDigit(s[i]))
            i++;
        return i == s.Length && i > fracStart;
    }

    // Text a YAML 1.1/1.2 resolver might read as a number: starts with a digit or a dot after an
    // optional sign (covers 1e5, 0x1F, 0o7, 1_000, 1:30, .5, .inf, .nan, dates).
    private static bool LooksNumeric(string s)
    {
        int i = s[0] is '+' or '-' ? 1 : 0;
        return i < s.Length && (char.IsAsciiDigit(s[i]) || s[i] == '.');
    }

    private static YNode Read(IParser parser, ParsingEvent ev, string file)
    {
        long line = ev.Start.Line;
        string where = string.Create(CultureInfo.InvariantCulture, $"{file}:{line}");
        switch (ev)
        {
            case AnchorAlias:
                throw new DataLoadException($"{where}: aliases are not allowed");
            case Scalar s:
                CheckNodeProperties(s.Anchor, s.Tag, where);
                ScalarKind kind = s.Style == ScalarStyle.Plain ? ClassifyPlain(s.Value, where) : ScalarKind.String;
                return new YScalar(file, line, s.Value, kind);
            case SequenceStart seq:
                CheckNodeProperties(seq.Anchor, seq.Tag, where);
                var items = new List<YNode>();
                for (ParsingEvent next = Next(parser, file); next is not SequenceEnd; next = Next(parser, file))
                    items.Add(Read(parser, next, file));
                return new YSeq(file, line, items);
            case MappingStart map:
                CheckNodeProperties(map.Anchor, map.Tag, where);
                return new YMap(file, line, ReadEntries(parser, file));
            default:
                throw new DataLoadException($"{where}: unexpected YAML event {ev.GetType().Name}");
        }
    }

    private static List<KeyValuePair<string, YNode>> ReadEntries(IParser parser, string file)
    {
        var entries = new List<KeyValuePair<string, YNode>>();
        for (ParsingEvent next = Next(parser, file); next is not MappingEnd; next = Next(parser, file))
        {
            string where = string.Create(CultureInfo.InvariantCulture, $"{file}:{next.Start.Line}");
            if (next is not Scalar key)
                throw new DataLoadException($"{where}: mapping keys must be scalars");
            CheckNodeProperties(key.Anchor, key.Tag, where);
            if (key.Style == ScalarStyle.Plain && key.Value == "<<")
                throw new DataLoadException($"{where}: merge keys are not allowed");
            if (key.Value.Length == 0)
                throw new DataLoadException($"{where}: empty key");
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].Key, key.Value, StringComparison.Ordinal))
                    throw new DataLoadException($"{where}: duplicate key '{key.Value}'");
            }
            entries.Add(new(key.Value, Read(parser, Next(parser, file), file)));
        }
        return entries;
    }

    private static void CheckNodeProperties(AnchorName anchor, TagName tag, string where)
    {
        if (!anchor.IsEmpty)
            throw new DataLoadException($"{where}: anchors are not allowed");
        if (!tag.IsEmpty)
            throw new DataLoadException($"{where}: tags are not allowed");
    }

    private static ParsingEvent Next(IParser parser, string file)
    {
        if (!parser.MoveNext() || parser.Current is null)
            throw new DataLoadException($"{file}: unexpected end of YAML");
        return parser.Current;
    }

    private static void Next<T>(IParser parser, string file) where T : ParsingEvent
    {
        ParsingEvent ev = Next(parser, file);
        if (ev is not T)
            throw new DataLoadException(string.Create(CultureInfo.InvariantCulture,
                $"{file}:{ev.Start.Line}: expected {typeof(T).Name}, found {ev.GetType().Name}"));
    }
}
