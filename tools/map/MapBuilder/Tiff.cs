using System.IO.Compression;
using System.Text;

namespace Singularity.MapBuilder;

/// <summary>
/// Minimal TIFF / BigTIFF / GeoTIFF / COG reader for the terrain bake (TASK-0168): one IFD, tiled or striped, 8/16/32-bit integers,
/// compression none / deflate / LZW, predictor 1 or 2. No GDAL: the builder reads rasters as plain data.
/// </summary>
internal sealed class TiffImage
{
    public int Width, Height, BlockW, BlockH, Bits, Format = 1, Compression = 1, Predictor = 1;
    public bool Tiled;
    public long[] Offsets = Array.Empty<long>(), Counts = Array.Empty<long>();
    /// <summary>GeoTIFF: pixel size and the geographic position of the upper-left corner, in degrees.</summary>
    public double PixelX, PixelY, OriginLon, OriginLat;
    public int? NoData;
    private readonly Func<long, int, byte[]> read;

    private TiffImage(Func<long, int, byte[]> read) { this.read = read; }

    public int BlocksAcross => (Width + BlockW - 1) / BlockW;

    /// <summary>Opens IFD number <paramref name="ifd"/> (0 = full resolution, higher = overviews). <paramref name="read"/>(offset, count) returns file bytes.</summary>
    public static TiffImage Open(Func<long, int, byte[]> read, int ifd = 0)
    {
        var h = read(0, 16);
        bool le = h[0] == (byte)'I';
        if (!le && h[0] != (byte)'M') throw new InvalidDataException("not a TIFF file");
        var img = new TiffImage(read) { le = le };
        int magic = img.U16(h, 2);
        if (magic != 42 && magic != 43) throw new InvalidDataException("not a TIFF file");
        bool big = magic == 43;
        long off = big ? (long)img.U64(h, 8) : img.U32(h, 4);
        for (int i = 0; ; i++)
        {
            int cw = big ? 8 : 2, ew = big ? 20 : 12, nw = big ? 8 : 4;
            long n = big ? (long)img.U64(read(off, 8), 0) : img.U16(read(off, 2), 0);
            var ent = read(off + cw, (int)(n * ew) + nw);
            if (i == ifd) { img.Parse(ent, (int)n, big); return img; }
            off = big ? (long)img.U64(ent, (int)n * ew) : img.U32(ent, (int)n * ew);
            if (off == 0) throw new InvalidDataException($"TIFF has no IFD {ifd}");
        }
    }

    private bool le;
    private int U16(byte[] b, int o) => le ? b[o] | b[o + 1] << 8 : b[o] << 8 | b[o + 1];
    private long U32(byte[] b, int o) => le ? (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24) : (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);
    private ulong U64(byte[] b, int o) => le ? (ulong)U32(b, o) | (ulong)U32(b, o + 4) << 32 : (ulong)U32(b, o) << 32 | (ulong)U32(b, o + 4);

    private static readonly int[] TypeSize = { 0, 1, 1, 2, 4, 8, 1, 1, 2, 4, 8, 4, 8, 4, 0, 0, 8, 8, 8 };

    private void Parse(byte[] ent, int n, bool big)
    {
        double GetD(byte[] b, int o) { var t = b.AsSpan(o, 8).ToArray(); if (!le) Array.Reverse(t); return BitConverter.ToDouble(t, 0); }
        long[] Longs(int type, int count, byte[] raw)
        {
            var r = new long[count];
            for (int k = 0; k < count; k++) r[k] = type == 3 ? U16(raw, k * 2) : type == 16 ? (long)U64(raw, k * 8) : U32(raw, k * 4);
            return r;
        }
        int ew = big ? 20 : 12, inline = big ? 8 : 4;
        int rowsPerStrip = int.MaxValue;
        double[] scale = Array.Empty<double>(), tie = Array.Empty<double>();
        for (int e = 0; e < n; e++)
        {
            int o = e * ew;
            int tag = U16(ent, o), type = U16(ent, o + 2);
            long count = big ? (long)U64(ent, o + 4) : U32(ent, o + 4);
            int vo = o + (big ? 12 : 8);
            long size = (type < TypeSize.Length ? TypeSize[type] : 1) * count;
            byte[] raw = size <= inline ? ent.AsSpan(vo, inline).ToArray() : read(big ? (long)U64(ent, vo) : U32(ent, vo), (int)size);
            int cnt = (int)count;
            switch (tag)
            {
                case 256: Width = (int)Longs(type, 1, raw)[0]; break;
                case 257: Height = (int)Longs(type, 1, raw)[0]; break;
                case 258: Bits = (int)Longs(type, 1, raw)[0]; break;
                case 259: Compression = (int)Longs(type, 1, raw)[0]; break;
                case 277: if (Longs(type, 1, raw)[0] != 1) throw new InvalidDataException("only single-band TIFF is supported"); break;
                case 278: rowsPerStrip = (int)Longs(type, 1, raw)[0]; break;
                case 273: case 324: Offsets = Longs(type, cnt, raw); break;
                case 279: case 325: Counts = Longs(type, cnt, raw); break;
                case 322: BlockW = (int)Longs(type, 1, raw)[0]; Tiled = true; break;
                case 323: BlockH = (int)Longs(type, 1, raw)[0]; break;
                case 317: Predictor = (int)Longs(type, 1, raw)[0]; break;
                case 339: Format = (int)Longs(type, 1, raw)[0]; break;
                case 33550: scale = Enumerable.Range(0, cnt).Select(k => GetD(raw, k * 8)).ToArray(); break;
                case 33922: tie = Enumerable.Range(0, cnt).Select(k => GetD(raw, k * 8)).ToArray(); break;
                case 42113: if (int.TryParse(Encoding.ASCII.GetString(raw).TrimEnd('\0', ' '), out int nd)) NoData = nd; break;
            }
        }
        if (!le && Bits > 8) throw new InvalidDataException("big-endian samples are not supported");
        if (!Tiled) { BlockW = Width; BlockH = Math.Min(rowsPerStrip, Height); }
        if (scale.Length >= 2) { PixelX = scale[0]; PixelY = scale[1]; }
        if (tie.Length >= 6) { OriginLon = tie[3] - tie[0] * PixelX; OriginLat = tie[4] + tie[1] * PixelY; }
        if (Bits is not (8 or 16 or 32) || Predictor is not (1 or 2)) throw new InvalidDataException($"unsupported TIFF layout: {Bits} bits, predictor {Predictor}");
        if (Predictor == 2 && Bits == 32) throw new InvalidDataException("predictor 2 with 32-bit samples is not supported");
    }

    /// <summary>Decoded samples of block <paramref name="index"/> (row-major, BlockW x block height), as ints.</summary>
    public int[] ReadBlock(int index)
    {
        var comp = read(Offsets[index], (int)Counts[index]);
        int rows = Tiled ? BlockH : Math.Min(BlockH, Height - index * BlockH);
        int bytes = BlockW * rows * (Bits / 8);
        byte[] raw = Compression switch
        {
            1 => comp,
            8 or 32946 => Inflate(comp, bytes),
            5 => Lzw(comp, bytes),
            _ => throw new InvalidDataException("unsupported TIFF compression " + Compression),
        };
        if (raw.Length < bytes) Array.Resize(ref raw, bytes);
        var res = new int[BlockW * rows];
        int bpp = Bits / 8;
        for (int i = 0; i < res.Length; i++)
        {
            int p = i * bpp;
            res[i] = Bits == 8 ? raw[p]
                : Bits == 16 ? (Format == 2 ? (short)(raw[p] | raw[p + 1] << 8) : raw[p] | raw[p + 1] << 8)
                : raw[p] | raw[p + 1] << 8 | raw[p + 2] << 16 | raw[p + 3] << 24;
        }
        if (Predictor == 2)
            for (int r = 0; r < rows; r++)
                for (int x = 1; x < BlockW; x++)
                {
                    int v = res[r * BlockW + x] + res[r * BlockW + x - 1];
                    res[r * BlockW + x] = Bits == 8 ? v & 0xFF : Format == 2 ? (short)v : v & 0xFFFF;
                }
        return res;
    }

    private static byte[] Inflate(byte[] data, int expected)
    {
        using var z = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
        var outp = new byte[expected];
        int got = 0, k;
        while (got < expected && (k = z.Read(outp, got, expected - got)) > 0) got += k;
        return outp;
    }

    /// <summary>TIFF LZW (MSB-first codes, early change).</summary>
    private static byte[] Lzw(byte[] src, int expected)
    {
        var o = new byte[expected];
        var prefix = new int[4096]; var suffix = new byte[4096]; var length = new int[4096];
        for (int i = 0; i < 256; i++) { suffix[i] = (byte)i; length[i] = 1; prefix[i] = -1; }
        int next = 258, bits = 9, w = 0, prev = -1, buf = 0, nb = 0, pos = 0;
        var tmp = new byte[4096];
        while (w < expected)
        {
            while (nb < bits) { if (pos >= src.Length) return o; buf = buf << 8 | src[pos++]; nb += 8; }
            int code = (buf >> (nb - bits)) & ((1 << bits) - 1);
            nb -= bits;
            if (code == 257) break;
            if (code == 256) { next = 258; bits = 9; prev = -1; continue; }
            int cur = code;
            if (code >= next)
            {
                if (prev < 0) throw new InvalidDataException("corrupt LZW stream");
                cur = prev;
            }
            int len = length[cur];
            int c = cur;
            for (int k = len - 1; k >= 0; k--) { tmp[k] = suffix[c]; c = prefix[c]; }
            int total = len;
            if (code >= next) tmp[total++] = tmp[0];
            for (int k = 0; k < total && w < expected; k++) o[w++] = tmp[k];
            if (prev >= 0 && next < 4096) { prefix[next] = prev; suffix[next] = tmp[0]; length[next] = length[prev] + 1; next++; }
            prev = code;
            bits = next >= 2047 ? 12 : next >= 1023 ? 11 : next >= 511 ? 10 : 9;
        }
        return o;
    }
}
