using System;
using System.Collections.Immutable;
using System.Linq;
using Godot;
using Singularity.Core.Commands;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Core.WhyNot;
using Singularity.Kernel;
using Singularity.Session;

namespace Singularity.Client.Spike;

/// <summary>Actor inset: real data of ActorView. Budget sliders send SetBudgetShares; design rows send CancelDesign / CloseLine; why_not from Session.</summary>
public sealed partial class ActorPanel : PanelContainer
{
    static readonly string[] ShareNames = { "НИОКР (R)", "Мощности (cap)", "Освоение (D)", "Закупки", "Качество (QP)" };

    readonly SimHost _host;
    readonly VBoxContainer _root = new();
    readonly Label _title = Ui.Lbl("", 18);
    readonly Label _sub = Ui.Wrap("", 12, Ui.Ink2);
    readonly Label _budget = Ui.Wrap("", 13);
    readonly HSlider[] _sl = new HSlider[5];
    readonly Label[] _slVal = new Label[5], _slForce = new Label[5];
    readonly Label _sum = Ui.Lbl("", 13), _verdict = Ui.Wrap("", 12, Ui.Command);
    readonly Button _apply;
    readonly VBoxContainer _designs = new(), _licence = new(), _why = new();
    readonly int[] _pct = new int[5];
    bool _edited; int _actor = -1; long _seq = -1; SliceView _lastView; string _lastVerdict;
    public event Action OpenTree;
    public event Action Closed;

    public int Actor => _actor;

    public ActorPanel(SimHost host)
    {
        _host = host;
        CustomMinimumSize = new Vector2(400, 0);
        AddThemeStyleboxOverride("panel", Ui.Box(Ui.Sheet, Ui.Ink, 2, 12));
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(scroll);
        _root.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _root.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_root);

        var head = new HBoxContainer();
        var tb = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        tb.AddChild(_title); tb.AddChild(_sub);
        head.AddChild(tb);
        head.AddChild(Ui.Btn("Дерево знаний", () => OpenTree?.Invoke(), 13));
        head.AddChild(Ui.Btn("×", () => Closed?.Invoke(), 14));
        _root.AddChild(head);

        Section("БЮДЖЕТ — доли (SetBudgetShares)");
        _root.AddChild(_budget);
        for (int i = 0; i < 5; i++)
        {
            int k = i;
            var r = new HBoxContainer();
            var n = Ui.Lbl(ShareNames[i], 13); n.CustomMinimumSize = new Vector2(120, 0);
            _sl[i] = new HSlider { MinValue = 0, MaxValue = 100, Step = 1, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 22) };
            StyleSlider(_sl[i]);
            _sl[i].ValueChanged += v => OnSlider(k, (int)v);
            _slVal[i] = Ui.Lbl("", 13); _slVal[i].CustomMinimumSize = new Vector2(44, 0);
            _slForce[i] = Ui.Lbl("", 11, Ui.Ink3); _slForce[i].CustomMinimumSize = new Vector2(84, 0);
            r.AddChild(n); r.AddChild(_sl[i]); r.AddChild(_slVal[i]); r.AddChild(_slForce[i]);
            _root.AddChild(r);
        }
        var ar = new HBoxContainer();
        _apply = Ui.Btn("Отдать приказ", Apply, 14);
        ar.AddChild(_apply); ar.AddChild(_sum);
        _root.AddChild(ar);
        _root.AddChild(_verdict);

        Section("ИЗДЕЛИЯ И ЛИНИИ (DesignView)");
        _root.AddChild(_designs);
        Section("ЛИЦЕНЗИЯ (LicenceView)");
        _root.AddChild(_licence);
        Section("ПОЧЕМУ НЕ (WhyNot)");
        _root.AddChild(_why);
    }

    static void StyleSlider(HSlider s)
    {
        var track = Ui.Box(Ui.Rule, null, 0, 0); track.ContentMarginTop = 2; track.ContentMarginBottom = 2;
        s.AddThemeStyleboxOverride("slider", track);
        s.AddThemeStyleboxOverride("grabber_area", Ui.Box(Ui.Ink, null, 0, 0));
        s.AddThemeStyleboxOverride("grabber_area_highlight", Ui.Box(Ui.Command, null, 0, 0));
        s.FocusMode = FocusModeEnum.None;
    }

    void Section(string text)
    {
        _root.AddChild(new HSeparator());
        _root.AddChild(Ui.Lbl(text, 11, Ui.Ink3));
    }

    public void ShowActor(int actor) { _actor = actor; _edited = false; _seq = -1; Visible = true; }

    void OnSlider(int i, int v)
    {
        _edited = true;
        int rest = 100 - v, others = 0;
        for (int k = 0; k < 5; k++) if (k != i) others += _pct[k];
        _pct[i] = v;
        int placed = 0, last = -1;
        for (int k = 0; k < 5; k++)
        {
            if (k == i) continue;
            _pct[k] = others == 0 ? rest / 4 : _pct[k] * rest / others;
            placed += _pct[k];
            if (last < 0 || _pct[k] >= _pct[last]) last = k;
        }
        _pct[last] += rest - placed; // largest other absorbs the rounding
        Sync();
    }

    void Sync()
    {
        for (int k = 0; k < 5; k++) { _sl[k].SetValueNoSignal(_pct[k]); _slVal[k].Text = _pct[k] + "%"; }
        _sum.Text = "  сумма " + _pct.Sum() + "%";
        _apply.Disabled = _pct.Sum() != 100 || !_edited;
    }

    static Fx6 Pct(int p) => Fx6.FromRaw(p * 10_000L);

    void Apply()
    {
        var sh = new BudgetShares(Pct(_pct[0]), Pct(_pct[1]), Pct(_pct[2]), Pct(_pct[3]), Pct(_pct[4]));
        _host.Submit(new SetBudgetShares(_actor, sh), "доли бюджета");
        _edited = false;
        _seq = -1;
    }

    public override void _Process(double delta)
    {
        var s = _host.Snap;
        if (s == null || _actor < 0 || !Visible) return;
        if (s.WhyActor != _actor) return;
        if (_seq == 0 && ReferenceEquals(s.View, _lastView) && s.LastVerdict == _lastVerdict) return;
        _seq = 0; _lastView = s.View; _lastVerdict = s.LastVerdict;
        var a = s.View.Actors[_actor];
        string id = _host.Layout.ActorIds[_actor];
        _title.Text = $"{id} — {(Names.RuOfActor.TryGetValue(id, out var ru) ? ru : id)}";
        _sub.Text = $"профиль «{Names.Profile[(int)a.Profile]}» · поколение в строю G{a.FieldedGen} · месяц {s.View.Month}";
        var inA = s.View.Inputs?.Actors.FirstOrDefault(x => x.Actor == _actor);
        _budget.Text = inA != null ? $"Бюджет месяца: {inA.Budget.ToDouble():F1} (ед. сценария), закупки {inA.ProcurementBudget.ToDouble():F1}" : "Бюджет месяца: ещё не считался";
        BudgetShares? inForce = s.SharesInForce[_actor];
        BudgetShares show = inForce ?? new PolicyParams().Shares(a.Profile);
        if (!_edited)
        {
            _pct[0] = Pc(show.Research); _pct[1] = Pc(show.Capability); _pct[2] = Pc(show.Effort); _pct[3] = Pc(show.Procurement); _pct[4] = Pc(show.Quality);
            _pct[3] += 100 - _pct.Sum();
            Sync();
        }
        Fx6[] f = inForce is { } b ? new[] { b.Research, b.Capability, b.Effort, b.Procurement, b.Quality } : null;
        for (int k = 0; k < 5; k++) _slForce[k].Text = f != null ? $"в силе {Pc(f[k])}%" : "по профилю";
        _verdict.Text = (s.LastVerdict + (s.LastOutcome != "" ? "\nСессия: " + s.LastOutcome : "")).Trim();
        FillDesigns(a);
        FillLicence(a);
        FillWhy(s);
    }

    static int Pc(Fx6 x) => (int)((x.Raw + 5_000) / 10_000);

    static void Clear(Container c) { foreach (var n in c.GetChildren()) { c.RemoveChild(n); n.QueueFree(); } }

    void FillDesigns(ActorView a)
    {
        Clear(_designs);
        bool any = false;
        foreach (var d in a.Designs)
        {
            if (d.Stage == DesignStage.None) continue;
            any = true;
            var r = new HBoxContainer();
            string line = $"#{d.Design} G{d.Gen} · {Names.Stage[(int)d.Stage]}" + (d.Stage is DesignStage.Series ? $" · темп {d.Rate.ToDouble():F2} · лимит {d.Bind}" : "")
                + $" · себест. {d.UnitCost.ToDouble():F2} · готовность {d.Readiness.ToDouble():F2} · в строю {d.InService.ToDouble():F0}";
            var l = Ui.Wrap(line, 12);
            r.AddChild(l);
            int design = d.Design;
            if (d.Stage == DesignStage.Series) r.AddChild(Ui.Btn("Закрыть линию", () => _host.Submit(new CloseLine(_actor, design), $"закрыть линию #{design}"), 12));
            else if (d.Stage is DesignStage.Project or DesignStage.Prototype) r.AddChild(Ui.Btn("Снять", () => _host.Submit(new CancelDesign(_actor, design), $"снять проект #{design}"), 12));
            _designs.AddChild(r);
        }
        if (!any) _designs.AddChild(Ui.Wrap("Проектов нет. Создание (ComposeDesign) — нужен API списка альтернатив слотов: не подключено.", 12, Ui.Ink2));
        else { var m = Ui.Wrap("Запуск серии (StartSeries) и создание (ComposeDesign): нужны темп/альтернативы — не подключено.", 11, Ui.Ink3); _designs.AddChild(m); }
    }

    void FillLicence(ActorView a)
    {
        Clear(_licence);
        var L = a.Licence;
        string seller = L.Seller >= 0 && L.Seller < _host.Layout.ActorIds.Length ? _host.Layout.ActorIds[L.Seller] : "—";
        long refus = L.Refusals.IsDefault ? 0 : L.Refusals.Sum();
        _licence.AddChild(Ui.Wrap(L.Active ? $"Лицензия активна: поколение G{L.Gen}, продавец {seller}, с месяца {L.Start}, поток {L.Flow.ToDouble():F2}" : "Лицензии нет (не действует)", 12));
        if (refus > 0)
            _licence.AddChild(Ui.Wrap($"Отказов BAD_LICENCE: {refus} (свой {L.Refusals[0]}, пусто {L.Refusals[1]}, поколение {L.Refusals[2]}, деньги {L.Refusals[3]})", 12, Ui.Ink2));
    }

    void FillWhy(HostSnapshot s)
    {
        Clear(_why);
        if (s.WhyError != "") { _why.AddChild(Ui.Wrap(s.WhyError, 12, Ui.Alarm)); return; }
        if (s.Target is not { } t) { _why.AddChild(Ui.Wrap("Цели нет: нового известного изделия, не принятого на вооружение, нет.", 12, Ui.Ink2)); return; }
        _why.AddChild(Ui.Wrap($"Цель: {t.Kind} {t.Id} (годовое требование). Звеньев: {s.Links.Count}", 12, Ui.Ink2));
        int n = 0;
        foreach (var l in s.Links)
        {
            if (n++ >= 7) { _why.AddChild(Ui.Wrap($"… ещё {s.Links.Count - 7}", 12, Ui.Ink3)); break; }
            string subj = l.Layer == WhyNotLayer.Knowledge ? Names.Node(_host.Data, l.SubjectId) : "#" + l.SubjectId;
            string eta = l.EtaMonths is int m ? $" · срок {m} мес" : "";
            string lev = l.Levers.IsDefaultOrEmpty ? "" : " · рычаги: " + string.Join(", ", l.Levers);
            var lab = Ui.Wrap($"{(n == 1 ? "■ " : "▫ ")}{l.Layer}/{l.Cause} {subj} · {l.Have.ToDouble():F2}/{l.Need.ToDouble():F2}{eta}{lev}", 12, n == 1 ? Ui.Alarm : Ui.Ink);
            _why.AddChild(lab);
        }
    }
}
