using System;
using Godot;

namespace Singularity.Client.Spike;

/// <summary>Design-system tokens (UI_DESIGN_SYSTEM): paper register, square corners, ink lines, no shadows or glow. Fonts: engine default (OpenGost not bundled in the prototype).</summary>
public static class Ui
{
    public static readonly Color Paper = new("F2EFE6"), Sheet = new("FAF8F2"), Hover = new("E8E4D8"), Ink = new("1D1E20"),
        Ink2 = new("55575A"), Ink3 = new("6E6C66"), Rule = new("BCB6A8"), Normal = new("86837C"),
        Alarm = new("C42A1C"), Warn = new("E89A1A"), WarnText = new("8A5300"), Command = new("2347A8"), Seq2 = new("C2CDD3");

    public static StyleBoxFlat Box(Color bg, Color? border = null, int bw = 1, float margin = 6)
    {
        var s = new StyleBoxFlat { BgColor = bg, ContentMarginLeft = margin, ContentMarginRight = margin, ContentMarginTop = margin * 0.5f, ContentMarginBottom = margin * 0.5f };
        if (border is Color c) { s.SetBorderWidthAll(bw); s.BorderColor = c; }
        return s;
    }

    public static Label Lbl(string text, int size = 14, Color? color = null)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color ?? Ink);
        return l;
    }

    public static Label Wrap(string text, int size = 13, Color? color = null)
    {
        var l = Lbl(text, size, color);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        l.CustomMinimumSize = new Vector2(60, 0);
        return l;
    }

    /// <summary>Mock marker: every control or figure without a Session API carries it.</summary>
    public static Label Mock(string what = "макет")
    {
        var l = Lbl(what, 11, WarnText);
        l.AddThemeStyleboxOverride("normal", Box(new Color(Warn, 0.18f), WarnText, 1, 3));
        return l;
    }

    public static Button Btn(string text, Action onPress, int size = 14, bool toggled = false)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, ToggleMode = false };
        b.AddThemeFontSizeOverride("font_size", size);
        Style(b, toggled);
        b.Pressed += onPress;
        return b;
    }

    public static void Style(Button b, bool on)
    {
        var normal = Box(on ? Ink : Sheet, Ink, 1, 8);
        var hover = Box(on ? Ink : Hover, Ink, 1, 8);
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", Box(Ink, Ink, 1, 8));
        b.AddThemeStyleboxOverride("disabled", Box(Paper, Rule, 1, 8));
        foreach (var n in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            b.AddThemeColorOverride(n, on ? Paper : Ink);
        b.AddThemeColorOverride("font_disabled_color", Ink3);
    }
}

/// <summary>DraftSegment: a row of mutually exclusive cells, the selected one inked.</summary>
public sealed partial class Segment : HBoxContainer
{
    readonly Button[] _cells;
    public event Action<int> Changed;
    public int Selected { get; private set; }

    public Segment(string[] items, int selected, int size = 14)
    {
        AddThemeConstantOverride("separation", -1);
        _cells = new Button[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            int k = i;
            _cells[i] = Ui.Btn(items[i], () => Select(k, true), size);
            AddChild(_cells[i]);
        }
        Select(selected, false);
    }

    public void Select(int i, bool notify)
    {
        Selected = i;
        for (int k = 0; k < _cells.Length; k++) Ui.Style(_cells[k], k == i);
        if (notify) Changed?.Invoke(i);
    }
}
