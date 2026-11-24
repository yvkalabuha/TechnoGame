using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;

namespace Singularity.Client.Spike;

// TASK-0137: globe looks (volume and limb atmosphere). Setting "Вид глобуса" (default B, #444); --globe-look current|a|aplus|b overrides it for a run; F6 cycles in debug.
// --globe-look-shots (with --shots) takes the frames of every variant and measures frame time.
public sealed partial class Main
{
    GlobeLook _look = GlobeLook.Current;
    ShaderMaterial _atmoMat;
    MeshInstance3D _atmo;

    void SetupGlobeLook(string[] args)
    {
        _atmoMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/globe_atmosphere.gdshader") };
        _atmo = new MeshInstance3D { Mesh = new SphereMesh { Radius = 1.08f, Height = 2.16f, RadialSegments = 96, Rings = 48 }, MaterialOverride = _atmoMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_atmo);
        // The setting "Вид глобуса" gives the look (default: physical atmosphere, #444); --globe-look overrides it for a run, F6 cycles it in debug.
        var i = Array.IndexOf(args, "--globe-look");
        ApplyGlobeLook(i >= 0 && i + 1 < args.Length ? GlobeLookLogic.Parse(args[i + 1]) : (GlobeLook)Settings.Current.GlobeLook);
        Settings.OnGlobeLook = v => ApplyGlobeLook((GlobeLook)v);
    }

    void ApplyGlobeLook(GlobeLook l)
    {
        _look = l;
        _globeMat.SetShaderParameter("look", (int)l);
        _atmoMat.SetShaderParameter("look", (int)l);
        _atmoMat.SetShaderParameter("shell_r", 1.08f);
        _atmo.Visible = l != GlobeLook.Current;
        SetGlobeLight(false, 0, 0);
    }

    // Camera-tied light by default; a fixed world sun (lon/lat) for the terminator and night frames.
    void SetGlobeLight(bool world, float lon, float lat)
    {
        var d = GlobeLookLogic.Direction(lon, lat);
        foreach (var m in new[] { _globeMat, _atmoMat })
        {
            m.SetShaderParameter("light_world", world);
            if (world) m.SetShaderParameter("light_dir", new Vector3(d.X, d.Y, d.Z));
        }
    }

    // ---------- shots of the variants ----------
    readonly List<Func<double, bool>> _lookQ = new();
    int _lookI; double _lookT; int _lookPerfLeft; readonly List<double> _lookDt = new(); double _lookGpu; int _lookGpuN;
    readonly List<string> _lookLog = new();

    void BuildLookShots()
    {
        void W(double sec, Action a) { double t = 0; _lookQ.Add(dt => { t += dt; if (t < sec) return false; a(); return true; }); }
        string dir = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), _shotDir));
        Directory.CreateDirectory(dir);
        _lookQ.Add(_ => { _signal.SetSpeed(0); TitleScene(true); DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled); RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true); return true; });
        foreach (var look in new[] { GlobeLook.Current, GlobeLook.A, GlobeLook.APlus, GlobeLook.B })
        {
            string n = GlobeLookLogic.Names[(int)look];
            const float lon = 12, lat = 48;
            W(0.1, () => { ApplyGlobeLook(look); _yaw = lon; _pitch = lat; _dist = 2.6f; });
            W(0.7, () => SaveShot($"{n}_europe_day.png", null, true));
            W(0.1, () => SetGlobeLight(true, lon + 90, 12));     // sun to the right: the terminator crosses the disk
            W(0.5, () => SaveShot($"{n}_terminator.png", null, true));
            W(0.1, () => SetGlobeLight(true, lon + 180, 0));     // sun behind the globe: the night side
            W(0.5, () => SaveShot($"{n}_night.png", null, true));
            W(0.1, () => { SetGlobeLight(false, 0, 0); TitleScene(false); });
            W(0.5, () => SaveShot($"{n}_europe_day_hud.png"));
            W(0.1, () => { TitleScene(true); _mainMenu.Open(false); });
            W(0.7, () => { SaveShot($"{n}_main_menu.png"); _mainMenu.Close(); });
            W(0.5, () => { _lookDt.Clear(); _lookGpu = 0; _lookGpuN = 0; _lookPerfLeft = 300; });
            _lookQ.Add(_ => _lookPerfLeft <= 0);
            W(0.0, () =>
            {
                var s = _lookDt.OrderBy(v => v).ToList();
                _lookLog.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1} frames, mean {2:F2} ms, p99 {3:F2} ms, GPU render {4:F2} ms", n, s.Count, s.Average() * 1000, s[(int)(s.Count * 0.99)] * 1000, _lookGpuN > 0 ? _lookGpu / _lookGpuN : -1));
            });
        }
        W(0.1, () => { ApplyGlobeLook((GlobeLook)Settings.Current.GlobeLook); _yaw = 12; _pitch = 48; _dist = 2.6f; _settings.Open(); _settings.ShotTab(2); });
        W(0.6, () => { SaveShot("settings_general_globe_look.png"); _settings.Close(); });
        BuildLimbTintCheck(W);
        W(0.0, () =>
        {
            var gpu = RenderingServer.GetVideoAdapterName();
            File.WriteAllText(Path.Combine(dir, "frame_times.txt"), $"GPU: {gpu}\n1920x1080, vsync off, 300 frames of Europe by day, no HUD, 100 % UI scale\n" + string.Join("\n", _lookLog) + "\n");
            GetTree().Quit(_shotFailed ? 1 : 0);
        });
    }

    // C5/A4: the atmosphere B may tint the disk only inside 4 % of the radius. For three countries one ground point is rendered at the disk centre and at 0.96 R,
    // with look A (no atmosphere inside the disk) and look B (same light); the difference of the two is the atmosphere's share and must stay under MaxLimbDeltaE.
    void BuildLimbTintCheck(Action<double, Action> W)
    {
        var cases = new[] { ("Russia", 60f, 60f), ("Brazil", -52f, -10f), ("Australia", 134f, -25f) };
        foreach (var (name, lon, lat) in cases)
        {
            Color ca = default, cb = default, la = default, lb = default, cb99 = default, ca99 = default; float ratio = 0; bool inside = false;
            Vector3 P = GlobeData.ToSphere(lon, lat);
            Color Sample()
            {
                var px = _cam.UnprojectPosition(P);
                var img = GetViewport().GetTexture().GetImage();
                ratio = (px - GetViewport().GetVisibleRect().Size * 0.5f).Length() / (float)(540.0 * 1.0 / Math.Tan(Mathf.DegToRad(_cam.Fov) / 2) * Math.Tan(Math.Asin(1.0 / _dist)));
                float r = 0, g = 0, b = 0;   // 5x5 mean: a 1 px border line or a graticule line does not decide the sample
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++) { var c = img.GetPixel(Math.Clamp((int)px.X + dx, 0, img.GetWidth() - 1), Math.Clamp((int)px.Y + dy, 0, img.GetHeight() - 1)); r += c.R; g += c.G; b += c.B; }
                return new Color(r / 25, g / 25, b / 25);
            }
            void Aim(double ratioWanted)
            {
                _dist = 4.0f; _yaw = lon;   // the whole disk fits the screen, so that 0.96 R is on it
                double th = GlobeLookLogic.AngleForLimbRatio(_dist, ratioWanted);
                _pitch = (float)(lat > 0 ? lat - th : lat + th);
            }
            W(0.1, () => { inside = _data.IdAt(lon, lat) != 0; Aim(0.0); ApplyGlobeLook(GlobeLook.A); });
            W(0.5, () => { ca = Sample(); ApplyGlobeLook(GlobeLook.B); });
            W(0.4, () => { cb = Sample(); Aim(0.96); ApplyGlobeLook(GlobeLook.A); });
            W(0.5, () => { la = Sample(); var r96 = ratio; ApplyGlobeLook(GlobeLook.B); _lookLog.Add($"limb sample {name}: ratio {r96:F3}"); });
            W(0.4, () => { lb = Sample(); Aim(0.99); ApplyGlobeLook(GlobeLook.A); });
            W(0.5, () => { ca99 = Sample(); ApplyGlobeLook(GlobeLook.B); });
            W(0.4, () =>
            {
                cb99 = Sample();
                double atm = GlobeLookLogic.DeltaE(la.R, la.G, la.B, lb.R, lb.G, lb.B);
                double atmCentre = GlobeLookLogic.DeltaE(ca.R, ca.G, ca.B, cb.R, cb.G, cb.B);
                double vsCentre = GlobeLookLogic.DeltaE(cb.R, cb.G, cb.B, lb.R, lb.G, lb.B);
                double at99 = GlobeLookLogic.DeltaE(ca99.R, ca99.G, ca99.B, cb99.R, cb99.G, cb99.B);
                _lookLog.Add($"  pixels {name}: centre A {ca.ToHtml(false)} B {cb.ToHtml(false)}, 0.96 R A {la.ToHtml(false)} B {lb.ToHtml(false)}, 0.99 R A {ca99.ToHtml(false)} B {cb99.ToHtml(false)}, ratio last {ratio:F3}");
                bool ok = inside && atm <= GlobeLookLogic.MaxLimbDeltaE && atmCentre <= 1.0;
                _lookLog.Add(string.Format(CultureInfo.InvariantCulture, "tint check {0}: point inside country {1}; atmosphere share dE at centre {2:F2}, at 0.96 R {3:F2} (limit {4:F1}), at 0.99 R {5:F2} (info); B at 0.96 R vs B at centre {6:F2} (light + volume, info): {7}", name, inside, atmCentre, atm, GlobeLookLogic.MaxLimbDeltaE, at99, vsCentre, ok ? "PASS" : "FAIL"));
                if (!ok) _shotFailed = true;
            });
        }
        W(0.1, () => ApplyGlobeLook((GlobeLook)Settings.Current.GlobeLook));
    }

    // Called from ShotStep when --globe-look-shots is set; returns after queuing work for this frame.
    void LookShotStep(double dt)
    {
        if (_lookQ.Count == 0) BuildLookShots();
        if (_lookPerfLeft > 0)
        {
            _lookDt.Add(dt); _lookPerfLeft--;
            double g = RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid());
            if (g > 0) { _lookGpu += g; _lookGpuN++; }
        }
        if (_lookI < _lookQ.Count && _lookQ[_lookI](dt)) _lookI++;
    }
}
