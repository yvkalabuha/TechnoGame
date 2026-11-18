using System.Collections.Generic;
using Godot;

namespace Singularity.Client.Spike;

/// <summary>2D layer over the 3D scene: limb line, rim ticks, country labels (map face, localized) with hysteresis and overlap rejection.
/// Labels of the slice countries carry an ink frame (no fill).</summary>
public sealed partial class Overlay : Control
{
    sealed class LabelState { public Country C; public Label Node; public bool On; public bool Slice; }

    static readonly Color Ink = new("1D1E20");
    static readonly Color Paper = new("F2EFE6");
    static readonly Color Limb = new Color("A9B8CC", 0.7f);

    readonly List<LabelState> _labels = new();
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
    public void AddLabel(Country c, bool slice)
    {
        var s = _labels.Find(x => x.C == c);
        if (s == null)
        {
            var l = new Label { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
            l.AddThemeFontOverride("font", Fonts.Map);
            l.AddThemeFontSizeOverride("font_size", 14);
            l.AddThemeColorOverride("font_color", Ink);
            AddChild(l);
            _labels.Add(s = new LabelState { C = c, Node = l });
        }
        s.Slice = slice;
        if (slice)
        {
            // No leftover outline (it smeared the glyphs) and a real bold instance of Literata instead of any synthetic weight.
            s.Node.AddThemeStyleboxOverride("normal", Ui.Box(Colors.Transparent, Ink, 1, 4));
            s.Node.RemoveThemeColorOverride("font_outline_color"); s.Node.RemoveThemeConstantOverride("outline_size");
            s.Node.AddThemeFontOverride("font", Fonts.MapBold);
        }
        else { s.Node.AddThemeColorOverride("font_outline_color", Paper); s.Node.AddThemeConstantOverride("outline_size", 4); }
        s.Node.Text = Names.Country(c.Name) ?? c.Name;
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
