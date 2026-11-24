using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;

namespace Singularity.Client.Spike;

// TASK-0137: prototype of the globe looks (volume and limb atmosphere). Switch: launch flag --globe-look current|a|aplus|b, key F6 cycles.
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
        var i = Array.IndexOf(args, "--globe-look");
        ApplyGlobeLook(i >= 0 && i + 1 < args.Length ? GlobeLookLogic.Parse(args[i + 1]) : GlobeLook.Current);
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
        W(0.0, () =>
        {
            var gpu = RenderingServer.GetVideoAdapterName();
            File.WriteAllText(Path.Combine(dir, "frame_times.txt"), $"GPU: {gpu}\n1920x1080, vsync off, 300 frames of Europe by day, no HUD, 100 % UI scale\n" + string.Join("\n", _lookLog) + "\n");
            GetTree().Quit(_shotFailed ? 1 : 0);
        });
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
