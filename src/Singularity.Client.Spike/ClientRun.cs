#nullable disable
using System.Collections.Immutable;
using System.IO;
using Singularity.Core.Data;
using Singularity.Core.Policy;
using Singularity.Session;

namespace Singularity.Client.Spike;

/// <summary>The configuration of the session the client plays (TASK-0156): the lever features are on for this run only; the global defaults are flipped later (TASK-0171).
/// Godot-free so that a test builds exactly the session the player gets.</summary>
public static class ClientRun
{
    /// <param name="root">Repository root (the mods and the alliances table live under it).</param>
    /// <param name="playerActorId">Actor id of the power the player plays, null = none (menu, benchmarks).</param>
    public static SliceRunConfig Config(GameData data, string root, ulong seed, string playerActorId)
    {
        int player = playerActorId == null ? -1 : data.Scenarios[0].ActorIndex(playerActorId);
        var cfg = new SliceRunConfig { Seed = seed, HashCheckpoints = false, ViewScope = ViewScope.Subscribed };
        if (player < 0) return cfg;
        return cfg with
        {
            PlayerActor = player,
            Reserve = true, Institutes = true, LicenceMarket = true,
            LicenceExternalActors = ImmutableArray.Create(player),
            AdviserActors = ImmutableArray.Create(player),
            Alliances = AllianceTable.Load(Path.Combine(root, "mods", "core", "scenarios", "1945", "alliances.yaml")),
        };
    }
}
