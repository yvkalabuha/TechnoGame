using System;
using Godot;

namespace Singularity.Client.Spike;

/// <summary>Left rail of the areas (CLIENT_V05C_SPEC §4, §7; decision #360): the map and seven areas, narrow by default (icon + hotkey letter), wide with names.
/// Selected state is told by form as well as colour (frame, 3 du domain edge, ▸ on the right edge). A locked area is dashed, hatched, with a lock and no badge.</summary>
public sealed partial class Rail : PanelContainer
{
    public const int NarrowWidth = RailMetrics.NarrowWidth, WideWidth = RailMetrics.WideWidth;

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

        string LetterText => Def.Letter == '\0' ? "Esc" : Def.Letter.ToString();
        bool Badged => Count > 0 && !Locked;
        ButtonRects Rects => RailMetrics.Of(Wide, Locked, Badged, LetterText.Length);

        // Wide button: the text block (name lines, then the lock caption) and where it starts.
        (Vector2 Name, float Cap, float Top) NameBlock(ButtonRects r)
        {
            var font = Ui.FontOf(FontKind.Text);
            var name = font.GetMultilineStringSize(Loc.T("area." + Def.Slug), HorizontalAlignment.Left, (float)r.NameW, 14);
            float cap = r.Caption ? font.GetHeight(RailMetrics.CaptionFontDu) : 0;   // C4: the reason of the lock is on the button, in words
            return (name, cap, (float)RailMetrics.CentreTop(r.H, name.Y + cap));
        }

        // Ink of the button for T6, the same boxes that PaintGlyphs draws (the badge too: it is an untextured plate, but it must keep its distance as well).
        public System.Collections.Generic.IEnumerable<Rect2> GlyphRects()
        {
            var t = GetGlobalTransform(); var r = Rects;
            Rect2 G(Box b) => new(t * new Vector2((float)b.X, (float)b.Y), new Vector2((float)b.W, (float)b.H) * t.Scale);
            yield return G(r.Icon); yield return G(r.Letter);
            if (r.Badge is { } bd) yield return G(bd);
            if (r.LockInk is { } lk) yield return G(lk);
            if (Wide)
            {
                var (name, cap, top) = NameBlock(r);
                yield return G(new Box(r.NameX, top, Math.Min(name.X, r.NameW), name.Y));
                if (r.Caption) yield return G(new Box(r.NameX, top + name.Y, r.NameW, cap));
            }
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
            var r = Rects;
            float iconA = locked ? 0.45f : 1f;   // the icon stays (muted); the lock is the common sign of the state
            g.DrawTextureRect(UiArt.Icon(Def.Slug), new Rect2((float)r.Icon.X, (float)r.Icon.Y, (float)r.Icon.W, (float)r.Icon.H), false, new Color(1, 1, 1, iconA));
            // A1: a hotkey letter is a value, 14 du (cap height 10 px)
            g.DrawString(Fonts.Mono, new Vector2((float)r.Letter.X, (float)r.LetterBase), LetterText, HorizontalAlignment.Left, -1, RailMetrics.LetterFontDu, Wide && !locked ? Ui.Ink3 : Ui.Ink2);
            if (Wide)
            {
                var font = Ui.FontOf(FontKind.Text);
                var (name, _, top) = NameBlock(r);
                float y0 = top + font.GetAscent(14);
                g.DrawMultilineString(font, new Vector2((float)r.NameX, y0), Loc.T("area." + Def.Slug), HorizontalAlignment.Left, (float)r.NameW, 14, -1, locked ? Ui.Ink3 : Ui.Ink);
                if (locked) g.DrawString(font, new Vector2((float)r.NameX, top + name.Y + font.GetAscent(RailMetrics.CaptionFontDu)), Loc.T("area.locked.cap"), HorizontalAlignment.Left, (float)r.NameW, RailMetrics.CaptionFontDu, Ui.Ink2);
            }
            if (locked)
            {
                Hatching.Draw(g, new Rect2(1, 1, s.X - 2, s.Y - 2), new Color(Ui.Ink, 0.07f), 6f);
                var c = Ui.Ink3;
                g.DrawDashedLine(new Vector2(0, 0), new Vector2(s.X, 0), c, 1f, 3f); g.DrawDashedLine(new Vector2(s.X, 0), s, c, 1f, 3f);
                g.DrawDashedLine(s, new Vector2(0, s.Y), c, 1f, 3f); g.DrawDashedLine(new Vector2(0, s.Y), Vector2.Zero, c, 1f, 3f);
                // A2: the SVG body is 14/24 of the drawn box, so a 28 du box gives a visible lock of 16 px and more.
                var lb = r.LockDraw;
                g.DrawTextureRect(UiArt.Icon("lock"), new Rect2((float)lb.X, (float)lb.Y, (float)lb.W, (float)lb.H), false);
            }
            if (Sel)
            {
                // Domain edge (3 du; the seventh ink is hatched) and a triangle on the right edge: a second channel besides colour.
                g.DrawRect(new Rect2(0, 2, 4, s.Y - 4), line);
                if (Def.Ink == AreaData.Seventh) Hatching.Draw(g, new Rect2(0, 2, 4, s.Y - 4), Ui.Sheet, 4f);
                var m = r.Marker;
                g.DrawColoredPolygon(new[] { new Vector2((float)m.R, (float)m.Y), new Vector2((float)m.X, (float)(m.Y + m.H / 2)), new Vector2((float)m.R, (float)m.B) }, Ui.Border);
            }
            if (r.Badge is { } bd) DrawBadge(g, bd);
        }

        // A real alert: ink outline, sign by the worst severity, the count. Never textured, so it sits on the glyph layer. No halo: it would run over the icon.
        void DrawBadge(Glyphs g, Box bd)
        {
            var r = new Rect2((float)bd.X, (float)bd.Y, (float)bd.W, (float)bd.H);
            bool alarm = Sev >= 2;
            g.DrawRect(r, alarm ? Ui.Alarm : Ui.Warn);
            g.DrawRect(r.Grow(-0.5f), Ui.Ink, false, 1f);   // the 1 du line is centred on the rect it is given: inset by half so that the ink box equals the plate rect (T6)
            var ink = alarm ? Colors.White : Ui.Ink;
            float x = r.Position.X, y = r.Position.Y;
            if (alarm) g.DrawRect(new Rect2(x + RailMetrics.BadgeSignX, y + RailMetrics.BadgeSignY, RailMetrics.BadgeSignSize, RailMetrics.BadgeSignSize), ink);
            else g.DrawColoredPolygon(new[] { new Vector2(x + RailMetrics.BadgeTriApexX, y + RailMetrics.BadgeTriTop), new Vector2(x + RailMetrics.BadgeTriRight, y + RailMetrics.BadgeTriBottom), new Vector2(x + RailMetrics.BadgeTriLeft, y + RailMetrics.BadgeTriBottom) }, ink);
            g.DrawString(Fonts.Mono, new Vector2(x + RailMetrics.BadgeTextX, y + RailMetrics.BadgeTextBase), Count >= 10 ? "9+" : Count.ToString(), HorizontalAlignment.Left, RailMetrics.BadgeTextW, RailMetrics.BadgeFontDu, ink);   // A1: a count is a value, >= 13 du and a digit >= 9 px
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
        AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Ui.Paper, BorderColor = Ui.Border, BorderWidthRight = 1, ContentMarginLeft = RailMetrics.SideMargin(false), ContentMarginRight = RailMetrics.SideMargin(false), ContentMarginTop = 8, ContentMarginBottom = 8 });
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
        if (GetThemeStylebox("panel") is StyleBoxFlat sb) { sb.ContentMarginLeft = sb.ContentMarginRight = RailMetrics.SideMargin(wide); }
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

    // Button sizes are fixed by the spec (58 du narrow; 44, locked 52 wide): eight areas fit without a scroll bar down to 1280x720 du (checked in the shot run).
    void Layout()
    {
        foreach (var b in _buttons) b.CustomMinimumSize = new Vector2(RailMetrics.ButtonW(Wide), RailMetrics.ButtonH(Wide, b.Locked));
    }

    /// <summary>Widths of the longest word of every area name at 14 du against the name column, for the log (the spec: the name must not be cut).</summary>
    public string NameFitLog()
    {
        var font = Ui.FontOf(FontKind.Text); string log = ""; float worst = 0, worstLocked = 0;
        foreach (var d in AreaData.All)
        {
            string name = Loc.T("area." + d.Slug);
            foreach (var word in name.Split(' ')) worst = Math.Max(worst, font.GetStringSize(word, HorizontalAlignment.Left, -1, 14).X);
            if (d.State == AreaState.Locked) worstLocked = Math.Max(worstLocked, font.GetStringSize(name, HorizontalAlignment.Left, -1, 14).X);
        }
        float cap = font.GetStringSize(Loc.T("area.locked.cap"), HorizontalAlignment.Left, -1, RailMetrics.CaptionFontDu).X;
        log += $"rail names ({Loc.Lang}): longest word {worst:F1} du of {RailMetrics.WideNameW} {(worst <= RailMetrics.WideNameW ? "PASS" : "FAIL")}; locked name on one line {worstLocked:F1} of {RailMetrics.WideLockedNameW} {(worstLocked <= RailMetrics.WideLockedNameW ? "PASS" : "FAIL")}; caption {cap:F1} of {RailMetrics.WideLockedNameW} {(cap <= RailMetrics.WideLockedNameW ? "PASS" : "FAIL")}\n";
        // Text block height of every wide button against the 5 du margin the spec asks for.
        foreach (var d in AreaData.All)
        {
            bool locked = d.State == AreaState.Locked;
            float w = locked ? RailMetrics.WideLockedNameW : RailMetrics.WideNameW;
            float h = font.GetMultilineStringSize(Loc.T("area." + d.Slug), HorizontalAlignment.Left, w, 14).Y + (locked ? font.GetHeight(RailMetrics.CaptionFontDu) : 0);
            float margin = (RailMetrics.ButtonH(true, locked) - h) / 2f;
            log += $"  wide {d.Slug}: text block {h:F1} du, margin {margin:F1} {(margin >= 5 - 1e-3 ? "PASS" : "FAIL")}\n";
        }
        bool lh = Math.Abs(font.GetHeight(14) - RailMetrics.Line14Du) < 0.5f && Math.Abs(font.GetHeight(RailMetrics.CaptionFontDu) - RailMetrics.Line12Du) < 0.5f;
        return log + $"  line height at 14 du {font.GetHeight(14):F1}, caption at 12 du {font.GetHeight(RailMetrics.CaptionFontDu):F1} (constants {RailMetrics.Line14Du}, {RailMetrics.Line12Du}): {(lh ? "PASS" : "FAIL")}\n";
    }

    /// <summary>Check for the log: the badge of every button lies inside it and touches neither the icon nor the letter.</summary>
    public string BadgeFitLog()
    {
        string log = ""; int bad = 0, n = 0;
        foreach (var wide in new[] { false, true })
            foreach (var d in AreaData.All)
            {
                bool locked = d.State == AreaState.Locked, mapNarrow = !wide && d.Id == AreaId.Map;   // Map never carries a badge, and narrow "Esc" would not fit beside one
                var r = RailMetrics.Of(wide, locked, !locked && !mapNarrow, d.Letter == '\0' ? 3 : 1); n++;
                var marks = new System.Collections.Generic.List<Box> { r.Letter, r.Marker };
                if (r.Badge is { } bd) marks.Add(bd);
                if (r.LockInk is { } lk) marks.Add(lk);
                bool ok = true;
                for (int i = 0; i < marks.Count; i++)
                {
                    if (marks[i].Intersects(r.Icon) && !marks[i].Equals(r.Marker)) ok = false;
                    for (int j = i + 1; j < marks.Count; j++) if (marks[i].Intersects(marks[j])) ok = false;
                }
                if (r.Badge is { } b2 && b2.EdgeGap(r.W, r.H) < 5 - 1e-9) ok = false;
                if (wide && r.Marker.X - r.Letter.R < 6) ok = false;
                if (wide && r.Badge is { } b3 && b3.X - (r.NameX + r.NameW) < 6) ok = false;
                if (!ok) { bad++; log += $"  layout fails: {d.Slug} wide {wide}\n"; }
            }
        return $"rail geometry (letter, marker, badge, lock do not intersect; letter-marker >= 6, name-badge >= 6, badge 5 du from the edge): {(bad == 0 ? "PASS" : "FAIL")} ({n} buttons)\n" + log;
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
