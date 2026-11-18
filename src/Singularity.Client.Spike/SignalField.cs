using System;
using System.Globalization;
using System.Linq;
using Godot;

namespace Singularity.Client.Spike;

/// <summary>Top bar (design system §7.5). Left: controls (date, tempo; the player plays one power, others open by a click on the globe). Right, after a vertical rule: the six annunciator
/// places and the simulation-failure place. Place states: calm; new (plate + double frame, until a click acknowledges it); acknowledged (calm frame, only a filled ■);
/// cleared (calm frame, hollow □ for a few seconds, then calm again).</summary>
public sealed partial class SignalField : PanelContainer
{
    enum PState { Calm, New, Acked, Cleared }
    const double ClearedSeconds = 8;

    /// <summary>Annunciator place; a mock place (no Session signal) is drawn with a dashed frame.</summary>
    sealed partial class PlacePanel : PanelContainer
    {
        public bool Mock;
        public override void _Draw()
        {
            if (!Mock) return;
            var s = Size; var c = Ui.Ink3;
            DrawDashedLine(new Vector2(0, 0), new Vector2(s.X, 0), c, 1.5f, 3f); DrawDashedLine(new Vector2(0, s.Y), new Vector2(s.X, s.Y), c, 1.5f, 3f);
            DrawDashedLine(new Vector2(0, 0), new Vector2(0, s.Y), c, 1.5f, 3f); DrawDashedLine(new Vector2(s.X, 0), new Vector2(s.X, s.Y), c, 1.5f, 3f);
        }
    }

    sealed class Place
    {
        public string Key; public bool Real; public Color Plate; public Label Node; public PlacePanel Box; public Panel Marker;
        public PState State; public int Count; public bool Cond; public double ClearedAt;
    }

    readonly SimHost _host;
    readonly Label _date = Ui.Lbl("—", 18, null, FontKind.Mono);
    readonly Label _perf = Ui.Lbl("", 14, Ui.Ink3, FontKind.Mono);
    readonly TimeControl _speed;
    readonly Place[] _places;
    readonly Place _fail;
    readonly Control _spacer;
    readonly int _actor;   // signals and orders belong to the player's own power only
    double _alloyDeficit; bool _alloyKnown;
    long _seq = -1; long _rejects, _lastCmdSeq;
    /// <summary>A place got a signal (true) or lost it (false); the key is the place key (sig.alloy, ...). Sounds follow this event.</summary>
    public event Action<string, bool> SignalChanged;
    public event Action JournalPressed;
    public event Action MenuPressed;

    public int Actor => _actor;
    /// <summary>True while the signal of this place key is active (new or acknowledged), for the rail badges.</summary>
    public bool Active(string key) { foreach (var p in _places.Append(_fail)) if (p.Key == key) return p.Cond; return false; }
    /// <summary>Screen rectangle of a place (screenshot checks of the validator rule T3) and of the free stretch of the bar (a textured control area).</summary>
    public Rect2 PlateRect(string key) { foreach (var p in _places.Append(_fail)) if (p.Key == key) return p.Box.GetGlobalRect(); return default; }
    public Rect2 FreeRect => _spacer.GetGlobalRect();
    public bool Debug { set => _perf.Visible = value; }
    public void SetSpeed(int s) { _speed.Select(s, false); _host.Speed = _speed.Selected; }
    public void StepSpeed(int d) => _speed.Step(d);
    public void TogglePause() => _speed.TogglePause();
    /// <summary>Screenshot run: acknowledges every new signal as a click would.</summary>
    public void AckAll() { foreach (var p in _places.Append(_fail)) if (p.State == PState.New) { p.State = PState.Acked; Paint(p); } }
    
    public SignalField(SimHost host, int player)
    {
        _host = host; _actor = player;
        CustomMinimumSize = new Vector2(0, 46);
        AddThemeStyleboxOverride("panel", Ui.Box(Ui.Paper, Ui.Ink, 1, 12, 0, 10));
        PaperSurface.Apply(this, SurfaceKind.Paper, "top");
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        AddChild(row);

        _date.CustomMinimumSize = new Vector2(112, 0);
        _date.TooltipText = "tip.date"; _date.MouseFilter = MouseFilterEnum.Pass;
        row.AddChild(_date);

        _speed = new TimeControl();
        _speed.Changed += i => _host.Speed = i;
        row.AddChild(_speed);

        row.AddChild(Ui.VRule());
        _places = new[]
        {
            new Place { Key = "sig.alloy", Real = true, Plate = Ui.Warn }, new Place { Key = "sig.licence", Real = true, Plate = Ui.Warn },
            new Place { Key = "sig.refusal", Real = true, Plate = Ui.Alarm },
            new Place { Key = "sig.goal" }, new Place { Key = "sig.gap" }, new Place { Key = "sig.war" },
        };
        foreach (var p in _places) row.AddChild(MakePlace(p));
        _fail = new Place { Key = "sig.fail", Real = true, Plate = Ui.Alarm };
        var fb = MakePlace(_fail); fb.Visible = false; row.AddChild(fb);

        _spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(_spacer);
        row.AddChild(_perf);
        // The sound button left the bar (#352): sound lives in Settings. The journal of signals took its place.
        var jr = Ui.Btn("", () => JournalPressed?.Invoke());
        jr.TooltipText = "tip.journal"; jr.CustomMinimumSize = new Vector2(34, 34);
        jr.AddChild(new TextureRect { Texture = UiArt.Icon("journal"), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore, Position = new Vector2(5, 5), Size = new Vector2(24, 24) });
        row.AddChild(jr);
        row.AddChild(Ui.VRule());
        var menu = new MenuButton3Lines { TooltipText = "tip.menu" };
        menu.Pressed += () => MenuPressed?.Invoke();
        row.AddChild(menu);
        _perf.Visible = false;
        Loc.Changed += Refresh;
        TreeExiting += () => Loc.Changed -= Refresh;
    }

    Control MakePlace(Place p)
    {
        p.Box = new PlacePanel { Mock = !p.Real, MouseFilter = MouseFilterEnum.Stop };
        p.Node = Ui.Lbl(p.Key, 14, Ui.Ink3);
        p.Marker = new Panel { CustomMinimumSize = new Vector2(9, 9), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        var inner = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        inner.AddThemeConstantOverride("separation", 5);
        inner.AddChild(p.Marker); inner.AddChild(p.Node);
        p.Box.AddChild(inner);
        p.Box.TooltipText = p.Real ? "tip.sig.real" : "tip.sig.mock";
        p.Box.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && p.State == PState.New) { p.State = PState.Acked; Paint(p); }
        };
        Paint(p);
        return p.Box;
    }

    void Paint(Place p)
    {
        Color marker = p.Plate == Ui.Warn ? Ui.Warn : Ui.Alarm;
        switch (p.State)
        {
            case PState.New:   // plate + double frame
                p.Box.AddThemeStyleboxOverride("panel", Ui.Box(p.Plate, Ui.Ink, 3, 6, Ui.R1));
                p.Node.AddThemeColorOverride("font_color", p.Plate == Ui.Alarm ? Colors.White : Ui.Ink);
                p.Marker.Visible = false;
                break;
            case PState.Acked:   // the same calm frame as at rest; the deviation is only the filled ■
                p.Box.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Paper, Ui.Rule, 1, 6, Ui.R1));
                p.Node.AddThemeColorOverride("font_color", Ui.Ink);
                SetMarker(p, marker, true);
                break;
            case PState.Cleared:   // hollow □: the signal was lifted
                p.Box.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Paper, Ui.Rule, 1, 6, Ui.R1));
                p.Node.AddThemeColorOverride("font_color", Ui.Ink2);
                SetMarker(p, Ui.Ink2, false);
                break;
            default:
                p.Box.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Paper, p.Real ? Ui.Rule : Colors.Transparent, 1, 6, Ui.R1));
                p.Node.AddThemeColorOverride("font_color", Ui.Ink3);
                p.Marker.Visible = false;
                break;
        }
        // A new alert carries its sign (▲ warn, ■ alarm), a second channel besides colour (#326).
        string sign = p.State == PState.New ? (p.Plate == Ui.Warn ? "▲ " : "■ ") : "";
        p.Node.Text = p.Count > 0 && p.State is PState.New or PState.Acked ? sign + Loc.T(p.Key) + " " + p.Count : p.State == PState.New ? sign + Loc.T(p.Key) : p.Key;
        p.Box.TooltipText = p.State == PState.Cleared ? "tip.sig.cleared" : p.Real ? "tip.sig.real" : "tip.sig.mock";
        p.Box.QueueRedraw();
    }

    static void SetMarker(Place p, Color c, bool filled)
    {
        var sb = new StyleBoxFlat { BgColor = filled ? c : Colors.Transparent, BorderColor = c };
        sb.SetBorderWidthAll(filled ? 0 : 2);
        p.Marker.AddThemeStyleboxOverride("panel", sb);
        p.Marker.Visible = true;
    }

    void Set(Place p, bool cond, int count = 0)
    {
        bool was = p.Cond;
        if (cond && !was) p.State = PState.New;                                  // appears: unacknowledged
        else if (!cond && was) { p.State = PState.Cleared; p.ClearedAt = Time.GetTicksMsec() / 1000.0; }
        p.Cond = cond;
        p.Count = count;
        Paint(p);
        if (cond != was && p.Real) SignalChanged?.Invoke(p.Key, cond);
    }

    void Refresh() { _seq = -1; foreach (var p in _places) Paint(p); Paint(_fail); }

    public override void _Process(double delta)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        foreach (var p in _places.Append(_fail))
            if (p.State == PState.Cleared && now - p.ClearedAt > ClearedSeconds) { p.State = PState.Calm; Paint(p); }
        if (_host.Fatal != null && !_fail.Cond)
        {
            _fail.Box.Visible = true; _fail.Box.TooltipText = _host.Fatal;
            Set(_fail, true);
        }
        var s = _host.Snap;
        if (s == null) { _date.Text = _host.Fatal != null ? Loc.T("sig.fail") : Loc.T(_host.Status); return; }
        _date.Text = Settings.Date(s.Date);
        _perf.Text = $"tick {s.Tick} · {s.LastTickMs.ToString("F1", CultureInfo.InvariantCulture)} ms" + (s.JobError != "" ? " · " + s.JobError : "");
        if (s.Seq == _seq) return;
        _seq = s.Seq;
        if (s.CmdSeq != _lastCmdSeq) { _lastCmdSeq = s.CmdSeq; if (s.LastRejected) _rejects++; }
        // Inputs.Actors is indexed by its own Actor field, not by position.
        var inp = s.View.Inputs?.Actors.FirstOrDefault(x => x.Actor == _actor);
        if (inp != null) { _alloyDeficit = inp.Capability.AlloyDeficit.ToDouble(); _alloyKnown = true; }
        var a = s.View.Actors[_actor];
        long refusals = a.Licence.Refusals.IsDefault ? 0 : a.Licence.Refusals.Sum();
        Set(_places[0], _alloyKnown && _alloyDeficit > 0);
        Set(_places[1], refusals > 0, (int)Math.Min(refusals, 99));
        Set(_places[2], _rejects > 0 && s.LastRejected, (int)Math.Min(_rejects, 99));
    }
}

/// <summary>Menu button: three lines on a radius.2 plate (#302: the language switch left the signal field, the menu took its place).</summary>
public sealed partial class MenuButton3Lines : Button
{
    public MenuButton3Lines()
    {
        CustomMinimumSize = new Vector2(34, 30); FocusMode = FocusModeEnum.None;
        AddThemeStyleboxOverride("normal", Ui.Box(Ui.Sheet, Ui.Ink, 1, 0, Ui.R2));
        AddThemeStyleboxOverride("hover", Ui.Box(Ui.Hover, Ui.Ink, 1, 0, Ui.R2));
        AddThemeStyleboxOverride("pressed", Ui.Box(Ui.Hover, Ui.Ink, 2, 0, Ui.R2));
    }

    public override void _Draw()
    {
        var c = Size * 0.5f;
        for (int i = -1; i <= 1; i++) DrawLine(c + new Vector2(-8, i * 5), c + new Vector2(8, i * 5), Ui.Ink, 1.5f);
    }
}
