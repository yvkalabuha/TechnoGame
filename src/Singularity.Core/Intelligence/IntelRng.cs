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
    public static int NetworkKey(string owner, string target) => NetworkKey(owner + ">" + target);

    /// <summary>
    /// The RNG keys of all networks, flat by <c>owner·rows + target</c>. Each is <see cref="NetworkKey"/>; a collision of two pairs (possible for valid ids,
    /// 32 bits) is resolved by taking the pairs in the ordinal order of their ids and moving a later pair to the next free key, so the result depends on the
    /// ids only, never on the rows they are listed in.
    /// </summary>
    public static int[] NetworkKeys(System.Collections.Immutable.ImmutableArray<string> ids)
    {
        int n = ids.Length;
        var pairs = new List<(string Name, int Slot)>(n * n);
        for (int o = 0; o < n; o++)
        {
            for (int t = 0; t < n; t++)
                pairs.Add((ids[o] + ">" + ids[t], o * n + t));
        }
        pairs.Sort((x, y) => string.CompareOrdinal(x.Name, y.Name));
        var used = new SortedSet<int>();
        var keys = new int[n * n];
        foreach ((string name, int slot) in pairs)
        {
            int key = NetworkKey(name);
            while (!used.Add(key))
                key = (int)((uint)(key + 1) & 0x7fff_ffff);
            keys[slot] = key;
        }
        return keys;
    }

    private static int NetworkKey(string pair)
    {
        uint h = 2166136261;
        foreach (byte b in Encoding.UTF8.GetBytes(pair))
            h = (h ^ b) * 16777619;
        return (int)(h & 0x7fff_ffff);
    }

    /// <summary>The window draw: after the trace passes the threshold the exposure comes in 1 to 12 months, picked by this draw.</summary>
    public static ulong Window(CounterRng events, long month, int networkKey) => events.NextU64(month, IntelSystem.SystemExpose, networkKey, 1);

    public static ulong Expose(CounterRng events, long month, int networkKey) => events.NextU64(month, IntelSystem.SystemExpose, networkKey, 0);

    /// <summary>n = 0: the exposed network is turned instead of lost; n = 1: a turned network is found out.</summary>
    public static ulong Turn(CounterRng events, long month, int networkKey, int n) => events.NextU64(month, IntelSystem.SystemTurn, networkKey, n);
}
