namespace Singularity.Client.Spike;

/// <summary>A colour palette as hex strings (Godot-free, so the contrast validator and the tests read the very tokens the client draws with).
/// Tints are state tints 1..8; Line, Wash and Text are the six inks (sand, teal, violet, olive, plum, slate).</summary>
public sealed record PaletteSet(
    string Name, string Alarm, string Warn, string Command, string Paper, string Sheet, string Hover,
    string Sea, string SeaDeep, string LandNone, string[] Tints, string[] Line, string[] Wash, string[] Text);

public static class Palette
{
    // Neutral ink and line tokens shared by the UI and the globe overlay (hex, no leading #).
    public const string Ink = "1D1E20", Ink2 = "55575A", Ink3 = "64625C", Rule = "BDB59F", Border = "3E3C38", GlobeGrid = "A9C4CC", Limb = "A9B8CC";

    // Instrument tokens (UI_INSTRUMENTS §2, #404). Desk is the table under the panels; desk-line and desk-hatch are the quiet layer-1 strokes (not drawn yet, validated now).
    // Layer 2 (the controls) draws in Ink, Ink2 and the six inks above.
    public const string Desk = "D9D3C0", DeskLine = "B8B094", DeskHatch = "C6BEA4";

    public static readonly string[] InkNames = { "sand", "teal", "violet", "olive", "plum", "slate" };

    /// <summary>Palette C «Акварель» (UI_DESIGN_SYSTEM §17.3-17.4) with warn #D9860F (decision #326).</summary>
    public static readonly PaletteSet C = new(
        "C", "C42A1C", "D9860F", "2347A8", "F3EEDF", "FBF8EF", "EBE5D2", "B8DCEA", "9CCBE0", "EBE3C6",
        new[] { "F7B98F", "9EC6F5", "BBDE86", "EFB0EC", "F8D56F", "8BE3C3", "EEA894", "A9AAF7" },
        new[] { "8F7A2E", "168A92", "7A4AA8", "4F8A1E", "A03E82", "4A7088" },
        new[] { "EAD9A3", "BFE6E5", "D8CCEE", "CFE6AA", "F0C9E0", "C3D8EA" },
        new[] { "635420", "1F5F63", "53407A", "4A6020", "75335F", "3F5666" });   // sand text darkened from 6B5B22 for T1 on the sand wash x0.94

    /// <summary>The v0.5a client palette (warm tints, grey-blue sea): the reference that the validator must pass.</summary>
    public static readonly PaletteSet V02 = new(
        "v0.2", "C42A1C", "E89A1A", "2347A8", "F2EFE6", "FAF8F2", "E8E4D8", "D3E1E4", "D3E1E4", "E6E2D6",
        new[] { "EBCDB7", "D5E0BC", "E3D0E3", "EFE0AE", "E0CAC2", "D4C79F", "DDB9A0", "C9D4A8" },
        new[] { "7F7452", "3F7A7C", "6F5C8C", "6A7A3A", "84587A", "5D6E7A" },
        new[] { "EFEBDC", "E3EEEE", "EBE7F1", "EAEFDD", "F0E5EC", "E4EAEE" },
        new[] { "6B5B22", "1F5F63", "53407A", "4A6020", "75335F", "3F5666" });
}
