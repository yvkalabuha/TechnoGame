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
    // Warm land tints only: none is close to the blue-grey sea (review item 5).
    static readonly string[] TintHex = { "EBCDB7", "D5E0BC", "E3D0E3", "EFE0AE", "E0CAC2", "D4C79F", "DDB9A0", "C9D4A8" };

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

    // v0.2: simulation, signal field, actor inset, tech tree
    SimHost _host;
    SignalField _signal;
    ActorPanel _actorPanel;
    TechTree _tree;
    AudioDirector _audio;
    Control _audioPop;
    bool _fatalLogged;
    Label _loading, _help;
    Button _helpBtn;
    bool _debug;
    int[] _actorCountry;                 // actor index -> country id (0 = none)
    bool _shots; int _shotStep; double _shotT; string _shotDir = "../../docs/ui/prototypes/client-v04";
    string _shotLog = ""; int _shotCand = -1; long _shotTick;

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
        _shots = args.Contains("--shots");
        var shotIdx = Array.IndexOf(args, "--shot-dir");
        if (shotIdx >= 0 && shotIdx + 1 < args.Length) _shotDir = args[shotIdx + 1];
        if (_bench) DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        var outIdx = Array.IndexOf(args, "--out");
        if (outIdx >= 0 && outIdx + 1 < args.Length) _outDir = args[outIdx + 1];

        Fonts.Load();
        Loc.Load();
        var langIdx = Array.IndexOf(args, "--lang");
        if (langIdx >= 0 && langIdx + 1 < args.Length) Loc.SetLang(args[langIdx + 1]);
        _debug = args.Contains("--debug");

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

        var top = _data.Countries.Where(c => Names.Country(c.Name) != null).OrderByDescending(c => c.BoxArea).Take(30).ToList();
        foreach (var c in top) _data.ComputeLabelPoint(c);
        Mark("label_points_ms");

        var layer = new CanvasLayer();
        AddChild(layer);
        _overlay = new Overlay();
        layer.AddChild(_overlay);
        _overlay.Setup(_cam);
        foreach (var c in top) _overlay.AddLabel(c, false);
        _hud = new Label { Position = new Vector2(12, 0), Visible = _debug };
        _hud.AddThemeFontOverride("font", Fonts.Mono);
        _hud.AnchorTop = 1; _hud.AnchorBottom = 1; _hud.OffsetTop = -120; _hud.AddThemeFontSizeOverride("font_size", 14); _hud.OffsetBottom = -46;
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
        else BuildClient(layer);
    }

    public override void _ExitTree() => _host?.Dispose();

    // ---------- client ----------
    void BuildClient(CanvasLayer layer)
    {
        _host = new SimHost();
        _host.Start();
        _loading = Ui.Lbl("ui.loading", 18);
        _loading.Position = new Vector2(20, 60);
        layer.AddChild(_loading);
        _tintsReady = false;
    }

    bool _tintsReady;

    /// <summary>Called once the session exists: builds the UI that needs the actor list.</summary>
    void FinishClient()
    {
        var ids = _host.Layout.ActorIds.ToArray();
        _actorCountry = new int[ids.Length];
        var lut = new byte[256];
        for (int i = 0; i < ids.Length; i++)
            if (Names.CountryOfActor.TryGetValue(ids[i], out var cn))
            {
                var c = _data.Countries.First(x => x.Name == cn);
                _actorCountry[i] = c.Id; lut[c.Id] = 255;
                _data.ComputeLabelPoint(c);
                _overlay.AddLabel(c, true);
            }
        _globeMat.SetShaderParameter("slice_lut", ImageTexture.CreateFromImage(Image.CreateFromData(256, 1, false, Image.Format.R8, lut)));
        _globeMat.SetShaderParameter("dim_others", 0.6f);
        _globeMat.SetShaderParameter("hatch_id", 0);   // the spike's demo hatching was a mock; the globe now shows the slice only

        var layer = _hud.GetParent<CanvasLayer>();
        _signal = new SignalField(_host, ids);
        _signal.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _signal.PowerChosen += i => ChoosePower(i, false);
        _signal.Debug = _debug;
        layer.AddChild(_signal);

        _audio = new AudioDirector(_host);
        AddChild(_audio);
        _signal.SignalChanged += _audio.OnSignal;
        _signal.AudioPressed += () => _audioPop.Visible = !_audioPop.Visible;
        _audioPop = new PanelContainer { Visible = false };
        ((PanelContainer)_audioPop).AddThemeStyleboxOverride("panel", Ui.Box(Ui.Sheet, Ui.Ink, 2, 12));
        _audioPop.AnchorLeft = 1; _audioPop.AnchorRight = 1; _audioPop.OffsetLeft = -380; _audioPop.OffsetRight = -10; _audioPop.OffsetTop = 52;
        _audioPop.AddChild(_audio.BuildPopover());
        layer.AddChild(_audioPop);

        // Inset sized by its content: anchored top-right, no bottom anchor.
        _actorPanel = new ActorPanel(_host) { Visible = false, Debug = _debug };
        _actorPanel.AnchorLeft = 1; _actorPanel.AnchorRight = 1;
        _actorPanel.OffsetLeft = -540; _actorPanel.OffsetRight = -10; _actorPanel.OffsetTop = 56;
        _actorPanel.Closed += () => _actorPanel.Visible = false;
        _actorPanel.OpenTree += () => OpenTree(0);
        layer.AddChild(_actorPanel);

        _tree = new TechTree(_host) { Visible = false };
        _tree.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _tree.OffsetTop = 44;
        _tree.Closed += () => _tree.Visible = false;
        layer.AddChild(_tree);
        BuildHelp(layer);
        _loading.Visible = false;
        _tintsReady = true;
        _host.WhyActor = 0;
    }

    void BuildHelp(CanvasLayer layer)
    {
        _help = Ui.Lbl("help.text", 14, Ui.Ink2);
        _help.AddThemeStyleboxOverride("normal", Ui.Box(Ui.Sheet, Ui.Ink, 1, 10));
        _help.Position = new Vector2(12, 0); _help.AnchorTop = 1; _help.AnchorBottom = 1; _help.OffsetTop = -150; _help.OffsetBottom = -50; _help.OffsetLeft = 12;
        _help.Visible = false;
        layer.AddChild(_help);
        _helpBtn = Ui.Btn("?", () => _help.Visible = !_help.Visible);
        _helpBtn.TooltipText = "tip.help";
        _helpBtn.AnchorTop = 1; _helpBtn.AnchorBottom = 1; _helpBtn.OffsetTop = -42; _helpBtn.OffsetBottom = -10; _helpBtn.OffsetLeft = 12; _helpBtn.OffsetRight = 48;
        layer.AddChild(_helpBtn);
    }

    void OpenTree(int mode) { _audio?.Tick(); _tree.ShowFor(_signal.Actor, mode); }

    void ChoosePower(int i, bool selectInSegment = true)
    {
        if (selectInSegment) _signal.SelectPower(i);
        _host.WhyActor = i;
        _actorPanel.ShowActor(i);
        if (_tree.Visible) _tree.ShowFor(i, 0);
        _audio?.Tick();
        if (_actorCountry[i] != 0)
        {
            _selected = _actorCountry[i]; ApplySelection();
            var ll = _data.Countries[_selected - 1].LabelPoint;
            _yaw = ll.X; _pitch = Mathf.Clamp(ll.Y, -60, 60); _dist = 2.4f;
        }
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
        if (_bench || (_tree != null && _tree.Visible && e is InputEventMouse)) return;
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
        else if (e is InputEventKey sk && sk.Pressed && _tintsReady && sk.Keycode is Key.Space or Key.Key1 or Key.Key2 or Key.Key3 or Key.T)
        {
            if (sk.Keycode == Key.Space) _signal.SetSpeed(_host.Speed == 0 ? 1 : 0);
            else if (sk.Keycode == Key.T) { if (_tree.Visible) _tree.Visible = false; else OpenTree(0); }
            else _signal.SetSpeed((int)(sk.Keycode - Key.Key1) + 1);
        }
        else if (e is InputEventKey f3 && f3.Pressed && f3.Keycode == Key.F3)
        {
            _debug = !_debug; _hud.Visible = _debug;
            if (_signal != null) { _signal.Debug = _debug; _actorPanel.Debug = _debug; }
        }
        else if (e is InputEventKey f1 && f1.Pressed && f1.Keycode == Key.F1 && _help != null) _help.Visible = !_help.Visible;
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
        int id = _data.IdAt(ll.X, ll.Y);
        if (_tintsReady && id != 0)
        {
            int a = Array.IndexOf(_actorCountry, id);
            if (a >= 0) { ChoosePower(a); return; }
        }
        _selected = id;
        ApplySelection();
    }

    // ---------- per-frame ----------
    public override void _Process(double delta)
    {
        _t += delta;
        if (_host != null && !_tintsReady)
        {
            if (_host.Fatal != null)
            {
                _loading.Text = Loc.T("ui.simerror") + _host.Fatal;
                if (!_fatalLogged) { _fatalLogged = true; GD.PrintErr(_host.Fatal); }   // once, not on every frame
            }
            else if (_host.Ready) FinishClient();
            else _loading.Text = Loc.T(_host.Status);
        }
        if (_helpBtn != null) { _helpBtn.Visible = !_tree.Visible; if (_tree.Visible) _help.Visible = false; }
        if (_shots && _tintsReady) ShotStep(delta);
        if (_bench) BenchStep(delta);
        UpdateCamera();
        if (_audio != null) { _audio.Actor = _signal.Actor; _audio.CameraMoved(new Vector2(_yaw + _pitch * 1.7f, _dist)); }
        AnimateObjects();
        if (_debug)
        {
            string sel = _selected == 0 ? "-" : _data.Countries[_selected - 1].Name;
            _hud.Text = $"FPS {Engine.GetFramesPerSecond()}  draw calls {(long)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)}  labels {_overlay.VisibleLabels}\n" +
                        $"region: {sel}  (factory, aircraft: spike mock)" + (_host?.Snap is { JobError: not "" } js ? "\njob error: " + js.JobError : "");
        }
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

    // ---------- screenshots (acceptance) ----------
    void ShotStep(double dt)
    {
        _shotT += dt;
        string dir = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), _shotDir));
        void Shot(string name, Rect2I? r = null)
        {
            Directory.CreateDirectory(dir);
            var img = GetViewport().GetTexture().GetImage();
            if (r is Rect2I rr) img = img.GetRegion(rr);
            img.SavePng(Path.Combine(dir, name));
        }
        var s = _host.Snap;
        var vp = GetViewport().GetVisibleRect().Size;
        switch (_shotStep)
        {
            case 0:
                _signal.SetSpeed(3); ChoosePower(0); _shotStep = 1; _shotT = 0; break;
            case 1 when _shotT >= 3:
                // Pause, move 10 points from procurement to R (as the sliders would) and send: the command must stay pending.
                _shotLog += $"before: date {s.Date} tick {s.Tick} shares_in_force {Fmt(s.SharesInForce[0])} tick_ms {s.LastTickMs:F2}\n";
                _signal.SetSpeed(0);
                _shotTick = s.Tick;
                _actorPanel.ShotEdit(0, 45);
                _shotStep = 2; _shotT = 0; break;
            case 2 when _shotT >= 1.2:
                _shotLog += $"paused: tick {s.Tick} (was {_shotTick}), pending {s.Pending}, last {s.Last.Status}\n";
                Shot("globe_panel_pending.png");
                _signal.SetSpeed(3); _shotStep = 3; _shotT = 0; break;
            case 3 when _shotT >= 3:
                _shotLog += $"after: date {s.Date} tick {s.Tick} shares_in_force {Fmt(s.SharesInForce[0])} last {s.Last.Status} pending {s.Pending} tick_ms {s.LastTickMs:F2}\n";
                Shot("globe_panel.png"); Shot("signal_field.png", new Rect2I(0, 0, (int)vp.X, 52));
                // A refused command (shares do not sum to 1) lights the ОТКАЗ place.
                _host.Submit(new Singularity.Core.Commands.SetBudgetShares(0, new Singularity.Core.Policy.BudgetShares(
                    Singularity.Kernel.Fx6.FromRaw(1_000_000), Singularity.Kernel.Fx6.FromRaw(500_000), default, default, default)), "cmd.budget");
                _shotStep = 4; _shotT = 0; break;
            case 4 when _shotT >= 1.0:
                _shotLog += $"refusal: last {s.Last.Status} rejected_flag {s.LastRejected} reason {s.Last.Reason}\n";
                Shot("signal_active_new.png", new Rect2I(0, 0, (int)vp.X, 52)); Shot("globe_panel_refused.png");
                _signal.AckAll(); _shotStep = 5; _shotT = 0; break;
            case 5 when _shotT >= 0.4:
                Shot("signal_active_acked.png", new Rect2I(0, 0, (int)vp.X, 52)); _shotStep = 6; _shotT = 0; break;
            case 6:
                // Pick the first power that has a yearly requirement (a goal for the tree).
                if (_shotT > 0.4)
                {
                    bool has = s.WhyActor == _shotCand && s.Target != null;
                    if (_shotCand >= 0) _shotLog += $"why_not actor {_host.Layout.ActorIds[_shotCand]}: target {(s.Target is { } tt ? tt.Kind + " " + tt.Id : "none")}, links {s.Links.Count}\n";
                    if (has || _shotCand >= _host.Layout.ActorIds.Length - 1) { ChoosePower(Math.Max(_shotCand, 0)); OpenTree(0); _shotStep = 7; _shotT = 0; }
                    else { _shotCand++; _host.WhyActor = _shotCand; _shotT = 0; }
                }
                break;
            case 7 when _shotT >= 1.5: Shot("tech_tree_a.png"); _tree.SetMode(1); _shotStep = 8; _shotT = 0; break;
            case 8 when _shotT >= 1.0:
                Shot("tech_tree_b.png");
                _signal.SetLang("en"); _shotStep = 9; _shotT = 0; break;
            case 9 when _shotT >= 1.0:
                Shot("tech_tree_b_en.png"); _tree.SetMode(0); _shotStep = 10; _shotT = 0; break;
            case 10 when _shotT >= 0.8:
                Shot("tech_tree_a_en.png"); _tree.Visible = false; _shotStep = 11; _shotT = 0; break;
            case 11 when _shotT >= 0.8:
                Shot("globe_panel_en.png"); Shot("signal_field_en.png", new Rect2I(0, 0, (int)vp.X, 52));
                _signal.SetLang("ru");
                // A valid command lifts the ОТКАЗ place: it shows the cleared state («снят»).
                _host.Submit(new Singularity.Core.Commands.SetBudgetShares(0, new Singularity.Core.Policy.BudgetShares(
                    Singularity.Kernel.Fx6.FromRaw(200_000), Singularity.Kernel.Fx6.FromRaw(200_000), Singularity.Kernel.Fx6.FromRaw(200_000),
                    Singularity.Kernel.Fx6.FromRaw(200_000), Singularity.Kernel.Fx6.FromRaw(200_000))), "cmd.budget");
                _shotStep = 12; _shotT = 0; break;
            case 12 when _shotT >= 1.0:
                _shotLog += $"cleared: last {s.Last.Status} rejected_flag {s.LastRejected}\n";
                Shot("signal_cleared.png", new Rect2I(0, 0, (int)vp.X, 52));
                _host.SimulateFatal("screenshot hook: simulated simulation failure");
                _shotStep = 13; _shotT = 0; break;
            case 13 when _shotT >= 0.5:
                Shot("signal_fail_new.png", new Rect2I(0, 0, (int)vp.X, 52));
                _signal.AckAll(); _shotStep = 14; _shotT = 0; break;
            case 14 when _shotT >= 0.4:
                Shot("signal_fail_acked.png", new Rect2I(0, 0, (int)vp.X, 52));
                _audioPop.Visible = true; _shotStep = 15; _shotT = 0; break;
            case 15 when _shotT >= 0.5:
                Shot("audio_popover.png"); _audioPop.Visible = false;
                _signal.SetSpeed(1); _shotStep = 16; _shotT = 0; break;
            case 16 when _shotT >= 0.5:
                _audio.StartDemo(); _shotStep = 17; _shotT = 0; break;
            case 17:
                // The demo script runs in real time; its sound is recorded from the Master bus.
                if (!_audio.StepDemo(dt) && _shotT >= AudioDirector.DemoSeconds + 0.5)
                {
                    int bytes = _audio.Active ? _audio.SaveDemo(Path.Combine(dir, "audio_demo.wav")) : 0;
                    _shotLog += $"audio demo: {(bytes > 0 ? bytes + " bytes of PCM" : "not recorded (no audio device)")}\n";
                    _shotLog += $"final: date {s.Date} tick {s.Tick}\n";
                    Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "run-log.txt"), _shotLog); GD.Print(_shotLog);
                    GetTree().Quit(); _shotStep = 18;
                }
                break;
        }
    }

    static string Fmt(Singularity.Core.Policy.BudgetShares? b) => b is { } x ? $"{x.Research.ToDouble():F2}/{x.Capability.ToDouble():F2}/{x.Effort.ToDouble():F2}/{x.Procurement.ToDouble():F2}/{x.Quality.ToDouble():F2}" : "none";

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
