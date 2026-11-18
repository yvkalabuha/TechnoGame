namespace Singularity.Client.Spike;

/// <summary>Content of the tree legend (spec §8), Godot-free so that a test can check it against the palette tokens. Disciplines are the five of the current tree, in the
/// order of the Core enum (Aero, Prop, Metal, Chem, Mech); the type codes are the Core NodeType order with the shape the tree draws for each.</summary>
public static class LegendData
{
    public sealed record Discipline(string NameKey, string LineHex, string WashHex);
    public sealed record GlyphType(string Code, string Shape, string NameKey);

    // Ink indices in Palette.C: teal 1, plum 4, slate 5, olive 3, sand 0.
    static readonly int[] Ink = { 1, 4, 5, 3, 0 };

    public static Discipline[] Disciplines(PaletteSet p)
    {
        var r = new Discipline[Ink.Length];
        for (int i = 0; i < r.Length; i++) r[i] = new Discipline("disc." + i, p.Line[Ink[i]], p.Wash[Ink[i]]);
        return r;
    }

    public static readonly GlyphType[] Types =
    {
        new("FND", "triangle", "ntype.0"), new("PRN", "circle", "ntype.1"), new("ENG", "hexagon", "ntype.2"),
        new("CMP", "pentagon", "ntype.3"), new("PRC", "square", "ntype.4"), new("APP", "diamond", "ntype.5"),
    };
}
