using Singularity.Core.Data;

namespace Singularity.Session.Tests;

/// <summary>Review of TASK-0171: the alliances table the market runs on is part of the identity of a run, also when the config gives none.</summary>
public class AlliancesIdentityTests
{
    private static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string f in Directory.GetFiles(from))
            File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (string d in Directory.GetDirectories(from))
            CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }

    [Fact]
    public void DefaultRun_CarriesTheTableOfTheDataInItsFingerprint_AndAnEditedFileChangesIt()
    {
        string root = Path.Combine(Path.GetTempPath(), "singularity-alliances-" + Guid.NewGuid().ToString("N"));
        try
        {
            string copy = Path.Combine(root, "core");
            CopyDir(Path.Combine(SliceTestData.RepoRoot, "mods", "core"), copy);
            string file = Path.Combine(copy, "scenarios", "1945", "alliances.yaml");
            File.WriteAllText(file, File.ReadAllText(file).Replace("  - {a: SWE, b: GBR, from: 1945, to: 1990}", "  - {a: SWE, b: GBR, from: 1945, to: 1980}"));
            GameData edited = ModLoader.Load(copy);
            var config = new SliceRunConfig { Seed = 7 };   // no Alliances given: the table comes from the data
            SliceWorld original = new(SliceTestData.Data, config), changed = new(edited, config);
            Assert.NotNull(original.Config.Alliances);
            Assert.NotEqual("empty", original.Config.Alliances!.Identity);
            Assert.NotEqual(original.Config.SnapshotFingerprint(), changed.Config.SnapshotFingerprint());
            // The same data gives the same identity, and a run with the table given explicitly is the same run.
            Assert.Equal(original.Config.SnapshotFingerprint(), new SliceWorld(SliceTestData.Data, config with { Alliances = original.Config.Alliances }).Config.SnapshotFingerprint());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LegacyRun_HasNoTableInItsFingerprint()
    {
        Assert.Null(new SliceWorld(SliceTestData.Data, SliceRunConfig.Legacy with { Seed = 7 }).Config.Alliances);
    }
}
