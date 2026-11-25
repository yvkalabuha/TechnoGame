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
    public int Group;        // colony label: colony group number (1..), else 0
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
    public byte[] RegionLut = Array.Empty<byte>();      // LutWidth x 3 RGBA8
    public readonly List<Country> ColonyLabels = new();   // Id 0, Name "C_KEY": labels of the large colonial areas (locale polity.C_KEY)
    int[] _colonyGroup = Array.Empty<int>();
    bool[] _hasOwnLand = Array.Empty<bool>();   // owner without home land that has dependent land outside every named colony group
    bool[] _hasHome = Array.Empty<bool>();   // owner has land that is not dependent; one that has none (Anglo-Egyptian Sudan) is labelled on its dependent land
    public WorldMap Map;
    public CountryPalette.Result Style;

    public void Load(string repoRoot)
    {
        Map = WorldMap.Load(repoRoot);
        RegionIds = Map.RasteriseRegions();
        if (Map.Owners.Length > 255 || Map.RegionIds.Length >= LutWidth) throw new InvalidOperationException("owner raster is 8-bit and the region table is 512 wide");
        Ids = Map.OwnerRaster(RegionIds);
        _hasHome = new bool[Map.Owners.Length + 1];
        for (int i = 0; i < Map.Rows.Length; i++) if (Map.RegionOwner[i] != 0 && !CountryPalette.IsDependent(Map.Rows[i].Status)) _hasHome[Map.RegionOwner[i]] = true;
        Style = CountryPalette.Build(Map);
        var area = new long[Map.Owners.Length + 1];
        foreach (var o in Ids) area[o]++;
        var anchors = Map.LabelAnchors(RegionIds);
        for (int o = 1; o <= Map.Owners.Length; o++)
            Countries.Add(new Country { Id = o, Name = Map.Owners[o - 1], BoxArea = area[o], LabelPoint = new Vector2(anchors[o].Lon, anchors[o].Lat), Tint = Style.OwnerTone[o] });

        var canchors = Map.ColonyAnchors(RegionIds);
        var cg = _colonyGroup = Map.ColonyGroupOfRegion();
        _hasOwnLand = new bool[Map.Owners.Length + 1];
        for (int i = 0; i < Map.Rows.Length; i++) if (Map.RegionOwner[i] != 0 && cg[i] == 0 && CountryPalette.IsDependent(Map.Rows[i].Status)) _hasOwnLand[Map.RegionOwner[i]] = true;
        var carea = new long[WorldMap.ColonyGroups.Length + 1];
        foreach (var rg in RegionIds) if (rg != 0) carea[cg[rg - 1]]++;
        for (int k = 1; k <= WorldMap.ColonyGroups.Length; k++)
            if (carea[k] > 0) ColonyLabels.Add(new Country { Id = 0, Name = "C_" + WorldMap.ColonyGroups[k - 1].Key, BoxArea = carea[k], LabelPoint = new Vector2(canchors[k].Lon, canchors[k].Lat), Group = k, Tint = Style.CodeTone[Math.Max(0, Array.IndexOf(Style.Codes, WorldMap.ColonyGroups[k - 1].Owner))] });
        ColonyLabels.Sort((a, b) => b.BoxArea.CompareTo(a.BoxArea));

        if (Style.Controllers.Length > 255) throw new InvalidOperationException("controller number is 8-bit in the region table");
        RegionLut = new byte[LutWidth * 3 * 4];
        for (int i = 0; i < Style.Regions.Length; i++)
        {
            var s = Style.Regions[i]; int id = i + 1;
            Put(0, id, s.Hatch == HatchKind.Colony ? CountryPalette.ColonyFill(s.Tone) : Fill(s.Tone), (byte)((int)s.Hatch * 85));
            Put(1, id, Line(s.HatchTone, HatchLogic.LineScale(s.Hatch)), (byte)s.Owner);
            Put(2, id, ((byte)s.Controller, 0, 0), 255);   // row 2: controller number in R (the line of control must not depend on the tone)
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

    /// <summary>The stand-in of tint.N.line: the fill at 42 % in linear light (HatchLogic.Line).</summary>
    public static (byte, byte, byte) Line(int tone, float scale = 0.42f) => HatchLogic.Line(tone, scale);

    /// <summary>The ID raster as RG8 bytes (low byte, high byte of the region number, bit 7 of the high byte = coast-worthy land).</summary>
    public byte[] RegionIdBytes()
    {
        var b = new byte[RegionIds.Length * 2];
        var coast = CoastMask.Build(RegionIds, W, H);
        // High byte: bits 0..6 = region number >> 8, bit 7 = land that carries a coast stroke (not an islet speck).
        for (int i = 0; i < RegionIds.Length; i++) { b[2 * i] = (byte)(RegionIds[i] & 255); b[2 * i + 1] = (byte)((RegionIds[i] >> 8) | (coast[i] ? 128 : 0)); }
        return b;
    }

    /// <summary>True when the point of the globe belongs to the land a label names: the non-dependent land of an owner (country labels) or the colony group (colony labels).</summary>
    public bool InLabelRegion(Country c, float lon, float lat)
    {
        int x = Math.Clamp((int)((lon + 180) / 360f * W), 0, W - 1);
        int y = Math.Clamp((int)((90 - lat) / 180f * H), 0, H - 1);
        int r = RegionIds[y * W + x];
        if (r == 0) return false;
        if (c.Id == 0) return c.Group > 0 && _colonyGroup[r - 1] == c.Group;
        return (!CountryPalette.IsDependent(Map.Rows[r - 1].Status) || (!_hasHome[c.Id] && (_colonyGroup[r - 1] == 0 || !_hasOwnLand[c.Id]))) && Ids[y * W + x] == c.Id;   // an occupied colony (Libya under Britain) is not Italy's own land
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
