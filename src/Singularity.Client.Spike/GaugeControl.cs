using System;
using Godot;

namespace Singularity.Client.Spike;

/// <summary>The game-speed gauge (UI_INSTRUMENTS §3, #404, #409, O-008): a layer-2 instrument drawn procedurally, no assets. One layout in every era: a tick scale with a marker, and the tempo word beside it (the label of the gauge in the bar).
/// The era (E1 1945–69, E2 1970–99, E3 2000–2100) changes only the skin of the ticks and of the marker, never how the value is read. Level 0 of the setting «Приборы-органы» gives the plain four-cell meter («before»).
/// No colour carries a value here. Not interactive: the keys and tooltips stay with the buttons.</summary>
public sealed partial class GaugeControl : Control
{
    static readonly int[] Ink = { Ui.Teal, Ui.Olive, Ui.Plum, Ui.Sand };   // the plain cells of the «before» look
    const float InstrW = 84, InstrH = 36, BeforeW = 46, BeforeH = 28, MoveSeconds = 0.12f;

    public int Filled;               // speed step 0..3
    public int Year = 1945;
    int _era, _level = -1;
    float _pos;                      // displayed step; the marker travels to Filled in 120 ms (at once with «reduce motion»)

    public GaugeControl() { MouseFilter = MouseFilterEnum.Ignore; Sync(); }

    void Sync()
    {
        int level = InstrumentLogic.OrganLevel, era = InstrumentLogic.Era(Year);
        if (level == _level && era == _era) return;
        _level = level; _era = era;
        CustomMinimumSize = level == 0 ? new Vector2(BeforeW, BeforeH) : new Vector2(InstrW, InstrH);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        Sync();
        if (Mathf.IsEqualApprox(_pos, Filled)) return;
        _pos = Settings.Current.Animations ? Mathf.MoveToward(_pos, Filled, (float)delta * 3f / MoveSeconds) : Filled;
        QueueRedraw();
    }

    readonly Vector2[] _tri = new Vector2[3];   // reused by the E3 marker, no per-frame allocation

    public override void _Draw()
    {
        if (_level == 0) { DrawBefore(); return; }
        float y0 = Size.Y - InstrH, sy = y0 + 26;
        DrawLine(new Vector2(6, sy), new Vector2(78, sy), Ui.Ink, 1f);
        // four step ticks (pause, day, week, month), 12 px high and 2 px wide in every era; the fourth is the dashed reserve
        for (int i = 0; i < 4; i++)
        {
            float x = 12 + i * 20;
            if (i == 3) { DrawDashedLine(new Vector2(x, sy - 6), new Vector2(x, sy + 6), Ui.Ink2, 1.5f, 2f); continue; }
            if (_era == 2) DrawRect(new Rect2(x - 1.5f, sy - 6, 3, 12), Ui.Ink);   // E2: blocky tick
            else DrawLine(new Vector2(x, sy - 6), new Vector2(x, sy + 6), Ui.Ink, 2f);
        }
        float cx = 12 + _pos * 20;
        if (_era == 1)
        {
            // E1: a needle with a round head
            DrawLine(new Vector2(cx, sy + 8), new Vector2(cx, y0 + 12), Ui.Ink, 2.5f);
            DrawCircle(new Vector2(cx, y0 + 10), 3.5f, Ui.Ink);
        }
        else if (_era == 2)
        {
            // E2: a solid block over the scale
            DrawRect(new Rect2(cx - 4, y0 + 5, 8, 8), Ui.Ink);
            DrawLine(new Vector2(cx, y0 + 13), new Vector2(cx, sy + 8), Ui.Ink, 2f);
        }
        else
        {
            // E3: a triangle cursor on a 2 px stroke
            DrawLine(new Vector2(cx, sy - 8), new Vector2(cx, sy + 8), Ui.Ink, 2f);
            _tri[0] = new Vector2(cx, sy - 8); _tri[1] = new Vector2(cx - 4, sy - 15); _tri[2] = new Vector2(cx + 4, sy - 15);
            DrawColoredPolygon(_tri, Ui.Ink);
        }
    }

    void DrawDashedRect(Rect2 r, Color c)
    {
        DrawDashedLine(r.Position, new Vector2(r.End.X, r.Position.Y), c, 1f, 2f); DrawDashedLine(new Vector2(r.End.X, r.Position.Y), r.End, c, 1f, 2f);
        DrawDashedLine(r.End, new Vector2(r.Position.X, r.End.Y), c, 1f, 2f); DrawDashedLine(new Vector2(r.Position.X, r.End.Y), r.Position, c, 1f, 2f);
    }

    // «Before» (CLIENT_V05C_SPEC §2): four cells 7/11/15/19 high; filled in their own ink, the current one ringed, empty hatched at 45 degrees; the fourth is the dashed reserve.
    static readonly float[] Heights = { 7, 11, 15, 19 };

    void Hatch(Rect2 r, Color col)
    {
        for (float a = r.Position.X - r.Size.Y; a < r.End.X; a += 4f)
        {
            float s0 = Math.Max(0, r.Position.X - a), s1 = Math.Min(r.Size.Y, r.End.X - a);
            if (s0 < s1) DrawLine(new Vector2(a + s0, r.End.Y - s0), new Vector2(a + s1, r.End.Y - s1), col, 1f);
        }
    }

    void DrawBefore()
    {
        for (int i = 0; i < 4; i++)
        {
            var r = new Rect2(4 + i * 10, Size.Y - 4 - Heights[i], 8, Heights[i]);
            if (i == 3) { DrawDashedRect(r, Ui.Ink3); continue; }
            if (i < Filled) DrawRect(r, Ui.InkLine[Ink[i]]); else Hatch(r, new Color(Ui.Ink, 0.45f));
            DrawRect(r, Ui.Ink, false, 1f);
            if (i == Filled - 1) DrawRect(r.Grow(2), Ui.Ink, false, 1.5f);
        }
    }
}
