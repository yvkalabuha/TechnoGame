#nullable enable

namespace Singularity.Client.Spike;

public enum NodeAction { SetGoal, MasteryFocus, WhyBlocked, ShowLinks }

/// <summary>One menu row: the action, the locale key of its word, and the locale key of the reason it is unavailable (null = available).</summary>
public readonly record struct NodeMenuItem(NodeAction Action, string LabelKey, string? WhyKey)
{
    public bool Enabled => WhyKey == null;
}

/// <summary>Godot-free rules of the node menu (#473): the four items, when each is unavailable and why in words (A1, A5), and the long press that opens it.</summary>
public static class NodeMenuLogic
{
    public const float LongPressSeconds = 0.5f, LongPressMaxMoveDu = 6f;
    public const int FocusLimit = 3;

    /// <summary>The state of the node the menu was opened on. <c>CanSetGoal</c> is false while the session has no command to choose the goal from the tree.</summary>
    public readonly record struct Ctx(bool Known, bool IsProcess, bool OwnPower, bool CanSetGoal, bool InFocus, int FocusCount);

    public static NodeMenuItem[] Items(Ctx c)
    {
        string? goal = c.CanSetGoal ? null : "menu.why.goal";
        string? focus = !c.IsProcess || !c.Known ? "menu.why.focus.process"
            : !c.OwnPower ? "menu.why.focus.power"
            : !c.InFocus && c.FocusCount >= FocusLimit ? "menu.why.focus.full" : null;
        string? why = c.Known ? "menu.why.blocked.known" : null;
        return new NodeMenuItem[]
        {
            new(NodeAction.SetGoal, "menu.goal", goal),
            new(NodeAction.MasteryFocus, c.InFocus ? "menu.focus.remove" : "menu.focus", focus),
            new(NodeAction.WhyBlocked, "menu.blocked", why),
            new(NodeAction.ShowLinks, "menu.links", null),
        };
    }

    /// <summary>A press that moved farther than the long-press tolerance is a drag (it pans the map); one within it can still become the held-open menu.</summary>
    public static bool IsDrag(float movedDu) => movedDu > LongPressMaxMoveDu;

    /// <summary>A held left button opens the menu: held long enough and moved no more than a few du (a drag pans the map instead).</summary>
    public static bool IsLongPress(float heldSeconds, float movedDu) => heldSeconds >= LongPressSeconds && movedDu <= LongPressMaxMoveDu;

    /// <summary>Next enabled item index in the direction (step +1/-1), wrapping; -1 when none is enabled. A disabled row is skipped by the keyboard but still shows its reason.</summary>
    public static int NextEnabled(NodeMenuItem[] items, int cur, int step)
    {
        for (int k = 1; k <= items.Length; k++)
        {
            int i = ((cur + step * k) % items.Length + items.Length) % items.Length;
            if (items[i].Enabled) return i;
        }
        return -1;
    }
}
