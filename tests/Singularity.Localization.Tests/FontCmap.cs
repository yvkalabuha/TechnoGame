using System.Buffers.Binary;

namespace Singularity.Localization.Tests;

/// <summary>Minimal TrueType 'cmap' reader (formats 4 and 12): the set of code points a font has glyphs for (ADR-0018 §3.5).</summary>
internal sealed class FontCmap
{
    readonly List<(int First, int Last)> _ranges = [];

    public string Name { get; }

    public FontCmap(string path)
    {
        Name = Path.GetFileName(path);
        byte[] d = File.ReadAllBytes(path);
        int tables = U16(d, 4);
        int cmap = -1;
        for (int i = 0; i < tables; i++)
        {
            int rec = 12 + 16 * i;
            if (System.Text.Encoding.ASCII.GetString(d, rec, 4) == "cmap")
                cmap = (int)U32(d, rec + 8);
        }
        if (cmap < 0)
            throw new InvalidDataException(path + ": no cmap table");
        int n = U16(d, cmap + 2);
        for (int i = 0; i < n; i++)
        {
            int platform = U16(d, cmap + 4 + 8 * i), encoding = U16(d, cmap + 6 + 8 * i);
            int sub = cmap + (int)U32(d, cmap + 8 + 8 * i);
            int format = U16(d, sub);
            bool unicode = platform == 0 || (platform == 3 && encoding is 1 or 10);
            if (!unicode)
                continue;
            if (format == 4)
                ReadFormat4(d, sub);
            else if (format == 12)
                ReadFormat12(d, sub);
        }
        if (_ranges.Count == 0)
            throw new InvalidDataException(path + ": no Unicode cmap of format 4 or 12");
    }

    void ReadFormat4(byte[] d, int sub)
    {
        int segX2 = U16(d, sub + 6);
        int ends = sub + 14, starts = ends + segX2 + 2;
        for (int s = 0; s < segX2 / 2; s++)
        {
            int end = U16(d, ends + 2 * s), start = U16(d, starts + 2 * s);
            if (start != 0xFFFF)
                _ranges.Add((start, end));
        }
    }

    void ReadFormat12(byte[] d, int sub)
    {
        uint groups = U32(d, sub + 12);
        for (int g = 0; g < groups; g++)
            _ranges.Add(((int)U32(d, sub + 16 + 12 * g), (int)U32(d, sub + 20 + 12 * g)));
    }

    // Segments with a zero glyph index may be mapped to .notdef; for ranges built from cmap this is accepted as covered.
    public bool Has(int codePoint)
    {
        foreach ((int first, int last) in _ranges)
        {
            if (codePoint >= first && codePoint <= last)
                return true;
        }
        return false;
    }

    static int U16(byte[] d, int o) => BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(o));
    static uint U32(byte[] d, int o) => BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(o));
}
