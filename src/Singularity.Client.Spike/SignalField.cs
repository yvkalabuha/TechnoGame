using System;
using System.Linq;
using Godot;

namespace Singularity.Client.Spike;

/// <summary>Top bar (design system §7.5). Left: controls (date, tempo, power, language). Right, after a vertical rule: the six annunciator
/// places and the simulation-failure place. Place states: calm, new (plate + double frame, until acknowledged by a click), acknowledged, cleared.</summary>
public sealed partial class SignalField : PanelContainer
{
    enum PState { Calm, New, Acked }

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
        public string Key; public bool Real; public Color Plate; public Label Node; public PlacePanel Box;
        public PState State; public int Count; public bool Cond;
    }

    readonly SimHost _host;
    readonly Label _date = Ui.Lbl("—", 18, null, FontKind.Mono);
    readonly Label _perf = Ui.Lbl("", 14, Ui.Ink3, FontKind.Mono);
    readonly Segment _speed, _power, _lang;
    readonly Place[] _places;
    readonly Place _fail;
    int _actor;
    double _alloyDeficit; bool _alloyKnown;
    long _seq = -1; long _rejects, _lastCmdSeq;
    public event Action<int> PowerChosen;

    public int Actor => _actor;
    public bool Debug { set => _perf.Visible = value; }
    public void SelectPower(int i) { _actor = i; _power.Select(i, false); }
    public void SetSpeed(int s) { _speed.Select(s, false); _host.Speed = s; }
    /// <summary>Screenshot run: acknowledges every new signal as a click would.</summary>
    public void AckAll() { foreach (var p in _places) if (p.State == PState.New) { p.State = PState.Acked; Paint(p); } }
    public void SetLang(string lang) => _lang.Select(Array.IndexOf(Loc.Languages, lang), true);

    public SignalField(SimHost host, string[] actors)
    {
        _host = host;
        CustomMinimumSize = new Vector2(0, 46);
        AddThemeStyleboxOverride("panel", Ui.Box(Ui.Paper, Ui.Ink, 1, 8));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        AddChild(row);

        _date.CustomMinimumSize = new Vector2(112, 0);
        _date.TooltipText = "tip.date"; _date.MouseFilter = MouseFilterEnum.Pass;
        row.AddChild(_date);

        _speed = new Segment(new[] { "speed.pause", "speed.day", "speed.week", "speed.month" }, 0);
        _speed.TooltipText = "tip.tempo";
        _speed.Changed += i => _host.Speed = i;
        row.AddChild(_speed);

        _power = new Segment(actors, 0, 14, false);
        _power.TooltipText = "tip.power";
        _power.Changed += i => { _actor = i; PowerChosen?.Invoke(i); };
        row.AddChild(_power);

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

        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        row.AddChild(_perf);
        row.AddChild(Ui.VRule());
        _lang = new Segment(new[] { "RU", "EN" }, 0, 14, false);
        _lang.TooltipText = "tip.lang";
        _lang.Changed += i => Loc.SetLang(Loc.Languages[i]);
        row.AddChild(_lang);
        _perf.Visible = false;
        Loc.Changed += Refresh;
        TreeExiting += () => Loc.Changed -= Refresh;
    }

    Control MakePlace(Place p)
    {
        p.Box = new PlacePanel { Mock = !p.Real, MouseFilter = MouseFilterEnum.Stop };
        p.Node = Ui.Lbl(p.Key, 14, Ui.Ink3);
        p.Box.AddChild(p.Node);
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
        // Calm: rule frame, ink-3 name. New: plate + double ink frame. Acknowledged: plate, single ink frame.
        if (p.State == PState.Calm) { p.Box.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Paper, p.Real ? Ui.Rule : Colors.Transparent, 1, 6)); p.Node.AddThemeColorOverride("font_color", Ui.Ink3); }
        else
        {
            var sb = Ui.Box(p.Plate, Ui.Ink, p.State == PState.New ? 3 : 1, 6);
            p.Box.AddThemeStyleboxOverride("panel", sb);
            p.Node.AddThemeColorOverride("font_color", p.Plate == Ui.Alarm ? Colors.White : Ui.Ink);
        }
        p.Node.Text = p.Count > 0 && p.State != PState.Calm ? Loc.T(p.Key) + " " + p.Count : p.Key;
        p.Box.QueueRedraw();
    }

    void Set(Place p, bool cond, int count = 0)
    {
        if (cond && !p.Cond) p.State = PState.New;   // appears: unacknowledged
        if (!cond) p.State = PState.Calm;            // cleared
        p.Cond = cond;
        p.Count = count;
        Paint(p);
    }

    void Refresh() { _seq = -1; foreach (var p in _places) Paint(p); Paint(_fail); }

    public override void _Process(double delta)
    {
        if (_host.Fatal != null && !_fail.Cond)
        {
            _fail.Box.Visible = true; _fail.Box.TooltipText = _host.Fatal;
            Set(_fail, true);
        }
        var s = _host.Snap;
        if (s == null) { _date.Text = _host.Fatal != null ? Loc.T("sig.fail") : Loc.T(_host.Status); return; }
        _date.Text = s.Date.ToString();
        _perf.Text = $"tick {s.Tick} · {s.LastTickMs:F1} ms";
        if (s.Seq == _seq) return;
        _seq = s.Seq;
        if (s.CmdSeq != _lastCmdSeq) { _lastCmdSeq = s.CmdSeq; if (s.LastRejected) _rejects++; }
        if (s.View.Inputs != null && _actor < s.View.Inputs.Actors.Length)
        {
            _alloyDeficit = s.View.Inputs.Actors[_actor].Capability.AlloyDeficit.ToDouble(); _alloyKnown = true;
        }
        var a = s.View.Actors[_actor];
        long refusals = a.Licence.Refusals.IsDefault ? 0 : a.Licence.Refusals.Sum();
        Set(_places[0], _alloyKnown && _alloyDeficit > 0);
        Set(_places[1], refusals > 0, (int)Math.Min(refusals, 99));
        Set(_places[2], _rejects > 0 && s.LastRejected, (int)Math.Min(_rejects, 99));
    }
}
