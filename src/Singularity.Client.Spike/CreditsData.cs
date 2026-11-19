using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Singularity.Client.Spike;

/// <summary>One line of the credits (assets/credits.json): who made an audio asset, under which licence, what was changed.</summary>
public sealed record CreditEntry(string Id, string Kind, string Title, string Author, string Licence, string LicenceUrl, string Url,
    IReadOnlyDictionary<string, string> Changes, IReadOnlyList<string> Files)
{
    public bool RequiresCredit => Licence.StartsWith("CC BY", StringComparison.Ordinal);
    public string ChangesIn(string lang) => Changes.TryGetValue(lang, out var s) ? s : Changes.TryGetValue("en", out var e) ? e : "";
}

/// <summary>Godot-free reader of the single credits list; the credits screen shows it and the tests check it against the asset folders.</summary>
public static class CreditsData
{
    public const string Music = "music", Sound = "sound";

    public static IReadOnlyList<CreditEntry> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<CreditEntry>();
        foreach (var e in doc.RootElement.GetProperty("entries").EnumerateArray())
        {
            var changes = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (e.TryGetProperty("changes", out var ch) && ch.ValueKind == JsonValueKind.Object)
                foreach (var p in ch.EnumerateObject()) changes[p.Name] = p.Value.GetString() ?? "";
            var files = e.GetProperty("files").EnumerateArray().Select(f => f.GetString() ?? "").ToList();
            list.Add(new CreditEntry(Str(e, "id"), Str(e, "kind"), Str(e, "title"), Str(e, "author"), Str(e, "licence"),
                Str(e, "licenceUrl"), Str(e, "url"), changes, files));
        }
        return list;
    }

    static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";
}
