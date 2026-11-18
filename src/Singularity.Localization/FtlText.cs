using System.Text;
using System.Text.RegularExpressions;

namespace Singularity.Localization;

/// <summary>Raw-text view of a .ftl file: one entry per message or term, comments removed (ADR-0018 §3.4).</summary>
static partial class FtlText
{
    [GeneratedRegex(@"^(-?[A-Za-z][A-Za-z0-9_-]*)[ ]*=")]
    private static partial Regex EntityStart();

    /// <summary>
    /// Splits a file into entities. An entity starts at a column-0 `id =` line and runs to
    /// the next entity or comment. Text is normalised: LF line ends, trailing whitespace and trailing blank lines removed.
    /// </summary>
    public static List<(string Id, string Text)> Split(string source)
    {
        var result = new List<(string, string)>();
        string[] lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var current = new List<string>();
        string? id = null;

        void Flush()
        {
            if (id is null)
                return;
            while (current.Count > 0 && current[^1].Length == 0)
                current.RemoveAt(current.Count - 1);
            result.Add((id, string.Join('\n', current)));
            id = null;
            current.Clear();
        }

        foreach (string raw in lines)
        {
            string line = raw.TrimEnd();
            // A new entity is a column-0 `id =` line; a column-0 `}` inside a select expression is a continuation.
            bool comment = raw.Length > 0 && raw[0] == '#';
            Match start = EntityStart().Match(raw);
            if (comment || start.Success)
            {
                Flush();
                if (comment)
                    continue;
                id = start.Groups[1].Value;
            }
            if (id is not null)
                current.Add(line);
        }
        Flush();
        return result;
    }

    /// <summary>First 16 hex characters of the SHA-256 of the entity text (UTF-8).</summary>
    public static string Hash(string entityText)
    {
        byte[] h = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(entityText));
        return Convert.ToHexStringLower(h)[..16];
    }
}
