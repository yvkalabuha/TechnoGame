using Singularity.Client.Spike;

namespace Singularity.Client.Audio.Tests;

/// <summary>assets/credits.json is the single attribution list (ADR-0009, DEBT-0050): complete against the audio folders, no CC BY entry without author and URL.</summary>
public class CreditsTests
{
    static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Singularity.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Singularity.sln not found");
    }

    static string Client() => Path.Combine(Root(), "src", "Singularity.Client.Spike");
    static IReadOnlyList<CreditEntry> Load() => CreditsData.Parse(File.ReadAllText(Path.Combine(Client(), "assets", "credits.json")));

    [Fact]
    public void Every_audio_file_has_a_credits_entry_and_every_listed_file_exists()
    {
        var listed = Load().SelectMany(e => e.Files).ToHashSet(StringComparer.Ordinal);   // a mixed sound may sit under several entries
        string audio = Path.Combine(Client(), "assets", "audio");
        var onDisk = new[] { "music", "ui", "ambient" }
            .SelectMany(d => Directory.GetFiles(Path.Combine(audio, d)))
            .Where(f => !f.EndsWith(".import"))
            .Select(f => "assets/audio/" + Path.GetRelativePath(audio, f).Replace('\\', '/'))
            .ToList();
        Assert.NotEmpty(onDisk);
        foreach (var f in onDisk) Assert.True(listed.Contains(f), "no credits entry: " + f);
        foreach (var f in listed) Assert.True(File.Exists(Path.Combine(Client(), f.Replace('/', Path.DirectorySeparatorChar))), "listed but missing: " + f);
    }

    [Fact]
    public void Cc_by_entries_have_author_url_licence_link_and_changes_and_cc0_is_named()
    {
        var all = Load();
        Assert.Equal(16, all.Count(e => e.Kind == CreditsData.Music && e.RequiresCredit));   // the soundtrack of #392
        Assert.Equal(4, all.Count(e => e.Kind == CreditsData.Sound && e.RequiresCredit));    // CC BY event sounds (TASK-0104)
        foreach (var e in all)
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Title), e.Id);
            Assert.False(string.IsNullOrWhiteSpace(e.Author), e.Id);
            Assert.StartsWith("http", e.Url);
            if (e.RequiresCredit)
            {
                Assert.Equal("CC BY 4.0", e.Licence);
                Assert.StartsWith("http", e.LicenceUrl);
                Assert.False(string.IsNullOrWhiteSpace(e.ChangesIn("en")) || string.IsNullOrWhiteSpace(e.ChangesIn("ru")), e.Id);
            }
            else Assert.Equal("CC0 1.0", e.Licence);
        }
        Assert.Equal(all.Count, all.Select(e => e.Id).Distinct().Count());
    }

    [Fact]
    public void Licences_md_points_to_the_list_and_does_not_copy_audio_credits()
    {
        var text = File.ReadAllText(Path.Combine(Client(), "LICENSES.md"));
        Assert.Contains("assets/credits.json", text);
        foreach (var e in Load()) Assert.DoesNotContain(e.Url, text);
    }
}
