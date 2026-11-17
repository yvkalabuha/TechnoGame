using System.Collections.Generic;
using Godot;

namespace Singularity.Client.Spike;

/// <summary>2D layer over the 3D scene: limb line, rim ticks, country labels with hysteresis, alert tags docked to the limb.</summary>
public sealed partial class Overlay : Control
{
    sealed class LabelState { public Country C; public Label Node; public bool On; }
    sealed class Tag { public Vector3 P; public Label Node; public bool Docked; }

    static readonly Color Ink = new("1D1E20");
    static readonly Color Paper = new("F2EFE6");
    static readonly Color Alarm = new("C42A1C");

    readonly List<LabelState> _labels = new();
    readonly List<Tag> _tags = new();
    Camera3D _cam;
    float _dist;
    Vector2 VpSize => GetViewportRect().Size;
    double _acc;
    public int VisibleLabels, DockedTags;

    public void Setup(Camera3D cam, List<Country> top)
    {
        _cam = cam;
        MouseFilter = MouseFilterEnum.Ignore;
        
        foreach (var c in top)
        {
            var l = new Label { Text = c.Name, Visible = false, MouseFilter = MouseFilterEnum.Ignore };
            l.AddThemeFontSizeOverride("font_size", 12);
            l.AddThemeColorOverride("font_color", Ink);
            l.AddThemeColorOverride("font_outline_color", Paper);
            l.AddThemeConstantOverride("outline_size", 4);
            AddChild(l);
            _labels.Add(new LabelState { C = c, Node = l });
        }
    }

    public void AddTag(float lon, float lat, string text, bool alarm = true)
    {
        var l = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", 13);
        l.AddThemeColorOverride("font_color", Colors.White);
        var sb = new StyleBoxFlat { BgColor = alarm ? Alarm : Ink, ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 2, ContentMarginBottom = 2 };
        sb.SetBorderWidthAll(1); sb.BorderColor = Ink;
        l.AddThemeStyleboxOverride("normal", sb);
        AddChild(l);
        _tags.Add(new Tag { P = GlobeData.ToSphere(lon, lat), Node = l });
    }

    public float GlobeRadiusPx(float dist)
    {
        float th = Mathf.Asin(1f / dist);
        return Mathf.Tan(th) / Mathf.Tan(Mathf.DegToRad(_cam.Fov) * 0.5f) * VpSize.Y * 0.5f;
    }

    public override void _Process(double delta)
    {
        _dist = _cam.GlobalPosition.Length();
        QueueRedraw();
        UpdateTags();
        _acc += delta;
        if (_acc >= 1.0 / 15.0) { _acc = 0; UpdateLabels(); }
    }

    public override void _Draw()
    {
        var c = VpSize * 0.5f;
        float r = GlobeRadiusPx(_dist);
        DrawArc(c, r, 0, Mathf.Tau, 180, Ink, 2f, true);            // solid limb line, no glow
        for (int i = 0; i < 72; i++)                                  // rim: 5-degree ticks, 30-degree long ticks
        {
            float a = i * Mathf.Tau / 72f, len = i % 6 == 0 ? 12f : 5f;
            var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            DrawLine(c + d * (r + 5f), c + d * (r + 5f + len), Ink, 1f, true);
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
        foreach (var s in _labels)
        {
            var p = GlobeData.ToSphere(s.C.LabelPoint.X, s.C.LabelPoint.Y);
            float cs = CosTo(p);
            s.On = s.On ? cs > 0.3f : cs > 0.4f;
            if (!s.On) { s.Node.Visible = false; continue; }
            var sp = _cam.UnprojectPosition(p);
            var sz = s.Node.GetCombinedMinimumSize();
            var rect = new Rect2(sp - sz * 0.5f, sz).Grow(2f);
            bool hit = false;
            foreach (var o in placed) if (o.Intersects(rect)) { hit = true; break; }
            if (hit) { s.Node.Visible = false; continue; }
            placed.Add(rect);
            s.Node.Position = rect.Position + new Vector2(2, 2);
            s.Node.Visible = true;
            VisibleLabels++;
        }
    }

    void UpdateTags()
    {
        var c = VpSize * 0.5f; float r = GlobeRadiusPx(_dist);
        DockedTags = 0;
        foreach (var t in _tags)
        {
            float cs = CosTo(t.P);
            var sp = _cam.UnprojectPosition(t.P);
            if (!float.IsFinite(sp.X) || !float.IsFinite(sp.Y)) continue;
            t.Docked = t.Docked ? cs < 0.25f : cs < 0.15f; // never hidden: docks to the rim instead
            var sz = t.Node.GetCombinedMinimumSize();
            Vector2 pos;
            if (t.Docked)
            {
                var d = sp - c;
                d = d.LengthSquared() < 1f ? Vector2.Up : d.Normalized();
                pos = c + d * (r + 28f) - sz * 0.5f;
                DockedTags++;
            }
            else pos = sp + new Vector2(8, -sz.Y - 4);
            var hi = (VpSize - sz).Max(Vector2.Zero);
            t.Node.Position = pos.Clamp(Vector2.Zero, hi);
        }
    }
}
