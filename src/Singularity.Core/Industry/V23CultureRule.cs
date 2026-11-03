using Singularity.Core.Data;
using Singularity.Kernel;

namespace Singularity.Core.Industry;

/// <summary>
/// The v2.3 culture rule (volume share, no money channel) kept only as an ablation behind
/// <see cref="ICultureRule"/> (contract §4.1a, Appendix A). It takes no money for process development:
/// <c>E_useful = 0</c>, so the whole item returns to the tooling argument <c>rest_a</c>.
/// </summary>
public sealed class V23CultureRule : ICultureRule
{
    public string Name => "R0 v2.3";

    public CultureStep Step(ICultureSnapshot s, int site, int cluster, int dim, long month) =>
        StepWith(s, site, cluster, dim, month, -1, Fx6.Zero);

    public Fx6 EUseful(ICultureSnapshot s, int site, int processRow, long month) => Fx6.Zero;

    public CultureLimiter Limiter(ICultureSnapshot s, int site, int processRow, long month)
    {
        IndustryLayout l = s.Layout;
        int c = l.ClusterIndex[processRow];
        int best = -1;
        Fx6 bestGap = Fx6.Zero;
        for (int d = 0; d < Dims3.Count; d++)
        {
            if (l.Req[processRow][d] <= Fx6.Zero)
                continue;
            Fx6 gap = l.Req[processRow][d] - s.H(site, c, d);
            if (best < 0 || gap > bestGap)
            {
                best = d;
                bestGap = gap;
            }
        }
        int number = l.Number[processRow];
        if (best < 0 || bestGap + Fx6.FromRaw(250_000) <= Fx6.Zero)
            return new CultureLimiter(LimiterKind.None, number);
        if (s.M(site, c, best) < s.H(site, c, best))
            return new CultureLimiter(LimiterKind.Tooling, number);
        if (s.Hcap(site, best, month) <= l.Req[processRow][best])
            return new CultureLimiter(LimiterKind.Saturation, number);
        return new CultureLimiter(LimiterKind.Volume, number);
    }

    public Fx6 DHForecast(ICultureSnapshot s, int site, int processRow, int dim, Fx6 e, Fx6 v, long month) =>
        StepWith(s, site, s.Layout.ClusterIndex[processRow], dim, month, processRow, v).DHPlusCapped;

    // overrideRow >= 0: that process runs with overrideV instead of proc_v.
    private static CultureStep StepWith(ICultureSnapshot s, int site, int c, int d, long month, int overrideRow, Fx6 overrideV)
    {
        IndustryLayout l = s.Layout;
        Span<CultureProcess> procs = stackalloc CultureProcess[l.ProcessCount];
        int np = 0;
        foreach (int r in l.Order)
        {
            Fx6 v = r == overrideRow ? overrideV : s.V(site, r);
            if (l.ClusterIndex[r] == c && v > Fx6.Zero)
                procs[np++] = new CultureProcess(l.Number[r], v, l.Req[r][d], s.WorldLead(site, r));
        }
        Span<CultureSpill> spill = stackalloc CultureSpill[l.Clusters.Length];
        int ns = 0;
        for (int c2 = 0; c2 < l.Clusters.Length; c2++)
        {
            if (c2 != c)
                spill[ns++] = new CultureSpill(s.Params.Spill(l.Clusters[c], l.Clusters[c2]), s.H(site, c2, d));
        }
        var cell = new CultureCell(s.H(site, c, d), s.Hcap(site, d, month), s.Act(site, c), s.W(site, c, month),
            s.QualityProgramme(site, month) && d == (int)Dimension.QualityControl);
        return IndustryMath.Culture(cell, procs[..np], spill[..ns], s.Params);
    }
}
