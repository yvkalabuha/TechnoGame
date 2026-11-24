using System.Collections.Generic;
using Godot;

namespace Singularity.Client.Spike;

/// <summary>2D layer over the 3D scene: limb line, rim ticks, country labels (map face, localized) with hysteresis and overlap rejection.
/// Labels of the slice countries are bold with an ink underline.</summary>
public sealed partial class Overlay : Control
{
    sealed class LabelState { public Country C; public Label Node; public bool On; public bool Slice, Emph, Callout, Framed; public Rect2 Rect; public float Area; public Vector2 Anchor; public Color Frame; }

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
    public float AreaOf(string code) { foreach (var s in _labels) if (s.C.Name == code) return s.Area; return -1f; }
    public bool IsShown(string code) { foreach (var s in _labels) if (s.C.Name == code) return s.Node.Visible; return false; }
    public System.Func<float, float, int> OwnerAt;   // core owner number at lon/lat (GlobeData.CoreOwnerAt); lets a label settle on the visible part of a long region
    System.Func<float, float, bool> _onRegion;
    int _regionOwner;
    bool OnRegion(float sx, float sy)
    {
        if (OwnerAt == null || _regionOwner <= 0) return false;
        var o = _cam.ProjectRayOrigin(new Vector2(sx, sy)); var d = _cam.ProjectRayNormal(new Vector2(sx, sy));
        float b = o.Dot(d), disc = b * b - (o.LengthSquared() - 1f);
        if (disc < 0f) return false;
        var hit = o + d * (-b - Mathf.Sqrt(disc));
        var ll = GlobeData.ToLonLat(hit);
        return OwnerAt(ll.X, ll.Y) == _regionOwner;
    }
    public bool LegendVisible;   // the key of the status hatching and the control line (C1, C2)
    string _legColony = "", _legOccupied = "", _legControl = "";
    static readonly Color LegendColony = ToColor(HatchLogic.Line(HatchLogic.ColonyFillTone, HatchLogic.LineScale(HatchKind.Colony))), LegendOcc = ToColor(HatchLogic.Line(HatchLogic.ControllerTone, HatchLogic.LineScale(HatchKind.Occupied)));
    static readonly Color LegendFillColony = ToColor(HatchLogic.Line(HatchLogic.ColonyFillTone, 1f)), LegendFillOcc = ToColor(HatchLogic.Line(HatchLogic.OccupiedFillTone, 1f));
    static Color ToColor((byte R, byte G, byte B) c) => new(c.R / 255f, c.G / 255f, c.B / 255f);
    void RefreshLegend() { _legColony = Loc.T("legend.colony"); _legOccupied = Loc.T("legend.occupied"); _legControl = Loc.T("legend.control"); }

    public void Setup(Camera3D cam)
    {
        _cam = cam;
        MouseFilter = MouseFilterEnum.Ignore;
        RefreshLegend();
        _onRegion = OnRegion;
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
            // A plate under the word (C5): sheet at 85 %, so that the text reads on every tone and the hatching never runs through it.
            l.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color(Ui.Sheet, 0.85f), ContentMarginLeft = 3, ContentMarginRight = 3, ContentMarginTop = 0, ContentMarginBottom = 0 });
            AddChild(l);
            _labels.Add(s = new LabelState { C = c, Node = l });
        }
        s.Slice = slice;
        s.Frame = c.Id > 0 ? ToColor(GlobeData.Line(c.Tint)) : Ink;
        s.Node.AddThemeColorOverride("font_outline_color", Paper); s.Node.AddThemeConstantOverride("outline_size", 4);
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
            float w = font.GetStringSize(s.Node.Text, HorizontalAlignment.Left, -1, 14).X;
            float y = s.Node.Position.Y + font.GetAscent(14) + 3f;
            var a = new Vector2(s.Node.Position.X, y); var b = new Vector2(s.Node.Position.X + w, y);
            DrawLine(a, b, Paper, 4f); DrawLine(a, b, Ink, 1.5f);
        }
    }

    void Relabel() { RefreshLegend(); foreach (var s in _labels) s.Node.Text = Names.Country(s.C.Name) ?? s.C.Name; }

    public float GlobeRadiusPx(float dist)
    {
        float th = Mathf.Asin(1f / dist);
        return Mathf.Tan(th) / Mathf.Tan(Mathf.DegToRad(_cam.Fov) * 0.5f) * VpSize.Y * 0.5f;
    }

    public override void _Process(double delta)
    {
        _dist = _cam.GlobalPosition.Length();
        QueueRedraw();
        _acc += delta;
        if (_acc >= 1.0 / 15.0) { _acc = 0; UpdateLabels(); }
    }

    public override void _Draw()
    {
        if (_dist < 1.01f) return;   // camera distance not known before the first frame
        DrawUnderlines();
        DrawCallouts();
        DrawMarkers();
        if (LegendVisible) DrawLegend();
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

    // Small states (G5): a leader line with a dot at the region, and a frame in the line tone round plates of states under 400 du^2.
    void DrawCallouts()
    {
        foreach (var s in _labels)
        {
            if (!s.Node.Visible) continue;
            if (s.Framed) DrawRect(s.Rect, s.Frame, false, 1.5f);
            if (!s.Callout) continue;
            var to = new Vector2(s.Rect.Position.X < s.Anchor.X ? s.Rect.End.X : s.Rect.Position.X, s.Rect.GetCenter().Y);
            DrawLine(s.Anchor, to, Paper, 4f, true); DrawLine(s.Anchor, to, Ink, 1.5f, true);
            DrawCircle(s.Anchor, 4.5f, Paper); DrawCircle(s.Anchor, 3f, Ink);
            DrawRect(s.Rect, Ink, false, 1f);
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
    Rect2 LegendRect(float textWidth) => new(16, 16, LegendSw + textWidth + 3 * LegendPad, 3 * LegendRowH + 2 * LegendPad);
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
        DrawRect(s1, LegendFillColony); HatchRect(this, s1, new Color(LegendColony, hc.Alpha), hc.Period, false, hc.Width);
        DrawString(font, new Vector2(x + sw + pad, y + 15), _legColony, HorizontalAlignment.Left, -1, 14, Ink);
        y += rowH; var s2 = new Rect2(x, y + 3, sw, rowH - 6);
        DrawRect(s2, LegendFillOcc); HatchRect(this, s2, new Color(LegendOcc, ho.Alpha), ho.Period, true, ho.Width);
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

    void UpdateLabels()
    {
        var placed = new List<Rect2>();
        if (LegendVisible) placed.Add(LegendRect(LegendTextWidth()).Grow(4f));   // labels keep clear of the key
        VisibleLabels = 0;
        float gr = GlobeRadiusPx(_dist);
        var gc = VpSize * 0.5f;
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
                var sz = s.Node.GetCombinedMinimumSize();
                // The plate moves inside the visible part of its region (margin 8 px, G4); it is hidden only when that part cannot hold it.
                float w = sz.X + 6f, h = sz.Y + 6f;
                var mode = LabelPlacement.ModeFor(LabelPlacement.ScreenArea(s.C.BoxArea, gr, cs));
                float px, py;
                _regionOwner = s.C.Id;
                s.Callout = mode == LabelPlacement.PlateMode.Callout; s.Framed = mode == LabelPlacement.PlateMode.Framed && s.C.Id > 0; s.Anchor = sp; s.Area = LabelPlacement.ScreenArea(s.C.BoxArea, gr, cs);
                if (s.Callout)
                {
                    LabelPlacement.CalloutCentre(sp.X, sp.Y, w, h, VpSize.X, out float ccx, out float ccy);
                    if (!LabelPlacement.Place(ccx, ccy, w, h, VpSize.X, VpSize.Y, out px, out py)) { s.Node.Visible = false; continue; }
                }
                else if (!LabelPlacement.PlaceInRegion(sp.X, sp.Y, w, h, VpSize.X, VpSize.Y, LabelPlacement.RegionRadius(s.C.BoxArea, gr), gc.X, gc.Y, gr * 0.97f, _onRegion, out px, out py)) { s.Node.Visible = false; continue; }
                var rect = new Rect2(px, py, w, h);
                s.Rect = rect;
                bool hit = false;
                foreach (var o in placed) if (o.Intersects(rect)) { hit = true; break; }
                if (hit) { s.Node.Visible = false; continue; }
                placed.Add(rect);
                s.Node.Position = rect.Position + new Vector2(3, 3);
                s.Node.Visible = true;
                VisibleLabels++;
            }
    }
}
