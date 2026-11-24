using System.Collections.Generic;
using Singularity.Core.Data;

namespace Singularity.Client.Spike;

/// <summary>Display names. The strings live in locale/data.csv (keys node.&lt;id without scenario prefix&gt;, country.&lt;Natural Earth name&gt;,
/// actor.&lt;id&gt;): this class is the only reader, so the table can be swapped for the mods-side locale format.</summary>
public static class Names
{
    // Slice actor ids (scenario_1945.yaml) -> polity codes of the 1945 map (scenarios/1945/polities.yaml).
    public static readonly Dictionary<string, string> CountryOfActor = new()
    {
        ["USA"] = "USA", ["GBR"] = "GBR", ["SUN"] = "SUN", ["FRA"] = "FRA", ["SWE"] = "SWE",
    };

    public static string Actor(string id) => Loc.TOr("actor." + id, id);

    /// <summary>Node name by id; the id itself belongs in tooltips only.</summary>
    public static string Node(string id) => Loc.TOr("node." + Short(id), id);
    /// <summary>Historical anchor event of a node. ADR-0018 moved it out of Core into mods/core/locale (.ftl); until the client migrates to Fluent (CSV key node-anchor.*), an empty string means "not shown".</summary>
    public static string NodeAnchor(string id) => Loc.TOr("node-anchor." + Short(id), "");

    public static string Node(GameData d, int number)
    {
        for (int i = 0; i < d.Nodes.Count; i++)
            if (d.Nodes.Number[i] == number) return Node(d.Nodes.Id[i]);
        return "#" + number;
    }

    /// <summary>Map label of a polity of 1945 by its code (locale key polity.CODE), or null when the table has no name for it.</summary>
    public static string Country(string polity) => Loc.Has("polity." + polity) ? Loc.T("polity." + polity) : null;

    /// <summary>"s8.prc.name" -> "prc.name".</summary>
    public static string Short(string id)
    {
        int i = id.IndexOf('.');
        return i >= 0 ? id[(i + 1)..] : id;
    }
}
