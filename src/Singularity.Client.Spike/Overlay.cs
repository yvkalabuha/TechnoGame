using System.Collections.Generic;
using Godot;

namespace Singularity.Client.Spike;

/// <summary>2D layer over the 3D scene: limb line, rim ticks, country labels (map face, localized) with hysteresis and overlap rejection.
/// Labels of the slice countries are bold with an ink underline.</summary>
public sealed partial class Overlay : Control
{
    sealed class LabelState { public Country C; public Label Node; public bool On; public bool Slice, Emph; }

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

    public void Setup(Camera3D cam)
    {
        _cam = cam;
        MouseFilter = MouseFilterEnum.Ignore;
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
            l.AddThemeColorOverride("font_color", colony ? Ui.Ink2 : Ink);   // colony names in the second ink: they never compete with the powers
            // A plate under the word (C5): sheet at 85 %, so that the text reads on every tone and the hatching never runs through it.
            l.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color(Ui.Sheet, 0.85f), ContentMarginLeft = 3, ContentMarginRight = 3, ContentMarginTop = 0, ContentMarginBottom = 0 });
            AddChild(l);
            _labels.Add(s = new LabelState { C = c, Node = l });
        }
        s.Slice = slice;
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

    void Relabel() { foreach (var s in _labels) s.Node.Text = Names.Country(s.C.Name) ?? s.C.Name; }

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
        DrawMarkers();
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

    // Visible when the surface normal vs. view ray cos > 0.4; once shown stays until cos < 0.3.
    float CosTo(Vector3 p)
    {
        var v = (_cam.GlobalPosition - p).Normalized();
        return p.Normalized().Dot(v);
    }

    void UpdateLabels()
    {
        var placed = new List<Rect2>();
        VisibleLabels = 0;
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
                var rect = new Rect2(sp - sz * 0.5f, sz).Grow(3f);
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
