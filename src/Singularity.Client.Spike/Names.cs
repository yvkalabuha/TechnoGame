using System.Collections.Generic;
using Singularity.Core.Data;

namespace Singularity.Client.Spike;

/// <summary>Display names. The strings live in locale/data.csv (keys node.&lt;id without scenario prefix&gt;, country.&lt;Natural Earth name&gt;,
/// actor.&lt;id&gt;): this class is the only reader, so the table can be swapped for the mods-side locale format.</summary>
public static class Names
{
    // Slice actor ids (scenario_1945.yaml) -> Natural Earth country names.
    public static readonly Dictionary<string, string> CountryOfActor = new()
    {
        ["USA"] = "United States of America", ["GBR"] = "United Kingdom", ["SUN"] = "Russia", ["FRA"] = "France", ["SWE"] = "Sweden",
    };

    public static string Actor(string id) => Loc.TOr("actor." + id, id);

    /// <summary>Node name by id; the id itself belongs in tooltips only.</summary>
    public static string Node(string id) => Loc.TOr("node." + Short(id), id);

    public static string Node(GameData d, int number)
    {
        for (int i = 0; i < d.Nodes.Count; i++)
            if (d.Nodes.Number[i] == number) return Node(d.Nodes.Id[i]);
        return "#" + number;
    }

    /// <summary>Map label for a Natural Earth country, or null when the table has no name for it.</summary>
    public static string Country(string neName) => Loc.Has("country." + neName) ? Loc.T("country." + neName) : null;

    /// <summary>"s8.prc.name" -> "prc.name".</summary>
    public static string Short(string id)
    {
        int i = id.IndexOf('.');
        return i >= 0 ? id[(i + 1)..] : id;
    }
}
