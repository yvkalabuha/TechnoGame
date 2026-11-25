using System.Collections.Immutable;

namespace Singularity.Core.Data;

/// <summary>Everything loaded from the mods: immutable after <see cref="ModLoader.Load"/>.</summary>
public sealed class GameData
{
    internal GameData(ImmutableArray<ModManifest> mods, NodeTable nodes, ProductClassTable productClasses,
        ImmutableArray<ScenarioData> scenarios, NodeHistoryTable history, ImmutableArray<string> warnings, InstituteTable institutes, IntelligenceData? intelligence = null)
    {
        Institutes = institutes;
        Intelligence = intelligence;
        Mods = mods;
        Nodes = nodes;
        ProductClasses = productClasses;
        Scenarios = scenarios;
        History = history;
        Warnings = warnings;
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

    /// <summary>Research institutes (own hash: not part of <see cref="DataHash"/>, so runs without them keep the data identity).</summary>
    public InstituteTable Institutes { get; }
    /// <summary>Display-only node history (#486); not part of <see cref="DataHash"/>, no simulation reads it.</summary>
    public NodeHistoryTable History { get; }

    /// <summary>Non-fatal load remarks (for example skipped history of a deleted node); not part of <see cref="DataHash"/>.</summary>
    public ImmutableArray<string> Warnings { get; }

    /// <summary>Scenarios ordered by ID.</summary>
    public ImmutableArray<ScenarioData> Scenarios { get; }

    /// <summary>The intelligence parameters, or null when no mod defines them. Outside <see cref="DataHash"/> (see <see cref="IntelligenceData"/>).</summary>
    public IntelligenceData? Intelligence { get; }

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
