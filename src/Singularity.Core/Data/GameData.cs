using System.Collections.Immutable;

namespace Singularity.Core.Data;

/// <summary>Everything loaded from the mods: immutable after <see cref="ModLoader.Load"/>.</summary>
public sealed class GameData
{
    internal GameData(ImmutableArray<ModManifest> mods, NodeTable nodes, ProductClassTable productClasses,
        ImmutableArray<ScenarioData> scenarios)
    {
        Mods = mods;
        Nodes = nodes;
        ProductClasses = productClasses;
        Scenarios = scenarios;
        using var w = new CanonicalWriter();
        w.Str("singularity-data/1");
        nodes.WriteCanonical(w);
        productClasses.WriteCanonical(w, nodes);
        w.Int(scenarios.Length);
        foreach (ScenarioData s in scenarios)
            s.WriteCanonical(w, nodes, productClasses);
        DataHash = w.FinishHex();
    }

    /// <summary>Mods in load order (dependencies first, ties by ID).</summary>
    public ImmutableArray<ModManifest> Mods { get; }
    public NodeTable Nodes { get; }
    public ProductClassTable ProductClasses { get; }

    /// <summary>Scenarios ordered by ID.</summary>
    public ImmutableArray<ScenarioData> Scenarios { get; }

    /// <summary>SHA-256 (hex) of the canonical form of all loaded tables (L1 data identity).</summary>
    public string DataHash { get; }

    public ScenarioData Scenario(string id)
    {
        foreach (ScenarioData s in Scenarios)
        {
            if (s.Id == id)
                return s;
        }
        throw new KeyNotFoundException($"Scenario '{id}' is not loaded.");
    }
}
