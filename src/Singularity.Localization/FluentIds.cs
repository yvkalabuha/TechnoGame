using System.Text;

namespace Singularity.Localization;

/// <summary>Kinds of Fluent entities (the closed list of ADR-0018 §1).</summary>
public enum KeyKind { Node, ProductClass, Slot, Alternative, Scenario, Actor, Country, Region, Cluster, Discipline, NodeType, Enum, Event, Ui, Fmt, Unit, Date }

/// <summary>A parsed Fluent id: its kind and the segments after the kind prefix.</summary>
public readonly record struct ParsedKey(KeyKind Kind, string[] Segments);

/// <summary>
/// Mapping between data ids and Fluent ids (ADR-0018 §1). Every '.' of a data id becomes '-'; data ids never
/// contain '-', so the mapping is a bijection. Terms (actor, country, region) are written with a leading '-'
/// in .ftl files; their lookup id (<see cref="DataRef"/>, Linguini) has none.
/// </summary>
public static class FluentIds
{
    static readonly string[] KindPrefixes =
        ["node", "pc", "scenario", "actor", "country", "region", "cluster", "discipline", "node_type", "enum", "event", "ui", "fmt", "unit", "date"];

    public static bool IsTermKind(KeyKind k) => k is KeyKind.Actor or KeyKind.Country or KeyKind.Region;

    // ---- data id -> Fluent id ----
    public static string Node(string nodeId) => "node-" + Map(nodeId);
    public static string ProductClass(string classId) => "pc-" + Seg(classId);
    public static string Slot(string classId, string slotId) => $"pc-{Seg(classId)}-slot-{Seg(slotId)}";
    public static string Alternative(string classId, string slotId, string altId) => $"pc-{Seg(classId)}-alt-{Seg(slotId)}-{Seg(altId)}";
    public static string Scenario(string id) => "scenario-" + Seg(id);
    public static string Cluster(string name) => "cluster-" + Seg(name);
    public static string Discipline(string name) => "discipline-" + Seg(name);
    public static string NodeType(string name) => "node_type-" + Seg(name);
    public static string Enum(string table, string value) => $"enum-{Seg(table)}-{Seg(value)}";

    /// <summary>Term id as the DataRef and the facade use it: no leading '-'.</summary>
    public static string Actor(string id) => "actor-" + Seg(id);
    public static string Country(string adm0A3) => "country-" + Seg(adm0A3);
    public static string Region(string id) => "region-" + Seg(id);

    /// <summary>Id as written in a file: terms get a leading '-'.</summary>
    public static string FileId(string lookupId) => TryParse(lookupId, out ParsedKey k) && IsTermKind(k.Kind) ? "-" + lookupId : lookupId;

    // ---- Fluent id -> data id ----
    public static string NodeIdOf(string key)
    {
        ParsedKey k = Parse(key);
        if (k.Kind != KeyKind.Node)
            throw new LocaleLoadException($"'{key}' is not a node key");
        return string.Join('.', k.Segments);
    }

    /// <summary>Segments of a key; throws <see cref="LocaleLoadException"/> if it breaks the grammar.</summary>
    public static ParsedKey Parse(string key) =>
        TryParse(key, out ParsedKey k) ? k : throw new LocaleLoadException($"id '{key}' does not match <kind>-<seg>(-<seg>)* with a known kind");

    public static bool TryParse(string key, out ParsedKey parsed)
    {
        parsed = default;
        string s = key.StartsWith('-') ? key[1..] : key;
        string[] parts = s.Split('-');
        if (parts.Length < 2 || Array.IndexOf(KindPrefixes, parts[0]) < 0)
            return false;
        for (int i = 1; i < parts.Length; i++)
        {
            if (!IsSegment(parts[i]))
                return false;
        }
        string[] segs = parts[1..];
        KeyKind kind = parts[0] switch
        {
            "node" => KeyKind.Node,
            "scenario" => KeyKind.Scenario,
            "actor" => KeyKind.Actor,
            "country" => KeyKind.Country,
            "region" => KeyKind.Region,
            "cluster" => KeyKind.Cluster,
            "discipline" => KeyKind.Discipline,
            "node_type" => KeyKind.NodeType,
            "enum" => KeyKind.Enum,
            "event" => KeyKind.Event,
            "ui" => KeyKind.Ui,
            "fmt" => KeyKind.Fmt,
            "unit" => KeyKind.Unit,
            "date" => KeyKind.Date,
            _ => KeyKind.ProductClass,
        };
        if (kind == KeyKind.ProductClass)
        {
            // pc-<class>, pc-<class>-slot-<slot>, pc-<class>-alt-<slot>-<alt>: positions are fixed.
            if (segs.Length == 1) { }
            else if (segs.Length == 3 && segs[1] == "slot") kind = KeyKind.Slot;
            else if (segs.Length == 4 && segs[1] == "alt") kind = KeyKind.Alternative;
            else return false;
        }
        parsed = new ParsedKey(kind, segs);
        return true;
    }

    /// <summary>Attributes allowed for a kind (ADR-0018 §1 table). Empty: the entity has a value only.</summary>
    public static IReadOnlyList<string> AllowedAttributes(KeyKind kind) => kind switch
    {
        KeyKind.Node => ["name", "short", "desc", "anchor", "history"],
        KeyKind.ProductClass => ["name", "short", "desc"],
        KeyKind.Slot or KeyKind.Alternative => ["name", "short"],
        KeyKind.Scenario => ["name", "desc"],
        KeyKind.Actor or KeyKind.Country or KeyKind.Region => ["gender", "short", "adj"],
        KeyKind.Cluster or KeyKind.Discipline or KeyKind.NodeType => ["name", "short"],
        KeyKind.Enum => ["short"],
        KeyKind.Event => ["title", "body"],
        KeyKind.Ui => ["short", "tip"],
        _ => [],
    };

    /// <summary>Attributes every entity of the kind must have in both base languages (L1).</summary>
    public static IReadOnlyList<string> RequiredAttributes(KeyKind kind) => kind switch
    {
        KeyKind.Node => ["name", "anchor"],
        KeyKind.ProductClass or KeyKind.Slot or KeyKind.Alternative or KeyKind.Scenario => ["name"],
        KeyKind.Actor or KeyKind.Country or KeyKind.Region => ["gender"],
        KeyKind.Cluster or KeyKind.Discipline or KeyKind.NodeType => ["name"],
        KeyKind.Event => ["title", "body"],
        _ => [],
    };

    /// <summary>Whether the entity must have a value (terms always do; messages of these kinds never).</summary>
    public static bool HasValue(KeyKind kind) => kind switch
    {
        KeyKind.Node or KeyKind.ProductClass or KeyKind.Slot or KeyKind.Alternative or KeyKind.Scenario
            or KeyKind.Cluster or KeyKind.Discipline or KeyKind.NodeType or KeyKind.Event => false,
        _ => true,
    };

    /// <summary>Whether an attribute name is valid for the kind (event options `opt-<id>` are open).</summary>
    public static bool IsAllowedAttribute(KeyKind kind, string attr)
    {
        if (kind == KeyKind.Event && attr.StartsWith("opt-", StringComparison.Ordinal) && attr.Length > 4)
            return IsSegment(attr[4..]);
        foreach (string a in AllowedAttributes(kind))
        {
            if (a == attr)
                return true;
        }
        return false;
    }

    static bool IsSegment(string s)
    {
        if (s.Length == 0)
            return false;
        foreach (char c in s)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c == '_'))
                return false;
        }
        return true;
    }

    // A data id that becomes a key: [A-Za-z][A-Za-z0-9_]*(\.[A-Za-z0-9_]+)*, else a loud error.
    static string Map(string dataId)
    {
        string[] parts = dataId.Split('.');
        if (dataId.Length == 0 || !char.IsAsciiLetter(dataId[0]) || parts.Any(p => !IsSegment(p)))
            throw new LocaleLoadException($"data id '{dataId}' cannot become a Fluent id");
        var sb = new StringBuilder(dataId.Length);
        foreach (char c in dataId)
            sb.Append(c == '.' ? '-' : c);
        return sb.ToString();
    }

    // Ids of classes, slots, alternatives, actors, scenarios and names have no '.'.
    static string Seg(string id)
    {
        if (id.Length == 0 || !char.IsAsciiLetter(id[0]) || !IsSegment(id))
            throw new LocaleLoadException($"id '{id}' cannot be a Fluent id segment");
        return id;
    }
}
