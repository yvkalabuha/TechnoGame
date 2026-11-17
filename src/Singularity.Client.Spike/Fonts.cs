using Godot;

namespace Singularity.Client.Spike;

/// <summary>Bundled OFL fonts (see LICENSES.md). Roles: Text = interface (Golos Text), Mono = numbers and codes (PT Mono),
/// Draft = drafting headings (Jura), Map = map labels (Literata). Symbols missing in them fall back to a system face.</summary>
public static class Fonts
{
    public static FontFile Text, Mono, Draft, Map;

    public static void Load()
    {
        Text = Make("GolosText"); Mono = Make("PTMono"); Draft = Make("Jura"); Map = Make("Literata");
        var theme = ThemeDB.GetDefaultTheme();
        theme.DefaultFont = Text;
        theme.DefaultFontSize = 14;
    }

    static FontFile Make(string name)
    {
        var f = GD.Load<FontFile>($"res://assets/fonts/{name}.ttf");
        f.Fallbacks = new Godot.Collections.Array<Font> { new SystemFont { FontNames = new[] { "DejaVu Sans", "Noto Sans Symbols2", "sans-serif" } } };
        return f;
    }
}
