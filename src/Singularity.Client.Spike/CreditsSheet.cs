using System;
using System.Linq;
using Godot;

namespace Singularity.Client.Spike;

/// <summary>Credits (DEBT-0050, ADR-0009): music and sounds from assets/credits.json, the single list the tests check against the audio folders.</summary>
public sealed partial class CreditsSheet : Control
{
    public event Action Closed;
    readonly PanelContainer _sheet;
    readonly Action<Vector2> _fit;
    readonly VBoxContainer _body = new();
    readonly CaptionButton _back;
    readonly ScrollContainer _scroll;
    System.Collections.Generic.IReadOnlyList<CreditEntry> _entries;

    public CreditsSheet()
    {
        SetAnchorsPreset(LayoutPreset.FullRect); Visible = false; MouseFilter = MouseFilterEnum.Ignore;
        AddChild(MenuUi.Scrim());
        _sheet = MenuUi.CenteredSheet(new Vector2(880, 560), out _fit);
        AddChild(_sheet);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 10);
        _sheet.AddChild(root);
        root.AddChild(Ui.Lbl("credits.title", 22, null, FontKind.Draft));
        root.AddChild(new ColorRect { Color = Ui.InkLine[Ui.Olive], CustomMinimumSize = new Vector2(0, 3) });
        _scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };   // A7: Tab scrolls the focused link into view
        _body.SizeFlagsHorizontal = SizeFlags.ExpandFill; _body.AddThemeConstantOverride("separation", 8);
        var pad = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };   // keeps the text clear of the 12 px bar
        pad.AddThemeConstantOverride("margin_right", 18);
        pad.AddChild(_body);
        _scroll.AddChild(pad);
        _scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        StyleScrollbar(_scroll);
        root.AddChild(_scroll);
        _back = MenuUi.Primary("credits.back", Close);
        // The button is ink-filled, so the focus frame sits outside it with a paper gap (A7: 2 px, ink on paper).
        _back.AddThemeStyleboxOverride("focus", new StyleBoxFlat { DrawCenter = false, BorderColor = Ui.Ink, ExpandMarginLeft = 3, ExpandMarginRight = 3, ExpandMarginTop = 3, ExpandMarginBottom = 3, AntiAliasing = true }.With2(f => { f.SetBorderWidthAll(2); f.SetCornerRadiusAll(4); }));
        _back.CustomMinimumSize = new Vector2(96, 36);   // A10: at least 32 du high
        var foot = new HBoxContainer();
        foot.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill }); foot.AddChild(_back);
        root.AddChild(foot);
        Loc.Changed += OnLang; TreeExiting += () => Loc.Changed -= OnLang;
        Resized += () => _fit(Size);
    }

    /// <summary>A12: a 12 px bar, ink-2 grabber on a rule-coloured track (both well above 3:1 to the sheet).</summary>
    static void StyleScrollbar(ScrollContainer sc)
    {
        var bar = sc.GetVScrollBar();
        bar.CustomMinimumSize = new Vector2(12, 0);
        bar.AddThemeStyleboxOverride("scroll", Ui.Box(Ui.Rule, null, 0, 0));
        foreach (var n in new[] { "grabber", "grabber_highlight", "grabber_pressed" })
            bar.AddThemeStyleboxOverride(n, Ui.Box(Ui.Ink2, Ui.Ink, 1, 0, 3));
    }

    void OnLang() { if (Visible) Rebuild(); }

    public void Open()
    {
        _entries ??= CreditsData.Parse(Godot.FileAccess.GetFileAsString("res://assets/credits.json"));
        Visible = true; _fit(Size);
        Rebuild();
        _back.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>Screenshot hook: scroll to a pixel offset.</summary>
    public void ShotScroll(int y) => _scroll.ScrollVertical = y;

    public void Close() { Visible = false; Closed?.Invoke(); }

    void Rebuild()
    {
        foreach (var c in _body.GetChildren()) { _body.RemoveChild(c); c.QueueFree(); }
        _body.AddChild(Ui.Wrap(Loc.T("credits.intro"), 14, Ui.Ink2));
        Section("credits.music", CreditsData.Music);
        Section("credits.sound", CreditsData.Sound);
        _body.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });   // A12: the last row is never clipped
    }

    void Section(string key, string kind)
    {
        _body.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        _body.AddChild(Ui.Lbl(key, 18, Ui.InkText[Ui.Olive], FontKind.Draft));
        _body.AddChild(new ColorRect { Color = Ui.Rule, CustomMinimumSize = new Vector2(0, 1) });
        foreach (var e in _entries.Where(x => x.Kind == kind))
        {
            var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 6);   // A10: gap between link targets >= 4 du
            box.AddChild(Ui.Wrap($"{e.Title} — {e.Author}", 16));
            var lic = new HBoxContainer(); lic.AddThemeConstantOverride("separation", 6);
            lic.AddChild(Ui.Lbl($"{Loc.T("credits.licence")}: {e.Licence} ·", 14, Ui.Ink2));
            lic.AddChild(Link(e.LicenceUrl));
            box.AddChild(lic);
            box.AddChild(Link(e.Url));
            string ch = e.ChangesIn(Loc.Lang);
            if (ch != "") box.AddChild(Ui.Wrap($"{Loc.T("credits.changes")}: {ch}", 14, Ui.Ink2));
            if (e.Files.Count == 0) box.AddChild(Status());
            var margin = new MarginContainer(); margin.AddThemeConstantOverride("margin_bottom", 8); margin.AddChild(box);
            _body.AddChild(margin);
        }
    }

    /// <summary>A URL that opens in the system browser on click or Enter (LinkButton.Uri); keyboard focus shows a 2 px ink frame (A6, A7).</summary>
    static LinkButton Link(string url)
    {
        var b = new LinkButton { Text = url, Uri = url, Underline = LinkButton.UnderlineMode.Always, FocusMode = FocusModeEnum.All, SizeFlagsHorizontal = SizeFlags.ShrinkBegin, TooltipText = url, CustomMinimumSize = new Vector2(0, 24) };   // A10: target at least 24 du high
        b.AddThemeFontOverride("font", Fonts.Text); b.AddThemeFontSizeOverride("font_size", 14);
        foreach (var n in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) b.AddThemeColorOverride(n, Ui.Ink);
        MenuUi.FocusRing(b);
        return b;
    }

    /// <summary>Status plate on its own line: an ink frame and a word, not a grey tone (C1, A5).</summary>
    static Control Status()
    {
        var l = Ui.Wrap(Loc.T("credits.soon"), 14, Ui.Ink);
        var pc = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        pc.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Hover, Ui.Ink, 1, 8, Ui.R2));
        pc.AddChild(l);
        return pc;
    }

    public override void _Input(InputEvent e)
    {
        if (!Visible || e is not InputEventKey { Pressed: true } k) return;
        int page = (int)(_scroll.Size.Y * 0.9f);
        int? step = k.Keycode switch { Key.Down => 48, Key.Up => -48, Key.Pagedown => page, Key.Pageup => -page, Key.Home => -1_000_000, Key.End => 1_000_000, _ => null };
        if (step is int d) { GetViewport().SetInputAsHandled(); _scroll.ScrollVertical = Math.Max(0, _scroll.ScrollVertical + d); return; }
        if (k.Keycode != Key.Escape) return;
        GetViewport().SetInputAsHandled();
        Close();
    }
}
