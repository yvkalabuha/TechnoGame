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

    static readonly string[] Required = { "id", "kind", "title", "author", "licence", "licenceUrl", "url", "changes", "files" };

    /// <summary>Strict: a missing or empty required field, an unknown kind or an unknown key throws FormatException.</summary>
    public static IReadOnlyList<CreditEntry> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<CreditEntry>();
        foreach (var e in doc.RootElement.GetProperty("entries").EnumerateArray())
        {
            string id = e.TryGetProperty("id", out var idp) ? idp.GetString() ?? "?" : "?";
            foreach (var p in e.EnumerateObject())
                if (!Required.Contains(p.Name)) throw new FormatException($"credits entry {id}: unknown key '{p.Name}'");
            foreach (var k in Required)
                if (!e.TryGetProperty(k, out var v) || (v.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(v.GetString())))
                    throw new FormatException($"credits entry {id}: missing or empty '{k}'");
            string kind = Str(e, "kind");
            if (kind != Music && kind != Sound) throw new FormatException($"credits entry {id}: bad kind '{kind}'");
            var changes = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in e.GetProperty("changes").EnumerateObject()) changes[p.Name] = p.Value.GetString() ?? "";
            var files = e.GetProperty("files").EnumerateArray().Select(f => f.GetString() ?? "").ToList();
            list.Add(new CreditEntry(id, kind, Str(e, "title"), Str(e, "author"), Str(e, "licence"), Str(e, "licenceUrl"), Str(e, "url"), changes, files));
        }
        return list;
    }

    static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";
}

/// <summary>Cyclic focus order over a list of controls (index 0 = the Back button, then the links in reading order).</summary>
public static class FocusRing
{
    /// <summary>Indices of the next (Tab) and previous (Shift+Tab) element, wrapping at both ends.</summary>
    public static (int Next, int Prev) Neighbours(int index, int count)
    {
        if (count <= 0 || index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
        return ((index + 1) % count, (index + count - 1) % count);
    }
}
