using System;
using Godot;

namespace Singularity.Client.Spike;

/// <summary>Time group (UI_DESIGN_SYSTEM §17.8, TASK-0139): pause, slower, faster as 32 du line icons (4 du apart) and the four-step speed gauge. No speed text (owner #443): the value is the number of filled steps;
/// its words with the unit live in the gauge tooltip and the accessible name. State reads without colour: fill, bar height and the frame of the active step.</summary>
public sealed partial class TimeControl : HBoxContainer
{
    public enum Kind { Pause, Slower, Faster }

    sealed partial class IconButton : Button
    {
        public Kind K; public bool On;
        // Whole-pixel parts of the glyph: axis-aligned edges are fills (an antialiased 2 du line would feather into the 2 du gaps), slanted edges are antialiased paths.
        readonly Rect2[] _rects; readonly Vector2[][] _paths;
        readonly StyleBoxFlat _filled = Ui.Box(Ui.Ink, Ui.Ink, 1, 0, Ui.R2);
        bool _mouseFocus;   // the focus came from a click: no ring, no key tip (A7 is for keyboard focus)
        public event Action<IconButton> KeyFocused; public event Action FocusLost;
        public IconButton(Kind k)
        {
            K = k; BuildParts((int)k, out _rects, out _paths);
            CustomMinimumSize = new Vector2(TimeGlyphMetrics.Button, TimeGlyphMetrics.Button); SizeFlagsVertical = SizeFlags.ShrinkCenter;
            FocusMode = FocusModeEnum.All; Text = ""; ClipContents = false;   // the focus ring is drawn outside the button
            AddThemeStyleboxOverride("normal", Ui.Box(Ui.Sheet, Ui.Ink, 1, 0, Ui.R2));
            AddThemeStyleboxOverride("hover", Ui.Box(Ui.Hover, Ui.Ink, 1, 0, Ui.R2));
            AddThemeStyleboxOverride("pressed", Ui.Box(Ui.Hover, Ui.Ink, 2, 0, Ui.R2));
            AddThemeStyleboxOverride("disabled", Ui.Box(Ui.Paper, Ui.Rule, 1, 0, Ui.R2));
            AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            FocusEntered += () => Callable.From(NotifyFocus).CallDeferred();   // deferred: the click handler below has set the flag by then
            FocusExited += () => { _mouseFocus = false; QueueRedraw(); FocusLost?.Invoke(); };
        }

        /// <summary>Keyboard-style focus (also the shot-run and the hand-over from a disabled button): a ring and a key tip even if a click had focused the button before.</summary>
        public void FocusByKey() { _mouseFocus = false; GrabFocus(); NotifyFocus(); }

        public bool RingShown => HasFocus() && !_mouseFocus;

        void NotifyFocus() { if (RingShown) KeyFocused?.Invoke(this); QueueRedraw(); }

        public override void _GuiInput(InputEvent e)
        {
            if (e is InputEventMouseButton { Pressed: true }) { _mouseFocus = true; FocusLost?.Invoke(); QueueRedraw(); }
            else if (e is InputEventKey) { _mouseFocus = false; QueueRedraw(); }
        }

        static void BuildParts(int kind, out Rect2[] rects, out Vector2[][] paths)
        {
            var rs = new System.Collections.Generic.List<Rect2>(); var ps = new System.Collections.Generic.List<Vector2[]>(); const float h = TimeGlyphMetrics.Stroke / 2f;
            void Edge((double X, double Y) a, (double X, double Y) b)
            {
                float x0 = (float)Math.Min(a.X, b.X), x1 = (float)Math.Max(a.X, b.X), y0 = (float)Math.Min(a.Y, b.Y), y1 = (float)Math.Max(a.Y, b.Y);
                rs.Add(a.X == b.X ? new Rect2(x0 - h, y0 - h, TimeGlyphMetrics.Stroke, y1 - y0 + 2 * h) : new Rect2(x0 - h, y0 - h, x1 - x0 + 2 * h, TimeGlyphMetrics.Stroke));
            }
            foreach (var poly in TimeGlyphMetrics.Shapes(kind))
            {
                bool slanted = poly[0].X != poly[1].X && poly[0].Y != poly[1].Y;
                if (!slanted) { for (int i = 0; i + 1 < poly.Length; i++) Edge(poly[i], poly[i + 1]); continue; }
                ps.Add(new[] { new Vector2((float)poly[0].X, (float)poly[0].Y), new Vector2((float)poly[1].X, (float)poly[1].Y), new Vector2((float)poly[2].X, (float)poly[2].Y) });   // base-top, tip, base-bottom
                Edge(poly[2], poly[3]);   // the vertical base
            }
            rects = rs.ToArray(); paths = ps.ToArray();
        }

        public override void _Draw()
        {
            // Pause pressed in = filled ink with a sheet sign; everything else is a line sign (2 du, miter, butt, no fill).
            bool filled = K == Kind.Pause && On;
            if (filled) DrawStyleBox(_filled, new Rect2(Vector2.Zero, Size));
            var c = Disabled ? Ui.Ink3 : filled ? Ui.Sheet : Ui.Ink;
            // Shapes in button coordinates: TimeGlyphMetrics (A2: ink >= 16 x 16 du, stroke >= 2 du)
            foreach (var r in _rects) DrawRect(r, c);
            foreach (var p in _paths) DrawPolyline(p, c, TimeGlyphMetrics.Stroke, true);
            if (RingShown)
            {
                // A6/A7: a 2 du ink ring 1 du outside the button; it lies on the paper of the bar, 3:1 and better.
                float w = Size.X, h = Size.Y; var ink = Ui.Ink;
                DrawRect(new Rect2(-3, -3, w + 6, 2), ink); DrawRect(new Rect2(-3, h + 1, w + 6, 2), ink);
                DrawRect(new Rect2(-3, -1, 2, h + 2), ink); DrawRect(new Rect2(w + 1, -1, 2, h + 2), ink);
            }
        }
    }

    readonly IconButton _pause = new(Kind.Pause), _slower = new(Kind.Slower), _faster = new(Kind.Faster);
    readonly GaugeControl _meter = new();
    readonly PanelContainer _keyTip = new(); readonly Label _keyTipText = Ui.Lbl("", 14);
    IconButton _tipFor;
    int _speed, _resume = 1;
    public event Action<int> Changed;
    public int Selected => _speed;

    /// <summary>Screen rectangle of a button and of the gauge (shot-run checks of centring).</summary>
    public Rect2 ButtonRect(Kind k) => (k == Kind.Pause ? _pause : k == Kind.Slower ? _slower : _faster).GetGlobalRect();
    public Rect2 MeterRect => _meter.GetGlobalRect();
    public void FocusButton(Kind k) => (k == Kind.Pause ? _pause : k == Kind.Slower ? _slower : _faster).FocusByKey();
    /// <summary>Rectangle of the key tip while it shows (keyboard focus), else an empty rectangle.</summary>
    public Rect2 KeyTipRect => _keyTip.Visible ? _keyTip.GetGlobalRect() : default;
    public bool RingShown(Kind k) => (k == Kind.Pause ? _pause : k == Kind.Slower ? _slower : _faster).RingShown;
    public bool HasButtonFocus(Kind k) => (k == Kind.Pause ? _pause : k == Kind.Slower ? _slower : _faster).HasFocus();

    public TimeControl()
    {
        AddThemeConstantOverride("separation", 8);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        // The mouse tooltip is Godot's; a keyboard-focused button has no pointer, so its tip is drawn here, next to the button (A7).
        _keyTip.TopLevel = true; _keyTip.Visible = false; _keyTip.MouseFilter = MouseFilterEnum.Ignore; _keyTip.ZIndex = 20;
        _keyTip.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Sheet, Ui.Ink, 1, 6));
        _keyTip.AddChild(_keyTipText); AddChild(_keyTip);
        foreach (var b in new[] { _pause, _slower, _faster }) { b.KeyFocused += ShowKeyTip; b.FocusLost += HideKeyTip; }
        _pause.Pressed += () => Select(_speed > 0 ? 0 : _resume, true);
        _slower.Pressed += () => Step(-1);
        _faster.Pressed += () => Step(1);
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", TimeGlyphMetrics.Gap);
        buttons.AddChild(_pause); buttons.AddChild(_slower); buttons.AddChild(_faster);
        AddChild(buttons); AddChild(_meter);
        Select(0, false);
        Loc.Changed += Refresh;
        TreeExiting += () => Loc.Changed -= Refresh;
    }

    void ShowKeyTip(IconButton b) { _tipFor = b; UpdateKeyTip(); }
    void HideKeyTip() { _tipFor = null; _keyTip.Visible = false; }

    void UpdateKeyTip()
    {
        if (_tipFor == null || !_tipFor.IsInsideTree()) return;
        _keyTipText.Text = _tipFor.TooltipText; _keyTip.ResetSize(); _keyTip.Visible = true;
        var r = _tipFor.GetGlobalRect(); _keyTip.GlobalPosition = new Vector2(r.Position.X, r.End.Y + 6);
    }

    /// <summary>Slower and faster move the tempo steps 1..3; while paused they only change the tempo that «continue» resumes at.</summary>
    public void Step(int d)
    {
        if (_speed == 0) { _resume = Math.Clamp(_resume + d, 1, 3); Refresh(); return; }
        Select(Math.Clamp(_speed + d, 1, 3), true);
    }

    public void TogglePause() => Select(_speed > 0 ? 0 : _resume, true);

    public void Select(int s, bool notify)
    {
        _speed = Math.Clamp(s, 0, 3);
        if (_speed > 0) _resume = _speed;
        Refresh();
        if (notify) Changed?.Invoke(_speed);
    }

    static readonly string[] Words = { "speed.pause", "speed.day", "speed.week", "speed.month" };

    void Refresh()
    {
        int level = _speed == 0 ? _resume : _speed;
        _pause.On = _speed == 0;
        _slower.Disabled = level <= 1; _faster.Disabled = level >= 3;
        // A disabled button cannot keep the keyboard focus: hand it to the pause button so that the keys never fall out of the group.
        if ((_slower.Disabled && _slower.HasFocus()) || (_faster.Disabled && _faster.HasFocus())) _pause.FocusByKey();
        _meter.Filled = _speed;
        string word = Loc.T(Words[_speed]);   // the real rate with its unit ("неделя/с") for the tooltip and the screen reader, never the step index (C3, C7; #443: no text beside the gauge)
        _meter.TooltipText = Loc.T("time.gauge.tip", word); _meter.AccessibilityName = Loc.T("time.gauge.name", word);
        // Tooltips name the remembered speed and say why a button is off (the key tip uses the same text).
        _pause.TooltipText = _speed == 0 ? Loc.T("tip.time.pause.on", Loc.T(Words[_resume])) : Loc.T("tip.time.pause.off");
        _slower.TooltipText = _slower.Disabled ? Loc.T("tip.time.slower.min", Loc.T(Words[1])) : Loc.T("tip.time.slower");
        _faster.TooltipText = _faster.Disabled ? Loc.T("tip.time.faster.max", Loc.T(Words[3])) : Loc.T("tip.time.faster");
        UpdateKeyTip();
        _pause.QueueRedraw(); _slower.QueueRedraw(); _faster.QueueRedraw(); _meter.QueueRedraw();
    }
}
