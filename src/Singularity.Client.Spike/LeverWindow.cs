using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Godot;
using Singularity.Core.Commands;
using Singularity.Core.Institutes;
using Singularity.Core.Policy;
using Singularity.Session;

namespace Singularity.Client.Spike;

/// <summary>The lever windows of the player (TASK-0156, docs/ui/specs/levers-windows.md): one sheet, tabs «Бюджет» and «Институты» (production lines and the licence market follow in part 2).
/// What each tab shows is decided by the pure models of LeverModels.cs; this class only draws them. The controls are built once and updated in place;
/// nothing is allocated in _Draw (there is none). Every state-changing control is a focusable Godot control; Space stays the pause key (Main), focused buttons act on Enter.</summary>
public sealed partial class LeverWindow : PanelContainer
{
    public const int TabBudget = 0, TabInstitutes = 1;
    static readonly string[] TabKeys = { "lever.tab.budget", "lever.tab.inst", "lever.tab.lines", "lever.tab.market" };
    static readonly string[] TitleKeys = { "lever.budget.title", "lever.inst.title", "lever.lines.title", "lever.market.title" };
    /// <summary>F3: the Session text of a refusal is shown under the plain words.</summary>
    public bool Debug { get; set; }

    readonly SimHost _host;
    public int Player { get; set; }
    public event Action Closed;
    public event Action Opened;

    readonly Label _title = Ui.Lbl("", 24, null, FontKind.Draft);
    readonly CaptionButton[] _tabBtn = new CaptionButton[4];
    readonly VBoxContainer _budgetBox = new(), _instBox = new(), _instBody = new();
    readonly Prose _moneyHelp = Ui.Wrap("lever.money.help", 14, Ui.Ink2);
    readonly Prose _footer = Ui.Wrap("", 14, Ui.Ink2), _result = Ui.Wrap("", 14, Ui.Ink2), _paused = Ui.Wrap("lever.paused", 14, Ui.Ink2);
    readonly Control _resultMark = new ColorRect { Color = Ui.Alarm, CustomMinimumSize = new Vector2(9, 9), SizeFlagsVertical = SizeFlags.ShrinkCenter, Visible = false };
    readonly Label _resultWord = Ui.Lbl("", 14, Ui.Ink);
    CaptionButton _apply, _reset, _close;
    int _tab = TabBudget;
    ScrollContainer _scroll;
    long _seen = -1;

    // budget tab
    readonly int[] _pct = new int[3];
    int[] _sent;
    bool _edited;
    readonly HSlider[] _sl = new HSlider[3];
    readonly CaptionButton[] _minus = new CaptionButton[3], _plus = new CaptionButton[3];
    readonly Prose _limitNote = Ui.Wrap("bud.limit", 14, Ui.Ink), _ppNote = Ui.Wrap("bud.pp.note", 14, Ui.Ink2);
    Control _limitRow, _shrinkRow;
    CaptionButton _firstPick, _buyBtn, _firstOffer, _firstRefuse;
    readonly Label[] _name = new Label[3], _val = new Label[3], _money = new Label[3], _delta = new Label[3], _stopWord = new Label[3];
    readonly Prose[] _fx = new Prose[3];
    readonly Label _total = Ui.Lbl("", 18, null, FontKind.Draft), _other = Ui.Lbl("", 14), _reserveLeft = Ui.Lbl("", 14);
    readonly Prose _stopWarn = Ui.Wrap("", 14, Ui.Ink), _resNote = Ui.Wrap("", 14, Ui.Ink2);
    readonly Label _resBalance = Ui.Lbl("", 18, null, FontKind.Draft);
    readonly Prose _resLeak = Ui.Wrap("", 14), _resForecast = Ui.Wrap("", 14), _resPays = Ui.Wrap("res.pays", 14, Ui.Ink2);
    readonly Control _stopRow;
    BudgetModel _bm; ActorInputsView _inp;
    bool _building;

    // institutes tab
    int _expandSlot = -1, _selCand = -1;
    string _sigInst = "";
    InstitutesModel _im;

    /// <summary>Money of a budget-index amount for display: 1990 dollars with the unit after a no-break space; a number format by the language settings.</summary>
    public static string Money(double indices) =>
        MoneyFormat.Current == null ? "—" : MoneyFormat.FormatIndices(indices, Loc.Lang == "ru", Loc.Lang == "ru" && !Loc.IsoNumbers);

    static string MoneyMillion(double million) =>
        MoneyFormat.Format(million, Loc.Lang == "ru", Loc.Lang == "ru" && !Loc.IsoNumbers);

    public LeverWindow(SimHost host)
    {
        _host = host;
        Visible = false;
        SetAnchorsPreset(LayoutPreset.FullRect);
        OffsetLeft = 160; OffsetRight = -160; OffsetTop = 100; OffsetBottom = -100;   // 1600 x 880 du at 1920 x 1080: the sheet of the spec
        AddThemeStyleboxOverride("panel", Ui.Box(Ui.Sheet, Ui.Ink, 2, 24, Ui.R3, 16));
        PaperSurface.Apply(this, SurfaceKind.Sheet, "levers");
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 8);
        AddChild(root);

        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 12);
        _title.SizeFlagsHorizontal = SizeFlags.ShrinkBegin; _title.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        head.AddChild(_title);
        var help = Ui.Btn("?", () => _moneyHelp.Visible = !_moneyHelp.Visible, focusable: true);   // a square next to the title, not stretched over two rows
        help.CustomMinimumSize = new Vector2(32, 32); help.SizeFlagsVertical = SizeFlags.ShrinkCenter; help.TooltipText = "lever.money.help.btn";
        head.AddChild(help);
        head.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        for (int i = 0; i < TabKeys.Length; i++)
        {
            int k = i;
            _tabBtn[i] = Ui.Btn(TabKeys[i], () => ShowTab(k), focusable: true);
            _tabBtn[i].CustomMinimumSize = new Vector2(0, 40);
            head.AddChild(_tabBtn[i]);
        }
        _close = Ui.Btn("lever.close", Close, focusable: true);
        _close.CustomMinimumSize = new Vector2(112, 40);
        head.AddChild(_close);
        root.AddChild(head);
        var sub = new HBoxContainer(); sub.AddThemeConstantOverride("separation", 16);
        sub.AddChild(Ui.Lbl("lever.money.suffix", 14, Ui.Ink2));
        sub.AddChild(_paused);   // one line, after the unit note
        root.AddChild(sub);
        _moneyHelp.Visible = false; root.AddChild(_moneyHelp);
        root.AddChild(new HSeparator());

        _stopRow = Ui.MarkedRow(Ui.Warn, _stopWarn);
        BuildBudget();
        _instBox.AddChild(_instBody);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        var pad = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; pad.AddThemeConstantOverride("margin_right", 14);
        var stack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        stack.AddChild(_budgetBox); stack.AddChild(_instBox);
        InitLinesAndMarket(stack);
        pad.AddChild(stack); scroll.AddChild(pad);
        root.AddChild(scroll); _scroll = scroll;

        root.AddChild(new HSeparator());
        var rr = new HBoxContainer(); rr.AddThemeConstantOverride("separation", 6);
        rr.AddChild(_resultMark); rr.AddChild(_resultWord); rr.AddChild(_result);
        root.AddChild(rr);
        var foot = new HBoxContainer(); foot.AddThemeConstantOverride("separation", 10);
        _footer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        foot.AddChild(_footer);
        _reset = Ui.Btn("bud.reset", ResetEdit, focusable: true); _reset.CustomMinimumSize = new Vector2(0, 40);
        _apply = Ui.Btn("bud.apply", Apply, 14, true, true); _apply.CustomMinimumSize = new Vector2(120, 40);
        foot.AddChild(_reset); foot.AddChild(_apply);
        root.AddChild(foot);
        Loc.Changed += Rebuild;
        TreeExiting += () => Loc.Changed -= Rebuild;
    }

    void Rebuild() { _seen = -1; _sigInst = ""; }

    public bool IsOpen => Visible;
    public int Tab => _tab;

    public void ShowTab(int tab)
    {
        bool wasOpen = Visible;
        _tab = tab;
        Visible = true;
        _budgetBox.Visible = tab == TabBudget; _instBox.Visible = tab == TabInstitutes; _lineBox.Visible = tab == TabLines; _marketBox.Visible = tab == TabMarket;
        _title.Text = TitleKeys[tab];
        _apply.Visible = _reset.Visible = tab == TabBudget;
        _seen = -1; _sigInst = ""; _sigLines = ""; _sigMarket = ""; _confirmLine = null; _confirmBuy = null;
        _edited = false; _sent = null;
        Refresh();
        if (!wasOpen) Opened?.Invoke();
        (tab == TabBudget ? (Control)_sl[0] : _tabBtn[tab]).CallDeferred(Control.MethodName.GrabFocus);
    }

    public void Close()
    {
        if (!Visible) return;
        Visible = false;
        Closed?.Invoke();
    }

    public override void _Process(double delta)
    {
        if (!Visible || !_host.Ready) return;
        if (_host.Snap.Seq != _seen) Refresh();
    }

    // ---- budget tab ----

    void BuildBudget()
    {
        _budgetBox.AddThemeConstantOverride("separation", 10);
        _budgetBox.AddChild(_total);
        for (int i = 0; i < 3; i++)
        {
            int k = i;
            var row = new VBoxContainer(); row.AddThemeConstantOverride("separation", 2);
            var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 10);
            _name[i] = Ui.Lbl("bud." + BudgetWindowModel.Keys[i], 16, null, FontKind.Draft); _name[i].CustomMinimumSize = new Vector2(250, 0);
            _fx[i] = Ui.Wrap("", 14, Ui.Ink2); _fx[i].CustomMinimumSize = new Vector2(360, 0);
            top.AddChild(_name[i]); top.AddChild(_fx[i]);
            row.AddChild(top);
            var ctl = new HBoxContainer(); ctl.AddThemeConstantOverride("separation", 8);
            var minus = Ui.Btn("−", () => Nudge(k, -1), focusable: true); minus.CustomMinimumSize = new Vector2(32, 32); _minus[i] = minus;
            _sl[i] = new HSlider { MinValue = 0, MaxValue = 100, Step = 1, CustomMinimumSize = new Vector2(360, 32), SizeFlagsVertical = SizeFlags.ShrinkCenter };
            ActorPanel.StyleSlider(_sl[i]);
            _sl[i].ValueChanged += v => OnSlider(k, (int)v);
            var plus = Ui.Btn("+", () => Nudge(k, 1), focusable: true); plus.CustomMinimumSize = new Vector2(32, 32); _plus[i] = plus;
            _val[i] = Ui.Num("", 18, null, 64); _money[i] = Ui.Num("", 18, null, 150); _delta[i] = Ui.Lbl("", 14, Ui.Ink2);
            _stopWord[i] = Ui.Lbl("bud.stop.word", 14, Ui.Ink, FontKind.Draft); _stopWord[i].Visible = false;
            ctl.AddChild(minus); ctl.AddChild(_sl[i]); ctl.AddChild(plus); ctl.AddChild(_val[i]); ctl.AddChild(_money[i]); ctl.AddChild(_stopWord[i]); ctl.AddChild(_delta[i]);
            row.AddChild(ctl);
            _budgetBox.AddChild(row);
        }
        _limitRow = Ui.MarkedRow(Ui.Warn, _limitNote, true); _limitRow.Visible = false;   // one line for all three items: a stopped handle says why (C4)
        _budgetBox.AddChild(_limitRow);
        _stopRow.Visible = false; _budgetBox.AddChild(_stopRow);
        var grey = new HBoxContainer(); grey.AddThemeConstantOverride("separation", 10);
        grey.AddChild(_other);
        _budgetBox.AddChild(grey);
        _budgetBox.AddChild(_ppNote);
        _budgetBox.AddChild(_reserveLeft);
        _budgetBox.AddChild(new HSeparator());
        _budgetBox.AddChild(Ui.Head("res.title"));
        _budgetBox.AddChild(_resBalance); _budgetBox.AddChild(_resLeak); _budgetBox.AddChild(_resForecast); _budgetBox.AddChild(_resPays);
        _shrinkRow = Ui.MarkedRow(Ui.Warn, _resNote, true);   // a warning is a full-contrast line with a marker, not a quiet grey one (C5)
        _budgetBox.AddChild(_shrinkRow);
    }

    void OnSlider(int i, int v)
    {
        if (_building) return;
        if (_bm == null || !_bm.HasData) return;
        int max = BudgetWindowModel.MaxFor(_inp.Shares, _pct, i);
        if (v > max) { v = max; _building = true; _sl[i].SetValueNoSignal(v); _building = false; }
        if (_pct[i] == v) { UpdateBudget(); return; }
        _pct[i] = v; _edited = true; UpdateBudget();
    }

    void Nudge(int i, int d) { if (_bm == null || !_bm.HasData) return; OnSlider(i, Math.Clamp(_pct[i] + d, 0, 100)); }

    void ResetEdit() { _edited = false; _sent = null; UpdateBudget(); }

    void Apply()
    {
        if (_bm == null || !_bm.HasData || _inp == null) return;
        _sent = (int[])_pct.Clone();
        _host.Submit(new SetBudgetShares(Player, BudgetWindowModel.ToShares(_inp.Shares, _pct)), "cmd.budget");
    }

    void UpdateBudget()
    {
        var snap = _host.Snap;
        _inp = snap.View.Inputs?.Actors.FirstOrDefault(x => x.Actor == Player);
        if (_inp != null)
        {
            int[] force = BudgetWindowModel.InForcePct(_inp.Shares);
            // an order sent while paused stays pending: the edit stays on screen until the shares in force catch up with it
            if (_sent != null && force.SequenceEqual(_sent)) { _sent = null; _edited = false; }
            if (!_edited && _sent == null) Array.Copy(force, _pct, 3);
        }
        _bm = BudgetWindowModel.Build(_inp, _sent != null || _edited ? _pct : null, false, MoneyFormat.Current, MoneyMillion);
        bool data = _bm.HasData;
        _building = true;
        for (int i = 0; i < 3; i++)
        {
            _sl[i].Editable = data; _sl[i].Visible = true;
            if (!data) { _val[i].Text = "—"; _money[i].Text = "—"; _delta[i].Text = ""; _minus[i].Disabled = _plus[i].Disabled = true; continue; }
            var r = _bm.Rows[i];
            _sl[i].SetValueNoSignal(r.Pct);
            _val[i].Text = r.Pct + " %";
            _money[i].Text = r.Money;
            _delta[i].Text = r.DeltaPp == 0 ? "" : Loc.T("bud.delta", (r.DeltaPp > 0 ? "+" : "") + r.DeltaPp, (r.DeltaPp > 0 ? "+" : "") + r.DeltaMoney);
            _stopWord[i].Visible = r.Stop;
            _minus[i].Disabled = !BudgetWindowModel.MinusEnabled(r, _bm.Editable);
            _plus[i].Disabled = !BudgetWindowModel.PlusEnabled(r, _bm.Editable);
            _fx[i].Text = i == BudgetWindowModel.Mastery ? Loc.T("bud.mastery.fx", _bm.EffortStopPercent) : Loc.T("bud." + BudgetWindowModel.Keys[i] + ".fx");
        }
        _building = false;
        _limitRow.Visible = BudgetWindowModel.ShowLimitNote(_bm);
        _ppNote.Visible = BudgetWindowModel.ShowPpNote(_bm);
        _total.Text = data ? Loc.T("bud.total", _bm.TotalMoney) : Loc.T("why.budget.nodata");
        _other.Text = data ? Loc.T("bud.other") + ": " + _bm.OtherPct + " % · " + _bm.OtherMoney + " — " + Loc.T("bud.other.fx") : "";
        _reserveLeft.Text = data && _bm.ReserveOn ? Loc.T("bud.reserve.left", _bm.ReservePct, _bm.ReserveYearMoney) : "";
        _stopRow.Visible = data && _bm.EffortStop;
        _stopWarn.Text = Loc.T("bud.stop", _bm.EffortStopPercent);
        // reserve card
        bool on = data && _bm.ReserveOn;
        _resBalance.Text = on ? Loc.T("res.balance", _bm.BalanceMoney) : "";
        _resLeak.Text = on ? Loc.T("res.leak", _bm.LeakYearMoney) : "";
        _resForecast.Text = on ? Loc.T("res.forecast", _bm.Forecast[0], _bm.Forecast[1], _bm.Forecast[2]) : "";
        _resNote.Text = !data ? "" : !on ? Loc.T("res.off") : _bm.ReserveEmpty ? Loc.T("res.empty") : _bm.ReserveShrinks ? Loc.T("res.shrink") : "";
        _shrinkRow.Visible = _resNote.Text != "";
        _resPays.Visible = on;
        // footer and the Apply button: a grey button says why, right beside it (C4)
        string changes = "";
        if (data) foreach (var r in _bm.Rows) if (r.DeltaPp != 0) changes += (changes == "" ? "" : "; ") + Loc.T("bud." + r.Key) + " " + (r.DeltaPp > 0 ? "+" : "") + r.DeltaPp + " " + Loc.T("bud.pp.unit") + ", " + (r.DeltaPp > 0 ? "+" : "") + r.DeltaMoney;
        bool pendingSame = _sent != null && _pct.SequenceEqual(_sent);
        bool can = data && _bm.Changed && !pendingSame && _bm.Editable;
        _apply.Disabled = !can;
        _footer.Text = !data ? Loc.T(_bm.ApplyDisabledKey) : can ? Loc.T("bud.footer", changes) : BudgetWindowModel.FooterNone(Loc.T("bud.footer.none"), _bm.ApplyDisabledKey, _bm.ApplyDisabledKey == "" ? "" : Loc.T(_bm.ApplyDisabledKey));
        _reset.Disabled = !(_edited || _sent != null);
    }

    // ---- institutes tab ----

    int Discipline(int node)
    {
        var n = _host.Data.Nodes;
        for (int i = 0; i < n.Count; i++) if (n.Number[i] == node) return (int)n.Discipline[i];
        return -1;
    }

    InstitutesModel BuildInstitutes(SliceView v)
    {
        var a = v.Actors.FirstOrDefault(x => x.Actor == Player);
        var inp = v.Inputs?.Actors.FirstOrDefault(x => x.Actor == Player);
        double research = inp == null ? 0 : inp.Budget.ToDouble() * inp.Shares.Research.ToDouble();
        var sc = _host.Data.Scenarios[0];
        var ctx = new InstituteContext(sc.StartYear, sc.StartMonth, InstitutesWindowModel.IsSmallCountry(sc, Player));
        return InstitutesWindowModel.Build(a, v.Month, research, MoneyFormat.Current, ctx, n => Names.Node(_host.Data, n), Discipline);
    }

    string LevelsText(ImmutableArray<(int Discipline, int Level)> lv) => LeverText.Join(lv.Select(x => Loc.T("disc." + x.Discipline) + " " + x.Level).ToArray());

    static string InstName(string id) => Loc.TOr("institute." + id, id);

    void FillInstitutes()
    {
        var v = _host.Snap.View;
        _im = BuildInstitutes(v);
        var m = _im;
        string sig = Loc.Lang + "|" + _expandSlot + "|" + _selCand + "|" + m.Enabled + "|" + string.Join(";", m.Standing.Select(r => r.Slot + r.Id + r.State + r.GoalNode + r.ProposedGoal + r.Options.Length)) + "|" + string.Join(";", m.Candidates.Select(r => r.Slot + "" + r.State + r.MonthsLeft + r.BuildDisabledKey + r.ProposesBuild + (long)r.CostTotalMillion)) + "|" + m.SlotsBusy;
        if (sig == _sigInst) return;
        _sigInst = sig;
        foreach (var c in _instBody.GetChildren()) { _instBody.RemoveChild(c); c.QueueFree(); }
        _instBody.AddThemeConstantOverride("separation", 8);
        if (!m.Enabled) { _instBody.AddChild(Ui.Wrap("inst.noinst", 16)); return; }
        _instBody.AddChild(Ui.Lbl(Loc.T("inst.slots", m.SlotsTotal, m.SlotsBusy), 16, Ui.Ink2, FontKind.Draft));
        Control firstFocus = null; _firstPick = null;
        foreach (var r in m.Standing)
        {
            var box = new PanelContainer(); box.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Paper, Ui.Rule, 1, 10, Ui.R2, 8));
            var col = new VBoxContainer(); col.AddThemeConstantOverride("separation", 4);
            var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 12);
            var nm = Ui.Lbl(InstName(r.Id), 16, null, FontKind.Draft); nm.CustomMinimumSize = new Vector2(520, 0); nm.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            h.AddChild(nm);
            h.AddChild(Ui.Wrap(LeverText.Join(Loc.T("inst.founded", r.RefYear), LevelsText(r.Levels)), 14, Ui.Ink2));
            col.AddChild(h);
            if (r.State == InstituteState.Goal)
            {
                col.AddChild(Ui.Wrap(Loc.T("inst.goal", r.GoalName) + ". " + Loc.T("inst.goal.fx", r.GoalBonusPct, Loc.N(r.GoalRemaining, 1)), 14, Ui.Ink));
            }
            else col.AddChild(Ui.Wrap("inst.free", 14, Ui.Ink));
            if (r.ProposedGoal > 0 && r.ProposedGoal != r.GoalNode)
            {
                var ar = new HBoxContainer(); ar.AddThemeConstantOverride("separation", 10);
                ar.AddChild(Ui.Wrap(Loc.T("inst.adviser", r.ProposedName), 14, Ui.Ink));
                int slot = r.Slot, node = r.ProposedGoal;
                var take = Ui.Btn("inst.adviser.take", () => _host.Submit(new SetPriorityNode(Player, slot, node), "cmd.adviser"), focusable: true);
                take.CustomMinimumSize = new Vector2(0, 32); ar.AddChild(take);
                col.AddChild(ar);
            }
            int s = r.Slot;
            bool open = _expandSlot == s;
            var pick = Ui.Btn((open ? "▾ " : "▸ ") + Loc.T(r.State == InstituteState.Goal ? "inst.pick.change" : "inst.pick.short"), () => { _expandSlot = open ? -1 : s; _sigInst = ""; }, focusable: true);
            pick.CustomMinimumSize = new Vector2(0, 32); pick.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
            firstFocus ??= pick; _firstPick ??= pick;
            col.AddChild(pick);
            if (open)
            {
                col.AddChild(Ui.Lbl(Loc.T("inst.pick", InstName(r.Id)), 14, Ui.Ink2, FontKind.Draft));
                if (r.Options.Length == 0) col.AddChild(Ui.Wrap("inst.opt.none", 14, Ui.Ink));
                foreach (var o in r.Options)
                {
                    int node = o.Node; bool cur = o.Node == r.GoalNode;
                    string t = o.BonusPct == 0
                        ? Loc.T("inst.opt.nobonus", o.Name, Loc.N(o.Remaining, 1)) + (o.BetterInstituteId != "" ? " " + Loc.T("inst.opt.better", InstName(o.BetterInstituteId)) : "")
                        : Loc.T(o.Threshold ? "inst.opt.threshold" : "inst.opt", o.Name, o.BonusPct, Loc.N(o.Remaining, 1));
                    col.AddChild(ListButton((cur ? "■ " : "□ ") + t, () => { _host.Submit(new SetPriorityNode(Player, s, node), "cmd.priority"); _expandSlot = -1; _sigInst = ""; }));
                }
            }
            // the price of a goal for the rest of science stands next to the button, not only inside the open list (O-082: no number, no promise of a date)
            col.AddChild(Ui.Wrap(Loc.T("inst.opt.price", InstituteParamsMonths()) + " " + Loc.T("inst.price.note"), 14, Ui.Ink2));
            box.AddChild(col); _instBody.AddChild(box);
        }
        _instBody.AddChild(new HSeparator());
        _instBody.AddChild(Ui.Head("inst.build.head"));
        if (m.Candidates.IsEmpty) { _instBody.AddChild(Ui.Wrap("inst.build.none", 14)); return; }
        if (_selCand < 0 || !m.Candidates.Any(c => c.Slot == _selCand)) _selCand = (m.Candidates.FirstOrDefault(c => c.ProposesBuild && c.State == InstituteState.Candidate) ?? m.Candidates.FirstOrDefault(c => c.State == InstituteState.Candidate) ?? m.Candidates[0]).Slot;
        foreach (var c in m.Candidates)
        {
            int cs = c.Slot; bool sel = cs == _selCand;
            string line = LeverText.Join(InstName(c.Id), Loc.T("inst.founded", c.RefYear), LevelsText(c.Levels), c.State == InstituteState.Building ? Loc.T("inst.building", c.MonthsLeft, c.BuildMonths) : "", c.ProposesBuild ? Loc.T("inst.adviser.build") : "");
            _instBody.AddChild(ListButton((sel ? "■ " : "□ ") + line, () => { _selCand = cs; _sigInst = ""; }));
        }
        var cand = m.Candidates.First(c => c.Slot == _selCand);
        var card = new PanelContainer(); card.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Paper, Ui.Ink, 1, 10, Ui.R2, 8));
        var cc = new VBoxContainer(); cc.AddThemeConstantOverride("separation", 4);
        cc.AddChild(Ui.Lbl(InstName(cand.Id), 16, null, FontKind.Draft));
        var p = new InstituteParams();
        cc.AddChild(Ui.Wrap(Loc.T("inst.build.gives", Loc.N(p.CapacityPerLevel.ToDouble() * 100, 1), Loc.N(p.UpkeepPerLevel.ToDouble() * 100, 1)), 14));
        cc.AddChild(Ui.Wrap(Loc.T("inst.build.price", MoneyMillion(cand.CostPerYearMillion), cand.BuildMonths, MoneyMillion(cand.CostTotalMillion), Loc.T("lever.money.suffix")), 14, Ui.Ink));
        cc.AddChild(Ui.Wrap(Loc.T("inst.build.share", Loc.N(cand.RateTenthPercent / 10.0, 1)), 14));
        if (cand.AheadYears > 0) cc.AddChild(Ui.MarkedRow(Ui.Warn, Ui.Wrap(Loc.T("inst.build.ahead", cand.AheadYears, cand.RefYear, Loc.N(cand.AheadMultiplier, 2)), 14), true));
        bool can = cand.BuildDisabledKey == "";
        int candSlot = cand.Slot;
        var start = Ui.Btn("inst.build.start", () => _host.Submit(new BuildInstitute(Player, candSlot), "cmd.build"), 14, can, true);
        start.Disabled = !can; start.CustomMinimumSize = new Vector2(0, 40); start.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        cc.AddChild(start);
        if (!can) cc.AddChild(Ui.Wrap(Loc.T("inst.build.cant", Loc.T(cand.BuildDisabledKey, cand.BuildDisabledArg)), 16, Ui.Ink));
        card.AddChild(cc); _instBody.AddChild(card);
        firstFocus?.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>A choice in a list: the text wraps inside the window and stays left-aligned (A9), the box is a full-width focusable button.</summary>
    static CaptionButton ListButton(string text, Action onPress)
    {
        var b = Ui.Btn(text, onPress, focusable: true);
        b.CustomMinimumSize = new Vector2(0, 32); b.Alignment = HorizontalAlignment.Left; b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        b.AutowrapMode = TextServer.AutowrapMode.WordSmart; b.ClipText = false;
        return b;
    }

    static int InstituteParamsMonths() => InstitutesWindowModel.ReassignMonths;

    // ---- common ----

    void Refresh()
    {
        if (!_host.Ready) return;
        var s = _host.Snap;
        _seen = s.Seq;
        for (int i = 0; i < TabKeys.Length; i++) _tabBtn[i].Text = (i == _tab ? "■ " : "□ ") + Loc.T(TabKeys[i]);   // the open tab is marked by a shape, not only a fill
        UpdateBudget();
        if (_tab == TabInstitutes) { FillInstitutes(); _footer.Text = ""; _apply.Visible = _reset.Visible = false; }
        else if (_tab == TabLines) { FillLines(); _footer.Text = ""; _apply.Visible = _reset.Visible = false; }
        else if (_tab == TabMarket) { FillMarket(); _footer.Text = ""; _apply.Visible = _reset.Visible = false; }
        ShowResult(s);
    }

    // The last order of this window in words; a refusal says why in plain words (C4), the English Session text only under F3.
    void ShowResult(HostSnapshot s)
    {
        var r = s.Last;
        bool mine = r.Status != CmdStatus.None && LeverText.ResultBelongsToTab(r.LabelKey, _tab);   // told only on the tab it belongs to (C1)
        _resultMark.Visible = mine && r.Status is CmdStatus.Rejected or CmdStatus.Error;
        _resultWord.Text = _resultMark.Visible ? Loc.T("lever.refused.word") : "";
        if (!mine) { _result.Text = ""; return; }
        string what = Loc.T(r.LabelKey, r.LabelArg);
        // plain words always; the code and the English Session text of a refusal only under F3
        string why = r.Status == CmdStatus.Rejected ? Loc.TOr(InstitutesWindowModel.RefusalKey(r.Reason) ?? "refusal." + r.Reason, Loc.T("refusal.generic")) : r.Status == CmdStatus.Error ? Loc.T("refusal.error") : "";
        _result.Text = Loc.T("cmd." + r.Status, what, why) + (Debug && r.Reason != "" ? "\n" + r.Reason + (r.Message != "" ? ": " + r.Message : "") : "");
    }

    /// <summary>Run-time assertion on a real session state (ui.md, TASK-0111): the same models the window draws, over the live view; null = consistent.</summary>
    public string LiveCheck()
    {
        if (!_host.Ready) return "no session";
        var v = _host.Snap.View;
        var inp = v.Inputs?.Actors.FirstOrDefault(x => x.Actor == Player);
        if (inp == null) return null;   // before the first monthly step the budget has no data and says so
        var b = BudgetWindowModel.Build(inp, null, false, MoneyFormat.Current, MoneyMillion);
        if (!b.HasData || b.TotalYearMillion <= 0) return "budget model empty on a live state";
        var im = BuildInstitutes(v);
        if (!im.Enabled) return "institute model disabled on a live state of a run with institutes";
        if (im.Standing.IsEmpty && im.Candidates.IsEmpty) return "institute model empty on a live state";
        return LiveCheckLinesMarket(v);
    }

    /// <summary>Shot run: true when the budget tab shows real numbers (a frame of «нет данных» is not evidence).</summary>
    public bool BudgetHasData => _bm != null && _bm.HasData;
    /// <summary>Shot run: the same path as a player moving slider i (the 100 % limit applies).</summary>
    public void ShotEdit(int i, int v) { OnSlider(i, v); }
    public void ShotReset() => ResetEdit();
    /// <summary>Shot run: opens the goal list of the first institute that stands; false when none does.</summary>
    public bool ShotPick() { if (_im == null || _im.Standing.IsEmpty) return false; _expandSlot = _im.Standing[0].Slot; _sigInst = ""; Refresh(); return true; }
    /// <summary>Shot run: selects the first institute that can be built and scrolls to its card; false when there is none.</summary>
    public bool ShotCandidate()
    {
        _expandSlot = -1;
        var c = _im?.Candidates.FirstOrDefault(x => x.State == InstituteState.Candidate);
        if (c == null) return false;
        _selCand = c.Slot; _sigInst = ""; Refresh();
        _scroll.CallDeferred(Control.MethodName.Set, "scroll_vertical", 100000);
        return true;
    }

    /// <summary>Shot run: at 100 % every item stands at its limit and every «+» is inactive; one point is taken off the last item so that the first «+» can be focused.</summary>
    CaptionButton MakeRoomForPlus()
    {
        var p = _plus.FirstOrDefault(x => !x.Disabled);
        if (p != null) return p;
        OnSlider(Procurement(), Math.Max(0, _pct[Procurement()] - 1));
        return _plus.FirstOrDefault(x => !x.Disabled);
    }

    static int Procurement() => BudgetWindowModel.Procurement;

    /// <summary>Shot run (A7): puts the keyboard focus on a control and tells whether it exists in this state. what: slider, plus, tab, pick, buy, accept.</summary>
    public string ShotFocus(string what)
    {
        static bool Usable(Control c) => c != null && c.IsVisibleInTree() && c is not BaseButton { Disabled: true };
        // an inactive control cannot take the focus: the nearest active one of the same kind stands in, and the name of what got it is returned
        Control c = what switch
        {
            "slider" => _sl[1],   // the base frame already carries the focus of the first slider (ShowTab), so the next one makes the frame differ
            "plus" => MakeRoomForPlus(),
            "tab" => _tabBtn[_tab],
            "pick" => _firstPick,
            "buy" => Usable(_buyBtn) ? _buyBtn : _firstOffer,
            "accept" => Usable(_acceptBtn) ? _acceptBtn : _firstRefuse,
            _ => null,
        };
        if (!Usable(c)) return "";
        c.GrabFocus();
        return c == _buyBtn || c == _acceptBtn || c == _firstPick || c == _tabBtn[_tab] || c == _sl[1] ? what : what + " (stand-in: " + c.GetType().Name + " " + (c as Button)?.Text + ")";
    }

    /// <summary>Shot run: the window as a player opens it, without a click.</summary>
    public void ShotOpen(int tab) => ShowTab(tab);
}
