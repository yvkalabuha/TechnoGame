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
    // s = UI scale (1, 1.25, 1.5): the pixel box is the du box times s.
    void TimeCentres(string tag, float s = 1f)
    {
        var img = GetViewport().GetTexture().GetImage(); var t = _signal.Clock; const float tol = 0.5f;   // the same limit at every scale: glyphs are snapped to whole physical pixels (TimeSnap)
        foreach (var k in new[] { TimeControl.Kind.Pause, TimeControl.Kind.Slower, TimeControl.Kind.Faster })
        {
            var r = t.ButtonRect(k); int ox = (int)MathF.Round(r.Position.X * s), oy = (int)MathF.Round(r.Position.Y * s);
            if (s == 1f) TimeCheck(r.Position == new Vector2(ox, oy) && r.Size == new Vector2(32, 32), $"{tag} {k} button on whole pixels, 32 x 32 (at {r.Position}, {r.Size})");
            int lo = (int)MathF.Round(4 * s), hi = (int)MathF.Round(28 * s);
            float bl = Lum(img.GetPixel(ox + lo, oy + lo)); double sw = 0, sx = 0, sy = 0;
            for (int y = lo; y < hi; y++)
                for (int x = lo; x < hi; x++) { double w = Math.Abs(Lum(img.GetPixel(ox + x, oy + y)) - bl); sw += w; sx += w * (x + 0.5); sy += w * (y + 0.5); }
            double cx = (ox + sx / sw - r.Position.X * s) / s, cy = (oy + sy / sw - r.Position.Y * s) / s;   // from the true (fractional) button corner
            TimeCheck(sw > 20 && Math.Abs(cx - 16) <= tol && Math.Abs(cy - 16) <= tol, $"{tag} {k} glyph centre {cx:F2}, {cy:F2} du (must be 16 +-{tol:F2})");
        }
        var m = t.MeterRect; var d = _signal.DateRect; var pr = t.ButtonRect(TimeControl.Kind.Pause);
        TimeCheck(Math.Abs(pr.GetCenter().Y - m.GetCenter().Y) <= 0.5 && Math.Abs(pr.GetCenter().Y - d.GetCenter().Y) <= 0.5, $"{tag} one axis: buttons {pr.GetCenter().Y}, gauge {m.GetCenter().Y}, date {d.GetCenter().Y}");
    }

    // Dark runs (ink) along a pixel line of the frame image, as (first, last) pixels.
    static List<(int A, int B)> InkRuns(Image img, bool row, int fixedPos, int from, int to, float mid)
    {
        var runs = new List<(int, int)>(); int start = -1;
        for (int p = from; p <= to + 1; p++)
        {
            bool dark = p <= to && Lum(row ? img.GetPixel(p, fixedPos) : img.GetPixel(fixedPos, p)) < mid;
            if (dark && start < 0) start = p; else if (!dark && start >= 0) { runs.Add((start, p - 1)); start = -1; }
        }
        return runs;
    }

    // Gauge: each bar's rendered ink box matches the geometry within 0.5 du at every scale (pixel scan), and the active frame is symmetric around the RENDERED active bar: equal gaps and equal frame
    // strokes left/right and top/bottom, read from the ink runs on the lines through the bar centre.
    void TimeGauge(string tag, float s = 1f)
    {
        var img = GetViewport().GetTexture().GetImage(); var m = _signal.Clock.MeterRect; int mx = (int)MathF.Round(m.Position.X * s), my = (int)MathF.Round(m.Position.Y * s);
        if (s == 1f) TimeCheck(m.Position == new Vector2(mx, my) && m.Size == new Vector2(60, 32), $"{tag} gauge on whole pixels, 60 x 32 (at {m.Position}, {m.Size})");
        float mid = (Lum(Ui.Ink) + Lum(img.GetPixel(mx + 1, my + 1))) / 2; int active = _signal.Clock.Selected - 1; int W2 = (int)MathF.Round(60 * s), H2 = (int)MathF.Round(32 * s);
        for (int i = 0; i < 3; i++)
        {
            var e = i == active ? TimeGaugeGeometry.Frame(i) : TimeGaugeGeometry.Bar(i); var b = TimeGaugeGeometry.Bar(i);
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            for (int y = Math.Max(0, (int)MathF.Round((b.Y - 6) * s)); y < Math.Min(H2, (int)MathF.Round((b.Y + b.H + 6) * s)); y++)
                for (int x = Math.Max(0, (int)MathF.Round((b.X - 4) * s)); x < Math.Min(W2, (int)MathF.Round((b.X + b.W + 4) * s)); x++)
                    if (Lum(img.GetPixel(mx + x, my + y)) < mid) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
            // Edges in du from the true gauge corner, compared with the geometry within 0.5 du.
            double l = (mx + x0) / s - m.Position.X, rt = (mx + x1 + 1) / s - m.Position.X, tp = (my + y0) / s - m.Position.Y, bt = (my + y1 + 1) / s - m.Position.Y;
            // A bar must sit within 0.5 du of its geometry; the frame is the pixel bar grown by a whole number of pixels, so its outer edge may be up to 1 du from the nominal (the symmetry checks below are the strict ones).
            double lim = i == active ? 1.0 : 0.5;
            bool ok = x1 >= 0 && Math.Abs(l - e.X) <= lim && Math.Abs(tp - e.Y) <= lim && Math.Abs(rt - (e.X + e.W)) <= lim && Math.Abs(bt - (e.Y + e.H)) <= lim;
            TimeCheck(ok, $"{tag} step {i + 1} {(i == active ? "frame" : "bar, no frame")} ink edges {l:F2},{tp:F2}..{rt:F2},{bt:F2} du, expected {e.X},{e.Y}..{e.X + e.W},{e.Y + e.H} (+-{lim})");
            if (i != active) continue;
            // Symmetry around the rendered bar: runs on the horizontal and the vertical line through the bar centre.
            int cy = my + (int)MathF.Round((b.Y + b.H / 2f) * s), cx = mx + (int)MathF.Round((b.X + b.W / 2f) * s);
            var hr = InkRuns(img, true, cy, mx + (int)MathF.Round((b.X - 4) * s), mx + (int)MathF.Round((b.X + b.W + 4) * s) - 1, mid);
            var vr = InkRuns(img, false, cx, my + Math.Max(0, (int)MathF.Round((b.Y - 6) * s)), my + Math.Min(H2, (int)MathF.Round((b.Y + b.H + 6) * s)) - 1, mid);
            foreach (var (name, r) in new[] { ("horizontal", hr), ("vertical", vr) })
            {
                if (r.Count < 3) { TimeCheck(false, $"{tag} step {i + 1} {name} line through the bar has {r.Count} ink runs (frame, bar, frame expected at least 3)"); continue; }
                int gA = r[1].A - (r[0].B + 1), gB = r[^1].A - (r[^2].B + 1), tA = r[0].B - r[0].A + 1, tB = r[^1].B - r[^1].A + 1;
                double tolPx = 0.5 * s;
                TimeCheck(Math.Abs(gA - gB) <= tolPx && Math.Abs(tA - tB) <= tolPx && gA >= 1, $"{tag} step {i + 1} {name} symmetry around the rendered bar: gaps {gA}/{gB} px, frame strokes {tA}/{tB} px (differences must be <= {tolPx:F2} px = 0.5 du)");
            }
        }
    }

    void TimeFocusRing(TimeControl.Kind k, bool expect = true)
    {
        var img = GetViewport().GetTexture().GetImage(); var r = _signal.Clock.ButtonRect(k); int ox = (int)MathF.Round(r.Position.X), oy = (int)MathF.Round(r.Position.Y) + 16;
        float mid = (Lum(Ui.Ink) + Lum(img.GetPixel(ox - 8, oy))) / 2;
        bool ring = Lum(img.GetPixel(ox - 3, oy)) < mid && Lum(img.GetPixel(ox - 2, oy)) < mid && Lum(img.GetPixel(ox - 1, oy)) >= mid && Lum(img.GetPixel(ox + 33, oy)) < mid && Lum(img.GetPixel(ox + 34, oy)) < mid;
        float a = RelLum(Ui.Ink) + 0.05f, b = RelLum(img.GetPixel(ox - 8, oy)) + 0.05f; double ratio = Math.Max(a, b) / Math.Min(a, b);
        if (expect) TimeCheck(ring && ratio >= 3, $"focus ring of {k}: 2 du ink ring 1 du outside, contrast to the bar {ratio:F1}:1");
        else TimeCheck(!ring, $"no focus ring on {k} after a mouse click (focus kept: {_signal.Clock.HasButtonFocus(k)})");
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
        // The pointer leaves for the map in several moves, so that Godot drops the tooltip of the last hovered button (the synthetic jump left one at the old spot).
        for (int i = 1; i <= 6; i++) { int k = i; W(0.15, () => { var p = new Vector2(200 + k * 150, 100 + k * 100); Input.ParseInputEvent(new InputEventMouseMotion { Position = p, GlobalPosition = p, Relative = new Vector2(150, 100) }); }); }
        // Godot's own tooltip of the last hovered button stays where the synthetic pointer left it (no real pointer moves it away); a real pointer drops it on leaving, so the shot run hides it.
        W(0.3, () => { HideStrayTooltip(GetTree().Root); });
        W(0.1, () => _signal.Clock.FocusButton(TimeControl.Kind.Faster));   // the pointer is far away, outside the HUD
        W(1.2, () =>
        {
            TimeFocusRing(TimeControl.Kind.Faster); var b = _signal.Clock.ButtonRect(TimeControl.Kind.Faster); var tip = _signal.Clock.KeyTipRect;
            TimeCheck(tip.Size.X > 0 && _signal.Clock.KeyTipText.Contains(Loc.T("time.gauge.name", Loc.T("speed.week"))) && tip.Position.Y >= b.End.Y && tip.Position.Y <= b.End.Y + 12 && Math.Abs(tip.Position.X - b.Position.X) <= 8, $"key tip of the focused faster button sits under it and names the speed: tip {tip}, button {b}, text {_signal.Clock.KeyTipText.Replace('\n', '|')}");
            SaveShot("time_focus_100.png", null, true); SaveShot("time_focus_bar_100.png", new Rect2I(0, 0, 640, 128)); PressKey(Key.Enter); PressKey(Key.Enter, false);
        });
        W(0.5, () =>
        {
            TimeCheck(_host.Speed == 3 && _signal.Clock.Selected == 3 && _host.Snap.Speed == 3, $"Enter on the focused faster button: session speed {_host.Speed}, snapshot {_host.Snap.Speed} (expected 3)");
            TimeCheck(_signal.Clock.HasButtonFocus(TimeControl.Kind.Pause), "faster became disabled at the top speed: the focus moved to pause");
        });
        W(0.1, () => { PressKey(Key.Space); PressKey(Key.Space, false); });
        W(0.4, () => TimeCheck(_host.Speed == 0, $"Space with a button focused: session speed {_host.Speed} (expected 0)"));
        W(0.1, () => { _signal.Clock.FocusButton(TimeControl.Kind.Slower); PressKey(Key.Enter); PressKey(Key.Enter, false); });
        W(0.4, () => TimeCheck(_signal.Clock.Selected == 0 && _host.Speed == 0, "Enter on slower while paused only changes the resume tempo (stays paused)"));
        W(0.1, () => _signal.SetSpeed(2));
        // A click on «faster» that reaches the top speed disables it: the focus moves to pause quietly, with no ring and no key tip.
        W(0.1, () => _signal.SetSpeed(2));
        Vector2 fc = default;
        W(0.3, () => { fc = _signal.Clock.ButtonRect(TimeControl.Kind.Faster).GetCenter(); Mouse(fc, null); Mouse(fc, true); Mouse(fc, false); });
        W(0.5, () => TimeCheck(_host.Speed == 3 && _signal.Clock.HasButtonFocus(TimeControl.Kind.Pause) && !_signal.Clock.RingShown(TimeControl.Kind.Pause) && _signal.Clock.KeyTipRect.Size.X == 0, $"mouse click took faster to the limit: focus on pause without ring or key tip (speed {_host.Speed})"));
        W(0.1, () => _signal.SetSpeed(2));
        // Mouse to keyboard on a button that is already focused: ring and key tip (with the speed) come back.
        W(0.1, () => _signal.SetSpeed(1));
        Vector2 mk = default;
        W(0.3, () => { mk = _signal.Clock.ButtonRect(TimeControl.Kind.Faster).GetCenter(); Mouse(mk, null); Mouse(mk, true); Mouse(mk, false); });
        W(0.5, () => TimeCheck(_host.Speed == 2 && _signal.Clock.HasButtonFocus(TimeControl.Kind.Faster) && !_signal.Clock.RingShown(TimeControl.Kind.Faster) && _signal.Clock.KeyTipRect.Size.X == 0, $"after a click faster has focus, no ring, no key tip (speed {_host.Speed})"));
        W(0.1, () => { PressKey(Key.Shift); PressKey(Key.Shift, false); });
        W(0.5, () => { var tip = _signal.Clock.KeyTipRect; TimeCheck(_signal.Clock.RingShown(TimeControl.Kind.Faster) && tip.Size.X > 0 && _signal.Clock.KeyTipText.Contains(Loc.T("time.gauge.name", Loc.T("speed.week"))), $"mouse to keyboard on the focused faster button: ring {_signal.Clock.RingShown(TimeControl.Kind.Faster)}, key tip {tip.Size.X > 0}, text {_signal.Clock.KeyTipText.Replace('\n', '|')}"); Mouse(new Vector2(1100, 700), null); });
        W(0.1, () => _signal.SetSpeed(2));
        // A mouse click leaves the focus but no ring and no key tip; a second click resumes.
        Vector2 pc = default;
        W(0.3, () => { pc = _signal.Clock.ButtonRect(TimeControl.Kind.Pause).GetCenter(); Mouse(pc, null); Mouse(pc, true); Mouse(pc, false); });
        W(0.5, () => { TimeFocusRing(TimeControl.Kind.Pause, false); TimeCheck(_signal.Clock.KeyTipRect.Size.X == 0 && !_signal.Clock.RingShown(TimeControl.Kind.Pause) && _host.Speed == 0, $"click on pause: paused (speed {_host.Speed}), no key tip"); Mouse(pc, true); Mouse(pc, false); });
        W(0.4, () => { TimeCheck(_host.Speed == 2, $"second click resumes at the remembered speed (session speed {_host.Speed})"); Mouse(new Vector2(1100, 700), null); });
        // 125 % and 150 %.
        foreach (int pct in new[] { 125, 150 })
        {
            W(0.1, () => { Settings.Current.UiScale = pct; Settings.Apply(Settings.Current, GetWindow(), false); });
            foreach (int s in new[] { 1, 2, 3, 0 })
            {
                W(0.6, () => _signal.SetSpeed(s));
                W(0.5, () => { string tag = $"{pct}% {names[s]}"; TimeCentres(tag, pct / 100f); TimeGauge(tag, pct / 100f); Frames(names[s], $"_{pct}", pct * 640 / 100, pct * 64 / 100); });
            }
        }
        W(0.1, () => { Settings.Current.UiScale = 100; Settings.Apply(Settings.Current, GetWindow(), false); });
        W(0.6, () =>
        {
            _shotLog += $"time shot run result: {(_shotFailed ? "FAILED" : "OK")}\n";
            try { File.WriteAllText(Path.Combine(dir, "run-log.txt"), _shotLog); } catch (Exception ex) { GD.PrintErr("cannot write run-log.txt: " + ex.Message); _shotFailed = true; }
            GD.Print(_shotLog); GetTree().Quit(_shotFailed ? 1 : 0);
        });
    }

    // The embedded tooltip lives in a Window below the root (internal child); hide every visible popup window.
    void HideStrayTooltip(Node n)
    {
        foreach (var c in n.GetChildren(true))
        {
            if (c is Window { Visible: true } w && c != GetTree().Root) { _shotLog += $"hid {c.GetType().Name} {c.Name} at {w.Position}\n"; w.Hide(); }
            HideStrayTooltip(c);
        }
    }

    void TimeShotStep(double dt)
    {
        if (_timeQ.Count == 0) BuildTimeShots();
        if (_timeI < _timeQ.Count && _timeQ[_timeI](dt)) _timeI++;
    }
}
