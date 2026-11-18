namespace Singularity.Lab.Bench;

/// <summary>
/// Order statistics of the bench (host-side only, never world state). Percentiles use linear interpolation between order statistics
/// (Hyndman–Fan type 7, the R/NumPy default): <c>h = (n − 1)·p</c>, <c>x[⌊h⌋] + (h − ⌊h⌋)·(x[⌊h⌋+1] − x[⌊h⌋])</c>.
/// </summary>
public static class BenchStats
{
    /// <summary>Percentile <paramref name="p"/> ∈ [0, 1] of the values (type 7). Throws on an empty input or a NaN.</summary>
    public static double Percentile(IReadOnlyList<double> values, double p)
    {
        if (values.Count == 0)
            throw new ArgumentException("No values.", nameof(values));
        if (!(p >= 0 && p <= 1))
            throw new ArgumentOutOfRangeException(nameof(p), p, "p must be in [0, 1].");
        double[] x = Sorted(values);
        double h = (x.Length - 1) * p;
        int lo = (int)Math.Floor(h);
        if (lo >= x.Length - 1)
            return x[^1];
        return x[lo] + (h - lo) * (x[lo + 1] - x[lo]);
    }

    public static double Median(IReadOnlyList<double> values) => Percentile(values, 0.5);

    /// <summary>The summary written for every series: count, min, p10, p50, p90, p95, p99, max, mean.</summary>
    public static Summary Summarize(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            throw new ArgumentException("No values.", nameof(values));
        double[] x = Sorted(values);
        double sum = 0;
        foreach (double v in x)
            sum += v;
        return new Summary(x.Length, x[0], Percentile(x, 0.10), Percentile(x, 0.50), Percentile(x, 0.90), Percentile(x, 0.95),
            Percentile(x, 0.99), x[^1], sum / x.Length);
    }

    private static double[] Sorted(IReadOnlyList<double> values)
    {
        var x = new double[values.Count];
        for (int i = 0; i < x.Length; i++)
        {
            if (double.IsNaN(values[i]))
                throw new ArgumentException("NaN in values.", nameof(values));
            x[i] = values[i];
        }
        Array.Sort(x);
        return x;
    }

    public sealed record Summary(int N, double Min, double P10, double P50, double P90, double P95, double P99, double Max, double Mean);
}
