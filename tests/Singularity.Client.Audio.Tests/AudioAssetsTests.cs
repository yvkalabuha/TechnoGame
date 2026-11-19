using Singularity.Client.Spike;

namespace Singularity.Client.Audio.Tests;

/// <summary>The event .ogg files match the catalogue: every cue has its file, 48 kHz mono, the declared playback length.</summary>
public class AudioAssetsTests
{
    static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Singularity.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Singularity.sln not found");
    }

    static string Assets() => Path.Combine(Root(), "src", "Singularity.Client.Spike", "assets", "audio");

    /// <summary>Mono Ogg Vorbis: rate and channels from the identification header, length from the last page's granule position.</summary>
    static (int rate, int channels, double seconds) ReadOgg(string path)
    {
        var b = File.ReadAllBytes(path);
        Assert.Equal("OggS", System.Text.Encoding.ASCII.GetString(b, 0, 4));
        Assert.Equal("vorbis", System.Text.Encoding.ASCII.GetString(b, 29, 6));
        int ch = b[39], rate = BitConverter.ToInt32(b, 40);
        int last = b.AsSpan().LastIndexOf("OggS"u8);
        Assert.Equal(0, b[last + 4]);                        // stream structure version
        Assert.True((b[last + 5] & 4) != 0, "last page must carry the end-of-stream flag");
        long granule = BitConverter.ToInt64(b, last + 6);
        return (rate, ch, granule / (double)rate);
    }

    public static IEnumerable<object[]> Files() =>
        AudioCatalog.All.Select(c => new object[] { c.Id, c.File });

    [Fact]
    public void Ui_folder_holds_exactly_the_files_the_catalogue_references_and_none_is_a_stub()
    {
        string dir = Path.Combine(Assets(), "ui");
        var onDisk = Directory.GetFiles(dir).Where(f => !f.EndsWith(".import")).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var wanted = AudioCatalog.All.Select(c => c.File + ".ogg").Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(wanted, onDisk);
        foreach (var f in onDisk) Assert.True(new FileInfo(Path.Combine(dir, f!)).Length > 2048, f);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Ogg_matches_the_catalogue(string cue, string file)
    {
        var (rate, ch, seconds) = ReadOgg(Path.Combine(Assets(), "ui", file + ".ogg"));
        Assert.Equal(48000, rate); Assert.Equal(1, ch);
        var c = AudioCatalog.Get(cue);
        // Seconds is the playback length: the file plus the extra clicks of a repeated cue.
        Assert.InRange(seconds + (c.Repeats - 1) * Cue.RepeatGap, c.Seconds - 0.02, c.Seconds + 0.02);
    }

    [Fact]
    public void Music_and_ambient_files_exist()
    {
        foreach (var f in new[] { "music/irradiated_skies.ogg", "music/led_twilight.ogg", "ambient/pale_window.ogg" })
            Assert.True(new FileInfo(Path.Combine(Assets(), f)).Length > 100_000, f);
    }
}
