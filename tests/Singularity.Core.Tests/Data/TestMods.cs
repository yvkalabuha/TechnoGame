namespace Singularity.Core.Tests.Data;

/// <summary>Paths of the repository mods and throw-away mod directories for loader tests.</summary>
internal sealed class TestMods : IDisposable
{
    public static string RepoRoot { get; } = FindRepoRoot();
    public static string CoreDir => Path.Combine(RepoRoot, "mods", "core");
    public static string SchemaDir => Path.Combine(RepoRoot, "mods", "schemas");
    public static string SliceDataDir => Path.Combine(RepoRoot, "docs", "research", "data", "jet-slice");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "singularity-mod-tests", Guid.NewGuid().ToString("N"));

    public TestMods() => Directory.CreateDirectory(_root);

    /// <summary>Copy of mods/core; <paramref name="edit"/> may rewrite file texts by name.</summary>
    public string CopyCore(Func<string, string, string>? edit = null)
    {
        string dir = Path.Combine(_root, "core");
        Directory.CreateDirectory(dir);
        foreach (string file in Directory.GetFiles(CoreDir))
        {
            string name = Path.GetFileName(file);
            string text = File.ReadAllText(file);
            File.WriteAllText(Path.Combine(dir, name), edit is null ? text : edit(name, text));
        }
        return dir;
    }

    /// <summary>Writes a mod directory: mod.yaml plus data files (name, text) listed in order.</summary>
    public string Mod(string id, string dependencies, params (string Name, string Text)[] files)
    {
        string dir = Path.Combine(_root, id);
        Directory.CreateDirectory(dir);
        string list = string.Join(", ", files.Select(f => f.Name));
        File.WriteAllText(Path.Combine(dir, "mod.yaml"),
            $"id: {id}\nversion: \"1.0.0\"\ndependencies: {dependencies}\nfiles: [{list}]\n");
        foreach ((string name, string text) in files)
            File.WriteAllText(Path.Combine(dir, name), text);
        return dir;
    }

    /// <summary>
    /// A product_class file that drops generation G4 of jet_fighter (keeps the core G1–G3 rows, without the G4 engine and the Mach 2
    /// airframe, whose <c>eng_gen</c> and class would exceed G_max = 3), so a test mod can delete the otherwise referenced node
    /// s8.app.jet_fighter_g4.
    /// </summary>
    public static (string Name, string Text) DropG4Class()
    {
        string[] lines = File.ReadAllLines(Path.Combine(CoreDir, "product_classes.yaml"));
        int from = Array.FindIndex(lines, l => l == "    slots:"), to = Array.FindIndex(lines, l => l.StartsWith("    # ---- Product-layer inputs", StringComparison.Ordinal));
        string[] slots = [.. lines[from..to].Where(l => !l.Contains("{id: eng_g4,", StringComparison.Ordinal) && !l.Contains("{id: af_mach2,", StringComparison.Ordinal)
            && !l.TrimStart().StartsWith('#'))];
        string[] rows = [.. lines.Where(l => l.TrimStart().StartsWith("- {gen: ", StringComparison.Ordinal) && !l.Contains("gen: 4", StringComparison.Ordinal))];
        Assert.Equal(3, rows.Length);
        return ("classes.yaml", "kind: product_class\npatch:\n  - id: jet_fighter\n" + string.Join("\n", slots) + "\n    generations:\n" + string.Join("\n", rows) + "\n");
    }

    /// <summary>Replaces exactly one occurrence, so a test cannot silently edit nothing.</summary>
    public static string ReplaceOnce(string text, string from, string to)
    {
        int at = text.IndexOf(from, StringComparison.Ordinal);
        Assert.True(at >= 0, $"'{from}' not found");
        Assert.True(text.IndexOf(from, at + 1, StringComparison.Ordinal) < 0, $"'{from}' is not unique");
        return string.Concat(text.AsSpan(0, at), to, text.AsSpan(at + from.Length));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Singularity.sln")))
                return dir.FullName;
        }
        throw new DirectoryNotFoundException("Singularity.sln not found above the test output directory.");
    }
}
