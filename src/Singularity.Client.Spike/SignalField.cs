using System;
using System.Linq;
using Godot;
using Singularity.Core.Commands;

namespace Singularity.Client.Spike;

/// <summary>Top signal field (design system §7.5): date, tempo segment, power selector, six annunciator places.</summary>
public sealed partial class SignalField : PanelContainer
{
    sealed class Place { public string Name; public bool Real; public Label Node; public PanelContainer Box; }

    readonly SimHost _host;
    readonly Label _date = Ui.Lbl("—", 18);
    readonly Label _perf = Ui.Lbl("", 11, Ui.Ink3);
    readonly Segment _speed, _power;
    readonly Place[] _places;
    int _actor;
    double _alloyDeficit; bool _alloyKnown;
    public event Action<int> PowerChosen;

    public int Actor => _actor;
    public void SelectPower(int i) { _actor = i; _power.Select(i, false); }
    public void SetSpeed(int s) { _speed.Select(s, false); _host.Speed = s; }

    public SignalField(SimHost host, string[] actors)
    {
        _host = host;
        CustomMinimumSize = new Vector2(0, 44);
        AddThemeStyleboxOverride("panel", Ui.Box(Ui.Paper, Ui.Ink, 1, 8));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        AddChild(row);

        row.AddChild(Ui.Lbl("ДАТА", 11, Ui.Ink3));
        _date.CustomMinimumSize = new Vector2(120, 0);
        row.AddChild(_date);

        row.AddChild(Ui.Lbl("ТЕМП", 11, Ui.Ink3));
        _speed = new Segment(new[] { "‖ пауза", "1 день/с", "неделя/с", "месяц/с" }, 0, 13);
        _speed.Changed += i => _host.Speed = i;
        row.AddChild(_speed);

        row.AddChild(Ui.Lbl("ДЕРЖАВА", 11, Ui.Ink3));
        _power = new Segment(actors, 0, 13);
        _power.Changed += i => { _actor = i; PowerChosen?.Invoke(i); };
        row.AddChild(_power);

        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        row.AddChild(Ui.Lbl("СИГНАЛЫ", 11, Ui.Ink3));
        _places = new[]
        {
            new Place { Name = "СПЛАВ", Real = true }, new Place { Name = "ЛИЦЕНЗИЯ", Real = true }, new Place { Name = "ОТКАЗ", Real = true },
            new Place { Name = "ЦЕЛЬ ◦" }, new Place { Name = "РАЗРЫВ ◦" }, new Place { Name = "ВОЙНА ◦" },
        };
        foreach (var p in _places)
        {
            p.Box = new PanelContainer();
            p.Node = Ui.Lbl(p.Name, 12, Ui.Ink3);
            p.Box.AddChild(p.Node);
            p.Box.TooltipText = p.Real ? "Данные Session" : "Макет: сигнала в Session нет";
            Paint(p, 0);
            row.AddChild(p.Box);
        }
        row.AddChild(Ui.Mock("◦ = макет"));
        row.AddChild(_perf);
    }

    static void Paint(Place p, int state)
    {
        // 0 calm, 1 active (ink plate), 2 new, not acknowledged (ink plate + double frame via border)
        if (state == 0) { p.Box.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Paper, Ui.Rule, 1, 6)); p.Node.AddThemeColorOverride("font_color", Ui.Ink3); }
        else if (state == 1) { p.Box.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Warn, null, 0, 6)); p.Node.AddThemeColorOverride("font_color", Ui.Ink); }
        else { p.Box.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Warn, Ui.Ink, 2, 6)); p.Node.AddThemeColorOverride("font_color", Ui.Ink); }
    }

    long _seq = -1;
    public override void _Process(double delta)
    {
        var s = _host.Snap;
        if (s == null) { _date.Text = _host.Fatal != null ? "ошибка" : _host.Status; return; }
        _date.Text = s.Date.ToString();
        _perf.Text = $"тик {s.Tick} · {s.LastTickMs:F1} мс";
        if (s.Seq == _seq) return;
        _seq = s.Seq;
        if (s.View.Inputs != null && _actor < s.View.Inputs.Actors.Length)
        {
            _alloyDeficit = s.View.Inputs.Actors[_actor].Capability.AlloyDeficit.ToDouble(); _alloyKnown = true;
        }
        var a = s.View.Actors[_actor];
        long refusals = a.Licence.Refusals.IsDefault ? 0 : a.Licence.Refusals.Sum();
        Paint(_places[0], _alloyKnown && _alloyDeficit > 0 ? 1 : 0);
        Paint(_places[1], refusals > 0 ? 1 : 0);
        Paint(_places[2], s.LastVerdict.Contains("отказ") ? 2 : 0);
    }
}
