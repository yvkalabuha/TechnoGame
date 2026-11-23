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

    const float NodeR = 6.5f;
    const float ZoomRate = 14f;
    const float WedgeMargin = 0.14f;

    readonly SimHost _host;
    readonly VBoxContainer _inset = new();
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
    readonly Dictionary<int, int> _prio = new();     // mock priorities, row -> 0..3
    readonly List<int> _focus = new();               // SetEffortFocus draft, process numbers
    float[] _rad, _ang;                              // ring (1..6 plus jitter) and angle in degrees
    readonly List<(Rect2 rect, int row)> _rowRects = new();
    List<int>[] _dep;                                // dependants: row -> rows that list it in requires
    // Zoom and pan of the planisphere (PlanisphereMath). The drawing is clipped to the view control; _cv is the item being painted.
    readonly Control _view = new() { ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
    CanvasItem _cv;
    float _zoom = 1f, _zoomTarget = 1f; Vector2 _pan, _anchor;
    bool _press, _dragged; Vector2 _pressPos;
    Lod CurLod => PlanisphereMath.LodOf(_zoom);
    /// <summary>The player's own power: orders (effort focus) are enabled only for it.</summary>
    public int Player { get; set; }
    public event Action Closed;

    static readonly string[] Level = { "level.0", "level.1", "level.2", "level.3" };

    public TechTree(SimHost host)
    {
        _host = host; _cv = this;
        MouseFilter = MouseFilterEnum.Stop;

        var top = new HBoxContainer { Position = new Vector2(14, 8) };
        top.AddThemeConstantOverride("separation", 14);
        top.AddChild(Ui.Lbl("tree.path", 18, null, FontKind.Draft));
        _viewSwitch = new Segment(new[] { "tree.a", "tree.b" }, 0);
        _viewSwitch.Changed += i => { _mode = i; Redraw(); };
        top.AddChild(_viewSwitch);
        top.AddChild(Ui.Btn("tree.reset", ResetView));
        top.AddChild(Ui.Btn("tree.close", () => Closed?.Invoke()));
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
        AddChild(top);

        _insetBox.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Sheet, Ui.Ink, 1, 12));
        // Sized by its content (like the power panel): anchored top-right, no bottom anchor; the scroll appears only above the stamp.
        _insetBox.AnchorLeft = 0.6f; _insetBox.AnchorRight = 1; _insetBox.OffsetLeft = 0; _insetBox.OffsetTop = 6; _insetBox.OffsetRight = -6;
        var sc = _insetScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _inset.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _inset.AddThemeConstantOverride("separation", 6);
        sc.AddChild(_inset); _insetBox.AddChild(sc);
        AddChild(_insetBox);
        Loc.Changed += Invalidate;
        TreeExiting += () => Loc.Changed -= Invalidate;
    }

    void Invalidate() { _insetDirty = true; _lgDirty = true; _legendBtn.Text = "L  " + Loc.T("legend.btn"); Redraw(); }
    void Redraw() { QueueRedraw(); _view.QueueRedraw(); }

    public void ResetView() { _zoomTarget = 1f; _anchor = BaseCentre; _pan = Vector2.Zero; Redraw(); }
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
    public int FrontierRow { get { for (int i = 0; _state != null && i < _state.Length; i++) if (_state[i] == St.Frontier && _route[i]) return i; return -1; } }

    public void ShowFor(int actor, int mode)
    {
        _actor = actor; _modelDirty = true; _insetDirty = true; _mode = mode; _viewSwitch.Select(mode, false); _focus.Clear(); _selected = -1; Visible = true;
    }
    public void SetMode(int mode) { _mode = mode; _viewSwitch.Select(mode, false); Redraw(); }
    /// <summary>Lowest edge of the goal panel: above the open legend sheet, else the bottom margin.</summary>
    public float InsetLimit => LegendOpen ? Size.Y - 346 : Size.Y - 100;
    public void Select(int row) { _selected = row; _insetDirty = true; Redraw(); }

    // ---------------- model ----------------
    public override void _Process(double delta)
    {
        // The goal panel yields to the open legend: its bottom stays 8 du above the sheet (the sheet top is Size.Y - 332) and the rest scrolls inside.
        float limit = InsetLimit;
        _insetScroll.CustomMinimumSize = new Vector2(0, Math.Min(_inset.GetCombinedMinimumSize().Y, LegendOpen ? limit - _insetBox.GetThemeStylebox("panel").GetMinimumSize().Y : limit));
        _legendBtn.Position = new Vector2(Area.Position.X + 10, Area.End.Y - 42); _legendBtn.Size = new Vector2(144, 32);
        _legendBtn.Visible = _mode == 0 && !LegendOpen;
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

    // Wedge = discipline (clockwise = newer generation), ring = node type (layer). Nodes of one wedge-ring cell are spread by generation.
    void Layout()
    {
        var nodes = _host.Data.Nodes;
        _rad = new float[nodes.Count]; _ang = new float[nodes.Count];
        var cell = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < nodes.Count; i++)
        {
            var k = ((int)nodes.Type[i], (int)nodes.Discipline[i]);
            if (!cell.TryGetValue(k, out var l)) cell[k] = l = new List<int>();
            l.Add(i);
        }
        foreach (var kv in cell)
        {
            var l = kv.Value.OrderBy(i => nodes.Gen[i]).ThenBy(i => nodes.Number[i]).ToList();
            for (int j = 0; j < l.Count; j++)
            {
                _ang[l[j]] = -90f + kv.Key.Item2 * 72f + 72f * (WedgeMargin + (1 - 2 * WedgeMargin) * (j + 0.5f) / l.Count);
                _rad[l[j]] = kv.Key.Item1 + 1f + (j % 2 == 0 ? -0.1f : 0.1f);
            }
        }
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
        _cv.DrawRect(r, Ui.Sheet); _cv.DrawRect(r, Ui.Ink, false, 2f);
        _cv.DrawLine(r.Position + new Vector2(0, 26), r.Position + new Vector2(r.Size.X, 26), Ui.Ink, 1f);
        Text(r.Position + new Vector2(10, 20), Loc.T("tree.stamp"), 16, Ui.Ink, FontKind.Draft);
        Text(r.Position + new Vector2(r.Size.X - 10 - 120, 20), Loc.T("tree.sheet", _mode == 0 ? "A" : "B"), 14, Ui.Ink2, FontKind.Draft, HorizontalAlignment.Right, 120);
        string who = _actor >= 0 ? Names.Actor(_host.Layout.ActorIds[_actor]) : "";
        Text(r.Position + new Vector2(10, 50), Fit(who, 190, 14), 14, Ui.Ink);
        Text(r.Position + new Vector2(r.Size.X - 10 - 130, 50), _date.ToString(), 14, Ui.Ink, FontKind.Mono, HorizontalAlignment.Right, 130);
    }

    // Channel route of an edge between rings: radial out of the source, an arc along the channel (half-integer radius), radial into the target.
    List<Vector2> Channel(int m, int i)
    {
        int ringI = (int)Mathf.Round(_rad[i]);
        float rc = ringI < 6 ? ringI + 0.5f : 5.78f;               // 5.5 is the double circle itself
        if (_rad[i] > _rad[m] + 0.3f) rc = ringI == 6 ? 5.78f : ringI - 0.5f;
        var pts = new List<Vector2> { PosOf(m), Polar(rc, _ang[m]) };
        float a0 = _ang[m], a1 = a0 + Mathf.PosMod(_ang[i] - a0 + 180f, 360f) - 180f;   // the short way round
        int steps = Math.Max(1, (int)(Math.Abs(a1 - a0) / 4f));
        for (int k = 1; k <= steps; k++) pts.Add(Polar(rc, a0 + (a1 - a0) * k / steps));
        pts.Add(PosOf(i));
        return pts;
    }

    // Wedge name outside the outer ring: left-aligned on the right side, right-aligned on the left, centred below; trimmed to the free width.
    (Rect2 rect, string text) WedgeLabel(int w)
    {
        float mid = -90f + w * 72f + 36f, cs = Mathf.Cos(Mathf.DegToRad(mid)), sn = Mathf.Sin(Mathf.DegToRad(mid));
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
        _countRects.Clear(); _labelRects.Clear();
        CountCells();
        var c = Centre; float u = Unit;
        // layers: six rings; the double circle between processes and products is the «knowledge ≠ production» line
        for (int r = 1; r <= 6; r++) if (r != 6) _cv.DrawArc(c, r * u, 0, Mathf.Tau, 96, Ui.Rule, 1f, true);
        _cv.DrawArc(c, 5.5f * u - 3f, 0, Mathf.Tau, 96, Ui.Ink, 1.6f, true);
        _cv.DrawArc(c, 5.5f * u + 3f, 0, Mathf.Tau, 96, Ui.Ink, 1.6f, true);
        _cv.DrawArc(c, 6 * u, 0, Mathf.Tau, 96, Ui.Rule, 1f, true);
        for (int w = 0; w < 5; w++)
        {
            float a = Mathf.DegToRad(-90f + w * 72f);
            var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            _cv.DrawLine(c + d * u * 0.6f, c + d * (6.5f * u), Ui.Rule, 1f);
            var (rc, s) = WedgeLabel(w);
            Text(new Vector2(rc.Position.X, rc.End.Y - 4), s, 14, Ui.Ink2, FontKind.Draft);
            // The wedge summary known/total: under the wedge name, never skipped, on the paper (no plate).
            var ssz = Fonts.Mono.GetStringSize(_sumText[w], HorizontalAlignment.Left, -1, 14);
            _sumRects[w] = new Rect2(rc.GetCenter().X - ssz.X * 0.5f, rc.End.Y + 2, ssz);
            Text(new Vector2(_sumRects[w].Position.X, _sumRects[w].Position.Y + ssz.Y * 0.8f), _sumText[w], 14, Ui.DiscText[w], FontKind.Mono);
        }
        DrawClock(c, u);
        DrawCells(c, u);

        var nodes = _host.Data.Nodes; var rq = nodes.Requires;
        var routeEdges = new HashSet<(int, int)>();
        for (int k = 0; k + 1 < _path.Count; k++) routeEdges.Add((_path[k], _path[k + 1]));
        // requires edges (every any_of alternative): quiet first, then selected, then the route on top
        for (int pass = 0; pass < 3; pass++)
            for (int i = 0; i < nodes.Count; i++)
                for (int g = rq.GroupStart[i]; g < rq.GroupStart[i + 1]; g++)
                    foreach (int m in rq.GroupMembers(g))
                    {
                        bool onRoute = _route[i] && _route[m];
                        bool toSel = _selected >= 0 && (i == _selected || m == _selected);
                        if (pass == 0 && !onRoute && !toSel) _cv.DrawPolyline(Channel(m, i).ToArray(), new Color(Ui.Rule, 0.7f), 1f, true);
                        else if (pass == 1 && toSel && !onRoute) _cv.DrawPolyline(Channel(m, i).ToArray(), Ui.Ink, 1.6f, true);
                        else if (pass == 2 && onRoute) _cv.DrawPolyline(Channel(m, i).ToArray(), Ui.Command, 2.2f, true);
                    }
        // the continuous route from the root of «why not» to the goal
        for (int k = 0; k + 1 < _path.Count; k++) _cv.DrawPolyline(Channel(_path[k], _path[k + 1]).ToArray(), Ui.Command, 3.4f, true);

        for (int i = 0; i < nodes.Count; i++) DrawNode(i);
        DrawLabels(c);
        DrawCallout();
    }

    // Clock: 1910 → 2100 around the circle, the hand shows the session date; G1–G4 arcs and the P10–P90 interval have no data (not drawn).
    void DrawClock(Vector2 c, float u)
    {
        float r = u * 0.5f;
        _cv.DrawArc(c, r, 0, Mathf.Tau, 48, Ui.Ink, 1.6f, true);
        for (int y = 1910; y < 2100; y += 10)
        {
            float a = Mathf.Tau * (y - 1910) / 190f - Mathf.Pi / 2;
            var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            _cv.DrawLine(c + d * r, c + d * (r + (y % 50 == 0 ? 6f : 3f)), Ui.Ink, y % 50 == 0 ? SettingsLogic.MinTickDu + 1f : SettingsLogic.MinTickDu, true);   // A2: ticks >= 2 physical px
        }
        float frac = (_date.Year + (_date.Month - 1) / 12f + (_date.Day - 1) / 365f - 1910f) / 190f;
        float h = Mathf.Tau * frac - Mathf.Pi / 2;
        _cv.DrawLine(c, c + new Vector2(Mathf.Cos(h), Mathf.Sin(h)) * (r + 4), Ui.Ink, 2f, true);
        _cv.DrawCircle(c, 2.5f, Ui.Ink);
    }

    // Node radius grows a little with the zoom; icon plates are larger.
    // The ring shrinks with the canvas; node marks keep their physical size (du scaled down to 0.7) so that clusters stay apart.
    float NodeScale => Math.Clamp(BaseUnit / 60f, 0.7f, 1f);
    float NR => CurLod switch { Lod.Overview => NodeR * NodeScale, Lod.Nodes => NodeR * 1.25f * NodeScale, _ => GlyphGeometry.IconsPlateMin + Math.Min(6f, (_zoom - PlanisphereMath.IconsFrom) * 2f) };

    // Counts per (ring, wedge) cell and per wedge, filled once per draw into fields (no allocation); the wedge summaries are the always-visible counts.
    readonly int[,] _cellTot = new int[6, 5], _cellKn = new int[6, 5];
    readonly int[] _wTot = new int[5], _wKn = new int[5], _sumShown = { -1, -1, -1, -1, -1 };
    readonly string[] _sumText = new string[5];
    readonly Rect2[] _sumRects = new Rect2[5];
    void CountCells()
    {
        var nodes = _host.Data.Nodes;
        Array.Clear(_cellTot); Array.Clear(_cellKn); Array.Clear(_wTot); Array.Clear(_wKn);
        for (int i = 0; i < nodes.Count; i++)
        {
            int t = (int)nodes.Type[i], d = (int)nodes.Discipline[i]; bool kn = _state[i] == St.Known;
            _cellTot[t, d]++; _wTot[d]++;
            if (kn) { _cellKn[t, d]++; _wKn[d]++; }
        }
        for (int w = 0; w < 5; w++) if (_sumShown[w] != _wKn[w] * 10000 + _wTot[w]) { _sumShown[w] = _wKn[w] * 10000 + _wTot[w]; _sumText[w] = _wKn[w] + "/" + _wTot[w]; }
    }

    // Overview: every (layer, discipline) cell is a wash with «known/total»; zoomed in the wash fades into a quiet band.
    void DrawCells(Vector2 c, float u)
    {
        var nodes = _host.Data.Nodes;
        var tot = _cellTot; var kn = _cellKn;
        float alpha = CurLod == Lod.Overview ? 0.75f : CurLod == Lod.Nodes ? 0.4f : 0.22f;
        for (int t = 0; t < 6; t++)
            for (int d = 0; d < 5; d++)
            {
                if (tot[t, d] == 0) continue;
                float a0 = -90f + d * 72f + 72f * WedgeMargin * 0.5f, a1 = -90f + (d + 1) * 72f - 72f * WedgeMargin * 0.5f;
                _cv.DrawArc(c, (t + 1) * u, Mathf.DegToRad(a0), Mathf.DegToRad(a1), 24, new Color(Ui.DiscWash[d], alpha), Math.Max(10f, u * 0.46f), true);
                if (CurLod != Lod.Overview) continue;
                var q = Polar(t + 1.5f, (a0 + a1) * 0.5f);
                string txt = kn[t, d] + "/" + tot[t, d];
                var sz = Fonts.Mono.GetStringSize(txt, HorizontalAlignment.Left, -1, 14);
                var rc = new Rect2(q - sz * 0.5f, sz);
                if (!CountFits(rc.Grow(2))) continue;   // a tag that would sit on a node or another label is left out (A9)
                _countRects.Add(rc.Grow(2));
                _cv.DrawRect(rc.Grow(2), new Color(Ui.Paper, 0.85f));
                Text(new Vector2(rc.Position.X, rc.Position.Y + sz.Y * 0.8f), txt, 14, Ui.DiscText[d], FontKind.Mono);
            }
    }

    readonly List<Rect2> _countRects = new(), _labelRects = new();

    Rect2 NodeBox(int i) { var p = PosOf(i); float h = NR + 1; return new Rect2(p - new Vector2(h, h), new Vector2(h * 2, h * 2)); }

    bool CountFits(Rect2 r)
    {
        for (int i = 0; i < _rad.Length; i++) if (NodeBox(i).Intersects(r)) return false;
        for (int w = 0; w < 5; w++) if (WedgeLabel(w).rect.Intersects(r)) return false;
        for (int w = 0; w < 5; w++) if (_sumRects[w].Intersects(r)) return false;
        foreach (var o in _countRects) if (o.Intersects(r)) return false;
        return true;
    }

    /// <summary>Shot-run assertion (A9): rectangles of the drawn count tags, node labels and wedge names that touch a node mark or each other. Returns the first offender or null.</summary>
    public string ShotOverlap()
    {
        var boxes = new List<(string what, Rect2 r)>();
        foreach (var r in _countRects) boxes.Add(("count tag", r));
        foreach (var r in _labelRects) boxes.Add(("label", r));
        for (int w = 0; w < 5; w++) boxes.Add(("wedge summary", _sumRects[w]));
        for (int w = 0; w < 5; w++) boxes.Add(("wedge name", WedgeLabel(w).rect));
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
        var line = route ? Ui.Command : Ui.DiscLine[d];
        var type = nodes.Type[i];
        float r = NR;
        var lod = CurLod;
        if (st == St.Unavailable && !route && lod != Lod.Icons) _cv.DrawCircle(p, lod == Lod.Overview ? 2.4f : 3.2f, Ui.DiscLine[d].Lerp(Ui.Paper, 0.45f));
        else
        {
            bool mastered = _nv[i].Process is { Mastered: true };
            Color fill = st == St.Known ? (mastered ? line : Ui.DiscWash[d]) : Ui.Sheet;
            if (lod == Lod.Icons && st == St.Unavailable) fill = Ui.Paper;
            Shape(type, p, r, fill, line, st != St.Known, 1.7f);
            if (st == St.Unavailable && lod != Lod.Icons) _cv.DrawCircle(p, 1.8f, line);
            if (lod == Lod.Icons) Glyph(i, type, p, r, st == St.Known && mastered ? Colors.White : (st == St.Unavailable ? Ui.Ink3 : Ui.DiscText[d]));
        }
        if (i == _goal) Shape(type, p, r + 4, Colors.Transparent, Ui.Command, false, 1.6f);
        if (i == _selected) _cv.DrawArc(p, r + 6, 0, Mathf.Tau, 24, Ui.Ink, 2f, true);
        if (i == _root) _cv.DrawRect(new Rect2(p + new Vector2(-r - 2 - SettingsLogic.WhyNotMarkDu, -r - SettingsLogic.WhyNotMarkDu), new Vector2(SettingsLogic.WhyNotMarkDu, SettingsLogic.WhyNotMarkDu)), Ui.Alarm);
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
        var occupied = new List<Rect2>();
        float hr = NR + 3; for (int i = 0; i < nodes.Count; i++) occupied.Add(new Rect2(PosOf(i) - new Vector2(hr, hr), new Vector2(hr * 2, hr * 2)));
        occupied.Add(new Rect2(c - new Vector2(Unit * 0.7f, Unit * 0.7f), new Vector2(Unit * 1.4f, Unit * 1.4f)));
        for (int w = 0; w < 5; w++)   // wedge names
        {
            occupied.Add(WedgeLabel(w).rect.Grow(2)); occupied.Add(_sumRects[w].Grow(2));
        }
        occupied.AddRange(_countRects);   // the count tags of the cells are drawn first; labels keep off them
        // The leader line and the route are obstacles too, so that no label sits on them.
        var lp = CalloutPoints();
        for (int k = 0; k + 1 < lp.Length; k++) AddSegment(occupied, lp[k], lp[k + 1], 5f);
        for (int k = 0; k + 1 < _path.Count; k++) { var ch = Channel(_path[k], _path[k + 1]); for (int j = 0; j + 1 < ch.Count; j++) AddSegment(occupied, ch[j], ch[j + 1], 3f); }
        if (_labelText == null || _labelText.Length != nodes.Count) _labelText = new string[nodes.Count]; else Array.Clear(_labelText);
        _calloutLabeled = false;
        var order = new List<int>();
        void Add(int i) { if (i >= 0 && !order.Contains(i)) order.Add(i); }
        Add(_selected); Add(_goal); Add(_root);
        foreach (int i in Enumerable.Range(0, nodes.Count).Where(i => _route[i] && _state[i] != St.Unavailable).OrderBy(i => nodes.Number[i])) Add(i);
        foreach (int i in Enumerable.Range(0, nodes.Count).Where(i => _state[i] == St.Frontier).OrderBy(i => nodes.Number[i])) Add(i);
        if (CurLod == Lod.Icons) foreach (int i in Enumerable.Range(0, nodes.Count).OrderBy(i => nodes.Number[i])) Add(i);
        var area = Area;
        var placed = new Dictionary<int, (Rect2 pad, Rect2 rc, string text, FontKind kind, float h)>();
        bool Place(int i, string text, FontKind kind, bool far = false)
        {
            var p = PosOf(i); float o = NR + 6;
            var sz = Ui.FontOf(kind).GetStringSize(text, HorizontalAlignment.Left, -1, 14);
            // near places first, then farther ones (so that the goal and the selected node always find a free spot)
            var cands = new[]
            {
                new Rect2(p + new Vector2(o, -sz.Y * 0.5f), sz), new Rect2(p + new Vector2(-o - sz.X, -sz.Y * 0.5f), sz),
                new Rect2(p + new Vector2(-sz.X * 0.5f, o), sz), new Rect2(p + new Vector2(-sz.X * 0.5f, -o - sz.Y), sz),
                new Rect2(p + new Vector2(o + 2, o + 2), sz), new Rect2(p + new Vector2(-o - 2 - sz.X, o + 2), sz),
                new Rect2(p + new Vector2(o + 2, -o - 2 - sz.Y), sz), new Rect2(p + new Vector2(-o - 2 - sz.X, -o - 2 - sz.Y), sz),
            };
            bool Try(Rect2 rc)
            {
                var pad = rc.Grow(2);
                if (!area.Encloses(pad) || occupied.Any(o => o.Intersects(pad))) return false;
                occupied.Add(pad);
                placed[i] = (pad, rc, text, kind, sz.Y);
                return true;
            }
            foreach (var rc in cands) if (Try(rc)) return true;
            if (!far) return false;
            // The goal, the selected node and the root must be named: farther places on three rings (computed in place); the leader line ties the name to the node.
            foreach (float ring in FarRings)
                for (int k = 0; k < 8; k++)
                {
                    var dv = new Vector2(Mathf.Cos(k * Mathf.Tau / 8), Mathf.Sin(k * Mathf.Tau / 8)) * (o + ring);
                    if (Try(new Rect2(p + dv - new Vector2(dv.X < -1 ? sz.X : dv.X > 1 ? 0 : sz.X * 0.5f, dv.Y < -1 ? sz.Y : dv.Y > 1 ? 0 : sz.Y * 0.5f), sz))) return true;
                }
            return false;
        }
        // Pass 1: the number tag of every frontier node (short, same in both languages); pass 2: names where there is room (a name replaces its own tag).
        foreach (int i in order) if (_state[i] == St.Frontier) Place(i, "№" + nodes.Number[i], FontKind.Mono);
        foreach (int i in order)
        {
            if (!(_route[i] || i == _selected || i == _goal || i == _root || CurLod == Lod.Icons || (CurLod == Lod.Nodes && _state[i] == St.Frontier))) continue;   // the «nodes» level names the frontier
            if (placed.TryGetValue(i, out var tag)) occupied.Remove(tag.pad);
            if (Place(i, Names.Node(nodes.Id[i]), FontKind.Text, NamedNode(i))) continue;
            if (tag.text != null) { occupied.Add(tag.pad); placed[i] = tag; }
            else Place(i, "№" + nodes.Number[i], FontKind.Mono);
        }
        foreach (int i in order)
        {
            if (!placed.TryGetValue(i, out var pl)) continue;
            _labelRects.Add(pl.pad);
            _labelText[i] = pl.text;
            bool callout = i == (_selected >= 0 ? _selected : _goal);
            if (callout) _calloutLabeled = true;
            if (callout || LabelNeedsLeader(i, pl.rc))
            {
                var own = PosOf(i); var near = own.Clamp(pl.pad.Position, pl.pad.End); var dir = (near - own).Normalized();
                _cv.DrawLine(own + dir * NR, near, _route[i] ? Ui.Command : Ui.Ink2, 1.5f, true);   // ends at the plate: the goal or selected node, or a node with a neighbour as near as it
            }
            _cv.DrawRect(pl.pad, Ui.Paper);
            var at = new Vector2(pl.rc.Position.X, pl.rc.Position.Y + pl.h * 0.8f);
            _cv.DrawStringOutline(Ui.FontOf(pl.kind), at, pl.text, HorizontalAlignment.Left, -1, 14, 5, Ui.Paper);   // paper halo keeps text off rings and lines
            Text(at, pl.text, 14, _route[i] ? Ui.Command : Ui.Ink2, pl.kind);
        }
    }

    static readonly float[] FarRings = { 28, 60, 100 };
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

    // Short callout with a shelf (§7.3) from the selected node, else from the goal; it does not run to the inset.
    Vector2[] CalloutPoints()
    {
        int i = _selected >= 0 ? _selected : _goal;
        if (i < 0) return Array.Empty<Vector2>();
        var p = PosOf(i);
        float y = Math.Max(Area.Position.Y + 8, p.Y - 30);
        return new[] { p + new Vector2(0, -NR - 2), new Vector2(p.X + 24, y), new Vector2(Math.Min(p.X + 24 + 44, Area.End.X + 6), y) };
    }

    string[] _labelText; bool _calloutLabeled;
    public string ShotLabelText(int row) => _labelText != null && row >= 0 && row < _labelText.Length ? _labelText[row] : null;
    public int RowOfId(string id) { var n = _host.Data.Nodes; for (int i = 0; i < n.Count; i++) if (n.Id[i] == id) return i; return -1; }

    void DrawCallout()
    {
        if (_calloutLabeled) return;   // the leader to the name plate replaces the shelf
        var pts = CalloutPoints();
        if (pts.Length == 0) return;
        _cv.DrawPolyline(pts, Ui.Ink, 1.2f, true);
        _cv.DrawCircle(pts[2], 3f, Ui.Ink);
    }

    // Legend (spec §8): a collapsed button at the lower left (key L), opened to a compact two-column sheet docked in the right column above the stamp.
    bool _legendOpen;
    /// <summary>The legend is on screen only in the chart view (mode 0); in the goal statement the flag is kept but the sheet reserves no space and takes no input.</summary>
    public bool LegendOpen => _legendOpen && _mode == 0;
    public bool CanLegend => _mode == 0;
    public void ToggleLegend(bool open) { _legendOpen = open; ClampPan(); Redraw(); }
    const float LegendRow = 21, LegendPad = 12, LegendCol = 229, LegendGap = 16, LegendH = 256, StampH = 62, StampGap = 8;
    /// <summary>The open legend sheet: right of the disc area, 8 du above the sheet stamp (spec §3).</summary>
    public Rect2 LegendSheet { get { float x = Size.X * 0.6f + 8; return new Rect2(x, Size.Y - 6 - StampH - StampGap - LegendH, Math.Min(500, Size.X - x - 6), LegendH); } }
    Rect2 StampRect => new(Size.X - 6 - 360, Size.Y - 6 - StampH, 360, StampH);

    // Cached legend content: localized texts (rebuilt when the language changes), colours and closed node outlines (built once).
    bool _lgDirty = true;
    readonly string[] _lgText = new string[19];
    static readonly string[] LgNum = { "1", "2", "3", "4", "5", "6" };
    readonly LegendData.Discipline[] _lgDiscs = LegendData.Disciplines(Palette.C);
    Color[] _lgWash, _lgLine;
    GlyphSet[] _lgPrims;

    void BuildLegendCache()
    {
        _lgDirty = false;
        string[] keys = { "legend.disc", "", "", "", "", "", "legend.lines", "legend.route", "legend.root", "legend.frontier", "legend.selected", "legend.glyphs", "legend.close" };
        for (int k = 0; k < keys.Length; k++) if (keys[k] != "") _lgText[k] = Loc.T(keys[k]);
        for (int k = 0; k < 5; k++) _lgText[1 + k] = Loc.T(_lgDiscs[k].NameKey);
        for (int k = 0; k < 6; k++) _lgText[13 + k] = Loc.T(LegendData.Types[k].NameKey);
        if (_lgPrims != null) return;
        _lgWash = new Color[5]; _lgLine = new Color[5];
        for (int k = 0; k < 5; k++) { _lgWash[k] = new Color(_lgDiscs[k].WashHex); _lgLine[k] = new Color(_lgDiscs[k].LineHex); }
        _lgPrims = new GlyphSet[6];
        for (int k = 0; k < 6; k++) _lgPrims[k] = new GlyphSet(GlyphPrims.LegendSwatch(LegendData.Types[k].Code));
    }

    static Rect2 LegendMarkRect(float xl, float rowY) { var (x, y, s) = SettingsLogic.WhyNotLegendMark(xl, rowY); return new Rect2(x, y, s, s); }

    void DrawLegend()
    {
        if (!LegendOpen) return;   // closed: the real button _legendBtn stands in its place
        if (_lgDirty || _lgPrims == null) BuildLegendCache();
        var r = LegendSheet;
        _cv.DrawStyleBox(Ui.Box(Ui.Sheet, Ui.Ink, 1, 0, Ui.R3), r);
        float xl = r.Position.X + LegendPad, xr = xl + LegendCol + LegendGap;
        float Y(int row) => r.Position.Y + LegendPad + 15 + row * LegendRow;
        void Head(float x, int row, int t) => Text(new Vector2(x, Y(row)), _lgText[t], 14, Ui.Ink2, FontKind.Draft);
        // left column: disciplines, then lines
        Head(xl, 0, 0);
        for (int k = 0; k < 5; k++)
        {
            var mk = new Vector2(xl + 8, Y(1 + k) - 5);
            _cv.DrawCircle(mk, 8, _lgWash[k]); _cv.DrawArc(mk, 8, 0, Mathf.Tau, 24, _lgLine[k], 1.8f, true);
            Text(new Vector2(xl + 26, Y(1 + k)), _lgText[1 + k], 14, Ui.Ink2);
        }
        const int ly = 6;
        Head(xl, ly, 6);
        float y = Y(ly + 1); _cv.DrawLine(new Vector2(xl, y - 5), new Vector2(xl + 18, y - 5), Ui.Sheet, 6f); _cv.DrawLine(new Vector2(xl, y - 5), new Vector2(xl + 18, y - 5), Ui.Command, 3f);
        Text(new Vector2(xl + 26, y), _lgText[7], 14, Ui.Ink2);
        y = Y(ly + 2); _cv.DrawRect(LegendMarkRect(xl, y), Ui.Alarm);
        Text(new Vector2(xl + 26, y), _lgText[8], 14, Ui.Ink2);
        y = Y(ly + 3); for (int k = 0; k < 6; k++) _cv.DrawArc(new Vector2(xl + 9, y - 5), 8, k * Mathf.Tau / 6, k * Mathf.Tau / 6 + Mathf.Tau / 6 * 0.55f, 5, Ui.Ink2, 1.8f, true);
        Text(new Vector2(xl + 26, y), _lgText[9], 14, Ui.Ink2);
        y = Y(ly + 4); _cv.DrawArc(new Vector2(xl + 9, y - 5), 5, 0, Mathf.Tau, 16, Ui.Ink, 1.5f, true); _cv.DrawArc(new Vector2(xl + 9, y - 5), 9, 0, Mathf.Tau, 20, Ui.Ink, 1.5f, true);
        Text(new Vector2(xl + 26, y), _lgText[10], 14, Ui.Ink2);
        // right column: node types with the ring number first, then the close hint
        Head(xr, 0, 11);
        for (int k = 0; k < 6; k++)
        {
            y = Y(1 + k);
            var mk = new Vector2(xr + 30, y - 5);
            Text(new Vector2(xr + 4, y), LgNum[k], 14, Ui.Ink, FontKind.Mono);
            GlyphDraw.Draw(_cv, _lgPrims[k], mk, Ui.Ink2);
            Text(new Vector2(xr + 46, y), LegendData.Types[k].Code, 14, Ui.Ink, FontKind.Mono);
            Text(new Vector2(xr + 92, y), _lgText[13 + k], 14, Ui.Ink2);
        }
        Text(new Vector2(xr, Y(8)), _lgText[12], 14, Ui.Ink3);
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
        for (int w = 0; w < 5; w++)
        {
            var r = _sumRects[w];
            if (_sumText[w] == null || r.Size.X <= 0) return $"summary {w} not drawn";
            bool inside = Area.Encloses(r) && Area.Encloses(WedgeLabel(w).rect);
            if (inside) inView++;
            else if (baseView) return $"summary {w} {r} is not fully inside the chart area {Area}";
            else continue;
            if (baseView) foreach (var (n, o) in others) if (o.Intersects(r) || o.Intersects(WedgeLabel(w).rect)) return $"summary {w} {r} touches {n} {o}";
            for (int v = 0; v < 5; v++) { if (v != w && _sumRects[v].Intersects(r)) return $"summary {w} touches summary {v}"; if (WedgeLabel(v).rect.Intersects(r)) return $"summary {w} touches wedge name {v}"; }
            for (int i = 0; i < _rad.Length; i++) if (NodeBox(i).Intersects(r)) return $"summary {w} touches node {i}";
            foreach (var l in _labelRects) if (l.Intersects(r)) return $"summary {w} touches a node label";
        }
        if (_legendBtn.Visible)
        {
            var b = new Rect2(_legendBtn.Position, _legendBtn.Size);
            if (b.Intersects(StampRect)) return "legend button touches the stamp";
            for (int w = 0; w < 5; w++) if (b.Intersects(_sumRects[w]) || b.Intersects(WedgeLabel(w).rect)) return $"legend button touches wedge {w}";
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
            Text(new Vector2(x0, ty), Fit(Names.Node(nodes.Id[row]), nameW - 56, 14), 14, col);
            Text(new Vector2(x0 + nameW - 50, ty), "№" + nodes.Number[row], 14, Ui.Ink2, FontKind.Mono, HorizontalAlignment.Right, 46);
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
        for (int g = 0; g <= 4; g++) Text(new Vector2(x0 + 200 + g * 108 + 4, y), "G" + g, 14, Ui.Ink2, FontKind.Mono);
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
        if (_mode == 0 && e is InputEventMouseButton { Pressed: true } w && Area.HasPoint(w.Position) && w.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            _zoomTarget = PlanisphereMath.ClampZoom(_zoomTarget * (w.ButtonIndex == MouseButton.WheelUp ? 1.25f : 0.8f));
            _anchor = w.Position;
            AcceptEvent();
        }
        else if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            if (mb.Pressed) { _press = _mode == 0 && Area.HasPoint(mb.Position); _dragged = false; _pressPos = mb.Position; if (_mode == 1) { int h = HitAt(mb.Position, 14f); if (h >= 0) Select(h); } }
            else
            {
                if (_press && !_dragged) { int hit = HitAt(mb.Position, 14f); if (hit >= 0) Select(hit); }
                _press = false;
            }
        }
        else if (e is InputEventMouseMotion mm && _press)
        {
            if (!_dragged && (mm.Position - _pressPos).Length() < 5f) return;
            _dragged = true;
            _pan += mm.Relative; ClampPan(); Redraw();
        }
    }

    // Hover shows the node name and number (ids never appear in the visible text).
    public override string _GetTooltip(Vector2 atPosition)
    {
        int hit = HitAt(atPosition, 12f);
        if (hit < 0) return "";
        var nodes = _host.Data.Nodes;
        return $"{Names.Node(nodes.Id[hit])} · №{nodes.Number[hit]}";   // the localized name, never the raw id
    }

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
            var nm = Ui.Lbl(Names.Node(nodes.Id[i]), 18); nm.TooltipText = $"{nodes.Id[i]} · №{nodes.Number[i]}"; nm.MouseFilter = MouseFilterEnum.Pass;
            _inset.AddChild(nm);
            _inset.AddChild(Ui.Wrap($"№{nodes.Number[i]} · {Loc.T("ntype." + (int)nodes.Type[i])} · G{nodes.Gen[i]} · {Loc.T("disc." + (int)nodes.Discipline[i])} · {StateWord(i)}", 14, Ui.Ink2));
            string anchor = Names.NodeAnchor(nodes.Id[i]), before = nodes.AnchorBefore[i] ? Loc.T("inset.before") : "";
            _inset.AddChild(Ui.Wrap(anchor == "" ? Loc.T("inset.anchor.year", nodes.AnchorYear[i], before) : Loc.T("inset.anchor", nodes.AnchorYear[i], before, anchor), 14, Ui.Ink3));
            BuildCard(i);
            var rq = nodes.Requires;
            var req = new List<string>();
            for (int g = rq.GroupStart[i]; g < rq.GroupStart[i + 1]; g++)
            {
                var mem = rq.GroupMembers(g).ToArray();
                string one = string.Join(" | ", mem.Select(m => (_state[m] == St.Known ? "✓ " : "✗ ") + Names.Node(nodes.Id[m])));
                req.Add(mem.Length > 1 ? "(" + one + ")" : one);
            }
            _inset.AddChild(Block(Ui.ReqLine, Ui.ReqWash, Ui.ReqText, "card.requires", (req.Count == 0 ? new List<string> { "—" } : req).Select(r => (Control)Ui.Wrap(r, 14, Ui.Ink)).ToArray()));
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
        _inset.AddChild(Ui.Btn((whyOpen ? "▾ " : "▸ ") + Loc.T("inset.why"), () => { _whyOpen = !_whyOpen; _insetDirty = true; }));
        if (whyOpen) BuildWhy(s, _host.Data);

        Sec("inset.focus");
        _inset.AddChild(Ui.Wrap(_focus.Count == 0 ? Loc.T("inset.focus.empty") : string.Join(" → ", _focus.Select(f => Names.Node(_host.Data, f))), 14, _focus.Count == 0 ? Ui.Ink2 : Ui.Ink));
        var send = Ui.Btn("inset.focus.send", () => { if (_actor == Player) _host.Submit(new SetEffortFocus(_actor, ImmutableArray.CreateRange(_focus)), "cmd.focus"); }, 14, _focus.Count > 0);
        send.Disabled = _focus.Count == 0 || _actor != Player;
        _inset.AddChild(send);
        if (s.Last.Status != CmdStatus.None)
        {
            string what = Loc.T(s.Last.LabelKey, s.Last.LabelArg);
            string why = s.Last.Status == CmdStatus.Rejected ? Loc.TOr("refusal." + s.Last.Reason, s.Last.Reason) : s.Last.Status == CmdStatus.Error ? s.Last.Reason : "";
            var lab = Ui.Wrap(Loc.T("cmd." + s.Last.Status, what, why), 14, Ui.Ink2);
            _inset.AddChild(s.Last.Status is CmdStatus.Rejected or CmdStatus.Error ? Ui.MarkedRow(Ui.Alarm, lab) : lab);
        }

        _inset.AddChild(new HSeparator());
        var hd = new HBoxContainer(); hd.AddThemeConstantOverride("separation", 10);
        hd.AddChild(Ui.Head("inset.prio")); hd.AddChild(Ui.Mock());
        _inset.AddChild(hd);
        foreach (int i in Enumerable.Range(0, nodes.Count).Where(i => _state[i] == St.Frontier).OrderByDescending(i => _route[i]).ThenBy(i => nodes.Number[i]).Take(7))
        {
            int row = i;
            var r = new HBoxContainer(); r.AddThemeConstantOverride("separation", 8);
            r.AddChild(Ui.Num("№" + nodes.Number[i], 14, Ui.Ink3, 44));
            var nm = Ui.Lbl(Names.Node(nodes.Id[i]), 14, _route[i] ? Ui.Command : Ui.Ink);
            nm.CustomMinimumSize = new Vector2(250, 0); nm.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; nm.TooltipText = $"{nodes.Id[i]} · №{nodes.Number[i]}"; nm.MouseFilter = MouseFilterEnum.Pass;
            r.AddChild(nm);
            var seg = new Segment(Level, _prio.TryGetValue(row, out int lv) ? lv : 1);
            seg.Changed += k => _prio[row] = k;
            r.AddChild(seg);
            _inset.AddChild(r);
        }
        _inset.AddChild(Ui.Wrap("inset.prio.note", 14, Ui.Ink3));
    }

    bool _whyOpen;

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
                string text = $"{Loc.TOr("cause." + l.Cause, l.Cause.ToString())}: {Names.Node(data, l.SubjectId)}" + (l.EtaMonths is int m ? Loc.T("inset.eta", m) : "");
                var lab = Ui.Wrap(text);
                _inset.AddChild(n++ == 0 ? Ui.MarkedRow(Ui.Alarm, lab) : lab);
            }
            if (_root >= 0 && _goal >= 0) _inset.AddChild(Ui.Wrap("inset.hint", 14, Ui.Ink3));
        }

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

    PanelContainer Block(Color line, Color wash, Color text, string headKey, params Control[] body)
    {
        var box = new PanelContainer(); box.AddThemeStyleboxOverride("panel", Plate(wash, line, 4));
        var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 3);
        v.AddChild(Ui.Lbl(headKey, 14, text, FontKind.Draft));
        foreach (var c in body) v.AddChild(c);
        box.AddChild(v);
        return box;
    }

    // Card of the selected node: schematic, what it affects, which parameter it raises, what it opens next. Colours: palette C inks.
    void BuildCard(int i)
    {
        var nodes = _host.Data.Nodes;
        int t = (int)nodes.Type[i], d = (int)nodes.Discipline[i];
        // 1. schematic: the picture, or a framed placeholder with the type label until the drawing exists
        var frame = new PanelContainer { CustomMinimumSize = new Vector2(0, 120) };
        var tex = Schematic(i);
        frame.AddThemeStyleboxOverride("panel", tex != null ? Plate(Ui.Sheet, Ui.Rule, 1) : Plate(Ui.DiscWash[d], Ui.DiscLine[d], 1));
        if (tex != null) frame.AddChild(new TextureRect { Texture = tex, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(0, 130), Modulate = new Color(1, 1, 1, 0.6f) });
        else
        {
            var ph = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            var l1 = Ui.Lbl(Loc.T("ntype." + t) + " · " + Loc.T("disc." + d), 18, Ui.DiscText[d], FontKind.Draft); l1.HorizontalAlignment = HorizontalAlignment.Center;
            var l2 = Ui.Lbl("card.noschem", 14, Ui.DiscText[d]); l2.HorizontalAlignment = HorizontalAlignment.Center;
            ph.AddChild(l1); ph.AddChild(l2); frame.AddChild(ph);
        }
        _inset.AddChild(frame);

        // 2. affects: layer and system (real data: type, cluster, discipline)
        _inset.AddChild(Block(Ui.AffLine, Ui.AffWash, Ui.AffText, "card.affects",
            Ui.Wrap(Loc.T("card.layer", Loc.T("ntype." + t), t + 1), 14, Ui.Ink),
            Ui.Wrap(Loc.T("card.system", Loc.T("cluster." + (int)nodes.Cluster[i]), Loc.T("disc." + d)), 14, Ui.Ink)));

        // 3. parameter: real values where Core has them (process yield, capability tier); the gain itself has no data
        var pbody = new List<Control>();
        if (_nv[i].Process is { } pv) pbody.Add(Ui.Wrap(Loc.T("card.param.yield", Loc.N(pv.Yield.ToDouble())), 14, Ui.Ink));
        else if (nodes.Type[i] == NodeType.APP) pbody.Add(Ui.Wrap(Loc.T("card.param.app", nodes.Gen[i]), 14, Ui.Ink));
        else pbody.Add(Ui.Wrap(Loc.T("card.param.none"), 14, Ui.Ink2));
        if (nodes.Type[i] == NodeType.PRC)
            pbody.Add(Ui.Wrap(Loc.T("card.param.cap", Loc.N(nodes.ReqPrecision[i].ToDouble()), Loc.N(nodes.ReqPurity[i].ToDouble()), Loc.N(nodes.ReqQualityControl[i].ToDouble()), nodes.Tier[i]), 14, Ui.Ink2));
        var mock = new HBoxContainer(); mock.AddThemeConstantOverride("separation", 8);
        mock.AddChild(Ui.Mock()); mock.AddChild(Ui.Wrap("card.param.api", 14, Ui.Ink3));
        pbody.Add(mock);
        _inset.AddChild(Block(Ui.RaiseLine, Ui.RaiseWash, Ui.RaiseText, "card.raises", pbody.ToArray()));

        // 4. unlocks: the rows that list this node in requires
        var next = new List<Control>();
        var dep = _dep[i].OrderBy(x => nodes.Number[x]).ToList();
        foreach (int x in dep.Take(6))
            next.Add(Ui.Wrap((_state[x] == St.Known ? "● " : _state[x] == St.Frontier ? "◐ " : "○ ") + Names.Node(nodes.Id[x]) + $" · №{nodes.Number[x]}", 14, Ui.Ink));
        if (dep.Count > 6) next.Add(Ui.Wrap(Loc.T("card.more", dep.Count - 6), 14, Ui.Ink2));
        if (dep.Count == 0) next.Add(Ui.Wrap(Loc.T("card.end"), 14, Ui.Ink2));
        _inset.AddChild(Block(Ui.OpenLine, Ui.OpenWash, Ui.OpenText, "card.unlocks", next.ToArray()));
    }

    void Sec(string key) { _inset.AddChild(new HSeparator()); _inset.AddChild(Ui.Head(key)); }
}
