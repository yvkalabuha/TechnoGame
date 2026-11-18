using System;
using Godot;

namespace Singularity.Client.Spike;

/// <summary>Left rail of the areas (CLIENT_V05C_SPEC §4, §7; decision #360): the map and seven areas, narrow by default (icon + hotkey letter), wide with names.
/// Selected state is told by form as well as colour (frame, 3 du domain edge, ▸ on the right edge). A locked area is dashed, hatched, with a lock and no badge.</summary>
public sealed partial class Rail : PanelContainer
{
    public const int NarrowWidth = 60, WideWidth = 196;

    /// <summary>Draws the glyphs of a button (icon, letter, name, badge) on a child node, so that the paper texture of the button never touches them.</summary>
    sealed partial class Glyphs : Control
    {
        public Action<Glyphs> Paint;
        public override void _Draw() => Paint?.Invoke(this);
    }

    sealed partial class AreaButton : Button, IGlyphBounds
    {
        public readonly AreaDef Def;
        public bool Sel, Wide; public int Count, Sev;
        readonly Glyphs _g = new() { MouseFilter = MouseFilterEnum.Ignore };

        public AreaButton(AreaDef def)
        {
            Def = def; FocusMode = FocusModeEnum.All; Text = ""; ClipText = false;
            _g.SetAnchorsPreset(LayoutPreset.FullRect);
            _g.Paint = PaintGlyphs;
            AddChild(_g);
            AddThemeStyleboxOverride("normal", Ui.Box(Colors.Transparent, null, 0, 0, Ui.R2));
            AddThemeStyleboxOverride("focus", new StyleBoxFlat { DrawCenter = false, BorderColor = Ui.Ink, AntiAliasing = true, ExpandMarginLeft = 2, ExpandMarginRight = 2, ExpandMarginTop = 2, ExpandMarginBottom = 2 }
                .With2(s => { s.SetBorderWidthAll(2); s.SetCornerRadiusAll(4); }));
            PaperSurface.Apply(this, SurfaceKind.Button, "rail." + def.Slug);
            Restyle();
        }

        public bool Locked => Def.State == AreaState.Locked;

        // Ink of the button for T6 (the badge is an untextured alert plate on its own layer and is left out).
        public System.Collections.Generic.IEnumerable<Rect2> GlyphRects()
        {
            var t = GetGlobalTransform(); var s = Size;
            Rect2 G(Rect2 r) => new(t * r.Position, r.Size * t.Scale);
            if (!Wide) { yield return G(new Rect2((s.X - 24) / 2f - 2, 7, 24, 24)); yield return G(new Rect2(s.X / 2f - 6, s.Y - 17, 12, 9)); }
            else { yield return G(new Rect2(10, (s.Y - 24) / 2f, 24, 24)); yield return G(new Rect2(44, (s.Y - 17) / 2f, s.X - 88, 17)); }
        }

        public void Restyle()
        {
            // hover: hover paper + a 1 du ink-2 line at the bottom; pressed: hover paper + 2 du border; selected: hover paper + 1 du frame (the edge and ▸ are drawn on the glyph layer).
            var hover = Ui.Box(Ui.Hover, null, 0, 0, Ui.R2); hover.BorderWidthBottom = 1; hover.BorderColor = Ui.Ink2;
            var pressed = Ui.Box(Ui.Hover, Ui.Border, 2, 0, Ui.R2);
            var sel = Ui.Box(Ui.Hover, Ui.Border, 1, 0, Ui.R2);
            AddThemeStyleboxOverride("normal", Sel ? sel : Ui.Box(Colors.Transparent, null, 0, 0, Ui.R2));
            AddThemeStyleboxOverride("hover", Sel ? sel : hover);
            AddThemeStyleboxOverride("pressed", pressed);
            _g.QueueRedraw();
        }

        void PaintGlyphs(Glyphs g)
        {
            var s = Size; bool locked = Locked;
            var (line, _, _) = UiArt.Inks(Def);
            var icon = UiArt.Icon(Def.Slug);
            float iconA = locked ? 0.45f : 1f;
            Rect2 iconRect;
            if (!Wide)
            {
                iconRect = new Rect2((s.X - 24) / 2f - 2, 7, 24, 24);
                string l = Def.Letter == '\0' ? "Esc" : Def.Letter.ToString();
                g.DrawString(Fonts.Mono, new Vector2(0, s.Y - 8), l, HorizontalAlignment.Center, s.X - 4, 12, Ui.Ink3);
            }
            else
            {
                iconRect = new Rect2(10, (s.Y - 24) / 2f, 24, 24);
                string l = Def.Letter == '\0' ? "Esc" : Def.Letter.ToString();
                g.DrawString(Fonts.Mono, new Vector2(s.X - 40, s.Y / 2f + 5), l, HorizontalAlignment.Right, 30, 14, Ui.Ink3);
                float w = s.X - 44 - 44;
                var font = Ui.FontOf(FontKind.Text);
                string name = Loc.T("area." + Def.Slug);
                var lines = font.GetMultilineStringSize(name, HorizontalAlignment.Left, w, 14);
                float y0 = (s.Y - lines.Y) / 2f + font.GetAscent(14);
                g.DrawMultilineString(font, new Vector2(44, y0), name, HorizontalAlignment.Left, w, 14, -1, locked ? Ui.Ink3 : Ui.Ink);
            }
            g.DrawTextureRect(icon, iconRect, false, new Color(1, 1, 1, iconA));
            if (locked)
            {
                Hatching.Draw(g, new Rect2(1, 1, s.X - 2, s.Y - 2), new Color(Ui.Ink, 0.07f), 6f);
                var c = Ui.Ink3;
                g.DrawDashedLine(new Vector2(0, 0), new Vector2(s.X, 0), c, 1f, 3f); g.DrawDashedLine(new Vector2(s.X, 0), s, c, 1f, 3f);
                g.DrawDashedLine(s, new Vector2(0, s.Y), c, 1f, 3f); g.DrawDashedLine(new Vector2(0, s.Y), Vector2.Zero, c, 1f, 3f);
                g.DrawTextureRect(UiArt.Icon("lock"), new Rect2(Wide ? s.X - 38 : s.X - 17, Wide ? s.Y - 17 : s.Y - 17, 12, 12), false, new Color(1, 1, 1, 0.9f));
            }
            if (Sel)
            {
                // Domain edge (3 du; the seventh ink is hatched) and a triangle on the right edge: a second channel besides colour.
                g.DrawRect(new Rect2(0, 2, 4, s.Y - 4), line);
                if (Def.Ink == AreaData.Seventh) Hatching.Draw(g, new Rect2(0, 2, 4, s.Y - 4), Ui.Sheet, 4f);
                g.DrawColoredPolygon(new[] { new Vector2(s.X + 1, s.Y / 2 - 5), new Vector2(s.X - 5, s.Y / 2), new Vector2(s.X + 1, s.Y / 2 + 5) }, Ui.Border);
            }
            if (Count > 0 && !locked) DrawBadge(g, s);
        }

        // A real alert: halo, ink outline, sign by the worst severity, the count. Never textured, so it sits on the glyph layer.
        void DrawBadge(Glyphs g, Vector2 s)
        {
            var r = new Rect2(s.X - 24, 1, 26, 16);
            bool alarm = Sev >= 2;
            g.DrawRect(r.Grow(2), Ui.Sheet);
            g.DrawRect(r, alarm ? Ui.Alarm : Ui.Warn);
            g.DrawRect(r, Ui.Ink, false, 1f);
            var ink = alarm ? Colors.White : Ui.Ink;
            if (alarm) g.DrawRect(new Rect2(r.Position.X + 4, r.Position.Y + 4, 8, 8), ink);
            else g.DrawColoredPolygon(new[] { new Vector2(r.Position.X + 8, r.Position.Y + 3), new Vector2(r.Position.X + 13, r.Position.Y + 12), new Vector2(r.Position.X + 3, r.Position.Y + 12) }, ink);
            g.DrawString(Fonts.Mono, new Vector2(r.Position.X + 13, r.Position.Y + 13), Count >= 10 ? "9+" : Count.ToString(), HorizontalAlignment.Left, 12, 12, ink);
        }
    }

    readonly AreaButton[] _buttons = new AreaButton[AreaData.All.Length];
    readonly VBoxContainer _col = new();
    readonly CaptionButton _toggle;
    readonly PanelContainer _tip = new() { TopLevel = true, Visible = false, MouseFilter = MouseFilterEnum.Ignore };
    readonly Label _tipText = Ui.Lbl("", 14);
    readonly Control[] _seps = new Control[3];
    AreaId _selected = AreaId.Map;
    public bool Wide { get; private set; }
    public int RailWidth => Wide ? WideWidth : NarrowWidth;
    public event Action<AreaId> Pressed;
    public event Action ToggleWide;

    public Rail()
    {
        AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Ui.Paper, BorderColor = Ui.Border, BorderWidthRight = 1, ContentMarginLeft = 4, ContentMarginRight = 4, ContentMarginTop = 8, ContentMarginBottom = 8 });
        PaperSurface.Apply(this, SurfaceKind.Paper, "rail");
        _col.AddThemeConstantOverride("separation", 4);
        AddChild(_col);
        int sep = 0, group = 0; AreaDef prev = null;
        foreach (var def in AreaData.All)
        {
            // Locked buttons are already dashed: no rule line between two locked groups.
            bool bothLocked = prev != null && prev.State == AreaState.Locked && def.State == AreaState.Locked;
            if (def.Group != group && bothLocked) group = def.Group;
            else if (def.Group != group) { var l = new ColorRect { Color = Ui.Rule, CustomMinimumSize = new Vector2(0, 1) }; _seps[sep++] = l; _col.AddChild(l); group = def.Group; }
            prev = def;
            var b = new AreaButton(def);
            _buttons[(int)def.Id] = b;
            b.Pressed += () => Pressed?.Invoke(def.Id);
            // A mouse click must not leave the keyboard focus on the button (Space pauses the game); keyboard activation keeps it.
            b.GuiInput += e => { if (e is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left }) b.CallDeferred(Control.MethodName.ReleaseFocus); };
            b.MouseEntered += () => ShowTip(b);
            b.MouseExited += HideTip;
            b.FocusEntered += () => ShowTip(b);
            b.FocusExited += HideTip;
            _col.AddChild(b);
        }
        _col.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        _toggle = Ui.Btn("«", () => ToggleWide?.Invoke());
        _toggle.FocusMode = FocusModeEnum.All; _toggle.TooltipText = "tip.rail"; _toggle.CustomMinimumSize = new Vector2(0, 30);
        _col.AddChild(_toggle);
        _tip.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Sheet, Ui.Border, 1, 8, Ui.R2));
        _tip.AddChild(_tipText);
        AddChild(_tip);
        Loc.Changed += Refresh;
        TreeExiting += () => Loc.Changed -= Refresh;
        SetWide(false);
    }

    public void SetWide(bool wide)
    {
        Wide = wide;
        foreach (var b in _buttons) { b.Wide = wide; b.Restyle(); }
        _toggle.Text = wide ? "«" : "»";
        OffsetRight = OffsetLeft + RailWidth;
        Layout();
    }

    public void Select(AreaId id)
    {
        _selected = id;
        foreach (var b in _buttons) { b.Sel = b.Def.Id == id; b.Restyle(); }
    }

    public void SetBadge(AreaId id, int count, int severity)
    {
        var b = _buttons[(int)id];
        if (b.Count == count && b.Sev == severity) return;
        b.Count = count; b.Sev = severity; b.Restyle();
    }

    /// <summary>The state of every area button as text for the log: selected, locked, badge.</summary>
    public AreaId Selected => _selected;

    /// <summary>Puts the keyboard focus on an area button (the focus ring and the tip show).</summary>
    public void FocusArea(AreaId id) => _buttons[(int)id].GrabFocus();

    /// <summary>Centre of an area button in window pixels (screenshot run: real mouse events).</summary>
    public Vector2 ButtonCenter(AreaId id) { var b = _buttons[(int)id]; return b.GlobalPosition + b.Size * b.GetGlobalTransform().Scale / 2f; }

    /// <summary>The badge of an area as text for the log: count and severity, or none.</summary>
    public string BadgeText(AreaId id) { var b = _buttons[(int)id]; return b.Count == 0 ? "no badge" : $"{(b.Sev >= 2 ? "■" : "▲")} {b.Count}"; }

    void Refresh() { foreach (var b in _buttons) b.Restyle(); }

    // Button height follows the free height (52 du, down to 40), so that eight areas fit without a scroll bar also at 150% on 1280x720.
    const float FixedHeight = 93f;
    void Layout()
    {
        float h = Math.Clamp((Size.Y - FixedHeight) / 8f, 40f, 52f);
        foreach (var b in _buttons) b.CustomMinimumSize = new Vector2(Wide ? WideWidth - 12 : 52, Wide ? Math.Min(h, 44f) : h);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized) Layout();
    }

    void ShowTip(AreaButton b)
    {
        if (Wide && !b.Locked) return;   // the wide rail names its buttons; the tip is for the narrow rail and for the term of a locked area
        string name = Loc.T("area." + b.Def.Slug);
        string key = b.Def.Letter == '\0' ? "Esc" : b.Def.Letter.ToString();
        _tipText.Text = b.Locked ? Loc.T("tip.area.locked", name, key, b.Def.Milestone) : Loc.T("tip.area", name, key);
        _tip.Visible = true;
        _tip.ResetSize();
        _tip.GlobalPosition = new Vector2(b.GlobalPosition.X + b.Size.X * GetGlobalTransform().Scale.X + 8, b.GlobalPosition.Y + (b.Size.Y * GetGlobalTransform().Scale.Y - _tip.Size.Y) / 2f);
    }

    void HideTip() => _tip.Visible = false;
}
