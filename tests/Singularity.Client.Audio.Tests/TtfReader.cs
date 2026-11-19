using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Singularity.Client.Audio.Tests;

/// <summary>Minimal TrueType reader for metric tests without Godot: units per em and the glyph box of a BMP character (cmap format 4, glyf).</summary>
public sealed class TtfReader
{
    readonly byte[] _d;
    readonly Dictionary<string, (int off, int len)> _t = new();
    readonly bool _longLoca;
    public int UnitsPerEm { get; }

    public TtfReader(string path)
    {
        _d = File.ReadAllBytes(path);
        int n = U16(4);
        for (int i = 0; i < n; i++) { int o = 12 + 16 * i; _t[Encoding.ASCII.GetString(_d, o, 4)] = ((int)U32(o + 8), (int)U32(o + 12)); }
        UnitsPerEm = U16(_t["head"].off + 18);
        _longLoca = S16(_t["head"].off + 50) != 0;
    }

    int U16(int o) => (_d[o] << 8) | _d[o + 1];
    int S16(int o) => (short)U16(o);
    uint U32(int o) => ((uint)U16(o) << 16) | (uint)U16(o + 2);

    int GlyphOf(char c)
    {
        int cm = _t["cmap"].off, n = U16(cm + 2);
        for (int i = 0; i < n; i++)
        {
            int st = cm + (int)U32(cm + 4 + 8 * i + 4);
            if (U16(st) != 4) continue;
            int segX2 = U16(st + 6), endO = st + 14, startO = endO + segX2 + 2, deltaO = startO + segX2, rangeO = deltaO + segX2;
            for (int s = 0; s < segX2; s += 2)
            {
                if (c > U16(endO + s) || c < U16(startO + s)) continue;
                int ro = U16(rangeO + s);
                if (ro == 0) return (c + S16(deltaO + s)) & 0xFFFF;
                int g = U16(rangeO + s + ro + 2 * (c - U16(startO + s)));
                return g == 0 ? 0 : (g + S16(deltaO + s)) & 0xFFFF;
            }
        }
        throw new InvalidOperationException("no cmap format 4 entry for " + c);
    }

    /// <summary>Height of the glyph box above the baseline, in font units.</summary>
    public int YMax(char c) => S16(GlyphStart(GlyphOf(c)) + 8);

    int GlyphStart(int g) => _t["glyf"].off + (_longLoca ? (int)U32(_t["loca"].off + 4 * g) : 2 * U16(_t["loca"].off + 2 * g));
}
