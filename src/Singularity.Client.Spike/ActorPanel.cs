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
    // Layer-2 instruments (UI_INSTRUMENTS, #404): calipers replace the sliders at organ levels 1 and 2, flasks stand beside the Science and Production text.
    // One caliper per panel (UI_INSTRUMENTS §4): it edits the share chosen by a click on its row (Up/Down on the caliper moves the choice). The other rows stay plain numbers.
    readonly CaliperControl _cal = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    PencilMarker _pen;
    readonly Label _calName = Ui.Lbl("", 14), _calVal = Ui.Num("", 14, null, 52);
    readonly Label[] _name = new Label[5];
    readonly Control[] _gap = new Control[5];
    HBoxContainer _calRow;
    readonly int[] _force = new int[5];
    int _sel;
    readonly FlaskControl[] _flaskSci = { new() { Ink = Ui.Violet }, new() { Ink = Ui.Violet }, new() { Ink = Ui.Violet } };
    readonly HBoxContainer _flaskSciRow = new();
    readonly VBoxContainer _sciBox = new();   // caption line, then the flask row, then the empty note
    readonly VBoxContainer[] _sciCell = { new(), new(), new() };
    readonly Label[] _sciName = { Ui.Lbl("", 14, Ui.Ink2), Ui.Lbl("", 14, Ui.Ink2), Ui.Lbl("", 14, Ui.Ink2) };
    readonly Label _sciThrNote = Ui.Lbl("", 14, Ui.Ink2);   // one «threshold reached» line under the flask row (hatched flasks)
    readonly bool[] _sciThr = new bool[3];
    int _instLevel = -1, _instYear = -1;
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
    /// <summary>Screenshot run: true while the order button is inside the panel and the outer scroll is off (v0.5d).</summary>
    public bool ApplyInView => _apply.GetGlobalRect().End.Y <= GetGlobalRect().End.Y && _scroll.VerticalScrollMode == ScrollContainer.ScrollMode.Disabled;
    /// <summary>The player's own power: only it has order controls; any other power opens read-only.</summary>
    public int Player { get; set; }
    bool Foreign => _actor != Player;
    // Tag of the selected foreign power (spec §9): tint chip, name, «FOREIGN POWER · read only» and a button back to the player's own power. Not an alert: ink on paper.
    readonly PanelContainer _foreign = new();
    readonly Panel _chip = new() { CustomMinimumSize = new Vector2(16, 16), SizeFlagsVertical = SizeFlags.ShrinkCenter };
    readonly Label _foreignName = Ui.Lbl("", 16, null, FontKind.Draft);
    readonly Panel _band = new() { CustomMinimumSize = new Vector2(6, 0) };
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
    readonly VBoxContainer _finLaterBox = new();
    Control _treeBtn, _headerView, _prodLater, _finLater;
    AreaId _area = AreaId.Map;
    bool _compactHead;
    bool Compact => GetViewportRect().Size.Y < AreaHeader.CompactBelow;
    public AreaId Area
    {
        get => _area;
        set
        {
            _area = value;
            if (_headerView != null) { _root.RemoveChild(_headerView); _headerView.QueueFree(); _headerView = null; }
            if (_areaMode)
            {
                _compactHead = Compact;
                _headerView = new AreaHeader(AreaData.Of(value), false, _compactHead);
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
        head.AddChild(Ui.Btn("ui.tree", () => OpenTree?.Invoke(), focusable: true));
        head.AddChild(Ui.Btn("×", () => Closed?.Invoke(), focusable: true));
        _root.AddChild(head); head.Visible = !areaMode;
        var fv = new VBoxContainer();
        var fr = new HBoxContainer(); fr.AddThemeConstantOverride("separation", 8);
        fr.AddChild(_chip); fr.AddChild(_foreignName);
        fv.AddChild(fr);
        fv.AddChild(Ui.Lbl("panel.foreign", 14, Ui.Ink2, FontKind.Draft));
        var back = Ui.Btn("panel.backown", () => BackToOwn?.Invoke(), focusable: true);
        back.TooltipText = "tip.backown";
        fv.AddChild(back);
        _foreign.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Paper, Ui.Ink, 1, 8, Ui.R2));
        // A 6 px band on the left edge in the power's tint (set in ShowActor) marks whose panel this is.
        var fh = new HBoxContainer(); fh.AddThemeConstantOverride("separation", 10);
        fh.AddChild(_band); fv.SizeFlagsHorizontal = SizeFlags.ExpandFill; fh.AddChild(fv);
        _foreign.AddChild(fh);
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
            var r = new HBoxContainer { CustomMinimumSize = new Vector2(0, 24) };   // a click target of at least 24 du
            var n = Ui.Lbl("share." + i, 14); n.CustomMinimumSize = new Vector2(138, 0); _name[i] = n;
            r.MouseFilter = MouseFilterEnum.Stop;
            r.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && InstrumentLogic.OrganLevel > 0 && _areaMode) Select(k); };
            _sl[i] = new HSlider { MinValue = 0, MaxValue = 100, Step = 1, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(100, 22) };
            StyleSlider(_sl[i]);
            _sl[i].ValueChanged += v => OnSlider(k, (int)v);
            _slVal[i] = Ui.Num("", 14, null, 52);
            _slForce[i] = Ui.Num("", 14, Ui.Ink2, 84);
            r.AddChild(n); _gap[i] = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, Visible = false };   // keeps the number columns in place while the caliper replaces the slider
            _gap[i].MouseFilter = MouseFilterEnum.Ignore;   // a click on the blank part still reaches the row (selects the share)
            r.AddChild(_sl[i]); r.AddChild(_gap[i]); r.AddChild(_slVal[i]); r.AddChild(_slForce[i]);
            _secBudget.AddChild(r);
        }
        _calRow = new HBoxContainer(); _calRow.AddThemeConstantOverride("separation", 0);
        _calName.CustomMinimumSize = new Vector2(138, 0); _calName.SizeFlagsVertical = SizeFlags.ShrinkCenter; _calVal.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _cal.Changed += v => OnSlider(_sel, v);
        _cal.Neighbour += d => Select(Math.Clamp(_sel + d, 0, 4));
        _pen = new PencilMarker(_cal); _calVal.AddChild(_pen);
        _calRow.AddChild(_calName); _calRow.AddChild(_cal); _calRow.AddChild(_calVal); _calRow.AddChild(new Control { CustomMinimumSize = new Vector2(84, 0) });
        _secBudget.AddChild(_calRow);
        var ar = new HBoxContainer();
        ar.AddThemeConstantOverride("separation", 10);
        _apply = Ui.Btn("ui.apply", Apply, focusable: true);   // a control that changes game state is reachable by keyboard (A6)
        ar.AddChild(_apply); ar.AddChild(_sum);
        _secBudget.AddChild(ar); _orderControls.Add(ar);
        var rr = new HBoxContainer(); rr.AddThemeConstantOverride("separation", 6);
        rr.AddChild(_resultMark); rr.AddChild(_result);
        _secBudget.AddChild(rr); _orderControls.Add(rr);

        _designsHead = Section("panel.designs", _secDesigns);
        // alloy flask: waits for a Session alloy-coverage field (follow-up task)
        _secDesigns.AddChild(_designs);
        _prodLater = Ui.Wrap("area.production.later", 14, Ui.Ink3); _secDesigns.AddChild(_prodLater);
        Section("panel.licence", _secLicence);
        if (areaMode)
        {
            // Finance: only the licence block scrolls, so that the order button above it stays in view at 150 % (v0.5d).
            var ls = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 48) };   // at least one body line stays visible; the height follows the content up to 200 du (see _Process)
            _licScroll = ls;
            _licence.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var lv = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; lv.AddChild(_licence); lv.AddChild(_finLaterBox);
            ls.AddChild(lv); _licInner = lv; _secLicence.AddChild(ls);
        }
        else _secLicence.AddChild(_licence);
        _finLater = Ui.Wrap("area.finance.later", 14, Ui.Ink3); _finLaterBox.AddChild(_finLater);
        if (!areaMode) _secLicence.AddChild(_finLaterBox);
        Section("panel.why", _secWhy);
        _treeBtn = Ui.Btn("ui.tree", () => OpenTree?.Invoke(), focusable: true); _treeBtn.SizeFlagsHorizontal = SizeFlags.ShrinkBegin; _secWhy.AddChild(_treeBtn);
        // The caption has its own line, the row holds only the flask cells (3 x 112 du + gaps + insets + scroll bar < 420 du), so the panel keeps its width.
        _flaskSciRow.AddThemeConstantOverride("separation", 8);
        for (int i = 0; i < _flaskSci.Length; i++)
        {
            _sciName[i].CustomMinimumSize = new Vector2(112, 0); _flaskSci[i].SizeFlagsHorizontal = SizeFlags.ShrinkCenter; _sciName[i].TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            // C1: the name wraps by words to at most two lines at the text size; only a longer name is trimmed (tooltip has the full one)
            _sciName[i].AutowrapMode = TextServer.AutowrapMode.WordSmart; _sciName[i].MaxLinesVisible = 2; _sciName[i].CustomMinimumSize = new Vector2(112, 40);
            _sciName[i].HorizontalAlignment = HorizontalAlignment.Center; _sciName[i].MouseFilter = MouseFilterEnum.Pass;
            _sciCell[i].AddThemeConstantOverride("separation", 0);
            _sciCell[i].AddChild(_flaskSci[i]); _sciCell[i].AddChild(_sciName[i]);
            _flaskSciRow.AddChild(_sciCell[i]);
        }
        var sciCap = Ui.Wrap("flask.sci.cap", 14, Ui.Ink2);
        _sciNone.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _sciThrNote.AutowrapMode = TextServer.AutowrapMode.WordSmart; _sciThrNote.Visible = false;
        _sciBox.AddChild(sciCap); _sciBox.AddChild(_flaskSciRow); _sciBox.AddChild(_sciThrNote); _sciBox.AddChild(_sciNone);
        _secWhy.AddChild(_sciBox);
        _secWhy.AddChild(_why);
        _prodLater.Visible = _finLater.Visible = _treeBtn.Visible = areaMode;
        Loc.Changed += Invalidate;
        TreeExiting += () => Loc.Changed -= Invalidate;
    }

    void Invalidate() { _dirty = true; _sigDesigns = _sigLicence = _sigWhy = ""; }

    ScrollContainer _licScroll; Control _licInner;
    static ImageTexture _grabber;

    // A 16 x 24 du grabber with a 2 du ink frame (A2, A10): the value mark must be visible and the whole 24 du row is the click target.
    static ImageTexture Grabber()
    {
        if (_grabber != null) return _grabber;
        var img = Image.CreateEmpty(16, 24, false, Image.Format.Rgba8);
        for (int y = 0; y < 24; y++)
            for (int x = 0; x < 16; x++)
                img.SetPixel(x, y, x < 2 || x >= 14 || y < 2 || y >= 22 ? Ui.Ink : Ui.Sheet);
        return _grabber = ImageTexture.CreateFromImage(img);
    }

    public static void StyleSlider(HSlider s)
    {
        var track = new StyleBoxFlat { BgColor = Ui.Rule, BorderColor = Ui.Ink2, ContentMarginTop = 3, ContentMarginBottom = 3 };
        track.SetBorderWidthAll(1);
        s.AddThemeStyleboxOverride("slider", track);
        s.AddThemeStyleboxOverride("grabber_area", Ui.Box(Ui.Ink, null, 0, 0));
        s.AddThemeStyleboxOverride("grabber_area_highlight", Ui.Box(Ui.Ink, null, 0, 0));
        foreach (string n in new[] { "grabber", "grabber_highlight", "grabber_disabled" }) s.AddThemeIconOverride(n, Grabber());
        s.CustomMinimumSize = new Vector2(Math.Max(s.CustomMinimumSize.X, 100), 24);
        s.FocusMode = FocusModeEnum.All;   // arrow keys move a slider: it must be reachable without the mouse (A6)
        MenuUi.FocusRing(s);
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

    /// <summary>Screenshot run: the narrowest width the panel can take with the scroll bar present (content, insets, bar), in du.</summary>
    public float ShotMinWidthWithBar() => _root.GetCombinedMinimumSize().X + 24 + 12 + _scroll.GetVScrollBar().GetCombinedMinimumSize().X;
    readonly Label _sciNone = Ui.Lbl("flask.sci.none", 14, Ui.Ink2);

    /// <summary>Screenshot run: the Science flasks show these (have, need) research links instead of the Session chain (the shot scenario has none); null clears it.</summary>
    public void ShotResearch(IReadOnlyList<Link> links) { _shotLinks = links; _dirty = true; }
    IReadOnlyList<Link> _shotLinks;
    public int ShotFlaskCount { get { int c = 0; foreach (var x in _sciCell) if (x.Visible) c++; return c; } }
    public float ShotCaptionWidth(string lang) => FlaskControl.WidestCaption(lang);

    /// <summary>Screenshot run: canvas centres of the blank part of a share row and of the caliper, for the real mouse events the shot sends.</summary>
    public Vector2 ShotRowPoint(int k) => _gap[k].GetGlobalRect().GetCenter();
    public Vector2 ShotCaliperPoint(double percent) { var r = _cal.GetGlobalRect(); return new Vector2(r.Position.X + _cal.XOf(percent), r.GetCenter().Y); }
    public int ShotSelected => _sel;
    public int ShotApplied { get; private set; }
    public bool ShotScrolls => _scroll.VerticalScrollMode == ScrollContainer.ScrollMode.Auto;
    public int ShotShare(int k) => _pct[k];
    /// <summary>Opened from the keyboard: the focus moves to the first focusable control of the panel once it has laid out (the panel is its own Tab scope, so Tab alone cannot enter it from the rail).</summary>
    public void FocusFirstWhenReady() => _focusWait = 2;
    int _focusWait;
    void FocusFirst()
    {
        Control Find(Node n)
        {
            foreach (var c in n.GetChildren())
            {
                if (c is not Control ch || !ch.IsVisibleInTree()) continue;
                if (ch.FocusMode == FocusModeEnum.All) return ch;
                var f = Find(ch); if (f != null) return f;
            }
            return null;
        }
        Find(this)?.GrabFocus();
    }
    /// <summary>True while a control of this panel holds the keyboard focus.</summary>
    public bool HasFocusInside() { var f = GetViewport()?.GuiGetFocusOwner(); return f != null && IsAncestorOf(f); }
    /// <summary>Screenshot run: the budget slider that holds the keyboard focus, or -1.</summary>
    public bool ShotSlidersVisible => _sl[0].IsVisibleInTree();
    public int ShotFocusedSlider { get { for (int k = 0; k < 5; k++) if (_sl[k].HasFocus()) return k; return -1; } }

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

    // A pale tint on paper gets a dark 1 px edge (ΔL* to the paper under TintStrip.MinDeltaL), so that the strip never melts into the panel.
    static void Swatch(Panel p, Color tint)
    {
        bool edge = TintStrip.NeedsEdge(tint.ToHtml(false), Palette.C.Paper);
        p.AddThemeStyleboxOverride("panel", Ui.Box(tint, edge ? Ui.Ink : null, edge ? 1 : 0, 0, 0));
    }

    void Sync()
    {
        for (int k = 0; k < 5; k++)
        {
            _sl[k].SetValueNoSignal(_pct[k]); _slVal[k].Text = _pct[k] + "%";
        }
        _cal.Value = _pct[_sel]; _calVal.Text = _pct[_sel] + "%"; _calName.Text = Loc.T("share." + _sel);
        // pencil: the chosen share holds an edit that was not sent yet (after the order it goes out)
        bool unsent = _edited && (_sentPct == null ? _pct[_sel] != _force[_sel] : _pct[_sel] != _sentPct[_sel]);
        _pen.Set(!Foreign, unsent);
        StyleNames();
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
        ShotApplied++;
        _sentPct = (int[])_pct.Clone();
        _awaiting = true; _sentSeq = _lastSnap?.CmdSeq ?? 0;
        Sync();
    }

    public override void _Process(double delta)
    {
        if (_focusWait > 0 && Visible && --_focusWait == 0) FocusFirst();
        // Height follows the content (an inset, not a full-height column), capped by the window.
        if (_areaMode && _headerView != null && Compact != _compactHead) Area = _area;   // the window was resized across the threshold
        bool finance = _areaMode && _area == AreaId.Finance;   // Finance: the outer scroll stays off, the licence block scrolls on its own
        if (_licScroll != null) _licScroll.CustomMinimumSize = new Vector2(0, Math.Clamp(_licInner.GetCombinedMinimumSize().Y, 48, 200));
        float want = _root.GetCombinedMinimumSize().Y + 6, cap = GetViewportRect().Size.Y - 120;
        _scroll.CustomMinimumSize = new Vector2(0, Math.Min(want, cap));
        _pad.AddThemeConstantOverride("margin_right", want <= cap || finance ? 0 : 12);
        _scroll.VerticalScrollMode = want <= cap || finance ? ScrollContainer.ScrollMode.Disabled : ScrollContainer.ScrollMode.Auto;   // a scroll bar only when the content really does not fit
        var s = _host.Snap;
        SyncInstruments(s?.View.Date.Year ?? 1945);
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
        if (Foreign) { _foreignName.Text = Names.Actor(_host.Layout.ActorIds[_actor]); if (TintOf != null) { Swatch(_chip, TintOf(_actor)); Swatch(_band, TintOf(_actor)); } }
        foreach (var c in _orderControls) c.Visible = !Foreign;
        _calRow.Visible = InstrumentLogic.CaliperShown(InstrumentLogic.OrganLevel, _areaMode, Foreign);
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
        Array.Copy(forcePct, _force, 5);
        Sync();
        FillResult(s);
        FillDesigns(a);
        FillLicence(a);
        FillWhy(s);
        FillFlasks(s);
    }

    static readonly StyleBoxFlat _nameOn = Ui.Box(Ui.Hover, Ui.Ink, 1, 4, Ui.R1), _nameOff = Ui.Box(Colors.Transparent, null, 0, 4, 0);   // shared, built once

    void Select(int k) { _sel = k; Sync(); }

    // The chosen share is marked by a frame and a wash on its name (a shape, not a colour); only while the caliper is on.
    void StyleNames()
    {
        bool organ = InstrumentLogic.OrganShown(InstrumentLogic.OrganLevel, _areaMode);
        for (int k = 0; k < 5; k++)
        {
            if (!organ) { _name[k].RemoveThemeStyleboxOverride("normal"); continue; }
            _name[k].AddThemeStyleboxOverride("normal", k == _sel ? _nameOn : _nameOff);
        }
    }

    void SetResearchFlask(int n, InstrumentLogic.ResearchFlask f)
    {
        string name = Names.Node(_host.Data, f.Node);
        double have = f.Have, need = f.Need, lv = InstrumentLogic.FlaskLevel(have, need);
        bool thr = f.Threshold;   // a hazard node at its threshold: full, hatched, not discovered
        _flaskSci[n].Set(lv, false, thr);
        _sciName[n].Text = name;
        _sciThr[n] = thr;
        _flaskSci[n].TooltipText = _sciName[n].TooltipText = thr
            ? Loc.T("flask.sci.threshold", name, Loc.N(have), Loc.N(need))
            : Loc.T("flask.sci.tip", name, InstrumentLogic.FlaskPercent(lv, false), Loc.N(have), Loc.N(need));
    }

    // Instrument layer: level and era follow the setting and the game date; at level 0 the sliders are back and the flasks are gone (the «before» look).
    void SyncInstruments(int year)
    {
        int level = InstrumentLogic.OrganLevel;
        year = InstrumentLogic.YearOverride ?? year;
        if (level == _instLevel && year == _instYear) return;
        _instLevel = level; _instYear = year;
        _cal.Year = year;
        bool organ = InstrumentLogic.OrganShown(level, _areaMode);   // layer 2 never shows in the read-only panel of a foreign power
        for (int k = 0; k < 5; k++) { _sl[k].Visible = !organ; _gap[k].Visible = organ; }
        _calRow.Visible = InstrumentLogic.CaliperShown(level, _areaMode, Foreign);
        StyleNames();
        _sciBox.Visible = level > 0 && _areaMode;
    }

    // Science: the first links of the why_not chain as Have against Need (the same numbers as the text lines).
    void FillFlasks(HostSnapshot s)
    {
        int n = 0;
        IReadOnlyList<Link> links = _shotLinks ?? (s.WhyError == "" && s.Target != null ? s.Links : Array.Empty<Link>());
        foreach (var f in InstrumentLogic.ResearchFlasks(links, _flaskSci.Length)) SetResearchFlask(n++, f);
        for (int i = 0; i < _flaskSci.Length; i++) _sciCell[i].Visible = i < n;
        _flaskSciRow.Visible = n > 0;
        bool anyThr = false; for (int i = 0; i < n; i++) anyThr |= _sciThr[i];
        _sciThrNote.Visible = anyThr; _sciThrNote.Text = anyThr ? Loc.T("flask.sci.threshold.note") : "";
        _sciNone.Visible = n == 0;   // no research chain on the current target: say so, never an empty row
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
        // The handle comes from the view, never computed here (ADR-0005 p. 3): a button sent after the slot was retaken is rejected by the layer.
        var rows = new List<(string text, string action, DesignHandle design)>();
        foreach (var d in a.Designs)
        {
            if (d.Stage == DesignStage.None) continue;
            string line = Loc.T("design.line", d.Design, d.Gen, Loc.T("stage." + (int)d.Stage), Loc.N(d.UnitCost.ToDouble()), Loc.N(d.Readiness.ToDouble()), Loc.N(d.InService.ToDouble(), 0))
                + (d.Stage is DesignStage.Series ? Loc.T("design.series", Loc.N(d.Rate.ToDouble()), Loc.T("bind." + d.Bind)) : "");
            rows.Add((line, d.Stage == DesignStage.Series ? "close" : d.Stage is DesignStage.Project or DesignStage.Prototype ? "cancel" : "", d.Handle));
        }
        string sig = Loc.Lang + "|" + string.Join("\n", rows.Select(r => r.text + r.action + r.design.Generation));
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
            if (!Foreign && action == "close") act = Ui.Btn("ui.closeline", () => _host.Submit(new CloseLine(_actor, design), "cmd.close", design.Slot), focusable: true);
            else if (!Foreign && action == "cancel") act = Ui.Btn("ui.cancel", () => _host.Submit(new CancelDesign(_actor, design), "cmd.cancel", design.Slot), focusable: true);
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
