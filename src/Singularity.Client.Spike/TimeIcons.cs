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
            if (HasFocus())
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
    int _speed, _resume = 1;
    public event Action<int> Changed;
    public int Selected => _speed;

    /// <summary>Screen rectangle of a button and of the gauge (shot-run checks of centring).</summary>
    public Rect2 ButtonRect(Kind k) => (k == Kind.Pause ? _pause : k == Kind.Slower ? _slower : _faster).GetGlobalRect();
    public Rect2 MeterRect => _meter.GetGlobalRect();
    public void FocusButton(Kind k) => (k == Kind.Pause ? _pause : k == Kind.Slower ? _slower : _faster).GrabFocus();
    public bool HasButtonFocus(Kind k) => (k == Kind.Pause ? _pause : k == Kind.Slower ? _slower : _faster).HasFocus();

    public TimeControl()
    {
        AddThemeConstantOverride("separation", 8);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _slower.TooltipText = "tip.time.slower"; _faster.TooltipText = "tip.time.faster";
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
        if ((_slower.Disabled && _slower.HasFocus()) || (_faster.Disabled && _faster.HasFocus())) _pause.GrabFocus();
        _meter.Filled = _speed;
        string word = Loc.T(Words[_speed]);   // the real rate with its unit ("неделя/с") for the tooltip and the screen reader, never the step index (C3, C7; #443: no text beside the gauge)
        _meter.TooltipText = Loc.T("time.gauge.tip", word); _meter.AccessibilityName = Loc.T("time.gauge.name", word);
        _pause.TooltipText = _speed == 0 ? "tip.time.pause.on" : "tip.time.pause.off";
        _pause.QueueRedraw(); _slower.QueueRedraw(); _faster.QueueRedraw(); _meter.QueueRedraw();
    }
}
