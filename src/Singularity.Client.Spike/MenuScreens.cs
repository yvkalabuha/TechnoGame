using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Singularity.Core.Data;

namespace Singularity.Client.Spike;

/// <summary>Shared building blocks of the menu screens (spec §1-§4).</summary>
public static class MenuUi
{
    public static StyleBoxFlat SheetBox() => Ui.Box(Ui.Sheet, Ui.Ink, 1, 16, Ui.R3, 16);

    /// <summary>Primary button: ink fill, sheet text (§17.7).</summary>
    public static CaptionButton Primary(string text, Action onPress)
    {
        var b = Ui.Btn(text, onPress);
        b.AddThemeStyleboxOverride("normal", Ui.Box(Ui.Ink, Ui.Ink, 1, 12));
        b.AddThemeStyleboxOverride("hover", Ui.Box(Ui.Ink2, Ui.Ink, 1, 12));
        b.AddThemeStyleboxOverride("pressed", Ui.Box(Ui.Ink2, Ui.Ink, 2, 12));
        foreach (var n in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) b.CaptionColor(n, Ui.Sheet);
        b.FocusMode = Control.FocusModeEnum.All;
        return b;
    }

    /// <summary>Focus ring: a second frame in ink, no fill (§17.7).</summary>
    public static void FocusRing(Control c) => c.AddThemeStyleboxOverride("focus", new StyleBoxFlat { DrawCenter = false, BorderColor = Ui.Ink, AntiAliasing = true }.With2(s => { s.SetBorderWidthAll(2); s.SetCornerRadiusAll(4); }));

    /// <summary>Sheet centred in its parent, never larger than the parent minus a margin (so that nothing is cropped at 1280x720 and 150% UI scale).</summary>
    public static void Fit(Control sheet, Vector2 parent, Vector2 design)
    {
        sheet.CustomMinimumSize = new Vector2(Math.Min(design.X, parent.X - 24), Math.Min(design.Y, parent.Y - 24));
    }

    /// <summary>Full-screen ink 50 % scrim (35 % was too faint behind the dialogs, review v0.5b) under a modal sheet; it also blocks the mouse for everything below.</summary>
    public static ColorRect Scrim()
    {
        var r = new ColorRect { Color = new Color(Ui.Ink, 0.5f), MouseFilter = Control.MouseFilterEnum.Stop };
        r.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        return r;
    }

    public static PanelContainer CenteredSheet(Vector2 design, out Action<Vector2> fit)
    {
        var p = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        p.AddThemeStyleboxOverride("panel", SheetBox());
        PaperSurface.Apply(p, SurfaceKind.Sheet, "sheet");
        p.AnchorLeft = p.AnchorRight = p.AnchorTop = p.AnchorBottom = 0.5f;
        p.GrowHorizontal = Control.GrowDirection.Both; p.GrowVertical = Control.GrowDirection.Both;
        fit = vp => Fit(p, vp, design);
        return p;
    }

    public static void StyleOption(OptionButton o)
    {
        o.AddThemeFontOverride("font", Fonts.Text); o.AddThemeFontSizeOverride("font_size", 14);
        o.AddThemeStyleboxOverride("normal", Ui.Box(Ui.Sheet, Ui.Ink, 1, 10)); o.AddThemeStyleboxOverride("hover", Ui.Box(Ui.Hover, Ui.Ink, 1, 10));
        o.AddThemeStyleboxOverride("pressed", Ui.Box(Ui.Hover, Ui.Ink, 2, 10)); o.AddThemeStyleboxOverride("disabled", Ui.Box(Ui.Paper, Ui.Rule, 1, 10));
        foreach (var n in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) o.AddThemeColorOverride(n, Ui.Ink);
        o.AddThemeColorOverride("font_disabled_color", Ui.Ink3);
        FocusRing(o);
        var pop = o.GetPopup();
        pop.AddThemeFontOverride("font", Fonts.Text); pop.AddThemeFontSizeOverride("font_size", 14);
        pop.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Sheet, Ui.Ink, 1, 6, Ui.R3));
        pop.AddThemeStyleboxOverride("hover", Ui.Box(Ui.Hover, null, 0, 6));
        pop.AddThemeColorOverride("font_color", Ui.Ink); pop.AddThemeColorOverride("font_hover_color", Ui.Ink); pop.AddThemeColorOverride("font_disabled_color", Ui.Ink3);
    }
}

/// <summary>Main menu (spec §1): a radius.3 panel on the left over the live globe; inside a game (Esc) the same panel in the centre over a dimmed client.</summary>
public sealed partial class MainMenu : Control
{
    public event Action Continue, NewGame, OpenSettings, ToMain, Quit;
    readonly ColorRect _dim = MenuUi.Scrim();
    readonly PanelContainer _panel = new() { MouseFilter = MouseFilterEnum.Stop };
    readonly VBoxContainer _box = new();
    readonly List<(Key Key, Action Act, Button B)> _keys = new();
    bool _inGame;
    public bool InGame => _inGame;

    public MainMenu()
    {
        SetAnchorsPreset(LayoutPreset.FullRect); Visible = false; MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_dim);
        _panel.AddThemeStyleboxOverride("panel", MenuUi.SheetBox());
        _panel.CustomMinimumSize = new Vector2(320, 0);
        _box.AddThemeConstantOverride("separation", 6);
        _panel.AddChild(_box);
        AddChild(_panel);
    }

    public void Open(bool inGame)
    {
        _inGame = inGame; Visible = true; _dim.Visible = inGame;
        _panel.AnchorTop = _panel.AnchorBottom = 0.5f; _panel.GrowVertical = GrowDirection.Both; _panel.OffsetTop = _panel.OffsetBottom = 0;
        if (inGame) { _panel.AnchorLeft = _panel.AnchorRight = 0.5f; _panel.GrowHorizontal = GrowDirection.Both; _panel.OffsetLeft = _panel.OffsetRight = 0; }
        else { _panel.AnchorLeft = _panel.AnchorRight = 0; _panel.GrowHorizontal = GrowDirection.End; _panel.OffsetLeft = 40; _panel.OffsetRight = 40; }
        Rebuild();
    }

    public void Close() => Visible = false;

    void Rebuild()
    {
        foreach (var c in _box.GetChildren()) { _box.RemoveChild(c); c.QueueFree(); }
        _keys.Clear();
        _box.AddChild(Ui.Lbl("PROJECT SINGULARITY", 24, null, FontKind.Draft));
        var stripe = new HBoxContainer(); stripe.AddThemeConstantOverride("separation", 0);
        for (int i = 0; i < 5; i++) stripe.AddChild(new ColorRect { Color = Ui.InkLine[i], CustomMinimumSize = new Vector2(56, 4) });
        _box.AddChild(stripe);
        _box.AddChild(Ui.Lbl("v0.5b · client spike", 14, Ui.Ink3, FontKind.Mono));
        _box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

        Button first = null;
        void Item(string key, int ink, string hint, Key k, Action act, string reason = null)
        {
            var b = new Button { Text = key, Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.All, CustomMinimumSize = new Vector2(0, 36), Disabled = reason != null };
            b.AddThemeFontOverride("font", Fonts.Text); b.AddThemeFontSizeOverride("font_size", 16);
            StyleItem(b, ink);
            var h = Ui.Lbl(hint, 14, Ui.Ink2, FontKind.Mono);
            h.AnchorLeft = h.AnchorRight = 1; h.AnchorTop = 0; h.AnchorBottom = 1; h.OffsetLeft = -60; h.OffsetRight = -12; h.OffsetTop = h.OffsetBottom = 0;
            h.HorizontalAlignment = HorizontalAlignment.Right; h.VerticalAlignment = VerticalAlignment.Center;
            b.AddChild(h);
            if (reason == null) { b.Pressed += act; _keys.Add((k, act, b)); }
            else b.TooltipText = reason;
            _box.AddChild(b);
            if (reason != null) _box.AddChild(Ui.Wrap(reason, 14, Ui.Ink3));
            if (reason == null) first ??= b;
        }
        if (_inGame)
        {
            Item("menu.continue", Ui.Teal, "Esc", Key.Escape, () => Continue?.Invoke());
            Item("menu.load", Ui.Sand, "L", Key.L, null, "menu.reason.persist");
            Item("menu.save", Ui.Violet, "S", Key.S, null, "menu.reason.persist");
            Item("menu.settings", Ui.Slate, "O", Key.O, () => OpenSettings?.Invoke());
            Item("menu.tomain", Ui.Plum, "M", Key.M, () => ToMain?.Invoke());
            Item("menu.quit", Ui.Plum, "Q", Key.Q, () => Quit?.Invoke());
        }
        else
        {
            Item("menu.continue", Ui.Teal, "C", Key.C, null, "menu.reason.nosave");
            Item("menu.new", Ui.Olive, "N", Key.N, () => NewGame?.Invoke());
            Item("menu.load", Ui.Sand, "L", Key.L, null, "menu.reason.persist");
            Item("menu.save", Ui.Violet, "S", Key.S, null, "menu.reason.nogame");
            Item("menu.settings", Ui.Slate, "O", Key.O, () => OpenSettings?.Invoke());
            Item("menu.quit", Ui.Plum, "Q", Key.Q, () => Quit?.Invoke());
        }
        if (first != null) first.CallDeferred(Control.MethodName.GrabFocus);
    }

    static void StyleItem(Button b, int ink)
    {
        StyleBoxFlat S(Color bg, Color border, int bw, int left)
        {
            var s = Ui.Box(bg, border, bw, 12, Ui.R2);
            s.BorderWidthLeft = left; s.BorderColor = border;
            s.ContentMarginLeft = 14;
            return s;
        }
        b.AddThemeStyleboxOverride("normal", S(Ui.Sheet, Ui.InkWash[ink], 0, 3));
        b.AddThemeStyleboxOverride("hover", S(Ui.Hover, Ui.Ink, 1, 3));
        b.AddThemeStyleboxOverride("pressed", S(Ui.Hover, Ui.Ink, 2, 3));
        b.AddThemeStyleboxOverride("disabled", S(Ui.Paper, Ui.Rule, 1, 1));
        // The domain edge of the selected item is in the ink line colour; the frame is ink.
        var hover = (StyleBoxFlat)b.GetThemeStylebox("hover");
        hover.BorderColor = Ui.Ink; hover.BorderWidthLeft = 5;
        var focus = new StyleBoxFlat { BgColor = Ui.Hover, BorderColor = Ui.Ink, AntiAliasing = true, ContentMarginLeft = 14 };
        focus.SetBorderWidthAll(2); focus.BorderWidthLeft = 5; focus.SetCornerRadiusAll(4);
        b.AddThemeStyleboxOverride("focus", focus);
        foreach (var n in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) b.AddThemeColorOverride(n, Ui.Ink);
        b.AddThemeColorOverride("font_disabled_color", Ui.Ink3);
        _ = ink;
    }

    public override void _Input(InputEvent e)
    {
        if (!Visible || e is not InputEventKey { Pressed: true, Echo: false } k) return;
        foreach (var (key, act, _) in _keys)
            if (k.Keycode == key) { GetViewport().SetInputAsHandled(); act(); return; }
        if (k.Keycode == Key.Escape) GetViewport().SetInputAsHandled();   // Esc does nothing on the title screen, and Continue (above) handles it in a game
    }
}

/// <summary>«New game» (spec §2): the powers of the scenario with a search field, a card of the selected one with real starting figures, the seed, Back / Start.</summary>
public sealed partial class NewGameSheet : Control
{
    public event Action Back;
    public event Action<string, int> Start;
    public event Action<string> Previewed;
    readonly PanelContainer _sheet;
    readonly Action<Vector2> _fit;
    readonly LineEdit _search = new() { PlaceholderText = "ng.search", ClearButtonEnabled = false }, _seed = new();
    readonly VBoxContainer _list = new(), _card = new();
    readonly Button _start;
    ScenarioData _sc; Func<string, Color> _tint; string _sel; string _scenarioId = "";

    public string Selected => _sel;
    public int Seed => int.TryParse(_seed.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : -1;

    public NewGameSheet()
    {
        SetAnchorsPreset(LayoutPreset.FullRect); Visible = false; MouseFilter = MouseFilterEnum.Ignore;
        AddChild(MenuUi.Scrim());
        _sheet = MenuUi.CenteredSheet(new Vector2(960, 600), out _fit);
        AddChild(_sheet);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 10);
        _sheet.AddChild(root);
        var head = Ui.Lbl("menu.new", 22, null, FontKind.Draft);
        root.AddChild(head);
        root.AddChild(new ColorRect { Color = Ui.InkLine[Ui.Olive], CustomMinimumSize = new Vector2(0, 3) });
        var cols = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 16);
        root.AddChild(cols);

        var left = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) }; left.AddThemeConstantOverride("separation", 8);
        _search.AddThemeFontOverride("font", Fonts.Text); _search.AddThemeFontSizeOverride("font_size", 14);
        _search.AddThemeStyleboxOverride("normal", Ui.Box(Ui.Paper, Ui.Ink, 1, 8)); _search.AddThemeColorOverride("font_color", Ui.Ink);
        _search.AddThemeColorOverride("font_placeholder_color", Ui.Ink3); MenuUi.FocusRing(_search);
        _search.TextChanged += _ => RebuildList();
        left.AddChild(_search);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _list.SizeFlagsHorizontal = SizeFlags.ExpandFill; _list.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_list); left.AddChild(scroll);
        cols.AddChild(left);

        var rcard = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rcard.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Paper, Ui.Rule, 1, 14, Ui.R2));
        _card.AddThemeConstantOverride("separation", 8); rcard.AddChild(_card);
        cols.AddChild(rcard);

        var foot = new HBoxContainer(); foot.AddThemeConstantOverride("separation", 10);
        foot.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var back = Ui.Btn("ui.back", () => Back?.Invoke()); back.FocusMode = FocusModeEnum.All; MenuUi.FocusRing(back);
        _start = MenuUi.Primary("ng.start", () => Start?.Invoke(_sel, Seed)); _start.TooltipText = "ng.start.tip"; MenuUi.FocusRing(_start);
        foot.AddChild(back); foot.AddChild(_start);
        root.AddChild(foot);

        _seed.AddThemeFontOverride("font", Fonts.Mono); _seed.AddThemeFontSizeOverride("font_size", 14);
        _seed.AddThemeStyleboxOverride("normal", Ui.Box(Ui.Sheet, Ui.Ink, 1, 8)); _seed.AddThemeColorOverride("font_color", Ui.Ink); MenuUi.FocusRing(_seed);
        _seed.TextChanged += _ => UpdateStart();
        Loc.Changed += OnLang; TreeExiting += () => Loc.Changed -= OnLang;
        Resized += () => _fit(Size);
    }

    void OnLang() { if (Visible) { RebuildList(); RebuildCard(); } }

    public void Open(ScenarioData sc, Func<string, Color> tint)
    {
        _sc = sc; _tint = tint; _sel = null; _search.Text = ""; _scenarioId = sc.Id;
        _seed.Text = new Random().Next(1, 1_000_000).ToString(CultureInfo.InvariantCulture);
        Visible = true; _fit(Size);
        RebuildList(); RebuildCard(); UpdateStart();
        _search.CallDeferred(Control.MethodName.GrabFocus);
    }

    public void Close() => Visible = false;

    /// <summary>Selects a power as a click on its row would.</summary>
    public void Select(string id) { _sel = id; RebuildList(); RebuildCard(); UpdateStart(); Previewed?.Invoke(id); }

    void UpdateStart() => _start.Disabled = _sel == null || Seed < 0;

    static string NameIn(string lang, string id) => Loc.In(lang, "actor." + id);

    void RebuildList()
    {
        foreach (var c in _list.GetChildren()) { _list.RemoveChild(c); c.QueueFree(); }
        string q = _search.Text.Trim().ToLowerInvariant();
        var ids = _sc.ActorId.ToArray()
            .Where(id => q == "" || NameIn("ru", id).ToLowerInvariant().Contains(q) || NameIn("en", id).ToLowerInvariant().Contains(q) || id.ToLowerInvariant().Contains(q))
            .OrderBy(id => Names.Actor(id), StringComparer.OrdinalIgnoreCase).ToList();
        if (ids.Count == 0) _list.AddChild(Ui.Lbl("ng.none", 14, Ui.Ink3));
        foreach (var id in ids)
        {
            string aid = id; bool on = id == _sel;
            var b = new Button { Text = Names.Actor(id), Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.All, CustomMinimumSize = new Vector2(0, 34), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            b.AddThemeFontOverride("font", Fonts.Text); b.AddThemeFontSizeOverride("font_size", 16);
            StyleBoxFlat S(Color bg, int bw) { var s = Ui.Box(bg, on ? Ui.Ink : Ui.Rule, on ? 2 : bw, 12, Ui.R2); s.ContentMarginLeft = 38; return s; }
            b.AddThemeStyleboxOverride("normal", S(on ? Ui.Hover : Ui.Sheet, 1)); b.AddThemeStyleboxOverride("hover", S(Ui.Hover, 1)); b.AddThemeStyleboxOverride("pressed", S(Ui.Hover, 2));
            MenuUi.FocusRing(b);
            foreach (var n in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) b.AddThemeColorOverride(n, Ui.Ink);
            var chip = new ColorRect { Color = _tint(id), CustomMinimumSize = new Vector2(18, 18), MouseFilter = MouseFilterEnum.Ignore };
            chip.Position = new Vector2(10, 8); chip.Size = new Vector2(18, 18);
            var frame = new Panel { MouseFilter = MouseFilterEnum.Ignore, Position = chip.Position, Size = chip.Size };
            frame.AddThemeStyleboxOverride("panel", new StyleBoxFlat { DrawCenter = false, BorderColor = Ui.Ink, AntiAliasing = true }.With2(s => { s.SetBorderWidthAll(1); s.SetCornerRadiusAll(2); }));
            b.AddChild(chip); b.AddChild(frame);
            b.Pressed += () => { _sel = aid; RebuildList(); RebuildCard(); UpdateStart(); Previewed?.Invoke(aid); };
            _list.AddChild(b);
        }
    }

    void RebuildCard()
    {
        _seed.GetParent()?.RemoveChild(_seed);   // keep the field alive across rebuilds
        foreach (var c in _card.GetChildren()) { _card.RemoveChild(c); c.QueueFree(); }
        if (_sel == null) { _card.AddChild(Ui.Wrap("ng.pick", 16, Ui.Ink2)); return; }
        int i = Array.IndexOf(_sc.ActorId.ToArray(), _sel);
        _card.AddChild(Ui.Lbl(Names.Actor(_sel), 24, null, FontKind.Draft));
        _card.AddChild(new ColorRect { Color = _tint(_sel), CustomMinimumSize = new Vector2(0, 4) });
        void Row(string key, string value)
        {
            var r = new HBoxContainer(); r.AddThemeConstantOverride("separation", 10);
            var l = Ui.Lbl(key, 14, Ui.Ink2); l.CustomMinimumSize = new Vector2(200, 0); r.AddChild(l);
            r.AddChild(Ui.Lbl(value, 16, null, FontKind.Mono)); _card.AddChild(r);
        }
        Row("ng.gdp", Loc.N(_sc.Gdp[i].ToDouble(), 0));
        Row("ng.gdppc", Loc.N(_sc.GdpPerCapita[i].ToDouble(), 0));
        Row("ng.budget", Loc.N(_sc.BudgetIndex[i].ToDouble(), 2));
        Row("ng.known", (_sc.KnownStart[i + 1] - _sc.KnownStart[i]).ToString(CultureInfo.InvariantCulture));
        Row("ng.trophy", _sc.TrophyEngineers[i].ToString(CultureInfo.InvariantCulture));
        _card.AddChild(Ui.Lbl("ng.source", 14, Ui.Ink3));
        _card.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        var sr = new HBoxContainer(); sr.AddThemeConstantOverride("separation", 10);
        var sl = Ui.Lbl("ng.seed", 14, Ui.Ink2); sl.CustomMinimumSize = new Vector2(200, 0); sr.AddChild(sl);
        _seed.CustomMinimumSize = new Vector2(120, 0);
        sr.AddChild(_seed);
        sr.AddChild(Ui.Btn("↻", () => { _seed.Text = new Random().Next(1, 1_000_000).ToString(CultureInfo.InvariantCulture); UpdateStart(); }));
        _card.AddChild(sr);
        var sc = new HBoxContainer(); sc.AddThemeConstantOverride("separation", 10);
        var scl = Ui.Lbl("ng.scenario", 14, Ui.Ink2); scl.CustomMinimumSize = new Vector2(200, 0); sc.AddChild(scl);
        sc.AddChild(Ui.Lbl("1945 · " + _scenarioId, 14, null, FontKind.Mono)); _card.AddChild(sc);
    }

    public override void _Input(InputEvent e)
    {
        if (Visible && e is InputEventKey { Pressed: true, Keycode: Key.Escape }) { GetViewport().SetInputAsHandled(); Back?.Invoke(); }
    }
}

static class StyleExt
{
    public static StyleBoxFlat With2(this StyleBoxFlat s, Action<StyleBoxFlat> f) { f(s); return s; }
}
