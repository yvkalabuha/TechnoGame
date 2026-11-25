using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Singularity.Client.Spike;

/// <summary>Godot-free text and routing helpers of the lever windows (TASK-0156 review round 1): the live window and the tests call the same functions.</summary>
public static class LeverText
{
    /// <summary>Joins the non-empty parts with " · ": an empty part never leaves a double or a trailing separator.</summary>
    public static string Join(params string[] parts)
    {
        var sb = new StringBuilder();
        foreach (var p in parts)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            if (sb.Length > 0) sb.Append(" · ");
            sb.Append(p);
        }
        return sb.ToString();
    }

    // whole numbers only: a decimal like 1,3 stays in the proportional face, where a narrow "1" does not leave a hole before the comma
    static readonly Regex Digits = new(@"(?<![\d.,])(?>\d+)%?(?![.,]\d)", RegexOptions.Compiled);

    /// <summary>BBCode for a prose: whole numbers (with a percent sign) get the mono face; a decimal number such as "19,3" stays in the text face.</summary>
    public static string MarkNumbers(string s) =>
        Digits.Replace(s.Replace("[", "[lb]"), m => "[code]" + m.Value + "[/code]");

    /// <summary>Whole million with the thousands grouped by <paramref name="sep"/>: 19253 → "19 253" (no-break space in Russian).</summary>
    public static string GroupThousands(long n, string sep)
    {
        string s = System.Math.Abs(n).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++) { if (i > 0 && (s.Length - i) % 3 == 0) sb.Append(sep); sb.Append(s[i]); }
        return (n < 0 ? "-" : "") + sb;
    }

    /// <summary>The result line of the last order is shown only for an order given while this window was open (a leftover «принято» from an earlier session of play is not news).</summary>
    public static bool ResultIsFresh(long cmdSeq, long seqWhenOpened) => cmdSeq > seqWhenOpened;

    // Tabs of the window: 0 budget, 1 institutes, 2 lines, 3 market.
    static readonly string[][] TabCommands =
    {
        new[] { "cmd.budget" },
        new[] { "cmd.priority", "cmd.build", "cmd.adviser" },
        new[] { "cmd.lineopen", "cmd.lineclose", "cmd.refuse", "cmd.close", "cmd.cancel" },
        new[] { "cmd.buy", "cmd.offer" },
    };

    /// <summary>The last order is told only on the tab it belongs to (a "shares: accepted" never stands under the licence market).</summary>
    public static bool ResultBelongsToTab(string labelKey, int tab) =>
        tab >= 0 && tab < TabCommands.Length && System.Array.IndexOf(TabCommands[tab], labelKey) >= 0;

    /// <summary>True when the window must hide the map legend (it would stick out from under the sheet).</summary>
    public static bool LegendHidden(bool treeVisible, bool titleHidden, bool leverWindowVisible) => treeVisible || titleHidden || leverWindowVisible;
}
