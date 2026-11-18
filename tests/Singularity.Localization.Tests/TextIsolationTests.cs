using Singularity.Core.Data;
using Singularity.Session;

namespace Singularity.Localization.Tests;

/// <summary>ADR-0018 §4, §7: text never enters Core data; deleting or changing locale/ changes neither DataHash nor StateHash.</summary>
public class TextIsolationTests : IDisposable
{
    readonly TestKit _kit = new();
    public void Dispose() => _kit.Dispose();

    string CopyCore(bool withLocale, Func<string, string>? editFtl = null)
    {
        string target = Path.Combine(_kit.Root, "core-" + Guid.NewGuid().ToString("N")[..6]);
        foreach (string file in Directory.GetFiles(TestKit.CoreDir, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(TestKit.CoreDir, file);
            bool locale = rel.StartsWith("locale" + Path.DirectorySeparatorChar, StringComparison.Ordinal);
            if (locale && !withLocale)
                continue;
            string dest = Path.Combine(target, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            if (locale && editFtl is not null && file.EndsWith(".ftl", StringComparison.Ordinal))
                File.WriteAllText(dest, editFtl(File.ReadAllText(file)));
            else
                File.Copy(file, dest);
        }
        return target;
    }

    static string StateHashAfterTicks(GameData data)
    {
        SliceSession s = SliceSession.Create(data, new SliceRunConfig { Seed = 7 });
        s.StepThroughMonth(3);
        return s.LastHash + "@" + s.Tick;
    }

    [Fact]
    public void DeletingOrEditingLocale_ChangesNeitherDataHashNorStateHash()
    {
        GameData withLocale = ModLoader.Load(CopyCore(withLocale: true));
        GameData without = ModLoader.Load(CopyCore(withLocale: false));
        GameData edited = ModLoader.Load(CopyCore(withLocale: true, t => t.Replace("Boundary layer", "Changed text", StringComparison.Ordinal) + "\nui-extra = x\n"));
        Assert.Equal(withLocale.DataHash, without.DataHash);
        Assert.Equal(withLocale.DataHash, edited.DataHash);
        string baseline = StateHashAfterTicks(withLocale);
        Assert.Equal(baseline, StateHashAfterTicks(without));
        Assert.Equal(baseline, StateHashAfterTicks(edited));
    }

    [Fact]
    public void AnchorEventIsAnUnknownField_AndNoTextColumnExistsInNodeTable()
    {
        string nodes = File.ReadAllText(Path.Combine(TestKit.CoreDir, "nodes.yaml"));
        Assert.DoesNotContain("event:", nodes, StringComparison.Ordinal);
        string dir = Path.Combine(_kit.Root, "with-event");
        Directory.CreateDirectory(dir);
        foreach (string f in Directory.GetFiles(TestKit.CoreDir))
            File.Copy(f, Path.Combine(dir, Path.GetFileName(f)));
        File.WriteAllText(Path.Combine(dir, "nodes.yaml"),
            nodes.Replace("      source: \"RULE-G0\"", "      event: \"text\"\n      source: \"RULE-G0\"", StringComparison.Ordinal));
        Assert.Throws<DataLoadException>(() => ModLoader.Load(dir));
        Assert.DoesNotContain(typeof(NodeTable).GetProperties(), p => p.Name.Contains("Event", StringComparison.Ordinal));
    }

    [Fact]
    public void ModLoader_ReservesLocaleDirectory()
    {
        Assert.Equal("locale", ModLoader.LocaleDirectory);
        string dir = CopyCore(withLocale: false);
        File.WriteAllText(Path.Combine(dir, "locale"), "not a directory");
        Assert.Throws<DataLoadException>(() => ModLoader.Load(dir));
    }

    [Fact]
    public void LocaleSubdirectory_IsNotReadByTheModLoader_EvenWithYamlInside()
    {
        string dir = CopyCore(withLocale: false);
        Directory.CreateDirectory(Path.Combine(dir, "locale", "en"));
        File.WriteAllText(Path.Combine(dir, "locale", "en", "unlisted.yaml"), "this: is: not: yaml: that: loads");
        Assert.Equal(ModLoader.Load(TestKit.CoreDir).DataHash, ModLoader.Load(dir).DataHash);
    }
}
