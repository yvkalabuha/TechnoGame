namespace Singularity.Client.Spike;

/// <summary>Godot-free look of the status hatching on the globe (A5, G7): the shader reads these numbers as uniforms.</summary>
public static class HatchLogic
{
    /// <summary>Line period in screen px, line width and strength. Occupation has a wider step and a lighter line than colony (G7: German zone must not read as Hungary).</summary>
    public static (float Period, float Width, float Alpha) Params(HatchKind k) => k switch
    {
        HatchKind.Colony => (8f, 1f, 0.9f),
        HatchKind.Occupied => (11f, 1f, 0.65f),
        _ => (8f, 0f, 0f),
    };

    /// <summary>Line of control: width in px and strength of the ink line over the fill (the shader and the legend sample read the same numbers).</summary>
    public const float ControlWidth = 1.2f, ControlAlpha = 0.8f;

    /// <summary>Share of the fill brightness kept by the hatch line (linear light): 0.42 is the tint.N.line stand-in; occupation is lighter.</summary>
    public static float LineScale(HatchKind k) => k == HatchKind.Occupied ? 0.62f : 0.42f;

    /// <summary>Tones of the legend swatches, taken from the palette: a fixed power (USA) as controller, the stone tone as the owner under occupation.</summary>
    public static int ColonyFillTone => CountryPalette.Fixed[0].Tone;
    public static int OccupiedFillTone => CountryPalette.Stone;
    public static int ControllerTone => CountryPalette.Fixed[2].Tone;

    /// <summary>The line colour of a tone: the fill scaled in linear light, as an sRGB byte triple.</summary>
    public static (byte R, byte G, byte B) Line(int tone, float scale)
    {
        int v = int.Parse(CountryPalette.Hex[tone], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
        static byte K(int c, float k)
        {
            double s = c / 255.0, lin = (s <= 0.04045 ? s / 12.92 : System.Math.Pow((s + 0.055) / 1.055, 2.4)) * k;
            double o = lin <= 0.0031308 ? lin * 12.92 : 1.055 * System.Math.Pow(lin, 1 / 2.4) - 0.055;
            return (byte)System.Math.Round(o * 255.0);
        }
        return (K((v >> 16) & 255, scale), K((v >> 8) & 255, scale), K(v & 255, scale));
    }

    /// <summary>Status of a region: a different controller is occupation (wins over colony), a dependent status is colony, else none.</summary>
    public static HatchKind Choose(string owner, string controller, bool dependent) =>
        owner != "" && controller != "" && controller != owner ? HatchKind.Occupied : dependent ? HatchKind.Colony : HatchKind.None;
}
