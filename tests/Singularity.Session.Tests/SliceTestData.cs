using Singularity.Core.Data;
using Singularity.Session;

namespace Singularity.Session.Tests;

/// <summary>The core mod loaded once for all tests of the assembly (immutable).</summary>
internal static class SliceTestData
{
    public static string RepoRoot { get; } = FindRepoRoot();
    public static readonly GameData Data = ModLoader.Load(Path.Combine(RepoRoot, "mods", "core"));

    public static SliceSession Create(SliceRunConfig? config = null) => SliceSession.Create(Data, config ?? new SliceRunConfig { Seed = 7 });

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Singularity.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Singularity.sln not found above the test binaries.");
    }
}
