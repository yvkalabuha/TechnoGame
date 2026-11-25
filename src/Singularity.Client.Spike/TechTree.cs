using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Godot;
using Singularity.Core.Commands;
using Singularity.Core.Data;
using Singularity.Core.Products;
using Singularity.Core.WhyNot;
using Singularity.Kernel;
using Singularity.Session;

namespace Singularity.Client.Spike;

/// <summary>
/// Tech tree screen (TECH_TREE_SCREEN): A «Планисфера» and B «Ведомость цели» over real NodeView/requires data of the chosen power,
/// plus the right inset (why_not, node, effort focus, priorities). No per-node progress or ETA intervals exist in Session: those are marked as mock.
/// Colour rule: ultramarine = the route and orders only; red = the root of «why not» only.
/// </summary>
public sealed partial class TechTree : Control
{
    enum St { Known, Frontier, Unavailable }

    static readonly Color EmbossLight = new("FFFFFA");
    const float SubpanelButtonDu = 32;   // A10: a button is at least 32 du high
    const float CloseW = 190, CloseH = 40;   // #473: the close button is 40 du high
    const float ZoomRate = 14f;
    const float WedgeMargin = 0.1f;

    readonly SimHost _host;
    readonly VBoxContainer _inset = new();
    readonly MarginContainer _insetPad = new();
    readonly PanelContainer _insetBox = new();
    readonly ScrollContainer _insetScroll;
    readonly Segment _viewSwitch;
    readonly Button _legendBtn = new() { FocusMode = Control.FocusModeEnum.All, MouseFilter = MouseFilterEnum.Stop, Text = "", Alignment = HorizontalAlignment.Left };
    int _actor = -1, _mode;
    bool _modelDirty = true, _insetDirty = true;
    SliceView _lastView; CmdResult _lastCmd; long _drawnTick = -1; CivilDate _date;
    int _selected = -1;                              // node row
    St[] _state; bool[] _route; int _root = -1, _goal = -1;
    List<int> _path = new();                         // root -> goal chain (route line)
    NodeView[] _nv; int[] _depth;
    readonly Dictionary<int, int> _rowOfNumber = new();
    readonly List<int> _focus = new();               // SetEffortFocus draft, process numbers
    float[] _rad, _ang;                              // radius in base units (from the anchor year) and angle in degrees
    int[] _sec; int _nSec = 5;                       // sector (discipline) of each node and the number of sectors
    string[] _yearText; float[] _yearW;              // «1953», «≈1953» per node (step 5), built once per model
    Rect2 _nowRect; string _hdrText, _nowText; int _hdrKn = -1, _hdrTot = -1, _nowYear = -1;
    readonly Button _closeBtn, _zoomIn, _zoomOut, _fit; readonly Label _stepLbl; readonly HBoxContainer _zoomBar = new();
    int _stepShown = -1, _hover = -1;
    readonly List<(Rect2 rect, int row)> _rowRects = new();
    List<int>[] _dep;                                // dependants: row -> rows that list it in requires
    // Zoom and pan of the planisphere (PlanisphereMath). The drawing is clipped to the view control; _cv is the item being painted.
    readonly Control _view = new() { ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
    CanvasItem _cv;
    float _zoom = 1f, _zoomTarget = 1f; Vector2 _pan, _anchor;
    bool _press, _dragged, _longDone; Vector2 _pressPos, _mouse; ulong _pressMs; int _pressRow = -1;
    Lod CurLod => PlanisphereMath.LodOf(_zoom);
    int Step => ZoomSteps.StepOf(_zoom);
    /// <summary>The player's own power: orders (effort focus) are enabled only for it.</summary>
    public int Player { get; set; }
    public event Action Closed;

    public TechTree(SimHost host)
    {
        _host = host; _cv = this;
        MouseFilter = MouseFilterEnum.Stop; FocusMode = FocusModeEnum.All;   // keyboard walk over the nodes (TASK-0132)
        FocusEntered += Redraw; FocusExited += Redraw;

        var top = new HBoxContainer { Position = new Vector2(14, 8) };
        top.AddThemeConstantOverride("separation", 4);
        top.AddChild(Ui.Lbl("tree.path", 18, null, FontKind.Draft));
        top.AddChild(new Control { CustomMinimumSize = new Vector2(10, 0) });
        _viewSwitch = new Segment(new[] { "tree.a", "tree.b" }, 0, minHeight: SubpanelButtonDu, focusable: true);
        _viewSwitch.Changed += i => { _mode = i; Redraw(); };
        foreach (var cell in _viewSwitch.GetChildren())   // A7: the focus of a tab is a 3 px ultramarine ring 3 du off the frame, not the ink frame of the selected tab
            if (cell is Button tb) tb.AddThemeStyleboxOverride("focus", new StyleBoxFlat { DrawCenter = false, BorderColor = Ui.Command, AntiAliasing = true, ExpandMarginLeft = 6, ExpandMarginRight = 6, ExpandMarginTop = 6, ExpandMarginBottom = 6 }.With2(s => { s.SetBorderWidthAll(3); s.SetCornerRadiusAll(6); }));
        top.AddChild(_viewSwitch);
        // «Close» (#473): 40 du high in the top-right corner of the sheet, always in view; a heavy 3 du frame in every state keeps it the most visible button,
        // focus keeps the two-tone ring of CaptionButton (A6, A7). Esc does the same.
        _closeBtn = Ui.Btn("tree.close", () => Closed?.Invoke(), focusable: true); _closeBtn.CustomMinimumSize = new Vector2(CloseW, CloseH);
        _closeBtn.AddThemeStyleboxOverride("normal", Ui.Box(Ui.Sheet, Ui.Ink, 3, 10));
        _closeBtn.AddThemeStyleboxOverride("hover", Ui.Box(Ui.Hover, Ui.Ink, 3, 10));
        _closeBtn.AddThemeStyleboxOverride("pressed", Ui.Box(Ui.Ink, Ui.Ink, 3, 10));
        // Zoom bar at the lower left of the map: − + whole disc, and «Step N of 6» (C3).
        _zoomOut = Ui.Btn("−", () => ZoomBy(-1), focusable: true); _zoomIn = Ui.Btn("+", () => ZoomBy(1), focusable: true); _fit = Ui.Btn("tree.reset", ResetView, focusable: true);
        _zoomOut.TooltipText = Loc.T("tree.zoom.out"); _zoomIn.TooltipText = Loc.T("tree.zoom.in");
        _zoomOut.CustomMinimumSize = new Vector2(40, SubpanelButtonDu); _zoomIn.CustomMinimumSize = new Vector2(40, SubpanelButtonDu); _fit.CustomMinimumSize = new Vector2(0, SubpanelButtonDu);
        _stepLbl = Ui.Lbl("", 14, Ui.Ink); _stepLbl.CustomMinimumSize = new Vector2(120, SubpanelButtonDu); _stepLbl.VerticalAlignment = VerticalAlignment.Center;
        _zoomBar.AddThemeConstantOverride("separation", 4);
        foreach (Control zc in new Control[] { _zoomOut, _zoomIn, _fit, _stepLbl }) _zoomBar.AddChild(zc);
        AddChild(_view);
        _view.Draw += PaintView;
        // The closed legend is a real button above the canvas (opaque sheet, 1 du ink border, 2 px focus ring): chart ink never shows through it.
        _legendBtn.AddThemeFontOverride("font", Fonts.Text); _legendBtn.AddThemeFontSizeOverride("font_size", 14);
        foreach (var n in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) _legendBtn.AddThemeColorOverride(n, Ui.Ink2);
        _legendBtn.AddThemeStyleboxOverride("normal", Ui.Box(Ui.Sheet, Ui.Ink, 1, 6, Ui.R2));
        _legendBtn.AddThemeStyleboxOverride("hover", Ui.Box(Ui.Hover, Ui.Ink, 1, 6, Ui.R2));
        _legendBtn.AddThemeStyleboxOverride("pressed", Ui.Box(Ui.Hover, Ui.Ink, 2, 6, Ui.R2));
        MenuUi.FocusRing(_legendBtn);
        _legendBtn.Text = "L  " + Loc.T("legend.btn");
        _legendBtn.Pressed += () => ToggleLegend(true);
        _legendBtn.GuiInput += e => { if (e is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left }) _legendBtn.CallDeferred(Control.MethodName.ReleaseFocus); };   // a click must not leave the focus (Space pauses)
        AddChild(_legendBtn);
        AddChild(_zoomBar);
        AddChild(top);

        _insetBox.AddThemeStyleboxOverride("panel", DepthBox.Make(Ui.Box(Ui.Sheet, Ui.Ink, 1, 12)));
        // Sized by its content (like the power panel): anchored top-right, no bottom anchor; the scroll appears only above the stamp.
        _insetBox.AnchorLeft = 0.6f; _insetBox.AnchorRight = 1; _insetBox.OffsetLeft = 0; _insetBox.OffsetTop = CloseH + 10; _insetBox.OffsetRight = -6;   // below the close button
        var sc = _insetScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        // A12: the scroll bar is 8 du wide, ink-2 grabber on a rule track.
        var vbar = sc.GetVScrollBar(); vbar.CustomMinimumSize = new Vector2(10, 0);
        vbar.AddThemeStyleboxOverride("scroll", Ui.Box(Ui.Rule, null, 0, 0, 0));
        foreach (var gb in new[] { "grabber", "grabber_highlight", "grabber_pressed" }) vbar.AddThemeStyleboxOverride(gb, Ui.Box(Ui.Ink2, null, 0, 0, 0));
        _inset.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _inset.AddThemeConstantOverride("separation", 6);
        // 12 du of room on the right so that the scroll bar never covers the frames of the cards (A9, A12).
        _insetPad.AddThemeConstantOverride("margin_right", 12); _insetPad.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _insetPad.AddChild(_inset); sc.AddChild(_insetPad); _insetBox.AddChild(sc);
        AddChild(_insetBox);
        AddChild(_closeBtn);
        _menu = new MenuLayer(this); AddChild(_menu);
        VisibilityChanged += () => { if (!Visible) CloseMenu(); };
        _legendOpen = true;   // the legend is visible by default (Gemini finding, TASK-0149)
        Loc.Changed += Invalidate;
        TreeExiting += () => Loc.Changed -= Invalidate;
    }

    void Invalidate() { _kbTipFor = -1; _stepShown = -1; _nowYear = -1; _hdrKn = -1; _edgeKey = -2; _zoomOut.TooltipText = Loc.T("tree.zoom.out"); _zoomIn.TooltipText = Loc.T("tree.zoom.in"); CloseMenu(); _insetDirty = true; _lgDirty = true; _legendBtn.Text = "L  " + Loc.T("legend.btn"); Redraw(); }
    void Redraw() { QueueRedraw(); _view.QueueRedraw(); }

    public void ResetView() { _zoomTarget = 1f; _anchor = BaseCentre; _pan = Vector2.Zero; Redraw(); }
    /// <summary>One zoom step up (+1) or down (-1) about the middle of the map (buttons and the keys + − 0).</summary>
    public void ZoomBy(int dir) { _zoomTarget = PlanisphereMath.ClampZoom(ZoomSteps.Next(_zoomTarget, dir)); _anchor = Area.GetCenter(); Redraw(); }
    public int ShotStep => Step;
    /// <summary>Screenshot run: jumps the zoom to a value and centres the given row (none: the drawing centre).</summary>
    public void ShotZoom(float zoom, int rowToCentre)
    {
        _zoom = _zoomTarget = PlanisphereMath.ClampZoom(zoom);
        _pan = Vector2.Zero;
        if (rowToCentre >= 0) { _pan = Area.GetCenter() - PosOf(rowToCentre); ClampPan(); }
        Redraw();
    }
    /// <summary>Shot run: zooms to the Icons level and centres on a node so that all six node types stand fully in the chart area; returns null on success, else why not.</summary>
    public string ShotZoomAllTypes()
    {
        var nodes = _host.Data.Nodes;
        for (float z = PlanisphereMath.IconsFrom; z <= PlanisphereMath.IconsFrom + 1.5f; z += 0.1f)
        {
            ShotZoom(z, -1);
            var area = Area.Grow(-(NR + 8));
            for (int c = 0; c < _rad.Length; c++)
            {
                var d = Area.GetCenter() - PosOf(c);
                var seen = new bool[6];
                for (int j = 0; j < _rad.Length; j++) if (area.HasPoint(PosOf(j) + d)) seen[(int)nodes.Type[j]] = true;
                if (Array.IndexOf(seen, false) >= 0) continue;
                ShotZoom(z, c);
                var seen2 = new bool[6];
                for (int j = 0; j < _rad.Length; j++) if (area.HasPoint(PosOf(j))) seen2[(int)nodes.Type[j]] = true;
                if (Array.IndexOf(seen2, false) < 0 && CurLod == Lod.Icons) return null;
            }
        }
        ShotZoom(PlanisphereMath.IconsFrom, -1);
        return "no view of the Icons level shows all six node types";
    }
    public int GoalRow => _goal;
    /// <summary>Shot run: a node worth centring on: the route frontier, else any frontier node, else the goal, else the first node.</summary>
    public int ShotPickRow()
    {
        if (FrontierRow >= 0) return FrontierRow;
        for (int i = 0; _state != null && i < _state.Length; i++) if (_state[i] == St.Frontier) return i;
        return _goal >= 0 ? _goal : 0;
    }
    public int SelectedRow => _selected;
    public int FrontierRow { get { for (int i = 0; _state != null && i < _state.Length; i++) if (_state[i] == St.Frontier && _route[i]) return i; return -1; } }

    public void ShowFor(int actor, int mode)
    {
        _actor = actor; _modelDirty = true; _insetDirty = true; _mode = mode; _viewSwitch.Select(mode, false); _focus.Clear(); _selected = -1; _kb = -1; _kbActive = false; CloseMenu(); Visible = true; CallDeferred(Control.MethodName.GrabFocus);   // Tab and the arrows work at once
    }
    public void SetMode(int mode) { _mode = mode; _viewSwitch.Select(mode, false); Redraw(); }
    /// <summary>Lowest edge of the goal panel: above the open legend sheet, else the bottom margin.</summary>
    public float InsetLimit => LegendOpen ? Size.Y - (6 + StampH + StampGap + LegendH + 14) : Size.Y - 100;
    public void Select(int row) { _selected = row; _insetDirty = true; Redraw(); }

    // ---------------- model ----------------
    public override void _Process(double delta)
    {
        // The goal panel yields to the open legend: its bottom stays 8 du above the sheet (the sheet top is Size.Y - 332) and the rest scrolls inside.
        float limit = InsetLimit;
        float topShift = _insetBox.OffsetTop - 6f;   // the box starts below the close button, so its room is that much shorter
        _insetScroll.CustomMinimumSize = new Vector2(0, Math.Min(_insetPad.GetCombinedMinimumSize().Y, LegendOpen ? limit - topShift - _insetBox.GetThemeStylebox("panel").GetMinimumSize().Y : limit - topShift));
        _closeBtn.Size = new Vector2(CloseW, CloseH); _closeBtn.Position = new Vector2(Size.X - 6 - CloseW, 4);
        _zoomBar.Position = new Vector2(Area.Position.X + 10, Area.End.Y - 42); _zoomBar.Visible = _mode == 0;
        _legendBtn.Position = new Vector2(Area.Position.X + 10, Area.End.Y - 82); _legendBtn.Size = new Vector2(144, 32);
        _legendBtn.Visible = _mode == 0 && !LegendOpen;
        if (_stepShown != Step) { _stepShown = Step; _stepLbl.Text = Loc.T("tree.step", Step); }
        PollLongPress();
        _view.Position = Area.Position; _view.Size = Area.Size;
        if (Math.Abs(_zoom - _zoomTarget) > 0.001f)
        {
            float old = _zoom;
            _zoom += (_zoomTarget - _zoom) * PlanisphereMath.EaseStep(delta, ZoomRate);
            if (Math.Abs(_zoom - _zoomTarget) <= 0.001f) _zoom = _zoomTarget;
            var c = BaseCentre + _pan;
            var (px, py) = PlanisphereMath.PanAfterZoom(_pan.X, _pan.Y, c.X, c.Y, _anchor.X, _anchor.Y, old, _zoom);
            _pan = new Vector2(px, py); ClampPan(); Redraw();
        }
        var s = _host.Snap;
        if (s == null || _actor < 0 || !Visible) return;
        if (s.WhyActor != _actor) return;
        if ((_modelDirty || !ReferenceEquals(s.View, _lastView)) && s.View.Actors[_actor].Nodes.IsPresent)   // not subscribed yet: keep the dirty flag, retry on the next snapshot
        {
            _modelDirty = false; _lastView = s.View;
            Rebuild(s);
            _insetDirty = true; _drawnTick = -1;
        }
        if (_insetDirty || !ReferenceEquals(s.Last, _lastCmd))
        {
            _insetDirty = false; _lastCmd = s.Last;
            BuildInset(s);
        }
        if (s.Tick != _drawnTick) { _drawnTick = s.Tick; _date = s.Date; Redraw(); }   // the clock follows the session date
    }

    void Rebuild(HostSnapshot s)
    {
        var nodes = _host.Data.Nodes;
        int n = nodes.Count;
        if (_rowOfNumber.Count == 0) for (int i = 0; i < n; i++) _rowOfNumber[nodes.Number[i]] = i;
        var byNumber = s.View.Actors[_actor].Nodes;
        _nv = new NodeView[n];
        foreach (var v in byNumber) _nv[_rowOfNumber[v.Number]] = v;
        var known = new bool[n];
        for (int i = 0; i < n; i++) known[i] = _nv[i].Known;
        _state = new St[n];
        for (int i = 0; i < n; i++)
            _state[i] = known[i] ? St.Known : nodes.Requires.IsSatisfied(i, known) ? St.Frontier : St.Unavailable;

        // Goal: the APP node of the required generation (spec §5.6: the newest known APP the actor has not fielded). The route is the union of the unknown
        // closures of the KNOWLEDGE links of why_not (what blocks building it); if the APP itself is unknown, its own closure.
        _root = -1; _goal = -1; _route = new bool[n];
        foreach (var l in s.Links) if (l.Layer == WhyNotLayer.Knowledge && _rowOfNumber.TryGetValue(l.SubjectId, out int r))
        {
            if (_root < 0) _root = r;
            var cl = Closure(r, known);
            for (int i = 0; i < n; i++) _route[i] |= cl[i];
        }
        if (s.Target is { Kind: WhyNotTargetKind.Requirement } t)
        {
            for (int i = 0; i < n && _goal < 0; i++)
                if (nodes.Type[i] == NodeType.APP && nodes.Gen[i] == t.Id && known[i]) _goal = i;
            for (int i = 0; i < n && _goal < 0; i++)
                if (nodes.Type[i] == NodeType.APP && nodes.Gen[i] == t.Id)
                {
                    _goal = i;
                    var cl = Closure(i, known);
                    for (int k = 0; k < n; k++) _route[k] |= cl[k];
                }
        }
        _dep = new List<int>[n];
        for (int i = 0; i < n; i++) _dep[i] = new List<int>();
        var rqd = nodes.Requires;
        for (int i = 0; i < n; i++)
            for (int g = rqd.GroupStart[i]; g < rqd.GroupStart[i + 1]; g++)
                foreach (int m in rqd.GroupMembers(g)) if (!_dep[m].Contains(i)) _dep[m].Add(i);
        _depth = new int[n];
        for (int i = 0; i < n; i++) _depth[i] = Depth(i, known, new Dictionary<int, int>());
        _path = RoutePath(n);
        Layout();
    }

    // Root -> goal chain along "is required by" edges (shortest); when the data has no such chain the route is the direct root-goal link.
    List<int> RoutePath(int n)
    {
        var res = new List<int>();
        if (_root < 0 || _goal < 0) return res;
        var dep = _dep;
        var prev = new int[n]; Array.Fill(prev, -2);
        var q = new Queue<int>(); q.Enqueue(_root); prev[_root] = -1;
        while (q.Count > 0 && prev[_goal] == -2)
        {
            int x = q.Dequeue();
            foreach (int y in dep[x]) if (prev[y] == -2) { prev[y] = x; q.Enqueue(y); }
        }
        if (prev[_goal] == -2) { res.Add(_root); res.Add(_goal); return res; }
        for (int c = _goal; c >= 0; c = prev[c]) res.Add(c);
        res.Reverse();
        return res;
    }

    bool[] Closure(int goal, bool[] known)
    {
        var res = new bool[known.Length];
        var st = new Stack<int>(); st.Push(goal);
        var rq = _host.Data.Nodes.Requires;
        while (st.Count > 0)
        {
            int x = st.Pop();
            if (res[x] || known[x]) continue;
            res[x] = true;
            for (int g = rq.GroupStart[x]; g < rq.GroupStart[x + 1]; g++) foreach (int m in rq.GroupMembers(g)) st.Push(m);
        }
        return res;
    }

    // Length of the longest chain of unknown requirements below the node (0 = frontier); any_of takes its shortest alternative.
    int Depth(int x, bool[] known, Dictionary<int, int> memo)
    {
        if (known[x]) return -1;
        if (memo.TryGetValue(x, out int d)) return d;
        var rq = _host.Data.Nodes.Requires;
        int best = 0;
        for (int g = rq.GroupStart[x]; g < rq.GroupStart[x + 1]; g++)
        {
            int alt = int.MaxValue;
            foreach (int m in rq.GroupMembers(g)) alt = Math.Min(alt, known[m] ? 0 : Depth(m, known, memo) + 1);
            if (alt != int.MaxValue) best = Math.Max(best, alt);
        }
        memo[x] = best;
        return best;
    }

    // Radius = anchor year, sector = discipline appended clockwise; the type of a node is its shape (owner #473). Nodes of one sector are spread by TreeYearLayout.Place.
    const float MinGapUnits = 0.3f;
    void Layout()
    {
        var nodes = _host.Data.Nodes; int n = nodes.Count;
        _rad = new float[n]; _ang = new float[n]; _sec = new int[n];
        int maxD = 0;
        for (int i = 0; i < n; i++) maxD = Math.Max(maxD, (int)nodes.Discipline[i]);
        _nSec = Math.Clamp(maxD + 1, 1, TreeYearLayout.MaxSectors);
        var year = new int[n]; var num = new int[n];
        _yearText = new string[n]; _yearW = new float[n];
        for (int i = 0; i < n; i++)
        {
            _sec[i] = Math.Min((int)nodes.Discipline[i], _nSec - 1); year[i] = nodes.AnchorYear[i]; num[i] = nodes.Number[i];
            _yearText[i] = (nodes.AnchorEstimated[i] ? "≈" : nodes.AnchorBefore[i] ? "<" : "") + nodes.AnchorYear[i];   // an estimated year is said to be one (#473)
            _yearW[i] = Fonts.Mono.GetStringSize(_yearText[i], HorizontalAlignment.Left, -1, 14).X;
        }
        TreeYearLayout.Place(year, _sec, num, _nSec, WedgeMargin, MinGapUnits, _rad, _ang);
    }

    // ---------------- drawing ----------------
    Rect2 Area => new(0, 44, Size.X * 0.6f - 8, Size.Y - 52);
    // The legend never takes room from the disc (spec task-0117-rail-and-legend §3): it docks in the right column above the sheet stamp.
    Vector2 BaseCentre => Area.Position + new Vector2(Area.Size.X * 0.5f, (Area.Size.Y - 76) * 0.5f + 4);
    Vector2 Centre => BaseCentre + _pan;
    public float BaseUnit => (Math.Min(Area.Size.X, Area.Size.Y - 76) * 0.5f - 44f) / 6.5f;
    /// <summary>Diameter of the disc in du (13 base units).</summary>
    public float DiscDiameter => 13f * BaseUnit;
    float Unit => BaseUnit * _zoom;

    void ClampPan()
    {
        var (x, y) = PlanisphereMath.ClampPan(_pan.X, _pan.Y, BaseUnit * 6.5f * _zoom);
        _pan = new Vector2(x, y);
    }

    Vector2 Polar(float ring, float deg) { float a = Mathf.DegToRad(deg); return Centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (ring * Unit); }
    Vector2 PosOf(int i) => Polar(_rad[i], _ang[i]);

    public override void _Draw()
    {
        _cv = this;
        _view.Visible = _mode == 0;
        _cv.DrawStyleBox(Ui.Box(Ui.Paper, Ui.Ink, 2, 0), new Rect2(Vector2.Zero, Size));
        DrawStamp();
        if (_state == null) return;
        if (_mode == 1) DrawStatement(); else DrawLegend();
    }

    // The planisphere is painted on the clipped view; coordinates stay in the parent's space.
    void PaintView()
    {
        if (_state == null || _mode != 0) return;
        _cv = _view; ShotGlyphs.Clear();
        _view.DrawSetTransform(-Area.Position);
        DrawPlanisphere();
        _cv = this;
    }

    string Fit(string s, float maxW, int size, Font font = null)
    {
        font ??= Fonts.Text;
        if (font.GetStringSize(s, HorizontalAlignment.Left, -1, size).X <= maxW) return s;
        _fitCuts++;
        while (s.Length > 1 && font.GetStringSize(s + "…", HorizontalAlignment.Left, -1, size).X > maxW) s = s[..^1];
        return s + "…";
    }

    // Word-wrapped paragraph (a line of meaning is never cut with an ellipsis; a word wider than the line is broken between characters); returns its height.
    float Para(Vector2 p, string s, float maxW, int size, Color c, FontKind kind = FontKind.Text)
    {
        var font = Ui.FontOf(kind); int fs = Math.Max(size, Ui.MinSize);
        float y = p.Y;
        if (!_wrap.TryGet(s, maxW, fs, (int)kind, out var lines)) lines = WrapMiss(s, maxW, fs, kind, font);
        foreach (var line in lines)
        {
            if (_recording) { float lw = font.GetStringSize(line, HorizontalAlignment.Left, -1, fs).X; if (TextWrap.Overflows(line, lw, maxW)) _paraBad.Add($"paragraph line '{line}' is {lw:F0} du, wider than its {maxW:F0} du"); }
            Record(font, new Vector2(p.X, y), line, fs, HorizontalAlignment.Left, -1);
            _cv.DrawString(font, new Vector2(p.X, y), line, HorizontalAlignment.Left, -1, fs, c); y += RailMetrics.Line14Du;
        }
        return y - p.Y;
    }

    readonly WrapCache _wrap = new();
    // The cache miss lives apart so that the hit path of Para holds no closure.
    List<string> WrapMiss(string s, float maxW, int fs, FontKind kind, Font font) => _wrap.Add(s, maxW, fs, (int)kind, TextWrap.Lines(s, maxW, t => font.GetStringSize(t, HorizontalAlignment.Left, -1, fs).X));

    // Rendered bounds of every line the statement draws on the canvas (shot run: each must stay inside the statement area).
    readonly List<Rect2> _drawn = new(); readonly List<string> _drawnText = new(); bool _recording;
    void Record(Font font, Vector2 p, string s, int fs, HorizontalAlignment al, float w)
    {
        if (!_recording) return;
        float sw = font.GetStringSize(s, HorizontalAlignment.Left, -1, fs).X;
        float x = al == HorizontalAlignment.Right && w > 0 ? p.X + w - sw : p.X;
        _drawn.Add(new Rect2(x, p.Y - font.GetAscent(fs), sw, font.GetAscent(fs) + font.GetDescent(fs))); _drawnText.Add(s);
    }

    /// <summary>Shot run only: record the drawn lines and check them (no per-line measuring in play).</summary>
    public bool ShotRecord;
    readonly List<string> _paraBad = new();   // shot run: paragraph lines wider than their own width
    int _fitCuts;   // ellipsized strings drawn since the statement began (shot run check)
    public int ShotFitCuts => _fitCuts;

    void Text(Vector2 p, string s, int size, Color c, FontKind kind = FontKind.Text, HorizontalAlignment al = HorizontalAlignment.Left, float w = -1)
    {
        var font = Ui.FontOf(kind); int fs = Math.Max(size, Ui.MinSize);
        Record(font, p, s, fs, al, w);
        _cv.DrawString(font, p, s, al, w, fs, c);
    }

    // Sheet stamp (DraftFrame §7.10.1): title, actor, session date.
    void DrawStamp()
    {
        var r = new Rect2(Size.X - 6 - 360, Size.Y - 6 - 62, 360, 62);
        _cv.DrawStyleBox(DepthBox.Plain, r);
        _cv.DrawLine(r.Position + new Vector2(1, 26), r.Position + new Vector2(r.Size.X - DepthBox.Edge - 1, 26), Ui.Ink, 1f);
        // Embossed title (VD 3.2): a light edge up-left, a dark one down-right, no gradient.
        var title = Loc.T("tree.stamp"); var tp = r.Position + new Vector2(10, 20);
        _cv.DrawString(Fonts.DraftBold, tp + new Vector2(-1, -1), title, HorizontalAlignment.Left, -1, 16, EmbossLight);
        _cv.DrawString(Fonts.DraftBold, tp + new Vector2(1, 1), title, HorizontalAlignment.Left, -1, 16, Ui.Rule.Darkened(0.1f));
        Text(tp, title, 16, Ui.Ink, FontKind.Draft);
        Text(r.Position + new Vector2(r.Size.X - 10 - 120, 20), Loc.T("tree.sheet", _mode == 0 ? "A" : "B"), 14, Ui.Ink2, FontKind.Draft, HorizontalAlignment.Right, 120);
        string who = _actor >= 0 ? Names.Actor(_host.Layout.ActorIds[_actor]) : "";
        Text(r.Position + new Vector2(10, 50), Fit(who, 190, 14), 14, Ui.Ink);
        Text(r.Position + new Vector2(r.Size.X - 10 - 130, 50), _date.ToString(), 14, Ui.Ink, FontKind.Mono, HorizontalAlignment.Right, 130);
    }

    // Route of an edge: radial out of the source, an arc along a channel radius between the two years (just outside the later one when they are close), radial into the target.
    // The returned array comes from a pool by length and is valid until the next call of the same length (every caller uses it at once), so a redraw allocates no route buffers.
    readonly Vector2[][] _chPool = new Vector2[128][];
    Vector2[] Channel(int m, int i)
    {
        float rc = Math.Abs(_rad[i] - _rad[m]) > 0.4f ? (_rad[m] + _rad[i]) * 0.5f : Math.Max(_rad[m], _rad[i]) + 0.12f;
        float a0 = _ang[m], a1 = a0 + Mathf.PosMod(_ang[i] - a0 + 180f, 360f) - 180f;   // the short way round
        int steps = Math.Max(1, (int)(Math.Abs(a1 - a0) / 4f)), n = steps + 3;
        var pts = _chPool[n] ??= new Vector2[n];
        pts[0] = PosOf(m); pts[1] = Polar(rc, _ang[m]);
        for (int k = 1; k <= steps; k++) pts[k + 1] = Polar(rc, a0 + (a1 - a0) * k / steps);
        pts[n - 1] = PosOf(i);
        return pts;
    }

    // Wedge name outside the outer ring: left-aligned on the right side, right-aligned on the left, centred below; trimmed to the free width.
    (Rect2 rect, string text) WedgeLabel(int w)
    {
        float mid = TreeYearLayout.SectorMid(w, _nSec), cs = Mathf.Cos(Mathf.DegToRad(mid)), sn = Mathf.Sin(Mathf.DegToRad(mid));
        var q = Polar(6.65f, mid);
        var da = Area;
        float avail = cs > 0.35f ? da.End.X - q.X - 4 : cs < -0.35f ? q.X - da.Position.X - 4 : 220;
        string s = Loc.T("disc." + w);
        if (Fonts.DraftBold.GetStringSize(s, HorizontalAlignment.Left, -1, 14).X > avail) s = Loc.T("disc." + w + ".s");   // short name before any ellipsis
        s = Fit(s, Math.Max(60, avail), 14, Fonts.DraftBold);
        var sz = Fonts.DraftBold.GetStringSize(s, HorizontalAlignment.Left, -1, 14);
        float x = cs > 0.35f ? q.X : cs < -0.35f ? q.X - sz.X : q.X - sz.X * 0.5f;
        float y = sn > 0.35f ? q.Y + 6 : sn < -0.35f ? q.Y - sz.Y - 2 : q.Y - sz.Y * 0.5f;
        return (new Rect2(x, y, sz), s);
    }

    void DrawPlanisphere()
    {
        _countRects.Clear(); _labelRects.Clear(); _plates.Clear();
        CountCells();
        var c = Centre; float u = Unit;
        DrawSectors(c, u);
        DrawDecades(c, u);
        DrawNowRing(c, u);
        for (int w = 0; w < _nSec; w++)
        {
            var (rc, s) = WedgeLabel(w);
            Text(new Vector2(rc.Position.X, rc.End.Y - 4), s, 14, Ui.Ink2, FontKind.Draft);
            // The wedge summary known/total: under the wedge name, never skipped, on the paper (no plate).
            var ssz = Fonts.Mono.GetStringSize(_sumText[w], HorizontalAlignment.Left, -1, 14);
            _sumRects[w] = new Rect2(rc.GetCenter().X - ssz.X * 0.5f, rc.End.Y + 2, ssz);
            Text(new Vector2(_sumRects[w].Position.X, _sumRects[w].Position.Y + ssz.Y * 0.8f), _sumText[w], 14, Ui.DiscText[w % Ui.DiscText.Length], FontKind.Mono);
        }
        Text(new Vector2(Area.Position.X + 14, Area.Position.Y + 20), _hdrText, 14, Ui.Ink, FontKind.Mono);

        var nodes = _host.Data.Nodes; var rq = nodes.Requires;
        // requires edges (every any_of alternative): quiet first, then selected, then the route on top
        for (int pass = 0; pass < 3; pass++)
            for (int i = 0; i < nodes.Count; i++)
                for (int g = rq.GroupStart[i]; g < rq.GroupStart[i + 1]; g++)
                    foreach (int m in rq.GroupMembers(g))
                    {
                        bool onRoute = _route[i] && _route[m];
                        bool toSel = _selected >= 0 && (i == _selected || m == _selected);
                        if (pass == 0 && !onRoute && !toSel) _cv.DrawPolyline(Channel(m, i), new Color(Ui.Rule, 0.7f), 1f, true);
                        else if (pass == 1 && toSel && !onRoute) _cv.DrawPolyline(Channel(m, i), Ui.Ink, 1.6f, true);
                        else if (pass == 2 && onRoute) _cv.DrawPolyline(Channel(m, i), Ui.Command, 2.2f, true);
                    }
        // the continuous route from the root of «why not» to the goal
        // A5: the route is a wide band with a dashed light centre (a double dashed line); the links of the selected node are a thin solid line, so the two differ in grey too.
        for (int k = 0; k + 1 < _path.Count; k++) _cv.DrawPolyline(Channel(_path[k], _path[k + 1]), Ui.Command, 5f, true);
        for (int k = 0; k + 1 < _path.Count; k++) { var ch = Channel(_path[k], _path[k + 1]); for (int j = 0; j + 1 < ch.Length; j++) _cv.DrawDashedLine(ch[j], ch[j + 1], Ui.Sheet, 1.8f, 5f, true); }

        for (int i = 0; i < nodes.Count; i++) DrawNode(i);
        if (Step >= ZoomSteps.Effects) for (int i = 0; i < nodes.Count; i++) DrawYearAndChips(i);
        DrawLabels(c);
        if (Step >= ZoomSteps.EdgeLabels) DrawEdgeLabels();
        DrawKeyFocus();
    }

    // Sector = discipline: a quiet wash (decor, C5) and a hatch of its own direction that carries the meaning in grey (A5); the lines between sectors start at the roots band.
    static readonly (float X, float Y)[][] HatchNormals = BuildHatchNormals();
    static (float X, float Y)[][] BuildHatchNormals() { var a = new (float X, float Y)[5][]; for (int k = 0; k < 5; k++) a[k] = TreeYearLayout.HatchNormals((SectorHatch)k); return a; }
    void DrawSectors(Vector2 c, float u)
    {
        float alpha = Step == 1 ? 0.5f : Step <= 3 ? 0.3f : 0.18f;
        float w = TreeYearLayout.SectorWidth(_nSec), rIn = (TreeYearLayout.Inner - TreeYearLayout.RootsBand) * u, rOut = TreeYearLayout.Edge * u;
        for (int d = 0; d < _nSec; d++)
        {
            float s0 = TreeYearLayout.SectorStart(d, _nSec), s1 = s0 + w;
            float a0 = Mathf.DegToRad(s0 + w * WedgeMargin * 0.5f), a1 = Mathf.DegToRad(s1 - w * WedgeMargin * 0.5f);
            _cv.DrawArc(c, (rIn + rOut) * 0.5f, a0, a1, TreeYearLayout.ArcPoints(rOut, w), new Color(Ui.DiscWash[d % Ui.DiscWash.Length], alpha), rOut - rIn);
            DrawHatch(c, rIn, rOut, a0, a1, TreeYearLayout.HatchOf(d), new Color(Ui.DiscText[d % Ui.DiscText.Length], TreeYearLayout.HatchAlpha));
            float b = Mathf.DegToRad(s0);
            _cv.DrawLine(c + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * rIn, c + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * (6.5f * u), GridColor, 1f, true);
        }
    }

    // Hatch lines of one direction inside an annular sector, only over the visible map: every line is walked in short steps and the runs inside the sector are drawn.
    void DrawHatch(Vector2 c, float rIn, float rOut, float a0, float a1, SectorHatch kind, Color col)
    {
        var area = Area; float r2in = rIn * rIn, r2out = rOut * rOut, sp = TreeYearLayout.HatchSpacing;
        float d0x = Mathf.Cos(a0), d0y = Mathf.Sin(a0), d1x = Mathf.Cos(a1), d1y = Mathf.Sin(a1);
        foreach (var (nx, ny) in HatchNormals[(int)kind])
        {
            float tx = -ny, ty = nx;
            float dmin = float.MaxValue, dmax = float.MinValue;
            for (int k = 0; k < 4; k++) { float cx = (k & 1) == 0 ? area.Position.X : area.End.X, cy = (k & 2) == 0 ? area.Position.Y : area.End.Y, v = (cx - c.X) * nx + (cy - c.Y) * ny; dmin = Math.Min(dmin, v); dmax = Math.Max(dmax, v); }
            dmin = Math.Max(dmin, -rOut); dmax = Math.Min(dmax, rOut);
            for (float d = Mathf.Ceil(dmin / sp) * sp; d <= dmax; d += sp)
            {
                float lim = Mathf.Sqrt(Math.Max(0f, r2out - d * d));
                bool run = false; Vector2 from = default, last = default;
                for (float s = -lim; s <= lim; s += 5f)
                {
                    float px = nx * d + tx * s, py = ny * d + ty * s;
                    bool inside = false;
                    if (px + c.X >= area.Position.X && px + c.X <= area.End.X && py + c.Y >= area.Position.Y && py + c.Y <= area.End.Y)
                    {
                        float q = px * px + py * py;
                        inside = q >= r2in && q <= r2out && d0x * py - d0y * px >= 0f && px * d1y - py * d1x >= 0f;
                    }
                    var pt = new Vector2(c.X + px, c.Y + py);
                    if (inside) { if (!run) { run = true; from = pt; } last = pt; }
                    else if (run) { _cv.DrawLine(from, last, col, 1f); run = false; }
                }
                if (run) _cv.DrawLine(from, last, col, 1f);
            }
        }
    }

    /// <summary>Colour of the grid (rings, sector lines): decor, at most 1.6:1 on the paper (Gemini, TASK-0149); a unit test holds the number.</summary>
    static Color GridColor => new(Ui.Rule, TreeYearLayout.GridAlpha);

    // Year rings: a circle per decade 1950..2030 (1980 and 2000 heavier), its year on a plate at the top; the 1945 circle bounds the centre, labelled inside.
    static readonly string[] RingText = BuildRingText();
    static string[] BuildRingText() { var a = new string[TreeYearLayout.RingYears.Length]; for (int k = 0; k < a.Length; k++) a[k] = TreeYearLayout.RingYears[k].ToString(); return a; }
    void DrawDecades(Vector2 c, float u)
    {
        _cv.DrawArc(c, TreeYearLayout.Inner * u, 0, Mathf.Tau, TreeYearLayout.ArcPoints(TreeYearLayout.Inner * u, 360f), GridColor, 1f, true);
        _cv.DrawArc(c, (TreeYearLayout.Inner - TreeYearLayout.RootsBand) * u, 0, Mathf.Tau, TreeYearLayout.ArcPoints((TreeYearLayout.Inner - TreeYearLayout.RootsBand) * u, 360f), GridColor, 1f, true);
        YearPlate(c.X, c.Y - TreeYearLayout.Inner * u, "1945");
        for (int k = 0; k < TreeYearLayout.RingYears.Length; k++)
        {
            int ry = TreeYearLayout.RingYears[k]; float r = TreeYearLayout.RadiusOf(ry) * u;
            _cv.DrawArc(c, r, 0, Mathf.Tau, TreeYearLayout.ArcPoints(r, 360f), GridColor, TreeYearLayout.IsLandmark(ry) ? 2f : 1f, true);
            YearPlate(c.X, c.Y - r, RingText[k]);
            if (ry is 1950 or 1980 or 2010)   // also on two other bisectors of the gaps between sectors, where no node ever stands (art director)
                for (int g = 1; g <= 2; g++)
                {
                    float ga = Mathf.DegToRad(TreeYearLayout.SectorStart(g * _nSec / 3 == 0 ? 1 : g * _nSec / 3, _nSec));
                    YearPlate(c.X + Mathf.Cos(ga) * r, c.Y + Mathf.Sin(ga) * r, RingText[k]);
                }
        }
        // the centre: the roots band lies around it, 1900-1945
        if (u * (TreeYearLayout.Inner - TreeYearLayout.RootsBand) > 40f)
        {
            string s1 = Loc.T("tree.centre.head"), s2 = Loc.T("tree.centre");
            var l1 = Fonts.Mono.GetStringSize(s1, HorizontalAlignment.Left, -1, 14); var l2 = Fonts.Text.GetStringSize(s2, HorizontalAlignment.Left, -1, 14);
            Text(new Vector2(c.X - l1.X * 0.5f, c.Y - 2), s1, 14, Ui.Ink2, FontKind.Mono);
            Text(new Vector2(c.X - l2.X * 0.5f, c.Y + 15), s2, 14, Ui.Ink2);
        }
    }

    // A year on a paper plate centred at a point of a ring (drawn before the nodes, so a node always stays above it).
    readonly List<Rect2> _plates = new();
    void YearPlate(float x, float y, string text)
    {
        if (!Area.Grow(20).HasPoint(new Vector2(x, y))) return;
        var sz = Fonts.Mono.GetStringSize(text, HorizontalAlignment.Left, -1, 14);
        var pl = new Rect2(x - sz.X * 0.5f - 3, y - sz.Y * 0.5f - 1, sz.X + 6, sz.Y + 2);
        _plates.Add(pl);
        _cv.DrawRect(pl, new Color(Ui.Sheet, 0.92f));
        Text(new Vector2(pl.Position.X + 3, pl.Position.Y + 1 + sz.Y * 0.8f), text, 14, Ui.Ink2, FontKind.Mono);
    }

    // «Now»: a dashed ring at the radius of the session date and its label on the first free place of the ring (bottom first); what lies outside is not yet open.
    void DrawNowRing(Vector2 c, float u)
    {
        float yf = _date.Year + (_date.Month - 1) / 12f + (_date.Day - 1) / 365f, r = TreeYearLayout.RadiusOf(yf) * u;
        for (int k = 0; k < 120; k++) _cv.DrawArc(c, r, k * Mathf.Tau / 120f, (k + 0.55f) * Mathf.Tau / 120f, 3, Ui.Ink, 1.6f, true);
        _nowRect = default;
        if (_nowYear != _date.Year || _nowText == null) { _nowYear = _date.Year; _nowText = Loc.T("tree.now", _date.Year); }
        var sz = Fonts.Text.GetStringSize(_nowText, HorizontalAlignment.Left, -1, 14) + new Vector2(10, 4);
        // first free place: on the ring, else pulled out of it on a leader (the ring is crowded with nodes at the start); bottom first
        for (int off = 0; off <= 2; off++)
            for (int k = 0; k < 60; k++)
            {
                float deg = 90f + (k % 2 == 0 ? 1 : -1) * ((k + 1) / 2) * 6f, a = Mathf.DegToRad(deg), dist = r + off * 46f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var rc = new Rect2(c + dir * dist - sz * 0.5f, sz);
                bool free = Area.Encloses(rc);
                for (int i = 0; free && i < _rad.Length; i++) if (NodeBox(i).Grow(2).Intersects(rc)) free = false;
                if (!free) continue;
                if (off > 0) _cv.DrawLine(c + dir * r, c + dir * (dist - (sz.Y * 0.5f)), Ui.Ink, 1.2f, true);
                _cv.DrawRect(rc, Ui.Sheet); _cv.DrawRect(rc, Ui.Ink, false, 1f);
                Text(new Vector2(rc.Position.X + 5, rc.Position.Y + 2 + sz.Y * 0.7f), _nowText, 14, Ui.Ink);
                _nowRect = rc;   // node names keep off it
                return;
            }
    }

    // At step 5 a node shows its year to the right of the mark («≈» when the year is estimated) and, under it, its effect chips when the core has them (TASK-0157).
    // «No data» is said in the node panel and the hover tip, not on every node of the map. Node names go above the mark and keep off the year (DrawLabels reads the rectangle).
    Rect2 YearRect(int i) { var p = PosOf(i); return new Rect2(p.X + NR + 4, p.Y - 9, _yearW[i] + 2, 18); }
    void DrawYearAndChips(int i)
    {
        var p = PosOf(i);
        if (!Area.Grow(40).HasPoint(p)) return;
        var yr = YearRect(i);
        _cv.DrawStringOutline(Fonts.Mono, new Vector2(yr.Position.X, p.Y + 5), _yearText[i], HorizontalAlignment.Left, -1, 14, 5, Ui.Sheet);
        Text(new Vector2(yr.Position.X, p.Y + 5), _yearText[i], 14, Ui.Ink2, FontKind.Mono);
        var chips = EffectChips(i);
        float y = p.Y + NR + 16;
        for (int k = 0; k < chips.Count && k < 3; k++) Text(new Vector2(p.X - 40, y + 18 * k), chips[k], 14, Ui.Ink, FontKind.Mono);
    }

    // Hook for the numeric effects of a node («speed +30»): TASK-0157 (NodeEffects in the core data) is not on main yet, so there is nothing to show. The client never invents the numbers (ui.md).
    IReadOnlyList<string> EffectChips(int row) => Array.Empty<string>();

    // Step 6: the edges of the selected node carry the name of the other end («needs: X», «opens: Y»), cached when the selection changes.
    readonly List<(int a, int b, string text)> _edgeLbl = new(); int _edgeKey = -2;
    void BuildEdgeLabels()
    {
        _edgeKey = _selected; _edgeLbl.Clear();
        if (_selected < 0) return;
        var nodes = _host.Data.Nodes; var rq = nodes.Requires;
        foreach (int m in _dep[_selected]) _edgeLbl.Add((_selected, m, Loc.T("edge.opens", Names.Node(nodes.Id[m]))));
        for (int g = rq.GroupStart[_selected]; g < rq.GroupStart[_selected + 1]; g++)
            foreach (int m in rq.GroupMembers(g)) _edgeLbl.Add((m, _selected, Loc.T("edge.needs", Names.Node(nodes.Id[m]))));
    }
    readonly List<Rect2> _edgePlates = new();
    // An edge plate sits on the middle of its edge, or a third of the way along when the middle is taken (three tries at most); when none is free it stays hidden (C1: it never covers a node, a name or a year).
    void DrawEdgeLabels()
    {
        if (_edgeKey != _selected) BuildEdgeLabels();
        _edgePlates.Clear();
        foreach (var (a, b, text) in _edgeLbl)
        {
            var ch = Channel(a, b);
            var sz = Fonts.Text.GetStringSize(text, HorizontalAlignment.Left, -1, 14);
            for (int t = 0; t < 3; t++)
            {
                var at = ch[t == 0 ? ch.Length / 2 : t == 1 ? ch.Length / 3 : ch.Length * 2 / 3];
                var rc = new Rect2(at - new Vector2(sz.X * 0.5f + 3, sz.Y * 0.5f), sz + new Vector2(6, 2));
                if (!Area.Encloses(rc) || PlateTaken(rc)) continue;
                _cv.DrawRect(rc, Ui.Sheet); _cv.DrawRect(rc, Ui.Ink2, false, 1f);
                Text(new Vector2(rc.Position.X + 3, rc.Position.Y + sz.Y * 0.8f), text, 14, Ui.Ink2);
                _edgePlates.Add(rc);
                break;
            }
        }
    }

    bool PlateTaken(Rect2 rc)
    {
        foreach (var l in _labelRects) if (l.Intersects(rc)) return true;
        foreach (var l in _edgePlates) if (l.Intersects(rc)) return true;
        for (int i = 0; i < _rad.Length; i++)
        {
            if (NodeBox(i).Intersects(rc)) return true;
            if (Step >= ZoomSteps.Effects && YearRect(i).Intersects(rc)) return true;
        }
        return false;
    }

    // Node mark size: every state is 16 du (TreeClarity.MarkDu), never scaled down with the canvas (A2); a little larger when zoomed in; icon plates are larger still.
    // NR is the box half-size used for hits, label places and overlap checks; the mark itself is drawn at MarkRadius(type).
    float NR => CurLod switch { Lod.Overview => 10f, Lod.Nodes => 12.5f, _ => GlyphGeometry.IconsPlateMin + Math.Min(6f, (_zoom - PlanisphereMath.IconsFrom) * 2f) };
    static readonly float[] MarkR = BuildMarkR();
    static float[] BuildMarkR() { var r = new float[6]; for (int k = 0; k < 6; k++) r[k] = GlyphGeometry.LegendRadius(LegendData.Types[k].Code); return r; }
    float MarkRadius(NodeType t) => CurLod == Lod.Icons ? NR : MarkR[(int)t] * (CurLod == Lod.Nodes ? 1.25f : 1f);

    // Counts per sector, filled once per draw into fields (no allocation); the sector summaries are the always-visible counts.
    readonly int[] _wTot = new int[TreeYearLayout.MaxSectors], _wKn = new int[TreeYearLayout.MaxSectors], _sumShown = { -1, -1, -1, -1, -1, -1, -1, -1 };
    readonly string[] _sumText = new string[TreeYearLayout.MaxSectors];
    readonly Rect2[] _sumRects = new Rect2[TreeYearLayout.MaxSectors];
    void CountCells()
    {
        var nodes = _host.Data.Nodes; int known = 0;
        Array.Clear(_wTot); Array.Clear(_wKn);
        for (int i = 0; i < nodes.Count; i++)
        {
            int d = _sec[i]; bool kn = _state[i] == St.Known;
            _wTot[d]++;
            if (kn) { _wKn[d]++; known++; }
        }
        for (int w = 0; w < _nSec; w++) if (_sumShown[w] != _wKn[w] * 10000 + _wTot[w]) { _sumShown[w] = _wKn[w] * 10000 + _wTot[w]; _sumText[w] = _wKn[w] + "/" + _wTot[w]; }
        if (_hdrKn != known || _hdrTot != nodes.Count || _hdrText == null) { _hdrKn = known; _hdrTot = nodes.Count; _hdrText = Loc.T("tree.count", nodes.Count, known); }
    }

    readonly List<Rect2> _countRects = new(), _labelRects = new();

    Rect2 NodeBox(int i) { var p = PosOf(i); float h = NR + 1; return new Rect2(p - new Vector2(h, h), new Vector2(h * 2, h * 2)); }

    bool CountFits(Rect2 r)
    {
        for (int i = 0; i < _rad.Length; i++) if (NodeBox(i).Intersects(r)) return false;
        for (int w = 0; w < _nSec; w++) if (WedgeLabel(w).rect.Intersects(r)) return false;
        for (int w = 0; w < _nSec; w++) if (_sumRects[w].Intersects(r)) return false;
        foreach (var o in _countRects) if (o.Intersects(r)) return false;
        return true;
    }

    /// <summary>Shot-run assertion (A9): rectangles of the drawn count tags, node labels and wedge names that touch a node mark or each other. Returns the first offender or null.</summary>
    public string ShotOverlap()
    {
        var boxes = new List<(string what, Rect2 r)>();
        foreach (var r in _countRects) boxes.Add(("count tag", r));
        foreach (var r in _labelRects) boxes.Add(("label", r));
        for (int w = 0; w < _nSec; w++) boxes.Add(("wedge summary", _sumRects[w]));
        for (int w = 0; w < _nSec; w++) boxes.Add(("wedge name", WedgeLabel(w).rect));
        for (int a = 0; a < boxes.Count; a++)
        {
            for (int i = 0; i < _rad.Length; i++) if (NodeBox(i).Intersects(boxes[a].r)) return $"{boxes[a].what} {boxes[a].r} touches node {i}";
            for (int b = a + 1; b < boxes.Count; b++) if (boxes[a].r.Intersects(boxes[b].r)) return $"{boxes[a].what} {boxes[a].r} touches {boxes[b].what} {boxes[b].r}";
        }
        return null;
    }

    void DrawNode(int i)
    {
        var nodes = _host.Data.Nodes;
        var p = PosOf(i); var st = _state[i]; bool route = _route[i];
        int d = (int)nodes.Discipline[i];
        var line = route ? Ui.Command : Ui.DiscText[d];   // A4: the dark text step of the ink holds 3:1 on the paper
        var type = nodes.Type[i];
        float r = MarkRadius(type);
        if (i == _root) RootMark.Draw(_cv, p, Math.Max(TreeClarity.MarkDu, r * 2f), CvBasis);
        else DrawMark(TreeClarity.StateOf(st == St.Known, st == St.Frontier, _nv[i].Process is { Mastered: true }), type, p, line, i);
        if (i == _goal) GoalFrame(type, p, r);
        if (i == _selected) SelectedRing(p, r, i == _goal);
    }

    // The one drawing of a node mark: the planisphere and the legend both come through here (spec 2.1: legend 1:1 with the map).
    void DrawMark(MarkState ms, NodeType type, Vector2 p, Color line, int row, float radius = 0)
    {
        var mk = TreeClarity.Mark(ms); float r = radius > 0 ? radius : MarkRadius(type); bool icons = CurLod == Lod.Icons;
        if (mk.Backing) _cv.DrawCircle(p, r * (TreeClarity.BackingDu / TreeClarity.MarkDu), Ui.Sheet);
        Shape(type, p, r, mk.Filled ? line : icons ? Ui.Sheet : Colors.Transparent, line, mk.Dashed, mk.StrokeDu);
        if (icons && row >= 0) Glyph(row, type, p, r, mk.Filled ? Colors.White : line);
    }

    // The goal: a double frame that follows the node's own shape, in the route colour. The selected node: a double circle in ink. Different shape and colour, so the two stay apart in grey.
    void GoalFrame(NodeType type, Vector2 p, float r)
    {
        Shape(type, p, r + 2.5f, Colors.Transparent, Ui.Command, false, 1.6f);
        Shape(type, p, r + 5f, Colors.Transparent, Ui.Command, false, 1.6f);
    }

    void SelectedRing(Vector2 p, float r, bool alsoGoal = false)
    {
        float o = alsoGoal ? 8f : 0f;   // outside the goal frame when the node is both
        _cv.DrawArc(p, r + 4.5f + o, 0, Mathf.Tau, 32, Ui.Ink, 2f, true);
        _cv.DrawArc(p, r + 2f + o, 0, Mathf.Tau, 32, Ui.Ink, 1.5f, true);
    }

    // One simple glyph per node type, drawn in a plate (Icons level). The primitives come from GlyphPrims (measured by the tests), built once at unit radius and scaled by the canvas transform.
    readonly GlyphSet[] _ngSets = new GlyphSet[6];
    Vector2 CvBasis => _cv == _view ? -Area.Position : Vector2.Zero;   // the transform PaintView set on the clipped view

    // Shot run: where each glyph should land (ink box from the same primitives, in the tree's space) so that the rendered frame can be probed.
    public Rect2 ShotArea => Area;
    public bool ShotProbe;
    public readonly List<(int Row, Rect2 Ink, Rect2 Box)> ShotGlyphs = new();
    public IReadOnlyList<Rect2> ShotLabelRects => _labelRects;
    void Glyph(int row, NodeType t, Vector2 p, float r, Color c)
    {
        int k = (int)t; float rg = r * GlyphGeometry.NodeGlyphShare;
        _ngSets[k] ??= new GlyphSet(GlyphPrims.NodeGlyph(t.ToString(), 1f));
        GlyphDraw.Draw(_cv, _ngSets[k], p, c, rg, CvBasis);
        if (ShotProbe)
        {
            var (x0, y0, x1, y1) = GlyphPrims.Ink(GlyphPrims.NodeGlyph(t.ToString(), rg));
            ShotGlyphs.Add((row, new Rect2(p.X + x0, p.Y + y0, x1 - x0, y1 - y0), NodeBox(row)));
        }
    }

    // Outline of a node type (LegendData.Types): FND triangle, PRC square, APP diamond, ENG hexagon, CMP pentagon; PRN is a circle (null).
    public static Vector2[] Outline(NodeType t, Vector2 p, float r)
    {
        var o = GlyphGeometry.Outline(t.ToString(), r);
        if (o == null) return null;
        var q = new Vector2[o.Length];
        for (int k = 0; k < o.Length; k++) q[k] = p + new Vector2(o[k].X, o[k].Y);
        return q;
    }

    void Shape(NodeType t, Vector2 p, float r, Color fill, Color line, bool dashed, float w)
    {
        var pts = Outline(t, p, r);
        if (pts != null)
        {
            if (fill.A > 0) _cv.DrawColoredPolygon(pts, fill);
            for (int k = 0; k < pts.Length; k++)
            {
                var a = pts[k]; var b = pts[(k + 1) % pts.Length];
                if (dashed) _cv.DrawDashedLine(a, b, line, w, 4f, true); else _cv.DrawLine(a, b, line, w, true);
            }
            return;
        }
        if (fill.A > 0) _cv.DrawCircle(p, r, fill);
        if (!dashed) { _cv.DrawArc(p, r, 0, Mathf.Tau, 24, line, w, true); return; }
        for (int k = 0; k < 8; k++) _cv.DrawArc(p, r, k * Mathf.Tau / 8, k * Mathf.Tau / 8 + Mathf.Tau / 8 * 0.55f, 6, line, w, true);   // dashed outline
    }

    // Labels only for the route, the frontier (by number), the selected node, the goal and the root; each takes the first free place.
    void DrawLabels(Vector2 c)
    {
        var nodes = _host.Data.Nodes;
        var occupied = _lbOccupied; occupied.Clear();
        for (int i = 0; i < nodes.Count; i++) { float hr = i == _selected ? SelKeepOut(i) : NR + 3; occupied.Add(new Rect2(PosOf(i) - new Vector2(hr, hr), new Vector2(hr * 2, hr * 2))); }   // the selected node keeps its ring (and the goal frame) clear
        occupied.Add(new Rect2(c - new Vector2(Unit * 0.7f, Unit * 0.7f), new Vector2(Unit * 1.4f, Unit * 1.4f)));
        for (int w = 0; w < _nSec; w++)   // wedge names
        {
            occupied.Add(WedgeLabel(w).rect.Grow(2)); occupied.Add(_sumRects[w].Grow(6));
        }
        occupied.AddRange(_countRects);   // the count tags of the cells are drawn first; labels keep off them
        if (_nowRect.Size.X > 0f) occupied.Add(_nowRect.Grow(2));
        foreach (var pl in _plates) occupied.Add(pl.Grow(2));   // decade years: names keep off them
        if (Step >= ZoomSteps.Effects) for (int i = 0; i < nodes.Count; i++) if (Area.Grow(40).HasPoint(PosOf(i))) { occupied.Add(YearRect(i).Grow(1)); }
        // The route is an obstacle too, so that no label sits on it.
        for (int k = 0; k + 1 < _path.Count; k++) { var ch = Channel(_path[k], _path[k + 1]); for (int j = 0; j + 1 < ch.Length; j++) AddSegment(occupied, ch[j], ch[j + 1], 3f); }
        if (_labelText == null || _labelText.Length != nodes.Count) _labelText = new string[nodes.Count]; else Array.Clear(_labelText);
        var order = _lbOrder; order.Clear();
        void Add(int i) { if (i >= 0 && !order.Contains(i)) order.Add(i); }
        var byNum = RowsByNumber(nodes.Number);
        Add(_selected); Add(_goal); Add(_root);
        foreach (int i in byNum) if (_route[i] && _state[i] != St.Unavailable) Add(i);
        foreach (int i in byNum) if (_state[i] == St.Frontier) Add(i);
        Add(_hover);
        foreach (int i in byNum) if (_state[i] == St.Frontier) Add(i);   // priority: selected > goal > openable > known
        foreach (int i in byNum) if (_state[i] == St.Known || Step >= ZoomSteps.Icons) Add(i);
        var area = Area;
        var hard = _lbHard; hard.Clear();
        for (int w = 0; w < _nSec; w++) { hard.Add(WedgeLabel(w).rect.Grow(2)); hard.Add(_sumRects[w].Grow(6)); }
        hard.AddRange(_countRects);
        if (_nowRect.Size.X > 0f) hard.Add(_nowRect.Grow(2));
        var placed = _lbPlaced; placed.Clear();
        bool Place(int i, string text, FontKind kind, bool far = false, bool force = false)
        {
            var p = PosOf(i); float o = i == _selected ? SelKeepOut(i) + 3 : NR + 6;
            var sz = Ui.FontOf(kind).GetStringSize(text, HorizontalAlignment.Left, -1, 14);
            // near places first, then farther ones (so that the goal and the selected node always find a free spot)
            var cands = _lbCands;
            cands[0] = new Rect2(p + new Vector2(o, -sz.Y * 0.5f), sz); cands[1] = new Rect2(p + new Vector2(-o - sz.X, -sz.Y * 0.5f), sz);
            cands[2] = new Rect2(p + new Vector2(-sz.X * 0.5f, o), sz); cands[3] = new Rect2(p + new Vector2(-sz.X * 0.5f, -o - sz.Y), sz);
            cands[4] = new Rect2(p + new Vector2(o + 2, o + 2), sz); cands[5] = new Rect2(p + new Vector2(-o - 2 - sz.X, o + 2), sz);
            cands[6] = new Rect2(p + new Vector2(o + 2, -o - 2 - sz.Y), sz); cands[7] = new Rect2(p + new Vector2(-o - 2 - sz.X, -o - 2 - sz.Y), sz);
            if (Step >= ZoomSteps.Effects) (cands[0], cands[3]) = (cands[3], cands[0]);   // name above the mark, the year stands on its right
            bool Try(Rect2 rc, bool ignoreOthers = false, bool leaderCheck = true)
            {
                var pad = rc.Grow(2);
                if (!area.Encloses(pad))   // pushed inside the window edge rather than dropped
                {
                    float nx = Math.Clamp(rc.Position.X, area.Position.X + 4, Math.Max(area.Position.X + 4, area.End.X - rc.Size.X - 4)), ny = Math.Clamp(rc.Position.Y, area.Position.Y + 4, Math.Max(area.Position.Y + 4, area.End.Y - rc.Size.Y - 4));
                    rc = new Rect2(nx, ny, rc.Size); pad = rc.Grow(2);
                }
                if (!area.Encloses(pad) || (!ignoreOthers && occupied.Any(o => o.Intersects(pad)))) return false;
                if (ignoreOthers && hard.Any(h => h.Intersects(pad))) return false;   // a forced plate may sit over nodes, never over a wedge name, a total or a count
                // The leader of a named node must not run across a wedge name, a wedge total or a count tag (C5).
                if (leaderCheck && NamedNode(i) && LeaderCrosses(p, pad, hard)) return false;
                occupied.Add(pad);
                placed[i] = (pad, rc, text, kind, sz.Y);
                return true;
            }
            foreach (var rc in cands) if (Try(rc)) return true;
            if (!far) return false;
            // The goal, the selected node and the root must be named: farther places on three rings (computed in place); the leader line ties the name to the node.
            bool Far(bool ignore, bool leader)
            {
                foreach (float ring in FarRings)
                    for (int k = 0; k < 8; k++)
                    {
                        var dv = new Vector2(Mathf.Cos(k * Mathf.Tau / 8), Mathf.Sin(k * Mathf.Tau / 8)) * (o + ring);
                        if (Try(new Rect2(p + dv - new Vector2(dv.X < -1 ? sz.X : dv.X > 1 ? 0 : sz.X * 0.5f, dv.Y < -1 ? sz.Y : dv.Y > 1 ? 0 : sz.Y * 0.5f), sz), ignore, leader)) return true;
                    }
                return false;
            }
            if (Far(false, true)) return true;
            // The callout node must be named at any price (a leader that ends in nothing is a defect): the nearest place inside the area, over other marks if it has to be.
            if (force)
            {
                // Same avoidance as above, in widening steps: over nodes allowed, over wedge names/totals/counts never; the leader may cross them only as the very last resort.
                foreach (var rc in cands) if (Try(rc, true)) return true;
                if (Far(true, true)) return true;
                foreach (var rc in cands) if (Try(rc, true, false)) return true;
                if (Far(true, false)) return true;
            }
            return false;
        }
        // Pass 0: the goal, the selected node and the root are named first, so that every lower-priority label yields to their plates (spec 2.5; TASK-0136 review).
        int calloutNode = _selected >= 0 ? _selected : _goal;
        foreach (int i in order) if (NamedNode(i)) Place(i, Names.Node(nodes.Id[i]), FontKind.Text, true, i == calloutNode);
        // Names where there is room; a node without room stays a bare marker (no «№N» tags on the map, #482).
        foreach (int i in order)
        {
            if (NamedNode(i) && placed.ContainsKey(i)) continue;
            if (!(_route[i] || i == _selected || i == _goal || i == _root || i == _hover || (Step >= ZoomSteps.Names && (_state[i] != St.Unavailable || Step >= ZoomSteps.Icons)) || (CurLod == Lod.Nodes && _state[i] == St.Frontier))) continue;   // the «nodes» level names the frontier
            Place(i, Names.Node(nodes.Id[i]), FontKind.Text, NamedNode(i));
        }
        // Drawn from the lowest priority to the highest, so that a named node's plate is always on top.
        for (int rank = 3; rank >= 0; rank--)
        foreach (int i in order)
        {
            if (LabelGeometry.Rank(NamedNode(i), _route[i], _state[i] == St.Frontier) != rank) continue;
            if (!placed.TryGetValue(i, out var pl)) continue;
            _labelRects.Add(pl.pad);
            _labelText[i] = pl.text;
            bool callout = i == calloutNode;
            if (callout || LabelNeedsLeader(i, pl.rc))
            {
                var own = PosOf(i); var near = own.Clamp(pl.pad.Position, pl.pad.End); var dir = (near - own).Normalized();
                _cv.DrawLine(own + dir * NR, near, _route[i] ? Ui.Command : Ui.Ink2, 1.5f, true);   // ends at the plate: the goal or selected node, or a node with a neighbour as near as it
            }
            _cv.DrawRect(pl.pad, Ui.Sheet);   // opaque plate (spec 2.3)
            if (callout) _cv.DrawRect(pl.pad, Ui.Command, false, 1f);
            var at = new Vector2(pl.rc.Position.X, pl.rc.Position.Y + pl.h * 0.8f);
            _cv.DrawStringOutline(Ui.FontOf(pl.kind), at, pl.text, HorizontalAlignment.Left, -1, 14, 5, Ui.Sheet);   // sheet halo keeps text off rings and lines
            Text(at, pl.text, 14, _route[i] ? Ui.Command : Ui.Ink2, pl.kind);
        }
    }

    // True when the straight leader from the node to the nearest point of the plate passes through one of the rectangles.
    static bool LeaderCrosses(Vector2 own, Rect2 pad, List<Rect2> rects)
    {
        var near = own.Clamp(pad.Position, pad.End);
        foreach (var r in rects) if (LabelGeometry.SegmentHitsRect(own.X, own.Y, near.X, near.Y, r.Position.X, r.Position.Y, r.Size.X, r.Size.Y)) return true;
        return false;
    }

    static readonly float[] FarRings = { 28, 60, 100 };
    // Per-frame scratch of DrawLabels (reused, so a redraw allocates no lists).
    readonly List<Rect2> _lbOccupied = new(), _lbHard = new(); readonly List<int> _lbOrder = new();
    readonly Dictionary<int, (Rect2 pad, Rect2 rc, string text, FontKind kind, float h)> _lbPlaced = new();
    readonly Rect2[] _lbCands = new Rect2[8];
    int[] _byNumber;
    int[] RowsByNumber(System.Collections.Immutable.ImmutableArray<int> num)
    {
        int n = num.Length;
        if (_byNumber == null || _byNumber.Length != n) { _byNumber = new int[n]; for (int i = 0; i < n; i++) _byNumber[i] = i; Array.Sort(_byNumber, (a, b) => num[a].CompareTo(num[b])); }
        return _byNumber;
    }
    /// <summary>Half-size of the box around the selected node that labels keep clear: outside its ring, and outside the goal frame when it is the goal as well (ring r + 12.5).</summary>
    float SelKeepOut(int i) => Math.Max(NR + 3, Reach(i) + 6);
    /// <summary>How far the drawing of a node reaches from its centre: the real corners of its outline, its goal frame and its selection ring (not just the mark radius).</summary>
    float Reach(int i)
    {
        var t = _host.Data.Nodes.Type[i]; float r = MarkRadius(t), e = GlyphGeometry.Extent(t.ToString(), r);
        if (i == _goal) e = Math.Max(e, GlyphGeometry.Extent(t.ToString(), r + 5f));
        if (i == _selected) e = Math.Max(e, r + 4.5f + (i == _goal ? 8f : 0f) + 1f);
        return e;
    }
    bool NamedNode(int i) => i == _selected || i == _goal || i == _root;

    bool LabelNeedsLeader(int i, Rect2 rc)
    {
        var nodes = _host.Data.Nodes; var own = PosOf(i);
        float d0 = LabelGeometry.DistToRect(own.X, own.Y, rc.Position.X, rc.Position.Y, rc.Size.X, rc.Size.Y);   // same core as LabelGeometry.NeedsLeader
        if (NamedNode(i) && LabelGeometry.IsApart(d0, NR)) return true;   // a far place of the goal, selected or root node
        for (int j = 0; j < nodes.Count; j++)
        {
            if (j == i) continue;
            var q = PosOf(j);
            if (LabelGeometry.OtherIsAsNear(d0, q.X, q.Y, rc.Position.X, rc.Position.Y, rc.Size.X, rc.Size.Y)) return true;
        }
        return false;
    }

    static void AddSegment(List<Rect2> into, Vector2 a, Vector2 b, float pad)
    {
        int n = Math.Max(1, (int)(a.DistanceTo(b) / 8f));
        for (int k = 0; k <= n; k++) { var q = a.Lerp(b, (float)k / n); into.Add(new Rect2(q - new Vector2(pad, pad), new Vector2(pad * 2, pad * 2))); }
    }

    string[] _labelText;
    public string ShotLabelText(int row) => _labelText != null && row >= 0 && row < _labelText.Length ? _labelText[row] : null;
    public int RowOfId(string id) { var n = _host.Data.Nodes; for (int i = 0; i < n.Count; i++) if (n.Id[i] == id) return i; return -1; }

    // Legend (spec §8): a collapsed button at the lower left (key L), opened to a compact two-column sheet docked in the right column above the stamp.
    bool _legendOpen;
    /// <summary>The legend is on screen only in the chart view (mode 0); in the goal statement the flag is kept but the sheet reserves no space and takes no input.</summary>
    public bool LegendOpen => _legendOpen && _mode == 0;
    public bool CanLegend => _mode == 0;
    public void ToggleLegend(bool open) { _legendOpen = open; ClampPan(); Redraw(); }
    const float LegendRow = 22, LegendPad = 12, LegendGap = 16, LegendW = 560, LegendH = 368, StampH = 62, StampGap = 8;
    /// <summary>The open legend sheet: right of the disc area, 8 du above the sheet stamp (spec §3).</summary>
    public Rect2 LegendSheet { get { float x = Size.X * 0.6f + 8; return new Rect2(x, Size.Y - 6 - StampH - StampGap - LegendH, Math.Min(LegendW, Size.X - x - 6), LegendH); } }
    Rect2 StampRect => new(Size.X - 6 - 360, Size.Y - 6 - StampH, 360, StampH);

    // Cached legend content: localized texts (rebuilt when the language changes), colours and closed node outlines (built once).
    bool _lgDirty = true;
    // Texts: 0 rings head, 1 states head, 2 disciplines head, 3 lines head, 4 route, 5 root, 6 why the root, 7 close; 8-12 disciplines; 13-18 ring names; 19-23 states, 24 goal, 25 links of the selected node, 26 the centre dial.
    readonly string[] _lgText = new string[28];
    readonly LegendData.Discipline[] _lgDiscs = LegendData.Disciplines(Palette.C);
    Color[] _lgWash, _lgLine;
    bool _lgBuilt;

    void BuildLegendCache()
    {
        _lgDirty = false;
        string[] keys = { "legend.rings", "legend.states", "legend.disc", "legend.lines", "legend.route", "legend.root", "legend.root.why", "legend.close" };
        for (int k = 0; k < keys.Length; k++) _lgText[k] = Loc.T(keys[k]);
        for (int k = 0; k < 5; k++) _lgText[8 + k] = Loc.T(_lgDiscs[k].NameKey);
        for (int k = 0; k < 6; k++) _lgText[13 + k] = Loc.T(LegendData.Types[k].NameKey);
        for (int k = 0; k < 5; k++) _lgText[19 + k] = Loc.T(TreeClarity.LegendKey(TreeClarity.LegendStates[k]));
        _lgText[24] = Loc.T("legend.goal"); _lgText[25] = Loc.T("legend.links"); _lgText[26] = Loc.T("legend.dial"); _lgText[27] = Loc.T("legend.now");
        if (_lgBuilt) return;
        _lgBuilt = true;
        _lgWash = new Color[5]; _lgLine = new Color[5];
        for (int k = 0; k < 5; k++) { _lgWash[k] = new Color(_lgDiscs[k].WashHex); _lgLine[k] = new Color(_lgDiscs[k].LineHex); }
    }

    // The same hatch as the sector on the map (same directions, 4 du apart), clipped to the round swatch of radius 8.
    void HatchSwatch(Vector2 mk, int sector, Color line)
    {
        foreach (var (nx, ny) in HatchNormals[(int)TreeYearLayout.HatchOf(sector)])
            for (float d = -6f; d <= 6f; d += 4f) { float h = Mathf.Sqrt(64f - d * d); var m = mk + new Vector2(nx, ny) * d; var t = new Vector2(-ny, nx) * h; _cv.DrawLine(m - t, m + t, line, 1.4f); }
    }

    // Legend (spec 2.2): rings, states, disciplines, lines. The marks go through the same drawing as the nodes of the map.
    void DrawLegend()
    {
        if (!LegendOpen) return;   // closed: the real button _legendBtn stands in its place
        if (_lgDirty || !_lgBuilt) BuildLegendCache();
        var r = LegendSheet;
        _cv.DrawStyleBox(DepthBox.Plain, r);
        float colW = (r.Size.X - 2 * LegendPad - LegendGap - DepthBox.Edge) / 2;
        float xl = r.Position.X + LegendPad, xr = xl + colW + LegendGap;
        float Y(int row, float gap = 0) => r.Position.Y + LegendPad + 15 + row * LegendRow + gap;
        void Head(float x, float y, int t) => Text(new Vector2(x, y), _lgText[t], 14, Ui.Ink2, FontKind.Draft);
        // left: rings, disciplines
        Head(xl, Y(0), 0);
        for (int k = 0; k < 6; k++)
        {
            float y = Y(1 + k); var mk = new Vector2(xl + 14, y - 5);
            var tp = LegendData.Types[k].Code; float rr = GlyphGeometry.LegendRadius(tp);
            Shape((NodeType)k, mk, rr, Colors.Transparent, Ui.Ink2, false, 2f);
            Glyph(-1, (NodeType)k, mk, rr, Ui.Ink2);   // the icon the map shows inside this shape from step 4 (the hourglass of a process included)
            Text(new Vector2(xl + 36, y), _lgText[13 + k], 14, Ui.Ink2);
        }
        Head(xl, Y(7, 8), 2);
        for (int k = 0; k < 5; k++)
        {
            float y = Y(8 + k, 8); var mk = new Vector2(xl + 14, y - 5);
            _cv.DrawCircle(mk, 8, _lgWash[k]); HatchSwatch(mk, k, _lgLine[k]); _cv.DrawArc(mk, 8, 0, Mathf.Tau, 24, _lgLine[k], 1.8f, true);
            Text(new Vector2(xl + 36, y), _lgText[8 + k], 14, Ui.Ink2);
        }
        Text(new Vector2(xl, Y(13, 8)), _lgText[26], 14, Ui.Ink2);   // distance from the centre = year, ring = decade
        Text(new Vector2(xl, Y(14, 8)), _lgText[27], 14, Ui.Ink2);
        // right: states, lines
        Head(xr, Y(0), 1);
        for (int k = 0; k < 5; k++)
        {
            var ms = TreeClarity.LegendStates[k]; float y = Y(1 + k); var mk = new Vector2(xr + 14, y - 5);
            var line = Ui.Ink2;
            DrawMark(ms == MarkState.Selected ? MarkState.Known : ms, NodeType.ENG, mk, line, -1, MarkR[(int)NodeType.ENG]);
            if (ms == MarkState.Selected) SelectedRing(mk, MarkR[(int)NodeType.ENG]);
            Text(new Vector2(xr + 36, y), _lgText[19 + k], 14, Ui.Ink2);
        }
        // the goal: the same blue frame the map draws around it (DrawNode)
        float gy = Y(6, 10); var gm = new Vector2(xr + 14, gy - 5);
        DrawMark(MarkState.Known, NodeType.ENG, gm, Ui.Ink2, -1, MarkR[(int)NodeType.ENG]);
        GoalFrame(NodeType.ENG, gm, MarkR[(int)NodeType.ENG]);
        Text(new Vector2(xr + 36, gy), _lgText[24], 14, Ui.Ink2);
        Head(xr, Y(7, 16), 3);
        float ry = Y(8, 16);
        _cv.DrawLine(new Vector2(xr + 2, ry - 5), new Vector2(xr + 26, ry - 5), Ui.Command, 5f); _cv.DrawDashedLine(new Vector2(xr + 2, ry - 5), new Vector2(xr + 26, ry - 5), Ui.Sheet, 1.8f, 5f);
        Text(new Vector2(xr + 36, ry), _lgText[4], 14, Ui.Ink2);
        ry = Y(9, 16);
        _cv.DrawLine(new Vector2(xr + 2, ry - 5), new Vector2(xr + 26, ry - 5), Ui.Ink, 1.6f);   // the links of the selected node, as the map draws them
        Text(new Vector2(xr + 36, ry), _lgText[25], 14, Ui.Ink2);
        ry = Y(10, 16);
        RootMark.Draw(_cv, new Vector2(xr + 14, ry - 5), TreeClarity.MarkDu, CvBasis);
        Text(new Vector2(xr + 36, ry), _lgText[5], 14, Ui.Ink2);
        Para(new Vector2(xr + 36, ry + 19), _lgText[6], colW - 36, 14, Ui.Ink2);
        Text(new Vector2(xr, Y(13, 14)), _lgText[7], 14, Ui.Ink2);
    }

    /// <summary>For the shot run: the legend sheet touches neither the stamp nor the disc area (A9); empty when it is fine.</summary>
    public string ShotLegendFit()
    {
        var r = LegendSheet;
        if (r.Intersects(StampRect)) return "sheet touches the stamp";
        if (r.Intersects(Area)) return "sheet touches the disc area";
        if (r.Position.X < 0 || r.End.X > Size.X || r.Position.Y < 0 || r.End.Y > Size.Y) return "sheet leaves the tree";
        var p = PanelRect;
        if (_insetBox.Visible && (p.Intersects(r) || r.Position.Y - p.End.Y < 8 - 0.01f)) return $"goal panel {p} is not 8 du above the sheet {r}";
        return null;
    }

    Rect2 PanelRect => new(_insetBox.Position, _insetBox.Size);
    /// <summary>Shot run: the goal panel scrolls inside (content taller than the panel), as the spec asks with the legend open.</summary>
    public bool ShotPanelScrolls => _insetScroll.GetVScrollBar().MaxValue - _insetScroll.GetVScrollBar().Page > 0.5;
    /// <summary>Shot run: the goal statement draws no ellipsized string and no Label of the goal panel is clipped; null when clean.</summary>
    public string ShotClippedText()
    {
        var bad = new List<string>();
        bad.AddRange(_paraBad);
        if (_fitCuts > 0) bad.Add($"{_fitCuts} ellipsized string(s) in the statement");
        if (_mode == 1)
        {
            // every line drawn on the canvas, by its rendered bounds, stays inside the statement area
            var a = Area;
            for (int i = 0; i < _drawn.Count; i++)
            {
                var r = _drawn[i];
                if (r.Position.X < a.Position.X - 0.5f || r.End.X > a.End.X + 0.5f || r.Position.Y < a.Position.Y - 0.5f || r.End.Y > a.End.Y + 0.5f) bad.Add($"line '{_drawnText[i]}' {r} leaves the area {a}");
            }
            if (_drawn.Count == 0) bad.Add("no statement line was recorded");
        }
        void Walk(Node n)
        {
            if (n is CanvasItem { Visible: false }) return;
            if (n is Label l && l.Text.Length > 0)
            {
                float w = l.GetThemeFont("font").GetStringSize(l.Text, HorizontalAlignment.Left, -1, l.GetThemeFontSize("font_size")).X;
                bool trims = l.TextOverrunBehavior != TextServer.OverrunBehavior.NoTrimming;
                if (l.GetVisibleLineCount() < l.GetLineCount() || ((trims || l.ClipText) && l.AutowrapMode == TextServer.AutowrapMode.Off && w > l.Size.X + 0.5f)) bad.Add($"label '{l.Text}' ({w:F0} of {l.Size.X:F0} du)");
            }
            foreach (var c in n.GetChildren()) Walk(c);
        }
        Walk(_insetBox);
        return bad.Count == 0 ? null : string.Join("; ", bad);
    }
    /// <summary>Shot run, on the live session state (TASK-0136): the goal panel holds no developer vocabulary and no priorities control, the "why not" root carries its mark, and the goal or selected node has its name plate. Null when clean.</summary>
    public string ShotClarityCheck()
    {
        if (_state == null) return "the tree has no model";
        var bad = new List<string>(); bool rootIcon = false;
        void Walk(Node n)
        {
            if (n is Segment) bad.Add("a priorities-style segment control is in the goal panel");
            if (n is RootMarkIcon) rootIcon = true;
            string txt = n is Label l ? l.Text : n is RichTextLabel r ? r.GetParsedText() : null;
            if (!string.IsNullOrEmpty(txt)) { if (Loc.Has(txt)) txt = Loc.T(txt); if (TreeClarity.Forbidden.IsMatch(txt)) bad.Add($"developer vocabulary in '{txt}'"); }
            foreach (var c in n.GetChildren()) Walk(c);
        }
        Walk(_insetBox);
        if (_root >= 0 && _insetBox.Visible && _whyShown && !rootIcon) bad.Add("the why-not root has no mark in the panel");
        // The inspector scrolls when its content is taller than the room, and then its last card can be reached (a broken scroll hid cards while the frame looked fine).
        float room = _insetScroll.Size.Y, content = _insetPad.GetCombinedMinimumSize().Y;
        var vb = _insetScroll.GetVScrollBar(); double range = vb.MaxValue - vb.Page;
        if (content > room + 1f)
        {
            if (range <= 0.5) bad.Add($"inspector content {content:F0} du does not fit {room:F0} du but the scroll range is {range:F1}");
            else
            {
                int before = _insetScroll.ScrollVertical; _insetScroll.ScrollVertical = int.MaxValue;
                if (_insetScroll.ScrollVertical + room < content - 2f) bad.Add($"inspector scrolled to the end shows {_insetScroll.ScrollVertical + room:F0} of {content:F0} du: the last card is out of reach");
                _insetScroll.ScrollVertical = before;
            }
        }
        float minH = float.MaxValue;   // A10: every button of the sub-panel is at least 32 du high as rendered
        foreach (var top in GetChildren()) if (top is HBoxContainer h)
            foreach (var b in h.GetChildren())
            {
                if (b is Segment sg) foreach (var cell in sg.GetChildren()) minH = Math.Min(minH, ((Control)cell).Size.Y);
                else if (b is Button bt) minH = Math.Min(minH, bt.Size.Y);
            }
        if (minH < SubpanelButtonDu - 0.5f) bad.Add($"a sub-panel button is {minH:F1} du high, below {SubpanelButtonDu}");
        int co = _selected >= 0 ? _selected : _goal;
        if (co >= 0 && ShotLabelText(co) == null) bad.Add($"node {co} (goal or selected) has no name plate, its leader would end in nothing");
        return bad.Count == 0 ? null : string.Join("; ", bad);
    }
    /// <summary>Shot run, SUN-like capture (TASK-0136): the frame must carry the evidence the grammar needs: a "why not" root (and its mark in the panel), unknown nodes and frontier nodes. Null when all are present.</summary>
    public string ShotEvidenceCheck()
    {
        if (_state == null) return "the tree has no model";
        var bad = new List<string>();
        if (_root < 0) bad.Add("no why-not root in this state");
        else if (!_whyShown) bad.Add("the why-not list is collapsed");
        bool rootIcon = false;
        void Walk(Node n) { if (n is RootMarkIcon) rootIcon = true; foreach (var c in n.GetChildren()) Walk(c); }
        Walk(_insetBox);
        if (_root >= 0 && !rootIcon) bad.Add("the root has no mark in the panel");
        bool unk = false, fr = false;   // counted in the visible area, not in the model
        for (int i = 0; i < _state.Length; i++) { if (!Area.HasPoint(PosOf(i))) continue; unk |= _state[i] == St.Unavailable; fr |= _state[i] == St.Frontier; }
        if (!unk) bad.Add("no unknown node in view");
        if (!fr) bad.Add("no frontier node in view");
        return bad.Count == 0 ? null : string.Join("; ", bad);
    }
    public string ShotPanelInfo => $"panel {PanelRect}, scroll max {_insetScroll.GetVScrollBar().MaxValue:F0} page {_insetScroll.GetVScrollBar().Page:F0}";
    /// <summary>Shot run: centre of the closed-legend button in canvas units (a real click; the view must not pan).</summary>
    public Vector2 ShotButtonCenter => _legendBtn.GlobalPosition + _legendBtn.Size / 2f;
    public Vector2 ShotPan => _pan;
    public void ShotPanBy(Vector2 d) { _pan += d; ClampPan(); Redraw(); }

    /// <summary>Shot run, spec 5.1 rule (a): the totals belong to the chart and move with the wedge label. At the base view (baseView) all five lie fully inside the visible chart area and
    /// touch no node, label, other total, legend sheet, goal panel or stamp. In a zoomed or panned view only the totals that are still inside the view are checked, and only against nodes, labels and each other.
    /// Returns the first offender or null; inView receives the number of totals inside the chart area.</summary>
    public string ShotSummaryCheck(bool baseView, out int inView)
    {
        inView = 0;
        var others = new List<(string, Rect2)> { ("stamp", StampRect) };
        if (LegendOpen) others.Add(("legend sheet", LegendSheet));
        if (_insetBox.Visible) others.Add(("goal panel", PanelRect));
        for (int w = 0; w < _nSec; w++)
        {
            var r = _sumRects[w];
            if (_sumText[w] == null || r.Size.X <= 0) return $"summary {w} not drawn";
            bool inside = Area.Encloses(r) && Area.Encloses(WedgeLabel(w).rect);
            if (inside) inView++;
            else if (baseView) return $"summary {w} {r} is not fully inside the chart area {Area}";
            else continue;
            if (baseView) foreach (var (n, o) in others) if (o.Intersects(r) || o.Intersects(WedgeLabel(w).rect)) return $"summary {w} {r} touches {n} {o}";
            for (int v = 0; v < _nSec; v++) { if (v != w && _sumRects[v].Intersects(r)) return $"summary {w} touches summary {v}"; if (WedgeLabel(v).rect.Intersects(r)) return $"summary {w} touches wedge name {v}"; }
            for (int i = 0; i < _rad.Length; i++) if (NodeBox(i).Intersects(r)) return $"summary {w} touches node {i}";
            foreach (var l in _labelRects) if (l.Intersects(r)) return $"summary {w} touches a node label";
        }
        if (_legendBtn.Visible)
        {
            var b = new Rect2(_legendBtn.Position, _legendBtn.Size);
            if (b.Intersects(StampRect)) return "legend button touches the stamp";
            for (int w = 0; w < _nSec; w++) if (b.Intersects(_sumRects[w]) || b.Intersects(WedgeLabel(w).rect)) return $"legend button touches wedge {w}";
            if (_legendBtn.Modulate.A < 1f || _legendBtn.GetIndex() != _view.GetIndex() + 1) return "legend button is not the node right after the canvas, or not opaque";
        }
        return null;
    }

    // ---- B: statement of the goal ----
    void DrawStatement()
    {
        var nodes = _host.Data.Nodes; var rq = nodes.Requires;
        _rowRects.Clear(); _fitCuts = 0; _paraBad.Clear(); _drawn.Clear(); _drawnText.Clear(); _recording = ShotRecord;
        var a = Area; float x0 = a.Position.X + 14, y = a.Position.Y + 8;
        Text(new Vector2(x0, y + 16), Loc.T("stmt.title"), 18, Ui.Ink, FontKind.Draft);
        string goal = _goal >= 0 ? Loc.T("stmt.goal", Names.Node(nodes.Id[_goal]), Loc.T(_state[_goal] == St.Known ? "state.known" : "state.unknown")) : Loc.T("stmt.nogoal");
        float gh = Para(new Vector2(x0 + 210, y + 16), goal, a.Size.X - 240, 14, Ui.Ink);
        y += Math.Max(28, gh + 10);
        float sy = Math.Clamp(a.Size.Y / 560f, 1f, 1.6f);   // rows breathe when the window is tall
        float nameW = 340, cellW = 56, rowH = 24 * sy;
        float gx = x0 + nameW + 8;
        var rows = new List<(int row, string group)>();
        if (_goal >= 0) rows.Add((_goal, "stmt.g.goal"));
        foreach (var i in Enumerable.Range(0, nodes.Count).Where(i => _route[i] && i != _goal && _state[i] == St.Frontier).OrderBy(i => nodes.Number[i])) rows.Add((i, "stmt.g.front"));
        foreach (var i in Enumerable.Range(0, nodes.Count).Where(i => _route[i] && i != _goal && _state[i] == St.Unavailable).OrderBy(i => _depth[i]).ThenBy(i => nodes.Number[i])) rows.Add((i, "stmt.g.wait"));
        foreach (var i in Enumerable.Range(0, nodes.Count).Where(i => !_route[i] && _state[i] == St.Frontier).OrderBy(i => nodes.Number[i]).Take(4)) rows.Add((i, "stmt.g.off"));
        int goalD = rows.Where(r => r.row != _goal).Select(r => _depth[r.row]).DefaultIfEmpty(-1).Max() + 1;
        if (_goal >= 0) _depth[_goal] = goalD;
        int maxD = rows.Count == 0 ? 0 : rows.Max(r => _depth[r.row]);
        y += Para(new Vector2(x0, y + 12), Loc.T("stmt.scale"), a.Size.X - 28, 14, Ui.Ink2) + 4;
        bool capsDrawn = false;   // the column captions ("now", "+1"...) stand in the row of the first group header, over the cells they label
        var yOf = new Dictionary<int, float>();
        string last = "";
        // longest chain: from the root of «why not», follow the unknown requirement with the largest depth
        var chain = new HashSet<int>();
        for (int cur = _root; cur >= 0;)
        {
            chain.Add(cur); int nxt = -1, bd = -1;
            for (int g = rq.GroupStart[cur]; g < rq.GroupStart[cur + 1]; g++)
                foreach (int m in rq.GroupMembers(g)) if (_state[m] != St.Known && _depth[m] > bd) { bd = _depth[m]; nxt = m; }
            cur = nxt;
        }
        foreach (var (row, group) in rows)
        {
            if (group != last)
            {
                y += 4; Text(new Vector2(x0, y + 14), Loc.T(group), 14, Ui.Ink2, FontKind.Draft);
                if (!capsDrawn) { for (int d = 0; d <= maxD; d++) Text(new Vector2(gx + d * cellW + 6, y + 14), d == 0 ? Loc.T("stmt.now") : "+" + d, 14, Ui.Ink2, FontKind.Mono); capsDrawn = true; }
                y += 20; last = group;
            }
            yOf[row] = y;
            var rr = new Rect2(x0 - 4, y - 1, gx - x0 + (maxD + 1) * cellW + 8, rowH);
            _rowRects.Add((rr, row));
            if (row == _selected) _cv.DrawRect(rr, Ui.Hover);
            var col = _route[row] ? Ui.Command : Ui.Ink;
            float ty = y + rowH * 0.5f + 5;
            Text(new Vector2(x0, ty), Fit(Names.Node(nodes.Id[row]), nameW - 6, 14), 14, col);
            float bx = gx + _depth[row] * cellW;
            var rect = new Rect2(bx, y + 4 * sy, cellW - 12, rowH - 10 * sy);
            if (_state[row] == St.Frontier) { _cv.DrawRect(rect, new Color(col, 0.18f)); _cv.DrawRect(rect, col, false, 1.4f); }
            else DrawDashedRect(rect, col);
            y += rowH;
        }
        // precedence links: from the predecessor bar end to the successor row start
        foreach (var (row, _) in rows)
            for (int g = rq.GroupStart[row]; g < rq.GroupStart[row + 1]; g++)
                foreach (int m in rq.GroupMembers(g))
                    if (yOf.TryGetValue(m, out float my) && yOf.TryGetValue(row, out float ry))
                    {
                        var p1 = new Vector2(gx + _depth[m] * cellW + cellW - 12, my + rowH * 0.5f); var p2 = new Vector2(gx + _depth[row] * cellW, ry + rowH * 0.5f);
                        bool main = chain.Contains(m) && chain.Contains(row);
                        _cv.DrawPolyline(new[] { p1, new Vector2(p2.X - 5, p1.Y), new Vector2(p2.X - 5, p2.Y), p2 }, main ? Ui.Command : Ui.Rule, main ? 3f : 1f, true);
                    }
        y += 10;
        Text(new Vector2(x0, y + 12), Loc.T("stmt.chain"), 14, Ui.Command);
        y += 30;
        // overview: discipline x generation cells, one square per node
        Text(new Vector2(x0, y + 12), Loc.T("stmt.overview", nodes.Count), 14, Ui.Ink2, FontKind.Draft);
        y += 34;
        for (int g = 0; g <= 4; g++) Text(new Vector2(x0 + 200 + g * 108 + 4, y), g.ToString(), 14, Ui.Ink2, FontKind.Mono);
        y += 4;
        for (int d = 0; d < 5; d++)
        {
            Text(new Vector2(x0, y + 17 * sy + d * 24 * sy), Loc.T("disc." + d), 14, Ui.Ink2);
            for (int g = 0; g <= 4; g++)
            {
                float cx = x0 + 200 + g * 108;
                _cv.DrawRect(new Rect2(cx, y + d * 24 * sy, 102, 22 * sy), Ui.Rule, false, 1f);
                int k = 0;
                for (int i = 0; i < nodes.Count; i++)
                    if ((int)nodes.Discipline[i] == d && nodes.Gen[i] == g)
                    {
                        var sq = new Rect2(cx + 4 + k * 12, y + d * 24 * sy + 5 * sy, 9, 12);
                        if (_state[i] == St.Known) _cv.DrawRect(sq, Ui.Ink); else if (_state[i] == St.Frontier) _cv.DrawRect(sq, Ui.Ink, false, 1.4f); else _cv.DrawRect(new Rect2(sq.Position + new Vector2(3, 5), new Vector2(3, 3)), Ui.Ink3);
                        k++;
                    }
            }
        }
        y += 5 * 24 * sy + 8;
        float lh = Para(new Vector2(x0, y + 12), Loc.T("legend.b"), a.Size.X - 28, 14, Ui.Ink2);
        Para(new Vector2(x0, y + 12 + lh + 4), Loc.T("legend.b2"), a.Size.X - 28, 14, Ui.Ink2);
        _recording = false;
    }

    void DrawDashedRect(Rect2 r, Color c)
    {
        _cv.DrawDashedLine(r.Position, r.Position + new Vector2(r.Size.X, 0), c, 1.4f, 3f);
        _cv.DrawDashedLine(r.Position + new Vector2(0, r.Size.Y), r.End, c, 1.4f, 3f);
        _cv.DrawDashedLine(r.Position, r.Position + new Vector2(0, r.Size.Y), c, 1.4f, 3f);
        _cv.DrawDashedLine(r.Position + new Vector2(r.Size.X, 0), r.End, c, 1.4f, 3f);
    }

    string StateWord(int row) => Loc.T(_state[row] switch
    {
        St.Known => _nv[row].ByLicence ? "state.licence" : "state.known",
        St.Frontier => "state.frontier",
        _ => "state.unavail",
    });

    // ---------------- input ----------------
    int HitAt(Vector2 pos, float radius)
    {
        if (_state == null) return -1;
        if (_mode == 1) { foreach (var (rect, row) in _rowRects) if (rect.HasPoint(pos)) return row; return -1; }
        if (!Area.HasPoint(pos)) return -1;
        int hit = -1; float best = Math.Max(radius, NR + 3);
        for (int i = 0; i < _rad.Length; i++) { float d = PosOf(i).DistanceTo(pos); if (d < best) { best = d; hit = i; } }
        return hit;
    }

    // Wheel zooms about the cursor, a drag pans, a click without a drag selects a node.
    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion m0)
        {
            _mouse = m0.Position;
            int hv = _mode == 0 && Area.HasPoint(m0.Position) ? HitAt(m0.Position, 12f) : -1;
            if (hv != _hover) { _hover = hv; Redraw(); }
        }
        if (e is InputEventMouseButton { Pressed: true } && _kbActive) { _kbActive = false; Redraw(); }   // a click ends the keyboard walk: no ring, no key tip
        if (e is InputEventKey { Pressed: true } ke && _mode == 0 && KeyNav(ke.Keycode, ke.ShiftPressed, ke.CtrlPressed || ke.AltPressed || ke.MetaPressed)) { AcceptEvent(); return; }
        if (_mode == 0 && e is InputEventMouseButton { Pressed: true } w && Area.HasPoint(w.Position) && w.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            _zoomTarget = PlanisphereMath.ClampZoom(_zoomTarget * (w.ButtonIndex == MouseButton.WheelUp ? 1.25f : 0.8f));
            _anchor = w.Position;
            AcceptEvent();
        }
        else if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            if (mb.Pressed)
            {
                _press = _mode == 0 && Area.HasPoint(mb.Position); _dragged = false; _longDone = false; _pressPos = mb.Position; _mouse = mb.Position; _pressMs = Time.GetTicksMsec();
                _pressRow = _press ? HitAt(mb.Position, 14f) : -1;
                if (_mode == 1) { int h = HitAt(mb.Position, 14f); if (h >= 0) Select(h); }
            }
            else
            {
                if (_press && !_dragged && !_longDone) { int hit = HitAt(mb.Position, 14f); if (hit >= 0) Select(hit); }
                _press = false;
            }
        }
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } rb && _mode == 0 && Area.HasPoint(rb.Position))
        {
            int hit = HitAt(rb.Position, 14f);
            if (hit >= 0) { Select(hit); OpenMenu(hit, rb.Position); }
            AcceptEvent();
        }
        else if (e is InputEventMouseMotion mm && _press)
        {
            if (!_dragged && (mm.Position - _pressPos).Length() < 5f) return;
            _dragged = true;
            _pan += mm.Relative; ClampPan(); Redraw();
        }
    }

    // ---- keyboard walk over the nodes (TASK-0132; A6-A8): Tab / Shift+Tab in number order, arrows to the nearest node, Enter opens the node panel; the key tip names the node ----
    int _kb = -1; bool _kbActive;
    string _kbTip; Vector2 _kbTipSize; int _kbTipFor = -1;
    float[] _kbX, _kbY;
    /// <summary>The node under the keyboard ring, or -1.</summary>
    public int KeyFocusRow => _kbActive && HasFocus() ? _kb : -1;
    public string KeyTipText => KeyFocusRow >= 0 ? _kbTip : null;

    bool KbVisible(int i) => Area.Grow(-NR).HasPoint(PosOf(i));

    /// <summary>Handles one key of the walk; true when the key was used (Tab past the last node is not used, so the focus moves on).</summary>
    public bool KeyNav(Key key, bool shift, bool modified)
    {
        if (modified || _state == null) return false;
        int n = _host.Data.Nodes.Count, next = -1;
        switch (key)
        {
            case Key.Tab:
                next = TreeKeyNav.Step(RowsByNumber(_host.Data.Nodes.Number), _kb, shift ? -1 : 1, KbVisible);
                if (next < 0) { _kbActive = false; _kb = -1; Redraw(); return false; }
                break;
            case Key.Left or Key.Right or Key.Up or Key.Down:
                if (_kb < 0 || !_kbActive) { next = _selected >= 0 && KbVisible(_selected) ? _selected : TreeKeyNav.Step(RowsByNumber(_host.Data.Nodes.Number), -1, 1, KbVisible); break; }
                if (_kbX == null || _kbX.Length != n) { _kbX = new float[n]; _kbY = new float[n]; }
                for (int i = 0; i < n; i++) { var p = PosOf(i); _kbX[i] = p.X; _kbY[i] = p.Y; }
                next = TreeKeyNav.Direction(_kbX, _kbY, _kb, key == Key.Left ? -1 : key == Key.Right ? 1 : 0, key == Key.Up ? -1 : key == Key.Down ? 1 : 0, KbVisible);
                if (next < 0) return true;   // the edge: the ring stays
                break;
            case Key.Enter or Key.KpEnter:
                if (_kb < 0 || !_kbActive) return false;
                Select(_kb); return true;
            case Key.Equal or Key.Plus or Key.KpAdd: ZoomBy(1); return true;
            case Key.Minus or Key.KpSubtract: ZoomBy(-1); return true;
            case Key.Key0 or Key.Kp0: ResetView(); return true;
            case Key.Menu or Key.F10 when key == Key.Menu || shift:   // the menu of the node under the keyboard ring, else of the selected one
                int mrow = _kbActive && _kb >= 0 ? _kb : _selected;
                if (mrow < 0) return false;
                Select(mrow); OpenMenu(mrow, PosOf(mrow)); return true;
            default: return false;
        }
        _kb = next; _kbActive = true; _kbTipFor = -1; Redraw();
        return true;
    }

    // The ring (2.5 du ink on a 5.5 du sheet halo: 3:1 on paper, wash and plate alike) and the key tip are drawn after the labels, on top of everything.
    void DrawKeyFocus()
    {
        int i = KeyFocusRow;
        if (i < 0) return;
        var nodes = _host.Data.Nodes; var p = PosOf(i); float r = Reach(i) + 5f;   // outside the corners, the goal frame and the selection ring
        _cv.DrawArc(p, r, 0, Mathf.Tau, 40, Ui.Sheet, 5.5f, true);
        _cv.DrawArc(p, r, 0, Mathf.Tau, 40, Ui.Ink, 2.5f, true);
        if (_kbTipFor != i) { _kbTip = Names.Node(nodes.Id[i]); _kbTipSize = Fonts.Text.GetStringSize(_kbTip, HorizontalAlignment.Left, -1, 14); _kbTipFor = i; }
        var sz = _kbTipSize + new Vector2(12, 8); var a = Area;
        float x = p.X + r + 4; if (x + sz.X > a.End.X - 2) x = p.X - r - 4 - sz.X;
        float y = Math.Clamp(p.Y - sz.Y * 0.5f, a.Position.Y + 2, a.End.Y - sz.Y - 2);
        var rc = new Rect2(x, y, sz);
        _cv.DrawRect(rc, Ui.Sheet); _cv.DrawRect(rc, Ui.Ink, false, 1f);
        Text(new Vector2(x + 6, y + 4 + _kbTipSize.Y * 0.8f), _kbTip, 14, Ui.Ink);
    }

    // Hover shows the node name and number (ids never appear in the visible text).
    public override string _GetTooltip(Vector2 atPosition)
    {
        int hit = HitAt(atPosition, 12f);
        if (hit < 0) return "";
        var nodes = _host.Data.Nodes;
        return Names.Node(nodes.Id[hit]) + "\n" + Loc.T("tree.effects.none");   // the localized name, never the raw id or a number
    }

    // ---------------- node menu (#473) ----------------
    // One function for the right click, the long press and the keyboard: four items; an unavailable one is hatched and says why in words (A1, A5).
    MenuLayer _menu; bool _menuOpen; int _menuRow, _menuHot = -1;
    NodeMenuItem[] _menuItems; Rect2 _menuBox; readonly Rect2[] _menuRect = new Rect2[4]; readonly List<string>[] _menuWhy = new List<string>[4]; string[] _menuLabel;
    const float MenuW = 330, MenuRowH = 34, MenuLine = 19;

    /// <summary>Opens the menu of a node at a point of the tree.</summary>
    void OpenMenu(int row, Vector2 at)
    {
        var nodes = _host.Data.Nodes;
        var ctx = new NodeMenuLogic.Ctx(_state[row] == St.Known, nodes.Type[row] == NodeType.PRC, _actor == Player, CanSetGoal: false, _focus.Contains(nodes.Number[row]), _focus.Count);
        _menuItems = NodeMenuLogic.Items(ctx); _menuRow = row; _menuLabel = new string[_menuItems.Length];
        float h = 8;
        for (int k = 0; k < _menuItems.Length; k++)
        {
            _menuLabel[k] = Loc.T(_menuItems[k].LabelKey);
            float rh = MenuRowH;
            _menuWhy[k] = null;
            if (!_menuItems[k].Enabled)
            {
                _menuWhy[k] = TextWrap.Lines(Loc.T(_menuItems[k].WhyKey!), MenuW - 28, t => Fonts.Text.GetStringSize(t, HorizontalAlignment.Left, -1, 14).X);
                rh += _menuWhy[k].Count * MenuLine + 4;
            }
            _menuRect[k] = new Rect2(4, h - 4 + 4, MenuW - 8, rh);   // y fixed up below
            h += rh + 2;
        }
        var pos = at + new Vector2(10, 10);
        pos.X = Math.Clamp(pos.X, 4, Size.X - MenuW - 4); pos.Y = Math.Clamp(pos.Y, 4, Size.Y - h - 4);
        _menuBox = new Rect2(pos, new Vector2(MenuW, h));
        float y = pos.Y + 4;
        for (int k = 0; k < _menuItems.Length; k++) { _menuRect[k] = new Rect2(pos.X + 4, y, MenuW - 8, _menuRect[k].Size.Y); y += _menuRect[k].Size.Y + 2; }
        _menuOpen = true; _menuHot = NodeMenuLogic.NextEnabled(_menuItems, -1, 1);
        _menu.Visible = true; _menu.MouseFilter = MouseFilterEnum.Stop; _menu.GrabFocus(); _menu.QueueRedraw();
    }

    void CloseMenu()
    {
        if (!_menuOpen) return;
        _menuOpen = false; _menu.Visible = false; _menu.MouseFilter = MouseFilterEnum.Ignore;
        if (IsVisibleInTree()) GrabFocus();
    }

    void PollLongPress()
    {
        if (!_press || _dragged || _pressRow < 0 || _menuOpen) return;
        float held = (Time.GetTicksMsec() - _pressMs) / 1000f;
        if (!NodeMenuLogic.IsLongPress(held, _mouse.DistanceTo(_pressPos))) return;
        _press = false; _longDone = true; Select(_pressRow); OpenMenu(_pressRow, _pressPos);
    }

    int MenuRowAt(Vector2 p) { for (int k = 0; k < _menuItems.Length; k++) if (_menuRect[k].HasPoint(p)) return k; return -1; }

    internal void MenuInput(InputEvent e)
    {
        if (!_menuOpen) return;
        switch (e)
        {
            case InputEventMouseMotion mm:
                int hv = MenuRowAt(mm.Position);
                if (hv >= 0 && _menuItems[hv].Enabled && hv != _menuHot) { _menuHot = hv; _menu.QueueRedraw(); }
                break;
            case InputEventMouseButton { Pressed: true } mb:
                int row = MenuRowAt(mb.Position);
                if (row >= 0 && mb.ButtonIndex == MouseButton.Left) { if (_menuItems[row].Enabled) ActivateMenu(row); }
                else if (row < 0 || mb.ButtonIndex != MouseButton.Left) CloseMenu();
                _menu.AcceptEvent();
                break;
            case InputEventKey { Pressed: true } k:
                if (k.Keycode == Key.Escape) CloseMenu();
                else if (k.Keycode is Key.Down or Key.Up) { _menuHot = NodeMenuLogic.NextEnabled(_menuItems, _menuHot, k.Keycode == Key.Down ? 1 : -1); _menu.QueueRedraw(); }
                else if (k.Keycode is Key.Enter or Key.KpEnter) { if (_menuHot >= 0) ActivateMenu(_menuHot); }
                else break;
                _menu.AcceptEvent();
                break;
        }
    }

    void ActivateMenu(int k)
    {
        var action = _menuItems[k].Action; int row = _menuRow; var nodes = _host.Data.Nodes;
        CloseMenu();
        switch (action)
        {
            case NodeAction.MasteryFocus:
                int num = nodes.Number[row];
                if (!_focus.Remove(num) && _focus.Count < NodeMenuLogic.FocusLimit) _focus.Add(num);
                _insetDirty = true; Select(row); break;
            case NodeAction.WhyBlocked: _whyOpen = true; Select(row); break;
            case NodeAction.ShowLinks:   // the links of a selected node are drawn; the view comes in to the step where the names are readable
                Select(row); _zoomTarget = PlanisphereMath.ClampZoom(Math.Max(_zoomTarget, ZoomSteps.Scales[ZoomSteps.Names - 1])); _anchor = PosOf(row); break;
        }
    }

    internal void PaintMenu(Control ci)
    {
        if (!_menuOpen) return;
        _cv = ci;
        ci.DrawRect(_menuBox.Grow(2), Ui.Sheet); ci.DrawRect(_menuBox, Ui.Sheet); ci.DrawRect(_menuBox, Ui.Ink, false, 1.5f);
        for (int k = 0; k < _menuItems.Length; k++)
        {
            var r = _menuRect[k]; bool on = _menuItems[k].Enabled, hot = k == _menuHot;
            if (on && hot) ci.DrawRect(r, Ui.Hover);
            var head = new Rect2(r.Position, new Vector2(r.Size.X, MenuRowH));
            if (!on) HatchRect(ci, head, 8f, new Color(Ui.Ink3, 0.45f));   // disabled: hatched, the reason follows in words
            ci.DrawRect(r, on ? Ui.Ink : Ui.Ink3, false, on && hot ? 2f : 1f);
            if (on && hot) ci.DrawRect(r.Grow(-2), Ui.Ink, false, 2f);
            float ty = r.Position.Y + MenuRowH * 0.5f + 5;
            if (!on) ci.DrawStringOutline(Fonts.Text, new Vector2(r.Position.X + 10, ty), _menuLabel[k], HorizontalAlignment.Left, -1, 14, 5, Ui.Sheet);
            Text(new Vector2(r.Position.X + 10, ty), _menuLabel[k], 14, on ? Ui.Ink : Ui.Ink2);
            if (!on) for (int j = 0; j < _menuWhy[k].Count; j++) Text(new Vector2(r.Position.X + 14, r.Position.Y + MenuRowH + 15 + j * MenuLine), _menuWhy[k][j], 14, Ui.Ink);
        }
        _cv = this;
    }

    // Diagonal hatch inside a rectangle, clipped without allocating.
    static void HatchRect(CanvasItem ci, Rect2 r, float step, Color c)
    {
        float h = r.Size.Y;
        for (float x = r.Position.X - h; x < r.End.X; x += step)
        {
            float t0 = Math.Max(0f, (r.Position.X - x) / h), t1 = Math.Min(1f, (r.End.X - x) / h);
            if (t1 <= t0) continue;
            ci.DrawLine(new Vector2(x + t0 * h, r.End.Y - t0 * h), new Vector2(x + t1 * h, r.End.Y - t1 * h), c, 1f);
        }
    }

    /// <summary>Shot run: opens the menu of a node (a real session state) and returns the rows as words, or null when the menu did not open.</summary>
    public string ShotOpenMenu(int row)
    {
        if (row < 0 || _state == null) return null;
        Select(row); OpenMenu(row, PosOf(row));
        var sb = new System.Text.StringBuilder();
        for (int k = 0; k < _menuItems.Length; k++) sb.Append(_menuItems[k].Enabled ? "[x] " : "[hatched: " + Loc.T(_menuItems[k].WhyKey!) + "] ").Append(_menuLabel[k]).Append("; ");
        return sb.ToString();
    }
    public void ShotCloseMenu() => CloseMenu();
    /// <summary>Shot run: the keyboard focus on the zoom-in button, and on the first view tab (frames that prove a visible focus on controls other than nodes, A7).</summary>
    public void ShotFocusZoomIn() => _zoomIn.GrabFocus();
    public bool ShotFocusTab(int k = 0) { if (_viewSwitch.GetChildCount() <= k || _viewSwitch.GetChild(k) is not Control c || c.FocusMode == FocusModeEnum.None) return false; c.GrabFocus(); return true; }
    /// <summary>Shot run (#473): the close button is 40 du high and fully inside the sheet, clear of the goal panel; the zoom bar sits inside the map; null when fine.</summary>
    public string ShotChromeCheck()
    {
        var cb = new Rect2(_closeBtn.Position, _closeBtn.Size);
        if (cb.Size.Y < CloseH - 0.5f) return $"close button is {cb.Size.Y:F0} du high";
        if (!new Rect2(Vector2.Zero, Size).Encloses(cb)) return "close button leaves the sheet";
        if (_insetBox.Visible && cb.Intersects(PanelRect)) return "close button touches the goal panel";
        var zb = new Rect2(_zoomBar.Position, _zoomBar.Size);
        if (!Area.Encloses(zb)) return $"zoom bar {zb} leaves the map {Area}";
        if (_zoomIn.Size.Y < SubpanelButtonDu - 0.5f || _zoomIn.Size.X < 24f) return "a zoom button is below 32 du";
        return null;
    }
    public bool ShotMenuOpen => _menuOpen;

    // ---------------- inset ----------------
    void BuildInset(HostSnapshot s)
    {
        foreach (var c in _inset.GetChildren()) { _inset.RemoveChild(c); c.QueueFree(); }
        var nodes = _host.Data.Nodes;
        string id = _host.Layout.ActorIds[_actor];
        _inset.AddChild(Ui.Lbl(Loc.T("inset.title", Names.Actor(id)), 20, null, FontKind.Draft));
        if (_selected >= 0)
        {
            Sec("inset.node");
            int i = _selected;
            var nm = Ui.Lbl(Names.Node(nodes.Id[i]), 18); nm.MouseFilter = MouseFilterEnum.Pass;
            _inset.AddChild(nm);
            _inset.AddChild(Ui.Wrap(Loc.T("card.index", nodes.Number[i], nodes.Count), 14, Ui.Ink3));
            _inset.AddChild(Ui.Wrap($"{Loc.T("ntype." + (int)nodes.Type[i])} · {Loc.T("ui.tree.gen", nodes.Gen[i])} · {Loc.T("disc." + (int)nodes.Discipline[i])} · {StateWord(i)}", 14, Ui.Ink2));
            string anchor = Names.NodeAnchor(nodes.Id[i]), before = nodes.AnchorBefore[i] ? Loc.T("inset.before") : "";
            string est = nodes.AnchorEstimated[i] ? ".est" : "";   // the data flag: an estimated year is said to be one
            var anc = Ui.Wrap(anchor == "" ? Loc.T("ui.tree.anchor" + est, nodes.AnchorYear[i], before) : Loc.T("ui.tree.anchor.named" + est, nodes.AnchorYear[i], before, anchor), 14, Ui.Ink3);
            anc.MouseFilter = MouseFilterEnum.Pass; anc.TooltipText = Loc.T("ui.tree.anchor.tip" + est);
            _inset.AddChild(anc);
            BuildCard(i);
            var rq = nodes.Requires;
            var req = new List<string>();
            for (int g = rq.GroupStart[i]; g < rq.GroupStart[i + 1]; g++)
            {
                var mem = rq.GroupMembers(g).ToArray();
                string one = string.Join(" | ", mem.Select(m => (_state[m] == St.Known ? "✓ " : "✗ ") + Names.Node(nodes.Id[m])));
                req.Add(mem.Length > 1 ? "(" + one + ")" : one);
            }
            _inset.AddChild(Block(CardIcon.Kind.Needs, "card.requires", (req.Count == 0 ? new List<string> { "—" } : req).Select(r => (Control)Ui.Wrap(r, 14, Ui.Ink)).ToArray()));
            if (_nv[i].Process is { } pv)
                _inset.AddChild(Ui.Wrap(Loc.T("inset.process", Loc.N(pv.Yield.ToDouble()), Loc.T(pv.Mastered ? "yes" : "no"), Loc.T(pv.Admitted ? "yes" : "no")), 14, Ui.Ink2));
            if (nodes.Type[i] == NodeType.PRC && _state[i] == St.Known && _actor == Player)
            {
                int num = nodes.Number[i];
                bool inF = _focus.Contains(num);
                _inset.AddChild(Ui.Btn(inF ? "inset.focus.remove" : "inset.focus.add", () =>
                {
                    if (inF) _focus.Remove(num); else if (_focus.Count < 3) _focus.Add(num);
                    _insetDirty = true;
                }));
            }
        }

        _inset.AddChild(new HSeparator());
        bool whyOpen = _whyOpen || _selected < 0;   // collapsed while a node card is shown, so the card stays above the fold
        string whyHead = _goal >= 0 ? Loc.T("inset.why", Names.Node(nodes.Id[_goal])) : Loc.T("inset.why.nogoal");   // the heading names its goal (spec 2б.5)
        _inset.AddChild(Ui.Btn((whyOpen ? "▾ " : "▸ ") + whyHead, () => { _whyOpen = !_whyOpen; _insetDirty = true; }));
        _whyShown = whyOpen;
        if (whyOpen) BuildWhy(s, _host.Data);

        Sec("inset.focus");
        _inset.AddChild(Ui.Wrap(Loc.T("inset.focus.help"), 14, Ui.Ink2));
        _inset.AddChild(Ui.Wrap(_focus.Count == 0 ? Loc.T("inset.focus.empty") : string.Join(" → ", _focus.Select(f => Names.Node(_host.Data, f))), 14, _focus.Count == 0 ? Ui.Ink2 : Ui.Ink));
        var send = Ui.Btn("inset.focus.send", () => { if (_actor == Player) _host.Submit(new SetEffortFocus(_actor, ImmutableArray.CreateRange(_focus)), "cmd.focus"); }, 14, _focus.Count > 0);
        send.Disabled = _focus.Count == 0 || _actor != Player;
        send.CustomMinimumSize = new Vector2(0, SubpanelButtonDu);
        if (!send.Disabled) send.AddThemeStyleboxOverride("normal", Ui.Box(Ui.Command, Ui.Ink, 2, 10));   // spec 2.9: the one enabled main button has a 2 du frame
        _inset.AddChild(send);
        if (_focus.Count == 0 && _actor == Player) _inset.AddChild(Ui.Wrap(Loc.T("inset.focus.nothing"), 16, Ui.Ink));   // C4: a grey button says why, right under it, at a readable size
        if (s.Last.Status != CmdStatus.None && TreeClarity.ShowsCommand(s.Last.LabelKey))
        {
            string what = Loc.T(s.Last.LabelKey, s.Last.LabelArg);
            string why = s.Last.Status == CmdStatus.Rejected ? Loc.TOr("refusal." + s.Last.Reason, s.Last.Reason) : s.Last.Status == CmdStatus.Error ? s.Last.Reason : "";
            var lab = Ui.Wrap(Loc.T("cmd." + s.Last.Status, what, why), 14, Ui.Ink2);
            _inset.AddChild(s.Last.Status is CmdStatus.Rejected or CmdStatus.Error ? Ui.MarkedRow(Ui.Alarm, lab) : lab);
        }

    }

    bool _whyOpen, _whyShown;

    void BuildWhy(HostSnapshot s, GameData data)
    {
        var nodes = data.Nodes;
        if (s.Target is not { } t) _inset.AddChild(Ui.Wrap(Loc.T("why.none"), 14, Ui.Ink2));
        else
        {
            string goalName = _goal >= 0 ? Names.Node(nodes.Id[_goal]) : Loc.T("why.goalkind", t.Kind.ToString(), t.Id);
            _inset.AddChild(Ui.Wrap(Loc.T("inset.goal", goalName, _root >= 0 ? _depth[_root] : 0)));
            int n = 0;
            foreach (var l in s.Links.Where(l => l.Layer == WhyNotLayer.Knowledge).Take(4))
            {
                string text = string.Join(" ", new[] { Loc.T("why.line", Loc.TOr("cause." + l.Cause, l.Cause.ToString()), ": " + Names.Node(data, l.SubjectId)), Loc.T("why.have", Loc.N(l.Have.ToDouble()), Loc.N(l.Need.ToDouble())), l.EtaMonths is int m ? Loc.T("why.eta", m) : "" }.Select(x => x.Trim()).Where(x => x != ""));
                var lab = Ui.Wrap(text);
                _inset.AddChild(n++ == 0 ? RootRow(lab) : lab);
            }
            if (_root >= 0 && _goal >= 0) _inset.AddChild(Ui.Wrap("inset.hint", 14, Ui.Ink3));
        }

    }

    // The first link of "why not" is the root: it carries the same sign as the root on the map (C2).
    static HBoxContainer RootRow(Control text)
    {
        var r = new HBoxContainer(); r.AddThemeConstantOverride("separation", 6);
        r.AddChild(new RootMarkIcon()); r.AddChild(text);
        return r;
    }

    // ---------------- node card ----------------
    static readonly string[] DiscToken = { "aero", "prop", "metal", "chem", "mech" };
    readonly Dictionary<int, Texture2D> _schem = new();

    // Blueprint schematic of ui-art-director: the file is chosen by NodeSchematics (README rule), an unreadable file gives the placeholder.
    Texture2D Schematic(int row)
    {
        if (_schem.TryGetValue(row, out var tex)) return tex;
        var nodes = _host.Data.Nodes;
        string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "../../docs/ui/assets/node-schematics"));
        if (System.IO.Directory.Exists(dir))
        {
            string stem = NodeSchematics.Pick(nodes.Id[row], nodes.Type[row].ToString(), DiscToken[(int)nodes.Discipline[row]], f => System.IO.File.Exists(System.IO.Path.Combine(dir, f + ".svg")));
            if (stem != null)
            {
                try
                {
                    var img = new Image();
                    if (img.LoadSvgFromString(System.IO.File.ReadAllText(System.IO.Path.Combine(dir, stem + ".svg")), 2f) == Error.Ok) tex = ImageTexture.CreateFromImage(img);
                }
                catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException) { GD.PushWarning("schematic: " + e.Message); }
            }
        }
        return _schem[row] = tex;
    }

    static StyleBoxFlat Plate(Color wash, Color line, int bar)
    {
        var sb = Ui.Box(wash, line, 1, 10);
        sb.BorderWidthLeft = bar; sb.SetCornerRadiusAll(6);
        return sb;
    }

    // Card (spec 2.6): sheet, 1 du rule frame, a 3 du ink-2 bar on the left; the kind shows in the icon and the heading, not in a fill colour.
    PanelContainer Block(CardIcon.Kind icon, string headKey, params Control[] body)
    {
        var box = new PanelContainer(); box.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Sheet, Ui.Rule, 1, 0, Ui.R1, 0));
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 0);
        row.AddChild(new ColorRect { Color = Ui.Ink2, CustomMinimumSize = new Vector2(3, 0) });
        var pad = new MarginContainer(); pad.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        foreach (var side in new[] { "margin_left", "margin_right" }) pad.AddThemeConstantOverride(side, 10);
        foreach (var side in new[] { "margin_top", "margin_bottom" }) pad.AddThemeConstantOverride(side, 6);
        var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 3);
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 6);
        head.AddChild(new CardIcon(icon)); head.AddChild(Ui.Lbl(headKey, 14, Ui.Ink, FontKind.Draft));
        v.AddChild(head);
        foreach (var c in body) v.AddChild(c);
        pad.AddChild(v); row.AddChild(pad); box.AddChild(row);
        return box;
    }

    // Card of the selected node: schematic, what it affects, which parameter it raises, what it opens next. Colours: palette C inks.
    void BuildCard(int i)
    {
        var nodes = _host.Data.Nodes;
        int t = (int)nodes.Type[i], d = (int)nodes.Discipline[i];
        // 1. schematic: the picture in a cut-out window (spec 2.7: a 6 du inner shadow top-left), or a framed placeholder with the type label until the drawing exists
        var tex = Schematic(i);
        if (tex != null)
        {
            var window = new PanelContainer(); var wb = Ui.Box(Ui.Sheet.Darkened(0.18f), Ui.Rule, 1, 0, Ui.R1, 0);
            wb.ContentMarginLeft = 6; wb.ContentMarginTop = 6; wb.ContentMarginRight = 1; wb.ContentMarginBottom = 1;
            window.AddThemeStyleboxOverride("panel", wb);
            var paper = new PanelContainer(); paper.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Sheet, null, 0, 0, 0, 0));
            paper.AddChild(new TextureRect { Texture = tex, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(0, 124), Modulate = new Color(1, 1, 1, 0.6f) });
            window.AddChild(paper);
            _inset.AddChild(window);
            _inset.AddChild(Ui.Lbl("ui.tree.schem.example", 14, Ui.Ink3));
        }
        else
        {
            var frame = new PanelContainer { CustomMinimumSize = new Vector2(0, 120) };
            frame.AddThemeStyleboxOverride("panel", Plate(Ui.DiscWash[d], Ui.DiscLine[d], 1));
            var ph = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            var l1 = Ui.Lbl(Loc.T("ntype." + t) + " · " + Loc.T("disc." + d), 18, Ui.DiscText[d], FontKind.Draft); l1.HorizontalAlignment = HorizontalAlignment.Center;
            var l2 = Ui.Lbl("card.noschem", 14, Ui.DiscText[d]); l2.HorizontalAlignment = HorizontalAlignment.Center;
            ph.AddChild(l1); ph.AddChild(l2); frame.AddChild(ph);
            _inset.AddChild(frame);
        }

        // 2. affects: layer and system (real data: type, cluster, discipline)
        _inset.AddChild(Block(CardIcon.Kind.Touches, "card.affects",
            Ui.Wrap(Loc.T("card.layer", Loc.T("ntype." + t), t + 1), 14, Ui.Ink),
            Ui.Wrap(Loc.T("card.system", Loc.T("cluster." + (int)nodes.Cluster[i]), Loc.T("disc." + d)), 14, Ui.Ink)));

        // 3. parameter: only what Core has (process yield, capability tier); the gain per parameter is shown when the core provides it
        var pbody = new List<Control>();
        foreach (var pl in TreeClarity.ParamLines(_nv[i].Process != null, nodes.Type[i] == NodeType.APP, nodes.Type[i] == NodeType.PRC))
            switch (pl)
            {
                case TreeClarity.ParamLine.Yield: pbody.Add(Ui.Wrap(Loc.T("card.param.yield", Loc.N(_nv[i].Process!.Value.Yield.ToDouble())), 14, Ui.Ink)); break;
                case TreeClarity.ParamLine.ProductGeneration: pbody.Add(Ui.Wrap(Loc.T("card.param.app", nodes.Gen[i]), 14, Ui.Ink)); break;
                case TreeClarity.ParamLine.KnowledgeStep: pbody.Add(Ui.Wrap(Loc.T("ui.tree.effects.knowledge"), 14, Ui.Ink2)); break;
                default: pbody.Add(Ui.Wrap(Loc.T("card.param.cap", Loc.N(nodes.ReqPrecision[i].ToDouble()), Loc.N(nodes.ReqPurity[i].ToDouble()), Loc.N(nodes.ReqQualityControl[i].ToDouble()), nodes.Tier[i]), 14, Ui.Ink2)); break;
            }
        pbody.Add(Ui.Wrap(Loc.T("tree.effects.none"), 14, Ui.Ink2));   // numeric effects: TASK-0157; said here once, not on every node of the map
        _inset.AddChild(Block(CardIcon.Kind.Raises, "card.raises", pbody.ToArray()));

        // 4. unlocks: the rows that list this node in requires
        var next = new List<Control>();
        var dep = _dep[i].OrderBy(x => nodes.Number[x]).ToList();
        foreach (int x in dep.Take(6))
            next.Add(Ui.Wrap((_state[x] == St.Known ? "● " : _state[x] == St.Frontier ? "◐ " : "○ ") + Names.Node(nodes.Id[x]), 14, Ui.Ink));
        if (dep.Count > 6) next.Add(Ui.Wrap(Loc.T("card.more", dep.Count - 6), 14, Ui.Ink2));
        if (dep.Count == 0) next.Add(Ui.Wrap(Loc.T("card.end"), 14, Ui.Ink2));
        _inset.AddChild(Block(CardIcon.Kind.Opens, "card.unlocks", next.ToArray()));
    }

    void Sec(string key) { _inset.AddChild(new HSeparator()); _inset.AddChild(Ui.Head(key)); }
}
