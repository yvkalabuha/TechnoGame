using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Singularity.MapBuilder;

internal sealed class SourceEntry
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";       // ne | ohm | raster | cog-overview (the last two only for 'terrain', TASK-0168)
    public string Url { get; set; } = "";
    public string? Query { get; set; }           // ohm: pinned Overpass query (with [date:...])
    public string Sha256 { get; set; } = "";
    public string License { get; set; } = "";
    public string Retrieved { get; set; } = "";
    public string? Note { get; set; }
    public List<RasterFile> Files { get; set; } = new();   // raster: pinned files; the url holds {name} and {dir}
    public string? Grid { get; set; }                       // cog-overview: id of the tile grid source
    public int Level { get; set; }                          // cog-overview: width in pixels of the overview used for each 3x3 degree tile
}

internal sealed class RasterFile
{
    public string Name { get; set; } = "";
    public string Sha256 { get; set; } = "";
}

internal sealed class SourceLock
{
    public List<SourceEntry> Sources { get; set; } = new();
}

/// <summary>sources.lock.yaml, the download cache and SHA-256 verification (ADR-0011 §2.1). Files are read only as data.</summary>
internal static class Sources
{
    public static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance).Build();

    public static SourceLock Load(string path) => Yaml.Deserialize<SourceLock>(File.ReadAllText(path));

    public static string CachePath(string cache, SourceEntry e) =>
        Path.Combine(cache, e.Id.Replace(':', '_').Replace('/', '_').Replace('@', '_') + ".json");

    public static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    public static bool IsTerrain(SourceEntry e) => e.Kind is "raster" or "cog-overview";

    /// <summary>Downloads missing entries into the cache. A hash mismatch is a loud error unless <paramref name="pin"/> is set (then the hash is printed). Terrain rasters are skipped (see TerrainSources).</summary>
    public static async Task FetchAsync(SourceLock lk, string cache, bool pin)
    {
        Directory.CreateDirectory(cache);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("singularity-map-builder/1 (offline data build)");
        foreach (var e in lk.Sources.Where(e => !IsTerrain(e)))
        {
            var path = CachePath(cache, e);
            byte[] data;
            if (File.Exists(path)) data = File.ReadAllBytes(path);
            else
            {
                data = e.Kind == "ne" ? await http.GetByteArrayAsync(e.Url) : await FetchOhmAsync(http, e);
                File.WriteAllBytes(path, data);
            }
            var sha = Sha(data);
            if (pin) Console.WriteLine($"{e.Id}: {sha}");
            else if (sha != e.Sha256) throw new InvalidDataException($"SHA-256 mismatch for {e.Id}: lock {e.Sha256}, got {sha}");
        }
    }

    /// <summary>OHM snapshot = raw text of the Overpass "elements" array. The pinned [date:] clause makes the query reproducible.</summary>
    private static async Task<byte[]> FetchOhmAsync(HttpClient http, SourceEntry e)
    {
        using var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("data", e.Query!) });
        using var resp = await http.PostAsync(e.Url, content);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsByteArrayAsync());
        return Encoding.UTF8.GetBytes(doc.RootElement.GetProperty("elements").GetRawText());
    }

    /// <summary>Reads a verified cache file; fails if the hash differs from the lock.</summary>
    public static byte[] ReadVerified(SourceLock lk, string cache, string id)
    {
        var e = lk.Sources.FirstOrDefault(s => s.Id == id) ?? throw new InvalidDataException($"source {id} is not in the lock");
        var path = CachePath(cache, e);
        if (!File.Exists(path)) throw new FileNotFoundException($"{id} is not cached; run 'fetch'", path);
        var data = File.ReadAllBytes(path);
        var sha = Sha(data);
        if (sha != e.Sha256) throw new InvalidDataException($"SHA-256 mismatch for {id}: lock {e.Sha256}, got {sha}");
        return data;
    }
}
