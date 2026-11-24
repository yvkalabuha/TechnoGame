using Singularity.Core.Data;
using Singularity.Session;

namespace Singularity.Session.Tests;

/// <summary>The core mod loaded once for all tests of the assembly (immutable).</summary>
internal static class SliceTestData
{
    public static string RepoRoot { get; } = FindRepoRoot();
    public static readonly GameData Data = ModLoader.Load(Path.Combine(RepoRoot, "mods", "core"));

    /// <summary>
    /// The core mod before TASK-0133 (61 nodes, four generations; Fixtures/core-g4). Tests that pin identity with builds older than the G5/G6 data,
    /// and the control layers L_K, L_K0, L_P (which reject generations above 4), run on it.
    /// </summary>
    public static readonly GameData LegacyData = ModLoader.Load(Path.Combine(RepoRoot, "tests", "Singularity.Session.Tests", "Fixtures", "core-g4"));

    public static SliceSession CreateLegacy(SliceRunConfig? config = null) => SliceSession.Create(LegacyData, config ?? new SliceRunConfig { Seed = 7 });

    public static SliceSession Create(SliceRunConfig? config = null) => SliceSession.Create(Data, config ?? new SliceRunConfig { Seed = 7 });

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Singularity.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Singularity.sln not found above the test binaries.");
    }
}
