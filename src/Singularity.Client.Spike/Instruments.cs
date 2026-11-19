using System;
using Godot;

namespace Singularity.Client.Spike;

/// <summary>Erlenmeyer flask indicator (UI_INSTRUMENTS §3, #404, #409): level = liquid + a meniscus line + the number; a deficit hatches the empty part at 45 degrees (a second channel).
/// Level 0 of «Приборы-органы» hides it (the text lines stay), level 1 uses a grey wash, level 2 the ink wash. Not interactive. The same look in every era (laboratory glass does not age).
/// Never put it under an alert plate or in a table: the owner of the layout places it beside the number.</summary>
public sealed partial class FlaskControl : Control
{
    const float W = 56, H = 80, Cx = 28, NeckTop = 2, NeckBottom = 16, Bottom = 56, NeckHalf = 6, BodyHalf = 20, LevelTop = 10;
    const float Slope = (BodyHalf - NeckHalf) / (Bottom - NeckBottom);   // half-width growth per du of height in the cone

    static readonly Vector2[] Outline =
    {
        new(Cx - NeckHalf, NeckTop), new(Cx - NeckHalf, NeckBottom), new(Cx - BodyHalf, Bottom),
        new(Cx + BodyHalf, Bottom), new(Cx + NeckHalf, NeckBottom), new(Cx + NeckHalf, NeckTop), new(Cx - NeckHalf, NeckTop),
    };
    static readonly float[] Marks = { 0.25f, 0.5f, 0.75f };

    public int Ink = Ui.Teal;            // wash of the liquid
    public bool Deficit;                 // hatch the empty part
    double _level = -1; string _txt = "";
    readonly Vector2[] _liq4 = new Vector2[4], _liq6 = new Vector2[6];   // reused by _Draw, no per-frame allocation

    public FlaskControl() { MouseFilter = MouseFilterEnum.Pass; CustomMinimumSize = new Vector2(W, H); }

    /// <summary>Fill 0..1 and the deficit flag; redraws only when something changed.</summary>
    public void Set(double level, bool deficit)
    {
        level = Math.Clamp(level, 0, 1);
        if (Math.Abs(level - _level) < 1e-9 && deficit == Deficit) return;
        _level = level; Deficit = deficit;
        _txt = Math.Round(level * 100).ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + "%";
        QueueRedraw();
    }

    static float HalfAt(float y) => y <= NeckBottom ? NeckHalf : NeckHalf + (y - NeckBottom) * Slope;

    public override void _Draw()
    {
        if (_level < 0) return;
        int lvl = InstrumentLogic.OrganLevel;
        float yL = Bottom - (float)_level * (Bottom - LevelTop);
        if (_level > 0.004)
        {
            var wash = lvl == 2 ? Ui.InkWash[Ink] : Ui.Hover;
            if (yL >= NeckBottom)
            {
                float h = HalfAt(yL);
                _liq4[0] = new Vector2(Cx - h, yL); _liq4[1] = new Vector2(Cx + h, yL); _liq4[2] = new Vector2(Cx + BodyHalf, Bottom); _liq4[3] = new Vector2(Cx - BodyHalf, Bottom);
                DrawColoredPolygon(_liq4, wash);
            }
            else
            {
                _liq6[0] = new Vector2(Cx - NeckHalf, yL); _liq6[1] = new Vector2(Cx + NeckHalf, yL); _liq6[2] = new Vector2(Cx + NeckHalf, NeckBottom);
                _liq6[3] = new Vector2(Cx + BodyHalf, Bottom); _liq6[4] = new Vector2(Cx - BodyHalf, Bottom); _liq6[5] = new Vector2(Cx - NeckHalf, NeckBottom);
                DrawColoredPolygon(_liq6, wash);
            }
        }
        if (Deficit && yL > NeckBottom) HatchCone(NeckBottom, yL, new Color(Ui.Ink3, 0.9f));
        for (int i = 0; i < Marks.Length; i++)
        {
            float y = Bottom - Marks[i] * (Bottom - LevelTop);
            DrawLine(new Vector2(Cx + HalfAt(y) + 2, y), new Vector2(Cx + HalfAt(y) + 7, y), Ui.Ink2, 1f);
        }
        DrawPolyline(Outline, Ui.Ink, 1.5f);
        DrawLine(new Vector2(Cx - NeckHalf - 3, NeckTop), new Vector2(Cx + NeckHalf + 3, NeckTop), Ui.Ink, 1.5f);   // rim
        if (_level > 0.004) DrawLine(new Vector2(Cx - HalfAt(yL), yL), new Vector2(Cx + HalfAt(yL), yL), Ui.Ink, 2f);   // meniscus line: the level in ink
        DrawString(Fonts.Mono, new Vector2(0, H - 4), _txt, HorizontalAlignment.Center, W, 14, Ui.Ink);
    }

    // 45-degree lines x + y = c clipped to the cone between y0 and y1 by solving the two edge inequalities in y (no clip rect).
    void HatchCone(float y0, float y1, Color col)
    {
        const float a0 = NeckHalf - NeckBottom * Slope;   // half-width = a0 + Slope * y
        for (float c = Cx - BodyHalf + NeckBottom; c <= Cx + BodyHalf + Bottom; c += 5f)
        {
            float t = c - Cx;
            float lo = Math.Max(y0, (t - a0) / (1 + Slope)), hi = Math.Min(y1, (t + a0) / (1 - Slope));
            if (lo < hi) DrawLine(new Vector2(c - lo, lo), new Vector2(c - hi, hi), col, 1f);
        }
    }
}

/// <summary>Caliper (UI_INSTRUMENTS §3): a share 0..100 % on a scale with a fixed and a sliding jaw, step 5, mouse drag and the arrow keys.
/// E1 vernier (the aligned vernier tick is the fraction), E2 lit segments, E3 plain scale with a cursor. The number stays in its own column beside it (never replaced).
/// Raises <see cref="Changed"/> with a value on the grid; the owner sets <see cref="Value"/> back, so the order and the redistribution rules stay where they were.</summary>
public sealed partial class CaliperControl : Control
{
    const float H = 42, Pad = 8, ScaleY = 24;
    public int Value;                          // 0..100, may sit off the grid (the other shares are rounded shares)
    public int Year = 1945;
    public bool Editable = true;
    public event Action<int> Changed;
    bool _drag;
    int _shown = int.MinValue, _era, _level = -1;

    public CaliperControl()
    {
        CustomMinimumSize = new Vector2(120, H);
        FocusMode = FocusModeEnum.All;
        MouseDefaultCursorShape = CursorShape.Hsize;
        TooltipText = "caliper.tip";
    }

    public override void _Process(double delta)
    {
        int era = InstrumentLogic.Era(Year), level = InstrumentLogic.OrganLevel;
        if (Value == _shown && era == _era && level == _level) return;
        _shown = Value; _era = era; _level = level;
        QueueRedraw();
    }

    float X0 => Pad;
    float X1 => Size.X - Pad;
    float XOf(double v) => X0 + (float)(v / 100.0) * (X1 - X0);

    public override void _GuiInput(InputEvent e)
    {
        if (!Editable) return;
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            _drag = mb.Pressed;
            if (mb.Pressed) { GrabFocus(); Pick(mb.Position.X); }
            AcceptEvent();
        }
        else if (e is InputEventMouseMotion mm && _drag) { Pick(mm.Position.X); AcceptEvent(); }
        else if (e is InputEventKey { Pressed: true } k && (k.Keycode == Key.Left || k.Keycode == Key.Right))
        {
            Emit(InstrumentLogic.CaliperArrow(Value, k.Keycode == Key.Right ? 1 : -1));
            AcceptEvent();
        }
    }

    void Pick(float x) => Emit(InstrumentLogic.CaliperSnap((x - X0) / (X1 - X0) * 100.0));
    void Emit(int v) { if (v != Value) Changed?.Invoke(v); }

    public override void _Draw()
    {
        float x0 = X0, x1 = X1, xv = XOf(Value), step = (x1 - x0) / 20f;
        int lvl = InstrumentLogic.OrganLevel;
        bool plain = _era == 3;
        // fill between the jaws: segments in E2, a thick line in E3, a thin band in E1 (decoration; the jaw position and the number carry the value)
        var fill = lvl == 2 ? Ui.InkWash[Ui.Sand] : Ui.Hover;
        if (_era == 2)
            for (int i = 0; i < 20; i++)
            {
                float a = x0 + i * step, part = Math.Clamp((float)(Value - i * 5) / 5f, 0f, 1f);
                if (part > 0) DrawRect(new Rect2(a + 1, 10, (step - 2) * part, 8), fill);
                DrawRect(new Rect2(a + 1, 10, step - 2, 8), part > 0 ? Ui.Ink2 : Ui.Ink3, false, 1f);
            }
        else if (_era == 3) DrawLine(new Vector2(x0, ScaleY - 4), new Vector2(xv, ScaleY - 4), Ui.Ink, 2f);
        else DrawRect(new Rect2(x0, 14, xv - x0, 5), fill);
        // scale: ticks every 5, longer every 10 and at the ends
        DrawLine(new Vector2(x0, ScaleY), new Vector2(x1, ScaleY), Ui.Ink, 1f);
        for (int i = 0; i <= 20; i++)
        {
            float x = x0 + i * step, len = i % 10 == 0 ? 7 : i % 2 == 0 ? 5 : 3;
            DrawLine(new Vector2(x, ScaleY), new Vector2(x, ScaleY + len), i % 2 == 0 ? Ui.Ink2 : Ui.Ink3, 1f);
        }
        // jaws: fixed at 0, sliding at the value
        DrawLine(new Vector2(x0, 4), new Vector2(x0, ScaleY + 16), Ui.Ink, 2f);
        DrawLine(new Vector2(xv, 4), new Vector2(xv, ScaleY + 16), Ui.Ink, 2f);
        DrawLine(new Vector2(xv, 4), new Vector2(xv + 8, 4), Ui.Ink, 2f);   // the arm of the sliding jaw
        if (_era == 1)
        {
            // vernier plate under the jaw: six ticks at 0.8 of a step, the one aligned with the grid (value mod 5) is long and inked
            int frac = Value % InstrumentLogic.CaliperStep;
            DrawRect(new Rect2(xv, ScaleY + 9, 5 * step * 0.8f + 4, 7), Ui.Sheet, true);
            DrawRect(new Rect2(xv, ScaleY + 9, 5 * step * 0.8f + 4, 7), Ui.Ink, false, 1f);
            for (int k = 0; k < 5; k++)
            {
                float x = xv + 2 + k * step * 0.8f; bool on = k == frac;
                DrawLine(new Vector2(x, ScaleY + 9), new Vector2(x, ScaleY + (on ? 16 : 14)), on ? Ui.Ink : Ui.Ink2, on ? 2f : 1f);
            }
        }
        else if (plain)
        {
            // cursor triangle on the jaw, as the E3 gauge
            _tri[0] = new Vector2(xv, ScaleY - 1); _tri[1] = new Vector2(xv - 3.5f, ScaleY - 8); _tri[2] = new Vector2(xv + 3.5f, ScaleY - 8);
            DrawColoredPolygon(_tri, Ui.Ink);
        }
        if (HasFocus()) DrawRect(new Rect2(1, 1, Size.X - 2, Size.Y - 2), Ui.Ink, false, 2f);
    }

    readonly Vector2[] _tri = new Vector2[3];
}

/// <summary>Pencil marker (UI_INSTRUMENTS §3): «this field is edited». A tilted drawing pencil and a 2 px underline of the value; a child of the number label, drawn over its right-aligned text.
/// Layer 2 only: hidden at level 0 of «Приборы-органы», where the plain field stays as it was.</summary>
public sealed partial class PencilMarker : Control
{
    bool _active; int _level = -1;
    readonly Vector2[] _body = new Vector2[4], _tip = new Vector2[3];
    float _underline = 24;

    public PencilMarker() { MouseFilter = MouseFilterEnum.Ignore; SetAnchorsPreset(LayoutPreset.FullRect); }

    /// <param name="active">The field is edited and still unsent.</param>
    /// <param name="textWidth">Width of the value text in du, for the underline.</param>
    public void Set(bool active, float textWidth)
    {
        if (active == _active && Math.Abs(textWidth - _underline) < 0.01f) return;
        _active = active; _underline = textWidth;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (InstrumentLogic.OrganLevel == _level) return;
        _level = InstrumentLogic.OrganLevel;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_active || _level == 0) return;
        float w = Size.X, h = Size.Y, yb = h - 3;
        DrawLine(new Vector2(w - _underline, yb), new Vector2(w, yb), Ui.Ink, 2f);
        // pencil lying at about -18 degrees: body, tip, lead; 16 du long
        const float c = 0.951f, s = -0.309f;   // cos and sin of -18 degrees
        Vector2 O = new(2, h * 0.5f + 5);
        Vector2 P(float x, float y) => O + new Vector2(x * c - y * s, x * s + y * c);
        _body[0] = P(5, -3); _body[1] = P(16, -3); _body[2] = P(16, 3); _body[3] = P(5, 3);
        _tip[0] = P(5, -3); _tip[1] = P(0, 0); _tip[2] = P(5, 3);
        DrawColoredPolygon(_body, Ui.Sheet);
        DrawPolyline(_body, Ui.Ink, 1f); DrawLine(_body[3], _body[0], Ui.Ink, 1f);
        DrawLine(P(5, 0), P(16, 0), Ui.Ink2, 1f);
        DrawPolyline(_tip, Ui.Ink, 1f);
        DrawLine(P(1.5f, 0), P(0, 0), Ui.Ink, 2f);
    }
}
