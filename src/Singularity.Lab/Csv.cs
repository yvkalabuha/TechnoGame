using System.Text;

namespace Singularity.Lab;

/// <summary>RFC 4180-style CSV: comma, "\n" line ends, quotes only when needed, UTF-8 without BOM.</summary>
public static class Csv
{
    public static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static string Line(IEnumerable<string> cells)
    {
        var sb = new StringBuilder();
        bool first = true;
        foreach (string cell in cells)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(Escape(cell));
        }
        return sb.ToString();
    }

    public static string Escape(string cell) =>
        cell.IndexOfAny([',', '"', '\n', '\r']) < 0 ? cell : "\"" + cell.Replace("\"", "\"\"") + "\"";

    public static StreamWriter Create(string path) => new(path, append: false, Utf8NoBom) { NewLine = "\n" };
}
