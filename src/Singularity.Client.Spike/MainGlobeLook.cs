using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;

namespace Singularity.Client.Spike;

// TASK-0137: globe looks (volume and limb atmosphere). Setting "Вид глобуса" (default B, #444); --globe-look current|a|aplus|b overrides it for a run (the first OK in Settings ends the override: the saved setting wins again); F6 cycles in debug.
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
        SetGlobeLightDir(world, world ? new Vector3(d.X, d.Y, d.Z) : DayLightDir);
    }

    // The prior look always reads light_dir; looks A, A+ and B read it only with light_world. Day returns to the default direction, so no look keeps a night light.
    static readonly Vector3 DayLightDir = new(0.7f, 0.35f, 0.6f);
    void SetGlobeLightDir(bool world, Vector3 dir)
    {
        foreach (var m in new[] { _globeMat, _atmoMat })
        {
            m.SetShaderParameter("light_world", world);
            m.SetShaderParameter("light_dir", dir);
        }
    }

    // ---------- shots of the variants ----------
    readonly List<Func<double, bool>> _lookQ = new();
    int _lookI; int _lookPerfLeft; readonly List<double> _lookDt = new(); double _lookGpu; int _lookGpuN;
    readonly List<string> _lookLog = new();

    void BuildLookShots()
    {
        void W(double sec, Action a) { double t = 0; _lookQ.Add(dt => { t += dt; if (t < sec) return false; a(); return true; }); }
        string dir = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), _shotDir));
        Directory.CreateDirectory(dir);
        _lookQ.Add(_ => { _signal.SetSpeed(0); TitleScene(true); DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled); Engine.MaxFps = 0; /* the benchmark is not capped */ RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true); return true; });
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
            File.WriteAllText(Path.Combine(dir, "frame_times.txt"), $"GPU: {gpu}\n1920x1080, vsync mode {DisplayServer.WindowGetVsyncMode()}, max_fps {Engine.MaxFps}, 300 frames of Europe by day, no HUD, 100 % UI scale\n" + string.Join("\n", _lookLog) + "\n");
            GetTree().Quit(_shotFailed ? 1 : 0);
        });
    }

    // C5/A4: the atmosphere B may tint the disk only inside 4 % of the radius. For three countries one ground point is rendered at the disk centre and at 0.96, 0.98 and 0.99 R,
    // with look A (no atmosphere inside the disk) and look B (same light); the difference of the two is the atmosphere's share. Limits: MaxLimbDeltaE at 0.96 R and at the centre, MaxInnerDeltaE at 0.98 R; 0.99 R is reported only.
    void BuildLimbTintCheck(Action<double, Action> W)
    {
        var cases = new[] { ("Russia", 60f, 60f), ("Brazil", -52f, -10f), ("Australia", 134f, -25f) };
        double[] ratios = { 0.0, 0.95, 0.96, 0.98, 0.99 };   // centre, outside the 4 % band (must be 0), band edge, inside the band, info
        foreach (var (name, lon, lat) in cases)
        {
            var colA = new Color[ratios.Length]; var colB = new Color[ratios.Length]; var got = new float[ratios.Length]; bool inside = false;
            Vector3 P = GlobeData.ToSphere(lon, lat);
            Color Sample(int k)
            {
                var px = _cam.UnprojectPosition(P);
                var img = GetViewport().GetTexture().GetImage();
                got[k] = (px - GetViewport().GetVisibleRect().Size * 0.5f).Length() / (float)(540.0 * 1.0 / Math.Tan(Mathf.DegToRad(_cam.Fov) / 2) * Math.Tan(Math.Asin(1.0 / _dist)));
                float r = 0, g = 0, b = 0;   // 5x5 mean: a 1 px border line or a graticule line does not decide the sample
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++) { var c = img.GetPixel(Math.Clamp((int)px.X + dx, 0, img.GetWidth() - 1), Math.Clamp((int)px.Y + dy, 0, img.GetHeight() - 1)); r += c.R; g += c.G; b += c.B; }
                return new Color(r / 25, g / 25, b / 25);
            }
            void Aim(double ratioWanted)
            {
                _dist = 4.0f; _yaw = lon;   // the whole disk fits the screen, so that 0.99 R is on it
                double th = GlobeLookLogic.AngleForLimbRatio(_dist, ratioWanted);
                _pitch = (float)(lat > 0 ? lat - th : lat + th);
            }
            for (int k = 0; k < ratios.Length; k++)
            {
                int kk = k;
                W(0.1, () => { if (kk == 0) inside = _data.IdAt(lon, lat) != 0; Aim(ratios[kk]); ApplyGlobeLook(GlobeLook.A); });
                W(0.5, () => { colA[kk] = Sample(kk); ApplyGlobeLook(GlobeLook.B); });
                W(0.4, () => colB[kk] = Sample(kk));
            }
            W(0.0, () =>
            {
                double De(int k) => GlobeLookLogic.DeltaE(colA[k].R, colA[k].G, colA[k].B, colB[k].R, colB[k].G, colB[k].B);
                bool aimed = true; for (int k = 1; k < ratios.Length; k++) aimed &= Math.Abs(got[k] - ratios[k]) <= 0.01;
                bool ok = inside && aimed && De(0) <= 1.0 && De(1) <= 0.05 && De(2) <= GlobeLookLogic.MaxLimbDeltaE && De(3) <= GlobeLookLogic.MaxInnerDeltaE;
                _lookLog.Add(string.Format(CultureInfo.InvariantCulture, "tint check {0}: point inside country {1}, aimed within 0.01 R {2}; atmosphere share dE at centre {3:F2} (limit 1.0), 0.95 R {4:F2} (limit 0.05), 0.96 R {5:F2} (limit {6:F1}), 0.98 R {7:F2} (limit {8:F1}), 0.99 R {9:F2} (info); B at 0.96 R vs B at centre {10:F2} (light + volume, info): {11}",
                    name, inside, aimed, De(0), De(1), De(2), GlobeLookLogic.MaxLimbDeltaE, De(3), GlobeLookLogic.MaxInnerDeltaE, De(4), GlobeLookLogic.DeltaE(colB[0].R, colB[0].G, colB[0].B, colB[2].R, colB[2].G, colB[2].B), ok ? "PASS" : "FAIL"));
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
