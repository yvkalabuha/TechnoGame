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

    /// <summary>
    /// Columns added with the slot generation (ADR-0005 p. 3, TASK-0099): <c>prd.dN.generation</c>, <c>prd.dN.parentGen</c>, <c>prd.lp.gN.gen</c>
    /// and the <c>StaleHandle</c> rejection counter; with the novelty memory (TASK-0110, DEBT-0054): <c>prd.altReached.N</c>; and with the end-to-end
    /// design handle (TASK-0115): the <c>.gen</c> column beside every design reference of <c>cmd.*</c> and <c>pol.*</c>; and with the horizon to 2030 (TASK-0121): <see cref="IsHorizonColumn"/>.
    /// Projections onto "the old columns" leave them out.
    /// </summary>
    public static bool IsAddedSinceBaselineColumn(string name) =>
        name.EndsWith(".generation", StringComparison.Ordinal) || name.EndsWith(".parentGen", StringComparison.Ordinal)
        || name.StartsWith("prd.lp.g", StringComparison.Ordinal) && name.EndsWith(".gen", StringComparison.Ordinal)
        || IsDesignRefGenColumn(name) || IsHorizonColumn(name) || IsReplCreditColumn(name)
        || name == "prd.rejected.StaleHandle" || name.StartsWith("prd.altReached.", StringComparison.Ordinal);

    // Only the new design-reference generation columns; the old pol.lic.gen, pol.lic.q.gen and pol.lic.bad.gen stay in projections.
    public static bool IsDesignRefGenColumn(string name)
    {
        if (!name.EndsWith(".gen", StringComparison.Ordinal)) return false;
        string[] prefixes = ["cmd.dsn.", "cmd.flt.", "cmd.series.", "cmd.close.", "pol.dsn.", "pol.flt.", "pol.start.", "pol.close."];
        foreach (string p in prefixes)
            if (name.StartsWith(p, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>
    /// Columns added with the horizon to 2030 (TASK-0121, #423): the cohorts of the years 1978…2030 (<c>prd.dN.cohort.YYYY</c>) and the second block of
    /// the <c>q</c> mask (<c>prd.dN.q.1</c>). Matched by exact name pattern, never by a broad suffix.
    /// </summary>
    public static bool IsHorizonColumn(string name)
    {
        const string prefix = "prd.d";
        if (!name.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        int dot = name.IndexOf('.', prefix.Length);
        if (dot <= prefix.Length || !int.TryParse(name.AsSpan(prefix.Length, dot - prefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _))
            return false;
        string rest = name[(dot + 1)..];
        if (rest == "q.1")
            return true;
        return rest.StartsWith("cohort.", StringComparison.Ordinal)
            && int.TryParse(rest.AsSpan("cohort.".Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int year)
            && year is >= 1978 and <= 2030;
    }

    /// <summary>The replacement credit of the fleet ageing (TASK-0129, #431): <c>prd.dN.replCredit</c>.</summary>
    public static bool IsReplCreditColumn(string name) =>
        name.StartsWith("prd.d", StringComparison.Ordinal) && name.EndsWith(".replCredit", StringComparison.Ordinal);

    /// <summary>
    /// A design slot has been retaken (generation ≥ 2): from then on the run is no longer the run of a build without slot reuse, so a
    /// comparison with a hash pinned before TASK-0099 stops being meaningful.
    /// </summary>
    public static bool SlotReused(SliceSession s) =>
        s.World.Table.Columns.OfType<IntColumn>().Any(c => c.Name.EndsWith(".generation", StringComparison.Ordinal) && c.Current.ToArray().Any(v => v >= 2));

    public static IEnumerable<string> Names(SliceSession s) => s.World.Table.Columns.Select(c => c.Name);
}
