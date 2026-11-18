using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Singularity.Core.Commands;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Core.WhyNot;
using Singularity.Kernel;
using Singularity.Session;

namespace Singularity.Client.Spike;

/// <summary>Actor inset (a pasted sheet sized by its content): real ActorView data. Budget sliders send SetBudgetShares; design rows send
/// CancelDesign / CloseLine; why_not from Session. A command sent on pause stays «pending» with the slider values kept.</summary>
public sealed partial class ActorPanel : PanelContainer
{
    const int PanelWidth = 520, AreaPanelWidth = 420;

    readonly SimHost _host;
    readonly VBoxContainer _root = new();
    readonly MarginContainer _pad = new();   // right inset of 12 du while the scroll bar shows, so that it never covers the last column
    readonly ScrollContainer _scroll = new() { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    readonly Label _title = Ui.Lbl("", 20, null, FontKind.Draft);
    readonly Prose _sub = Ui.Wrap("", 14, Ui.Ink2);
    readonly Prose _budget = Ui.Wrap("", 14);
    readonly HSlider[] _sl = new HSlider[5];
    readonly Label[] _slVal = new Label[5], _slForce = new Label[5];
    readonly Label _sum = Ui.Lbl("", 14, Ui.Ink2, FontKind.Mono);
    readonly Prose _result = Ui.Wrap("", 14, Ui.Ink2);
    readonly Control _resultMark = new ColorRect { Color = Ui.Alarm, CustomMinimumSize = new Vector2(9, 9), SizeFlagsVertical = SizeFlags.ShrinkCenter, Visible = false };
    readonly CaptionButton _apply;
    readonly VBoxContainer _designs = new(), _licence = new(), _why = new();
    readonly int[] _pct = new int[5];
    int[] _sentPct;
    bool _edited, _awaiting; long _sentSeq;
    int _actor = -1; bool _dirty = true; SliceView _lastView; CmdResult _lastCmd; HostSnapshot _lastSnap;
    string _sigDesigns = "", _sigLicence = "", _sigWhy = "";
    public event Action OpenTree;
    public event Action Closed;

    public int Actor => _actor;
    /// <summary>The player's own power: only it has order controls; any other power opens read-only.</summary>
    public int Player { get; set; }
    bool Foreign => _actor != Player;
    // Tag of the selected foreign power (spec §9): tint chip, name, «FOREIGN POWER · read only» and a button back to the player's own power. Not an alert: ink on paper.
    readonly PanelContainer _foreign = new();
    readonly ColorRect _chip = new() { CustomMinimumSize = new Vector2(16, 16), SizeFlagsVertical = SizeFlags.ShrinkCenter };
    readonly Label _foreignName = Ui.Lbl("", 16, null, FontKind.Draft);
    public Func<int, Color> TintOf;
    public event Action BackToOwn;
    readonly List<Control> _orderControls = new();
    // Shares of a foreign power come from its AI doctrine profile (PolicyParams), not from per-country data: the panel says so.
    readonly Label _sharesNote = Ui.Lbl("panel.shares.profile", 14, Ui.Ink3);
    Control _designsHead;
    bool _debug;
    /// <summary>F3 switches Session text on and off: the panel is rebuilt at once.</summary>
    public bool Debug { get => _debug; set { if (_debug == value) return; _debug = value; Invalidate(); _dirty = true; } }

    // Area mode (v0.5c): the player's own power opens in the left area panels: Science (why_not and the tree), Production (design lines), Finance (budget, licences).
    // The right panel without an area is the read-only view of a foreign power with every section.
    readonly bool _areaMode;
    readonly VBoxContainer _secBudget = new(), _secDesigns = new(), _secLicence = new(), _secWhy = new();
    Control _treeBtn, _headerView, _prodLater, _finLater;
    AreaId _area = AreaId.Map;
    public AreaId Area
    {
        get => _area;
        set
        {
            _area = value;
            if (_headerView != null) { _root.RemoveChild(_headerView); _headerView.QueueFree(); _headerView = null; }
            if (_areaMode)
            {
                _headerView = new AreaHeader(AreaData.Of(value), false);
                _root.AddChild(_headerView); _root.MoveChild(_headerView, 0);
                _secBudget.Visible = _secLicence.Visible = _finLater.Visible = value == AreaId.Finance;
                _secDesigns.Visible = _prodLater.Visible = value == AreaId.Production;
                _secWhy.Visible = _treeBtn.Visible = value == AreaId.Science;
            }
        }
    }

    public ActorPanel(SimHost host, bool areaMode = false)
    {
        _host = host; _areaMode = areaMode;
        CustomMinimumSize = new Vector2(areaMode ? AreaPanelWidth : PanelWidth, 0);
        AddThemeStyleboxOverride("panel", Ui.Box(Ui.Sheet, Ui.Ink, 2, 12, Ui.R3, 12));
        PaperSurface.Apply(this, SurfaceKind.Sheet, areaMode ? "area" : "actor");
        AddChild(_scroll);
        _root.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _root.AddThemeConstantOverride("separation", 6);
        _pad.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _pad.AddChild(_root);
        _scroll.AddChild(_pad);

        var head = new HBoxContainer();
        var tb = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        tb.AddChild(_title); tb.AddChild(_sub);
        head.AddChild(tb);
        head.AddChild(Ui.Btn("ui.tree", () => OpenTree?.Invoke()));
        head.AddChild(Ui.Btn("×", () => Closed?.Invoke()));
        _root.AddChild(head); head.Visible = !areaMode;
        var fv = new VBoxContainer();
        var fr = new HBoxContainer(); fr.AddThemeConstantOverride("separation", 8);
        fr.AddChild(_chip); fr.AddChild(_foreignName);
        fv.AddChild(fr);
        fv.AddChild(Ui.Lbl("panel.foreign", 14, Ui.Ink2, FontKind.Draft));
        var back = Ui.Btn("panel.backown", () => BackToOwn?.Invoke());
        back.TooltipText = "tip.backown";
        fv.AddChild(back);
        _foreign.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Paper, Ui.Ink, 1, 8, Ui.R2));
        _foreign.AddChild(fv);
        _foreign.Visible = false; _root.AddChild(_foreign);

        foreach (var sec in new[] { _secBudget, _secDesigns, _secLicence, _secWhy }) { sec.AddThemeConstantOverride("separation", 6); _root.AddChild(sec); }
        Section("panel.budget", _secBudget);
        _secBudget.AddChild(_budget);
        _sharesNote.Visible = false; _secBudget.AddChild(_sharesNote);
        var hd = new HBoxContainer();
        hd.AddChild(new Control { CustomMinimumSize = new Vector2(138, 0) });
        hd.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        hd.AddChild(Ui.Num("col.share", 14, Ui.Ink2, 52));
        hd.AddChild(Ui.Num("col.inforce", 14, Ui.Ink2, 84));
        _secBudget.AddChild(hd);
        for (int i = 0; i < 5; i++)
        {
            int k = i;
            var r = new HBoxContainer();
            var n = Ui.Lbl("share." + i, 14); n.CustomMinimumSize = new Vector2(138, 0);
            _sl[i] = new HSlider { MinValue = 0, MaxValue = 100, Step = 1, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(100, 22) };
            StyleSlider(_sl[i]);
            _sl[i].ValueChanged += v => OnSlider(k, (int)v);
            _slVal[i] = Ui.Num("", 14, null, 52);
            _slForce[i] = Ui.Num("", 14, Ui.Ink2, 84);
            r.AddChild(n); r.AddChild(_sl[i]); r.AddChild(_slVal[i]); r.AddChild(_slForce[i]);
            _secBudget.AddChild(r);
        }
        var ar = new HBoxContainer();
        ar.AddThemeConstantOverride("separation", 10);
        _apply = Ui.Btn("ui.apply", Apply);
        ar.AddChild(_apply); ar.AddChild(_sum);
        _secBudget.AddChild(ar); _orderControls.Add(ar);
        var rr = new HBoxContainer(); rr.AddThemeConstantOverride("separation", 6);
        rr.AddChild(_resultMark); rr.AddChild(_result);
        _secBudget.AddChild(rr); _orderControls.Add(rr);

        _designsHead = Section("panel.designs", _secDesigns);
        _secDesigns.AddChild(_designs);
        _prodLater = Ui.Wrap("area.production.later", 14, Ui.Ink3); _secDesigns.AddChild(_prodLater);
        Section("panel.licence", _secLicence);
        _secLicence.AddChild(_licence);
        _finLater = Ui.Wrap("area.finance.later", 14, Ui.Ink3); _secLicence.AddChild(_finLater);
        Section("panel.why", _secWhy);
        _treeBtn = Ui.Btn("ui.tree", () => OpenTree?.Invoke()); _treeBtn.SizeFlagsHorizontal = SizeFlags.ShrinkBegin; _secWhy.AddChild(_treeBtn);
        _secWhy.AddChild(_why);
        _prodLater.Visible = _finLater.Visible = _treeBtn.Visible = areaMode;
        Loc.Changed += Invalidate;
        TreeExiting += () => Loc.Changed -= Invalidate;
    }

    void Invalidate() { _dirty = true; _sigDesigns = _sigLicence = _sigWhy = ""; }

    public static void StyleSlider(HSlider s)
    {
        var track = Ui.Box(Ui.Rule, null, 0, 0); track.ContentMarginTop = 2; track.ContentMarginBottom = 2;
        s.AddThemeStyleboxOverride("slider", track);
        s.AddThemeStyleboxOverride("grabber_area", Ui.Box(Ui.Ink, null, 0, 0));
        s.AddThemeStyleboxOverride("grabber_area_highlight", Ui.Box(Ui.Ink, null, 0, 0));
        s.FocusMode = FocusModeEnum.None;
    }

    Control Section(string key, Container into)
    {
        var v = new VBoxContainer();
        v.AddChild(new HSeparator());
        v.AddChild(Ui.Head(key));
        into.AddChild(v);
        return v;
    }

    public void ShowActor(int actor) { _actor = actor; _edited = false; _awaiting = false; _sentPct = null; _dirty = true; Invalidate(); Visible = true; }

    /// <summary>Screenshot run: the same path as a user dragging slider <paramref name="i"/> and pressing the order button.</summary>
    public void ShotEdit(int i, int v) { OnSlider(i, v); Apply(); }

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

    bool Pending => _awaiting || (_lastSnap != null && _lastSnap.Pending > 0 && _sentPct != null);

    void Sync()
    {
        for (int k = 0; k < 5; k++) { _sl[k].SetValueNoSignal(_pct[k]); _slVal[k].Text = _pct[k] + "%"; }
        _sum.Text = Loc.T("ui.sum", _pct.Sum());
        bool changed = _sentPct == null || !_pct.SequenceEqual(_sentPct);
        bool ready = _pct.Sum() == 100 && _edited && changed && !Pending;
        _apply.Disabled = !ready;
        _apply.Text = Pending ? "ui.pending" : "ui.apply";
        Ui.StyleButton(_apply, ready);
    }

    static Fx6 Pct(int p) => Fx6.FromRaw(p * 10_000L);

    void Apply()
    {
        if (Foreign) return;
        var sh = new BudgetShares(Pct(_pct[0]), Pct(_pct[1]), Pct(_pct[2]), Pct(_pct[3]), Pct(_pct[4]));
        _host.Submit(new SetBudgetShares(_actor, sh), "cmd.budget");
        _sentPct = (int[])_pct.Clone();
        _awaiting = true; _sentSeq = _lastSnap?.CmdSeq ?? 0;
        Sync();
    }

    public override void _Process(double delta)
    {
        // Height follows the content (an inset, not a full-height column), capped by the window.
        float want = _root.GetCombinedMinimumSize().Y + 6, cap = GetViewportRect().Size.Y - 120;
        _scroll.CustomMinimumSize = new Vector2(0, Math.Min(want, cap));
        _pad.AddThemeConstantOverride("margin_right", want <= cap ? 0 : 12);
        _scroll.VerticalScrollMode = want <= cap ? ScrollContainer.ScrollMode.Disabled : ScrollContainer.ScrollMode.Auto;   // a scroll bar only when the content really does not fit
        var s = _host.Snap;
        if (s == null || _actor < 0 || !Visible) return;
        if (s.WhyActor != _actor) return;
        _lastSnap = s;
        bool changed = _dirty || !ReferenceEquals(s.View, _lastView) || !ReferenceEquals(s.Last, _lastCmd);
        if (_awaiting && s.CmdSeq > _sentSeq)
        {
            _awaiting = false;
            if (s.LastRejected || s.Last.Status == CmdStatus.Error) { _edited = false; _sentPct = null; }   // refused: back to the values in force
            changed = true;
        }
        if (!changed) return;
        _dirty = false; _lastView = s.View; _lastCmd = s.Last;
        var a = s.View.Actors[_actor];
        string id = _host.Layout.ActorIds[_actor];
        _title.Text = Names.Actor(id);
        _foreign.Visible = Foreign;
        if (Foreign) { _foreignName.Text = Names.Actor(_host.Layout.ActorIds[_actor]); if (TintOf != null) _chip.Color = TintOf(_actor); }
        foreach (var c in _orderControls) c.Visible = !Foreign;
        foreach (var sl in _sl) { sl.Editable = !Foreign; sl.MouseFilter = Foreign ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop; sl.Modulate = new Color(1, 1, 1, Foreign ? 0.45f : 1f); }
        _sharesNote.Visible = Foreign;
        _title.TooltipText = id;
        _sub.Text = Loc.T("panel.sub", Loc.T("profile." + (int)a.Profile), a.FieldedGen, s.View.Month >= 0 ? s.View.Month.ToString() : "—");
        var inA = s.View.Inputs?.Actors.FirstOrDefault(x => x.Actor == _actor);
        _budget.Text = inA != null ? Loc.T("panel.budget.text", Loc.N(inA.Budget.ToDouble(), 1), Loc.N(inA.ProcurementBudget.ToDouble(), 1)) : Loc.T("panel.budget.none");
        BudgetShares? inForce = s.SharesInForce[_actor];
        BudgetShares show = inForce ?? new PolicyParams().Shares(a.Profile);
        int[] forcePct = { Pc(show.Research), Pc(show.Capability), Pc(show.Effort), Pc(show.Procurement), Pc(show.Quality) };
        forcePct[3] += 100 - forcePct.Sum();
        // The edit stays (also while the command is pending) until the values in force catch up with it.
        if (_edited && _sentPct != null && !_awaiting && inForce != null && _sentPct.SequenceEqual(forcePct) && _pct.SequenceEqual(forcePct)) { _edited = false; _sentPct = null; }
        if (!_edited) { Array.Copy(forcePct, _pct, 5); }
        for (int k = 0; k < 5; k++) _slForce[k].Text = inForce != null ? forcePct[k] + "%" : "—";
        Sync();
        FillResult(s);
        FillDesigns(a);
        FillLicence(a);
        FillWhy(s);
    }

    void FillResult(HostSnapshot s)
    {
        var r = s.Last;
        _resultMark.Visible = r.Status is CmdStatus.Rejected or CmdStatus.Error;
        if (r.Status == CmdStatus.None) { _result.Text = ""; return; }
        string what = Loc.T(r.LabelKey, r.LabelArg);
        string why = r.Status switch
        {
            CmdStatus.Rejected => Loc.TOr("refusal." + r.Reason, r.Reason),
            CmdStatus.Error => r.Reason,
            _ => "",
        };
        _result.Text = Loc.T("cmd." + r.Status, what, why) + (Debug && r.Message != "" ? "\n" + r.Message : "") + (Debug && s.LastOutcome != "" ? "\n" + s.LastOutcome : "");
    }

    static int Pc(Fx6 x) => (int)((x.Raw + 5_000) / 10_000);

    static void Clear(Container c) { foreach (var n in c.GetChildren()) { c.RemoveChild(n); n.QueueFree(); } }

    void FillDesigns(ActorView a)
    {
        var rows = new List<(string text, string action, int design)>();
        foreach (var d in a.Designs)
        {
            if (d.Stage == DesignStage.None) continue;
            string line = Loc.T("design.line", d.Design, d.Gen, Loc.T("stage." + (int)d.Stage), Loc.N(d.UnitCost.ToDouble()), Loc.N(d.Readiness.ToDouble()), Loc.N(d.InService.ToDouble(), 0))
                + (d.Stage is DesignStage.Series ? Loc.T("design.series", Loc.N(d.Rate.ToDouble()), Loc.T("bind." + d.Bind)) : "");
            rows.Add((line, d.Stage == DesignStage.Series ? "close" : d.Stage is DesignStage.Project or DesignStage.Prototype ? "cancel" : "", d.Design));
        }
        string sig = Loc.Lang + "|" + string.Join("\n", rows.Select(r => r.text + r.action));
        if (sig == _sigDesigns) return;
        _sigDesigns = sig;
        Clear(_designs);
        bool hide = Foreign && rows.Count == 0;   // a foreign power without lines: no empty heading
        _designsHead.Visible = _designs.Visible = !hide;
        foreach (var (text, action, design) in rows)
        {
            var r = new HBoxContainer();
            r.AddChild(Ui.Wrap(text));
            Button act = null;
            if (!Foreign && action == "close") act = Ui.Btn("ui.closeline", () => _host.Submit(new CloseLine(_actor, design), "cmd.close", design));
            else if (!Foreign && action == "cancel") act = Ui.Btn("ui.cancel", () => _host.Submit(new CancelDesign(_actor, design), "cmd.cancel", design));
            if (act != null) { act.SizeFlagsVertical = SizeFlags.ShrinkBegin; r.AddChild(act); }
            _designs.AddChild(r);
        }
        var note = new HBoxContainer(); note.AddThemeConstantOverride("separation", 8);
        note.AddChild(Ui.Mock()); note.AddChild(Ui.Wrap(rows.Count == 0 ? "design.none" : "design.nocreate", 14, Ui.Ink3));
        if (!Foreign) _designs.AddChild(note);
    }

    void FillLicence(ActorView a)
    {
        var L = a.Licence;
        string seller = L.Seller >= 0 && L.Seller < _host.Layout.ActorIds.Length ? _host.Layout.ActorIds[L.Seller] : "—";
        long refus = L.Refusals.IsDefault ? 0 : L.Refusals.Sum();
        string t1 = L.Active ? Loc.T("licence.active", L.Gen, seller, L.Start, Loc.N(L.Flow.ToDouble())) : Loc.T("licence.none");
        string t2 = refus > 0 ? Loc.T("licence.refusals", refus, L.Refusals[0], L.Refusals[1], L.Refusals[2], L.Refusals[3]) : "";
        string sig = Loc.Lang + t1 + t2;
        if (sig == _sigLicence) return;
        _sigLicence = sig;
        Clear(_licence);
        _licence.AddChild(Ui.Wrap(t1));
        if (t2 != "") _licence.AddChild(Ui.Wrap(t2, 14, Ui.Ink2));
    }

    void FillWhy(HostSnapshot s)
    {
        var lines = new List<(string text, bool root)>();
        if (s.WhyError != "") lines.Add((Debug ? s.WhyError : Loc.T("why.error"), true));
        else if (s.Target is not { } t) lines.Add((Loc.T("why.none"), false));
        else
        {
            string goal = t.Kind == WhyNotTargetKind.Requirement ? Loc.T("why.goal", t.Id) : Loc.T("why.goalkind", t.Kind.ToString(), t.Id);
            lines.Add((goal + (Debug ? Loc.T("why.links", s.Links.Count) : ""), false));
            int n = 0;
            foreach (var l in s.Links)
            {
                if (n++ >= 7) { lines.Add((Loc.T("why.more", s.Links.Count - 7), false)); break; }
                string subj = l.Layer == WhyNotLayer.Knowledge ? Names.Node(_host.Data, l.SubjectId) : "№" + l.SubjectId;
                string eta = l.EtaMonths is int m ? Loc.T("why.eta", m) : "";
                string lev = l.Levers.IsDefaultOrEmpty ? "" : Loc.T("why.levers", string.Join(", ", l.Levers.Select(x => Loc.TOr("lever." + x, x.ToString()))));
                lines.Add(($"{Loc.TOr("layer." + l.Layer, l.Layer.ToString())} · {Loc.TOr("cause." + l.Cause, l.Cause.ToString())}: {subj} · {Loc.N(l.Have.ToDouble())}/{Loc.N(l.Need.ToDouble())}{eta}{lev}", n == 1));
            }
        }
        string sig = Loc.Lang + "|" + string.Join("\n", lines.Select(x => x.text));
        if (sig == _sigWhy) return;
        _sigWhy = sig;
        Clear(_why);
        for (int i = 0; i < lines.Count; i++)
        {
            bool first = lines[i].root;
            var lab = Ui.Wrap(lines[i].text, 14, i == 0 && !first ? Ui.Ink2 : Ui.Ink);
            _why.AddChild(first ? Ui.MarkedRow(Ui.Alarm, lab) : lab);
        }
    }
}
