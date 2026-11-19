using System;
using Godot;

namespace Singularity.Client.Spike;

/// <summary>Erlenmeyer flask indicator (UI_INSTRUMENTS §3, #404, #409, #417): level = liquid + a meniscus line + the number and unit; a deficit hatches the empty part at 45 degrees and adds a word (never a single channel).
/// Level 0 of «Приборы-органы» hides it (the text lines stay), level 1 uses a grey wash, level 2 the ink wash. Not interactive. The same look in every era (laboratory glass does not age).
/// Never put it under an alert plate or in a table: the owner of the layout places it beside the text.</summary>
public sealed partial class FlaskControl : Control
{
    const float W = 100, H = 80, Cx = 50, NeckTop = 2, NeckBottom = 16, Bottom = 56, NeckHalf = 6, BodyHalf = 20, LevelTop = 10;
    const float Slope = (BodyHalf - NeckHalf) / (Bottom - NeckBottom);   // half-width growth per du of height in the cone

    static readonly Vector2[] Outline =
    {
        new(Cx - NeckHalf, NeckTop), new(Cx - NeckHalf, NeckBottom), new(Cx - BodyHalf, Bottom),
        new(Cx + BodyHalf, Bottom), new(Cx + NeckHalf, NeckBottom), new(Cx + NeckHalf, NeckTop), new(Cx - NeckHalf, NeckTop),
    };
    static readonly float[] Marks = { 0.25f, 0.5f, 0.75f };

    public int Ink = Ui.Teal;            // wash of the liquid
    public bool Deficit;                 // hatch the empty part and say so in words
    double _level = -1; string _txt = "", _lang = ""; int _lvl = -1;
    readonly Vector2[] _liq4 = new Vector2[4], _liq6 = new Vector2[6];   // reused by _Draw, no per-frame allocation

    public FlaskControl() { MouseFilter = MouseFilterEnum.Pass; CustomMinimumSize = new Vector2(W, H); }

    /// <summary>Fill 0..1 and the deficit flag; redraws only when something changed.</summary>
    public void Set(double level, bool deficit)
    {
        level = Math.Clamp(level, 0, 1);
        if (Math.Abs(level - _level) < 1e-9 && deficit == Deficit) return;
        _level = level; Deficit = deficit; _lang = "";
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_lvl == InstrumentLogic.OrganLevel && _lang == Loc.Lang) return;
        _lvl = InstrumentLogic.OrganLevel;
        if (_level >= 0) BuildText(); else _lang = Loc.Lang;   // _lang follows also while hidden or unset, so that no redraw is asked every frame
        QueueRedraw();
    }

    // Number, unit and, in a deficit, a word (#417 C3, A5). A deficit never reads 100 %.
    void BuildText()
    {
        _lang = Loc.Lang;
        double pct = Math.Round(_level * 100);
        if (Deficit) pct = Math.Min(pct, 99);
        _txt = pct.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + " %" + (Deficit ? " · " + Loc.T("flask.deficit") : "");
    }

    static float HalfAt(float y) => y <= NeckBottom ? NeckHalf : NeckHalf + (y - NeckBottom) * Slope;

    public override void _Draw()
    {
        if (_level < 0) return;
        if (_lang != Loc.Lang) BuildText();
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
        if (Deficit)
        {
            // the empty part, neck included, so that the deficit keeps its hatching at any level below full
            var hc = new Color(Ui.Ink3, 0.9f);
            HatchBand(NeckTop, Math.Min(yL, NeckBottom), NeckHalf, 0f, hc);
            if (yL > NeckBottom) HatchBand(NeckBottom, yL, NeckHalf - NeckBottom * Slope, Slope, hc);
        }
        for (int i = 0; i < Marks.Length; i++)
        {
            float y = Bottom - Marks[i] * (Bottom - LevelTop);
            DrawLine(new Vector2(Cx + HalfAt(y) + 2, y), new Vector2(Cx + HalfAt(y) + 7, y), Ui.Ink2, 1f);
        }
        DrawPolyline(Outline, Ui.Ink, 1.5f);
        DrawLine(new Vector2(Cx - NeckHalf - 3, NeckTop), new Vector2(Cx + NeckHalf + 3, NeckTop), Ui.Ink, 1.5f);   // rim
        if (_level > 0.004) DrawLine(new Vector2(Cx - HalfAt(yL), yL), new Vector2(Cx + HalfAt(yL), yL), Ui.Ink, 2.5f);   // meniscus line: the level in ink
        DrawString(Fonts.Mono, new Vector2(0, H - 4), _txt, HorizontalAlignment.Center, W, 14, Ui.Ink);
    }

    // 45-degree lines x + y = c clipped to a band y0..y1 whose half-width is a0 + slope * y, by solving the two edge inequalities in y (no clip rect).
    void HatchBand(float y0, float y1, float a0, float slope, Color col)
    {
        for (float c = Cx - BodyHalf + NeckTop; c <= Cx + BodyHalf + Bottom; c += 5f)
        {
            float t = c - Cx;
            var (lo, hi) = InstrumentLogic.HatchSpan(y0, y1, a0, slope, t);
            if (lo < hi) DrawLine(new Vector2(c - lo, lo), new Vector2(c - hi, hi), col, 1f);
        }
    }
}

/// <summary>Caliper (UI_INSTRUMENTS §3, #417, O-008): one large instrument per panel; it edits the share chosen by the owner of the layout. One layout in every era: a fixed and a sliding jaw on a 0..100 % scale with its ends marked,
/// step 5, mouse drag, Left/Right arrows (Up/Down ask for the neighbour share). The era changes only the skin (E1 plain jaws, E2 blocky 3 px ticks, E3 a cursor triangle on the jaw). No vernier.
/// The one number with its unit is the value field beside it (never a second number over the jaw). Raises <see cref="Changed"/> with a value on the grid; the owner sets <see cref="Value"/> back,
/// so the order and the redistribution rules stay where they were.</summary>
public sealed partial class CaliperControl : Control
{
    const float H = 52, Pad = 8, ScaleY = 24;
    public int Value;                          // 0..100, may sit off the grid (the other shares are rounded shares)
    public int Year = 1945;
    public bool Editable = true;
    public event Action<int> Changed;
    public event Action<int> Neighbour;        // -1 up, +1 down
    bool _drag;
    int _shown = int.MinValue, _era, _level = -1;
    readonly Vector2[] _tri = new Vector2[3];

    public CaliperControl()
    {
        CustomMinimumSize = new Vector2(120, H);   // above the 24 du hard minimum and the 32 du target (A10)
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

    public override void _Notification(int what)
    {
        if (what == NotificationFocusEnter || what == NotificationFocusExit) QueueRedraw();
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
        else if (e is InputEventKey { Pressed: true } k)
        {
            if (k.Keycode == Key.Left || k.Keycode == Key.Right) { Emit(InstrumentLogic.CaliperArrow(Value, k.Keycode == Key.Right ? 1 : -1)); AcceptEvent(); }
            else if (k.Keycode == Key.Up || k.Keycode == Key.Down) { Neighbour?.Invoke(k.Keycode == Key.Down ? 1 : -1); AcceptEvent(); }
        }
    }

    void Pick(float x) => Emit(InstrumentLogic.CaliperSnap((x - X0) / (X1 - X0) * 100.0));
    void Emit(int v) { if (v != Value) Changed?.Invoke(v); }

    public override void _Draw()
    {
        float x0 = X0, x1 = X1, xv = XOf(Value), step = (x1 - x0) / 20f;
        var fill = InstrumentLogic.OrganLevel == 2 ? Ui.InkWash[Ui.Sand] : Ui.Hover;
        DrawRect(new Rect2(x0, ScaleY - 8, xv - x0, 5), fill);   // between the jaws: decoration, the jaw position and the number carry the value
        DrawLine(new Vector2(x0, ScaleY), new Vector2(x1, ScaleY), Ui.Ink, 1f);
        // ticks every 5 points: 6 px, 9 px every 10, 12 px at the two ends; E2 draws them 3 px wide
        float tw = _era == 2 ? 3f : 1f;
        for (int i = 0; i <= 20; i++)
        {
            float x = x0 + i * step, len = i == 0 || i == 20 ? 12 : i % 2 == 0 ? 9 : 6;
            DrawLine(new Vector2(x, ScaleY), new Vector2(x, ScaleY + len), i % 2 == 0 ? Ui.Ink : Ui.Ink2, i == 0 || i == 20 ? 2f : tw);
        }
        // the ends are named: 0 and 100
        DrawString(Fonts.Mono, new Vector2(x0 - 4, H - 3), "0", HorizontalAlignment.Left, -1, 14, Ui.Ink);
        DrawString(Fonts.Mono, new Vector2(x1 - 30, H - 3), "100", HorizontalAlignment.Right, 34, 14, Ui.Ink);
        DrawLine(new Vector2(x0, 6), new Vector2(x0, ScaleY + 8), Ui.Ink, 2f);
        DrawLine(new Vector2(xv, 6), new Vector2(xv, ScaleY + 8), Ui.Ink, 2.5f);
        if (_era == 3)
        {
            _tri[0] = new Vector2(xv, ScaleY - 9); _tri[1] = new Vector2(xv - 4, ScaleY - 16); _tri[2] = new Vector2(xv + 4, ScaleY - 16);
            DrawColoredPolygon(_tri, Ui.Ink);
        }
        else DrawLine(new Vector2(xv, 6), new Vector2(xv + 8, 6), Ui.Ink, 2.5f);   // E1, E2: the arm of the sliding jaw
        if (HasFocus()) DrawRect(new Rect2(1, 1, Size.X - 2, Size.Y - 2), Ui.Ink, false, 2f);   // focus frame, 2 px (A7)
    }
}

/// <summary>The value field of the caliper (#417 C1, C2): it looks editable by itself, a 1 px frame, no underline (an underline reads as a link). A pencil, 16 du long, shows only while its caliper has focus
/// or the field holds an unsent edit. A child of the number label, drawn over its right-aligned text. Hidden at level 0 of «Приборы-органы».</summary>
public sealed partial class PencilMarker : Control
{
    bool _active, _editable, _focus; int _level = -1;
    readonly Control _target;
    readonly Vector2[] _body = new Vector2[4], _tip = new Vector2[3];

    /// <param name="target">The control whose focus shows the pencil.</param>
    public PencilMarker(Control target) { _target = target; MouseFilter = MouseFilterEnum.Ignore; SetAnchorsPreset(LayoutPreset.FullRect); }

    /// <param name="editable">Own power: the field gets its frame.</param>
    /// <param name="active">The field holds an unsent edit.</param>
    public void Set(bool editable, bool active)
    {
        if (editable == _editable && active == _active) return;
        _editable = editable; _active = active;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        bool focus = _target != null && _target.HasFocus();
        if (InstrumentLogic.OrganLevel == _level && focus == _focus) return;
        _level = InstrumentLogic.OrganLevel; _focus = focus;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_editable || _level == 0) return;
        DrawRect(new Rect2(0.5f, 0.5f, Size.X - 1, Size.Y - 1), _focus ? Ui.Ink : Ui.Ink3, false, 1f);
        if (!_active && !_focus) return;
        // pencil lying at about -18 degrees: body, tip, lead; 16 du long
        const float c = 0.951f, s = -0.309f;   // cos and sin of -18 degrees
        Vector2 O = new(3, Size.Y * 0.5f + 4);
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
