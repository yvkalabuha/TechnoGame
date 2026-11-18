using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Godot;

namespace Singularity.Client.Spike;

/// <summary>
/// Localization (owner decision #83: EN + RU from the first version). CSV files `keys,en,ru` in res://locale/ become Godot
/// Translation objects, so Label/Button texts that equal a key translate themselves and follow the locale live.
/// Dynamic strings go through <see cref="T(string)"/>. ui.csv = interface; data.csv = node, country and actor names
/// (client-side stand-in until the mods-side locale format exists: the only reader of it is <see cref="Names"/>).
/// </summary>
public static class Loc
{
    public static readonly string[] Languages = { "ru", "en" };
    static readonly HashSet<string> Known = new();   // lookup only, never enumerated for output
    public static string Lang { get; private set; } = "ru";
    public static event Action Changed;

    public static void Load()
    {
        foreach (var file in new[] { "ui.csv", "data.csv" })
        {
            var rows = ParseCsv(File.ReadAllText(ProjectSettings.GlobalizePath("res://locale/" + file), Encoding.UTF8));
            var head = rows[0];
            for (int c = 1; c < head.Count; c++)
            {
                var tr = new Translation { Locale = head[c] };   // one object per file and language: no lookup of existing ones needed
                for (int r = 1; r < rows.Count; r++)
                {
                    if (rows[r].Count <= c || rows[r][0] == "") continue;
                    tr.AddMessage(rows[r][0], rows[r][c].Replace("\\n", "\n"));
                    Known.Add(rows[r][0]);
                }
                TranslationServer.AddTranslation(tr);
            }
        }
        TranslationServer.SetLocale(Lang);
    }

    public static void SetLang(string lang)
    {
        if (lang == Lang) return;
        Lang = lang;
        TranslationServer.SetLocale(lang);
        Changed?.Invoke();
    }

    /// <summary>Number for display: fixed decimals, decimal comma in Russian (locale-dependent, never the process culture).</summary>
    public static string N(double v, int decimals = 2)
    {
        string s = v.ToString("F" + decimals, CultureInfo.InvariantCulture);
        return Lang == "ru" ? s.Replace('.', ',') : s;
    }

    public static bool Has(string key) => Known.Contains(key);

    public static string T(string key) => Known.Contains(key) ? TranslationServer.Translate(key).ToString() : key;

    public static string T(string key, params object[] args) => string.Format(CultureInfo.InvariantCulture, T(key), args);

    /// <summary>Translation, or <paramref name="fallback"/> when the key is not in the tables.</summary>
    public static string TOr(string key, string fallback) => Known.Contains(key) ? T(key) : fallback;

    /// <summary>RFC 4180 subset: comma separated, double quotes, doubled quotes inside.</summary>
    static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>(); var row = new List<string>(); var sb = new StringBuilder(); bool q = false;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (q)
            {
                if (ch == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; } else q = false; }
                else sb.Append(ch);
            }
            else if (ch == '"') q = true;
            else if (ch == ',') { row.Add(sb.ToString()); sb.Clear(); }
            else if (ch == '\n' || ch == '\r')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(sb.ToString()); sb.Clear();
                if (row.Count > 1 || row[0] != "") rows.Add(row);
                row = new List<string>();
            }
            else sb.Append(ch);
        }
        if (sb.Length > 0 || row.Count > 0) { row.Add(sb.ToString()); rows.Add(row); }
        return rows;
    }
}
