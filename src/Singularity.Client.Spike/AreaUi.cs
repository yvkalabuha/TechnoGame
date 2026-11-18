using System;
using System.Collections.Generic;
using Godot;

namespace Singularity.Client.Spike;

/// <summary>Icons and pictures of the areas (CLIENT_V05C_SPEC §5, §6). SVGs are rasterised at start of use in the size the UI scale needs, so they stay sharp at 150%.</summary>
public static class UiArt
{
    static readonly Dictionary<string, ImageTexture> Cache = new();   // lookup only

    static Image Raster(string res, float scale)
    {
        var img = new Image();
        string svg = FileAccess.GetFileAsString(res);
        if (svg == "" || img.LoadSvgFromString(svg, scale) != Error.Ok) { GD.PushWarning("UiArt: cannot load " + res); return Image.CreateEmpty(24, 24, false, Image.Format.Rgba8); }
        return img;
    }

    /// <summary>Line icon of an area (24 du; drawn from a 2x raster when the UI scale is above 125%).</summary>
    public static ImageTexture Icon(string slug)
    {
        float scale = Settings.Current.UiScale > 125 ? 2f : 1f;
        string key = "i|" + slug + "|" + scale;
        if (!Cache.TryGetValue(key, out var t)) Cache[key] = t = ImageTexture.CreateFromImage(Raster($"res://assets/ui/area-icons/{slug}-24.svg", scale));
        return t;
    }

    /// <summary>Picture 320x120 du, rasterised at 2x with mipmaps. A locked area gets the same picture in grey, contrast 0.8, 55% opaque, with 45-degree hatching (§6).</summary>
    public static ImageTexture Picture(string slug, bool locked)
    {
        string key = "p|" + slug + "|" + locked;
        if (Cache.TryGetValue(key, out var t)) return t;
        var img = Raster($"res://assets/ui/area-pictures/{slug}.svg", 2f);
        if (locked)
        {
            img.Convert(Image.Format.Rgba8);
            for (int y = 0; y < img.GetHeight(); y++)
                for (int x = 0; x < img.GetWidth(); x++)
                {
                    var c = img.GetPixel(x, y);
                    float g = 0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B;
                    g = (g - 0.5f) * 0.8f + 0.5f;
                    if ((x + y) % 12 < 2) g *= 0.85f;   // hatching
                    img.SetPixel(x, y, new Color(g, g, g, 0.55f));
                }
        }
        img.GenerateMipmaps();
        return Cache[key] = ImageTexture.CreateFromImage(img);
    }

    /// <summary>Line, wash and text colours of an area's domain ink; neutral areas use ink on hover paper; the seventh ink is moss (always hatched).</summary>
    public static (Color Line, Color Wash, Color Text) Inks(AreaDef a) =>
        a.Ink == AreaData.Neutral ? (Ui.Ink, Ui.Hover, Ui.Ink)
        : a.Ink == AreaData.Seventh ? (new Color(AreaData.SeventhLine), new Color(AreaData.SeventhWash), new Color(AreaData.SeventhText))
        : (Ui.InkLine[a.Ink], Ui.InkWash[a.Ink], Ui.InkText[a.Ink]);
}

/// <summary>Draws 45-degree hatching in a rect, clipped by arithmetic (no clip rect needed).</summary>
public static class Hatching
{
    public static void Draw(CanvasItem c, Rect2 r, Color col, float step = 6f, float width = 1f)
    {
        for (float a = r.Position.X - r.Size.Y; a < r.End.X; a += step)
        {
            float s0 = Math.Max(0, r.Position.X - a), s1 = Math.Min(r.Size.Y, r.End.X - a);
            if (s0 < s1) c.DrawLine(new Vector2(a + s0, r.End.Y - s0), new Vector2(a + s1, r.End.Y - s1), col, width);
        }
    }
}

/// <summary>Header of an area panel: picture 320x120, a bar with the sign, the name and the state tag (domain wash, 3 du domain line on top), then the area's question.</summary>
public sealed partial class AreaHeader : VBoxContainer
{
    sealed partial class Bar : PanelContainer
    {
        public Color Line; public bool Hatch;
        public override void _Draw()
        {
            if (Hatch) Hatching.Draw(this, new Rect2(0, 3, Size.X, Size.Y - 3), new Color(Line, 0.35f), 6f);
            DrawRect(new Rect2(0, 0, Size.X, 3), Line);
        }
    }

    public AreaHeader(AreaDef def, bool locked)
    {
        AddThemeConstantOverride("separation", 4);
        var (line, wash, text) = UiArt.Inks(def);
        var plate = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        plate.AddThemeStyleboxOverride("panel", Ui.Box(wash, Ui.Border, 1, 0, Ui.R1));
        var col = new VBoxContainer(); col.AddThemeConstantOverride("separation", 0);
        plate.AddChild(col);
        col.AddChild(new TextureRect { Texture = UiArt.Picture(def.Slug, locked), CustomMinimumSize = new Vector2(318, 119), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale, TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps, MouseFilter = MouseFilterEnum.Ignore });
        var bar = new Bar { Line = line, Hatch = def.Ink == AreaData.Seventh, MouseFilter = MouseFilterEnum.Ignore };
        bar.AddThemeStyleboxOverride("panel", Ui.Box(wash, null, 0, 8, 0));
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
        row.AddChild(new TextureRect { Texture = UiArt.Icon(def.Slug), CustomMinimumSize = new Vector2(24, 24), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale, SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore });
        row.AddChild(Ui.Lbl("area." + def.Slug, 16, text, FontKind.Draft));
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        row.AddChild(Ui.Lbl(locked ? "area.state.locked" : def.State == AreaState.Part ? "area.state.part" : "area.state.live", 14, Ui.Ink2, FontKind.Mono));
        bar.AddChild(row);
        col.AddChild(bar);
        AddChild(plate);
        AddChild(Ui.Wrap("area." + def.Slug + ".q", 14, Ui.Ink2));
    }
}

/// <summary>Card of a locked area: its picture in grey, the question and the term. Nothing here is clickable and there are no numbers (no fake mechanics).</summary>
public sealed partial class AreaCard : PanelContainer
{
    sealed partial class Dashed : PanelContainer
    {
        public override void _Draw()
        {
            var s = Size; var c = Ui.Ink3;
            Hatching.Draw(this, new Rect2(0, 0, s.X, s.Y), new Color(Ui.Ink, 0.05f), 6f);
            DrawDashedLine(Vector2.Zero, new Vector2(s.X, 0), c, 1.5f, 4f); DrawDashedLine(new Vector2(s.X, 0), s, c, 1.5f, 4f);
            DrawDashedLine(s, new Vector2(0, s.Y), c, 1.5f, 4f); DrawDashedLine(new Vector2(0, s.Y), Vector2.Zero, c, 1.5f, 4f);
        }
    }

    public const int PanelWidth = 360;
    readonly VBoxContainer _root = new();
    AreaDef _def;

    public AreaCard()
    {
        CustomMinimumSize = new Vector2(PanelWidth, 0);
        AddThemeStyleboxOverride("panel", Ui.Box(Ui.Sheet, Ui.Ink, 2, 12, Ui.R3));
        PaperSurface.Apply(this, SurfaceKind.Sheet, "card");
        _root.AddThemeConstantOverride("separation", 8);
        AddChild(_root);
        Visible = false;
        Loc.Changed += () => { if (Visible && _def != null) ShowArea(_def); };   // the term line is built from a translated string
    }

    public void ShowArea(AreaDef def)
    {
        _def = def;
        foreach (var c in _root.GetChildren()) { _root.RemoveChild(c); c.QueueFree(); }
        _root.AddChild(new AreaHeader(def, true));
        var box = new Dashed(); box.AddThemeStyleboxOverride("panel", Ui.Box(Colors.Transparent, null, 0, 10, 0));
        var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 4);
        v.AddChild(Ui.Lbl("area.locked", 14, Ui.Ink, FontKind.Draft));
        v.AddChild(Ui.Wrap(Loc.T("area.locked.when", def.Milestone), 14));
        v.AddChild(Ui.Wrap("area.locked.note", 14, Ui.Ink3));
        box.AddChild(v);
        _root.AddChild(box);
        Visible = true;
    }
}

/// <summary>Slide-out journal of signals (IA §2): chronology of what appeared and what was lifted, newest first. Not an area.</summary>
public sealed partial class JournalPanel : PanelContainer
{
    public const int PanelWidth = 360, MaxEntries = 100;
    readonly VBoxContainer _list = new();
    readonly List<(string Date, string Key, bool On)> _entries = new();
    public event Action Closed;

    public JournalPanel()
    {
        CustomMinimumSize = new Vector2(PanelWidth, 0);
        AddThemeStyleboxOverride("panel", Ui.Box(Ui.Sheet, Ui.Ink, 2, 12, Ui.R3));
        PaperSurface.Apply(this, SurfaceKind.Sheet, "journal");
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 6);
        AddChild(root);
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 8);
        head.AddChild(new TextureRect { Texture = UiArt.Icon("journal"), CustomMinimumSize = new Vector2(24, 24), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale, SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore });
        var t = Ui.Lbl("journal.title", 18, null, FontKind.Draft); t.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(t);
        head.AddChild(Ui.Btn("×", () => Closed?.Invoke()));
        root.AddChild(head);
        root.AddChild(new HSeparator());
        var sc = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 260), SizeFlagsVertical = SizeFlags.ExpandFill };
        _list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _list.AddThemeConstantOverride("separation", 4);
        sc.AddChild(_list);
        root.AddChild(sc);
        Visible = false;
        Rebuild();
        Loc.Changed += Rebuild;
        TreeExiting += () => Loc.Changed -= Rebuild;
    }

    public void Add(string date, string key, bool on)
    {
        _entries.Insert(0, (date, key, on));
        if (_entries.Count > MaxEntries) _entries.RemoveAt(_entries.Count - 1);
        Rebuild();
    }

    void Rebuild()
    {
        foreach (var c in _list.GetChildren()) { _list.RemoveChild(c); c.QueueFree(); }
        if (_entries.Count == 0) { _list.AddChild(Ui.Lbl("journal.empty", 14, Ui.Ink3)); return; }
        foreach (var (date, key, on) in _entries)
        {
            var area = AreaData.OfSignal(key);
            string tag = area is { } a ? Loc.T("area." + AreaData.Of(a.Area).Slug) : Loc.T("journal.system");
            string sign = !on ? "□ " : area is { Severity: 2 } ? "■ " : area != null ? "▲ " : "■ ";
            _list.AddChild(Ui.Wrap(Loc.T("journal.line", date, sign + Loc.T(key), tag, Loc.T(on ? "journal.on" : "journal.off")), 14, on ? Ui.Ink : Ui.Ink2));
        }
    }
}
