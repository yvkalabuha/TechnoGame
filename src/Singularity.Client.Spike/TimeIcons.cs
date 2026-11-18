using System;
using Godot;

namespace Singularity.Client.Spike;

/// <summary>Time group (UI_DESIGN_SYSTEM §17.8, spec §5): pause, slower, faster as 28x28 line icons on a 20-unit grid, and a coloured four-cell tempo gauge (filled: 0 pause, 1 day/s, 2 week/s, 3 month/s) with the tempo word beside it. State reads without colour: fill and the number of ticks.</summary>
public sealed partial class TimeControl : HBoxContainer
{
    public enum Kind { Pause, Slower, Faster }

    sealed partial class IconButton : Button
    {
        public Kind K; public bool On;
        public IconButton(Kind k)
        {
            K = k; CustomMinimumSize = new Vector2(28, 28); FocusMode = FocusModeEnum.None; Text = ""; ClipContents = true;
            var normal = Ui.Box(Ui.Sheet, Ui.Ink, 1, 0, Ui.R2);
            AddThemeStyleboxOverride("normal", normal);
            AddThemeStyleboxOverride("hover", Ui.Box(Ui.Hover, Ui.Ink, 1, 0, Ui.R2));
            AddThemeStyleboxOverride("pressed", Ui.Box(Ui.Hover, Ui.Ink, 2, 0, Ui.R2));
            AddThemeStyleboxOverride("disabled", Ui.Box(Ui.Paper, Ui.Rule, 1, 0, Ui.R2));
        }

        public override void _Draw()
        {
            // Pause pressed in = filled ink with a sheet sign; everything else is a line sign (1.5 stroke, miter, butt, no fill).
            bool filled = K == Kind.Pause && On;
            if (filled) DrawStyleBox(Ui.Box(Ui.Ink, Ui.Ink, 1, 0, Ui.R2), new Rect2(Vector2.Zero, Size));
            var c = Disabled ? Ui.Ink3 : filled ? Ui.Sheet : Ui.Ink;
            var o = (Size - new Vector2(20, 20)) * 0.5f;
            Vector2 P(float x, float y) => o + new Vector2(x, y);
            switch (K)
            {
                case Kind.Pause:
                    DrawPolyline(new[] { P(6, 4), P(9, 4), P(9, 16), P(6, 16), P(6, 4) }, c, 1.5f);
                    DrawPolyline(new[] { P(11, 4), P(14, 4), P(14, 16), P(11, 16), P(11, 4) }, c, 1.5f);
                    break;
                case Kind.Slower:
                    DrawPolyline(new[] { P(10, 4), P(3, 10), P(10, 16), P(10, 4) }, c, 1.5f);
                    DrawPolyline(new[] { P(17, 4), P(10, 10), P(17, 16), P(17, 4) }, c, 1.5f);
                    break;
                default:
                    DrawPolyline(new[] { P(3, 4), P(10, 10), P(3, 16), P(3, 4) }, c, 1.5f);
                    DrawPolyline(new[] { P(10, 4), P(17, 10), P(10, 16), P(10, 4) }, c, 1.5f);
                    break;
            }
            if (Disabled)   // unavailable: hatching at 45 degrees (not colour alone)
                for (float d = -Size.Y; d < Size.X; d += 6) DrawLine(new Vector2(d, Size.Y - 2), new Vector2(d + Size.Y - 4, 2), new Color(Ui.Ink3, 0.35f), 1f);
        }
    }

    // Speed gauge (CLIENT_V05C_SPEC §2): four cells 8 wide, 7/11/15/19 high, 2 apart. Cells 1..N are filled in their own ink (teal, olive, plum), the current one has an outer ring,
    // empty cells are hatched at 45 degrees. The fourth (reserve, sand, #331) is dashed and inactive while the simulation has three tempos.
    // Read without colour: number of filled cells, height, hatching. Decor ink: no ■ ▲ ●, no blinking.
    static readonly int[] GaugeInk = { Ui.Teal, Ui.Olive, Ui.Plum, Ui.Sand };

    sealed partial class Meter : Control
    {
        public int Filled;
        static readonly float[] Heights = { 7, 11, 15, 19 };
        public Meter() { CustomMinimumSize = new Vector2(46, 28); MouseFilter = MouseFilterEnum.Ignore; }

        static void Hatch(CanvasItem c, Rect2 r, Color col)
        {
            for (float a = r.Position.X - r.Size.Y; a < r.End.X; a += 3f)
            {
                float s0 = Math.Max(0, r.Position.X - a), s1 = Math.Min(r.Size.Y, r.End.X - a);
                if (s0 < s1) c.DrawLine(new Vector2(a + s0, r.End.Y - s0), new Vector2(a + s1, r.End.Y - s1), col, 1f);
            }
        }

        public override void _Draw()
        {
            for (int i = 0; i < 4; i++)
            {
                var r = new Rect2(4 + i * 10, Size.Y - 4 - Heights[i], 8, Heights[i]);
                if (i == 3)
                {
                    var c = Ui.Ink3;
                    DrawDashedLine(r.Position, new Vector2(r.End.X, r.Position.Y), c, 1f, 2f); DrawDashedLine(new Vector2(r.End.X, r.Position.Y), r.End, c, 1f, 2f);
                    DrawDashedLine(r.End, new Vector2(r.Position.X, r.End.Y), c, 1f, 2f); DrawDashedLine(new Vector2(r.Position.X, r.End.Y), r.Position, c, 1f, 2f);
                    continue;
                }
                bool on = i < Filled;
                if (on) DrawRect(r, Ui.InkLine[GaugeInk[i]]); else Hatch(this, r, new Color(Ui.Ink, 0.45f));
                DrawRect(r, Ui.Ink, false, 1f);
                if (i == Filled - 1) DrawRect(r.Grow(2), Ui.Ink, false, 1.5f);
            }
        }
    }

    readonly IconButton _pause = new(Kind.Pause), _slower = new(Kind.Slower), _faster = new(Kind.Faster);
    readonly Meter _meter = new();
    readonly Label _word = Ui.Lbl("speed.pause", 14, null, FontKind.Draft);
    int _speed, _resume = 1;
    public event Action<int> Changed;
    public int Selected => _speed;

    public TimeControl()
    {
        AddThemeConstantOverride("separation", 0);
        _pause.TooltipText = "tip.time.pause"; _slower.TooltipText = "tip.time.slower"; _faster.TooltipText = "tip.time.faster";
        _pause.Pressed += () => Select(_speed > 0 ? 0 : _resume, true);
        _slower.Pressed += () => Step(-1);
        _faster.Pressed += () => Step(1);
        AddChild(_pause); AddChild(_slower); AddChild(_faster);
        var gap = new Control { CustomMinimumSize = new Vector2(10, 0) };
        AddChild(gap); AddChild(_meter);
        _word.CustomMinimumSize = new Vector2(96, 0);
        _word.VerticalAlignment = VerticalAlignment.Center;
        AddChild(_word);
        Select(0, false);
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
        _meter.Filled = _speed;
        _word.Text = Words[_speed];
        _word.AddThemeColorOverride("font_color", _speed == 0 ? Ui.Ink2 : Ui.InkText[GaugeInk[_speed - 1]]);
        _pause.QueueRedraw(); _slower.QueueRedraw(); _faster.QueueRedraw(); _meter.QueueRedraw();
    }
}
