using System;
using System.Linq;
using Godot;

namespace Singularity.Client.Spike;

/// <summary>Settings (spec §4): three tabs (Video, Sound, General), Reset / Cancel / Apply, and the 15-second rollback dialog for the risky video changes.
/// Controls that have no engine or Session effect yet carry the «mock» plate.</summary>
public sealed partial class SettingsSheet : Control
{
    /// <summary>Slider of the Alert bus: the zone below the floor is hatched and the value never goes under it, by mouse or key.</summary>
    sealed partial class AlertSlider : HSlider
    {
        public override void _Draw()
        {
            float w = Size.X * (float)(AudioSettings.AlertFloor);
            float y = Size.Y * 0.5f;
            for (float x = 0; x < w; x += 5) DrawLine(new Vector2(x, y + 4), new Vector2(Math.Min(x + 8, w), y - 4), Ui.Ink2, 1f);
        }
    }

    public event Action Closed;
    /// <summary>Plays a test sound on a bus (index in SettingsData.BusNames); null = no audio yet (no game), the button is disabled.</summary>
    public Action<int> TestBus;
    public event Action<SettingsData> Applied;

    readonly PanelContainer _sheet;
    readonly Action<Vector2> _fit;
    readonly VBoxContainer _body = new();
    readonly Button[] _tabs = new Button[3];
    readonly ColorRect _underline = new() { CustomMinimumSize = new Vector2(0, 3) };
    readonly PanelContainer _dialog = new();
    readonly Label _dialogText = Ui.Lbl("", 16);
    readonly RollbackTimer _rb = new(15);
    SettingsData _w = new(), _orig = new(), _before;
    int _tab;
    int _soundFocus = 1;

    static readonly int[] TabInk = { Ui.Teal, Ui.Violet, Ui.Sand };
    static readonly string[] TabKeys = { "set.video", "set.sound", "set.general" };

    public bool DialogOpen => _dialog.Visible;

    public SettingsSheet()
    {
        SetAnchorsPreset(LayoutPreset.FullRect); Visible = false; MouseFilter = MouseFilterEnum.Ignore;
        _sheet = MenuUi.CenteredSheet(new Vector2(880, 560), out _fit);
        AddChild(_sheet);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 10);
        _sheet.AddChild(root);
        root.AddChild(Ui.Lbl("menu.settings", 22, null, FontKind.Draft));
        root.AddChild(_underline);
        var cols = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 16);
        root.AddChild(cols);
        var tabs = new VBoxContainer { CustomMinimumSize = new Vector2(180, 0) }; tabs.AddThemeConstantOverride("separation", 4);
        for (int i = 0; i < 3; i++)
        {
            int k = i;
            _tabs[i] = new Button { Text = TabKeys[i], Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.All, CustomMinimumSize = new Vector2(0, 36) };
            _tabs[i].AddThemeFontOverride("font", Fonts.Text); _tabs[i].AddThemeFontSizeOverride("font_size", 16);
            _tabs[i].Pressed += () => { _tab = k; Rebuild(); };
            MenuUi.FocusRing(_tabs[i]);
            tabs.AddChild(_tabs[i]);
        }
        cols.AddChild(tabs);
        var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _body.SizeFlagsHorizontal = SizeFlags.ExpandFill; _body.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(_body);
        cols.AddChild(scroll);
        var foot = new HBoxContainer(); foot.AddThemeConstantOverride("separation", 10);
        var reset = Ui.Btn("set.reset", ResetTab); reset.FocusMode = FocusModeEnum.All; MenuUi.FocusRing(reset);
        var cancel = Ui.Btn("set.cancel", Cancel); cancel.FocusMode = FocusModeEnum.All; MenuUi.FocusRing(cancel);
        var apply = MenuUi.Primary("set.apply", Apply); MenuUi.FocusRing(apply);
        foot.AddChild(reset); foot.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill }); foot.AddChild(cancel); foot.AddChild(apply);
        root.AddChild(foot);

        _dialog.Visible = false;
        _dialog.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Sheet, Ui.Ink, 2, 18, Ui.R3));
        _dialog.AnchorLeft = _dialog.AnchorRight = _dialog.AnchorTop = _dialog.AnchorBottom = 0.5f;
        _dialog.GrowHorizontal = GrowDirection.Both; _dialog.GrowVertical = GrowDirection.Both;
        var dv = new VBoxContainer(); dv.AddThemeConstantOverride("separation", 12);
        dv.AddChild(_dialogText);
        var dr = new HBoxContainer(); dr.AddThemeConstantOverride("separation", 10);
        var revert = Ui.Btn("set.revert", Revert); revert.FocusMode = FocusModeEnum.All; MenuUi.FocusRing(revert);
        var keep = MenuUi.Primary("set.keep", Keep); MenuUi.FocusRing(keep);
        dr.AddChild(revert); dr.AddChild(keep); dv.AddChild(dr);
        _dialog.AddChild(dv);
        AddChild(_dialog);

        Loc.Changed += OnLang; TreeExiting += () => Loc.Changed -= OnLang;
        Resized += () => _fit(Size);
    }

    void OnLang() { if (Visible) Rebuild(); }

    // Screenshot hooks: show a tab or the rollback dialog without applying anything.
    public void Close() { Visible = false; _dialog.Visible = false; }
    public void ShotTab(int tab) { _tab = tab; Rebuild(); }
    public void ShotRollback() { _before = Settings.Current.Clone(); _rb.Start(); _dialog.Visible = true; UpdateDialog(); }

    public void Open()
    {
        _orig = Settings.Current.Clone(); _w = Settings.Current.Clone(); _tab = 0;
        Visible = true; _dialog.Visible = false; _fit(Size);
        Rebuild();
        _tabs[0].CallDeferred(Control.MethodName.GrabFocus);
    }

    // ---------- structure ----------
    void Rebuild()
    {
        foreach (var c in _body.GetChildren()) { _body.RemoveChild(c); c.QueueFree(); }
        for (int i = 0; i < 3; i++) StyleTab(_tabs[i], TabInk[i], i == _tab);
        _underline.Color = Ui.InkLine[TabInk[_tab]];
        _body.AddChild(Ui.Lbl(TabKeys[_tab], 18, Ui.InkText[TabInk[_tab]], FontKind.Draft));
        switch (_tab) { case 0: BuildVideo(); break; case 1: BuildSound(); break; default: BuildGeneral(); break; }
    }

    static void StyleTab(Button b, int ink, bool on)
    {
        StyleBoxFlat S(Color bg, bool sel)
        {
            var s = Ui.Box(bg, sel ? Ui.Ink : Colors.Transparent, sel ? 1 : 0, 12, Ui.R2);
            s.BorderWidthLeft = 3; s.BorderColor = sel ? Ui.Ink : Colors.Transparent; s.ContentMarginLeft = 14;
            return s;
        }
        // The selected tab: hover fill, an ink frame, and the 3 du edge in the domain colour (drawn by an extra bar so frame and edge differ).
        var normal = S(on ? Ui.Hover : Ui.Sheet, on); var hover = S(Ui.Hover, on);
        if (on) { normal.BorderWidthLeft = 5; hover.BorderWidthLeft = 5; }
        b.AddThemeStyleboxOverride("normal", normal); b.AddThemeStyleboxOverride("hover", hover); b.AddThemeStyleboxOverride("pressed", hover);
        foreach (var n in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) b.AddThemeColorOverride(n, Ui.Ink);
        foreach (var c in b.GetChildren()) { b.RemoveChild(c); c.QueueFree(); }
        var edge = new ColorRect { Color = Ui.InkLine[ink], MouseFilter = MouseFilterEnum.Ignore };
        edge.SetAnchorsPreset(LayoutPreset.LeftWide); edge.OffsetRight = on ? 3 : 0; edge.OffsetLeft = 0;
        b.AddChild(edge);
    }

    HBoxContainer Row(string key, Control control, string reason = null, bool mock = false)
    {
        var v = new HBoxContainer(); v.AddThemeConstantOverride("separation", 12);
        var l = Ui.Lbl(key, 14, Ui.Ink2); l.CustomMinimumSize = new Vector2(190, 0); l.VerticalAlignment = VerticalAlignment.Center;
        v.AddChild(l); v.AddChild(control);
        if (mock) v.AddChild(Ui.Mock());
        _body.AddChild(v);
        if (reason != null) _body.AddChild(Ui.Wrap(reason, 14, Ui.Ink3));
        return v;
    }

    Segment Seg(string[] items, int sel, Action<int> set, bool translate = true)
    {
        var s = new Segment(items, sel, 14, translate, true);
        s.Changed += i => { set(i); };
        return s;
    }

    OptionButton Option(string[] items, int sel, Action<int> set, int disabled = -1)
    {
        var o = new OptionButton { FocusMode = FocusModeEnum.All };
        foreach (var it in items) o.AddItem(Loc.Has(it) ? Loc.T(it) : it);
        if (disabled >= 0) o.SetItemDisabled(disabled, true);
        o.Selected = sel;
        MenuUi.StyleOption(o);
        o.ItemSelected += i => set((int)i);
        return o;
    }

    static readonly string[] OnOff = { "set.on", "set.off" };

    // ---------- tabs ----------
    void BuildVideo()
    {
        var res = new string[SettingsData.Resolutions.Length];
        for (int i = 0; i < res.Length; i++) res[i] = $"{SettingsData.Resolutions[i].W}×{SettingsData.Resolutions[i].H}";
        Row("set.resolution", Option(res, _w.Resolution, i => _w.Resolution = i));
        Row("set.winmode", Seg(new[] { "set.win.window", "set.win.borderless", "set.win.full" }, _w.WindowMode, i => _w.WindowMode = i));
        Row("set.quality", Seg(new[] { "set.q.low", "set.q.mid", "set.q.high", "set.q.custom" }, _w.Quality, i => _w.Quality = i));
        Segment mode = null;
        Row("set.upscale", Option(new[] { "set.up.off", "FSR 1.0", "FSR 2.2", "DLSS" }, _w.Upscaler, i => { _w.Upscaler = i; if (mode != null) SetModeEnabled(mode); }, 3), "set.dlss.reason");
        mode = Seg(new[] { "set.um.quality", "set.um.balance", "set.um.perf" }, _w.UpscaleMode, i => _w.UpscaleMode = i);
        Row("set.upmode", mode); SetModeEnabled(mode);
        Row("set.vsync", Seg(OnOff, _w.Vsync ? 0 : 1, i => _w.Vsync = i == 0));
        Row("set.fps", Option(new[] { "30", "60", "120", "set.nolimit" }, _w.FpsLimit, i => _w.FpsLimit = i));
        var sl = new HSlider { MinValue = SettingsLogic.UiScaleMin, MaxValue = SettingsLogic.UiScaleMax, Step = SettingsLogic.UiScaleStep, Value = _w.UiScale, CustomMinimumSize = new Vector2(200, 24), FocusMode = FocusModeEnum.All };
        ActorPanel.StyleSlider(sl);
        var val = Ui.Num(Loc.N(_w.UiScale, _w.UiScale % 1 == 0 ? 0 : 1) + "%", 14, null, 64);
        sl.ValueChanged += v => { _w.UiScale = SettingsLogic.SnapUiScale(v); val.Text = Loc.N(_w.UiScale, _w.UiScale % 1 == 0 ? 0 : 1) + "%"; };
        var box = new HBoxContainer(); box.AddThemeConstantOverride("separation", 8); box.AddChild(sl); box.AddChild(val);
        Row("set.uiscale", box);
        _body.AddChild(Ui.Wrap("set.video.note", 14, Ui.Ink3));
    }

    void SetModeEnabled(Segment s) => s.Enabled = _w.Upscaler != 0;

    void BuildSound()
    {
        for (int i = 0; i < SettingsData.BusNames.Length; i++)
        {
            int k = i; bool alert = i == 5;
            HSlider sl = alert ? new AlertSlider() : new HSlider();
            sl.MinValue = 0; sl.MaxValue = 100; sl.Step = 1; sl.Value = _w.Volume[i]; sl.CustomMinimumSize = new Vector2(220, 24); sl.FocusMode = FocusModeEnum.All;
            ActorPanel.StyleSlider(sl);
            var val = Ui.Num(((int)_w.Volume[i]) + "%", 14, null, 52);
            sl.ValueChanged += v =>
            {
                if (alert && v < AudioSettings.AlertFloor * 100) { sl.SetValueNoSignal(AudioSettings.AlertFloor * 100); v = AudioSettings.AlertFloor * 100; }
                _w.Volume[k] = v; val.Text = (int)v + "%"; _soundFocus = k;
            };
            var box = new HBoxContainer(); box.AddThemeConstantOverride("separation", 8); box.AddChild(sl); box.AddChild(val);
            Row("set.bus." + i, box, null, i == 4);
        }
        _body.AddChild(Ui.Wrap("set.alertnote", 14, Ui.Ink3));
        Row("set.bg", Seg(OnOff, _w.SoundInBackground ? 0 : 1, i => _w.SoundInBackground = i == 0));
        var test = Ui.Btn("set.test", () => TestBus?.Invoke(_soundFocus)); test.FocusMode = FocusModeEnum.All; MenuUi.FocusRing(test);
        if (TestBus == null) { test.Disabled = true; test.TooltipText = "menu.reason.nogame"; }
        _body.AddChild(test);
    }

    void BuildGeneral()
    {
        Row("set.lang", Seg(new[] { "Русский", "English" }, Math.Max(0, Array.IndexOf(Loc.Languages, _w.Lang)), i => { _w.Lang = Loc.Languages[i]; Loc.SetLang(_w.Lang); }, false));
        Row("set.datefmt", Seg(new[] { "set.df.lang", "set.df.iso" }, _w.DateFormat, i => _w.DateFormat = i));
        Row("set.pausealarm", Seg(OnOff, _w.PauseOnAlarm ? 0 : 1, i => _w.PauseOnAlarm = i == 0), null, true);
        Row("set.tipdelay", Seg(SettingsData.TipDelays.Select(d => Loc.N(d, d < 1 ? 1 : 0) + " " + Loc.T("set.sec")).ToArray(), _w.TipDelay, i => _w.TipDelay = i, false));
        Row("set.anim", Seg(new[] { "set.on", "set.reduce" }, _w.Animations ? 0 : 1, i => _w.Animations = i == 0), null, true);
        _body.AddChild(Ui.Wrap("set.lang.note", 14, Ui.Ink3));
    }

    // ---------- actions ----------
    void ResetTab()
    {
        switch (_tab) { case 0: _w.ResetVideo(); break; case 1: _w.ResetSound(); break; default: _w.ResetGeneral(); break; }
        Rebuild();
    }

    void Cancel()
    {
        if (Loc.Lang != _orig.Lang) Loc.SetLang(_orig.Lang);
        Visible = false; Closed?.Invoke();
    }

    void Apply()
    {
        var old = Settings.Current;
        bool confirm = _w.NeedsConfirm(old) && DisplayServer.GetName() != "headless";
        _before = old.Clone();
        Settings.Current = _w.Clone();
        Settings.Apply(Settings.Current, GetWindow());
        Applied?.Invoke(Settings.Current);
        if (confirm) { _rb.Start(); _dialog.Visible = true; UpdateDialog(); }
        else { Settings.Save(); Visible = false; Closed?.Invoke(); }
    }

    void Keep() { _rb.Confirm(); _dialog.Visible = false; Settings.Save(); Visible = false; Closed?.Invoke(); }

    public void Revert()
    {
        _rb.Confirm(); _dialog.Visible = false;
        Settings.Current = _before.Clone(); _w = _before.Clone();
        Settings.Apply(Settings.Current, GetWindow());
        Applied?.Invoke(Settings.Current);
        if (Loc.Lang != _w.Lang) Loc.SetLang(_w.Lang);
        Rebuild();
    }

    void UpdateDialog() => _dialogText.Text = Loc.T("set.rollback", (int)Math.Ceiling(_rb.Remaining));

    public override void _Process(double delta)
    {
        if (!_dialog.Visible) return;
        if (_rb.Tick(delta)) Revert(); else UpdateDialog();
    }

    public override void _Input(InputEvent e)
    {
        if (!Visible || e is not InputEventKey { Pressed: true, Keycode: Key.Escape }) return;
        GetViewport().SetInputAsHandled();
        if (_dialog.Visible) Revert(); else Cancel();
    }
}
