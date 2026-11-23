using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using Singularity.Core.Data;
using Singularity.Core.WhyNot;

namespace Singularity.Client.Spike;

/// <summary>Throwaway spike (TASK-0067): globe-sheet surface, ID-raster regions, CC0 objects, bench mode. Not simulation code.</summary>
public sealed partial class Main : Node3D
{
    static readonly Color PaperColor = Ui.Paper;

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
    // v0.5c: left rail of areas, area panels (live: the real ActorPanel in area mode; locked: a card), journal of signals
    Rail _rail; ActorPanel _areaPanel; AreaCard _areaCard; JournalPanel _journal;
    AreaId _area = AreaId.Map;
    bool _fatalLogged;
    // v0.5b: menus. The title screen runs over the live globe; "new game" starts the session with the chosen power and seed.
    MainMenu _mainMenu; NewGameSheet _newGame; SettingsSheet _settings; CreditsSheet _credits;
    Task<GameData> _dataTask;
    bool _titleMode, _pendingNewGame, _startedFromMenu;
    int _menuResume = -1;
    static bool s_openTitle;   // "to the main menu" reloads the scene and must not skip the menu again
    Label _loading, _help;
    Button _helpBtn;
    bool _debug;
    int[] _actorCountry;                 // actor index -> country id (0 = none)
    int _player, _viewed;                // the player's own power (launch parameter --power ID, default USA) and the power on show
    string _powerArg = "USA";
    float _tgtYaw, _tgtPitch; bool _flying;   // smooth turn to a clicked country; the zoom is kept
    int _clickCase = -1; double _clickT; string _clickLog = "";
    bool _shots; int _shotStep; double _shotT; string _shotDir = "../../shots-out";
    string _shotLog = ""; int _shotCand = -1; long _shotTick;

    // bench
    bool _shotDone; bool _bench; int _phase = -1; double _phaseT; readonly List<double> _frames = new();
    readonly StringBuilder _json = new();
    string _outDir = "bench-out";

    public override void _Ready()
    {
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        // Space is the pause key everywhere: no Godot control activates on it (Enter and keypad Enter still do), so a focused button never takes it.
        foreach (var ev in InputMap.ActionGetEvents("ui_accept"))
            if (ev is InputEventKey { Keycode: Key.Space } or InputEventKey { PhysicalKeycode: Key.Space }) InputMap.ActionEraseEvent("ui_accept", ev);
        _startup["engine_to_ready_ms"] = (long)Time.GetTicksMsec();
        var args = OS.GetCmdlineUserArgs();
        _bench = args.Contains("--bench");
        _shots = args.Contains("--shots");
        Settings.ReadOnly = _shots || _bench;
        Settings.Framed = args.Contains("--window");
        if (_shots || _bench)
        {
            // Frames are a real 1920x1080 render: a borderless window of the base size, so that no window manager decoration can clamp it (#424).
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, true);
            // --shot-size WxH: a native larger display (spec 4.3 item 6): map and tree frames only, the frame size is checked against the requested size.
            var sizeIdx = Array.IndexOf(args, "--shot-size");
            if (_shots && sizeIdx >= 0 && sizeIdx + 1 < args.Length && args[sizeIdx + 1].Split('x') is { Length: 2 } wh && int.TryParse(wh[0], out int lw) && int.TryParse(wh[1], out int lh))
            {
                // A display below the base cannot give a PASS (A1/A2): refuse it instead of scaling below 1 px/du.
                if (lw < SettingsLogic.BaseWidth || lh < SettingsLogic.BaseHeight) { GD.PrintErr($"--shot-size {lw}x{lh}: both dimensions must be at least {SettingsLogic.BaseWidth}x{SettingsLogic.BaseHeight}"); GetTree().Quit(2); return; }
                _largeSize = new Vector2I(lw, lh);
            }
            else if (_shots && sizeIdx >= 0) { GD.PrintErr("--shot-size expects WxH, for example 2560x1440"); GetTree().Quit(2); return; }
            DisplayServer.WindowSetSize(_largeSize.X > 0 ? _largeSize : new Vector2I(SettingsLogic.BaseWidth, SettingsLogic.BaseHeight));
            // A window of the full screen size cannot be had from a window manager that keeps a panel (it clamps the height): a run whose frame size equals the screen uses fullscreen, where the render is the screen.
            // A larger screen takes the borderless window; a smaller one fails at the first full frame with the sizes in the message.
            var target = _largeSize.X > 0 ? _largeSize : new Vector2I(SettingsLogic.BaseWidth, SettingsLogic.BaseHeight);
            if (target == DisplayServer.ScreenGetSize()) DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
        }
        var shotIdx = Array.IndexOf(args, "--shot-dir");
        if (shotIdx >= 0 && shotIdx + 1 < args.Length) _shotDir = args[shotIdx + 1];
        if (_bench) DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        var outIdx = Array.IndexOf(args, "--out");
        if (outIdx >= 0 && outIdx + 1 < args.Length) _outDir = args[outIdx + 1];

        Fonts.Load();
        Loc.Load();
        if (!_shots && !_bench) Settings.Load();   // screenshot runs use the defaults, so that they are reproducible
        Loc.SetLang(Settings.Current.Lang);
        var langIdx = Array.IndexOf(args, "--lang");
        if (langIdx >= 0 && langIdx + 1 < args.Length) Loc.SetLang(args[langIdx + 1]);
        Settings.Apply(Settings.Current, GetWindow(), !_shots && !_bench);
        _debug = args.Contains("--debug");
        var pwIdx = Array.IndexOf(args, "--power");
        if (pwIdx >= 0 && pwIdx + 1 < args.Length) _powerArg = args[pwIdx + 1];
        // --power ID is the service shortcut (ui-tester, --shots): it skips the menu and starts that power at once.
        _titleMode = (!_shots && !_bench && pwIdx < 0) || s_openTitle;
        s_openTitle = false;

        var sky = new Sky { SkyMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/stars.gdshader") } };
        var env = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Sky, Sky = sky, AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.Black };
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
        _hud.AddThemeColorOverride("font_color", Ui.Ink);
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
        else
        {
            BuildMenus(layer);
            if (_shots && _largeSize.X == 0) { _coldShot = true; _mainMenu.Open(false); }   // the title shots are taken on a cold start, before any HUD exists
            if (_titleMode)
            {
                _dataTask = Task.Run(SimHost.LoadData);
                _mainMenu.Open(false);
            }
            else BuildClient(layer, 7, null);
        }
    }

    public override void _Notification(int what)
    {
        // "Sound in background" off: the master bus is muted while the window is not in focus.
        if (what == NotificationApplicationFocusOut && !Settings.Current.SoundInBackground) AudioServer.SetBusMute(0, true);
        else if (what == NotificationApplicationFocusIn) AudioServer.SetBusMute(0, false);
    }

    public override void _ExitTree() => _host?.Dispose();

    // ---------- client ----------
    void BuildMenus(CanvasLayer layer)
    {
        _mainMenu = new MainMenu(); _newGame = new NewGameSheet(); _settings = new SettingsSheet(); _credits = new CreditsSheet();
        layer.AddChild(_mainMenu); layer.AddChild(_newGame); layer.AddChild(_settings); layer.AddChild(_credits);
        _mainMenu.Continue += CloseGameMenu;
        _mainMenu.NewGame += () => { _mainMenu.Close(); _pendingNewGame = true; };
        _mainMenu.OpenSettings += () => { _mainMenu.Close(); _settings.Open(); };
        _mainMenu.OpenCredits += () => { _mainMenu.Close(); _credits.Open(); };
        _credits.Closed += () => _mainMenu.Open(!_titleMode);
        _mainMenu.Quit += () => GetTree().Quit();
        _mainMenu.ToMain += () => { _host?.Dispose(); _host = null; s_openTitle = true; GetTree().ReloadCurrentScene(); };
        _newGame.Back += () => { _newGame.Close(); _mainMenu.Open(false); };
        _newGame.Start += StartGame;
        _newGame.Previewed += PreviewPower;
        _settings.Closed += () => _mainMenu.Open(!_titleMode);
        _settings.Applied += s =>
        {
            if (_rail != null && _rail.Wide != s.RailWide) { _rail.SetWide(s.RailWide); Recenter(); }
            if (_audio == null) return;
            // Not persisted here: the sheet saves the file after «keep» (or at once when no confirm is needed).
            _audio.SetVolume(Bus.Music, s.Volume[1] / 100, false); _audio.SetVolume(Bus.Ambient, s.Volume[2] / 100, false);
            _audio.SetVolume(Bus.UI, s.Volume[3] / 100, false); _audio.SetVolume(Bus.Alert, s.Volume[5] / 100, false);
        };
    }

    void CloseGameMenu()
    {
        _mainMenu.Close(); GetViewport().GuiReleaseFocus();
        if (_menuResume > 0) _signal.SetSpeed(_menuResume);
        _menuResume = -1;
    }

    void OpenGameMenu()
    {
        if (_menuResume >= 0) return;
        _menuResume = _host.Speed; _signal.SetSpeed(0);
        _mainMenu.Open(true);
    }

    bool MenuOpen => _newGame.Visible || _settings.Visible || _credits.Visible || (_mainMenu.Visible && _mainMenu.InGame);

    // New game: the world starts with the chosen power and seed (the same path as --power ID).
    void StartGame(string actorId, int seed)
    {
        _newGame.Close(); _mainMenu.Close(); GetViewport().GuiReleaseFocus();
        _titleMode = false; _startedFromMenu = true; _powerArg = actorId;
        _selected = 0; ApplySelection();
        BuildClient(_hud.GetParent<CanvasLayer>(), seed, _dataTask.Result);
    }

    // The globe turns to the selected power while the "new game" sheet is open.
    void PreviewPower(string actorId)
    {
        if (!Names.CountryOfActor.TryGetValue(actorId, out var cn)) return;
        var c = _data.Countries.First(x => x.Name == cn);
        _data.ComputeLabelPoint(c);
        _selected = c.Id; ApplySelection();
        _tgtYaw = c.LabelPoint.X; _tgtPitch = Mathf.Clamp(c.LabelPoint.Y, -75f, 75f); _flying = true;
    }

    void BuildClient(CanvasLayer layer, int seed, GameData data)
    {
        _host = new SimHost(seed, data);
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
        _player = Array.IndexOf(ids, _powerArg);
        if (_player < 0)
        {
            GD.PushWarning($"--power {_powerArg}: unknown power id, falling back to USA");
            _player = Math.Max(0, Array.IndexOf(ids, "USA"));
        }
        _viewed = _player;
        ApplySelection();
        _signal = new SignalField(_host, _player);
        _signal.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _signal.Debug = _debug;
        layer.AddChild(_signal);

        _audio = new AudioDirector(_host);
        AddChild(_audio);
        _signal.SignalChanged += _audio.OnSignal;
        _signal.JournalPressed += ToggleJournal;
        _signal.SignalChanged += (key, on) => { if (_host.Snap != null) _journal.Add(Settings.Date(_host.Snap.Date), key, on); };
        _signal.MenuPressed += OpenGameMenu;
        _signal.SignalChanged += AlertMarker;
        _settings.TestBus = bus => { if (bus == 5) _audio.Cue("signal_attention"); else _audio.Tick(); };
        _settings.MusicAmount = () => _audio.MusicAmount; _settings.MusicAmountSet = i => _audio.MusicAmount = i; _settings.ResetFatigue = _audio.ResetFatigue;

        _rail = new Rail { AnchorTop = 0, AnchorBottom = 1, AnchorLeft = 0, AnchorRight = 0, OffsetLeft = 0, OffsetBottom = 0 };
        _rail.SetWide(Settings.Current.RailWide);
        _rail.Pressed += (id, fromKeyboard) => OpenArea(id, fromKeyboard);
        _rail.ToggleWide += ToggleRail;
        layer.AddChild(_rail);
        _areaPanel = new ActorPanel(_host, true) { Visible = false, Player = _player };
        _areaPanel.OpenTree += () => OpenTree(0);
        layer.AddChild(_areaPanel);
        _areaCard = new AreaCard();
        layer.AddChild(_areaCard);
        _journal = new JournalPanel { AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -JournalPanel.PanelWidth - 10, OffsetRight = -10 };
        _journal.Closed += () => _journal.Visible = false;
        layer.AddChild(_journal);

        // Inset sized by its content: anchored top-right, no bottom anchor.
        _actorPanel = new ActorPanel(_host) { Visible = false, Debug = _debug, Player = _player };
        _actorPanel.AnchorLeft = 1; _actorPanel.AnchorRight = 1;
        _actorPanel.OffsetLeft = -540; _actorPanel.OffsetRight = -10; _actorPanel.OffsetTop = 56;
        _actorPanel.Closed += () => _actorPanel.Visible = false;
        _actorPanel.TintOf = i => _actorCountry[i] != 0 ? Ui.Tints[_data.Countries[_actorCountry[i] - 1].Tint] : Ui.Paper;
        _actorPanel.BackToOwn += () => ShowPower(_player, true);
        _actorPanel.OpenTree += () => OpenTree(0);
        layer.AddChild(_actorPanel);
        layer.MoveChild(_journal, -1);   // the journal slides over the foreign-power panel

        _tree = new TechTree(_host) { Visible = false, Player = _player, ShotRecord = _shots };
        _tree.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _tree.OffsetTop = 44;
        _tree.Closed += () => _tree.Visible = false;
        layer.AddChild(_tree);
        BuildHelp(layer);
        _loading.Visible = false;
        _tintsReady = true;
        foreach (Control c in new Control[] { _mainMenu, _newGame, _settings, _credits }) layer.MoveChild(c, -1);   // menus stay above the panels
        ShowPower(_player, _startedFromMenu);   // the player's own power has no right panel: the map shows, the areas open from the rail
    }

    // Alerts of the player's own power are also plates on the globe: halo, outline, sign (#326). Stacked below each other at the country.
    static readonly string[] AlertKeys = { "sig.alloy", "sig.licence", "sig.refusal", "sig.fail" };
    void AlertMarker(string key, bool on)
    {
        int idx = Array.IndexOf(AlertKeys, key);
        if (idx < 0) return;
        if (!on || _actorCountry[_player] == 0) { _overlay.RemoveMarker(key); return; }
        var ll = _data.Countries[_actorCountry[_player] - 1].LabelPoint;
        _overlay.SetMarker(key, new Vector2(ll.X, ll.Y), idx < 2, key, idx);
    }

    void BuildHelp(CanvasLayer layer)
    {
        _help = Ui.Lbl("help.text", 14, Ui.Ink2);
        _help.AddThemeStyleboxOverride("normal", Ui.Box(Ui.Sheet, Ui.Ink, 1, 10, Ui.R3));
        _help.Position = new Vector2(12, 0); _help.AnchorTop = 1; _help.AnchorBottom = 1; _help.OffsetTop = -150; _help.OffsetBottom = -50; _help.OffsetLeft = 12;
        _help.Visible = false;
        layer.AddChild(_help);
        _helpBtn = Ui.Btn("?", () => _help.Visible = !_help.Visible);
        _helpBtn.TooltipText = "tip.help";
        _helpBtn.AnchorTop = 1; _helpBtn.AnchorBottom = 1; _helpBtn.OffsetTop = -42; _helpBtn.OffsetBottom = -10; _helpBtn.OffsetLeft = 12; _helpBtn.OffsetRight = 48;
        layer.AddChild(_helpBtn);
    }

    void OpenTree(int mode) { _audio?.Tick(); _tree.ShowFor(_viewed, mode); }

    // A foreign power opens in the right panel (read-only); the player's own power is the area panels on the left. Only one of them is open at a time
    // (one why_not target in the Session snapshot). On a click the globe turns to the power; the zoom is not touched.
    void ShowPower(int i, bool turn)
    {
        _viewed = i;
        _host.WhyActor = i;
        if (i == _player) _actorPanel.Visible = false;
        else { CloseArea(); _actorPanel.ShowActor(i); }
        if (_tree.Visible) _tree.ShowFor(i, 0);
        if (!turn) return;
        _audio?.Tick();
        FlyTo(i);
    }

    void FlyTo(int i)
    {
        if (_actorCountry[i] == 0) return;
        _selected = _actorCountry[i]; ApplySelection();
        var ll = _data.Countries[_selected - 1].LabelPoint;
        // The country goes to the middle of the free area beside the open panel and the rail (spec section 9), the zoom is kept.
        (_tgtYaw, _tgtPitch) = PlanisphereMath.CameraForShift(ll.X, ll.Y, _dist, _cam.Fov, GetViewport().GetVisibleRect().Size.Y, PanelShift());
        _tgtPitch = Mathf.Clamp(_tgtPitch, -75f, 75f);
        _flying = true;
    }

    // The free area changed (an area opened or closed, the rail changed width): the selected own country moves to its middle again.
    void Recenter() { if (_tintsReady && _actorCountry != null && _actorCountry[_viewed] != 0 && _selected == _actorCountry[_viewed]) FlyTo(_viewed); }

    // fromKeyboard: a hotkey or Enter on the rail button. Then the focus follows into the live panel (it is its own Tab scope, so Tab alone could not enter it); a mouse opening keeps the focus as it is.
    void OpenArea(AreaId id, bool fromKeyboard = false)
    {
        if (id == AreaId.Map) { CloseArea(); Recenter(); return; }
        var def = AreaData.Of(id);
        if (_viewed != _player) ShowPower(_player, false);
        _audio?.Tick();
        _area = id; _rail.Select(id);
        _host.WhyActor = _player;
        if (def.State == AreaState.Locked) { _areaPanel.Visible = false; _areaCard.ShowArea(def); }
        else { _areaCard.Visible = false; _areaPanel.Area = id; _areaPanel.ShowActor(_player); if (fromKeyboard) _areaPanel.FocusFirstWhenReady(); else _areaPanel.CancelFocusWait(); }
        Recenter();
    }

    void CloseArea()
    {
        _areaPanel?.CancelFocusWait();
        var closed = _area; bool focusInside = _areaPanel != null && _areaPanel.HasFocusInside();
        _area = AreaId.Map; _rail?.Select(AreaId.Map);
        if (_areaPanel != null) _areaPanel.Visible = false;
        if (_areaCard != null) _areaCard.Visible = false;
        if (focusInside && closed != AreaId.Map) _rail?.FocusArea(closed);   // Esc from the panel returns the focus to the button of that area
    }

    void ToggleRail()
    {
        Settings.Current.RailWide = !Settings.Current.RailWide; Settings.Save();
        _rail.SetWide(Settings.Current.RailWide); Recenter();
    }

    void ToggleJournal() { _audio?.Tick(); _journal.Visible = !_journal.Visible; }

    /// <summary>Signed offset of the middle of the free area from the screen centre: positive when a right panel takes room, negative for the rail and an area panel on the left.</summary>
    float PanelShift()
    {
        if (_actorPanel != null && _actorPanel.Visible) return (Math.Max(_actorPanel.Size.X, _actorPanel.CustomMinimumSize.X) + 10) / 2f;
        if (_rail == null) return 0f;
        float left = _rail.RailWidth;
        if (_area != AreaId.Map) left += 8 + (_areaPanel.Visible ? Math.Max(_areaPanel.Size.X, _areaPanel.CustomMinimumSize.X) : _areaCard.CustomMinimumSize.X) + 8;
        return -left / 2f;
    }

    void FlyStep(double dt)
    {
        if (!_flying) return;
        float k = PlanisphereMath.EaseStep(dt, 7f);
        _yaw += PlanisphereMath.ShortestTurn(_yaw, _tgtYaw) * k;
        _pitch += (_tgtPitch - _pitch) * k;
        if (Mathf.Abs(PlanisphereMath.ShortestTurn(_yaw, _tgtYaw)) < 0.02f && Mathf.Abs(_tgtPitch - _pitch) < 0.02f) { _yaw = _tgtYaw; _pitch = _tgtPitch; _flying = false; }
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
        // The shader works in linear light: the hex tokens of palette C are converted, so that the lit fills match the validator's model.
        var tints = Palette.C.Tints.Select(h => { var c = new Color(h).SrgbToLinear(); return new Vector3(c.R, c.G, c.B); }).ToArray();
        _globeMat.SetShaderParameter("tints", tints);
        _globeMat.SetShaderParameter("sea_color", new Color(Palette.C.Sea));
        _globeMat.SetShaderParameter("grid_color", new Color(Palette.GlobeGrid));
        var pl = Ui.Paper.SrgbToLinear();
        _globeMat.SetShaderParameter("paper", new Vector3(pl.R, pl.G, pl.B));
        AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = _globeMat });
    }

    int TintOfActor(string actorId) =>
        Names.CountryOfActor.TryGetValue(actorId, out var cn) ? Math.Max(0, _data.Countries.First(c => c.Name == cn).Tint) : 0;

    void ApplySelection()
    {
        _globeMat.SetShaderParameter("selected_id", _selected);
        _overlay.Emphasize(_selected, _actorCountry != null && _player >= 0 && _player < _actorCountry.Length ? _actorCountry[_player] : -1);
    }

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
        if (_bench || (_tree != null && _tree.Visible && e is InputEventMouse) || (_mainMenu != null && MenuOpen)) return;
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed) { _pressed = true; _dragged = false; _pressPos = mb.Position; _flying = false; }
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
        else if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } && _tintsReady) OnEscape();
        else if (e is InputEventKey ak && ak.Pressed && !ak.Echo && _tintsReady && AreaKey(ak)) { }
        else if (e is InputEventKey sk && sk.Pressed && !sk.Echo && _tintsReady && sk.Keycode is Key.Space or Key.P or Key.Bracketleft or Key.Bracketright or Key.Key1 or Key.Key2 or Key.Key3 or Key.L)
        {
            if (sk.Keycode is Key.Space or Key.P) _signal.TogglePause();
            else if (sk.Keycode == Key.Bracketleft) _signal.StepSpeed(-1);
            else if (sk.Keycode == Key.Bracketright) _signal.StepSpeed(1);
            else if (sk.Keycode == Key.L) { if (_tree.Visible && _tree.CanLegend) _tree.ToggleLegend(!_tree.LegendOpen); }
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

    // Hotkeys of the rail (by Keycode, not by the typed character): T E F M I G J open an area, the backslash switches the rail width, the backquote opens the journal.
    // T inside Science opens the tree, T in the tree closes it. Returns true when the key was taken.
    bool AreaKey(InputEventKey k)
    {
        if (k.CtrlPressed || k.AltPressed || k.MetaPressed) return false;
        if (k.Keycode == Key.Backslash || k.PhysicalKeycode == Key.Backslash) { ToggleRail(); return true; }
        if (k.Keycode == Key.Quoteleft || k.PhysicalKeycode == Key.Quoteleft) { ToggleJournal(); return true; }
        var code = k.Keycode is >= Key.A and <= Key.Z ? k.Keycode : k.PhysicalKeycode;   // a non-Latin layout reports the physical Latin key
        var def = code is >= Key.A and <= Key.Z ? AreaData.ByLetter((char)code) : null;
        if (def == null) return false;
        if (_tree.Visible) { if (def.Id == AreaId.Science) _tree.Visible = false; return true; }
        if (def.Id == AreaId.Science && _area == AreaId.Science) OpenTree(0); else OpenArea(def.Id, true);
        return true;
    }

    // Esc: closes the legend, the tree, the journal, the open area (back to the map); a foreign power returns to the player's own; otherwise the game menu.
    void OnEscape()
    {
        if (_tree.Visible) { if (_tree.LegendOpen) _tree.ToggleLegend(false); else _tree.Visible = false; }
        else if (_help.Visible) _help.Visible = false;
        else if (_journal.Visible) _journal.Visible = false;
        else if (_area != AreaId.Map) { CloseArea(); Recenter(); }
        else if (_viewed != _player) ShowPower(_player, true);
        else OpenGameMenu();
    }

    void Pick(Vector2 screen)
    {
        if (_titleMode) return;
        var o = _cam.ProjectRayOrigin(screen); var d = _cam.ProjectRayNormal(screen);
        float b = o.Dot(d), c = o.Dot(o) - 1f, disc = b * b - c;
        if (disc < 0) { _selected = 0; ApplySelection(); return; }
        var hit = o + d * (-b - Mathf.Sqrt(disc));
        var ll = GlobeData.ToLonLat(hit);
        int id = _data.IdAt(ll.X, ll.Y);
        if (_tintsReady && id != 0)
        {
            int a = Array.IndexOf(_actorCountry, id);
            if (a >= 0) { ShowPower(a, true); return; }
        }
        _selected = id;
        ApplySelection();
    }

    // ---------- per-frame ----------
    public override void _Process(double delta)
    {
        _t += delta;
        if (_titleMode)
        {
            // Title screen: the globe turns slowly until a power is picked in "new game".
            if (!_flying && !_pressed && !(_newGame.Visible && _newGame.Selected != null)) _yaw += (float)(4 * delta);
            if (_pendingNewGame && _dataTask.IsCompleted)
            {
                _pendingNewGame = false;
                if (_dataTask.IsFaulted) { GD.PrintErr(_dataTask.Exception); _mainMenu.Open(false); }
                else _newGame.Open(_dataTask.Result.Scenario(Singularity.Session.SliceRunConfig.DefaultScenario), id => Ui.Tints[TintOfActor(id)]);
            }
        }
        if (_coldShot) ColdTitleShots(delta);
        if (_host != null && !_tintsReady)
        {
            if (_host.Fatal != null)
            {
                _loading.Text = Loc.T("ui.simerror") + _host.Fatal;
                if (!_fatalLogged) { _fatalLogged = true; GD.PrintErr(_host.Fatal); }   // once, not on every frame
            }
            else if (_host.Ready && !_coldShot) FinishClient();
            else { _loading.Text = Loc.T(_host.Status); _loading.Visible = !_coldShot; }
        }
        if (_helpBtn != null) { _helpBtn.Visible = !_tree.Visible && !_areaPanel.Visible && _titleHidden.Count == 0; if (_tree.Visible || _areaPanel.Visible || _titleHidden.Count > 0) _help.Visible = false; }   // never over an area panel; F1 still opens the help
        if (_rail != null) LayoutClient();
        if (_shots && _tintsReady) ShotStep(delta);
        if (_bench) BenchStep(delta);
        FlyStep(delta);
        UpdateCamera();
        if (_audio != null) { _audio.Actor = _player; _audio.CameraMoved(new Vector2(_yaw + _pitch * 1.7f, _dist)); }
        AnimateObjects();
        if (_debug)
        {
            string sel = _selected == 0 ? "-" : _data.Countries[_selected - 1].Name;
            _hud.Text = $"FPS {Engine.GetFramesPerSecond()}  draw calls {(long)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)}  labels {_overlay.VisibleLabels}\n" +
                        $"region: {sel}  (factory, aircraft: spike mock)" + (_host?.Snap is { JobError: not "" } js ? "\njob error: " + js.JobError : "");
        }
    }

    // Rail, area panels and the journal sit under the top bar, whose height follows its content; the help button clears the rail.
    void LayoutClient()
    {
        float top = _signal.Size.Y, left = _rail.RailWidth;
        _rail.OffsetTop = top; _rail.OffsetRight = left;
        foreach (Control c in new Control[] { _areaPanel, _areaCard })
        {
            c.AnchorLeft = c.AnchorRight = 0; c.OffsetLeft = left + 8; c.OffsetTop = top + 8; c.OffsetRight = left + 8 + c.CustomMinimumSize.X;
        }
        _journal.OffsetTop = top + 8;
        _helpBtn.OffsetLeft = left + 8; _helpBtn.OffsetRight = left + 44; _help.OffsetLeft = left + 8;
        // Badges: the active signals of an area, with the worst severity; locked areas never get one.
        var count = new int[AreaData.All.Length]; var sev = new int[AreaData.All.Length];
        foreach (var key in AlertKeys)
            if (_signal.Active(key) && AreaData.OfSignal(key) is { } a) { count[(int)a.Area]++; sev[(int)a.Area] = Math.Max(sev[(int)a.Area], a.Severity); }
        if (_pinBadges != null)   // shot run: fixed badges instead of the live signals
        {
            Array.Clear(count); Array.Clear(sev);
            foreach (var (id, n, v) in _pinBadges) { count[(int)id] = n; sev[(int)id] = v; }
        }
        foreach (var d in AreaData.All) if (d.Id != AreaId.Map) _rail.SetBadge(d.Id, count[(int)d.Id], sev[(int)d.Id]);
    }
    (AreaId Id, int Count, int Sev)[] _pinBadges;

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
    bool _coldShot; double _coldT; int _coldStep;

    // A10 on the credits sheet: every link zone is at least 24 du high and two zones never come closer than 4 du (spec: no accidental neighbour hit).
    bool CheckCreditsLinks(string sfx)
    {
        var z = _credits.ShotLinkZones();
        if (z.Count == 0) { InstFail($"credits link check {sfx}: no links found"); return false; }
        for (int a = 0; a < z.Count; a++)
        {
            if (z[a].Size.Y < 24 - 0.01f) { InstFail($"credits link check {sfx}: link {a} is {z[a].Size.Y} du high, minimum 24"); return false; }
            for (int b = a + 1; b < z.Count; b++)
                if (z[a].Grow(3.99f).Intersects(z[b])) { InstFail($"credits link check {sfx}: links {a} and {b} are closer than 4 du ({z[a]} / {z[b]})"); return false; }
        }
        _shotLog += $"credits links {sfx}: {z.Count} zones, all >= 24 du high, gaps >= 4 du\n";
        return true;
    }

    // Physical pixels per canvas unit now (base scale times the UI setting).
    float PxPerDu() => DisplayServer.WindowGetSize().X / GetViewport().GetVisibleRect().Size.X;

    readonly HashSet<string> _savedShots = new();
    static readonly System.Text.RegularExpressions.Regex SizedShot = new(@"^(.*)_1920x1080(?:_s(\d+))?(_en)?\.png$");

    // Category C: every screen of the matrix that is shot at 100 % is shot at 125 % and 150 % too (the same language); a missing one fails the run.
    void CheckScaleCoverage()
    {
        if (_largeSize.X > 0) return;
        var sets = new Dictionary<string, HashSet<string>>();
        foreach (var n in _savedShots)
            if (SizedShot.Match(n) is { Success: true } m) { string sc = m.Groups[2].Success ? m.Groups[2].Value : "100"; if (!sets.TryGetValue(sc, out var hs)) sets[sc] = hs = new(); hs.Add(m.Groups[1].Value + m.Groups[3].Value); }
        if (!sets.TryGetValue("100", out var at100)) { _shotLog += "scale coverage: FAIL no 100 % frames in the matrix\n"; _shotFailed = true; return; }
        foreach (var sc in new[] { "125", "150" })
        {
            var miss = at100.Where(x => !(sets.TryGetValue(sc, out var o) && o.Contains(x))).OrderBy(x => x, StringComparer.Ordinal).ToList();
            _shotLog += $"scale coverage {sc} %: {at100.Count} screens at 100 %, missing {miss.Count}{(miss.Count > 0 ? " (" + string.Join(", ", miss) + ")" : "")}: {(miss.Count == 0 ? "PASS" : "FAIL")}\n";
            if (miss.Count > 0) _shotFailed = true;
        }
    }

    // The scale really applied: the observed px/du must match the policy for the UI setting (a setting that did not take effect fails the run).
    void CheckScaleApplied(string sfx, int scale)
    {
        var w = DisplayServer.WindowGetSize(); float obs = PxPerDu();
        bool ok = SettingsLogic.ScaleApplied(obs, w.X, w.Y, scale);
        _shotLog += $"scale applied {sfx}: observed {obs:F3} px/du, expected {SettingsLogic.PixelsPerDu(w.X, w.Y, scale):F3}: {(ok ? "PASS" : "FAIL")}\n";
        if (!ok) InstFail($"scale {scale} % not applied at {sfx}: observed {obs:F3} px/du");
    }

    // Title screen as in the game: only the globe and the menu, the HUD hidden (restored on exit).
    readonly List<(CanvasItem Node, bool Was)> _titleHidden = new();
    void TitleScene(bool on)
    {
        if (on)
        {
            foreach (var ch in _hud.GetParent<CanvasLayer>().GetChildren())
                if (ch is CanvasItem ci && ci != _overlay && ci != _mainMenu && ci != _newGame && ci != _settings && ci != _credits) { _titleHidden.Add((ci, ci.Visible)); ci.Visible = false; }
            return;
        }
        foreach (var (n, was) in _titleHidden) n.Visible = was;
        _titleHidden.Clear();
    }

    // A failed PNG write fails the run: a missing frame is not evidence.
    // A write that throws (disk full, no directory) is a failed save, not a crash before the run returns exit 1.
    Error TrySave(Image img, string path) { try { return img.SavePng(path); } catch (Exception ex) { _shotFailed = true; _shotLog += $"ERROR: save {path} threw {ex.GetType().Name}: {ex.Message}\n"; return Error.Failed; } }
    void CheckSave(Error e, string path) { if (e != Error.Ok) InstFail($"cannot save {path}: {e}"); }

    void SaveShot(string name, Rect2I? r = null, bool grey = false)
    {
        string dir = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), _shotDir));
        Directory.CreateDirectory(dir);
        var img = GetViewport().GetTexture().GetImage();
        if (r is Rect2I rr) img = img.GetRegion(rr);
        else if (_largeSize.X > 0 ? (img.GetWidth() != _largeSize.X || img.GetHeight() != _largeSize.Y) : !SettingsLogic.IsBaseFrame(img.GetWidth(), img.GetHeight())) { InstFail($"frame {name} is {img.GetWidth()}x{img.GetHeight()}, a full frame must be a real {(_largeSize.X > 0 ? _largeSize.X + "x" + _largeSize.Y : "1920x1080")} render (window {DisplayServer.WindowGetSize().X}x{DisplayServer.WindowGetSize().Y}, display {DisplayServer.ScreenGetSize().X}x{DisplayServer.ScreenGetSize().Y})"); return; }
        _savedShots.Add(name);
        CheckSave(TrySave(img, Path.Combine(dir, name)), Path.Combine(dir, name));
        if (grey) { string gp = Path.Combine(dir, name.Replace(".png", "_grey.png")); img.Convert(Image.Format.L8); CheckSave(TrySave(img, gp), gp); }   // A5: a greyscale frame next to the colour one
    }

    // Title screen on a cold start: the menu over the turning globe, no HUD (the loading label is hidden too).
    void ColdTitleShots(double dt)
    {
        _coldT += dt; _yaw += (float)(4 * dt);
        if (_loading != null) _loading.Visible = false;
        if (_coldStep == 0 && _coldT >= 1.0) { SaveShot("main_menu.png"); Loc.SetLang("en"); _coldStep = 1; _coldT = 0; }
        else if (_coldStep == 1 && _coldT >= 0.4) { SaveShot("main_menu_en.png"); _mainMenu.Close(); _credits.Open(); _coldStep = 2; _coldT = 0; }
        else if (_coldStep == 2 && _coldT >= 0.5) { SaveShot("credits_en.png"); Loc.SetLang("ru"); _coldStep = 3; _coldT = 0; }
        else if (_coldStep == 3 && _coldT >= 0.4) { SaveShot("credits_ru.png"); _credits.Visible = false; _coldShot = false; if (_loading != null) _loading.Visible = true; }
    }

    // Key screens at the base size in RU and EN, at 100 % and 150 % UI scale (spec §10; #424/#425): a queue of (wait before, action).
    // The window is a real 1920x1080 render (SaveShot asserts it); other window sizes are not evidence and are not shot.
    readonly List<(double Wait, Action Act)> _sizeQ = new(); int _sizeI;

    int _capRow;
    Vector2I _largeSize;   // non-zero: the native-size run (map and tree frames at this window size)
    float _discClosed; Vector2 _panBefore;

    // Native larger display: the window is asked for _largeSize; if the render is another size SaveShot rejects the frame and the run fails (no faked size).
    void BuildLargeShots()
    {
        _sizeI = 0; _sizeQ.Clear();
        string sfx = $"_{_largeSize.X}x{_largeSize.Y}";
        void Q(double wait, Action act) => _sizeQ.Add((wait, act));
        Q(0.5, () => { var cv = GetViewport().GetVisibleRect().Size; FitCheck(SettingsLogic.ScaleFloorOk(DisplayServer.WindowGetSize().X, DisplayServer.WindowGetSize().Y, 100)); CheckScaleApplied(sfx, 100); _shotLog += $"large display {sfx}: asked {_largeSize.X}x{_largeSize.Y}, window {DisplayServer.WindowGetSize().X}x{DisplayServer.WindowGetSize().Y}, screen {DisplayServer.ScreenGetSize().X}x{DisplayServer.ScreenGetSize().Y}, canvas {cv.X}x{cv.Y} du, content scale {GetWindow().ContentScaleMode} {GetWindow().ContentScaleAspect}\n"; _host.WhyActor = _player; ShowPower(_player, false); });
        Q(1.5, () => { SaveShot($"map{sfx}.png"); OpenArea(AreaId.Finance); });
        Q(0.8, () => { SaveShot($"area_finance{sfx}.png"); CloseArea(); OpenTree(0); });
        Q(1.5, () =>
        {
            string sm = _tree.ShotSummaryCheck(true, out int nIn);
            _shotLog += $"tree summaries {sfx} (base view): {nIn} of 5 totals fully inside the chart area: {(sm == null && nIn == 5 ? "PASS" : "FAIL " + sm)}\n"; if (sm != null || nIn != 5) InstFail($"tree summaries {sfx}: {sm}");
            SaveShot($"tree_nolegend{sfx}.png"); _discClosed = _tree.DiscDiameter; _tree.ToggleLegend(true);
        });
        Q(0.6, () =>
        {
            string ov = _tree.ShotOverlap(); string fit = _tree.ShotLegendFit(); float d = _tree.DiscDiameter;
            bool ok = ov == null && fit == null && Math.Abs(d - _discClosed) < 0.01f;
            _shotLog += $"tree legend {sfx}: disc {d:F1} du (closed {_discClosed:F1}), overlap {ov ?? "none"}, legend {fit ?? "clear"}: {(ok ? "PASS" : "FAIL")}\n"; if (!ok) InstFail($"tree legend {sfx}");
            SaveShot($"tree_legend{sfx}.png"); _tree.ToggleLegend(false); _tree.Visible = false;
        });
    }
    void BuildSizeShots()
    {
        _sizeI = 0; _sizeQ.Clear();
        var combos = new (int Scale, string Lang)[] { (100, "ru"), (100, "en"), (125, "ru"), (125, "en"), (150, "ru"), (150, "en") };
        foreach (var (scale, lang) in combos)
        {
            string sfx = "_1920x1080" + (scale == 150 ? "_s150" : scale == 125 ? "_s125" : "") + (lang == "en" ? "_en" : "");
            void Q(double wait, Action act) => _sizeQ.Add((wait, act));
            Q(0.1, () => { Settings.Current.UiScale = scale; Settings.Apply(Settings.Current, GetWindow(), false); Loc.SetLang(lang); });
            Q(1.0, () => { var cv = GetViewport().GetVisibleRect().Size; _shotLog += $"size shots {sfx}: window {DisplayServer.WindowGetSize().X}x{DisplayServer.WindowGetSize().Y}, canvas {cv.X}x{cv.Y} du, scale {scale} %, content scale {GetWindow().ContentScaleSize.X}x{GetWindow().ContentScaleSize.Y} {GetWindow().ContentScaleMode} {GetWindow().ContentScaleAspect}\n"; FitCheck(SettingsLogic.ScaleFloorOk(DisplayServer.WindowGetSize().X, DisplayServer.WindowGetSize().Y, scale)); CheckScaleApplied(sfx, scale); _mainMenu.Open(true); });
            Q(0.5, () => { SaveShot($"menu_ingame{sfx}.png"); _mainMenu.Close(); TitleScene(true); _mainMenu.Open(false); });
            Q(0.5, () => { SaveShot($"main_menu{sfx}.png"); _mainMenu.Close(); TitleScene(false); _credits.Open(); });   // the real title screen (no HUD) at every scale

            Q(0.6, () => { SaveShot($"credits{sfx}.png"); CheckCreditsLinks(sfx); _credits.ShotScroll(100000); });
            Q(0.4, () => { SaveShot($"credits_end{sfx}.png"); _credits.Visible = false; _newGame.Open(_host.Data.Scenario(Singularity.Session.SliceRunConfig.DefaultScenario), id => Ui.Tints[TintOfActor(id)]); });
            Q(0.5, () => { SaveShot($"new_game_empty{sfx}.png"); _newGame.Select("FRA"); });
            Q(0.6, () => { SaveShot($"new_game_selected{sfx}.png"); _newGame.Close(); _settings.Open(); });
            Q(0.5, () => { SaveShot($"settings_video{sfx}.png"); _settings.ShotTab(1); });
            Q(0.4, () => { SaveShot($"settings_sound{sfx}.png"); _settings.ShotTab(2); });
            Q(0.4, () => { SaveShot($"settings_general{sfx}.png"); _settings.ShotTab(0); _settings.ShotRollback(); });
            Q(0.5, () => { SaveShot($"settings_rollback{sfx}.png"); _settings.Revert(false); _settings.Close(); Loc.SetLang(lang); AimAlert(1, false); });
            Q(0.8, () => { SaveShot($"alert_tint5{sfx}.png"); _overlay.RemoveMarker("demo0"); _overlay.RemoveMarker("demo1"); _overlay.RemoveMarker("demo2"); OpenArea(AreaId.Finance); });
            Q(0.8, () => { SaveShot($"area_finance{sfx}.png"); FitCheck(_rail.GetCombinedMinimumSize().Y <= _rail.Size.Y + 0.5f); _shotLog += $"rail {sfx}: fits without scroll {_rail.GetCombinedMinimumSize().Y <= _rail.Size.Y + 0.5f} (min {_rail.GetCombinedMinimumSize().Y:F0} of {_rail.Size.Y:F0})\n"; OpenArea(AreaId.Forces); });
            Q(0.6, () => { SaveShot($"area_forces{sfx}.png"); OpenArea(AreaId.Science); });
            Q(0.8, () => { SaveShot($"area_science{sfx}.png"); OpenArea(AreaId.Production); });
            Q(0.8, () => { SaveShot($"area_production{sfx}.png"); CloseArea(); if (Settings.Current.RailWide) ToggleRail(); _pinBadges = Array.Empty<(AreaId, int, int)>(); });
            // Level 0 keyboard check through the real route only: the Finance hotkey opens the panel and moves the focus into it, Tab and an arrow key move a share slider, Space still pauses, Esc returns to the rail button.
            if (scale == 100 && lang == "ru")
            {
                int levelBefore = InstrumentLogic.OrganLevel; int[] shareBefore = new int[5]; bool hotFocus = false; int slider = -1, speedBefore = 0;
                Q(0.6, () => { InstrumentLogic.OrganLevel = 0; CloseArea(); });
                Q(0.3, () => { PressKey(Key.F); PressKey(Key.F, false); });
                Q(0.6, () => { hotFocus = _areaPanel.HasFocusInside(); PressKey(Key.Tab); PressKey(Key.Tab, false); });
                Q(0.3, () => { slider = _areaPanel.ShotFocusedSlider; for (int k = 0; k < 5; k++) shareBefore[k] = _areaPanel.ShotShare(k); PressKey(Key.Right); PressKey(Key.Right, false); });
                Q(0.3, () =>
                {
                    bool changed = slider >= 0 && _areaPanel.ShotShare(slider) != shareBefore[slider];
                    bool ok = hotFocus && changed;
                    _shotLog += $"finance level 0 keyboard: hotkey F moved the focus into the panel {hotFocus}, Tab reached share slider {slider}, Right arrow changed it {(slider >= 0 ? shareBefore[slider] + " -> " + _areaPanel.ShotShare(slider) : "n/a")}: {(ok ? "PASS" : "FAIL")}\n";
                    if (!ok) InstFail("finance level 0 keyboard check failed (real route)");
                    speedBefore = _host.Speed; PressKey(Key.Space); PressKey(Key.Space, false);
                });
                Q(0.3, () =>
                {
                    bool paused = _host.Speed != speedBefore;
                    _shotLog += $"finance level 0 keyboard: Space with a slider focused toggled the pause {paused} ({speedBefore} -> {_host.Speed}): {(paused ? "PASS" : "FAIL")}\n";
                    if (!paused) InstFail("Space did not pause with a share slider focused");
                    PressKey(Key.Space); PressKey(Key.Space, false);
                });
                Q(0.3, () => { PressKey(Key.Escape); PressKey(Key.Escape, false); });
                Q(0.4, () =>
                {
                    bool back = _rail.HasAreaFocus(AreaId.Finance) && _host.Speed == speedBefore;
                    _shotLog += $"finance level 0 keyboard: Esc closed the panel and the focus is on the Finance rail button {_rail.HasAreaFocus(AreaId.Finance)}, pause restored {_host.Speed == speedBefore}: {(back ? "PASS" : "FAIL")}\n";
                    if (!back) InstFail("focus did not return to the rail button");
                    InstrumentLogic.OrganLevel = levelBefore; GetViewport().GuiReleaseFocus();
                });
            }
            // The goal statement with the legend flag left open in the chart view: the hidden sheet must reserve no space and the first Esc must close the tree (category D).
            Q(0.3, () => { OpenTree(1); _tree.ToggleLegend(true); });
            Q(0.8, () =>
            {
                bool ok = !_tree.LegendOpen && _tree.InsetLimit == _tree.Size.Y - 100;
                _shotLog += $"goal statement {sfx}: hidden legend open {_tree.LegendOpen}, goal panel limit {_tree.InsetLimit:F0} of {_tree.Size.Y - 100:F0} (no reserve): {(ok ? "PASS" : "FAIL")}\n"; if (!ok) InstFail($"goal statement reserves space for a hidden legend {sfx}");
                string clip = _tree.ShotClippedText();
                _shotLog += $"goal statement {sfx}: no clipped or ellipsized text: {(clip == null ? "PASS" : "FAIL " + clip)}\n"; if (clip != null) InstFail($"goal statement text is clipped {sfx}: {clip}");
                SaveShot($"tech_tree_b{sfx}.png"); PressKey(Key.Escape); PressKey(Key.Escape, false);
            });
            Q(0.3, () =>
            {
                bool ok = !_tree.Visible;
                _shotLog += $"goal statement {sfx}: first Esc closed the tree with a hidden legend flag: {(ok ? "PASS" : "FAIL")}\n"; if (!ok) InstFail($"Esc was consumed by a hidden legend {sfx}");
                _tree.ToggleLegend(false); _tree.Visible = false;
            });
            // The Icons level with all six node types in one view (FND, PRN, ENG, CMP, PRC, APP): the in-node glyphs the legend shows.
            string allTypes = null;
            Q(0.4, () => OpenTree(0));
            Q(0.8, () => { _tree.ShotProbe = true; allTypes = _tree.ShotZoomAllTypes(); });
            Q(0.9, () =>
            {
                _shotLog += $"tree zoom icons all types {sfx}: {(allTypes == null ? "PASS" : "FAIL " + allTypes)}\n"; if (allTypes != null) { InstFail($"icons view {sfx}: {allTypes}"); return; }
                string probe = allTypes == null ? ProbeTreeGlyphs() : null;
                _shotLog += $"tree glyph probe {sfx}: {(probe == null ? "PASS" : "FAIL " + probe)}\n"; if (probe != null) { InstFail($"glyph probe {sfx}: {probe}"); return; }
                SaveShot($"tree_zoom_icons_all{sfx}.png", null, scale == 100); _tree.ShotProbe = false; _tree.ResetView(); _tree.Visible = false;
            });
            // Captions the art director asked to see (TASK-0126): the nodes are centred at the Nodes level, the named ones selected, a frontier node left alone (its number tag or name).
            {   // every scale and language: the coverage rule wants each 100 % frame at 125 % and 150 % too
                Q(0.4, () => OpenTree(0));
                foreach (var (cap, id, num, sel) in new[] { ("turbine", "s7.fnd.turbine_heat_transfer", 0, true), ("jet_test", "s8.prc.jet_engine_assembly_test", 0, true), ("frontier", "", -1, false) })
                {
                    Q(0.3, () => { int row = id != "" ? _tree.RowOfId(id) : _tree.FrontierRow; _capRow = row; if (row < 0) { if (sel) InstFail($"caption shot {cap}: node not found"); else _shotLog += $"caption {cap}{sfx}: no frontier node in this state (no «№N» tag to show), no frame\n"; return; } _tree.Select(sel ? row : -1); _tree.ShotZoom(2.4f, row); _capRow = row; });
                    Q(0.8, () => { if (_capRow < 0) return; string t = _tree.ShotLabelText(_capRow); _shotLog += $"caption {cap}{sfx}: label '{t ?? "none"}'\n"; if (sel && t != Names.Node(id)) InstFail($"caption {cap}: expected the label '{Names.Node(id)}', drawn '{t ?? "none"}'"); SaveShot($"tree_caption_{cap}{sfx}.png"); });
                }
                Q(0.2, () => { _tree.Select(-1); _tree.ResetView(); _tree.Visible = false; });
            }
            // The rail in its states: all normal, then an alarm on Finance and a warning "9+" on Production, narrow and wide (spec task-0117-rail-and-legend §4).
            Q(0.5, () => { SaveShot($"rail_narrow_normal{sfx}.png"); RunT6($"rail narrow normal{sfx}", true); _pinBadges = new[] { (AreaId.Finance, 1, 2), (AreaId.Production, 10, 1) }; });
            Q(0.5, () => { SaveShot($"rail_narrow_badges{sfx}.png"); RunT6($"rail narrow badges{sfx}", true); LogCheck(_rail.BadgeFitLog()); _pinBadges = Array.Empty<(AreaId, int, int)>(); ToggleRail(); });
            // Wide, Map selected, no badge: "Esc" and the marker are apart.
            Q(0.6, () => { SaveShot($"rail_wide_normal{sfx}.png", null, scale == 100); RunT6($"rail wide normal{sfx}", true); _pinBadges = new[] { (AreaId.Finance, 1, 2), (AreaId.Production, 10, 1) }; });
            Q(0.5, () => { SaveShot($"rail_wide_badges{sfx}.png", null, scale == 100 && lang == "ru"); RunT6($"rail wide badges{sfx}", true); LogCheck(_rail.NameFitLog()); FitCheck(_rail.GetCombinedMinimumSize().Y <= _rail.Size.Y + 0.5f); _shotLog += $"rail wide {sfx}: fits without scroll {_rail.GetCombinedMinimumSize().Y <= _rail.Size.Y + 0.5f} (min {_rail.GetCombinedMinimumSize().Y:F0} of {_rail.Size.Y:F0}, rail {_rail.Size.Y:F0} du)\n"; ToggleRail(); _pinBadges = null; OpenTree(0); });
            // Tree, legend closed: the five wedge summaries (the closed-legend button is checked in the same call).
            Q(1.0, () =>
            {
                string sm = _tree.ShotSummaryCheck(true, out int nIn);
                _shotLog += $"tree summaries {sfx} (base view): {nIn} of 5 totals fully inside the chart area, no overlap with node, label, total, stamp, legend button: {(sm == null && nIn == 5 ? "PASS" : "FAIL " + sm)}\n"; if (sm != null || nIn != 5) InstFail($"tree summaries {sfx}: {sm}");
                _shotLog += "rule: wedge totals belong to the chart and move with the wedge label; asserted at the base view (zoom 1.0, pan 0): 5 of 5 inside the chart area. Zoomed or panned frames have no visibility requirement; asserted there: totals still in view touch no node, label or other total (zoom 1.2: at least 1 in view; zoom 2.6 + pan, the tree_legend_closed_zoom state: no count, overlaps only)\n";
                SaveShot($"tree_nolegend{sfx}.png", null, scale == 100 && lang == "ru"); _discClosed = _tree.DiscDiameter;
                _tree.ShotZoom(1.2f, -1);   // a mild zoom keeps the lower wedge total in view for the zoomed-view assertion below
            });
            Q(0.4, () =>
            {
                string zs = _tree.ShotSummaryCheck(false, out int zin);
                bool zok = zs == null && zin >= 1;
                _shotLog += $"tree summaries {sfx} (zoom 1.2, pan 0): {zin} of 5 totals in the view; those clear of node, label, total: {(zok ? "PASS" : "FAIL " + zs)}\n"; if (!zok) InstFail($"tree zoomed summaries {sfx}: {zs} in view {zin}");
                _tree.ShotZoom(2.6f, -1); _tree.ShotPanBy(new Vector2(160, -120));
            });
            // The closed button over the zoomed and panned chart; a real click must open the legend and must not pan.
            Q(0.6, () =>
            {
                string zz = _tree.ShotSummaryCheck(false, out _);
                _shotLog += $"tree summaries {sfx} (zoom 2.6, pan): totals in view clear of node, label, total: {(zz == null ? "PASS" : "FAIL " + zz)}\n"; if (zz != null) InstFail($"tree zoom 2.6 summaries {sfx}: {zz}");
                SaveShot($"tree_legend_closed_zoom{sfx}.png"); _panBefore = _tree.ShotPan; Mouse(_tree.ShotButtonCenter * PxPerDu(), null); });
            Q(0.2, () => Mouse(_tree.ShotButtonCenter * PxPerDu(), true));
            Q(0.1, () => Mouse(_tree.ShotButtonCenter * PxPerDu(), false));
            Q(0.5, () =>
            {
                bool ok = _tree.LegendOpen && _tree.ShotPan == _panBefore;
                _shotLog += $"tree legend button {sfx}: real click opens the legend {_tree.LegendOpen}, pan unchanged {_tree.ShotPan == _panBefore}: {(ok ? "PASS" : "FAIL")}\n"; if (!ok) InstFail($"tree legend button {sfx}");
                _tree.ResetView();
            });
            // A9 is checked on the state that is saved: the legend is open. The disc keeps its size and stays at 650 physical px or more (spec §3).
            Q(0.5, () =>
            {
                string ov = _tree.ShotOverlap(); string fit = _tree.ShotLegendFit(); string sm = _tree.ShotSummaryCheck(true, out int nIn2);
                if (ov != null) InstFail($"tree overlap {sfx}: {ov}"); else _shotLog += $"tree overlap {sfx}: none\n";
                _shotLog += $"tree summaries (legend open) {sfx}: {(sm == null ? "PASS" : "FAIL " + sm)}\n"; if (sm != null) InstFail($"tree summaries open {sfx}: {sm}");
                float d = _tree.DiscDiameter, px = d * scale / 100f;
                bool ok = Math.Abs(d - _discClosed) < 0.01f && px >= 650f && fit == null;
                _shotLog += $"tree legend {sfx}: disc {d:F1} du = {px:F0} px (closed {_discClosed:F1} du), legend sheet {_tree.LegendSheet}, {(fit ?? "clear of stamp, disc area and goal panel")}: {(ok ? "PASS" : "FAIL")}\n";
                if (!ok) InstFail($"tree legend {sfx}: disc {d:F1} du (closed {_discClosed:F1}), {px:F0} px, {fit}");
                SaveShot($"tree_legend{sfx}.png"); _tree.Select(_tree.GoalRow >= 0 ? _tree.GoalRow : _tree.FrontierRow);
            });
            // A node is selected with the legend open: the goal panel yields (bottom 8 du above the sheet) and scrolls inside.
            Q(0.9, () =>
            {
                string fit = _tree.ShotLegendFit(); bool sc = _tree.ShotPanelScrolls; string sm = _tree.ShotSummaryCheck(true, out int nIn3);
                bool ok = fit == null && sc && sm == null;
                _shotLog += $"tree legend + selected node {sfx}: {_tree.ShotPanelInfo}; panel clear of the sheet {(fit == null ? "yes" : fit)}, scrolls {sc}, summaries clear {(sm ?? "yes")}: {(ok ? "PASS" : "FAIL")}\n"; if (!ok) InstFail($"tree legend + selection {sfx}: {fit} {sc} {sm}");
                SaveShot($"tree_legend_sel{sfx}.png", null, scale == 100 && lang == "ru"); _tree.ToggleLegend(false); _tree.ResetView(); _tree.Visible = false; _journal.Visible = true;
            });
            Q(0.6, () => { SaveShot($"journal{sfx}.png"); _journal.Visible = false; });
        }
        _sizeQ.Add((0.5, () => { Settings.Current.UiScale = 100; Settings.Apply(Settings.Current, GetWindow(), false); Loc.SetLang("ru"); }));
    }
    void ShotStep(double dt)
    {
        _shotT += dt;
        string dir = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), _shotDir));
        void Shot(string name, Rect2I? r = null) => SaveShot(name, r);
        var s = _host.Snap;
        var vp = GetViewport().GetVisibleRect().Size;
        if (ClickTest(dt, vp, Shot)) return;
        if (_largeSize.X > 0 && _shotStep == 0) { BuildLargeShots(); _shotStep = 70; _shotT = 0; }
        switch (_shotStep)
        {
            case 0:
                _shotLog += $"session start: seed {_host.Seed} start_hash {_host.StartHash}\n";
                RunValidator();
                _signal.SetSpeed(3); ShowPower(_player, false); OpenArea(AreaId.Finance); _shotStep = 1; _shotT = 0; break;
            case 1 when _shotT >= 3:
                // Pause, move 10 points from procurement to R (as the sliders would) and send: the command must stay pending.
                _shotLog += $"before: date {s.Date} tick {s.Tick} shares_in_force {Fmt(s.SharesInForce[0])} tick_ms {s.LastTickMs:F2}\n";
                _signal.SetSpeed(0);
                _shotTick = s.Tick;
                _areaPanel.ShotEdit(0, 45);
                _shotStep = 2; _shotT = 0; break;
            case 2 when _shotT >= 1.2:
                _shotLog += $"paused: tick {s.Tick} (was {_shotTick}), pending {s.Pending}, last {s.Last.Status}\n";
                Shot("globe_panel_pending.png");
                _signal.SetSpeed(3); _shotStep = 3; _shotT = 0; break;
            case 3 when _shotT >= 3:
                _shotLog += $"after: date {s.Date} tick {s.Tick} shares_in_force {Fmt(s.SharesInForce[0])} last {s.Last.Status} pending {s.Pending} tick_ms {s.LastTickMs:F2}\n";
                Shot("globe_panel.png"); Shot("signal_field.png", new Rect2I(0, 0, (int)vp.X, 52));
                // A refused command (shares do not sum to 1) lights the ОТКАЗ place.
                _host.Submit(new Singularity.Core.Commands.SetBudgetShares(_player, new Singularity.Core.Policy.BudgetShares(
                    Singularity.Kernel.Fx6.FromRaw(1_000_000), Singularity.Kernel.Fx6.FromRaw(500_000), default, default, default)), "cmd.budget");
                _shotStep = 4; _shotT = 0; break;
            case 4 when _shotT >= 1.0:
                _shotLog += $"refusal: last {s.Last.Status} rejected_flag {s.LastRejected} reason {s.Last.Reason}\n";
                Shot("signal_active_new.png", new Rect2I(0, 0, (int)vp.X, 52)); Shot("globe_panel_refused.png");
                _shotLog += $"rail badge: Finance carries the active ОТКАЗ ({_rail.BadgeText(AreaId.Finance)}), Production {_rail.BadgeText(AreaId.Production)}\n";
                Shot("rail_badge_finance.png", new Rect2I(0, 0, 420, 360));
                CheckPlateFlat();
                _signal.AckAll(); _shotStep = 5; _shotT = 0; break;
            case 5 when _shotT >= 0.4:
                Shot("signal_active_acked.png", new Rect2I(0, 0, (int)vp.X, 52)); _shotStep = 6; _shotT = 0; break;
            case 6:
                // Pick the first power that has a yearly requirement (a goal for the tree).
                if (_shotT > 0.4)
                {
                    bool has = s.WhyActor == _shotCand && s.Target != null;
                    if (_shotCand >= 0) _shotLog += $"why_not actor {_host.Layout.ActorIds[_shotCand]}: target {(s.Target is { } tt ? tt.Kind + " " + tt.Id : "none")}, links {s.Links.Count}\n";
                    if (has || _shotCand >= _host.Layout.ActorIds.Length - 1) { ShowPower(Math.Max(_shotCand, 0), false); OpenTree(0); _shotStep = 7; _shotT = 0; }
                    else { _shotCand++; _host.WhyActor = _shotCand; _shotT = 0; }
                }
                break;
            case 7 when _shotT >= 1.5: Shot("tech_tree_a.png"); _tree.ShotZoom(2.4f, _tree.FrontierRow >= 0 ? _tree.FrontierRow : _tree.GoalRow); _shotStep = 30; _shotT = 0; break;
            case 30 when _shotT >= 0.8:
                Shot("tree_zoom_nodes.png");
                { int row = _tree.FrontierRow >= 0 ? _tree.FrontierRow : _tree.GoalRow; _tree.ShotZoom(5.5f, row); if (row >= 0) _tree.Select(row); }
                _shotStep = 31; _shotT = 0; break;
            case 31 when _shotT >= 1.0:
                Shot("tree_zoom_icons_card.png"); _tree.ResetView(); _shotStep = 32; _shotT = 0; break;
            case 32 when _shotT >= 0.8: _tree.SetMode(1); _shotStep = 8; _shotT = 0; break;
            case 8 when _shotT >= 1.0:
                Shot("tech_tree_b.png");
                Loc.SetLang("en"); _shotStep = 9; _shotT = 0; break;
            case 9 when _shotT >= 1.0:
                Shot("tech_tree_b_en.png"); _tree.SetMode(0); _shotStep = 10; _shotT = 0; break;
            case 10 when _shotT >= 0.8:
                Shot("tech_tree_a_en.png"); _tree.Visible = false; _shotStep = 11; _shotT = 0; break;
            case 11 when _shotT >= 0.8:
                Shot("globe_panel_en.png"); Shot("signal_field_en.png", new Rect2I(0, 0, (int)vp.X, 52));
                Loc.SetLang("ru");
                // A valid command lifts the ОТКАЗ place: it shows the cleared state («снят»).
                _host.Submit(new Singularity.Core.Commands.SetBudgetShares(_player, new Singularity.Core.Policy.BudgetShares(
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
                _signal.SetSpeed(1); _areaHot = 0; _shotStep = 80; _shotT = 0; break;
            // v0.5c: areas by their hotkeys (a real key event each), the rail, the wide rail, EN, the texture presets, the journal
            case 80 when _shotT >= 0.5: _shotLog += $"hotkeys: sound button in the top bar: {(FindText(_signal, "♪") ? "PRESENT" : "absent")}\n"; PressKey(Key.T); _shotStep = 81; _shotT = 0; break;
            case 81 when _shotT >= 0.8: AreaShot(); if (++_areaHot < HotKeys.Length) PressKey(HotKeys[_areaHot]); else PressKey(Key.Escape); _shotT = 0; if (_areaHot >= HotKeys.Length) _shotStep = 82; break;
            case 82 when _shotT >= 0.6: _shotLog += $"hotkeys: Escape -> area {_area}\n"; Shot("area_none.png"); _rail.FocusArea(AreaId.Production); _shotStep = 94; _shotT = 0; break;
            case 94 when _shotT >= 0.4: Shot("rail_focus.png", new Rect2I(0, 0, 420, 360)); GetViewport().GuiReleaseFocus(); PressKey(Key.Backslash); _shotStep = 83; _shotT = 0; break;
            case 83 when _shotT >= 0.5: PressKey(Key.F); _shotStep = 84; _shotT = 0; break;
            case 84 when _shotT >= 0.8:
                FitCheck(_rail.GetCombinedMinimumSize().Y <= _rail.Size.Y + 0.5f); _shotLog += $"rail wide: width {_rail.RailWidth}, wide {_rail.Wide}, fits without scroll {_rail.GetCombinedMinimumSize().Y <= _rail.Size.Y + 0.5f} (min {_rail.GetCombinedMinimumSize().Y:F0} of {_rail.Size.Y:F0})\n";
                Shot("rail_wide_finance.png"); Loc.SetLang("en"); _shotStep = 85; _shotT = 0; break;
            case 85 when _shotT >= 0.6: Shot("rail_wide_finance_en.png"); PressKey(Key.M); _shotStep = 86; _shotT = 0; break;
            case 86 when _shotT >= 0.6: Shot("rail_wide_forces_en.png"); PressKey(Key.Backslash); _shotStep = 87; _shotT = 0; break;
            case 87 when _shotT >= 0.6: Shot("area_forces_en.png"); PressKey(Key.F); _shotStep = 88; _shotT = 0; break;
            case 88 when _shotT >= 0.6: Shot("area_finance_en.png"); Loc.SetLang("ru"); _shotStep = 89; _shotT = 0; break;
            case 89 when _shotT >= 0.5:
                foreach (var (preset, name) in new[] { (0, "off"), (1, "weak"), (2, "normal") })
                {
                    _texQ.Add((preset, name));
                }
                _shotStep = 90; _shotT = 0; break;
            case 90 when _shotT >= 0.5:
                if (_texQ.Count > 0) { var (preset, name) = _texQ[0]; _texQ.RemoveAt(0); PaperSurface.SetPreset(preset); AddProbe(); _shotT = 0; _texName = name; _shotStep = 91; }
                else { PaperSurface.SetPreset(2); RunT6(); _shotStep = 92; _shotT = 0; }
                break;
            case 91 when _shotT >= 0.4: ProbeMetrics(_texName); Shot($"textures_{_texName}.png", new Rect2I(0, 0, 640, 700)); _shotLog += $"textures {_texName}: shot taken\n"; _shotStep = 90; _shotT = 0; break;
            case 92 when _shotT >= 0.4: ToggleJournal(); _shotStep = 93; _shotT = 0; break;
            case 93 when _shotT >= 0.6: Shot("journal.png"); _journal.Visible = false; CloseArea(); _shotStep = 95; _shotT = 0; break;
            // hover and press of a rail button by real mouse events (the press is released outside, so nothing opens)
            case 95: Mouse(_rail.ButtonCenter(AreaId.Science), null); _shotStep = 96; _shotT = 0; break;
            case 96 when _shotT >= 0.4: Shot("rail_hover.png", new Rect2I(0, 0, 420, 360)); Mouse(_rail.ButtonCenter(AreaId.Science), true); _shotStep = 97; _shotT = 0; break;
            case 97 when _shotT >= 0.3: Shot("rail_press.png", new Rect2I(0, 0, 420, 360)); Mouse(_rail.ButtonCenter(AreaId.Science) + new Vector2(0, 600), null); _shotStep = 98; _shotT = 0; break;
            case 98 when _shotT >= 0.2: Mouse(_rail.ButtonCenter(AreaId.Science) + new Vector2(0, 600), false); CloseArea(); _shotStep = 40; _shotT = 0; break;
            // v0.5b: time icons in their states
            case 40 when _shotT >= 0.5: Shot("time_day.png", new Rect2I(0, 0, 620, 52)); _signal.SetSpeed(2); _shotStep = 41; _shotT = 0; break;
            case 41 when _shotT >= 0.4: Shot("time_week.png", new Rect2I(0, 0, 620, 52)); _signal.SetSpeed(3); _shotStep = 42; _shotT = 0; break;
            case 42 when _shotT >= 0.4: Shot("time_month.png", new Rect2I(0, 0, 620, 52)); _signal.SetSpeed(0); _shotStep = 43; _shotT = 0; break;
            case 43 when _shotT >= 0.4: Shot("time_pause.png", new Rect2I(0, 0, 620, 52)); _signal.SetSpeed(0); _alertI = 0; _instI = -1; _shotStep = 140; _shotT = 0; break;
            // layer-2 instruments in the area panels: caliper (Finance), flasks (Science, Production); every era at 100 % and 150 %, plus a greyscale copy of each
            case 140 when _shotT >= 0.7: if (InstShot() && _shotStep == 140) { _shotStep = 141; AddFlaskProbe(); } _shotT = 0; break;
            case 141 when _shotT >= 0.5: SaveProbe("flask_levels_100"); Settings.Current.UiScale = 150; Settings.Apply(Settings.Current, GetWindow(), false); _shotStep = 142; _shotT = 0; break;
            case 142 when _shotT >= 0.6: SaveProbe("flask_levels_150"); Settings.Current.UiScale = 100; Settings.Apply(Settings.Current, GetWindow(), false); _flaskProbe.QueueFree(); _shotStep = 143; _shotT = 0; break;
            // Science from a real session state: no fixture; a month at a time until the chain shows (at most about 12 s), then the live panel is checked against the snapshot
            case 143 when _shotT >= 0.3: Settings.Current.UiScale = 100; Settings.Apply(Settings.Current, GetWindow(), false); InstrumentLogic.YearOverride = null; OpenArea(AreaId.Science); _signal.SetSpeed(0); _liveMonths = 0; _liveFirst = -1; _liveHit = null; _shotStep = 144; _shotT = 0; break;
            // Live Science: the simulation runs in steps of 30 ticks (about a month); in each step every power's why_not snapshot is read. At the first step where a power has a Knowledge link,
            // that power's Science panel is opened (the area panel pointed at it) and checked; the scan then goes on to 30 years for the shares per power. No power with a link in 30 years: the run fails.
            case 144 when _shotT >= 0.2:
                if (!ScanMonths(6)) break;
                if (_liveFirst >= 0)
                {
                    _shotLog += $"live science: first Knowledge link: {_host.Layout.ActorIds[_liveFirst]} at step {_liveMonths} (date {_host.Snap.Date})\n";
                    _host.WhyActor = _liveFirst; _areaPanel.ShowActor(_liveFirst); _shotStep = 145; _shotT = 0; break;
                }
                if (_liveMonths >= LiveMonthLimit) { LogLiveShares(); InstFail($"no live research chain in {_liveMonths} months for any power (date {_host.Snap.Date})"); }
                _shotT = 0; break;
            case 145 when _shotT >= 0.8:
            {
                int links = LiveKnowledgeLinks(), flasks = _areaPanel.ShotFlaskCount, want = Math.Min(links, 3);
                _shotLog += $"live science: actor {_host.Layout.ActorIds[_liveFirst]}, knowledge links {links}, flasks shown {flasks}, date {_host.Snap.Date}\n";
                if (links == 0) { InstFail("live science check failed: the snapshot lost its Knowledge links before the check"); break; }
                if (flasks != want) { InstFail($"live science check failed: snapshot has {links} Knowledge links, the panel shows {flasks} flasks, expected {want}"); break; }
                var gr = _areaPanel.GetGlobalRect(); float k = PxPerDu();
                SaveShot("flask_science_live.png", new Rect2I((int)(gr.Position.X * k), (int)(gr.Position.Y * k), (int)(gr.Size.X * k), (int)(gr.Size.Y * k)));
                CloseArea(); _host.WhyActor = _player; _shotStep = 146; _shotT = 0; break;
            }
            case 146 when _shotT >= 0.2:
                if (!ScanMonths(60)) break;
                if (_liveMonths >= LiveMonthLimit) { LogLiveShares(); _shotStep = 45; }
                _shotT = 0; break;
            // alert plates on tints 1 and 5 (7 is not on the map): day side, then tint 5 on the night side
            case 45: AimAlert(_alertI, false); _shotStep = 46; _shotT = 0; break;
            case 46 when _shotT >= 0.7:
                Shot($"alert_tint{AlertTints[_alertI] + 1}.png");
                if (++_alertI < AlertTints.Length) _shotStep = 45; else { AimAlert(1, true); _shotStep = 47; }
                _shotT = 0; break;
            case 47 when _shotT >= 0.7:
                Shot("alert_tint5_night.png");
                _globeMat.SetShaderParameter("light_dir", new Vector3(0.7f, 0.35f, 0.6f)); _overlay.RemoveMarker("demo0"); _overlay.RemoveMarker("demo1"); _overlay.RemoveMarker("demo2");
                _shotStep = 50; _shotT = 0; break;
            // menus: (title look is taken on a cold start), in-game look, EN, new game, settings, rollback, UI scale 150%
            case 50: _mainMenu.Open(true); _shotStep = 53; _shotT = 0; break;
            case 53 when _shotT >= 0.4:
                Shot("menu_ingame.png"); _mainMenu.Close();
                _newGame.Open(_host.Data.Scenario(Singularity.Session.SliceRunConfig.DefaultScenario), id => Ui.Tints[TintOfActor(id)]);
                _shotStep = 54; _shotT = 0; break;
            case 54 when _shotT >= 0.5: Shot("new_game_empty.png"); _newGame.Select("FRA"); _shotStep = 55; _shotT = 0; break;
            case 55 when _shotT >= 0.6: Shot("new_game_selected.png"); Loc.SetLang("en"); _shotStep = 56; _shotT = 0; break;
            case 56 when _shotT >= 0.4:
                Shot("new_game_selected_en.png"); Loc.SetLang("ru"); _newGame.Close(); _settings.Open(); _shotStep = 57; _shotT = 0; break;
            case 57 when _shotT >= 0.5: Shot("settings_video.png"); _settings.ShotTab(1); _shotStep = 58; _shotT = 0; break;
            case 58 when _shotT >= 0.4: Shot("settings_sound.png"); _settings.ShotTab(2); _shotStep = 59; _shotT = 0; break;
            case 59 when _shotT >= 0.4: Shot("settings_general.png"); Loc.SetLang("en"); _shotStep = 60; _shotT = 0; break;
            case 60 when _shotT >= 0.4:
                Shot("settings_general_en.png"); Loc.SetLang("ru"); _settings.ShotTab(0); _settings.ShotRollback(); _shotStep = 61; _shotT = 0; break;
            case 61 when _shotT >= 0.6:
                Shot("settings_rollback.png"); _settings.Revert(false); _settings.Close();
                Settings.Current.UiScale = 150; Settings.Apply(Settings.Current, GetWindow(), false);
                _mainMenu.Open(true); _shotStep = 62; _shotT = 0; break;
            case 62 when _shotT >= 0.6:
                Shot("menu_ingame_150.png"); _mainMenu.Close(); _settings.Open(); _shotStep = 63; _shotT = 0; break;
            case 63 when _shotT >= 0.6:
                Shot("settings_video_150.png"); _settings.Close(); OpenArea(AreaId.Finance); _shotStep = 64; _shotT = 0; break;
            case 64 when _shotT >= 0.8:
                Shot("area_finance_150.png");
                FitCheck(_areaPanel.ApplyInView); _shotLog += $"finance at 150%: order button in view without scroll {_areaPanel.ApplyInView}\n";
                FitCheck(_rail.GetCombinedMinimumSize().Y <= _rail.Size.Y + 0.5f); _shotLog += $"rail at 150%: fits without scroll {_rail.GetCombinedMinimumSize().Y <= _rail.Size.Y + 0.5f} (min {_rail.GetCombinedMinimumSize().Y:F0} of {_rail.Size.Y:F0})\n";
                CloseArea();
                Settings.Current.UiScale = 100; Settings.Apply(Settings.Current, GetWindow(), false);
                ShowPower(_player, false); OpenTree(0); _shotStep = 65; _shotT = 0; break;
            case 65 when _shotT >= 1.0: Shot("tree_legend_closed.png"); _tree.ToggleLegend(true); _shotStep = 66; _shotT = 0; break;
            case 66 when _shotT >= 0.5:
                Shot("tree_legend.png"); _tree.ToggleLegend(false); _tree.ShotZoom(2.4f, _tree.FrontierRow >= 0 ? _tree.FrontierRow : _tree.GoalRow); _shotStep = 67; _shotT = 0; break;
            case 67 when _shotT >= 0.8:
                Shot("tree_nodes_frontier_names.png"); _tree.ResetView(); _tree.Visible = false; _signal.SetSpeed(1); BuildSizeShots(); _shotStep = 70; _shotT = 0; break;
            case 70 when _sizeI >= _sizeQ.Count: if (_largeSize.X > 0) FinishShotRun(dir); else { _shotStep = 16; _shotT = 0; } break;
            case 70 when _shotT >= _sizeQ[_sizeI].Wait: _sizeQ[_sizeI++].Act(); _shotT = 0; break;
            case 16 when _shotT >= 0.5:
                _audio.StartDemo(); _shotStep = 17; _shotT = 0; break;
            case 17:
                // The demo script runs in real time; its sound is recorded from the Master bus.
                if (!_audio.StepDemo(dt) && _shotT >= AudioDirector.DemoSeconds + 0.5)
                {
                    // The recorder gives WAV; it goes to a temp file and ffmpeg packs it into OGG Vorbis (the WAV was 3.4 MB).
                    string tmpWav = Path.Combine(Path.GetTempPath(), "singularity_audio_demo.wav");
                    int bytes = _audio.Active ? _audio.SaveDemo(tmpWav) : 0;
                    string packed = "";
                    if (bytes > 0)
                    {
                        Directory.CreateDirectory(dir);
                        string ogg = Path.Combine(dir, "audio_demo.ogg");
                        var code = OS.Execute("ffmpeg", new[] { "-y", "-loglevel", "error", "-i", tmpWav, "-c:a", "libvorbis", "-q:a", "4", ogg });
                        packed = code == 0 ? $", packed to audio_demo.ogg ({new FileInfo(ogg).Length} bytes)" : $", ffmpeg failed (exit {code}): audio_demo.ogg not written";
                        File.Delete(tmpWav);
                    }
                    _shotLog += $"audio demo: {(bytes > 0 ? bytes + " bytes of PCM" + packed : "not recorded (no audio device)")}\n";
                    _shotLog += $"final: date {s.Date} tick {s.Tick}\n";
                    FinishShotRun(dir);
                }
                break;
        }
    }

    // ---------- v0.5c shot helpers ----------
    static readonly Key[] HotKeys = { Key.T, Key.E, Key.F, Key.M, Key.I, Key.G, Key.J };
    int _areaHot; string _texName = ""; readonly List<(int, string)> _texQ = new();

    // A real key press through the input pipeline, so that the hotkey route is the one a player uses.
    // Mouse by the input pipeline: null = move only, true = left button down, false = up.
    static void Mouse(Vector2 p, bool? down)
    {
        if (down == null) Input.ParseInputEvent(new InputEventMouseMotion { Position = p, GlobalPosition = p });
        else Input.ParseInputEvent(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = down.Value, ButtonMask = down.Value ? MouseButtonMask.Left : 0 });
    }

    // Instrument shots: 3 areas x 3 eras x 2 UI scales; the previous configuration is saved, then the next one is set up. Returns true when all are done.
    int _instTarget, _instSpeedBefore;
    int _instI, _instPhase, _instShareBefore; bool _instCheckInput, _instCheckSend, _instSent, _instSkipShot; int _instAppliedBefore;

    // A failed shot-run check: the error goes to the run log and the process exits non-zero.
    const int LiveMonthLimit = 360;
    int[] _liveHit; int _liveMonths, _liveFirst = -1;

    bool WaitFor(Func<bool> done, string what)
    {
        var sw = Stopwatch.StartNew();
        while (!done()) { if (sw.ElapsedMilliseconds > 20000) { InstFail($"timeout waiting for {what} (tick {_host.Snap.Tick}, speed {_host.Speed}, why actor {_host.Snap.WhyActor})"); return false; } System.Threading.Thread.Sleep(1); }
        return true;
    }

    // Steps the simulation n times 30 ticks and reads every power's why_not snapshot after each step (blocking: a shot-run helper). False after a failure.
    bool ScanMonths(int n)
    {
        int na = _host.Layout.ActorIds.Length;
        _liveHit ??= new int[na];
        for (int m = 0; m < n && _liveMonths < LiveMonthLimit; m++)
        {
            long target = _host.Snap.Tick + 30;
            _host.StepTicks(30);
            if (!WaitFor(() => _host.Snap.Tick >= target, "a simulation step")) return false;
            for (int a = 0; a < na; a++)
            {
                _host.WhyActor = a;
                if (!WaitFor(() => _host.Snap.WhyActor == a && _host.Snap.Tick >= target, "the why_not snapshot of a power")) return false;
                if (LiveKnowledgeLinks() > 0) { _liveHit[a]++; if (_liveFirst < 0) _liveFirst = a; }
            }
            _liveMonths++;
            if (_liveFirst >= 0 && _shotStep == 144) return true;   // the first month with a link: stop here for the panel check
        }
        return true;
    }

    void LogLiveShares()
    {
        for (int a = 0; a < _liveHit.Length; a++)
            _shotLog += $"live science share: {_host.Layout.ActorIds[a]} has a Knowledge link in {_liveHit[a]} of {_liveMonths} steps ({100.0 * _liveHit[a] / Math.Max(_liveMonths, 1):F1} %)\n";
    }

    // Knowledge links the panel would read: the same condition as the live path of the Science flasks.
    int LiveKnowledgeLinks() => _host.Snap is { WhyError: "", Target: not null } sn ? Singularity.Client.Spike.InstrumentLogic.ResearchFlasks(sn.Links, int.MaxValue).Count : 0;

    // The shot run exits non-zero when any live check failed (T6, rail, tree, input). Palette validator failures that exist on main are listed below by exact name: reported, not failing.
    bool _shotFailed;
    static readonly HashSet<(string Palette, string Name)> KnownDebt = new()
    {
        // only the v0.2 palette (kept for comparison) carries these; the same names failing on palette C, the one the game uses, are not debt
        ("V02", "warn on sea (shaded)"), ("V02", "warn on sea.deep (shaded)"), ("V02", "warn on land-none (shaded)"),
        ("V02", "tint.1 vs tint.5"), ("V02", "tint.1 vs tint.7"), ("V02", "tint.2 vs tint.8"), ("V02", "tint.4 vs tint.6"), ("V02", "tint.5 vs tint.7"),
    };
    void FitCheck(bool ok) { if (!ok) _shotFailed = true; }   // a logged "fits without scroll False" fails the run
    void LogCheck(string text) { _shotLog += text; if (text.Contains("FAIL")) _shotFailed = true; }
    void Validate(string title, IReadOnlyList<PairResult> res, PaletteSet p = null)
    {
        _shotLog += p != null ? ContrastValidator.Format(p, res) : TextureValidator.Format(title, res);
        if (res.Count == 0) _shotFailed = true;
        foreach (var r in res)
        {
            if (r.Pass) continue;
            if (p != null && KnownDebt.Contains((ReferenceEquals(p, Palette.V02) ? "V02" : "C", r.Name))) _shotLog += $"known, DEBT: {r.Name} (palette V02)\n";
            else { _shotLog += $"UNLISTED FAILURE: {r.Name}\n"; _shotFailed = true; }
        }
    }
    // The rendered frame, not the layout numbers: every glyph at the Icons level stands inside its node box and has ink where it was told to be drawn, and every label has ink in its rectangle.
    string ProbeTreeGlyphs()
    {
        var img = GetViewport().GetTexture().GetImage(); float k = PxPerDu(); var org = _tree.GlobalPosition; var area = _tree.ShotArea;
        int ink(Rect2 r)
        {
            int x0 = (int)((org.X + r.Position.X) * k), y0 = (int)((org.Y + r.Position.Y) * k), x1 = (int)((org.X + r.End.X) * k), y1 = (int)((org.Y + r.End.Y) * k), n = 0;
            if (x0 < 1 || y0 < 1 || x1 >= img.GetWidth() || y1 >= img.GetHeight()) return -1;
            var bg = img.GetPixel(x0, y0);   // the corner of the box is fill or paper, never the stroke
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) { var p = img.GetPixel(x, y); if (Math.Abs(p.R - bg.R) + Math.Abs(p.G - bg.G) + Math.Abs(p.B - bg.B) > 0.5f) n++; }
            return n;
        }
        int seen = 0, labels = 0;
        foreach (var (row, inkBox, box) in _tree.ShotGlyphs)
        {
            if (!area.Encloses(box)) continue;
            seen++;
            if (!box.Grow(0.5f).Encloses(inkBox)) return $"glyph of node {row}: ink {inkBox} leaves its node box {box}";
            int n = ink(inkBox);
            if (n >= 0 && n < 12) return $"glyph of node {row}: only {n} ink pixels where it should be drawn at {inkBox}";
        }
        foreach (var lr in _tree.ShotLabelRects)
        {
            if (!area.Encloses(lr)) continue;
            labels++;
            int n = ink(lr);
            if (n >= 0 && n < 8) return $"label {lr}: only {n} ink pixels (it was drawn elsewhere)";
        }
        return seen >= 6 && labels >= 3 ? null : $"probe saw {seen} glyphs and {labels} labels in the area";
    }

    // A2 on the drawn pixels: the alpha box of each rasterised icon, in du at its drawn size (24 du; the lock 28 du), must reach 16 x 16.
    void CheckIconInk()
    {
        var slugs = AreaData.All.Select(d => d.Slug).Append("journal").Append("lock");
        foreach (var slug in slugs)
        {
            var img = UiArt.Icon(slug).GetImage(); int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            for (int y = 0; y < img.GetHeight(); y++)
                for (int x = 0; x < img.GetWidth(); x++)
                    if (img.GetPixel(x, y).A >= 0.35f) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
            float k = (slug == "lock" ? RailMetrics.LockDrawDu : 24f) / img.GetWidth();
            float w = (x1 - x0 + 1) * k, h = (y1 - y0 + 1) * k;
            bool ok = x1 >= 0 && w >= GlyphPrims.IconFloor && h >= GlyphPrims.IconFloor;
            _shotLog += $"icon ink {slug}: {w:F1} x {h:F1} du (raster {img.GetWidth()} px): {(ok ? "PASS" : "FAIL")}\n"; if (!ok) _shotFailed = true;
        }
    }

    void FinishShotRun(string dir)
    {
        CheckScaleCoverage(); if (_largeSize.X == 0) CheckIconInk();
        _shotLog += $"shot run result: {(_shotFailed ? "FAILED (a live check failed, exit 1)" : "OK (exit 0)")}\n";
        Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "run-log.txt"), _shotLog); GD.Print(_shotLog);
        GetTree().Quit(_shotFailed ? 1 : 0); _shotStep = 18;
    }

    void InstFail(string msg)
    {
        _shotLog += "ERROR: " + msg + "\n";
        _shotFailed = true;
        try
        {
            string dir = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), _shotDir));
            Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "run-log.txt"), _shotLog);
        }
        catch (Exception ex) { GD.PrintErr("cannot write run-log.txt: " + ex.Message); }   // the run still exits 1
        GD.PrintErr(msg);
        GetTree().Quit(1); _shotStep = 18;
    }
    // Shot-only strip: flasks at 15, 60 and 95 % without and with a deficit (the live data has no deficit and no Science chain in the shot scenario).
    PanelContainer _flaskProbe;
    void AddFlaskProbe()
    {
        var box = new PanelContainer { Position = new Vector2(200, 80), MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Sheet, Ui.Ink, 2, 12, Ui.R3, 12));
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10);
        foreach (bool def in new[] { false, true })
            foreach (double lv in new[] { 0.15, 0.6, 0.95 })
            { var f = new FlaskControl { Ink = Ui.Teal }; f.Set(lv, def); row.AddChild(f); }
        box.AddChild(row);
        _areaPanel.GetParent().AddChild(box);
        _flaskProbe = box;
    }

    void SaveProbe(string name)
    {
        var img = GetViewport().GetTexture().GetImage();
        float k = img.GetWidth() / GetViewport().GetVisibleRect().Size.X;
        var gr = _flaskProbe.GetGlobalRect();
        img = img.GetRegion(new Rect2I((int)(gr.Position.X * k), (int)(gr.Position.Y * k), (int)(gr.Size.X * k), (int)(gr.Size.Y * k)).Intersection(new Rect2I(0, 0, img.GetWidth(), img.GetHeight())));
        string dir = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), _shotDir));
        CheckSave(TrySave(img, Path.Combine(dir, name + ".png")), Path.Combine(dir, name + ".png"));
        img.Convert(Image.Format.La8); CheckSave(TrySave(img, Path.Combine(dir, name + "_grey.png")), Path.Combine(dir, name + "_grey.png"));
    }
    static readonly int[] InstYears = { 1950, 1985, 2030 };
    static readonly AreaId[] InstAreas = { AreaId.Finance, AreaId.Science, AreaId.Production };
    static readonly string[] InstNames = { "caliper_finance", "flask_science", "flask_production" };
    // Real 1920x1080 frames at 100 % and 150 % UI scale (#417, #424): 3 areas x 3 eras x 2 scales, each with a greyscale copy,
    // the whole screen too, and the speed meter bar with the Finance rows.
    bool InstShot()
    {
        // Finance: the edit goes through the input pipeline (a click on a share row, a click on the caliper, an arrow key), one step per call so that the layout settles in between.
        if (_instPhase > 0)
        {
            float w = PxPerDu();   // events are window pixels, the panel reports canvas units
            if (_instPhase == 1) { var c = _areaPanel.ShotRowPoint(1) * w; Mouse(c, null); Mouse(c, true); Mouse(c, false); }
            else if (_instPhase == 3) PressKey(Key.Tab);   // after the frame with the unsent edit: Tab from the caliper to Apply
            else if (_instPhase == 4) { if (_instI == 0) { var gr = _areaPanel.GetGlobalRect(); SaveShot("apply_focus.png", new Rect2I((int)(gr.Position.X * w), (int)(gr.Position.Y * w), (int)(gr.Size.X * w), (int)(gr.Size.Y * w))); } _instAppliedBefore = _areaPanel.ShotApplied; _instSpeedBefore = _host.Speed; PressKey(Key.Space); PressKey(Key.Space, false); }   // Space is the pause key even with Apply focused
            else if (_instPhase == 5)
            {
                // Space must not have submitted the order, and must have toggled the pause; then it is toggled back
                if (_areaPanel.ShotApplied != _instAppliedBefore || _host.Speed == _instSpeedBefore) { InstFail($"finance Space check failed: orders submitted {_areaPanel.ShotApplied - _instAppliedBefore}, speed {_instSpeedBefore} -> {_host.Speed}"); return true; }
                PressKey(Key.Space); PressKey(Key.Space, false);
            }
            else if (_instPhase == 6)
            {
                if (_host.Speed != _instSpeedBefore) { InstFail($"finance Space check failed: pause was not restored ({_instSpeedBefore} -> {_host.Speed})"); return true; }
                PressKey(Key.Space); PressKey(Key.Tab, true, true); PressKey(Key.Tab, false, true);   // Space down on Apply, then Shift+Tab away from it
            }
            else if (_instPhase == 7) { PressKey(Key.Space, false); PressKey(Key.Tab); PressKey(Key.Tab, false); }   // Space up on another control, then Tab back to Apply
            else if (_instPhase == 8)
            {
                if (_areaPanel.ShotApplied != _instAppliedBefore || _host.Speed == _instSpeedBefore) { InstFail($"finance Space-release check failed: orders submitted {_areaPanel.ShotApplied - _instAppliedBefore}, speed {_instSpeedBefore} -> {_host.Speed}"); return true; }
                PressKey(Key.Space); PressKey(Key.Space, false);   // pause back
            }
            else if (_instPhase == 9)
            {
                if (_host.Speed != _instSpeedBefore) { InstFail($"finance Space-release check failed: pause was not restored ({_instSpeedBefore} -> {_host.Speed})"); return true; }
                _instAppliedBefore = _areaPanel.ShotApplied; PressKey(Key.Enter); PressKey(Key.Enter, false); _instCheckSend = true;   // Enter still submits after the Space sequence
            }
            else
            {
                _instShareBefore = _areaPanel.ShotShare(1);
                _instTarget = _instShareBefore == 45 ? 60 : 40;   // an order sent in an earlier pass may be in force by now: pick a different spot
                var c = _areaPanel.ShotCaliperPoint(_instTarget) * w; Mouse(c, null); Mouse(c, true); Mouse(c, false); PressKey(Key.Right);
                _instCheckInput = true;   // the events are processed on the next frame: checked at the next call
            }
            _instPhase = _instPhase switch { 1 => 2, 3 => 4, 4 => 5, 5 => 6, 6 => 7, 7 => 8, 8 => 9, _ => 0 };
            return false;
        }
        if (_instCheckInput)
        {
            // The injected row click, caliper click and arrow key must have selected share 1 and moved it (click to the target, one step right = target + 5); otherwise the frames would show an untouched panel.
            _instCheckInput = false;
            int sel = _areaPanel.ShotSelected, val = _areaPanel.ShotShare(1);
            _shotLog += $"finance input: selected share {sel}, share value {val} (was {_instShareBefore})\n";
            if (sel != 1 || val == _instShareBefore || val != _instTarget + 5) { InstFail($"finance input check failed: expected share 1 at {_instTarget + 5} (was {_instShareBefore}), got share {sel} at {val}"); return true; }
        }
        if (_instCheckSend)
        {
            // Keyboard only: Tab reached Apply and Enter submitted the edit (the panel is waiting for the command result).
            _instCheckSend = false; _instSkipShot = true;
            bool sent = _areaPanel.ShotApplied == _instAppliedBefore + 1;
            _shotLog += $"finance keyboard submit: order submitted {sent}\n";
            if (!sent) { InstFail("finance keyboard check failed: Tab then Enter did not submit the order"); return true; }
        }
        if (_instI >= 0 && !_instSkipShot)
        {
            int p = _instI % 9, pct = _instI < 9 ? 100 : 150;
            var img = GetViewport().GetTexture().GetImage();
            float k = img.GetWidth() / GetViewport().GetVisibleRect().Size.X;
            var all = new Rect2I(0, 0, img.GetWidth(), img.GetHeight());
            if (!SettingsLogic.IsBaseFrame(img.GetWidth(), img.GetHeight())) { InstFail($"instrument frame is {img.GetWidth()}x{img.GetHeight()}, expected a real 1920x1080 render"); return true; }
            string dir = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), _shotDir));
            void Save(Image im, Rect2 gr, string name)
            {
                var c = im.GetRegion(new Rect2I((int)(gr.Position.X * k), (int)(gr.Position.Y * k), (int)(gr.Size.X * k), (int)(gr.Size.Y * k)).Intersection(all));
                CheckSave(TrySave(c, Path.Combine(dir, name + ".png")), Path.Combine(dir, name + ".png"));
                c.Convert(Image.Format.La8); CheckSave(TrySave(c, Path.Combine(dir, name + "_grey.png")), Path.Combine(dir, name + "_grey.png"));
            }
            string sfx = $"e{p % 3 + 1}_{pct}";
            if (p / 3 == 1)
                foreach (string lang in Loc.Languages)
                    if (_areaPanel.ShotCaptionWidth(lang) > FlaskControl.CaptionLimit) { InstFail($"flask caption check failed: {lang} caption is {_areaPanel.ShotCaptionWidth(lang)} du, cell limit {FlaskControl.CaptionLimit}"); return true; }
            if (p / 3 != 0 && Math.Abs(_areaPanel.GetGlobalRect().Size.X - 420) > 0.5f) { InstFail($"area panel width check failed: {InstNames[p / 3]}_{sfx} is {_areaPanel.GetGlobalRect().Size.X} du wide, expected 420"); return true; }
            if (p / 3 != 0 && _areaPanel.ShotMinWidthWithBar() > 420.5f) { InstFail($"area panel width check failed: {InstNames[p / 3]}_{sfx} needs {_areaPanel.ShotMinWidthWithBar()} du with the scroll bar, limit 420"); return true; }
            _shotLog += $"panel scrolls: {_areaPanel.ShotScrolls}\n";
            Save(img, _areaPanel.GetGlobalRect(), $"{InstNames[p / 3]}_{sfx}");
            if (p / 3 == 0) Save(img, new Rect2(0, 0, 620, 52), $"speed_{sfx}");
            Save(img, new Rect2(Vector2.Zero, GetViewport().GetVisibleRect().Size), $"full_{InstNames[p / 3]}_{sfx}");   // the whole screen, for the control in context
            _shotLog += $"instrument shot {InstNames[p / 3]}_{sfx}: image {img.GetWidth()}x{img.GetHeight()}, scale {pct} %\n";
            if (p / 3 == 0) { _instPhase = 3; return false; }   // the frame with the unsent edit is kept; now the keyboard route to Apply
        }
        _instSkipShot = false;
        if (++_instI >= 18)
        {
            _areaPanel.ShotResearch(null); InstrumentLogic.YearOverride = null; Settings.Current.UiScale = 100; Settings.Apply(Settings.Current, GetWindow(), false); CloseArea();
            return true;
        }
        int q = _instI % 9, era = q % 3;
        Settings.Current.UiScale = _instI < 9 ? 100 : 150; Settings.Apply(Settings.Current, GetWindow(), false);
        InstrumentLogic.YearOverride = InstYears[era];
        OpenArea(InstAreas[q / 3]);
        // shot-only state: the scenario has no research chain
        _areaPanel.ShotResearch(InstAreas[q / 3] == AreaId.Science ? new[] { ShotLink("s8.fnd.compressible_flow", 3, 4), ShotLink("s8.fnd.boundary_layer", 1, 5), ShotLink("s7.fnd.brayton_cycle", 5, 5) } : null);
        if (InstAreas[q / 3] == AreaId.Finance) _instPhase = 1;   // an unsent edit and the focus come from real input: pencil marker and focus frame show
        return false;
    }

    // A why_not Knowledge link for the shot fixture: the panel reads links the same way as in play.
    Link ShotLink(string id, long have, long need)
    {
        var n = _host.Data.Nodes;
        int number = -1;
        for (int i = 0; i < n.Count; i++) if (n.Id[i] == id) number = n.Number[i];
        return new Link(WhyNotLayer.Knowledge, WhyNotCause.MissingNode, number, Singularity.Kernel.Fx6.FromInt(have), Singularity.Kernel.Fx6.FromInt(need), System.Collections.Immutable.ImmutableArray<Lever>.Empty, Singularity.Kernel.Fx6.Zero, null);
    }

    static void PressKey(Key k, bool pressed = true, bool shift = false) => Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = pressed, ShiftPressed = shift });

    void AreaShot()
    {
        var def = AreaData.Of(_area);
        _shotLog += $"hotkey {HotKeys[_areaHot]} -> area {_area}; live panel {_areaPanel.Visible}, locked card {_areaCard.Visible}; rail selected {_rail.Selected}\n";
        SaveShot("area_" + def.Slug + ".png");
    }

    static bool FindText(Node n, string text)
    {
        if ((n is CaptionButton cb && cb.Text == text) || (n is Button b && b.Text == text) || (n is Label l && l.Text == text)) return true;
        foreach (var c in n.GetChildren()) if (FindText(c, text)) return true;
        return false;
    }

    // Validator rule T3 on the screenshot: inside a «new» alert plate the luminance does not vary (no paper texture), while a textured stretch of the bar does.
    void CheckPlateFlat()
    {
        var img = GetViewport().GetTexture().GetImage();
        float k = img.GetWidth() / GetViewport().GetVisibleRect().Size.X;
        IReadOnlyList<byte> Luma(Rect2 r)
        {
            var l = new List<byte>();
            for (int y = (int)(r.Position.Y * k); y < (int)(r.End.Y * k); y++)
                for (int x = (int)(r.Position.X * k); x < (int)(r.End.X * k); x++)
                { var c = img.GetPixel(x, y); l.Add((byte)Math.Round((0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) * 255)); }
            return l;
        }
        var plate = _signal.PlateRect("sig.refusal");
        // The strip between the 3 du frame and the label: only the plate fill is in it.
        var strip = new Rect2(plate.Position.X + 3.5f, plate.Position.Y + 5, 2, plate.Size.Y - 10);
        var free = _signal.FreeRect; var area = new Rect2(free.Position.X + free.Size.X / 2 - 10, free.Position.Y + 6, 20, free.Size.Y - 12);
        var r3 = TextureValidator.T3(TextureValidator.Variance(Luma(strip)), TextureValidator.Variance(Luma(area)), PaperSurface.Strength > 0);
        _shotLog += (r3.Pass ? "PASS " : "FAIL ") + r3.Name + ": " + r3.Detail + $" (plate strip {strip.Position.X:F0},{strip.Position.Y:F0} {strip.Size.X:F0}x{strip.Size.Y:F0})\n";
    }

    // Texture acceptance (spec §3.3): a bare sheet 240x240 without any text is drawn next to the panel; sigma of luminance inside it and the darkening of the 0-6 du edge band are measured on the screenshot.
    Panel _probe;
    void AddProbe()
    {
        _probe?.QueueFree();
        _probe = new Panel { Position = new Vector2(700, 80), Size = new Vector2(240, 240), MouseFilter = Control.MouseFilterEnum.Ignore };
        _probe.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Sheet, null, 0, 0, 0));
        PaperSurface.Apply(_probe, SurfaceKind.Sheet, "probe");
        _areaPanel.GetParent().AddChild(_probe);
    }

    void ProbeMetrics(string name)
    {
        var img = GetViewport().GetTexture().GetImage();
        float k = img.GetWidth() / GetViewport().GetVisibleRect().Size.X;
        double L(int x, int y) { var c = img.GetPixel(x, y); return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) * 255; }
        var inner = new List<double>(); double band = 0; int bn = 0;
        for (int y = 0; y < 240 * k; y++)
            for (int x = 0; x < 240 * k; x++)
            {
                double e = Math.Min(Math.Min(x, 240 * k - 1 - x), Math.Min(y, 240 * k - 1 - y)) / k;
                int px = (int)(_probe.GlobalPosition.X * k) + x, py = (int)(_probe.GlobalPosition.Y * k) + y;
                if (e >= 12) inner.Add(L(px, py));
                else if (e < 6) { band += L(px, py); bn++; }
            }
        double mean = inner.Average(), sigma = Math.Sqrt(inner.Average(v => (v - mean) * (v - mean)));
        double drop = 1 - band / bn / mean;
        bool ok = name switch
        {
            "normal" => sigma >= 3.0 && drop is >= 0.05 and <= 0.09,
            "weak" => sigma >= 1.5 && drop is >= 0.025 and <= 0.045,
            _ => sigma < 0.5 && Math.Abs(drop) < 0.005,
        };
        _shotLog += $"{(ok ? "PASS" : "FAIL")} texture acceptance {name}: sigma inside {sigma:F2} levels, edge band 0-6 du darker by {drop * 100:F1}% (normal: sigma >= 3.0, 5-9%; weak: 1.5, 2.5-4.5%; off: 0)\n";
        _probe.QueueFree(); _probe = null;
    }

    // T6 on the live layout: the distance from the edge of every textured surface to the nearest text, icon or plate inside it.
    void RunT6(string tag = null, bool railOnly = false)
    {
        var items = new List<(string, double, double)>();
        foreach (var (node, kind) in PaperSurface.Surfaces())
        {
            if (!GodotObject.IsInstanceValid(node) || !node.IsVisibleInTree()) continue;
            if (railOnly && !_rail.IsAncestorOf(node)) continue;
            var r = node.GetGlobalRect(); double min = double.MaxValue; string who = "";
            void Visit(Node n)
            {
                foreach (var c in n.GetChildren())
                {
                    if (c is not Control ch || !ch.Visible) continue;   // content outside the surface (scrolled away) is clipped, not drawn
                    if (ch is Label or TextureRect or RichTextLabel)
                    {
                        foreach (var g in Content(ch))
                        {
                            if (!r.Intersects(g)) continue;
                            double d = Math.Min(Math.Min(g.Position.X - r.Position.X, r.End.X - g.End.X), Math.Min(g.Position.Y - r.Position.Y, r.End.Y - g.End.Y));
                            if (d < min) { min = d; who = ch.GetType().Name + " " + (ch is Label lb ? lb.Text : ch.Name); }
                        }
                    }
                    Visit(ch);
                }
            }
            if (node is IGlyphBounds gb)
                foreach (var g in gb.GlyphRects())
                {
                    double d = Math.Min(Math.Min(g.Position.X - r.Position.X, r.End.X - g.End.X), Math.Min(g.Position.Y - r.Position.Y, r.End.Y - g.End.Y));
                    if (d < min) { min = d; who = "glyph"; }
                }
            Visit(node);
            if (min < double.MaxValue) items.Add(($"{node.GetParent()?.Name}/{node.Name} ({kind}) nearest {who}", min / UiScaleOf(node), TextureValidator.Limit(kind)));
        }
        Validate("T6 edge band" + (tag != null ? " (" + tag + ")" : "") + ": no text, icon or plate within the band", TextureValidator.T6(items));
    }

    // Distance in du: global rects are in pixels; the UI scale of the window multiplies them.
    static double UiScaleOf(Control c) => c.GetGlobalTransform().Scale.X;

    // The ink of a control: a Label shows its text as big as the text really is (centred or aligned), not the whole rect; an icon its rect; a button glyph layer the rects it registered.
    static IEnumerable<Rect2> Content(Control c)
    {
        if (c is Label l)
        {
            var font = l.GetThemeFont("font"); int size = l.GetThemeFontSize("font_size");
            var ts = font.GetMultilineStringSize(l.Text == "" ? " " : TranslationServer.Translate(l.Text), l.HorizontalAlignment, c.Size.X, size);
            var gr = c.GetGlobalRect(); float k = c.GetGlobalTransform().Scale.X;
            float w = Math.Min(ts.X * k, gr.Size.X), h = Math.Min(ts.Y * k, gr.Size.Y);
            float x = l.HorizontalAlignment switch { HorizontalAlignment.Center => gr.Position.X + (gr.Size.X - w) / 2, HorizontalAlignment.Right => gr.End.X - w, _ => gr.Position.X };
            float y = l.VerticalAlignment switch { VerticalAlignment.Center => gr.Position.Y + (gr.Size.Y - h) / 2, VerticalAlignment.Bottom => gr.End.Y - h, _ => gr.Position.Y };
            yield return new Rect2(x, y, w, h);
        }
        else yield return c.GetGlobalRect();
    }

    // Runs the alert-contrast validator on palette C and on the v0.2 palette, prints PASS/FAIL per pair into the log. A failure does not stop the run.
    void RunValidator()
    {
        foreach (var p in new[] { Palette.C, Palette.V02 }) Validate("", ContrastValidator.Run(p), p);
        Validate("texture rules T1, T2, T4, T5 and moss/olive (T3 and T6 run on the live client below), palette C", TextureValidator.Run(Palette.C));
        Validate("speed gauge", TextureValidator.RunGauge());
        Validate("instrument layers (UI_INSTRUMENTS §5), palette C", ContrastValidator.RunInstruments(Palette.C));
        _shotLog += "shot-only: the plates on tints 1/5/7 are demo markers; in the game a plate appears only for a real signal of the player's power\n";
    }

    // Demo alert plates on one country of each of the tints 1, 5 and 7 (palette C risk, spec 6.4); camera on the chosen one.
    static readonly int[] AlertTints = { 0, 4 };
    int _alertI;
    void AimAlert(int which, bool night)
    {
        for (int i = 0; i < AlertTints.Length; i++)
        {
            // The greedy colouring may leave a tint unused (the last ones are rare): fall back to the next lower tint that is on the map.
            int tint = AlertTints[i];
            while (tint > 0 && !_data.Countries.Any(x => x.Tint == tint)) tint--;
            var c = _data.Countries.Where(x => x.Tint == tint && Names.Country(x.Name) != null).OrderByDescending(x => x.BoxArea).FirstOrDefault()
                    ?? _data.Countries.Where(x => x.Tint == tint).OrderByDescending(x => x.BoxArea).First();
            _data.ComputeLabelPoint(c);
            _overlay.SetMarker("demo" + i, c.LabelPoint, true, "sig.alloy");
            if (i == which)
            {
                _yaw = c.LabelPoint.X; _pitch = Mathf.Clamp(c.LabelPoint.Y, -60, 60); _dist = 2.3f; _flying = false;
                _shotLog += $"alert demo: tint {tint + 1} (asked {AlertTints[i] + 1}) on {c.Name}{(night ? " (night side)" : "")}\n";
            }
        }
        _globeMat.SetShaderParameter("light_dir", night ? new Vector3(-0.7f, -0.35f, -0.6f) : new Vector3(0.7f, 0.35f, 0.6f));
        _globeMat.SetShaderParameter("light_amount", 1f);
    }

    // Click test: for the USA, the USSR and France a click on the country, before and after rotating the globe. After the turn the country must be
    // at the viewport centre and the zoom must be unchanged.
    int _clickPhase, _lightShot; Vector2 _clickPoint; float _clickDist;
    bool ClickTest(double dt, Vector2 vp, Action<string, Rect2I?> shot)
    {
        if (_clickCase >= 6) return false;
        if (_clickCase < 0) { _clickCase = 0; _clickT = 0; _clickPhase = 0; _globeMat.SetShaderParameter("light_amount", 0f); }
        _clickT += dt;
        var ids = _host.Layout.ActorIds.ToArray();
        string[] pick = { "USA", "SUN", "FRA" };
        int a = Array.IndexOf(ids, pick[_clickCase / 2]);
        bool odd = _clickCase % 2 == 1;
        var c = _data.Countries[_actorCountry[a] - 1];
        var sph = GlobeData.ToSphere(c.LabelPoint.X, c.LabelPoint.Y);
        if (_clickPhase == 0)
        {
            _yaw = c.LabelPoint.X + (odd ? 28 : -25); _pitch = Mathf.Clamp(c.LabelPoint.Y + (odd ? -14 : 12), -80, 80); _dist = 2.6f; _flying = false; UpdateCamera();
            if (_clickT < 0.4) return true;
            if (_clickCase == 0 && _lightShot == 0) { shot("globe_unlit.png", null); _globeMat.SetShaderParameter("light_amount", 1f); _lightShot = 1; _clickT = 0; return true; }
            if (_clickCase == 0 && _lightShot == 1) { if (_clickT < 0.3) return true; shot("globe_lit.png", null); _lightShot = 2; }
            _clickPoint = _cam.UnprojectPosition(sph); _clickDist = _dist;
            Pick(_clickPoint); _clickPhase = 1; _clickT = 0;
            return true;
        }
        if (_clickT < 1.8) return true;
        float err = _cam.UnprojectPosition(sph).DistanceTo(vp * 0.5f - new Vector2(PanelShift(), 0));
        _clickLog += $"click {pick[_clickCase / 2]} {(odd ? "after" : "before")} rotation: clicked at ({_clickPoint.X:F0},{_clickPoint.Y:F0}), panel {_viewed == a}, " +
                     $"free-area centre error {err:F1} px (shift {PanelShift():F0}), zoom {_clickDist:F2} -> {_dist:F2}, selected region {_selected == _actorCountry[a]}\n";
        if (!odd) shot($"map_click_{pick[_clickCase / 2]}.png", null);
        _clickCase++; _clickPhase = 0; _clickT = 0;
        if (_clickCase >= 6) { _shotLog += _clickLog; _yaw = 10; _pitch = 35; _dist = 3.0f; _flying = false; ShowPower(_player, false); }
        return true;
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
