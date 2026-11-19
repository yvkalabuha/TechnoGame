using System;

namespace Singularity.Client.Spike;

/// <summary>Godot-free rules of the instrument layers (UI_INSTRUMENTS §3, decisions #404 and #409).</summary>
public static class InstrumentLogic
{
    /// <summary>Instrument-controls level: 0 off («before» look), 1 weak (ink only), 2 normal (the three inks). Set from the settings.</summary>
    public static int OrganLevel = 2;

    /// <summary>Era step of an instrument by game year: 1 = 1945–69 (and earlier), 2 = 1970–99, 3 = 2000–2100.</summary>
    public static int Era(int year) => year < 1970 ? 1 : year < 2000 ? 2 : 3;

    /// <summary>Needle angle in degrees from vertical for a speed step 0..3 (E1 dial: the step ticks, 30 degrees apart).</summary>
    public static readonly double[] NeedleDeg = { -60, -30, 0, 30 };
}
