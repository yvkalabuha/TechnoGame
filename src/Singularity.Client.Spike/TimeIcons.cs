using System;
using Godot;

namespace Singularity.Client.Spike;

/// <summary>Time group (UI_DESIGN_SYSTEM §17.8, spec §5): pause, slower, faster as 28x28 line icons on a 20-unit grid, and a tempo meter of three ticks of
/// growing height (filled: 0 pause, 1 day/s, 2 week/s, 3 month/s) with the tempo word beside it. State reads without colour: fill and the number of ticks.</summary>
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

    sealed partial class Meter : Control
    {
        public int Filled;
        static readonly float[] Heights = { 5, 9, 14 };
        public Meter() { CustomMinimumSize = new Vector2(22, 28); MouseFilter = MouseFilterEnum.Ignore; }
        public override void _Draw()
        {
            for (int i = 0; i < 3; i++)
            {
                var r = new Rect2(2 + i * 6, Size.Y - 7 - Heights[i], 4, Heights[i]);
                if (i < Filled) DrawRect(r, Ui.Ink);
                DrawRect(r, Ui.Ink, false, 1f);
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
        _pause.QueueRedraw(); _slower.QueueRedraw(); _faster.QueueRedraw(); _meter.QueueRedraw();
    }
}
