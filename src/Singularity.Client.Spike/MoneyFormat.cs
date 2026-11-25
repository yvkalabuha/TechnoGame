#nullable disable
using System;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Singularity.Client.Spike;

/// <summary>Godot-free display of money (docs/game-design/economy/dollars-1990.md, O-079). The world counts budget indices; the player sees constant 1990 dollars.
/// The factor K and the index scale come from <c>data/dollars_1990.json</c> with their source; nothing here enters the world state or a golden.</summary>
public static class MoneyFormat
{
    /// <summary>Parameters of the display scale as loaded from the data file.</summary>
    public sealed record Scale(double K, double Deflator2011, double Deflator1950, double MillionUsd1950PerIndexMonth, int EffortStopPercent, string Source)
    {
        /// <summary>Million 1990 dollars in one budget index (a monthly amount): 80 million 1950 dollars to 2011 prices, then K.</summary>
        public double Million1990PerIndex => MillionUsd1950PerIndexMonth * (Deflator2011 / Deflator1950) * K;
    }

    public static Scale Current { get; private set; } = null;

    /// <summary>Reads the data file text; throws on a missing field, so a broken file never shows wrong money.</summary>
    public static Scale Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        double D(string n) => r.GetProperty(n).GetDouble();
        var s = new Scale(D("k_1990_per_2011"), D("deflator_2011"), D("deflator_1950"), D("million_usd_1950_per_index_month"), (int)D("effort_stop_percent"), r.GetProperty("source").GetString() ?? "");
        if (s.K <= 0 || s.Deflator1950 <= 0 || s.Deflator2011 <= 0 || s.MillionUsd1950PerIndexMonth <= 0) throw new InvalidDataException("dollars_1990.json: non-positive scale");
        return s;
    }

    public static void Load(string path) => Current = Parse(File.ReadAllText(path));
    public static void Use(Scale s) => Current = s;

    /// <summary>Million 1990 dollars of <paramref name="indices"/> budget indices (a monthly flow, or a sum of such months).</summary>
    public static double Million1990(double indices, Scale s = null) => indices * (s ?? Current ?? throw new InvalidOperationException("money scale not loaded")).Million1990PerIndex;

    /// <summary>The yearly amount of a monthly flow: twelve months of <paramref name="monthlyIndices"/>.</summary>
    public static double Million1990PerYear(double monthlyIndices, Scale s = null) => Million1990(monthlyIndices, s) * 12;

    static readonly string Nbsp = ((char)160).ToString();

    /// <summary>«$850 млн», «$30,6 млрд», «$31 млрд»... one digit after the comma below 100 billion, whole above; «$1,5 трлн» from a trillion.
    /// The unit follows a no-break space. The suffix «(в ценах 1990)» is added by the caller once per line.</summary>
    public static string Format(double million1990, bool ru, bool decimalComma)
    {
        long m = (long)Math.Round(Math.Abs(million1990), MidpointRounding.AwayFromZero);
        string sign = million1990 < 0 && m > 0 ? "-" : "";
        string unit, num;
        if (m < 1000) { unit = ru ? "млн" : "M"; num = m.ToString(CultureInfo.InvariantCulture); }
        else if (m < 1_000_000)
        {
            unit = ru ? "млрд" : "B";
            double b = Math.Round(m / 1000.0, 1, MidpointRounding.AwayFromZero);
            // «$31 млрд», not «$31,0 млрд» (dollars-1990.md): the digit after the comma only when it is not zero
            num = b >= 100 || b == Math.Floor(b) ? Math.Round(b, MidpointRounding.AwayFromZero).ToString("F0", CultureInfo.InvariantCulture) : b.ToString("F1", CultureInfo.InvariantCulture);
        }
        else { unit = ru ? "трлн" : "T"; num = Math.Round(m / 1_000_000.0, 1, MidpointRounding.AwayFromZero).ToString("F1", CultureInfo.InvariantCulture); }
        if (decimalComma) num = num.Replace('.', ',');
        return sign + "$" + num + Nbsp + unit;
    }

    /// <summary>Format of an amount of budget indices (a flow or a stock): the dollars of <see cref="Million1990"/>.</summary>
    public static string FormatIndices(double indices, bool ru, bool decimalComma, Scale s = null) => Format(Million1990(indices, s), ru, decimalComma);
}
