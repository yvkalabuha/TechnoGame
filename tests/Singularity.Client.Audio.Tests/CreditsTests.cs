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
        var listed = Load().SelectMany(e => e.Files).ToHashSet(StringComparer.Ordinal);   // a mixed sound sits under several entries
        string audio = Path.Combine(Client(), "assets", "audio");
        var onDisk = Directory.GetFiles(audio, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".import"))
            .Select(f => "assets/audio/" + Path.GetRelativePath(audio, f).Replace('\\', '/'))
            .ToList();
        Assert.NotEmpty(onDisk);
        foreach (var f in onDisk) Assert.True(listed.Contains(f), "no credits entry: " + f);
        foreach (var f in listed) Assert.True(File.Exists(Path.Combine(Client(), f.Replace('/', Path.DirectorySeparatorChar))), "listed but missing: " + f);
    }

    // Every source of each event file (EVENT_SOUNDS_MAP §5 / LICENSES history): a file made from several sources must list them all, so a CC BY credit cannot be lost.
    static readonly (string File, string[] Sources)[] Sources =
    {
        ("signal_attention", new[] { "sound_bell_ding" }),
        ("signal_critical", new[] { "sound_bell_ding", "sound_relay_switch" }),
        ("signal_clear", new[] { "sound_bell_ding" }),
        ("tempo", new[] { "sound_electrical_switch_02" }), ("pause", new[] { "sound_electrical_switch_02" }), ("unpause", new[] { "sound_electrical_switch_02" }),
        ("war_declared", new[] { "sound_kill_switch", "sound_morse_static" }),
        ("order_done", new[] { "sound_triumph_matura_300" }), ("order_ok", new[] { "sound_traditional_stamp" }),
        ("order_reject", new[] { "sound_relay_switch" }), ("queue_tick", new[] { "sound_paper_shuffle_stack" }),
        ("signal_alert", new[] { "sound_stromberg_carlson_1543" }),
        ("node_discovered", new[] { "sound_typewriter_bell_2" }), ("breakthrough", new[] { "sound_typewriter_bell_2" }),
        ("line_commissioned", new[] { "sound_factory_whistle" }),
        ("launch_space", new[] { "sound_factory_whistle", "sound_relay_switch" }),
        ("launch_nuclear", new[] { "sound_kill_switch", "sound_transformer_hum" }),
        ("ui_tick", new[] { "sound_shuffling_papers" }),
    };

    [Fact]
    public void Each_event_file_lists_exactly_all_its_sources()
    {
        var all = Load();
        string audio = Path.Combine(Client(), "assets", "audio", "ui");
        var files = Directory.GetFiles(audio, "*.ogg").Select(f => Path.GetFileNameWithoutExtension(f)!).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(Sources.Select(x => x.File).OrderBy(x => x, StringComparer.Ordinal), files);
        foreach (var (file, sources) in Sources)
        {
            var have = all.Where(e => e.Files.Contains($"assets/audio/ui/{file}.ogg")).Select(e => e.Id).OrderBy(x => x, StringComparer.Ordinal);
            Assert.Equal(sources.OrderBy(x => x, StringComparer.Ordinal), have);
        }
    }

    static string[] DocLines(string name) => File.ReadAllLines(Path.Combine(Root(), "docs", "audio", name));

    [Fact]
    public void Authors_and_licences_match_the_research_records()
    {
        var all = Load();
        // EVENT_SOUNDS_MAP §5: «- Title — author — url ...», under a "CC BY" or "CC0" heading line.
        var lines = DocLines("EVENT_SOUNDS_MAP.md");
        int from = Array.FindIndex(lines, l => l.StartsWith("## 5.")), to = Array.FindIndex(lines, l => l.StartsWith("## 6."));
        Assert.True(from >= 0 && to > from);
        string lic = "";
        int seen = 0;
        var row = new System.Text.RegularExpressions.Regex("^- .+? — (.+?) — (https?://\\S+)");
        foreach (var l in lines[from..to])
        {
            if (l.StartsWith("CC BY")) lic = "CC BY 4.0"; else if (l.StartsWith("CC0")) lic = "CC0 1.0";
            var m = row.Match(l);
            if (!m.Success) continue;
            seen++;
            var e = all.SingleOrDefault(x => x.Url == m.Groups[2].Value);
            Assert.True(e != null, "not in credits.json: " + m.Groups[2].Value);
            Assert.Equal(m.Groups[1].Value, e!.Author);
            Assert.Equal(lic, e.Licence);
        }
        Assert.Equal(13, seen);
        // MUSIC_SELECTION: «| 01 | MacLeod | Impact Prelude | url |»; credits.json keeps the full author string.
        int music = 0;
        foreach (var l in DocLines("MUSIC_SELECTION.md"))
        {
            var c = l.Split('|').Select(x => x.Trim()).ToArray();
            if (c.Length < 6 || !System.Text.RegularExpressions.Regex.IsMatch(c[1], "^\\d\\d$")) continue;
            music++;
            var e = all.SingleOrDefault(x => x.Url == c[4]);
            Assert.True(e != null, "not in credits.json: " + c[4]);
            Assert.Equal(c[3], e!.Title);
            Assert.StartsWith(c[2] == "MacLeod" ? "Kevin MacLeod" : "Scott Buckley", e.Author);
            Assert.Equal("CC BY 4.0", e.Licence);
        }
        Assert.Equal(16, music);
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
                foreach (var lang in new[] { "en", "ru" })   // explicit keys: ChangesIn falls back to English
                {
                    Assert.True(e.Changes.TryGetValue(lang, out var ch), e.Id + " lacks changes." + lang);
                    Assert.False(string.IsNullOrWhiteSpace(ch), e.Id + " empty changes." + lang);
                }
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

    const string Ok = """{"id":"x","kind":"KIND","title":"t","author":"AUTHOR","licence":"CC0 1.0","licenceUrl":"u","url":"http://x","changes":{},"files":[]}""";
    static string Doc(string kind = "music", string author = "a", string extra = "") =>
        "{\"entries\":[" + Ok.Replace("KIND", kind).Replace("AUTHOR", author).Replace("\"files\":[]", "\"files\":[]" + extra) + "]}";

    [Fact]
    public void Parser_is_strict()
    {
        Assert.Single(CreditsData.Parse(Doc()));
        Assert.Throws<FormatException>(() => CreditsData.Parse(Doc(kind: "video")));
        Assert.Throws<FormatException>(() => CreditsData.Parse(Doc(author: "")));
        Assert.Throws<FormatException>(() => CreditsData.Parse(Doc().Replace("\"author\":\"a\",", "")));
        Assert.Throws<FormatException>(() => CreditsData.Parse(Doc(extra: ",\"licenсe\":\"x\"")));
    }

    [Fact]
    public void Credits_text_colours_pass_4_5_to_1_on_the_worst_paper_texture()
    {
        // The screen uses only ink and ink-2 (never the lighter ink-3); the sheet texture darkens the paper to 0.94 at worst (A3).
        var sheet = ColorMath.FromHex(Palette.C.Sheet, 0.94);
        Assert.True(ColorMath.Contrast(ColorMath.FromHex(Palette.Ink2), sheet) >= 4.5);
        Assert.True(ColorMath.Contrast(ColorMath.FromHex(Palette.Ink), sheet) >= 7);
    }
}
