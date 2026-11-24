using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace Singularity.Client.Spike;

/// <summary>One owner (polity) of the 1945 map; id = owner number of the owner raster (1..255).</summary>
public sealed class Country
{
    public int Id;
    public string Name;      // polity code, for example "SUN"; the display name is Names.Country(code)
    public double BoxArea;   // pixels of the owner on the raster: the label order
    public Vector2 LabelPoint;
    public int Tint;         // tone index 0..11 of CountryPalette
}

/// <summary>
/// The globe data: region ID raster (R16, from geo/arcs.bin), owner raster for picking (R8) and the region look-up table that the shader reads
/// (row 0: fill sRGB + hatch kind, row 1: hatch line sRGB + owner number). Built at startup from the map tables and the 1945 owners.
/// </summary>
public sealed class GlobeData
{
    public const int W = WorldMap.W, H = WorldMap.H;
    public const int LutWidth = 512;
    public readonly List<Country> Countries = new();
    public byte[] Ids = Array.Empty<byte>();            // owner number per pixel, 0 = sea or no owner
    public ushort[] RegionIds = Array.Empty<ushort>();  // region number per pixel, 0 = sea
    public byte[] RegionLut = Array.Empty<byte>();      // LutWidth x 2 RGBA8
    public readonly List<Country> ColonyLabels = new();   // Id 0, Name "C_KEY": labels of the large colonial areas (locale polity.C_KEY)
    public WorldMap Map;
    public CountryPalette.Result Style;

    public void Load(string repoRoot)
    {
        Map = WorldMap.Load(repoRoot);
        RegionIds = Map.RasteriseRegions();
        if (Map.Owners.Length > 255 || Map.RegionIds.Length >= LutWidth) throw new InvalidOperationException("owner raster is 8-bit and the region table is 512 wide");
        Ids = Map.OwnerRaster(RegionIds);
        Style = CountryPalette.Build(Map);
        var area = new long[Map.Owners.Length + 1];
        foreach (var o in Ids) area[o]++;
        var anchors = Map.LabelAnchors(RegionIds);
        for (int o = 1; o <= Map.Owners.Length; o++)
            Countries.Add(new Country { Id = o, Name = Map.Owners[o - 1], BoxArea = area[o], LabelPoint = new Vector2(anchors[o].Lon, anchors[o].Lat), Tint = Style.OwnerTone[o] });

        var canchors = Map.ColonyAnchors(RegionIds);
        var cg = Map.ColonyGroupOfRegion();
        var carea = new long[WorldMap.ColonyGroups.Length + 1];
        foreach (var rg in RegionIds) if (rg != 0) carea[cg[rg - 1]]++;
        for (int k = 1; k <= WorldMap.ColonyGroups.Length; k++)
            if (carea[k] > 0) ColonyLabels.Add(new Country { Id = 0, Name = "C_" + WorldMap.ColonyGroups[k - 1].Key, BoxArea = carea[k], LabelPoint = new Vector2(canchors[k].Lon, canchors[k].Lat) });
        ColonyLabels.Sort((a, b) => b.BoxArea.CompareTo(a.BoxArea));

        RegionLut = new byte[LutWidth * 2 * 4];
        for (int i = 0; i < Style.Regions.Length; i++)
        {
            var s = Style.Regions[i]; int id = i + 1;
            Put(0, id, Fill(s.Tone), (byte)((int)s.Hatch * 85));
            Put(1, id, Line(s.HatchTone), (byte)s.Owner);
        }
    }

    void Put(int row, int x, (byte R, byte G, byte B) c, byte a)
    {
        int o = (row * LutWidth + x) * 4;
        RegionLut[o] = c.R; RegionLut[o + 1] = c.G; RegionLut[o + 2] = c.B; RegionLut[o + 3] = a;
    }

    static (byte, byte, byte) Fill(int tone)
    {
        int v = int.Parse(CountryPalette.Hex[tone], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return ((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    /// <summary>The stand-in of tint.N.line: the fill at 42 % in linear light, as an sRGB byte triple.</summary>
    public static (byte, byte, byte) Line(int tone)
    {
        var (r, g, b) = Fill(tone);
        static byte K(byte c) { var l = new Color(c / 255f, 0, 0).SrgbToLinear().R * 0.42f; return (byte)Math.Round(new Color(l, 0, 0).LinearToSrgb().R * 255f); }
        return (K(r), K(g), K(b));
    }

    /// <summary>The ID raster as RG8 bytes (low byte, high byte of the region number).</summary>
    public byte[] RegionIdBytes()
    {
        var b = new byte[RegionIds.Length * 2];
        for (int i = 0; i < RegionIds.Length; i++) { b[2 * i] = (byte)(RegionIds[i] & 255); b[2 * i + 1] = (byte)(RegionIds[i] >> 8); }
        return b;
    }

    /// <summary>Owner number at lon/lat in degrees (0 = sea or no owner).</summary>
    public int IdAt(float lon, float lat)
    {
        int x = Math.Clamp((int)((lon + 180) / 360f * W), 0, W - 1);
        int y = Math.Clamp((int)((90 - lat) / 180f * H), 0, H - 1);
        return Ids[y * W + x];
    }

    /// <summary>Unit-sphere point for lon/lat in degrees (lon 0 at +Z, east towards +X).</summary>
    public static Vector3 ToSphere(float lonDeg, float latDeg)
    {
        float lo = Mathf.DegToRad(lonDeg), la = Mathf.DegToRad(latDeg);
        return new Vector3(Mathf.Cos(la) * Mathf.Sin(lo), Mathf.Sin(la), Mathf.Cos(la) * Mathf.Cos(lo));
    }

    public static Vector2 ToLonLat(Vector3 p) => new(Mathf.RadToDeg(Mathf.Atan2(p.X, p.Z)), Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(p.Y, -1f, 1f))));
}
