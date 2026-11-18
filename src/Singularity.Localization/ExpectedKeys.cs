using Singularity.Core.Data;

namespace Singularity.Localization;

/// <summary>Fluent ids that the loaded game data requires (L1) and the only data-backed ids that may exist (L2).</summary>
public static class ExpectedKeys
{
    /// <summary>Display enumerations of the data names (ADR-0018 §1: by data name, not by C# member).</summary>
    public static IEnumerable<(string Table, string Value)> EnumValues()
    {
        foreach ((string n, _) in DataNames.Dimensions) yield return ("dimension", n);
        foreach ((string n, _) in DataNames.Hazards) yield return ("hazard", n);
        foreach ((string n, _) in DataNames.ActorClasses) yield return ("actor_class", n);
        foreach ((string n, _) in DataNames.Variants) yield return ("variant", n);
    }

    /// <summary>
    /// Natural Earth ADM0_A3 codes of the map labels (`-country-*`). They are map content, not game data, so
    /// this list is the contract: each needs both languages, a gender and the Russian cases.
    /// </summary>
    public static readonly string[] MapCountries =
    [
        "AFG", "AGO", "ARG", "AUS", "BGD", "BLR", "BOL", "BRA", "BWA", "CAF", "CAN", "CHL", "CHN", "CMR", "COD", "COG",
        "COL", "CUB", "DEU", "DZA", "ECU", "EGY", "ESP", "ETH", "FIN", "FRA", "GAB", "GBR", "GHA", "GRL", "GUY", "IDN",
        "IND", "IRN", "IRQ", "ISL", "ITA", "JPN", "KAZ", "KEN", "KHM", "KOR", "LAO", "LBY", "MAR", "MDG", "MEX", "MLI",
        "MMR", "MNG", "MOZ", "MRT", "MYS", "NAM", "NER", "NGA", "NOR", "NPL", "NZL", "OMN", "PAK", "PER", "PHL", "PNG",
        "POL", "PRK", "PRY", "ROU", "RUS", "SAU", "SDN", "SOM", "SWE", "SYR", "TCD", "THA", "TKM", "TUR", "TZA", "UGA",
        "UKR", "URY", "USA", "UZB", "VEN", "VNM", "YEM", "ZAF", "ZMB", "ZWE",
    ];

    /// <summary>Every entity of the game data with a visible field, as (id as written in files, kind).</summary>
    public static SortedDictionary<string, KeyKind> Of(GameData data)
    {
        var keys = new SortedDictionary<string, KeyKind>(StringComparer.Ordinal);
        NodeTable n = data.Nodes;
        for (int r = 0; r < n.Count; r++)
            keys[FluentIds.Node(n.Id[r])] = KeyKind.Node;

        ProductClassTable pc = data.ProductClasses;
        for (int c = 0; c < pc.ClassId.Length; c++)
            keys[FluentIds.ProductClass(pc.ClassId[c])] = KeyKind.ProductClass;
        for (int s = 0; s < pc.SlotId.Length; s++)
            keys[FluentIds.Slot(pc.ClassId[pc.SlotClass[s]], pc.SlotId[s])] = KeyKind.Slot;
        for (int a = 0; a < pc.AltId.Length; a++)
        {
            int slot = pc.AltSlot[a];
            keys[FluentIds.Alternative(pc.ClassId[pc.SlotClass[slot]], pc.SlotId[slot], pc.AltId[a])] = KeyKind.Alternative;
        }

        foreach (ScenarioData sc in data.Scenarios)
        {
            keys[FluentIds.Scenario(sc.Id)] = KeyKind.Scenario;
            foreach (string actor in sc.ActorId)
                keys["-" + FluentIds.Actor(actor)] = KeyKind.Actor;
        }
        foreach (string code in MapCountries)
            keys["-" + FluentIds.Country(code)] = KeyKind.Country;
        foreach ((string name, _) in DataNames.Clusters) keys[FluentIds.Cluster(name)] = KeyKind.Cluster;
        foreach ((string name, _) in DataNames.Disciplines) keys[FluentIds.Discipline(name)] = KeyKind.Discipline;
        foreach ((string name, _) in DataNames.NodeTypes) keys[FluentIds.NodeType(name)] = KeyKind.NodeType;
        foreach ((string table, string value) in EnumValues()) keys[FluentIds.Enum(table, value)] = KeyKind.Enum;
        return keys;
    }
}
