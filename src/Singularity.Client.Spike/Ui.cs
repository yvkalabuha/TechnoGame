using System;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;

namespace Singularity.Client.Spike;

public enum FontKind { Text, Mono, Draft, Map }

/// <summary>Design-system tokens (UI_DESIGN_SYSTEM): paper register, square corners, ink lines, no shadows or glow. Minimum size 14 px.</summary>
public static class Ui
{
    public const int MinSize = 14;
    // Palette C «Акварель» (UI_DESIGN_SYSTEM §17, decisions #302, #303, #326): the tokens come from Palette.C, which the contrast validator reads too.
    static Color H(string hex) => new(hex);
    public static readonly Color Paper = H(Palette.C.Paper), Sheet = H(Palette.C.Sheet), Hover = H(Palette.C.Hover), Ink = H(Palette.Ink),
        Ink2 = H(Palette.Ink2), Ink3 = H(Palette.Ink3), Rule = H(Palette.Rule), Normal = new("86837C"), Border = H(Palette.Border),
        Alarm = H(Palette.C.Alarm), Warn = H(Palette.C.Warn), WarnMark = H(Palette.WarnMark), Command = H(Palette.C.Command), Seq2 = new("C2CDD3");

    // Radius scale (§17.7): 0 data, 2 keys and plates, 4 buttons and fields, 6 panels and sheets.
    public const float R1 = 2, R2 = 4, R3 = 6;

    // The six inks in the order sand, teal, violet, olive, plum, slate. Line = frames, nodes, bars; wash = fill; text = dark step for text.
    public static readonly Color[] InkLine = Palette.C.Line.Select(H).ToArray(), InkWash = Palette.C.Wash.Select(H).ToArray(), InkText = Palette.C.Text.Select(H).ToArray();
    public const int Sand = 0, Teal = 1, Violet = 2, Olive = 3, Plum = 4, Slate = 5;
    // Disciplines of the current tree (Aero, Prop, Metal, Chem, Mech) take teal, plum, slate, olive, sand; violet is kept for the «parameter» block.
    static readonly int[] DiscInk = { Teal, Plum, Slate, Olive, Sand };
    public static readonly Color[] DiscLine = DiscInk.Select(i => InkLine[i]).ToArray(), DiscWash = DiscInk.Select(i => InkWash[i]).ToArray(), DiscText = DiscInk.Select(i => InkText[i]).ToArray();
    public static Color[] Tints => TintCache ??= Palette.C.Tints.Select(H).ToArray();
    static Color[] TintCache;

    public static Font FontOf(FontKind k) => k switch { FontKind.Mono => Fonts.Mono, FontKind.Draft => Fonts.DraftBold, FontKind.Map => Fonts.Map, _ => Fonts.Text };

    /// <summary>vMargin: vertical content margin when it is not half of the horizontal one (textured surfaces keep 10 du from every edge, validator T6).</summary>
    public static StyleBoxFlat Box(Color bg, Color? border = null, int bw = 1, float margin = 6, float radius = R2, float? vMargin = null)
    {
        var s = new StyleBoxFlat { BgColor = bg, AntiAliasing = true, ShadowSize = 0, ContentMarginLeft = margin, ContentMarginRight = margin, ContentMarginTop = vMargin ?? margin * 0.5f, ContentMarginBottom = vMargin ?? margin * 0.5f };
        if (border is Color c) { s.SetBorderWidthAll(bw); s.BorderColor = c; }
        s.SetCornerRadiusAll((int)radius);
        return s;
    }

    /// <summary>Text may be a localization key: Godot translates it and re-translates it when the locale changes.</summary>
    public static Label Lbl(string text, int size = 14, Color? color = null, FontKind kind = FontKind.Text)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", FontOf(kind));
        l.AddThemeFontSizeOverride("font_size", Math.Max(size, MinSize));
        l.AddThemeColorOverride("font_color", color ?? Ink);
        return l;
    }

    public static Label Num(string text, int size = 14, Color? color = null, int width = 0)
    {
        var l = Lbl(text, size, color, FontKind.Mono);
        l.HorizontalAlignment = HorizontalAlignment.Right;
        if (width > 0) l.CustomMinimumSize = new Vector2(width, 0);
        return l;
    }

    /// <summary>Block heading: drafting face in a real semibold instance, caps by the key text, ink-2 (contrast on paper).</summary>
    public static Label Head(string key)
    {
        var l = Lbl(key, 14, Ink2, FontKind.Draft);
        return l;
    }

    /// <summary>Wrapped text where every number is set in the mono face.</summary>
    public static Prose Wrap(string text, int size = 14, Color? color = null) => new(text, Math.Max(size, MinSize), color ?? Ink);

    /// <summary>Row with a small square marker (colour only here: the rule is «colour = deviation») and ink text.</summary>
    public static HBoxContainer MarkedRow(Color marker, Control text, bool triangle = false)
    {
        var r = new HBoxContainer();
        r.AddThemeConstantOverride("separation", 6);
        // a warning of the lever windows adds a shape (▲): it differs from the ■ of a chosen item in a greyscale frame too (A5)
        Control m;
        if (triangle || marker == Warn)   // every warning carries the same shape
        {
            var t = new Label { Text = "▲", SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
            t.AddThemeColorOverride("font_color", marker == Warn ? WarnMark : marker); t.AddThemeFontSizeOverride("font_size", 14);
            m = t;
        }
        else m = new ColorRect { Color = marker, CustomMinimumSize = new Vector2(9, 9), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        r.AddChild(m); r.AddChild(text);
        return r;
    }

    /// <summary>Mock marker: neutral plate (ink-3), never a warning colour. Every control or figure without a Session API carries it.</summary>
    public static Label Mock(string key = "ui.mock")
    {
        var l = Lbl(key, 14, Ink3);
        var sb = Box(Paper, Ink3, 1, 4);
        l.AddThemeStyleboxOverride("normal", sb);
        return l;
    }

    public static CaptionButton Btn(string text, Action onPress, int size = 14, bool command = false, bool focusable = false)
    {
        var b = new CaptionButton { Text = text, FocusMode = focusable ? Control.FocusModeEnum.All : Control.FocusModeEnum.None, ToggleMode = false };
        if (focusable) b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());   // CaptionButton draws its own two-tone ring (A7)
        b.TwoToneFocus = focusable;
        b.AddThemeFontOverride("font", Fonts.Text);
        b.AddThemeFontSizeOverride("font_size", Math.Max(size, MinSize));
        PaperSurface.Apply(b, SurfaceKind.Button, text);   // StyleButton takes the material off again while the button is an order-ready command plate (an alert colour)
        StyleButton(b, command);
        b.Pressed += onPress;
        return b;
    }

    /// <summary>Plain button: sheet with an ink frame. Command button (an order is ready): ultramarine plate, white text.</summary>
    public static void StyleButton(CaptionButton b, bool command)
    {
        // An order-ready command plate is an alert colour: it gets no paper texture, and takes it back when it turns plain again.
        if (command && b.Material != null) { b.SetMeta("paper_mat", b.Material); b.Material = null; }
        else if (!command && b.Material == null && b.HasMeta("paper_mat")) b.Material = b.GetMeta("paper_mat").As<Material>();
        b.AddThemeStyleboxOverride("normal", command ? Box(Command, Ink, 1, 10) : Box(Sheet, Ink, 1, 10));
        b.AddThemeStyleboxOverride("hover", command ? Box(Command.Lightened(0.1f), Ink, 1, 10) : Box(Hover, Ink, 1, 10));
        b.AddThemeStyleboxOverride("pressed", Box(Ink, Ink, 1, 10));
        b.AddThemeStyleboxOverride("disabled", Box(Paper, Rule, 1, 10));
        foreach (var n in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            b.CaptionColor(n, command ? Colors.White : Ink);
        b.CaptionColor("font_pressed_color", Paper);
        b.CaptionColor("font_disabled_color", H(Palette.DisabledText));   // 4.5:1 on paper and on the paper texture (DEBT-0095, test DisabledButtonText)
    }

    public static ColorRect VRule() => new() { Color = Ink, CustomMinimumSize = new Vector2(2, 26), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
}

/// <summary>DraftSegment: mutually exclusive cells of one width (the widest label in the current language), no fill:
/// the selected cell has an ink frame and a thick bottom tick, the others a thin rule frame. Items are keys or literals.</summary>
public sealed partial class Segment : HBoxContainer
{
    readonly Button[] _cells;
    readonly string[] _items;
    readonly int _size;
    readonly bool _translate;
    readonly float _minH;
    public event Action<int> Changed;
    public int Selected { get; private set; }

    /// <summary>Unavailable segment: cells are disabled (rule frame, ink-3 text).</summary>
    public bool Enabled { set { foreach (var c in _cells) { c.Disabled = !value; if (!value) { c.AddThemeStyleboxOverride("disabled", Ui.Box(Ui.Paper, Ui.Rule, 1, 8)); c.AddThemeColorOverride("font_disabled_color", Ui.Ink3); } } } }

    public Segment(string[] items, int selected, int size = 14, bool translate = true, bool focusable = false, float minHeight = 0)
    {
        _minH = minHeight; _items = items; _size = Math.Max(size, Ui.MinSize); _translate = translate;
        AddThemeConstantOverride("separation", -1);
        _cells = new Button[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            int k = i;
            _cells[i] = new Button { Text = items[i], FocusMode = focusable ? FocusModeEnum.All : FocusModeEnum.None, ClipText = false };
            if (focusable) MenuUi.FocusRing(_cells[i]);
            _cells[i].AddThemeFontOverride("font", Fonts.Text);
            _cells[i].AddThemeFontSizeOverride("font_size", _size);
            _cells[i].Pressed += () => Select(k, true);
            AddChild(_cells[i]);
        }
        Select(selected, false);
        Loc.Changed += Fit;
        TreeExiting += () => Loc.Changed -= Fit;
        Fit();
    }

    /// <summary>Same width for every cell so that columns of segments line up and no label is clipped in either language.</summary>
    void Fit()
    {
        float w = 0;
        foreach (var it in _items) w = Math.Max(w, Fonts.Text.GetStringSize(_translate ? Loc.T(it) : it, HorizontalAlignment.Left, -1, _size).X);
        foreach (var c in _cells) c.CustomMinimumSize = new Vector2(MathF.Ceiling(w) + 18, _minH);
    }

    public void Select(int i, bool notify)
    {
        Selected = i;
        for (int k = 0; k < _cells.Length; k++) Style(_cells[k], k == i);
        if (notify) Changed?.Invoke(i);
    }

    static void Style(Button b, bool on)
    {
        StyleBoxFlat Mk(Color bg, bool sel)
        {
            var s = Ui.Box(bg, sel ? Ui.Ink : Ui.Rule, sel ? 2 : 1, 8, Ui.R2);
            if (sel) s.BorderWidthBottom = 5;
            return s;
        }
        b.AddThemeStyleboxOverride("normal", Mk(Ui.Sheet, on));
        b.AddThemeStyleboxOverride("hover", Mk(Ui.Hover, on));
        b.AddThemeStyleboxOverride("pressed", Mk(Ui.Hover, true));
        foreach (var n in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            b.AddThemeColorOverride(n, on ? Ui.Ink : Ui.Ink2);
    }
}

/// <summary>Running text: Golos Text with every number (also 12,5 and 40%) in PT Mono (design system: figures are mono, also inside prose).
/// The text may be a localization key; it is translated here and again when the language changes.</summary>
public sealed partial class Prose : RichTextLabel
{
    string _raw = "";

    public Prose(string text, int size, Color color)
    {
        BbcodeEnabled = true; FitContent = true; ScrollActive = false; AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AutoTranslateMode = AutoTranslateModeEnum.Disabled;
        MouseFilter = MouseFilterEnum.Ignore;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        CustomMinimumSize = new Vector2(60, 0);
        AddThemeFontOverride("normal_font", Fonts.Text);
        AddThemeFontOverride("mono_font", Fonts.Mono);
        AddThemeFontSizeOverride("normal_font_size", size);
        AddThemeFontSizeOverride("mono_font_size", size);
        AddThemeColorOverride("default_color", color);
        Text = text;
        Loc.Changed += Refresh;
        TreeExiting += () => Loc.Changed -= Refresh;
    }

    /// <summary>Sets the text (a key or a literal); numbers get the mono face.</summary>
    public new string Text
    {
        get => _raw;
        set { _raw = value ?? ""; Refresh(); }
    }

    void Refresh()
    {
        string s = Loc.Has(_raw) ? Loc.T(_raw) : _raw;
        base.Text = LeverText.MarkNumbers(s);
    }
}

/// <summary>Button whose caption is drawn by a child Label, so that the paper texture (a material on the button) never touches text. Button.Text stays the source of the text and
/// of the minimum size, but the button's own text is transparent; colours go through <see cref="CaptionColor"/> (the theme font colours stay transparent).</summary>
public sealed partial class CaptionButton : Button
{
    readonly Label _cap = new() { MouseFilter = MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    /// <summary>Two-tone focus ring (A7): 2 px ink outside, 2 px paper inside, so that one of the two reads on the paper and on the command fill alike.</summary>
    public bool TwoToneFocus;
    static readonly StyleBoxFlat RingOuter = Ring(Ui.Ink), RingInner = Ring(Ui.Paper);
    static StyleBoxFlat Ring(Color c) { var s = new StyleBoxFlat { DrawCenter = false, BorderColor = c }; s.SetBorderWidthAll(2); s.SetCornerRadiusAll(3); return s; }
    Color _normal = Ui.Ink, _hover = Ui.Ink, _pressed = Ui.Paper, _disabled = Ui.Ink3;

    public CaptionButton()
    {
        _cap.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_cap);
        foreach (var n in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color", "font_disabled_color" })
            AddThemeColorOverride(n, Colors.Transparent);
    }

    /// <summary>Same names as the theme colours of a Button.</summary>
    public void CaptionColor(string name, Color c)
    {
        switch (name)
        {
            case "font_color": case "font_focus_color": _normal = c; break;
            case "font_hover_color": _hover = c; break;
            case "font_pressed_color": _pressed = c; break;
            case "font_disabled_color": _disabled = c; break;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (TwoToneFocus && HasFocus()) { DrawStyleBox(RingOuter, new Rect2(Vector2.Zero, Size)); DrawStyleBox(RingInner, new Rect2(new Vector2(2, 2), Size - new Vector2(4, 4))); }
        var sb = GetThemeStylebox("normal");
        _cap.OffsetLeft = sb.GetContentMargin(Side.Left); _cap.OffsetRight = -sb.GetContentMargin(Side.Right);
        _cap.OffsetTop = sb.GetContentMargin(Side.Top); _cap.OffsetBottom = -sb.GetContentMargin(Side.Bottom);
        _cap.Text = Text;
        _cap.HorizontalAlignment = Alignment;   // a list button is left-aligned and wraps (A9)
        _cap.AutowrapMode = AutowrapMode;
        _cap.AddThemeFontOverride("font", GetThemeFont("font"));
        _cap.AddThemeFontSizeOverride("font_size", GetThemeFontSize("font_size"));
        _cap.AddThemeColorOverride("font_color", GetDrawMode() switch
        {
            DrawMode.Disabled => _disabled,
            DrawMode.Pressed or DrawMode.HoverPressed => _pressed,
            DrawMode.Hover => _hover,
            _ => _normal,
        });
    }
}

