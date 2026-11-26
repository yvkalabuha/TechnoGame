using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Godot;
using Singularity.Core.Commands;
using Singularity.Core.Data;
using Singularity.Kernel;
using Singularity.Session;

namespace Singularity.Client.Spike;

// Tech map stage 1 (TASK-0192, spec docs/ui/specs/tech-map-stage1-spec.md): institute strip under the map, node choice card, queue panel, path to goal.
// The numbers all come from the session (FitInstitutes, FocusQuote, PathToGoal, SuggestQueues and the institute views); ScienceMapLogic only chooses what to show.
public sealed partial class TechTree
{
    const float StripH = 184f, StripHead = 32f, StripCardW = 184f, StripCardH = 152f;
    readonly PanelContainer _strip = new();
    readonly HBoxContainer _stripCards = new();
    readonly HBoxContainer _stripHead = new();
    bool _stripShown;
    string _stripKey = "";
    int _page, _selInst = -1, _goalNum, _noticeSeen, _cancelAsk = -1;
    bool _freeNotice, _planOpen, _fromReserve;
    ImmutableArray<QueuePlan> _draft = ImmutableArray<QueuePlan>.Empty;
    SliceCommand _sentAck;
    bool _shotAssignEnabled, _shotConfirm; string _shotReason; int _shotQueueRows = -1;
    int _cmpA = -1;                                   // node row marked as A of the comparison (Б is the selected node)
    int[] _pathRows = Array.Empty<int>(); string[] _pathNum = Array.Empty<string>(); object _pathRef;   // the path on the map, numbered once per new path

    /// <summary>The slot science of the player is on: the strip, the card and the path replace the old goal panel.</summary>
    bool SlotsUi => _actor >= 0 && _actor == Player && _host.Snap is { Science: not null } sn && _actor < sn.View.Actors.Length && sn.View.Actors[_actor].Slots != null && sn.View.Actors[_actor].Institutes is { IsDefault: false };

    void BuildStrip()
    {
        _strip.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Sheet, Ui.Ink, 1, 8));
        var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 4);
        _stripHead.AddThemeConstantOverride("separation", 8);
        _stripCards.AddThemeConstantOverride("separation", 8);
        v.AddChild(_stripHead); v.AddChild(_stripCards);
        _strip.AddChild(v);
        _strip.Visible = false;
        AddChild(_strip);
    }

    /// <summary>Selects an institute of the strip (its queue shows in the inspector) and clears the node selection.</summary>
    void SelectInstitute(int slot) { _selInst = slot; _selected = -1; _planOpen = false; _cancelAsk = -1; _insetDirty = true; AskScience(); Redraw(); }

    void AskScience() => _host.AskScience(_selected >= 0 ? _host.Data.Nodes.Number[_selected] : 0, _goalNum, _cmpA >= 0 ? _host.Data.Nodes.Number[_cmpA] : 0);

    /// <summary>Shift+Enter on a node of the map: the selected node becomes A, the node under the key ring B (spec §4).</summary>
    void CompareWith(int row) { if (_selected >= 0 && _selected != row) _cmpA = _selected; Select(row); }

    // The squares 1..N of the path on the map (spec §5): one per node, drawn last of the nodes; the strings are made when a new path arrives, so a draw allocates nothing.
    void SyncPathRows(HostSnapshot s)
    {
        PathView p = SlotsUi && _goalNum > 0 && s.Science.Goal == _goalNum ? s.Science.Path : null;
        if (ReferenceEquals(p, _pathRef)) return;
        _pathRef = p;
        var rows = new List<int>();
        if (p != null) foreach (var n in p.Nodes) if (_rowOfNumber.TryGetValue(n.Node, out int r)) rows.Add(r);
        _pathRows = rows.ToArray(); _pathNum = new string[_pathRows.Length];
        for (int k = 0; k < _pathNum.Length; k++) _pathNum[k] = (k + 1).ToString();
        Redraw();
    }

    void DrawPathNumbers()
    {
        for (int k = 0; k < _pathRows.Length; k++)
        {
            var p = PosOf(_pathRows[k]); float r = NR;
            var box = new Rect2(p.X + r * 0.5f, p.Y - r - 22f, 22f, 22f);
            if (!Area.Encloses(box)) continue;
            _cv.DrawRect(box, Ui.Command); _cv.DrawRect(box, Ui.Ink, false, 1f);
            Text(new Vector2(box.Position.X + 11f - 4.2f * _pathNum[k].Length, box.Position.Y + 17f), _pathNum[k], 14, Colors.White, FontKind.Mono);
        }
    }

    // Alt+Up / Alt+Down on any button of a queue row move that row (spec §6).
    void AltMove(Control c, Action up, Action down) =>
        c.GuiInput += e =>
        {
            if (e is InputEventKey { Pressed: true, AltPressed: true } k && k.Keycode is Key.Up or Key.Down)
            {
                if (k.Keycode == Key.Up) up(); else down();
                c.AcceptEvent();
            }
        };

    void SetGoal(int number) { _goalNum = number; _planOpen = false; _modelDirty = true; _insetDirty = true; AskScience(); }

    // Placement and the rebuild of the strip; called from _Process, never from a draw.
    void UpdateStrip(HostSnapshot s)
    {
        bool show = SlotsUi;
        _stripShown = show; _strip.Visible = show;
        if (!show) return;
        _strip.Position = new Vector2(0, Size.Y - StripH - 6); _strip.Size = new Vector2(Size.X * 0.6f - 8, StripH);
        var views = s.View.Actors[_actor].Institutes;
        var slots = s.View.Actors[_actor].Slots;
        if (s.Science.NoticeSeq != _noticeSeen) { _noticeSeen = s.Science.NoticeSeq; _freeNotice = true; _insetDirty = true; }
        string key = s.View.Month + "|" + s.CmdSeq + "|" + _page + "|" + _selInst + "|" + Loc.Lang + "|" + views.Length;
        if (key == _stripKey) return;
        _stripKey = key;
        foreach (var c in _stripHead.GetChildren()) { _stripHead.RemoveChild(c); c.QueueFree(); }
        foreach (var c in _stripCards.GetChildren()) { _stripCards.RemoveChild(c); c.QueueFree(); }
        int standing = views.Count(v => v.Standing);
        var head = Ui.Lbl(Loc.T("sci.strip.head", standing, views.Length, slots.FreeInstituteCount), 14, Ui.Ink, FontKind.Draft);
        head.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _stripHead.AddChild(head);
        int pages = ScienceMapLogic.PageCount(views.Length);
        if (pages > 1)
        {
            _stripHead.AddChild(Ui.Btn("◀", () => { _page = Math.Max(0, _page - 1); _stripKey = ""; }, 14, false, true));
            _stripHead.AddChild(Ui.Lbl(Loc.T("sci.strip.page", _page + 1, pages), 14, Ui.Ink2));
            _stripHead.AddChild(Ui.Btn("▶", () => { _page = Math.Min(pages - 1, _page + 1); _stripKey = ""; }, 14, false, true));
        }
        var (first, count) = ScienceMapLogic.Page(views.Length, _page);
        for (int k = first; k < first + count; k++) _stripCards.AddChild(StripCard(views[k], s.View.Month));
    }

    Control StripCard(InstituteView v, long month)
    {
        var st = ScienceMapLogic.Strip(v, month);
        var nodes = _host.Data.Nodes;
        var lines = new List<string> { Loc.T("sci.strip.name", v.Slot + 1, Loc.TOr("inst." + v.Id, v.Id)) };
        if (st.Kind == SlotKind.Busy || st.Kind == SlotKind.Free)
        {
            var all = new List<string>();
            if (!v.DirLevels.IsDefault)
                for (int d = 0; d < v.DirLevels.Length; d++) if (v.DirLevels[d] > 0) all.Add(ScienceMapLogic.Tag(d) + " " + v.DirLevels[d]);
            lines.Add(string.Join(" · ", ScienceMapLogic.Cap(all, ScienceMapLogic.StripDirsShown)));
        }
        switch (st.Kind)
        {
            case SlotKind.Busy:
                int row = _rowOfNumber.TryGetValue(st.Active, out int r) ? r : -1;
                lines.Add(row >= 0 ? Names.Node(nodes.Id[row]) : "—");
                lines.Add(Loc.T("sci.strip.progress", st.ProgressMonths, st.ProgressMonths + st.MonthsLeft, st.MonthsLeft));
                lines.Add(Loc.T("sci.focus." + st.FocusLevel));
                lines.Add(Loc.T("sci.strip.queue", st.Queued, ScienceMapLogic.QueueMax));
                break;
            case SlotKind.Free:
                lines.Add(Loc.T("sci.strip.free"));
                lines.Add(Loc.T("sci.strip.queue", st.Queued, ScienceMapLogic.QueueMax));
                break;
            case SlotKind.Building:
                lines.Add(Loc.T("sci.strip.building", st.BuildLeft));
                lines.Add(Loc.T("sci.strip.buildprog", st.BuildDone, st.BuildDone + st.BuildLeft));
                break;
            default:
                lines.Add(Loc.T("sci.strip.unavail"));
                break;
        }
        var b = Ui.Btn(string.Join("\n", lines), () => SelectInstitute(v.Slot), 14, false, true);
        b.CustomMinimumSize = new Vector2(StripCardW, StripCardH);
        b.TooltipText = Loc.T("sci.strip.tip." + (int)st.Kind);
        if (st.Kind == SlotKind.Free) b.AddThemeStyleboxOverride("normal", Ui.Box(Ui.Warn, Ui.Ink, 2, 8));
        else if (st.Kind == SlotKind.Unavailable) b.AddThemeStyleboxOverride("normal", Ui.Box(Ui.Paper, Ui.Ink3, 1, 8));
        if (v.Slot == _selInst) b.AddThemeStyleboxOverride("normal", Ui.Box(Ui.Hover, Ui.Ink, 3, 8));
        b.AddThemeStyleboxOverride("focus", Ui.FocusBox());
        return b;
    }

    // ---- shot run hooks: what the live session put on screen, for the run-time assertions (frames alone do not prove the live path) ----
    public bool ShotSlotsUi => SlotsUi && _strip.Visible;
    public int ShotStripCards => _stripCards.GetChildCount();
    public bool ShotAssignEnabled => _shotAssignEnabled;
    public string ShotReason => _shotReason;
    public int ShotQueueRows => _shotQueueRows;
    public int ShotPathSquares => _pathRows.Length;
    public bool ShotConfirmShown => _shotConfirm;
    public bool ShotNoticeShown => _freeNotice;
    public bool ShotPlanOpen => _planOpen;
    public int ShotSelectedInstitute => _selInst;
    /// <summary>First row in a state (0 known, 1 frontier, 2 unavailable), or −1.</summary>
    public int ShotRowByState(int kind) { for (int i = 0; _state != null && i < _state.Length; i++) if ((int)_state[i] == kind) return i; return -1; }
    /// <summary>The first unknown product node (APP): a goal with a real path.</summary>
    public int ShotGoalRow() { var n = _host.Data.Nodes; for (int i = 0; _state != null && i < _state.Length; i++) if (n.Type[i] == NodeType.APP && _state[i] != St.Known) return i; return -1; }
    /// <summary>Numbers of the first <paramref name="n"/> frontier nodes that no institute holds.</summary>
    public int[] ShotFreeFrontier(int n)
    {
        var r = new List<int>(); var nodes = _host.Data.Nodes; var views = _host.Snap.View.Actors[_actor].Institutes;
        for (int i = 0; _state != null && i < _state.Length && r.Count < n; i++)
        {
            int num = nodes.Number[i];
            if (_state[i] == St.Frontier && !views.Any(v => v.Active == num || (!v.Queue.IsDefault && v.Queue.Contains(num)))) r.Add(num);
        }
        return r.ToArray();
    }
    public void ShotSelectInstitute(int slot) => SelectInstitute(slot);
    public void ShotSetGoal(int row) => SetGoal(row >= 0 ? _host.Data.Nodes.Number[row] : 0);
    public void ShotOpenPlan() { if (_host.Snap?.Science is { } s && !s.Plan.IsDefaultOrEmpty) { _planOpen = true; _draft = s.Plan; _insetDirty = true; } }
    public int ShotPlanLines { get { int n = 0; foreach (var q in _draft) n += q.Nodes.Length; return n; } }
    /// <summary>Sends the suggested layout without acknowledgement: the path of the real refusal «money covers less than 60 %».</summary>
    public void ShotApplyPlan() { if (_host.Snap?.Science is { } s && !s.Plan.IsDefaultOrEmpty) Send(new ApplyQueues(Player, s.Plan), new ApplyQueues(Player, s.Plan, true)); }
    public void ShotClearNotice() { _freeNotice = false; _insetDirty = true; }

    // ---- commands ----
    void Send(SliceCommand command, SliceCommand acknowledged = null, string key = "cmd.sci")
    {
        _sentAck = acknowledged;
        _host.Submit(command, key);
        _insetDirty = true;
    }

    CaptionButton Act(string text, Action onPress, bool enabled = true, bool command = false)
    {
        var b = Ui.Btn(text, onPress, 14, command && enabled, true);
        b.CustomMinimumSize = new Vector2(0, 36);
        b.Disabled = !enabled;
        return b;
    }

    // ---- inspector of the player's slot science ----
    void BuildScienceInset(HostSnapshot s)
    {
        _shotConfirm = false; _shotAssignEnabled = false; _shotReason = null; _shotQueueRows = -1;
        var sci = s.Science;
        var views = s.View.Actors[_actor].Institutes;
        if (_freeNotice)
        {
            _inset.AddChild(Ui.MarkedRow(Ui.Warn, Ui.Wrap(Loc.T("sci.notice"), 16, Ui.Ink)));
            _inset.AddChild(Act("sci.notice.ok", () => { _freeNotice = false; _insetDirty = true; }));
        }
        if (_selected >= 0) BuildNodeCard(s, sci, views);
        else if (_selInst >= 0 && _selInst < views.Length) BuildInstitutePanel(s, views[_selInst]);
        else _inset.AddChild(Ui.Wrap(Loc.T("sci.hint"), 16, Ui.Ink2));
        if (_goalNum > 0) BuildPath(s, sci, views);
        if (s.Last.Status != CmdStatus.None && s.Last.LabelKey == "cmd.sci")
        {
            string why = s.Last.Status == CmdStatus.Rejected ? Loc.TOr("refusal." + s.Last.Reason, s.Last.Reason) : s.Last.Status == CmdStatus.Error ? s.Last.Reason : "";
            var lab = Ui.Wrap(Loc.T("cmd." + s.Last.Status, Loc.T("cmd.sci"), why), 14, Ui.Ink2);
            _inset.AddChild(s.Last.Status is CmdStatus.Rejected or CmdStatus.Error ? Ui.MarkedRow(Ui.Alarm, lab) : lab);
        }
        if (s.Last.Status == CmdStatus.Rejected && s.Last.Reason == nameof(CommandRefusal.NeedsConfirmation) && _sentAck != null)
        {
            _inset.AddChild(new HSeparator());
            _shotConfirm = true;
            _inset.AddChild(Ui.MarkedRow(Ui.Alarm, Ui.Wrap(Loc.T("sci.confirm"), 14, Ui.Ink)));
            var go = _sentAck;
            _inset.AddChild(Act("sci.confirm.go", () => { _sentAck = null; _host.Submit(go, "cmd.sci"); _insetDirty = true; }, true, true));
        }
    }

    static string Mo(int m) => Loc.T("sci.months", m);
    static string Units(double v) => Loc.T("sci.units", Loc.N(v, 0));

    void BuildNodeCard(HostSnapshot s, ScienceSnap sci, ImmutableArray<InstituteView> views)
    {
        var nodes = _host.Data.Nodes; int i = _selected; int num = nodes.Number[i];
        _inset.AddChild(Ui.Lbl(Names.Node(nodes.Id[i]), 20, null, FontKind.Draft));
        _inset.AddChild(Ui.Wrap($"{Loc.T("ntype." + (int)nodes.Type[i])} · {(nodes.AnchorEstimated[i] ? "≈ " : "")}{nodes.AnchorYear[i]} · {StateWord(i)}", 14, Ui.Ink2));
        var ov = ScienceMapLogic.Overlap(nodes, i, default);
        _inset.AddChild(Ui.Wrap(Loc.T("sci.dirs", string.Join(" · ", ScienceMapLogic.Cap(ov.Parts.Select(p => p.Tag + (p.Main ? " " + Loc.T("sci.main") : "")), ScienceMapLogic.NodeDirsShown))), 14, Ui.Ink));
        bool fresh = sci.Node == num;
        int[] order = fresh ? ScienceMapLogic.FitOrder(sci.Fit) : Array.Empty<int>();
        int heldBy = -1;
        foreach (var v in views) if (v.Active == num || (!v.Queue.IsDefault && v.Queue.Contains(num))) heldBy = v.Slot;
        // Price, time and military value: the quote of the best institute at no focus.
        FocusRow[] rows = Array.Empty<FocusRow>();
        int quoteSlot = heldBy >= 0 ? heldBy : order.Length > 0 ? order[0] : -1;
        if (fresh && quoteSlot >= 0) for (int f = 0; f < sci.Fit.Length; f++) if (sci.Fit[f].Slot == quoteSlot) rows = ScienceMapLogic.FocusRows(sci.Focus[f]);
        string mil = ScienceMapLogic.MilKey(nodes.MilValue[i]);
        if (!fresh) _inset.AddChild(Ui.Wrap(Loc.T("sci.wait"), 14, Ui.Ink3));
        else if (_state[i] == St.Known) _inset.AddChild(Ui.Wrap(Loc.T("sci.known"), 14, Ui.Ink2));
        else if (rows.Length == 0) _inset.AddChild(Ui.Wrap(Loc.T("sci.nodata"), 14, Ui.Ink2));
        else
        {
            var q = sci.Focus[sci.Fit.ToList().FindIndex(f => f.Slot == quoteSlot)];
            _inset.AddChild(Ui.Wrap(Loc.T("sci.numbers", Units(q[0].Price.ToDouble()), Mo(rows[0].Months)) + (mil == null ? "" : " · " + Loc.T("sci.mil", Loc.T(mil))), 16, Ui.Ink));
        }
        // «Research in an institute»
        int missing = 0;
        var rq = nodes.Requires;
        for (int g = rq.GroupStart[i]; g < rq.GroupStart[i + 1] && missing == 0; g++)
        {
            var mem = rq.GroupMembers(g).ToArray();
            if (!mem.Any(m => _state[m] == St.Known)) missing = nodes.Number[mem[0]];
        }
        var can = ScienceMapLogic.CanAssign(_state[i] == St.Known, _state[i] == St.Frontier, missing, heldBy, views, order, out int target);
        var assign = Act("sci.assign", () => { if (target >= 0) Send(new AssignNode(Player, target, num), new AssignNode(Player, target, num, true)); }, can.Enabled && fresh, true);
        _inset.AddChild(assign);
        _shotAssignEnabled = can.Enabled && fresh; _shotReason = can.Enabled ? "" : Reason(can, nodes);
        if (!can.Enabled) _inset.AddChild(Ui.Wrap(Reason(can, nodes), 16, Ui.Ink));
        // The institutes against the node.
        if (fresh && order.Length > 0)
        {
            Sec("sci.table");
            foreach (int slot in order)
            {
                var fit = sci.Fit.First(f => f.Slot == slot); var v = views[slot];
                var o = ScienceMapLogic.Overlap(nodes, i, v.DirLevels);
                string match = fit.NoOverlap ? Loc.T("sci.nobonus") : Loc.T("sci.match", o.Matching, o.Total, string.Join(" · ", o.Parts.Where(p => p.Level > 0).Select(p => p.Tag + " " + p.Level)));
                string state = v.Active == 0 ? Loc.T("sci.row.free") : v.Queue.Length >= ScienceMapLogic.QueueMax ? Loc.T("sci.row.full", ScienceMapLogic.QueueMax) : Loc.T("sci.row.busy", Mo((int)Math.Ceiling(v.MonthsLeft.ToDouble())));
                string best = slot == order[0] ? " · " + Loc.T("sci.best") : "";
                bool ok = can.Enabled && (v.Active == 0 || v.Queue.Length < ScienceMapLogic.QueueMax);
                int sl = slot;
                var row = Act(Loc.T("sci.row", slot + 1, match, Loc.N(fit.Speed.ToDouble()), Mo((int)Math.Ceiling(fit.Months.ToDouble())), state) + best, () => Send(new AssignNode(Player, sl, num), new AssignNode(Player, sl, num, true)), ok);
                _inset.AddChild(row);
            }
        }
        // Focus: bought for the node the institute works on.
        if (rows.Length > 0)
        {
            Sec("sci.focus.head");
            int active = heldBy >= 0 && views[heldBy].Active == num ? heldBy : -1;
            foreach (var r in rows)
            {
                string t = Loc.T("sci.focus." + r.Level) + ": " + Mo(r.Months) + (r.Level > 0 ? " · +" + Units(r.Extra) : "");
                int lvl = r.Level;
                bool on = active >= 0 && views[active].FocusLevel == r.Level;
                _inset.AddChild(Act((on ? "● " : "○ ") + t, () => Send(new SetFocus(Player, active, lvl, _fromReserve)), active >= 0 && !on));
            }
            _inset.AddChild(Act((_fromReserve ? "✓ " : "") + Loc.T("sci.reserve"), () => { _fromReserve = !_fromReserve; _insetDirty = true; }));
            if (active < 0) _inset.AddChild(Ui.Wrap(Loc.T("sci.focus.when"), 14, Ui.Ink2));
        }
        // What blocks it and what it opens.
        Sec("sci.blocks");
        var groups = new List<int[]>();
        for (int g = rq.GroupStart[i]; g < rq.GroupStart[i + 1]; g++) groups.Add(rq.GroupMembers(g).ToArray());
        var unmet = TreeClarity.UnmetGroups(groups, m => _state[m] == St.Known);
        if (unmet.Count == 0) _inset.AddChild(Ui.Wrap(Loc.T("sci.blocks.none"), 14, Ui.Ink2));
        foreach (var g in unmet) _inset.AddChild(Ui.Wrap("✗ " + string.Join(" | ", g.Select(m => Names.Node(nodes.Id[m]))), 14, Ui.Ink));
        var dep = _dep[i].OrderBy(x => nodes.Number[x]).Take(6).ToList();
        if (dep.Count > 0) { Sec("sci.opens"); foreach (int x in dep) _inset.AddChild(Ui.Wrap((_state[x] == St.Known ? "● " : _state[x] == St.Frontier ? "◐ " : "○ ") + Names.Node(nodes.Id[x]), 14, Ui.Ink)); }
        double priceB = fresh && rows.Length > 0 ? sci.Focus[sci.Fit.ToList().FindIndex(f => f.Slot == quoteSlot)][0].Price.ToDouble() : -1;
        if (_cmpA >= 0 && _cmpA != i) BuildCompare(sci, i, priceB, rows.Length > 0 ? rows[0].Months : -1);
        else if (_cmpA == i) { _inset.AddChild(Ui.Wrap(Loc.T("sci.cmp.pick"), 14, Ui.Ink2)); _inset.AddChild(Act("sci.cmp.close", () => { _cmpA = -1; _insetDirty = true; AskScience(); })); }
        else _inset.AddChild(Act("sci.cmp.start", () => { _cmpA = i; _insetDirty = true; AskScience(); }));
        _inset.AddChild(Act(_goalNum == num ? "sci.goal.clear" : "sci.goal.set", () => SetGoal(_goalNum == num ? 0 : num)));
    }

    // «Compare nodes» (spec §4): A is the marked node, Б the selected one; the last line is the difference of two numbers of the same quotes.
    void BuildCompare(ScienceSnap sci, int b, double priceB, int monthsB)
    {
        var nodes = _host.Data.Nodes; int a = _cmpA;
        Sec("sci.cmp");
        var rowsA = ScienceMapLogic.FocusRows(sci.Compare == nodes.Number[a] ? sci.CompareQuote : default);
        bool haveA = rowsA.Length > 0, haveB = priceB >= 0 && monthsB >= 0;
        double priceA = haveA ? sci.CompareQuote[0].Price.ToDouble() : -1;
        _inset.AddChild(Ui.Wrap(Loc.T("sci.cmp.names", Names.Node(nodes.Id[a]), Names.Node(nodes.Id[b])), 14, Ui.Ink));
        _inset.AddChild(Ui.Wrap(Loc.T("sci.cmp.row", Loc.T("sci.cmp.price"), haveA ? Units(priceA) : "—", haveB ? Units(priceB) : "—"), 14, Ui.Ink));
        _inset.AddChild(Ui.Wrap(Loc.T("sci.cmp.row", Loc.T("sci.cmp.time"), haveA ? Mo(rowsA[0].Months) : "—", haveB ? Mo(monthsB) : "—"), 14, Ui.Ink));
        string MilOf(int r) => ScienceMapLogic.MilKey(nodes.MilValue[r]) is { } k ? Loc.T(k) : "—";
        _inset.AddChild(Ui.Wrap(Loc.T("sci.cmp.row", Loc.T("sci.cmp.mil"), MilOf(a), MilOf(b)), 14, Ui.Ink));
        string Tags(int r) => string.Join(" ", ScienceMapLogic.Cap(ScienceMapLogic.Overlap(nodes, r, default).Parts.Select(p => p.Tag), ScienceMapLogic.NodeDirsShown));
        _inset.AddChild(Ui.Wrap(Loc.T("sci.cmp.row", Loc.T("sci.cmp.dirs"), Tags(a), Tags(b)), 14, Ui.Ink));
        _inset.AddChild(Ui.Wrap(Loc.T("sci.cmp.row", Loc.T("sci.cmp.state"), StateWord(a), StateWord(b)), 14, Ui.Ink));
        if (haveA && haveB)
        {
            var c = ScienceMapLogic.Compare(priceA, rowsA[0].Months, priceB, monthsB);
            string p = c.PriceWinner == 0 ? Loc.T("sci.cmp.same.price") : Loc.T(c.PriceWinner == 1 ? "sci.cmp.cheaper.a" : "sci.cmp.cheaper.b", Units(c.PriceDiff));
            string t = c.MonthsWinner == 0 ? Loc.T("sci.cmp.same.time") : Loc.T(c.MonthsWinner == 1 ? "sci.cmp.faster.a" : "sci.cmp.faster.b", Mo(c.MonthsDiff));
            _inset.AddChild(Ui.Wrap(p + " · " + t, 14, Ui.Ink));
        }
        else _inset.AddChild(Ui.Wrap(Loc.T("sci.nodata"), 14, Ui.Ink2));
        _inset.AddChild(Act("sci.cmp.swap", () => { int old = _cmpA; _cmpA = _selected; Select(old); }));
        _inset.AddChild(Act("sci.cmp.close", () => { _cmpA = -1; _insetDirty = true; AskScience(); }));
    }

    string Reason(AssignState a, NodeTable nodes) => a.Why switch
    {
        AssignWhy.Known => Loc.T("sci.why.known"),
        AssignWhy.NoInstitute => Loc.T("sci.why.none"),
        AssignWhy.AlreadyAssigned => Loc.T("sci.why.held", a.Arg + 1),
        AssignWhy.QueueFull => Loc.T("sci.why.full", ScienceMapLogic.QueueMax),
        AssignWhy.NotInFrontier => a.Arg > 0 && _rowOfNumber.TryGetValue(a.Arg, out int r) ? Loc.T("sci.why.need", Names.Node(nodes.Id[r])) : Loc.T("sci.why.need.unknown"),
        _ => "",
    };

    void BuildInstitutePanel(HostSnapshot s, InstituteView v)
    {
        var nodes = _host.Data.Nodes;
        _inset.AddChild(Ui.Lbl(Loc.T("sci.strip.name", v.Slot + 1, Loc.TOr("inst." + v.Id, v.Id)), 20, null, FontKind.Draft));
        if (!v.Standing)
        {
            if (v.BuildStart >= 0)
            {
                var st = ScienceMapLogic.Strip(v, s.View.Month);
                _inset.AddChild(Ui.Wrap(Loc.T("sci.strip.building", st.BuildLeft) + " · " + Loc.T("sci.strip.buildprog", st.BuildDone, st.BuildDone + st.BuildLeft), 14, Ui.Ink));
                if (_cancelAsk != v.Slot) _inset.AddChild(Act("sci.build.cancel", () => { _cancelAsk = v.Slot; _insetDirty = true; }));
                else
                {
                    _inset.AddChild(Ui.MarkedRow(Ui.Alarm, Ui.Wrap(Loc.T("sci.build.lost"), 14, Ui.Ink)));
                    _inset.AddChild(Act("sci.build.cancel.go", () => { _cancelAsk = -1; Send(new CancelBuild(Player, v.Slot)); }));
                }
            }
            else
            {
                _inset.AddChild(Ui.Wrap(Loc.T("sci.strip.unavail"), 14, Ui.Ink2));
                _inset.AddChild(Act("sci.build", () => Send(new BuildInstitute(Player, v.Slot)), true, true));
            }
            return;
        }
        if (v.Active > 0 && _rowOfNumber.TryGetValue(v.Active, out int ar))
        {
            var st = ScienceMapLogic.Strip(v, s.View.Month);
            _inset.AddChild(Ui.Wrap(Names.Node(nodes.Id[ar]) + " · " + Loc.T("sci.strip.progress", st.ProgressMonths, st.ProgressMonths + st.MonthsLeft, st.MonthsLeft) + " · " + Loc.T("sci.focus." + v.FocusLevel), 14, Ui.Ink));
        }
        else _inset.AddChild(Ui.Wrap(Loc.T("sci.panel.free"), 16, Ui.Ink));
        Sec("sci.queue");
        _inset.AddChild(Ui.Wrap(Loc.T("sci.strip.queue", v.Queue.IsDefault ? 0 : v.Queue.Length, ScienceMapLogic.QueueMax), 14, Ui.Ink2));
        int[] q = v.Queue.IsDefault ? Array.Empty<int>() : v.Queue.ToArray();
        _shotQueueRows = q.Length;
        for (int k = 0; k < q.Length; k++)
        {
            int idx = k, node = q[k];
            string name = _rowOfNumber.TryGetValue(node, out int rr) ? Names.Node(nodes.Id[rr]) : "?";
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 4);
            var lab = Ui.Wrap((k + 1) + ". " + name, 14, Ui.Ink); lab.CustomMinimumSize = new Vector2(0, 32);
            row.AddChild(lab);
            var up = Act("sci.up", () => { var n = ScienceMapLogic.Move(q, idx, -1); if (n != null) Send(new ReorderQueue(Player, v.Slot, [.. n])); }, k > 0);
            var dn = Act("sci.down", () => { var n = ScienceMapLogic.Move(q, idx, 1); if (n != null) Send(new ReorderQueue(Player, v.Slot, [.. n])); }, k < q.Length - 1);
            var rm = Act("sci.remove", () => Send(new Unassign(Player, v.Slot, node)));
            foreach (var kb in new Control[] { up, dn, rm }) AltMove(kb, () => { var n = ScienceMapLogic.Move(q, idx, -1); if (n != null) Send(new ReorderQueue(Player, v.Slot, [.. n])); }, () => { var n = ScienceMapLogic.Move(q, idx, 1); if (n != null) Send(new ReorderQueue(Player, v.Slot, [.. n])); });
            row.AddChild(up); row.AddChild(dn); row.AddChild(rm);
            _inset.AddChild(row);
        }
        _inset.AddChild(Ui.Wrap(Loc.T("sci.queue.note") + " " + Loc.T("sci.queue.keys"), 14, Ui.Ink3));
    }

    // ---- path to goal ----
    void BuildPath(HostSnapshot s, ScienceSnap sci, ImmutableArray<InstituteView> views)
    {
        var nodes = _host.Data.Nodes;
        Sec("sci.path");
        string goalName = _rowOfNumber.TryGetValue(_goalNum, out int gr) ? Names.Node(nodes.Id[gr]) : "?";
        _inset.AddChild(Ui.Wrap(Loc.T("sci.path.goal", goalName), 16, Ui.Ink));
        if (sci.Goal != _goalNum || sci.Path == null) { _inset.AddChild(Ui.Wrap(sci.Goal != _goalNum ? Loc.T("sci.wait") : Loc.T("sci.path.nodata"), 14, Ui.Ink2)); _inset.AddChild(Act("sci.goal.clear", () => SetGoal(0))); return; }
        var sum = ScienceMapLogic.Summary(sci.Path);
        if (sum.Nodes == 0) _inset.AddChild(Ui.Wrap(Loc.T("sci.path.done"), 14, Ui.Ink2));
        else _inset.AddChild(Ui.Wrap(Loc.T("sci.path.sum", sum.Nodes, Units(sum.Price), sum.MonthsMax == sum.MonthsMin ? Mo(sum.MonthsMin) : sum.MonthsMin + "–" + Mo(sum.MonthsMax)), 14, Ui.Ink));
        for (int k = 0; k < sci.Path.Nodes.Length; k++)
        {
            var pn = sci.Path.Nodes[k];
            string name = _rowOfNumber.TryGetValue(pn.Node, out int r) ? Names.Node(nodes.Id[r]) : "?";
            int row = r;
            var line = Act((k + 1) + ". " + name + " · " + Units(pn.Price.ToDouble()) + " · " + Mo((int)Math.Ceiling(pn.Months.ToDouble())) + " · " + Loc.T(pn.InFrontier ? "sci.path.ready" : "sci.path.waits"), () => Select(row));
            _inset.AddChild(line);
        }
        var first = sci.Path.Nodes.FirstOrDefault(p => p.InFrontier);
        _inset.AddChild(Act("sci.path.next", () => { if (_rowOfNumber.TryGetValue(first.Node, out int fr)) Select(fr); }, first.Node != 0));
        _inset.AddChild(Act("sci.path.layout", () => { _planOpen = true; _draft = sci.Plan; _insetDirty = true; }, sum.Nodes > 0 && !sci.Plan.IsDefaultOrEmpty && !_planOpen, true));
        if (sum.Nodes > 0 && sci.Plan.IsDefaultOrEmpty) _inset.AddChild(Ui.Wrap(Loc.T("sci.path.noplan"), 14, Ui.Ink));
        if (_planOpen) BuildPlanSheet(sci, views);
        _inset.AddChild(Act("sci.goal.clear", () => SetGoal(0)));
    }

    // The sheet «Spread over the institutes»: nothing is applied until «Apply»; the player may move or drop a node first.
    void BuildPlanSheet(ScienceSnap sci, ImmutableArray<InstituteView> views)
    {
        var nodes = _host.Data.Nodes;
        _inset.AddChild(new HSeparator());
        _inset.AddChild(Ui.Wrap(Loc.T("sci.plan.note"), 14, Ui.Ink2));
        var groups = ScienceMapLogic.PlanGroups(_draft, sci.Path, sci.PathFit);
        var standing = views.Where(v => v.Standing).Select(v => v.Slot).ToArray();
        foreach (var (slot, lines, months, price) in groups)
        {
            _inset.AddChild(Ui.Wrap(Loc.T("sci.plan.group", slot + 1, Units(price), Mo(months)), 16, Ui.Ink));
            foreach (var ln in lines)
            {
                string name = _rowOfNumber.TryGetValue(ln.Node, out int r) ? Names.Node(nodes.Id[r]) : "?";
                int node = ln.Node, from = slot;
                var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 4);
                var lab = Ui.Wrap("№" + ln.Number + " " + name + " · " + Units(ln.Price) + " · " + Mo(ln.Months) + (ln.WaitsForPrevious ? " · " + Loc.T("sci.plan.waits") : ""), 14, Ui.Ink);
                lab.CustomMinimumSize = new Vector2(0, 32);
                row.AddChild(lab);
                int next = standing.Length > 1 ? standing[(Array.IndexOf(standing, from) + 1) % standing.Length] : from;
                row.AddChild(Act("sci.plan.move", () => { _draft = ScienceMapLogic.Reassign(_draft, node, next); _insetDirty = true; }, next != from));
                row.AddChild(Act("sci.remove", () => { _draft = ScienceMapLogic.Reassign(_draft, node, -1); _insetDirty = true; }));
                _inset.AddChild(row);
            }
        }
        var plan = _draft;
        _inset.AddChild(Act("sci.plan.apply", () => { _planOpen = false; Send(new ApplyQueues(Player, plan), new ApplyQueues(Player, plan, true)); }, true, true));
        _inset.AddChild(Act("sci.plan.cancel", () => { _planOpen = false; _insetDirty = true; }));
    }
}
