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

    void Invalidate() { _insetDirty = true; Redraw(); }
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
    public int GoalRow => _goal;
    public int FrontierRow { get { for (int i = 0; _state != null && i < _state.Length; i++) if (_state[i] == St.Frontier && _route[i]) return i; return -1; } }

    public void ShowFor(int actor, int mode)
    {
        _actor = actor; _modelDirty = true; _insetDirty = true; _mode = mode; _viewSwitch.Select(mode, false); _focus.Clear(); _selected = -1; Visible = true;
    }
    public void SetMode(int mode) { _mode = mode; _viewSwitch.Select(mode, false); Redraw(); }
    public void Select(int row) { _selected = row; _insetDirty = true; Redraw(); }

    // ---------------- model ----------------
    public override void _Process(double delta)
    {
        _insetScroll.CustomMinimumSize = new Vector2(0, Math.Min(_inset.GetCombinedMinimumSize().Y, Size.Y - 100));
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
        if (_modelDirty || !ReferenceEquals(s.View, _lastView))
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
    Vector2 BaseCentre => Area.Position + new Vector2(Area.Size.X * 0.5f, (Area.Size.Y - 76) * 0.5f + 4);
    Vector2 Centre => BaseCentre + _pan;
    float BaseUnit => (Math.Min(Area.Size.X, Area.Size.Y - 76) * 0.5f - 44f) / 6.5f;
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
        if (_mode == 1) DrawStatement();
    }

    // The planisphere is painted on the clipped view; coordinates stay in the parent's space.
    void PaintView()
    {
        if (_state == null || _mode != 0) return;
        _cv = _view;
        _view.DrawSetTransform(-Area.Position);
        DrawPlanisphere();
        _cv = this;
    }

    string Fit(string s, float maxW, int size, Font font = null)
    {
        font ??= Fonts.Text;
        if (font.GetStringSize(s, HorizontalAlignment.Left, -1, size).X <= maxW) return s;
        while (s.Length > 1 && font.GetStringSize(s + "…", HorizontalAlignment.Left, -1, size).X > maxW) s = s[..^1];
        return s + "…";
    }

    void Text(Vector2 p, string s, int size, Color c, FontKind kind = FontKind.Text, HorizontalAlignment al = HorizontalAlignment.Left, float w = -1)
        => _cv.DrawString(Ui.FontOf(kind), p, s, al, w, Math.Max(size, Ui.MinSize), c);

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
        float avail = cs > 0.35f ? Area.End.X - q.X - 4 : cs < -0.35f ? q.X - Area.Position.X - 4 : 220;
        string s = Fit(Loc.T("disc." + w), Math.Max(60, avail), 14, Fonts.DraftBold);
        var sz = Fonts.DraftBold.GetStringSize(s, HorizontalAlignment.Left, -1, 14);
        float x = cs > 0.35f ? q.X : cs < -0.35f ? q.X - sz.X : q.X - sz.X * 0.5f;
        float y = sn > 0.35f ? q.Y + 6 : sn < -0.35f ? q.Y - sz.Y - 2 : q.Y - sz.Y * 0.5f;
        return (new Rect2(x, y, sz), s);
    }

    void DrawPlanisphere()
    {
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
        DrawLegend();
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
            _cv.DrawLine(c + d * r, c + d * (r + (y % 50 == 0 ? 6f : 3f)), Ui.Ink, 1f, true);
        }
        float frac = (_date.Year + (_date.Month - 1) / 12f + (_date.Day - 1) / 365f - 1910f) / 190f;
        float h = Mathf.Tau * frac - Mathf.Pi / 2;
        _cv.DrawLine(c, c + new Vector2(Mathf.Cos(h), Mathf.Sin(h)) * (r + 4), Ui.Ink, 2f, true);
        _cv.DrawCircle(c, 2.5f, Ui.Ink);
    }

    // Node radius grows a little with the zoom; icon plates are larger.
    float NR => CurLod switch { Lod.Overview => NodeR, Lod.Nodes => NodeR * 1.25f, _ => 13f + Math.Min(6f, (_zoom - PlanisphereMath.IconsFrom) * 2f) };

    // Overview: every (layer, discipline) cell is a wash with «known/total»; zoomed in the wash fades into a quiet band.
    void DrawCells(Vector2 c, float u)
    {
        var nodes = _host.Data.Nodes;
        var tot = new int[6, 5]; var kn = new int[6, 5];
        for (int i = 0; i < nodes.Count; i++) { tot[(int)nodes.Type[i], (int)nodes.Discipline[i]]++; if (_state[i] == St.Known) kn[(int)nodes.Type[i], (int)nodes.Discipline[i]]++; }
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
                _cv.DrawRect(rc.Grow(2), new Color(Ui.Paper, 0.85f));
                Text(new Vector2(rc.Position.X, rc.Position.Y + sz.Y * 0.8f), txt, 14, Ui.DiscText[d], FontKind.Mono);
            }
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
            if (lod == Lod.Icons) Glyph(type, p, r * 0.52f, st == St.Known && mastered ? Colors.White : (st == St.Unavailable ? Ui.Ink3 : Ui.DiscText[d]));
        }
        if (i == _goal) Shape(type, p, r + 4, Colors.Transparent, Ui.Command, false, 1.6f);
        if (i == _selected) _cv.DrawArc(p, r + 6, 0, Mathf.Tau, 24, Ui.Ink, 2f, true);
        if (i == _root) _cv.DrawRect(new Rect2(p + new Vector2(-r - 12, -r - 10), new Vector2(10, 10)), Ui.Alarm);
    }

    // One simple glyph per node type, drawn in a plate of radius 2r (zoomed-in level of detail).
    void Glyph(NodeType t, Vector2 p, float r, Color c)
    {
        switch (t)
        {
            case NodeType.FND:        // foundation: three stacked bars
                for (int k = -1; k <= 1; k++) _cv.DrawLine(p + new Vector2(-r, k * r * 0.6f), p + new Vector2(r, k * r * 0.6f), c, 2f, true);
                break;
            case NodeType.PRN:        // principle: ring with a dot
                _cv.DrawArc(p, r, 0, Mathf.Tau, 20, c, 1.8f, true); _cv.DrawCircle(p, 1.8f, c);
                break;
            case NodeType.ENG:        // engineering: hexagon
            {
                var pts = new Vector2[7];
                for (int k = 0; k < 7; k++) pts[k] = p + new Vector2(Mathf.Cos(k * Mathf.Tau / 6), Mathf.Sin(k * Mathf.Tau / 6)) * r;
                _cv.DrawPolyline(pts, c, 1.8f, true);
                break;
            }
            case NodeType.CMP:        // component: a square with a cut corner
                _cv.DrawPolyline(new[] { p + new Vector2(-r, -r), p + new Vector2(r * 0.4f, -r), p + new Vector2(r, -r * 0.4f), p + new Vector2(r, r), p + new Vector2(-r, r), p + new Vector2(-r, -r) }, c, 1.8f, true);
                break;
            case NodeType.PRC:        // process: arrow
                _cv.DrawLine(p + new Vector2(-r, 0), p + new Vector2(r, 0), c, 1.8f, true);
                _cv.DrawPolyline(new[] { p + new Vector2(r * 0.3f, -r * 0.7f), p + new Vector2(r, 0), p + new Vector2(r * 0.3f, r * 0.7f) }, c, 1.8f, true);
                break;
            default:                  // product: a small solid diamond
                _cv.DrawColoredPolygon(new[] { p + new Vector2(0, -r), p + new Vector2(r, 0), p + new Vector2(0, r), p + new Vector2(-r, 0) }, c);
                break;
        }
    }

    void Shape(NodeType t, Vector2 p, float r, Color fill, Color line, bool dashed, float w)
    {
        if (t == NodeType.PRC || t == NodeType.APP)
        {
            Vector2[] pts = t == NodeType.PRC
                ? new[] { p + new Vector2(-r, -r), p + new Vector2(r, -r), p + new Vector2(r, r), p + new Vector2(-r, r) }
                : new[] { p + new Vector2(0, -r * 1.3f), p + new Vector2(r * 1.3f, 0), p + new Vector2(0, r * 1.3f), p + new Vector2(-r * 1.3f, 0) };
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
            occupied.Add(WedgeLabel(w).rect.Grow(2));
        }
        // The leader line and the route are obstacles too, so that no label sits on them.
        var lp = CalloutPoints();
        for (int k = 0; k + 1 < lp.Length; k++) AddSegment(occupied, lp[k], lp[k + 1], 5f);
        for (int k = 0; k + 1 < _path.Count; k++) { var ch = Channel(_path[k], _path[k + 1]); for (int j = 0; j + 1 < ch.Count; j++) AddSegment(occupied, ch[j], ch[j + 1], 3f); }
        var order = new List<int>();
        void Add(int i) { if (i >= 0 && !order.Contains(i)) order.Add(i); }
        Add(_selected); Add(_goal); Add(_root);
        foreach (int i in Enumerable.Range(0, nodes.Count).Where(i => _route[i] && _state[i] != St.Unavailable).OrderBy(i => nodes.Number[i])) Add(i);
        foreach (int i in Enumerable.Range(0, nodes.Count).Where(i => _state[i] == St.Frontier).OrderBy(i => nodes.Number[i])) Add(i);
        if (CurLod == Lod.Icons) foreach (int i in Enumerable.Range(0, nodes.Count).OrderBy(i => nodes.Number[i])) Add(i);
        var area = Area;
        var placed = new Dictionary<int, (Rect2 pad, Rect2 rc, string text, FontKind kind, float h)>();
        bool Place(int i, string text, FontKind kind)
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
            foreach (var rc in cands)
            {
                var pad = rc.Grow(2);
                if (!area.Encloses(pad) || occupied.Any(o => o.Intersects(pad))) continue;
                occupied.Add(pad);
                placed[i] = (pad, rc, text, kind, sz.Y);
                return true;
            }
            return false;
        }
        // Pass 1: the number tag of every frontier node (short, same in both languages); pass 2: names where there is room (a name replaces its own tag).
        foreach (int i in order) if (_state[i] == St.Frontier) Place(i, "№" + nodes.Number[i], FontKind.Mono);
        foreach (int i in order)
        {
            if (!(_route[i] || i == _selected || i == _goal || i == _root || CurLod == Lod.Icons)) continue;
            if (placed.TryGetValue(i, out var tag)) occupied.Remove(tag.pad);
            if (Place(i, Names.Node(nodes.Id[i]), FontKind.Text)) continue;
            if (tag.text != null) { occupied.Add(tag.pad); placed[i] = tag; }
            else Place(i, "№" + nodes.Number[i], FontKind.Mono);
        }
        foreach (int i in order)
        {
            if (!placed.TryGetValue(i, out var pl)) continue;
            _cv.DrawRect(pl.pad, new Color(Ui.Paper, 0.92f));
            var at = new Vector2(pl.rc.Position.X, pl.rc.Position.Y + pl.h * 0.8f);
            _cv.DrawStringOutline(Ui.FontOf(pl.kind), at, pl.text, HorizontalAlignment.Left, -1, 14, 5, Ui.Paper);   // paper halo keeps text off rings and lines
            Text(at, pl.text, 14, _route[i] ? Ui.Command : Ui.Ink2, pl.kind);
        }
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

    void DrawCallout()
    {
        var pts = CalloutPoints();
        if (pts.Length == 0) return;
        _cv.DrawPolyline(pts, Ui.Ink, 1.2f, true);
        _cv.DrawCircle(pts[2], 3f, Ui.Ink);
    }

    void DrawLegend()
    {
        float ly = Area.End.Y - 62;
        _cv.DrawRect(new Rect2(Area.Position.X + 6, ly - 16, Area.Size.X - 12, 78), new Color(Ui.Paper, 0.92f));
        Text(new Vector2(14, ly), Loc.T("legend.a1"), 14, Ui.Ink2);
        Text(new Vector2(14, ly + 20), Loc.T("legend.a2"), 14, Ui.Ink2);
        Text(new Vector2(14, ly + 40), Loc.T("legend.a3"), 14, Ui.Ink2);
    }

    // ---- B: statement of the goal ----
    void DrawStatement()
    {
        var nodes = _host.Data.Nodes; var rq = nodes.Requires;
        _rowRects.Clear();
        var a = Area; float x0 = a.Position.X + 14, y = a.Position.Y + 8;
        Text(new Vector2(x0, y + 16), Loc.T("stmt.title"), 18, Ui.Ink, FontKind.Draft);
        string goal = _goal >= 0 ? Loc.T("stmt.goal", Names.Node(nodes.Id[_goal]), Loc.T(_state[_goal] == St.Known ? "state.known" : "state.unknown")) : Loc.T("stmt.nogoal");
        Text(new Vector2(x0 + 210, y + 16), Fit(goal, a.Size.X - 240, 14), 14, Ui.Ink);
        y += 28;
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
        Text(new Vector2(x0, y + 12), Fit(Loc.T("stmt.scale"), a.Size.X - 28, 14), 14, Ui.Ink2);
        y += 22;
        for (int d = 0; d <= maxD; d++) Text(new Vector2(gx + d * cellW + 6, y + 12), d == 0 ? Loc.T("stmt.now") : "+" + d, 14, Ui.Ink2, FontKind.Mono);
        y += 20;
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
            if (group != last) { y += 4; Text(new Vector2(x0, y + 14), Loc.T(group), 14, Ui.Ink2, FontKind.Draft); y += 20; last = group; }
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
        Text(new Vector2(x0, y + 12), Loc.T("legend.b"), 14, Ui.Ink2);
        Text(new Vector2(x0, y + 34), Fit(Loc.T("legend.b2"), a.Size.X - 28, 14), 14, Ui.Ink2);
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

    // Hover shows the technical id and number: ids never appear in the visible text.
    public override string _GetTooltip(Vector2 atPosition)
    {
        int hit = HitAt(atPosition, 12f);
        if (hit < 0) return "";
        var nodes = _host.Data.Nodes;
        return $"{nodes.Id[hit]} · №{nodes.Number[hit]}";
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
