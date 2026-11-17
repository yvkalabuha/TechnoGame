using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Singularity.Core.Commands;
using Singularity.Core.Data;
using Singularity.Core.Products;
using Singularity.Core.WhyNot;
using Singularity.Session;

namespace Singularity.Client.Spike;

/// <summary>
/// Tech tree screen (TECH_TREE_SCREEN): A «Планисфера» and B «Ведомость цели» over real NodeView/requires data of the chosen power,
/// plus the right inset (why_not, node, priorities, effort focus). No per-node progress or ETA intervals exist in Session: those are marked «макет».
/// </summary>
public sealed partial class TechTree : Control
{
    enum St { Known, Frontier, Unavailable }

    readonly SimHost _host;
    readonly VBoxContainer _inset = new();
    readonly PanelContainer _insetBox = new();
    readonly Segment _viewSwitch;
    int _actor = -1, _mode; long _seq = -1; SliceView _lastView; string _lastVerdict;
    int _selected = -1;                              // node row
    St[] _state; bool[] _route; int _root = -1, _goal = -1;
    NodeView[] _nv; int[] _depth;
    readonly Dictionary<int, int> _rowOfNumber = new();
    readonly Dictionary<int, int> _prio = new();     // mock priorities, row -> 0..3
    readonly List<int> _focus = new();               // SetEffortFocus draft, process numbers
    Vector2[] _pos;
    public event Action Closed;

    static readonly string[] TypeName = { "FND знание", "PRN принцип", "ENG инженерия", "CMP компонент", "PRC процесс", "APP изделие" };
    static readonly string[] DiscName = { "Аэродинамика", "Силовые установки", "Металлы", "Химия", "Механика" };
    static readonly string[] Level = { "пауза", "норма", "выше", "фокус" };

    public TechTree(SimHost host)
    {
        _host = host;
        MouseFilter = MouseFilterEnum.Stop;

        var top = new HBoxContainer { Position = new Vector2(14, 8) };
        top.AddThemeConstantOverride("separation", 12);
        top.AddChild(Ui.Lbl("Карта ▸ Знание", 16));
        _viewSwitch = new Segment(new[] { "A · Планисфера", "B · Ведомость цели" }, 0, 13);
        _viewSwitch.Changed += i => { _mode = i; QueueRedraw(); };
        top.AddChild(_viewSwitch);
        top.AddChild(Ui.Btn("Закрыть ×", () => Closed?.Invoke(), 13));
        AddChild(top);

        _insetBox.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Sheet, Ui.Ink, 1, 12));
        _insetBox.SetAnchorsPreset(LayoutPreset.RightWide);
        _insetBox.AnchorLeft = 0.6f; _insetBox.OffsetLeft = 0; _insetBox.OffsetTop = 6; _insetBox.OffsetBottom = -6; _insetBox.OffsetRight = -6;
        var sc = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _inset.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _inset.AddThemeConstantOverride("separation", 5);
        sc.AddChild(_inset); _insetBox.AddChild(sc);
        AddChild(_insetBox);
    }

    public void ShowFor(int actor, int mode) { _actor = actor; _seq = -1; _mode = mode; _viewSwitch.Select(mode, false); _focus.Clear(); Visible = true; }
    public void SetMode(int mode) { _mode = mode; _viewSwitch.Select(mode, false); QueueRedraw(); }

    // ---------------- model ----------------
    public override void _Process(double delta)
    {
        var s = _host.Snap;
        if (s == null || _actor < 0 || !Visible) return;
        if (s.WhyActor != _actor) return;
        if (_seq == 0 && ReferenceEquals(s.View, _lastView) && s.LastVerdict == _lastVerdict) return;
        _seq = 0; _lastView = s.View; _lastVerdict = s.LastVerdict;
        Rebuild(s);
        BuildInset(s);
        QueueRedraw();
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
        _depth = new int[n];
        for (int i = 0; i < n; i++) _depth[i] = Depth(i, known, new Dictionary<int, int>());
        Layout();
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

    void Layout()
    {
        var nodes = _host.Data.Nodes;
        _pos = new Vector2[nodes.Count];
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
                float a = (-90f + kv.Key.Item2 * 72f + 72f * (0.08f + 0.84f * (j + 0.5f) / l.Count)) * Mathf.Pi / 180f;
                _pos[l[j]] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (kv.Key.Item1 + 1f + (j % 2) * 0.12f);   // unit: ring index
            }
        }
    }

    // ---------------- drawing ----------------
    Rect2 Area => new(0, 44, Size.X * 0.6f - 8, Size.Y - 52);

    public override void _Draw()
    {
        DrawStyleBox(Ui.Box(Ui.Paper, Ui.Ink, 2, 0), new Rect2(Vector2.Zero, Size));
        if (_state == null) return;
        if (_mode == 0) DrawPlanisphere(); else DrawStatement();
    }

    Vector2 Centre => Area.GetCenter();
    float Ring => Math.Min(Area.Size.X, Area.Size.Y) * 0.5f - 34f;
    Vector2 PosOf(int i) => Centre + _pos[i] * (Ring / 6.5f);

    void Text(Vector2 p, string s, int size, Color c, HorizontalAlignment al = HorizontalAlignment.Left, float w = -1)
        => DrawString(ThemeDB.FallbackFont, p, s, al, w, size, c);

    void DrawPlanisphere()
    {
        var c = Centre; float u = Ring / 6.5f;
        for (int r = 1; r <= 6; r++) DrawArc(c, r * u, 0, Mathf.Tau, 96, Ui.Rule, 1f, true);
        for (int w = 0; w < 5; w++)
        {
            float a = (-90f + w * 72f) * Mathf.Pi / 180f;
            var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            DrawLine(c + d * u * 0.6f, c + d * (6.55f * u), Ui.Rule, 1f);
            float m = (-90f + w * 72f + 36f) * Mathf.Pi / 180f;
            var q = c + new Vector2(Mathf.Cos(m), Mathf.Sin(m)) * (6.85f * u);
            Text(q + new Vector2(-70, 4), DiscName[w], 12, Ui.Ink2, HorizontalAlignment.Center, 140);
        }
        for (int r = 0; r < 6; r++) Text(c + new Vector2(3, -(r + 1) * u + 11), TypeName[r], 10, Ui.Ink3);
        Text(c + new Vector2(-60, 4), _host.Snap.Date.ToString(), 13, Ui.Ink, HorizontalAlignment.Center, 120);

        var nodes = _host.Data.Nodes; var rq = nodes.Requires;
        // edges: requires (all members; any_of draws every alternative)
        for (int i = 0; i < nodes.Count; i++)
            for (int g = rq.GroupStart[i]; g < rq.GroupStart[i + 1]; g++)
                foreach (int m in rq.GroupMembers(g))
                {
                    bool onRoute = _route[i] && (_route[m] || _state[m] == St.Known);
                    bool toSel = _selected >= 0 && (i == _selected || m == _selected);
                    if (onRoute && _route[m]) DrawLine(PosOf(m), PosOf(i), Ui.Command, 2f, true);
                    else if (toSel) DrawLine(PosOf(m), PosOf(i), Ui.Ink, 1.2f, true);
                    else DrawLine(PosOf(m), PosOf(i), new Color(Ui.Rule, 0.55f), 1f, true);
                }
        for (int i = 0; i < nodes.Count; i++) DrawNode(i);
        // labels: route, frontier, selected, goal
        for (int i = 0; i < nodes.Count; i++)
            if (_route[i] && _state[i] != St.Unavailable || _state[i] == St.Frontier || i == _selected || i == _goal || i == _root)
{
                var p = PosOf(i); var dir = (p - c).Normalized();
                bool right = dir.X >= 0;
            {
                bool named = _route[i] || i == _selected || i == _goal || i == _root;
                string label = named ? Names.Short(nodes.Id[i]).Replace("_", " ") : "№" + nodes.Number[i];
                Text(p + new Vector2(right ? 10 : -210, 4 + dir.Y * 6), label, 10, _route[i] ? Ui.Command : Ui.Ink2, right ? HorizontalAlignment.Left : HorizontalAlignment.Right, right ? -1 : 200);
            }
            }
        // legend
        float ly = Area.End.Y - 66;
        Text(new Vector2(14, ly), "○ знание   □ процесс   ◇ изделие", 11, Ui.Ink2);
        Text(new Vector2(14, ly + 15), "контур — известно · пунктир — фронтир (подпись №) · точка — недоступно · синий — маршрут (что мешает собрать цель) · ■ — корень «почему не»", 11, Ui.Ink2);
        Text(new Vector2(14, ly + 30), "доля прогресса узла фронтира — нет в Session (макет не рисуем)", 11, Ui.WarnText);
    }

    void DrawNode(int i)
    {
        var nodes = _host.Data.Nodes;
        var p = PosOf(i); var st = _state[i]; bool route = _route[i];
        var col = route ? Ui.Command : Ui.Ink;
        var type = nodes.Type[i];
        float r = 6.5f;
        if (st == St.Unavailable && !route) { DrawCircle(p, 2.2f, Ui.Ink3); }
        else
        {
            bool mastered = _nv[i].Process is { Mastered: true };
            Color fill = st == St.Known ? (mastered ? Ui.Ink : Ui.Sheet) : Ui.Paper;
            bool dashed = st != St.Known;
            Shape(type, p, r, fill, col, dashed && st == St.Frontier || route && st == St.Unavailable, 1.6f);
            if (st == St.Unavailable) DrawCircle(p, 1.8f, col);
        }
        if (i == _goal) Shape(type, p, r + 4, Colors.Transparent, Ui.Command, false, 1.4f);
        if (i == _selected) DrawArc(p, r + 8, 0, Mathf.Tau, 24, Ui.Ink, 2f, true);
        if (i == _root) DrawRect(new Rect2(p + new Vector2(-r - 11, -r - 9), new Vector2(9, 9)), Ui.Alarm);
    }

    void Shape(NodeType t, Vector2 p, float r, Color fill, Color line, bool dashed, float w)
    {
        Vector2[] pts;
        if (t == NodeType.PRC) pts = new[] { p + new Vector2(-r, -r), p + new Vector2(r, -r), p + new Vector2(r, r), p + new Vector2(-r, r) };
        else if (t == NodeType.APP) pts = new[] { p + new Vector2(0, -r * 1.3f), p + new Vector2(r * 1.3f, 0), p + new Vector2(0, r * 1.3f), p + new Vector2(-r * 1.3f, 0) };
        else
        {
            pts = new Vector2[16];
            for (int k = 0; k < 16; k++) { float a = k * Mathf.Tau / 16; pts[k] = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r; }
        }
        if (fill.A > 0) DrawColoredPolygon(pts, fill);
        for (int k = 0; k < pts.Length; k++)
        {
            var a = pts[k]; var b = pts[(k + 1) % pts.Length];
            if (dashed) DrawDashedLine(a, b, line, w, 3f, true); else DrawLine(a, b, line, w, true);
        }
    }

    // ---- B: statement of the goal ----
    void DrawStatement()
    {
        var nodes = _host.Data.Nodes; var rq = nodes.Requires;
        var a = Area; float x0 = a.Position.X + 14, y = a.Position.Y + 10;
        Text(new Vector2(x0, y + 12), "ВЕДОМОСТЬ ЦЕЛИ", 15, Ui.Ink);
        string goal = _goal >= 0 ? $"цель: {Names.Short(nodes.Id[_goal])} (изделие {(_state[_goal] == St.Known ? "известно" : "неизвестно")})" : "цели нет (нет известного изделия без серии)";
        Text(new Vector2(x0 + 190, y + 12), goal, 13, Ui.Command);
        y += 24;
        Text(new Vector2(x0, y + 10), "Горизонталь — ступень зависимости (число неизвестных звеньев до узла), а не время: интервалов сроков в Session нет (макет).", 11, Ui.WarnText);
        y += 22;
        float nameW = 210, stateW = 96, cellW = 54, rowH = 20;
        float gx = x0 + nameW + stateW;
        var rows = new List<(int row, string group)>();
        if (_goal >= 0) rows.Add((_goal, "ЦЕЛЬ"));
        foreach (var i in Enumerable.Range(0, nodes.Count).Where(i => _route[i] && i != _goal && _state[i] == St.Frontier).OrderBy(i => nodes.Number[i])) rows.Add((i, "ФРОНТИР НА ПУТИ"));
        foreach (var i in Enumerable.Range(0, nodes.Count).Where(i => _route[i] && i != _goal && _state[i] == St.Unavailable).OrderBy(i => _depth[i]).ThenBy(i => nodes.Number[i])) rows.Add((i, "ЖДУТ"));
        foreach (var i in Enumerable.Range(0, nodes.Count).Where(i => !_route[i] && _state[i] == St.Frontier).OrderBy(i => nodes.Number[i]).Take(8)) rows.Add((i, "ФРОНТИР ВНЕ ПУТИ"));
        int goalD = rows.Where(r => r.row != _goal).Select(r => _depth[r.row]).DefaultIfEmpty(-1).Max() + 1;
        if (_goal >= 0) _depth[_goal] = goalD;
        int maxD = rows.Count == 0 ? 0 : rows.Max(r => _depth[r.row]);
        for (int d = 0; d <= maxD; d++) Text(new Vector2(gx + d * cellW + 6, y + 12), d == 0 ? "сейчас" : "+" + d, 10, Ui.Ink3);
        y += 18;
        var yOf = new Dictionary<int, float>();
        string last = "";
        // longest chain: from the goal, follow the unknown requirement with the largest depth
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
            if (group != last) { y += 6; Text(new Vector2(x0, y + 11), group, 10, Ui.Ink3); y += 15; last = group; }
            yOf[row] = y;
            bool sel = row == _selected;
            if (sel) DrawRect(new Rect2(x0 - 4, y - 1, gx - x0 + (maxD + 1) * cellW + 8, rowH), Ui.Hover);
            var col = _route[row] ? Ui.Command : Ui.Ink;
            Text(new Vector2(x0, y + 14), Names.Short(nodes.Id[row]).Replace("_", " "), 12, col);
            Text(new Vector2(x0 + nameW, y + 14), StateWord(row), 11, Ui.Ink2);
            float bx = gx + _depth[row] * cellW;
            var rect = new Rect2(bx, y + 3, cellW - 10, rowH - 8);
            if (_state[row] == St.Frontier) { DrawRect(rect, new Color(col, 0.18f)); DrawRect(rect, col, false, 1.2f); }
            else DrawDashedRect(rect, col);
            y += rowH;
        }
        // precedence links: from the predecessor bar end to the successor row start
        foreach (var (row, _) in rows)
            for (int g = rq.GroupStart[row]; g < rq.GroupStart[row + 1]; g++)
                foreach (int m in rq.GroupMembers(g))
                    if (yOf.TryGetValue(m, out float my) && yOf.TryGetValue(row, out float ry))
                    {
                        var p1 = new Vector2(gx + _depth[m] * cellW + cellW - 10, my + 9); var p2 = new Vector2(gx + _depth[row] * cellW, ry + 9);
                        bool main = chain.Contains(m) && chain.Contains(row);
                        DrawPolyline(new[] { p1, new Vector2(p2.X - 4, p1.Y), new Vector2(p2.X - 4, p2.Y), p2 }, main ? Ui.Command : Ui.Rule, main ? 3f : 1f, true);
                    }
        y += 8;
        int nk = _state.Count(s2 => s2 == St.Known);
        Text(new Vector2(x0, y + 12), $"ИЗВЕСТНО: {nk} узлов (свёрнуто)", 11, Ui.Ink2);
        for (int g = 0, k = 0; g < nodes.Count; g++)
            if (_state[g] == St.Known) { DrawLine(new Vector2(x0 + 230 + k * 5, y + 4), new Vector2(x0 + 230 + k * 5, y + 14), Ui.Ink, 1f); k++; }
        y += 26;
        Text(new Vector2(x0, y + 12), "Толстая синяя связь — самая длинная цепочка от корня «почему не» (по ступеням).", 11, Ui.Command);
        y += 26;
        // overview: discipline x generation cells with one square per node
        Text(new Vector2(x0, y + 10), "ОБЗОР 61 УЗЛА: дисциплина × поколение", 11, Ui.Ink3);
        y += 30;
        for (int d = 0; d < 5; d++)
        {
            Text(new Vector2(x0, y + 12 + d * 20), DiscName[d], 11, Ui.Ink2);
            for (int g = 0; g <= 4; g++)
            {
                float cx = x0 + 150 + g * 110;
                DrawRect(new Rect2(cx, y + d * 20, 104, 18), Ui.Rule, false, 1f);
                int k = 0;
                for (int i = 0; i < nodes.Count; i++)
                    if ((int)nodes.Discipline[i] == d && nodes.Gen[i] == g)
                    {
                        var sq = new Rect2(cx + 3 + k * 10, y + d * 20 + 4, 8, 10);
                        if (_state[i] == St.Known) DrawRect(sq, Ui.Ink); else if (_state[i] == St.Frontier) DrawRect(sq, Ui.Ink, false, 1.2f); else DrawRect(new Rect2(sq.Position + new Vector2(3, 3), new Vector2(2, 2)), Ui.Ink3);
                        k++;
                    }
            }
        }
        for (int g = 0; g <= 4; g++) Text(new Vector2(x0 + 150 + g * 110 + 4, y - 2), "G" + g, 10, Ui.Ink3);
    }

    void DrawDashedRect(Rect2 r, Color c)
    {
        DrawDashedLine(r.Position, r.Position + new Vector2(r.Size.X, 0), c, 1.2f, 3f);
        DrawDashedLine(r.Position + new Vector2(0, r.Size.Y), r.End, c, 1.2f, 3f);
        DrawDashedLine(r.Position, r.Position + new Vector2(0, r.Size.Y), c, 1.2f, 3f);
        DrawDashedLine(r.Position + new Vector2(r.Size.X, 0), r.End, c, 1.2f, 3f);
    }

    string StateWord(int row) => _state[row] switch
    {
        St.Known => _nv[row].ByLicence ? "по лицензии" : "известно",
        St.Frontier => "фронтир",
        _ => "недоступно",
    };

    // ---------------- input ----------------
    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb || _state == null) return;
        int hit = -1; float best = 14f;
        if (_mode == 0)
        {
            for (int i = 0; i < _pos.Length; i++) { float d = PosOf(i).DistanceTo(mb.Position); if (d < best) { best = d; hit = i; } }
        }
        else hit = HitRow(mb.Position);
        if (hit >= 0) { _selected = hit; _seq = -1; QueueRedraw(); }
    }

    int HitRow(Vector2 p) => -1; // B rows are selected through the inset list; row hit-testing is not wired in the prototype

    // ---------------- inset ----------------
    void BuildInset(HostSnapshot s)
    {
        foreach (var c in _inset.GetChildren()) { _inset.RemoveChild(c); c.QueueFree(); }
        var nodes = _host.Data.Nodes;
        string id = _host.Layout.ActorIds[_actor];
        _inset.AddChild(Ui.Lbl($"{id} · знание", 17));
        Sec("ПОЧЕМУ НЕ (WhyNot)");
        if (s.Target is not { } t) _inset.AddChild(Wrap("Цели нет.", Ui.Ink2));
        else
        {
            _inset.AddChild(Wrap($"Цель: {(_goal >= 0 ? Names.Short(nodes.Id[_goal]) : t.Kind + " " + t.Id)} · от корня «почему не» ступеней зависимости: {(_root >= 0 ? _depth[_root] : 0)}", Ui.Command));
            int n = 0;
            foreach (var l in s.Links.Where(l => l.Layer == WhyNotLayer.Knowledge).Take(4))
                _inset.AddChild(Wrap($"{(n++ == 0 ? "■" : "▫")} {l.Cause}: {Names.Node(_host.Data, l.SubjectId)}" + (l.EtaMonths is int m ? $" · срок {m} мес (скаляр, не интервал)" : ""), n == 1 ? Ui.Alarm : Ui.Ink));
            if (_root >= 0 && _goal >= 0) _inset.AddChild(Wrap("Первое звено — ближайшее по цене; дату цели задаёт самая длинная цепочка (вид B).", Ui.Ink3, 11));
        }
        Sec("УЗЕЛ");
        if (_selected < 0) _inset.AddChild(Wrap("Нажмите узел на планисфере.", Ui.Ink2));
        else
        {
            int i = _selected;
            _inset.AddChild(Wrap($"{nodes.Id[i]} · №{nodes.Number[i]}", Ui.Ink));
            _inset.AddChild(Wrap($"{nodes.Type[i]} · G{nodes.Gen[i]} · {DiscName[(int)nodes.Discipline[i]]} · {StateWord(i)}", Ui.Ink2));
            _inset.AddChild(Wrap($"Якорь: {nodes.AnchorYear[i]}{(nodes.AnchorBefore[i] ? " (до)" : "")} · {nodes.AnchorEvent[i]}", Ui.Ink3, 11));
            var rq = nodes.Requires;
            var req = new List<string>();
            for (int g = rq.GroupStart[i]; g < rq.GroupStart[i + 1]; g++)
            {
                var mem = rq.GroupMembers(g).ToArray();
                string one = string.Join(" | ", mem.Select(m => (_state[m] == St.Known ? "✓ " : "✗ ") + Names.Short(nodes.Id[m])));
                req.Add(mem.Length > 1 ? "(" + one + ")" : one);
            }
            _inset.AddChild(Wrap("Требует: " + (req.Count == 0 ? "—" : string.Join("; ", req)), Ui.Ink, 12));
            if (_nv[i].Process is { } pv)
                _inset.AddChild(Wrap($"Процесс: выход {pv.Yield.ToDouble():F2} · освоен {(pv.Mastered ? "да" : "нет")} · допуск {(pv.Admitted ? "да" : "нет")}", Ui.Ink2, 12));
            if (nodes.Type[i] == NodeType.PRC && _state[i] == St.Known)
            {
                int num = nodes.Number[i];
                bool inF = _focus.Contains(num);
                _inset.AddChild(Ui.Btn(inF ? "Убрать из фокуса освоения" : "В фокус освоения", () =>
                {
                    if (inF) _focus.Remove(num); else if (_focus.Count < 3) _focus.Add(num);
                    _seq = -1;
                }, 12));
            }
        }
        Sec("ФОКУС ОСВОЕНИЯ — SetEffortFocus (реально; до 3 известных процессов)");
        _inset.AddChild(Wrap(_focus.Count == 0 ? "Черновик пуст. Выберите известный процесс (□)." : string.Join(" → ", _focus.Select(f => Names.Node(_host.Data, f))), Ui.Ink));
        var send = Ui.Btn("Отдать приказ: фокус", () => _host.Submit(new SetEffortFocus(_actor, _focus.ToImmutableArrayCompat()), "фокус освоения"), 13);
        _inset.AddChild(send);
        if (s.LastVerdict != "") _inset.AddChild(Wrap(s.LastVerdict, Ui.Command, 11));
        var hd = new HBoxContainer(); hd.AddChild(Ui.Lbl("ПРИОРИТЕТЫ НИОКР по фронтиру ", 11, Ui.Ink3)); hd.AddChild(Ui.Mock("макет: рычага research_priority в Session нет"));
        _inset.AddChild(new HSeparator()); _inset.AddChild(hd);
        foreach (int i in Enumerable.Range(0, nodes.Count).Where(i => _state[i] == St.Frontier).OrderByDescending(i => _route[i]).ThenBy(i => nodes.Number[i]).Take(7))
        {
            int row = i;
            var r = new HBoxContainer();
            var nm = Ui.Lbl(Names.Short(nodes.Id[i]).Replace("_", " "), 12, _route[i] ? Ui.Command : Ui.Ink); nm.CustomMinimumSize = new Vector2(190, 0);
            r.AddChild(nm);
            var seg = new Segment(Level, _prio.TryGetValue(row, out int lv) ? lv : 1, 11);
            seg.Changed += k => _prio[row] = k;
            r.AddChild(seg);
            _inset.AddChild(r);
        }
        _inset.AddChild(Wrap("Приоритеты ни к чему не подключены; сроки (интервал P10–P90), доли прогресса и «отставание от чужого знания» — тоже макет/нет данных.", Ui.WarnText, 11));
    }

    void Sec(string t) { _inset.AddChild(new HSeparator()); _inset.AddChild(Ui.Lbl(t, 11, Ui.Ink3)); }

    static Label Wrap(string t, Color c, int size = 13)
    {
        return Ui.Wrap(t, size, c);
    }
}

static class ListExt
{
    public static System.Collections.Immutable.ImmutableArray<int> ToImmutableArrayCompat(this List<int> l) => System.Collections.Immutable.ImmutableArray.CreateRange(l);
}
