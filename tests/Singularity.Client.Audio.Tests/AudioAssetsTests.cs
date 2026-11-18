using Singularity.Client.Spike;

namespace Singularity.Client.Audio.Tests;

/// <summary>The generated WAV files match the catalogue: every cue has its files, 48 kHz 16-bit mono, the declared length, peak at most -3 dBFS.</summary>
public class AudioAssetsTests
{
    static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Singularity.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Singularity.sln not found");
    }

    static string Assets() => Path.Combine(Root(), "src", "Singularity.Client.Spike", "assets", "audio");

    static (int rate, int bits, int channels, short[] data) ReadWav(string path)
    {
        var b = File.ReadAllBytes(path);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(b, 0, 4));
        int pos = 12, rate = 0, bits = 0, ch = 0;
        while (pos + 8 <= b.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
            int size = BitConverter.ToInt32(b, pos + 4);
            if (id == "fmt ") { ch = BitConverter.ToInt16(b, pos + 10); rate = BitConverter.ToInt32(b, pos + 12); bits = BitConverter.ToInt16(b, pos + 22); }
            if (id == "data")
            {
                var d = new short[size / 2];
                Buffer.BlockCopy(b, pos + 8, d, 0, size / 2 * 2);
                return (rate, bits, ch, d);
            }
            pos += 8 + size + (size & 1);
        }
        throw new InvalidDataException(path);
    }

    public static IEnumerable<object[]> Files() =>
        AudioCatalog.All.SelectMany(c => Enumerable.Range(0, c.Variants).Select(v => new object[] { c.Id, c.File(v) }));

    [Theory]
    [MemberData(nameof(Files))]
    public void Wav_matches_the_catalogue(string cue, string file)
    {
        var (rate, bits, ch, data) = ReadWav(Path.Combine(Assets(), "ui", file + ".wav"));
        Assert.Equal(48000, rate); Assert.Equal(16, bits); Assert.Equal(1, ch);
        double seconds = data.Length / 48000.0;
        Assert.InRange(seconds, AudioCatalog.Get(cue).Seconds - 0.03, AudioCatalog.Get(cue).Seconds + 0.03);
        double peak = data.Max(x => Math.Abs((double)x)) / 32767.0;
        Assert.True(20 * Math.Log10(peak) <= -2.9, $"{file} peak {20 * Math.Log10(peak):F1} dBFS");
        Assert.True(peak > 0.01);
    }

    [Fact]
    public void Music_and_ambient_files_exist()
    {
        foreach (var f in new[] { "music/irradiated_skies.ogg", "music/led_twilight.ogg", "ambient/pale_window.ogg" })
            Assert.True(new FileInfo(Path.Combine(Assets(), f)).Length > 100_000, f);
    }

    [Fact]
    public void Every_audio_file_has_a_licence_entry()
    {
        string lic = File.ReadAllText(Path.Combine(Root(), "src", "Singularity.Client.Spike", "LICENSES.md"));
        foreach (var f in new[] { "irradiated_skies.ogg", "led_twilight.ogg", "pale_window.ogg", "assets/audio/ui/" })
            Assert.Contains(f, lic);
    }
}
