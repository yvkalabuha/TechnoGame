using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Singularity.MapBuilder;

/// <summary>
/// Downloads of the terrain rasters (TASK-0168, #536) into the cache, outside git: GMTED2010 and GLWD v2 files, and the overview
/// level of every ESA WorldCover tile (read by HTTP range, so the 117 GB of the 10 m product are never transferred).
/// Downloads are resumable and retried; a finished file is never fetched again.
/// </summary>
internal static class TerrainSources
{
    public static string Dir(string cache, params string[] sub) => Path.Combine(new[] { cache, "terrain" }.Concat(sub).ToArray());

    public static string FilePath(string cache, SourceEntry e, RasterFile f) => Dir(cache, e.Id, f.Name);

    private static string Url(SourceEntry e, RasterFile f) =>
        e.Url.Replace("{name}", f.Name).Replace("{dir}", f.Name.Length > 7 ? f.Name[6] + f.Name[3..6] : "");

    public static string AggregateSha(IEnumerable<(string Name, string Sha)> files) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Concat(files.OrderBy(f => f.Name, StringComparer.Ordinal).Select(f => $"{f.Name} {f.Sha}\n"))))).ToLowerInvariant();

    public static string FileSha(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
    }

    public static HttpClient NewClient()
    {
        var http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("singularity-map-builder/1 (offline data build)");
        return http;
    }

    /// <summary>Resumable download. Returns false on HTTP 404.</summary>
    public static async Task<bool> DownloadAsync(HttpClient http, string url, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) return true;
        var part = path + ".part";
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                long have = File.Exists(part) ? new FileInfo(part).Length : 0;
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (have > 0) req.Headers.Range = new RangeHeaderValue(have, null);
                using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                if (resp.StatusCode == HttpStatusCode.NotFound) return false;
                if (resp.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable) { File.Move(part, path); return true; }
                resp.EnsureSuccessStatusCode();
                bool append = resp.StatusCode == HttpStatusCode.PartialContent;
                await using (var fs = new FileStream(part, append ? FileMode.Append : FileMode.Create, FileAccess.Write))
                {
                    await using var body = await resp.Content.ReadAsStreamAsync(cts.Token);
                    await body.CopyToAsync(fs, cts.Token);
                }
                File.Move(part, path);
                return true;
            }
            catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException && attempt < 8)
            {
                await Task.Delay(TimeSpan.FromSeconds(2 * (attempt + 1)));
            }
        }
    }

    private static async Task<byte[]> RangeAsync(HttpClient http, string url, long off, int count)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Range = new RangeHeaderValue(off, off + count - 1);
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                using var resp = await http.SendAsync(req, cts.Token);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsByteArrayAsync(cts.Token);
            }
            catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException)
            {
                if (attempt >= 8) throw;
                await Task.Delay(TimeSpan.FromSeconds(2 * (attempt + 1)));
            }
        }
    }

    /// <summary>fetch --terrain: downloads everything the terrain sources need. With <paramref name="pin"/> prints the hashes instead of checking them.</summary>
    public static async Task FetchAsync(SourceLock lk, string cache, bool pin)
    {
        using var http = NewClient();
        foreach (var e in lk.Sources.Where(Sources.IsTerrain))
        {
            if (e.Kind == "raster")
            {
                var sem = new SemaphoreSlim(4);
                var missing = new List<string>();
                await Task.WhenAll(e.Files.Select(async f =>
                {
                    await sem.WaitAsync();
                    try
                    {
                        if (!await DownloadAsync(http, Url(e, f), FilePath(cache, e, f))) lock (missing) missing.Add(f.Name);
                    }
                    finally { sem.Release(); }
                }));
                foreach (var m in missing.Order(StringComparer.Ordinal)) Console.WriteLine($"{e.Id}: {m}: HTTP 404 (not on the server: remove it from the lock)");
                var shas = e.Files.Where(f => !missing.Contains(f.Name)).Select(f => (f.Name, Sha: FileSha(FilePath(cache, e, f)))).ToList();
                if (pin)
                {
                    foreach (var (n, s) in shas.OrderBy(x => x.Name, StringComparer.Ordinal)) Console.WriteLine($"{e.Id}\t{n}\t{s}");
                    Console.WriteLine($"{e.Id}\t*\t{AggregateSha(shas)}");
                }
                else Verify(e, shas);
            }
            else await FetchCogAsync(http, lk, e, cache, pin);
        }
    }

    public static void Verify(SourceEntry e, List<(string Name, string Sha)> actual)
    {
        foreach (var f in e.Files)
        {
            var a = actual.FirstOrDefault(x => x.Name == f.Name);
            if (a.Name == null) throw new InvalidDataException($"{e.Id}: {f.Name} is not cached; run 'fetch --terrain'");
            if (a.Sha != f.Sha256) throw new InvalidDataException($"SHA-256 mismatch for {e.Id}/{f.Name}: lock {f.Sha256}, got {a.Sha}");
        }
        if (AggregateSha(actual) != e.Sha256) throw new InvalidDataException($"SHA-256 mismatch for {e.Id}: the set of files differs from the lock");
    }

    public static List<string> Tiles(SourceLock lk, string cache, string gridId)
    {
        var ge = lk.Sources.First(s => s.Id == gridId);
        var path = FilePath(cache, ge, ge.Files[0]);
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        return doc.RootElement.GetProperty("features").EnumerateArray().Select(f => f.GetProperty("properties").GetProperty("ll_tile").GetString()!)
            .OrderBy(t => t, StringComparer.Ordinal).ToList();
    }

    private static async Task FetchCogAsync(HttpClient http, SourceLock lk, SourceEntry e, string cache, bool pin)
    {
        var dir = Dir(cache, e.Id);
        Directory.CreateDirectory(dir);
        var tiles = Tiles(lk, cache, e.Grid!);
        var sem = new SemaphoreSlim(8);
        int done = 0;
        await Task.WhenAll(tiles.Select(async t =>
        {
            await sem.WaitAsync();
            try
            {
                var path = CogGrid.TilePath(dir, t, e.Level);
                if (!File.Exists(path)) await ExtractTileAsync(http, e.Url.Replace("{name}", t), path, e.Level);
                if (Interlocked.Increment(ref done) % 200 == 0) Console.WriteLine($"{e.Id}: {done}/{tiles.Count} tiles");
            }
            finally { sem.Release(); }
        }));
        var agg = CogAggregate(dir, tiles, e.Level);
        if (pin) Console.WriteLine($"{e.Id}\t*\t{agg}");
        else if (agg != e.Sha256) throw new InvalidDataException($"SHA-256 mismatch for {e.Id}: lock {e.Sha256}, got {agg}");
    }

    /// <summary>SHA-256 over the decoded overview bytes of every tile (the pin of a cog-overview source).</summary>
    public static string CogAggregate(string dir, List<string> tiles, int level) => AggregateSha(CogShas(dir, tiles, level));

    /// <summary>SHA-256 of the decoded bytes of every pinned tile; CogGrid checks the bytes it reads against these.</summary>
    public static List<(string Name, string Sha)> CogShas(string dir, List<string> tiles, int level)
    {
        var shas = new List<(string Name, string Sha)>();
        var raw = new byte[level * level];
        foreach (var t in tiles)
        {
            var path = CogGrid.TilePath(dir, t, level);
            if (!File.Exists(path)) throw new InvalidDataException($"{path} is not cached; run 'fetch --terrain'");
            using var z = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
            z.ReadExactly(raw);
            shas.Add((t, Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant()));
        }
        return shas;
    }

    /// <summary>Reads the overview IFD of one WorldCover COG by range requests and stores its pixels as a gzip of raw bytes.</summary>
    private static async Task ExtractTileAsync(HttpClient http, string url, string path, int level)
    {
        var head = await RangeAsync(http, url, 0, 65536);
        byte[] Read(long off, int n)
        {
            if (off + n <= head.Length) return head.AsSpan((int)off, n).ToArray();
            return RangeAsync(http, url, off, n).GetAwaiter().GetResult();
        }
        var bytes = await Task.Run(() =>
        {
            TiffImage? img = null;
            for (int i = 0; i < 12 && img == null; i++)
            {
                var c = TiffImage.Open(Read, i);
                if (c.Width == level) img = c;
            }
            if (img == null) throw new InvalidDataException($"{url}: no overview of width {level}");
            var res = new byte[level * level];
            for (int b = 0; b < img.Offsets.Length; b++)
            {
                var blk = img.ReadBlock(b);
                int bx = b % img.BlocksAcross * img.BlockW, by = b / img.BlocksAcross * img.BlockH;
                int rows = blk.Length / img.BlockW;
                for (int r = 0; r < rows && by + r < level; r++)
                    for (int x = 0; x < img.BlockW && bx + x < level; x++) res[(by + r) * level + bx + x] = (byte)blk[r * img.BlockW + x];
            }
            return res;
        });
        var tmp = path + ".part";
        await using (var fs = File.Create(tmp))
        await using (var z = new GZipStream(fs, CompressionLevel.Optimal)) await z.WriteAsync(bytes);
        File.Move(tmp, path, true);
    }

    /// <summary>GLWD v2: extracts the named members of the zip into the cache and returns their paths.</summary>
    public static string[] ExtractZipMembers(string cache, SourceEntry e, string[] members)
    {
        var zipPath = FilePath(cache, e, e.Files[0]);
        var outDir = Dir(cache, e.Id, "extract");
        Directory.CreateDirectory(outDir);
        using var zip = ZipFile.OpenRead(zipPath);
        var res = new List<string>();
        foreach (var m in members)
        {
            var entry = zip.Entries.FirstOrDefault(x => x.Name == m) ?? throw new InvalidDataException($"{e.Id}: {m} is not in the archive");
            var dest = Path.Combine(outDir, m);
            // Always extracted again from the archive whose SHA-256 was just checked: a stale file of the same length is never trusted.
            var tmp = dest + ".part";
            entry.ExtractToFile(tmp, true);
            File.Move(tmp, dest, true);
            res.Add(dest);
        }
        return res.ToArray();
    }
}
