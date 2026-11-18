#nullable enable
using System;

namespace Singularity.Client.Spike;

public enum AreaId { Map, Science, Production, Finance, Forces, Intel, Politics, Projects }

/// <summary>How much of an area works: Live = real data and orders, Part = only some of it, Locked = no mechanic yet (no numbers, no controls).</summary>
public enum AreaState { Live, Part, Locked }

/// <summary>One navigation area (CLIENT_V05C_SPEC §4, decisions #352, #360). Ink: index into the six inks, -1 neutral, 6 the seventh colour (hatched, #360).</summary>
public sealed record AreaDef(AreaId Id, string Slug, char Letter, int Ink, AreaState State, string Milestone, int Group);

public static class AreaData
{
    public const int Neutral = -1, Seventh = 6;

    // Sand 0, teal 1, violet 2, olive 3, plum 4, slate 5. Only Science, Production and Finance have real data in the client today:
    // Forces stays locked (no force data in the Session API yet; the budget shares are the budget, not forces): no fake mechanics.
    public static readonly AreaDef[] All =
    {
        new(AreaId.Map, "map", '\0', Neutral, AreaState.Live, "", 0),
        new(AreaId.Science, "science", 'T', 2, AreaState.Live, "", 1),
        new(AreaId.Production, "production", 'E', 1, AreaState.Part, "M1", 1),
        new(AreaId.Finance, "finance", 'F', 0, AreaState.Part, "M1", 1),
        new(AreaId.Forces, "forces", 'M', 5, AreaState.Locked, "M2–M3", 2),
        new(AreaId.Intel, "intel", 'I', 4, AreaState.Locked, "M2–M3", 2),
        new(AreaId.Politics, "politics", 'G', 3, AreaState.Locked, "M1–M3", 3),
        new(AreaId.Projects, "projects", 'J', Seventh, AreaState.Locked, "M1", 3),
    };

    public static AreaDef Of(AreaId id) => All[(int)id];

    /// <summary>The area opened by a hotkey letter (upper case), or null.</summary>
    public static AreaDef? ByLetter(char c)
    {
        foreach (var a in All) if (a.Letter == c && c != '\0') return a;
        return null;
    }

    /// <summary>Area a signal belongs to and its severity (2 alarm, 1 warn); null for signals that belong to no area (system fault: journal only).</summary>
    public static (AreaId Area, int Severity)? OfSignal(string key) => key switch
    {
        "sig.alloy" => (AreaId.Production, 1),
        "sig.licence" => (AreaId.Finance, 1),
        "sig.refusal" => (AreaId.Finance, 2),
        _ => null,
    };

    /// <summary>The seventh ink: a moss green, always drawn with 45-degree hatching as a second channel (#360). Line, wash, text step.</summary>
    public const string SeventhLine = "3A8A5C", SeventhWash = "C6E6D2", SeventhText = "2A5E3F";
}
