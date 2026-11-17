using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Godot;

namespace Singularity.Client.Spike;

/// <summary>Throwaway spike (TASK-0067): globe-sheet surface, ID-raster regions, CC0 objects, bench mode. Not simulation code.</summary>
public sealed partial class Main : Node3D
{
    static readonly Color PaperColor = new("F2EFE6");
    static readonly string[] TintHex = { "EBCDB7", "C8D7E8", "D5E0BC", "E3D0E3", "EFE0AE", "BEDCD1", "E0CAC2", "CDCDE6" };

    GlobeData _data = new();
    ShaderMaterial _globeMat;
    Camera3D _cam;
    Overlay _overlay;
    Label _hud;
    float _yaw = 10, _pitch = 35, _dist = 3.0f;
    Vector2 _pressPos; bool _pressed, _dragged;
    int _selected;
    bool _hatchScreenSpace = true;
    readonly List<ShaderMaterial> _factoryMats = new();
    Node3D _chimney;
    readonly List<ShaderMaterial> _allObjMats = new();
    Node3D _plane, _planeAnchor;
    float _planeAngle;
    double _t;
    string _factoryState = "";
    readonly Dictionary<string, long> _startup = new();
    readonly Stopwatch _sw = Stopwatch.StartNew();

    // bench
    bool _shotDone; bool _bench; int _phase = -1; double _phaseT; readonly List<double> _frames = new();
    readonly StringBuilder _json = new();
    string _outDir = "bench-out";

    public override void _Ready()
    {
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        _startup["engine_to_ready_ms"] = (long)Time.GetTicksMsec();
        var args = OS.GetCmdlineUserArgs();
        _bench = args.Contains("--bench");
        if (_bench) DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        var outIdx = Array.IndexOf(args, "--out");
        if (outIdx >= 0 && outIdx + 1 < args.Length) _outDir = args[outIdx + 1];

        var env = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = PaperColor };
        AddChild(new WorldEnvironment { Environment = env });

        Mark("start");
        string dataPath = ProjectSettings.GlobalizePath("res://assets/data/ne_50m_admin_0_countries_min.json");
        _data.Load(dataPath); Mark("load_geojson_ms");
        _data.ComputeBoxAreas(); _data.Rasterise(); Mark("rasterise_id_map_ms");
        _data.AssignTints(); Mark("assign_tints_ms");

        BuildGlobe();
        _cam = new Camera3D { Fov = 35, Near = 0.004f, Far = 50 };
        AddChild(_cam);
        UpdateCamera();

        var top = _data.Countries.OrderByDescending(c => c.BoxArea).Take(30).ToList();
        foreach (var c in top) _data.ComputeLabelPoint(c);
        Mark("label_points_ms");

        var layer = new CanvasLayer();
        AddChild(layer);
        _overlay = new Overlay();
        layer.AddChild(_overlay);
        _overlay.Setup(_cam, top);
        _overlay.AddTag(31, 49, "■ Узкое место");
        _overlay.AddTag(-100, 40, "▲ Дефицит");
        _overlay.AddTag(120, 30, "■ Срыв стройки");
        _hud = new Label { Position = new Vector2(12, 10) };
        _hud.AddThemeColorOverride("font_color", new Color("1D1E20"));
        _hud.AddThemeColorOverride("font_outline_color", PaperColor);
        _hud.AddThemeConstantOverride("outline_size", 4);
        layer.AddChild(_hud);

        BuildObjects();
        Mark("objects_ms");
        // Demo "occupied" region (hatched in the owner's tint line colour), fixed for the spike.
        _globeMat.SetShaderParameter("hatch_id", IdOf("Mongolia"));
        _globeMat.SetShaderParameter("hatch_owner_id", IdOf("China"));
        _selected = IdOf("Germany");
        ApplySelection();
        if (_bench) { Directory.CreateDirectory(_outDir); }
    }

    void Mark(string key)
    {
        _startup[key] = _sw.ElapsedMilliseconds;
        _sw.Restart();
    }

    int IdOf(string name) => _data.Countries.First(c => c.Name == name).Id;

    // ---------- globe ----------
    void BuildGlobe()
    {
        const int nx = 192, ny = 96;
        var v = new Vector3[(nx + 1) * (ny + 1)];
        var uv = new Vector2[v.Length];
        for (int j = 0; j <= ny; j++)
            for (int i = 0; i <= nx; i++)
            {
                float lon = -180f + 360f * i / nx, lat = 90f - 180f * j / ny;
                v[j * (nx + 1) + i] = GlobeData.ToSphere(lon, lat);
                uv[j * (nx + 1) + i] = new Vector2((float)i / nx, (float)j / ny);
            }
        var idx = new List<int>();
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                idx.AddRange(new[] { a, b, c, b, d, c }); // clockwise seen from outside
            }
        var arr = new Godot.Collections.Array();
        arr.Resize((int)Mesh.ArrayType.Max);
        arr[(int)Mesh.ArrayType.Vertex] = v;
        arr[(int)Mesh.ArrayType.Normal] = v;
        arr[(int)Mesh.ArrayType.TexUV] = uv;
        arr[(int)Mesh.ArrayType.Index] = idx.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);

        var img = Image.CreateFromData(GlobeData.W, GlobeData.H, false, Image.Format.R8, _data.Ids);
        var idTex = ImageTexture.CreateFromImage(img);
        var lut = new byte[256];
        foreach (var c in _data.Countries) lut[c.Id] = (byte)c.Tint;
        var lutTex = ImageTexture.CreateFromImage(Image.CreateFromData(256, 1, false, Image.Format.R8, lut));

        _globeMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/globe.gdshader") };
        _globeMat.SetShaderParameter("ids", idTex);
        _globeMat.SetShaderParameter("tint_lut", lutTex);
        var tints = TintHex.Select(h => { var c = new Color(h); return new Vector3(c.R, c.G, c.B); }).ToArray();
        _globeMat.SetShaderParameter("tints", tints);
        AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = _globeMat });
    }

    void ApplySelection() => _globeMat.SetShaderParameter("selected_id", _selected);

    // ---------- objects ----------
    ShaderMaterial NewObjMat(Aabb box)
    {
        var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/object.gdshader") };
        m.SetShaderParameter("y_min", box.Position.Y);
        m.SetShaderParameter("y_size", box.Size.Y);
        _allObjMats.Add(m);
        return m;
    }

    Node3D Anchor(float lon, float lat)
    {
        var p = GlobeData.ToSphere(lon, lat);
        var north = (Vector3.Up - p * p.Y).Normalized();
        var a = new Node3D();
        AddChild(a);
        a.LookAtFromPosition(p, p + north, p); // local +Y = surface normal
        return a;
    }

    /// <summary>Loads a CC0 model, strips its materials, normalises its size and puts its base on the anchor.</summary>
    Node3D PlaceModel(Node3D anchor, string res, float size, Vector3 offset, List<ShaderMaterial> mats = null)
    {
        Node3D root;
        if (res.EndsWith(".obj"))
        {
            root = new Node3D();
            root.AddChild(new MeshInstance3D { Mesh = GD.Load<Mesh>(res) });
        }
        else root = GD.Load<PackedScene>(res).Instantiate<Node3D>();
        var box = Bounds(root);
        float s = size / Math.Max(box.Size.X, Math.Max(box.Size.Y, box.Size.Z));
        foreach (var mi in AllMeshes(root))
        {
            var m = NewObjMat(mi.Mesh.GetAabb());
            mi.MaterialOverride = m;
            mats?.Add(m);
        }
        root.Scale = Vector3.One * s;
        root.Position = offset - new Vector3(box.GetCenter().X, box.Position.Y, box.GetCenter().Z) * s;
        var holder = new Node3D { Position = Vector3.Zero };
        anchor.AddChild(holder);
        holder.AddChild(root);
        holder.Position = Vector3.Zero;
        return holder;
    }

    static IEnumerable<MeshInstance3D> AllMeshes(Node n)
    {
        if (n is MeshInstance3D mi && mi.Mesh != null) yield return mi;
        foreach (var c in n.GetChildren()) foreach (var r in AllMeshes(c)) yield return r;
    }

    static Aabb Bounds(Node3D root)
    {
        bool first = true; Aabb res = default;
        foreach (var mi in AllMeshes(root))
        {
            var t = mi.Transform; Node p = mi.GetParent();
            while (p != null && p != root && p is Node3D n3) { t = n3.Transform * t; p = p.GetParent(); }
            var b = t * mi.Mesh.GetAabb();
            res = first ? b : res.Merge(b); first = false;
        }
        return res;
    }

    void BuildObjects()
    {
        // Factory site (Germany): building under construction, chimney appears when in service, water tower, tank.
        var factory = Anchor(10.0f, 51.0f);
        PlaceModel(factory, "res://assets/models/building-a.glb", 0.010f, Vector3.Zero, _factoryMats);
        _chimney = PlaceModel(factory, "res://assets/models/chimney-large.glb", 0.012f, new Vector3(0.007f, 0, 0));
        PlaceModel(factory, "res://assets/models/water-tower.glb", 0.010f, new Vector3(-0.008f, 0, 0.004f));

        // Airfield (Germany, east): hangar + runway strip (primitive, not an asset) + the aircraft circling.
        var air = Anchor(13.4f, 52.5f);
        PlaceModel(air, "res://assets/models/building-t.glb", 0.007f, new Vector3(0.006f, 0, -0.004f));
        var runwayBox = new BoxMesh { Size = new Vector3(0.030f, 0.0002f, 0.003f) };
        var runway = new MeshInstance3D { Mesh = runwayBox, MaterialOverride = NewObjMat(runwayBox.GetAabb()) };
        ((ShaderMaterial)runway.MaterialOverride).SetShaderParameter("base", new Color("9C9A93"));
        air.AddChild(runway);
        _planeAnchor = air;
        _plane = PlaceModel(air, "res://assets/models/funky_aircraft_low.obj", 0.006f, Vector3.Zero);

        // 300 instances of one model on land (P6: instancing cost).
        var scene = GD.Load<PackedScene>("res://assets/models/building-j.glb").Instantiate<Node3D>();
        var mesh = AllMeshes(scene).First().Mesh;
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = 300 };
        var box = mesh.GetAabb();
        float sc = 0.004f / Math.Max(box.Size.X, Math.Max(box.Size.Y, box.Size.Z));
        uint seed = 12345; int placed = 0;
        float Rnd() { seed = seed * 1664525u + 1013904223u; return (seed >> 8) / 16777216f; }
        while (placed < 300)
        {
            float lon = -170 + 340 * Rnd(), lat = -55 + 120 * Rnd();
            if (_data.IdAt(lon, lat) == 0) continue;
            var p = GlobeData.ToSphere(lon, lat);
            var north = (Vector3.Up - p * p.Y).Normalized();
            var basis = Basis.LookingAt(-north, p) * Basis.FromScale(Vector3.One * sc);
            mm.SetInstanceTransform(placed++, new Transform3D(basis, p - p * box.Position.Y * sc));
        }
        var mmi = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = NewObjMat(box) };
        AddChild(mmi);
    }

    // ---------- camera and input ----------
    void UpdateCamera()
    {
        float y = Mathf.DegToRad(_yaw), p = Mathf.DegToRad(_pitch);
        var pos = _dist * new Vector3(Mathf.Cos(p) * Mathf.Sin(y), Mathf.Sin(p), Mathf.Cos(p) * Mathf.Cos(y));
        _cam.LookAtFromPosition(pos, Vector3.Zero, Vector3.Up);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_bench) return;
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed) { _pressed = true; _dragged = false; _pressPos = mb.Position; }
                else { _pressed = false; if (!_dragged) Pick(mb.Position); }
            }
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp) _dist = Math.Max(1.02f, 1f + (_dist - 1f) * 0.88f);
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown) _dist = Math.Min(8f, 1f + (_dist - 1f) / 0.88f);
        }
        else if (e is InputEventMouseMotion mm && _pressed)
        {
            if ((mm.Position - _pressPos).Length() > 5f) _dragged = true;
            float k = 0.12f * Math.Min(1f, (_dist - 1f) * 0.8f + 0.05f);
            _yaw -= mm.Relative.X * k; _pitch = Mathf.Clamp(_pitch + mm.Relative.Y * k, -80f, 80f);
        }
        else if (e is InputEventKey key && key.Pressed && key.Keycode == Key.H)
        {
            _hatchScreenSpace = !_hatchScreenSpace;
            _globeMat.SetShaderParameter("hatch_in_screen_space", _hatchScreenSpace);
        }
    }

    void Pick(Vector2 screen)
    {
        var o = _cam.ProjectRayOrigin(screen); var d = _cam.ProjectRayNormal(screen);
        float b = o.Dot(d), c = o.Dot(o) - 1f, disc = b * b - c;
        if (disc < 0) { _selected = 0; ApplySelection(); return; }
        var hit = o + d * (-b - Mathf.Sqrt(disc));
        var ll = GlobeData.ToLonLat(hit);
        _selected = _data.IdAt(ll.X, ll.Y);
        ApplySelection();
    }

    // ---------- per-frame ----------
    public override void _Process(double delta)
    {
        _t += delta;
        if (_bench) BenchStep(delta);
        UpdateCamera();
        AnimateObjects();
        string sel = _selected == 0 ? "-" : _data.Countries[_selected - 1].Name;
        _hud.Text = $"FPS {Engine.GetFramesPerSecond()}  draw calls {(long)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)}  " +
                    $"labels {_overlay.VisibleLabels}  docked tags {_overlay.DockedTags}\n" +
                    $"Region: {sel}\nFactory: {_factoryState}\nLMB drag = rotate, wheel = zoom, click = pick, H = hatch space ({(_hatchScreenSpace ? "screen" : "uv")})";
    }

    void AnimateObjects()
    {
        // Loop: 8 s under construction (progress 0..1), then 4 s in service.
        double c = _t % 12.0;
        float progress = c < 8 ? (float)(c / 8.0) : 1f;
        bool inService = c >= 8;
        foreach (var m in _factoryMats) m.SetShaderParameter("progress", progress);
        if (_chimney != null) _chimney.Visible = inService;
        _factoryState = inService ? "in service" : $"under construction {(int)(progress * 100)}%";
        _planeAngle += (float)(0.5 * GetProcessDeltaTime());
        _plane.Position = new Vector3(Mathf.Cos(_planeAngle), 0, Mathf.Sin(_planeAngle)) * 0.02f + new Vector3(0, 0.008f, 0);
        _plane.Rotation = new Vector3(0, -_planeAngle, 0);
    }

    // ---------- bench ----------
    // phases: warm-up, idle (whole disc), rotating, region view (hatching), zoomed in over the objects (+ shot mid-construction)
    static readonly string[] PhaseNames = { "warmup", "idle", "rotate", "region", "zoom" };
    static readonly double[] PhaseLen = { 3, 6, 6, 5, 8 };

    void BenchStep(double delta)
    {
        if (_phase < 0) { _phase = 0; _phaseT = 0; EnterPhase(0); }
        _phaseT += delta;
        if (_phase >= 1 && _phaseT > 0.5) _frames.Add(delta); // skip the shot-saving hitch after a phase change
        if (_phase == 2) _yaw += (float)(40 * delta);
        if (_phase == 4 && !_shotDone && _phaseT >= 4.0)
        {
            _shotDone = true; // factory progress is 0.5 here
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_outDir, "shot_construction.png"));
        }
        if (_phaseT >= PhaseLen[_phase])
        {
            if (_phase >= 1) { Finish(PhaseNames[_phase]); }
            _phase++; _phaseT = 0; _frames.Clear();
            if (_phase >= PhaseLen.Length) { WriteBench(); GetTree().Quit(); return; }
            EnterPhase(_phase);
        }
    }

    void EnterPhase(int p)
    {
        if (p == 1) { _yaw = 10; _pitch = 35; _dist = 4.2f; }
        if (p == 3) { _yaw = 100; _pitch = 46; _dist = 1.6f; _selected = IdOf("China"); ApplySelection(); }
        if (p == 4) { _yaw = 11.7f; _pitch = 51.8f; _dist = 1.08f; _selected = IdOf("Germany"); ApplySelection(); _t = 0; _shotDone = false; }
    }

    void Finish(string name)
    {
        double total = _frames.Sum(), avgFps = _frames.Count / total;
        var sorted = _frames.OrderByDescending(x => x).ToList();
        int n1 = Math.Max(1, sorted.Count / 100);
        double low1 = 1.0 / sorted.Take(n1).Average();
        Image img = GetViewport().GetTexture().GetImage();
        img.SavePng(Path.Combine(_outDir, $"shot_{name}.png"));
        _json.Append($"\"{name}\":{{\"frames\":{_frames.Count},\"avg_fps\":{avgFps:F1},\"p1_low_fps\":{low1:F1},\"max_frame_ms\":{sorted[0] * 1000:F2}," +
                     $"\"draw_calls\":{(long)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)}," +
                     $"\"objects\":{(long)Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame)}," +
                     $"\"primitives\":{(long)Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame)}," +
                     $"\"video_mem_mb\":{Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576.0:F1}," +
                     $"\"rss_mb\":{RssMb():F0}}},");
    }

    static double RssMb()
    {
        foreach (var l in File.ReadLines("/proc/self/status"))
            if (l.StartsWith("VmRSS:")) return double.Parse(l.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]) / 1024.0;
        return -1;
    }

    void WriteBench()
    {
        var sb = new StringBuilder("{");
        sb.Append($"\"gpu\":\"{RenderingServer.GetVideoAdapterName()}\",\"driver\":\"{RenderingServer.GetVideoAdapterApiVersion()}\",\"godot\":\"{Engine.GetVersionInfo()["string"]}\",");
        sb.Append("\"startup_ms\":{");
        foreach (var kv in _startup) sb.Append($"\"{kv.Key}\":{kv.Value},");
        sb.Length--; sb.Append("},");
        sb.Append(_json.ToString());
        sb.Length--; sb.Append('}');
        File.WriteAllText(Path.Combine(_outDir, "bench.json"), sb.ToString());
        GD.Print(sb.ToString());
    }
}
