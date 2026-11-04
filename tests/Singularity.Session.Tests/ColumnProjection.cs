using System.Security.Cryptography;
using System.Text;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>
/// Hash of a chosen subset of the table columns (by name, in registration order): the check «NO_RETIRE gives the trajectory of the
/// older build by projection on the columns that existed then» (contract r6 §8.5, spec T-fleet 5).
/// </summary>
internal static class ColumnProjection
{
    public static string Hash(SliceSession s, Func<string, bool> include)
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (Column c in s.World.Table.Columns)
        {
            if (!include(c.Name))
                continue;
            h.AppendData(Encoding.UTF8.GetBytes(c.Name));
            switch (c)
            {
                case LongColumn l:
                    foreach (long v in l.Current)
                        h.AppendData(BitConverter.GetBytes(v));
                    break;
                case IntColumn i:
                    foreach (int v in i.Current)
                        h.AppendData(BitConverter.GetBytes(v));
                    break;
            }
        }
        return Convert.ToHexStringLower(h.GetHashAndReset());
    }

    public static IEnumerable<string> Names(SliceSession s) => s.World.Table.Columns.Select(c => c.Name);
}
