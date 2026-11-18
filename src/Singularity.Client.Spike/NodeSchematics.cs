#nullable enable
using System;

namespace Singularity.Client.Spike;

/// <summary>Godot-free choice of the blueprint file of a node (docs/ui/assets/node-schematics/README, "selection rule").</summary>
public static class NodeSchematics
{
    // Table 2 of the README: nodes that have their own drawing (exact id match).
    static readonly (string Id, string File)[] Own =
    {
        ("s8.eng.turbojet_g1", "turbojet_g1"), ("s8.eng.turbojet_g2", "turbojet_g2"), ("s8.eng.turbojet_g3", "turbojet_g3"),
        ("s8.eng.afterburner", "afterburner"), ("s8.eng.swept_wing_airframe", "swept_wing"),
        ("s8.app.jet_fighter_g1", "jet_fighter_g1"), ("s8.app.jet_fighter_g2", "jet_fighter_g2"),
        ("s8.app.jet_fighter_g3", "jet_fighter_g3"), ("s8.app.jet_fighter_g4", "jet_fighter_g4"),
        ("s5.prc.superalloy_melt_g2", "superalloy_melt_g2"), ("s8.cmp.axial_compressor_assembly", "axial_compressor"),
    };

    /// <summary>File stem (no extension) for a node, or null for "no picture". 1) own file by exact id; 2) {type}-{discipline}; 3) none.</summary>
    /// <param name="exists">Whether a stem is present in the asset folder.</param>
    public static string? Pick(string nodeId, string type, string discipline, Func<string, bool> exists)
    {
        foreach (var (id, file) in Own)
            if (id == nodeId && exists(file)) return file;
        string combo = $"{type.ToLowerInvariant()}-{discipline.ToLowerInvariant()}";
        return exists(combo) ? combo : null;
    }
}
