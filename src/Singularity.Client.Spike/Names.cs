using System.Collections.Generic;
using Singularity.Core.Data;

namespace Singularity.Client.Spike;

/// <summary>Display names (no localization keys yet: draft strings from node ids) and the actor-to-country table.</summary>
public static class Names
{
    // Slice actor ids (scenario_1945.yaml) -> Natural Earth country names.
    public static readonly Dictionary<string, string> CountryOfActor = new()
    {
        ["USA"] = "United States of America", ["GBR"] = "United Kingdom", ["SUN"] = "Russia", ["FRA"] = "France", ["SWE"] = "Sweden",
    };
    public static readonly Dictionary<string, string> RuOfActor = new()
    {
        ["USA"] = "США", ["GBR"] = "Великобритания", ["SUN"] = "СССР", ["FRA"] = "Франция", ["SWE"] = "Швеция",
    };

    public static string Node(GameData d, int number)
    {
        for (int i = 0; i < d.Nodes.Count; i++)
            if (d.Nodes.Number[i] == number) return Short(d.Nodes.Id[i]);
        return "#" + number;
    }

    public static string Short(string id)
    {
        int i = id.IndexOf('.');
        return i >= 0 ? id[(i + 1)..] : id;
    }

    public static readonly string[] Stage = { "—", "проект", "опытный", "серия", "в эксплуатации", "снят" };
    public static readonly string[] Profile = { "качество", "массовость", "адаптивный" };
}
