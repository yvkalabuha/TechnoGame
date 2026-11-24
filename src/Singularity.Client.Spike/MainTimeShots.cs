using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace Singularity.Client.Spike;

// TASK-0139: frames and run-time checks of the time controls on a real session. --time-shots (with --shots --shot-size 1920x1080 --shot-dir DIR) plays the speeds and the pause at 100 % and 150 %,
// measures the optical centre of each button glyph and the frame of the active gauge step on the drawn pixels, and drives the buttons by keyboard and mouse against the live session speed.
public sealed partial class Main
{
    readonly List<Func<double, bool>> _timeQ = new(); int _timeI;

    static float Lum(Color c) => 0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B;
    static float Lin(float v) => v <= 0.04045f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);
    static float RelLum(Color c) => 0.2126f * Lin(c.R) + 0.7152f * Lin(c.G) + 0.0722f * Lin(c.B);

    void TimeCheck(bool ok, string text) => LogCheck($"time: {text}: {(ok ? "PASS" : "FAIL")}\n");

    // Optical centre of the ink of each button (mass weighted by the difference to the button fill), in du from the button corner; the border band of 4 du is left out.
    void TimeCentres(string tag)
    {
        var img = GetViewport().GetTexture().GetImage(); var t = _signal.Clock;
        foreach (var k in new[] { TimeControl.Kind.Pause, TimeControl.Kind.Slower, TimeControl.Kind.Faster })
        {
            var r = t.ButtonRect(k); int ox = (int)MathF.Round(r.Position.X), oy = (int)MathF.Round(r.Position.Y);
            TimeCheck(r.Position == new Vector2(ox, oy) && r.Size == new Vector2(32, 32), $"{tag} {k} button on whole pixels, 32 x 32 (at {r.Position}, {r.Size})");
            float bl = Lum(img.GetPixel(ox + 4, oy + 4)); double sw = 0, sx = 0, sy = 0;
            for (int y = 4; y < 28; y++)
                for (int x = 4; x < 28; x++) { double w = Math.Abs(Lum(img.GetPixel(ox + x, oy + y)) - bl); sw += w; sx += w * (x + 0.5); sy += w * (y + 0.5); }
            double cx = sx / sw, cy = sy / sw;
            TimeCheck(sw > 20 && Math.Abs(cx - 16) <= 0.5 && Math.Abs(cy - 16) <= 0.5, $"{tag} {k} glyph centre {cx:F2}, {cy:F2} (must be 16 +-0.5)");
        }
        var m = t.MeterRect; var d = _signal.DateRect; var pr = t.ButtonRect(TimeControl.Kind.Pause);
        TimeCheck(Math.Abs(pr.GetCenter().Y - m.GetCenter().Y) <= 0.5 && Math.Abs(pr.GetCenter().Y - d.GetCenter().Y) <= 0.5, $"{tag} one axis: buttons {pr.GetCenter().Y}, gauge {m.GetCenter().Y}, date {d.GetCenter().Y}");
    }

    // The frame of the active step is the bar grown by 3 du: the dark pixels around each bar must span exactly the expected rectangle, so the frame is symmetric and touches no neighbour.
    void TimeGauge(string tag)
    {
        var img = GetViewport().GetTexture().GetImage(); var m = _signal.Clock.MeterRect; int mx = (int)MathF.Round(m.Position.X), my = (int)MathF.Round(m.Position.Y);
        TimeCheck(m.Position == new Vector2(mx, my) && m.Size == new Vector2(60, 32), $"{tag} gauge on whole pixels, 60 x 32 (at {m.Position}, {m.Size})");
        float mid = (Lum(Ui.Ink) + Lum(img.GetPixel(mx + 1, my + 1))) / 2; int active = _signal.Clock.Selected - 1;
        for (int i = 0; i < 3; i++)
        {
            var e = i == active ? TimeGaugeGeometry.Frame(i) : TimeGaugeGeometry.Bar(i); var b = TimeGaugeGeometry.Bar(i);
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            for (int y = Math.Max(0, b.Y - 6); y < Math.Min(32, b.Y + b.H + 6); y++)
                for (int x = Math.Max(0, b.X - 4); x < Math.Min(60, b.X + b.W + 4); x++)
                    if (Lum(img.GetPixel(mx + x, my + y)) < mid) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
            bool ok = x0 == e.X && y0 == e.Y && x1 == e.X + e.W - 1 && y1 == e.Y + e.H - 1;
            double ccx = (x0 + x1 + 1) / 2.0 - (b.X + b.W / 2.0), ccy = (y0 + y1 + 1) / 2.0 - (b.Y + b.H / 2.0);
            TimeCheck(ok && Math.Abs(ccx) <= 0.5 && Math.Abs(ccy) <= 0.5, $"{tag} step {i + 1} {(i == active ? "frame" : "bar, no frame")} ink box {x0},{y0}..{x1},{y1} expected {e.X},{e.Y}..{e.X + e.W - 1},{e.Y + e.H - 1}, offset from the bar centre {ccx:F1}, {ccy:F1}");
        }
    }

    void TimeFocusRing(TimeControl.Kind k)
    {
        var img = GetViewport().GetTexture().GetImage(); var r = _signal.Clock.ButtonRect(k); int ox = (int)MathF.Round(r.Position.X), oy = (int)MathF.Round(r.Position.Y) + 16;
        float mid = (Lum(Ui.Ink) + Lum(img.GetPixel(ox - 8, oy))) / 2;
        bool ring = Lum(img.GetPixel(ox - 3, oy)) < mid && Lum(img.GetPixel(ox - 2, oy)) < mid && Lum(img.GetPixel(ox - 1, oy)) >= mid && Lum(img.GetPixel(ox + 33, oy)) < mid && Lum(img.GetPixel(ox + 34, oy)) < mid;
        float a = RelLum(Ui.Ink) + 0.05f, b = RelLum(img.GetPixel(ox - 8, oy)) + 0.05f; double ratio = Math.Max(a, b) / Math.Min(a, b);
        TimeCheck(ring && ratio >= 3, $"focus ring of {k}: 2 du ink ring 1 du outside, contrast to the bar {ratio:F1}:1");
    }

    void BuildTimeShots()
    {
        void W(double sec, Action a) { double t = 0; _timeQ.Add(dt => { t += dt; if (t < sec) return false; a(); return true; }); }
        string dir = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), _shotDir));
        Directory.CreateDirectory(dir);
        var names = new[] { "pause", "day", "week", "month" };
        void Frames(string name, string sfx, int w, int h) { SaveShot($"time_{name}{sfx}.png", null, true); SaveShot($"time_{name}_bar{sfx}.png", new Rect2I(0, 0, w, h)); }
        W(1.0, () => _signal.SetSpeed(1));
        W(0.6, () => { });
        foreach (int s in new[] { 1, 2, 3, 0 })
        {
            W(0.1, () => _signal.SetSpeed(s));
            W(0.5, () => { string tag = $"100% {names[s]}"; TimeCentres(tag); TimeGauge(tag); Frames(names[s], "_100", 640, 64); });
        }
        // Pressed state of each button by real mouse events (released outside, nothing acts), at speed 2 where all three are enabled.
        W(0.1, () => _signal.SetSpeed(2));
        foreach (var k in new[] { TimeControl.Kind.Pause, TimeControl.Kind.Slower, TimeControl.Kind.Faster })
        {
            Vector2 c = default;
            W(0.3, () => { c = _signal.Clock.ButtonRect(k).GetCenter(); Mouse(c, null); });
            W(0.3, () => Mouse(c, true));
            W(0.3, () => { TimeCentres($"100% pressed {k}"); if (k == TimeControl.Kind.Pause) SaveShot("time_pressed_100.png", new Rect2I(0, 0, 640, 64)); Mouse(c + new Vector2(0, 600), null); Mouse(c + new Vector2(0, 600), false); });
        }
        W(0.3, () => TimeCheck(_host.Speed == 2 && _signal.Clock.Selected == 2, $"pressed-and-released-outside changed nothing (session speed {_host.Speed})"));
        // Keyboard: Tab-order focus on «faster», Enter raises the live session speed; Space pauses even with a button focused.
        W(0.1, () => { _signal.Clock.FocusButton(TimeControl.Kind.Faster); });
        W(0.4, () => { TimeFocusRing(TimeControl.Kind.Faster); Frames("focus", "_100", 640, 64); PressKey(Key.Enter); PressKey(Key.Enter, false); });
        W(0.5, () => TimeCheck(_host.Speed == 3 && _signal.Clock.Selected == 3 && _host.Snap.Speed == 3, $"Enter on the focused faster button: session speed {_host.Speed}, snapshot {_host.Snap.Speed} (expected 3)"));
        W(0.1, () => { PressKey(Key.Space); PressKey(Key.Space, false); });
        W(0.4, () => TimeCheck(_host.Speed == 0 && _signal.Clock.HasButtonFocus(TimeControl.Kind.Faster) == false || _host.Speed == 0, $"Space with a button focused: session speed {_host.Speed} (expected 0)"));
        W(0.1, () => { _signal.Clock.FocusButton(TimeControl.Kind.Slower); PressKey(Key.Enter); PressKey(Key.Enter, false); });
        W(0.4, () => TimeCheck(_signal.Clock.Selected == 0 && _host.Speed == 0, "Enter on slower while paused only changes the resume tempo (stays paused)"));
        W(0.1, () => _signal.SetSpeed(2));
        // 150 %.
        W(0.1, () => { Settings.Current.UiScale = 150; Settings.Apply(Settings.Current, GetWindow(), false); });
        foreach (int s in new[] { 1, 2, 3, 0 })
        {
            W(0.6, () => _signal.SetSpeed(s));
            W(0.5, () => Frames(names[s], "_150", 960, 96));
        }
        W(0.1, () => { Settings.Current.UiScale = 100; Settings.Apply(Settings.Current, GetWindow(), false); });
        W(0.6, () =>
        {
            _shotLog += $"time shot run result: {(_shotFailed ? "FAILED" : "OK")}\n";
            try { File.WriteAllText(Path.Combine(dir, "run-log.txt"), _shotLog); } catch (Exception ex) { GD.PrintErr("cannot write run-log.txt: " + ex.Message); _shotFailed = true; }
            GD.Print(_shotLog); GetTree().Quit(_shotFailed ? 1 : 0);
        });
    }

    void TimeShotStep(double dt)
    {
        if (_timeQ.Count == 0) BuildTimeShots();
        if (_timeI < _timeQ.Count && _timeQ[_timeI](dt)) _timeI++;
    }
}
