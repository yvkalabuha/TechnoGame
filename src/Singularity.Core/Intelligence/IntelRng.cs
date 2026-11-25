using System.Text;
using Singularity.Kernel;

namespace Singularity.Core.Intelligence;

/// <summary>
/// RNG keys of intelligence (ADR-0021 streams <c>intel.expose</c>, <c>intel.turn</c>; INTELLIGENCE.md §8). A network is keyed by the stable ids of its two
/// actors, never by their position in the scenario: reordering the actors keeps every network on its own draws.
/// </summary>
public static class IntelRng
{
    /// <summary>Entity key of the network of <paramref name="owner"/> against <paramref name="target"/>: 32-bit FNV-1a of <c>"owner>target"</c> (UTF-8), sign bit cleared.</summary>
    public static int NetworkKey(string owner, string target)
    {
        uint h = 2166136261;
        foreach (byte b in Encoding.UTF8.GetBytes(owner + ">" + target))
            h = (h ^ b) * 16777619;
        return (int)(h & 0x7fff_ffff);
    }

    public static ulong Expose(CounterRng events, long month, int networkKey) => events.NextU64(month, IntelSystem.SystemExpose, networkKey, 0);

    /// <summary>n = 0: the exposed network is turned instead of lost; n = 1: a turned network is found out.</summary>
    public static ulong Turn(CounterRng events, long month, int networkKey, int n) => events.NextU64(month, IntelSystem.SystemTurn, networkKey, n);
}
