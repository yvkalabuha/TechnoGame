using Godot;

namespace Singularity.Client.Spike;

/// <summary>Puts the paper texture on a panel or a button (CLIENT_V05C_SPEC §3). One baked tile and one shader; the strength is a global shader uniform, so the
/// preset of the settings acts at once, without a restart. Never call this for alert plates, the halo, the map or the globe.</summary>
public static class PaperSurface
{
    static Shader _shader;
    static ImageTexture _tile;
    static bool _ready;
    static readonly System.Collections.Generic.List<(Control Node, SurfaceKind Kind)> Applied = new();   // for the T6 layout check; insertion order
    static float _strength = 1f;

    /// <summary>Registers the global uniform and bakes the tile (once). Safe to call again.</summary>
    public static void Init()
    {
        if (_ready) return;
        _ready = true;
        RenderingServer.GlobalShaderParameterAdd("tex_strength", RenderingServer.GlobalShaderParameterType.Float, _strength);
        _shader = GD.Load<Shader>("res://shaders/paper_surface.gdshader");
        _tile = ImageTexture.CreateFromImage(Image.CreateFromData(PaperTexture.TileSize, PaperTexture.TileSize, false, Image.Format.Rgb8, PaperTexture.BakeTile()));
    }

    /// <summary>Preset 0 off, 1 weak, 2 normal.</summary>
    public static void SetPreset(int preset)
    {
        Init();
        _strength = (float)PaperTexture.Strength(preset);
        RenderingServer.GlobalShaderParameterSet("tex_strength", _strength);
    }

    public static float Strength => _strength;

    public static System.Collections.Generic.IReadOnlyList<(Control Node, SurfaceKind Kind)> Surfaces() { Applied.RemoveAll(a => !GodotObject.IsInstanceValid(a.Node)); return Applied; }

    /// <summary>Gives <paramref name="node"/> the texture. <paramref name="seed"/> (a stable name) sets the tile offset and the pressed-centre spot. Buttons also react to the press.</summary>
    public static void Apply(Control node, SurfaceKind kind, string seed)
    {
        Init();
        var p = PaperTexture.Params(kind);
        var (a, b) = PaperTexture.NameHash(seed);
        var m = new ShaderMaterial { Shader = _shader };
        m.SetShaderParameter("tile", _tile);
        m.SetShaderParameter("amp", new Vector3((float)p.Grain, (float)p.Fibre, (float)p.Blotch));
        m.SetShaderParameter("wear", (float)p.Wear);
        m.SetShaderParameter("wear_w", (float)p.WearWidth);
        m.SetShaderParameter("tile_off", new Vector2((float)a, (float)b));
        m.SetShaderParameter("rub_c", new Vector2((float)(0.3 + 0.4 * a), (float)(0.3 + 0.4 * b)));
        m.SetShaderParameter("rect_size", node.Size);
        node.Material = m;
        Applied.Add((node, kind));
        node.Resized += () => m.SetShaderParameter("rect_size", node.Size);
        if (node is BaseButton btn && kind == SurfaceKind.Button)
        {
            var pr = PaperTexture.Params(kind, true);
            void Set(SurfaceParams q, float press)
            {
                m.SetShaderParameter("press", press);
                m.SetShaderParameter("amp", new Vector3((float)q.Grain, (float)q.Fibre, (float)q.Blotch));
                m.SetShaderParameter("wear_w", (float)q.WearWidth);
            }
            btn.ButtonDown += () => Set(pr, 1f);
            btn.ButtonUp += () => Set(p, 0f);
        }
    }
}

/// <summary>A textured control that draws its ink (icons, letters) itself on a child layer: gives the global rects of that ink for the T6 check.</summary>
public interface IGlyphBounds
{
    System.Collections.Generic.IEnumerable<Rect2> GlyphRects();
}
