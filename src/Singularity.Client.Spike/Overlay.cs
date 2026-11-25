using System.Collections.Generic;
using Godot;

namespace Singularity.Client.Spike;

/// <summary>2D layer over the 3D scene: limb line, rim ticks, country labels (map face, localized) with hysteresis and overlap rejection.
/// Labels of the slice countries are bold with an ink underline.</summary>
public sealed partial class Overlay : Control
{
    sealed class LabelState { public Country C; public Label Node; public bool On; public bool Slice, Emph, Callout; public Rect2 Rect; public float Area; public Vector2 Anchor; public int Size = 14; public float TextW; }

    static readonly Color Ink = Ui.Ink;
    static readonly Color Paper = Ui.Paper;
    static readonly Color Limb = new Color(Palette.Limb, 0.7f);

    /// <summary>Alert plate on the globe (#326): sheet halo 3 px, ink outline 1 px, sign ▲ (warn) or ■ (alarm) and a word.</summary>
    sealed record Marker(string Key, Vector2 LonLat, bool Warn, string TextKey, int Slot);
    readonly List<Marker> _markers = new();
    readonly List<LabelState> _labels = new();

    /// <summary>The plate hangs up and to the right of the country point on a leader line with a dot at the point; slot stacks several plates.</summary>
    public void SetMarker(string key, Vector2 lonLat, bool warn, string textKey, int slot = 0)
    {
        _markers.RemoveAll(m => m.Key == key);
        _markers.Add(new Marker(key, lonLat, warn, textKey, slot));
    }

    public void RemoveMarker(string key) => _markers.RemoveAll(m => m.Key == key);

    void DrawMarkers()
    {
        foreach (var m in _markers)
        {
            var p = GlobeData.ToSphere(m.LonLat.X, m.LonLat.Y);
            if (CosTo(p) < 0.2f) continue;
            string text = (m.Warn ? "▲ " : "■ ") + Loc.T(m.TextKey);
            var sz = Fonts.Text.GetStringSize(text, HorizontalAlignment.Left, -1, 14) + new Vector2(16, 8);
            var anchor = _cam.UnprojectPosition(p);
            var rect = new Rect2(anchor + new Vector2(16, -30 - m.Slot * (sz.Y + 8) - sz.Y * 0.5f), sz);
            var from = anchor; var to = new Vector2(rect.Position.X, rect.GetCenter().Y);
            DrawLine(from, to, Ui.Sheet, 4f, true); DrawLine(from, to, Ui.Ink, 1.5f, true);
            DrawCircle(anchor, 4.5f, Ui.Paper); DrawCircle(anchor, 3f, Ui.Ink);   // 1 px paper edge
            DrawStyleBox(Ui.Box(Ui.Sheet, null, 0, 0, Ui.R2 + 3), rect.Grow(3));
            DrawStyleBox(Ui.Box(m.Warn ? Ui.Warn : Ui.Alarm, Ui.Ink, 1, 0, Ui.R1), rect);
            DrawString(Fonts.Text, rect.Position + new Vector2(8, sz.Y - 8), text, HorizontalAlignment.Left, -1, 14, m.Warn ? Ui.Ink : Colors.White);
        }
    }
    Camera3D _cam;
    float _dist;
    Vector2 VpSize => GetViewportRect().Size;
    double _acc;
    public int VisibleLabels;
    public double MaxUpdateMs;   // slowest UpdateLabels pass, printed by the map-frames run (the fit search is the cost)
    public float AreaOf(string code) { foreach (var s in _labels) if (s.C.Name == code) return s.Area; return -1f; }
    public bool IsShown(string code) { foreach (var s in _labels) if (s.C.Name == code) return s.Node.Visible; return false; }
    public System.Func<Country, float, float, bool> InRegion;   // does the point at lon/lat belong to the land the label names (GlobeData.InLabelRegion)
    System.Func<float, float, bool> _onRegion;
    Country _regionOf;
    Vector3 _camPos; Basis _cb; float _aspect, _tanHalf;
    // The label pass costs milliseconds (fit search): it runs only when the view, the key or the label set changed.
    Transform3D _lastXf; Vector2 _lastVp, _lastLeg; bool _lastLegOpen; int _lastLabelCount = -1, _lastSel = -2, _lastPlayer = -2;
    bool LabelsDirty()
    {
        var xf = _cam.GlobalTransform;
        if (xf == _lastXf && VpSize == _lastVp && _legPos == _lastLeg && LegendOpen == _lastLegOpen && _labels.Count == _lastLabelCount && _selId == _lastSel && _playerId == _lastPlayer) return false;
        _lastXf = xf; _lastVp = VpSize; _lastLeg = _legPos; _lastLegOpen = LegendOpen; _lastLabelCount = _labels.Count; _lastSel = _selId; _lastPlayer = _playerId;
        return true;
    }
    void CacheCamera()
    {
        _camPos = _cam.GlobalPosition; _cb = _cam.GlobalTransform.Basis.Orthonormalized();
        _tanHalf = Mathf.Tan(Mathf.DegToRad(_cam.Fov) * 0.5f); _aspect = VpSize.X / VpSize.Y;
    }
    bool OnRegion(float sx, float sy)
    {
        if (InRegion == null || _regionOf == null) return false;
        return ScreenToLonLat(sx, sy, out float lon, out float lat) && InRegion(_regionOf, lon, lat);
    }
    bool ScreenToLonLat(float sx, float sy, out float lon, out float lat)
    {
        lon = lat = 0;
        // The camera ray in managed math (no engine call per probe): the fit search tests hundreds of points per pass.
        float nx = (sx / VpSize.X * 2f - 1f) * _aspect * _tanHalf, ny = (1f - sy / VpSize.Y * 2f) * _tanHalf;
        var d = (_cb.X * nx + _cb.Y * ny - _cb.Z).Normalized();
        var o = _camPos;
        float b = o.Dot(d), disc = b * b - (o.LengthSquared() - 1f);
        if (disc < 0f) return false;
        var hit = o + d * (-b - Mathf.Sqrt(disc));
        var ll = GlobeData.ToLonLat(hit);
        lon = ll.X; lat = ll.Y;
        return true;
    }
    public bool LegendVisible;   // the key is allowed on this screen (the map, not the tree)
    public bool LegendOpen = true;   // expanded; collapsed it is the "?" button (key L); the state is remembered in Settings
    public float MapLeft, MapTop = 0f;   // the map area: right of the rail, under the top bar
    public bool LegendLeftBusy, LegendRightBusy;   // an area panel on the left; an actor panel or the journal on the right
    public event System.Action<bool> LegendToggled;
    public bool LegendPlaced => _legPlaced;
    CaptionButton _legBtn;
    Vector2 _legPos; bool _legPlaced;
    const float BtnW = 28f, BtnH = 24f;
    public void ToggleLegend() { LegendOpen = !LegendOpen; LegendToggled?.Invoke(LegendOpen); }
    // Key rectangle for the current state; false when no corner is free.
    void LayoutLegend()
    {
        float tw = LegendTextWidth(), w = LegendOpen ? LegendSw + tw + 3 * LegendPad + BtnW + 4 : BtnW + 4;
        _legPlaced = LegendVisible && LegendLayout.Place(VpSize.X, MapLeft, MapTop, w, LegendLeftBusy, LegendRightBusy, out _legPos.X, out _legPos.Y);
        _legBtn.Visible = _legPlaced;
        if (!_legPlaced) return;
        _legBtn.Position = _legPos + (LegendOpen ? new Vector2(w - BtnW - 4, 4) : Vector2.Zero);
        _legBtn.Size = new Vector2(BtnW, BtnH);
    }
    string _legColony = "", _legOccupied = "", _legControl = "";
    static readonly Color LegendColony = ToColor(HatchLogic.Line(HatchLogic.ColonyFillTone, HatchLogic.LineScale(HatchKind.Colony))), LegendOcc = new Color(0.22f, 0.22f, 0.24f);   // occupation swatch: a neutral hatch, no country's tone (the controller's tone differs per zone)
    static readonly Color LegendFillColony = ToColor(CountryPalette.ColonyFill(HatchLogic.ColonyFillTone)), LegendFillOcc = new Color(0.86f, 0.86f, 0.85f);
    static Color ToColor((byte R, byte G, byte B) c) => new(c.R / 255f, c.G / 255f, c.B / 255f);
    void RefreshLegend() { _legColony = Loc.T("legend.colony"); _legOccupied = Loc.T("legend.occupied"); _legControl = Loc.T("legend.control"); }

    public void Setup(Camera3D cam)
    {
        _cam = cam;
        MouseFilter = MouseFilterEnum.Ignore;
        RefreshLegend();
        _onRegion = OnRegion;
        _legBtn = Ui.Btn("?", ToggleLegend, 14, false, true);
        _legBtn.TooltipText = "tip.legend"; _legBtn.Visible = false;
        AddChild(_legBtn);
        Loc.Changed += Relabel;
        TreeExiting += () => Loc.Changed -= Relabel;
    }

    /// <summary>Adds the label of a country, or turns an existing one into a framed slice label.</summary>
    public void AddLabel(Country c, bool slice, bool colony = false)
    {
        var s = _labels.Find(x => x.C == c);
        if (s == null)
        {
            var l = new Label { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
            l.AddThemeFontOverride("font", Fonts.Map);
            l.AddThemeFontSizeOverride("font_size", 14);
            l.AddThemeColorOverride("font_color", Ink);   // colony names: same size and ink as the powers (A1); the hatching marks the status
            // No plate (#523): legibility comes from a 2 px halo in the country's own tone, set below.
            AddChild(l);
            _labels.Add(s = new LabelState { C = c, Node = l });
        }
        s.Slice = slice;
        var halo = c.Id > 0 ? ToColor(HatchLogic.Line(c.Tint, 1f)) : ToColor(CountryPalette.ColonyFill(c.Tint));   // tone of the land under the word, so that the hatching does not run through the letters
        s.Node.AddThemeColorOverride("font_outline_color", new Color(halo, 0.85f)); s.Node.AddThemeConstantOverride("outline_size", 2);
        StyleLabel(s);
        s.Node.Text = Names.Country(c.Name) ?? c.Name;
    }

    int _selId = -1, _playerId = -1;

    /// <summary>Bold + underline only for the selected power and the player's power; all other labels stay regular (v0.5d).</summary>
    public void Emphasize(int selectedCountryId, int playerCountryId)
    {
        _selId = selectedCountryId; _playerId = playerCountryId;
        foreach (var s in _labels) StyleLabel(s);
    }

    void StyleLabel(LabelState s)
    {
        s.Emph = s.Slice && (s.C.Id == _selId || s.C.Id == _playerId);
        s.Node.AddThemeFontOverride("font", s.Emph ? Fonts.MapBold : Fonts.Map);
    }

    // Underline as long as the word, just under the baseline, with a paper halo so that it does not sink into the coast line.
    void DrawUnderlines()
    {
        foreach (var s in _labels)
        {
            if (!s.Emph || !s.Node.Visible) continue;
            var font = Fonts.MapBold;
            float w = s.TextW;
            float y = s.Node.Position.Y + font.GetAscent(s.Size) + 3f;
            var a = new Vector2(s.Node.Position.X, y); var b = new Vector2(s.Node.Position.X + w, y);
            DrawLine(a, b, Paper, 4f); DrawLine(a, b, Ink, 1.5f);
        }
    }

    void Relabel() { RefreshLegend(); foreach (var s in _labels) s.Node.Text = Names.Country(s.C.Name) ?? s.C.Name; _lastLabelCount = -1; }   // a new language: lay the names out again

    public float GlobeRadiusPx(float dist)
    {
        float th = Mathf.Asin(1f / dist);
        return Mathf.Tan(th) / Mathf.Tan(Mathf.DegToRad(_cam.Fov) * 0.5f) * VpSize.Y * 0.5f;
    }

    public override void _Process(double delta)
    {
        _dist = _cam.GlobalPosition.Length();
        LayoutLegend();
        QueueRedraw();
        _acc += delta;
        if (_acc >= 1.0 / 15.0) { _acc = 0; if (LabelsDirty()) UpdateLabels(); }
    }

    public override void _Draw()
    {
        if (_dist < 1.01f) return;   // camera distance not known before the first frame
        DrawUnderlines();
        DrawCallouts();
        DrawMarkers();
        if (_legPlaced && LegendOpen) DrawLegend();
        var c = VpSize * 0.5f;
        float r = GlobeRadiusPx(_dist);
        DrawArc(c, r, 0, Mathf.Tau, 180, Limb, 1.5f, true);         // thin pale limb line against the dark sky, no glow
        for (int i = 0; i < 72; i++)                                  // rim: 5-degree ticks, 30-degree long ticks
        {
            float a = i * Mathf.Tau / 72f, len = i % 6 == 0 ? 12f : 5f;
            var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            DrawLine(c + d * (r + 5f), c + d * (r + 5f + len), Limb, 1f, true);
        }
    }

    // Small states (G5, #523): a leader line with a dot at the region; the word has no plate, only the halo.
    void DrawCallouts()
    {
        foreach (var s in _labels)
        {
            if (!s.Node.Visible) continue;
            if (!s.Callout) continue;
            var to = new Vector2(s.Rect.Position.X < s.Anchor.X ? s.Rect.End.X : s.Rect.Position.X, s.Rect.GetCenter().Y);
            DrawLine(s.Anchor, to, Paper, 4f, true); DrawLine(s.Anchor, to, Ink, 1.5f, true);
            DrawCircle(s.Anchor, 4.5f, Paper); DrawCircle(s.Anchor, 3f, Ink);
        }
    }

    static void HatchRect(CanvasItem c, Rect2 r, Color col, float step, bool down, float width)
    {
        for (float a = r.Position.X - r.Size.Y; a < r.End.X; a += step)
        {
            float s0 = Mathf.Max(0, r.Position.X - a), s1 = Mathf.Min(r.Size.Y, r.End.X - a);
            if (s0 >= s1) continue;
            c.DrawLine(down ? new Vector2(a + s0, r.Position.Y + s0) : new Vector2(a + s0, r.End.Y - s0), down ? new Vector2(a + s1, r.Position.Y + s1) : new Vector2(a + s1, r.End.Y - s1), col, width);
        }
    }

    const float LegendSw = 36f, LegendRowH = 22f, LegendPad = 10f;
    Rect2 LegendRect(float textWidth) => LegendOpen
        ? new(_legPos.X, _legPos.Y, LegendSw + textWidth + 3 * LegendPad + BtnW + 4, 3 * LegendRowH + 2 * LegendPad)
        : new(_legPos.X, _legPos.Y, BtnW + 4, BtnH + 4);
    float LegendTextWidth()
    {
        var font = Fonts.Map;
        return Mathf.Max(font.GetStringSize(_legColony, HorizontalAlignment.Left, -1, 14).X, Mathf.Max(font.GetStringSize(_legOccupied, HorizontalAlignment.Left, -1, 14).X, font.GetStringSize(_legControl, HorizontalAlignment.Left, -1, 14).X));
    }

    // Key of the map marks, bottom left: a swatch and a word per row; drawn from cached strings, no allocation.
    void DrawLegend()
    {
        const float sw = 36f, rowH = 22f, pad = 10f;
        var font = Fonts.Map;
        float tw = Mathf.Max(font.GetStringSize(_legColony, HorizontalAlignment.Left, -1, 14).X, Mathf.Max(font.GetStringSize(_legOccupied, HorizontalAlignment.Left, -1, 14).X, font.GetStringSize(_legControl, HorizontalAlignment.Left, -1, 14).X));
        var panel = LegendRect(tw);
        DrawRect(panel, new Color(Ui.Sheet, 0.92f)); DrawRect(panel, Ink, false, 1f);
        float y = panel.Position.Y + pad, x = panel.Position.X + pad;
        var s1 = new Rect2(x, y + 3, sw, rowH - 6);
        var hc = HatchLogic.Params(HatchKind.Colony); var ho = HatchLogic.Params(HatchKind.Occupied);
        DrawRect(s1, LegendFillColony); HatchRect(this, s1, new Color(LegendColony, hc.Alpha), hc.Period * Mathf.Sqrt2, false, hc.Width);
        DrawString(font, new Vector2(x + sw + pad, y + 15), _legColony, HorizontalAlignment.Left, -1, 14, Ink);
        y += rowH; var s2 = new Rect2(x, y + 3, sw, rowH - 6);
        DrawRect(s2, LegendFillOcc); HatchRect(this, s2, new Color(LegendOcc, ho.Alpha), ho.Period * Mathf.Sqrt2, true, ho.Width);   // the period of the map is measured across the lines; HatchRect steps along x
        DrawString(font, new Vector2(x + sw + pad, y + 15), _legOccupied, HorizontalAlignment.Left, -1, 14, Ink);
        y += rowH;
        var s3 = new Rect2(x, y + 3, sw, rowH - 6);
        DrawRect(s3, LegendFillColony); DrawLine(new Vector2(x, y + rowH * 0.5f), new Vector2(x + sw, y + rowH * 0.5f), new Color(Ink, HatchLogic.ControlAlpha), HatchLogic.ControlWidth);
        DrawString(font, new Vector2(x + sw + pad, y + 15), _legControl, HorizontalAlignment.Left, -1, 14, Ink);
    }

    // Visible when the surface normal vs. view ray cos > 0.4; once shown stays until cos < 0.3.
    float CosTo(Vector3 p)
    {
        var v = (_cam.GlobalPosition - p).Normalized();
        return p.Normalized().Dot(v);
    }

    bool BlockedByForeignLand(float sx, float sy)
    {
        if (InRegion == null || IsLand == null || !ScreenToLonLat(sx, sy, out float lon, out float lat)) return false;
        return IsLand(lon, lat) && !InRegion(_regionOf, lon, lat);
    }
    public System.Func<float, float, bool> IsLand;   // GlobeData.IsLandAt
    System.Func<float, float, bool> _blocked;

    // Leader label: free water or own land only, clear of the other labels; hidden when no direction is free (#523: it appears when the player zooms in).
    bool PlaceLeaderLabel(LabelState s, Vector2 anchor, float w, float h, float area, List<LabelPlacement.Box> placed, out float px, out float py)
    {
        _blocked ??= BlockedByForeignLand;
        px = py = 0;
        if (!LabelPlacement.PlaceLeader(anchor.X, anchor.Y, w, h, VpSize.X, VpSize.Y, VpSize.X * 0.5f, VpSize.Y * 0.5f, GlobeRadiusPx(_dist) * LabelPlacement.LimbKeep,
                LabelPlacement.RegionRadius(area), placed, _blocked, out float cx, out float cy)) return false;
        px = cx - w * 0.5f; py = cy - h * 0.5f;
        return true;
    }

    void UpdateLabels()
    {
        var placed = new List<LabelPlacement.Box>();
        if (_legPlaced) { var lr = LegendRect(LegendTextWidth()).Grow(4f); placed.Add(new(lr.Position.X, lr.Position.Y, lr.Size.X, lr.Size.Y)); }   // labels keep clear of the key
        VisibleLabels = 0;
        long t0 = (long)Time.GetTicksUsec();
        CacheCamera();
        // Slice countries first, so that they win overlaps.
        foreach (var pass in new[] { true, false })
            foreach (var s in _labels)
            {
                if (s.Slice != pass) continue;
                var p = GlobeData.ToSphere(s.C.LabelPoint.X, s.C.LabelPoint.Y);
                float cs = CosTo(p);
                s.On = s.On ? cs > 0.3f : cs > 0.4f;
                if (!s.On) { s.Node.Visible = false; continue; }
                var sp = _cam.UnprojectPosition(p);
                // Real projected area of the region from the local projection at its point: decides size, leader line or hiding (#523).
                var ex = _cam.UnprojectPosition(GlobeData.ToSphere(s.C.LabelPoint.X + 1f, s.C.LabelPoint.Y)) - sp;
                var ey = _cam.UnprojectPosition(GlobeData.ToSphere(s.C.LabelPoint.X, s.C.LabelPoint.Y + 1f)) - sp;
                float area = LabelPlacement.ScreenArea(s.C.OwnArea, ex.X, ex.Y, ey.X, ey.Y);
                if (area < LabelPlacement.HiddenBelow) { s.Node.Visible = false; continue; }   // too small to name at this zoom: shown when the player zooms in
                var mode = LabelPlacement.ModeFor(area);
                s.Callout = mode == LabelPlacement.PlateMode.Callout; s.Anchor = sp; s.Area = area;
                var font = s.Emph ? Fonts.MapBold : Fonts.Map;
                var text = s.Node.Text;
                float w = 0, h = 0, px = 0, py = 0;
                int size = s.Callout ? (int)LabelPlacement.NominalFont : LabelPlacement.FontSizeFor(area);
                _regionOf = s.C;
                if (s.Callout)
                {
                    s.TextW = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
                    w = s.TextW + 4f; h = font.GetHeight(size) + 2f;
                    if (!PlaceLeaderLabel(s, sp, w, h, area, placed, out px, out py)) { s.Node.Visible = false; continue; }
                }
                else
                {
                    // The word is as large as the inscribed text rectangle allows (down to the minimum size) and stays inside its own country, else it is hidden until zoomed in.
                    // A long unit name has an optional short form (locale polity_short.CODE) that is tried after the full one at every size.
                    float fit = LabelPlacement.FitScale(s.Node.Visible), step = Mathf.Clamp(Mathf.Sqrt(area) / 10f, 4f, 24f);
                    string shortName = Names.CountryShort(s.C.Name), chosen = null;
                    int pref = size;
                    for (int v = 0; v < 2 && chosen == null; v++)
                    {
                        var variant = v == 0 ? (Names.Country(s.C.Name) ?? s.C.Name) : shortName;
                        if (variant == null) break;
                        for (int sz = pref; sz > 0; sz = LabelPlacement.NextSize(sz))
                        {
                            float tw = font.GetStringSize(variant, HorizontalAlignment.Left, -1, sz).X, bw = tw + 4f, bh = font.GetHeight(sz) + 2f;
                            if (!LabelPlacement.MayFit(area, bw * fit, bh * fit)) continue;
                            if (LabelPlacement.FindSpot(sp.X, sp.Y, bw, bh, fit, 4f, VpSize.X * 0.5f, VpSize.Y * 0.5f, GlobeRadiusPx(_dist) * LabelPlacement.LimbKeep, VpSize.X, VpSize.Y, step, _onRegion, out float cx, out float cy))
                            { chosen = variant; size = sz; s.TextW = tw; w = bw; h = bh; px = cx - w * 0.5f; py = cy - h * 0.5f; break; }
                        }
                    }
                    if (chosen == null)
                    {
                        if (s.C.Id <= 0 || area < LabelPlacement.CalloutFallbackFrom) { s.Node.Visible = false; continue; }
                        // Too narrow for the word on its own land: the full name on a leader line into the open (a country named at a wider view is not dropped).
                        s.Callout = true; size = (int)LabelPlacement.NominalFont; chosen = Names.Country(s.C.Name) ?? s.C.Name;
                        s.TextW = font.GetStringSize(chosen, HorizontalAlignment.Left, -1, size).X;
                        w = s.TextW + 4f; h = font.GetHeight(size) + 2f;
                        if (!PlaceLeaderLabel(s, sp, w, h, area, placed, out px, out py)) { s.Node.Visible = false; continue; }
                    }
                    if (s.Node.Text != chosen) s.Node.Text = chosen;
                }
                if (size != s.Size) { s.Size = size; s.Node.AddThemeFontSizeOverride("font_size", size); }
                var rect = new Rect2(px, py, w, h);
                s.Rect = rect;
                bool hit = false;
                var pbox = new LabelPlacement.Box(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y);
                if (!s.Callout) foreach (var o in placed) if (o.Intersects(pbox)) { hit = true; break; }   // leaders were checked against placed labels when searching
                if (hit) { s.Node.Visible = false; continue; }
                placed.Add(pbox);
                s.Node.Position = rect.Position + new Vector2(2, 1);
                s.Node.Visible = true;
                VisibleLabels++;
            }
        MaxUpdateMs = System.Math.Max(MaxUpdateMs, ((long)Time.GetTicksUsec() - t0) / 1000.0);
    }
}
