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

    static readonly Regex Digits = new(@"\d+%?", RegexOptions.Compiled);

    /// <summary>BBCode for a prose: runs of digits (with a percent sign) get the mono face, the decimal comma or point stays in the text face so that "19,3" does not fall apart into "19 , 3".</summary>
    public static string MarkNumbers(string s) =>
        Digits.Replace(s.Replace("[", "[lb]"), m => "[code]" + m.Value + "[/code]");

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
