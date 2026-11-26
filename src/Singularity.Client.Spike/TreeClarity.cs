#nullable enable
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Singularity.Client.Spike;

/// <summary>Visual state of a node on the planisphere (TASK-0136 spec 2.1). The planisphere and the legend draw from the same table, so they cannot drift apart.</summary>
public enum MarkState { Unknown, Frontier, Known, Mastered, Selected }

/// <summary>How a node mark is stroked: line width in du, dashed outline, filled body, light round backing under it.</summary>
public readonly record struct NodeMark(float StrokeDu, bool Dashed, bool Filled, bool Backing);

/// <summary>Godot-free rules of the knowledge-tree clarity pass (TASK-0136): node mark grammar, what the effects block shows, menu reasons, words banned from player text.</summary>
public static class TreeClarity
{
    /// <summary>Side of the bounding square of every node mark, in du (A2: a meaningful mark is at least 16 du).</summary>
    public const float MarkDu = 16;
    /// <summary>Diameter of the light backing of a frontier node.</summary>
    public const float BackingDu = 22;
    /// <summary>Diameter of the double circle of the selected node.</summary>
    public const float SelectedDu = 24;

    /// <summary>The five rows of the legend, in order; the planisphere resolves a node with <see cref="StateOf"/>.</summary>
    public static readonly MarkState[] LegendStates = { MarkState.Unknown, MarkState.Frontier, MarkState.Known, MarkState.Mastered, MarkState.Selected };

    public static NodeMark Mark(MarkState s) => s switch
    {
        MarkState.Unknown => new(1f, false, false, false),
        MarkState.Frontier => new(1.5f, true, false, true),
        MarkState.Known => new(2f, false, false, false),
        MarkState.Mastered => new(2f, false, true, false),
        _ => new(2f, false, false, false),   // Selected: the ring around a known-state mark
    };

    /// <summary>Locale key of the legend row of a state.</summary>
    public static string LegendKey(MarkState s) => "ui.tree.state." + (int)s;

    public static MarkState StateOf(bool known, bool frontier, bool mastered) => known ? (mastered ? MarkState.Mastered : MarkState.Known) : frontier ? MarkState.Frontier : MarkState.Unknown;

    /// <summary>Outline of the root of the "why not": a square with the top-right corner cut off (the shape channel of A5), centred on 0,0, side <paramref name="side"/>.</summary>
    public static (float X, float Y)[] RootFrame(float side)
    {
        float h = side / 2, c = side * 0.3f;
        return new[] { (-h, -h), (h - c, -h), (h, -h + c), (h, h), (-h, h) };
    }

    /// <summary>The requirement groups of a node that are not met yet: a group is met when any of its members is known. Read from the tree data, nothing inferred.</summary>
    public static List<int[]> UnmetGroups(IReadOnlyList<int[]> groups, System.Func<int, bool> known)
    {
        var res = new List<int[]>();
        foreach (var g in groups)
        {
            bool met = false;
            foreach (int m in g) if (known(m)) { met = true; break; }
            if (!met) res.Add(g);
        }
        return res;
    }

    public enum ParamLine { Yield, ProductGeneration, KnowledgeStep, Capability }

    /// <summary>What the "what it raises" block shows for a node. Only lines the core has data for; the gain per parameter is not shown until the core provides it.</summary>
    public static ParamLine[] ParamLines(bool hasProcess, bool isProduct, bool isProcessNode)
    {
        var first = hasProcess ? ParamLine.Yield : isProduct ? ParamLine.ProductGeneration : ParamLine.KnowledgeStep;
        return isProcessNode ? new[] { first, ParamLine.Capability } : new[] { first };
    }

    public enum MenuItem { Continue, NewGame, Load, Save, Settings, Credits, ToMain, Quit }

    /// <summary>Locale key of the reason a menu item is unavailable, or null when it works. The client has no save files and no load path yet, so Load is always unavailable.</summary>
    public static string? MenuWhy(MenuItem item, bool inGame) => item switch
    {
        MenuItem.Continue => inGame ? null : "ui.menu.continue.why",
        MenuItem.Load => "ui.menu.load.why",
        MenuItem.Save => inGame ? "ui.menu.save.why.notready" : "ui.menu.save.why.nogame",
        _ => null,
    };

    /// <summary>The tree inspector reports only the command it owns (the mastery focus); a budget or design command of another screen is not news here (it read as "resources allocated" under the focus button).</summary>
    public static bool ShowsCommand(string labelKey) => labelKey is "cmd.focus" or "cmd.sci";

    /// <summary>Developer vocabulary that must not appear in the text the player reads (C6).</summary>
    public static readonly Regex Forbidden = new(@"\bAPI\b|NodeEffects|\bP10|\bтик|макет|\bядр|симуляци|\btick\b|\bmock\b|spike|ADR-", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
}
