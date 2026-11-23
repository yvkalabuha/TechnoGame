using System.Collections;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>Helpers of the view and switch tests: a deterministic text dump of a view and a digest of the state without the run columns.</summary>
internal static class ViewSupport
{
    /// <summary>Deterministic text of a view: records by property name, arrays in order, <see cref="Fx6"/> as raw.</summary>
    public static string Dump(object? value)
    {
        var sb = new StringBuilder();
        Write(sb, value);
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, object? o)
    {
        switch (o)
        {
            case null:
                sb.Append("null");
                break;
            case Fx6 f:
                sb.Append(f.Raw.ToString(CultureInfo.InvariantCulture));
                break;
            case string s:
                sb.Append('"').Append(s).Append('"');
                break;
            case IEnumerable e:
                sb.Append('[');
                foreach (object? item in e)
                {
                    Write(sb, item);
                    sb.Append(',');
                }
                sb.Append(']');
                break;
            case IConvertible c:
                sb.Append(c.ToString(CultureInfo.InvariantCulture));
                break;
            default:
                sb.Append(o.GetType().Name).Append('{');
                foreach (var p in o.GetType().GetProperties().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    sb.Append(p.Name).Append('=');
                    Write(sb, p.GetValue(o));
                    sb.Append(';');
                }
                sb.Append('}');
                break;
        }
    }

    /// <summary>SHA-256 over every column except <c>run.*</c>: the trajectory of the world, whatever the run columns say.</summary>
    public static string TrajectoryDigest(SliceSession session)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (Column c in session.World.Table.Columns)
        {
            if (c.Name.StartsWith("run.", StringComparison.Ordinal))
                continue;
            hash.AppendData(Encoding.UTF8.GetBytes(c.Name));
            switch (c)
            {
                case IntColumn i:
                    foreach (int v in i.Current)
                        hash.AppendData(BitConverter.GetBytes(v));
                    break;
                case LongColumn l:
                    foreach (long v in l.Current)
                        hash.AppendData(BitConverter.GetBytes(v));
                    break;
            }
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>Run on the 61-node data before TASK-0133 (control layers L_K, L_K0, L_P reject generations above 4).</summary>
    public static SliceSession RunLegacy(SliceRunConfig config, int months)
    {
        SliceSession s = SliceTestData.CreateLegacy(config);
        for (long t = 0; t < months; t++)
            s.StepThroughMonth(t);
        return s;
    }

    public static SliceSession Run(SliceRunConfig config, int months)
    {
        SliceSession s = SliceTestData.Create(config);
        for (long t = 0; t < months; t++)
            s.StepThroughMonth(t);
        return s;
    }
}
