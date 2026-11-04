using System.Collections.Immutable;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Core.WhyNot;

public sealed partial class WhyNotAnalyzer
{
    // Limiter levers, then the quality programme for the quality_control dimension, then the lighter variant (contract §6 table).
    private static Lever[] Wanted(Lever[] first, Dimension? dim, int variantSlot)
    {
        var list = new List<Lever>(first);
        if (dim == Dimension.QualityControl)
            list.Add(Lever.QualityProgramme);
        if (variantSlot >= 0)
            list.Add(Lever.SimplifiedVariant);
        return [.. list];
    }

    private Fx6 MinYield(int site, int[] prc, long month)
    {
        Fx6 min = Fx6.Zero;
        for (int i = 0; i < prc.Length; i++)
        {
            Fx6 y = _i.Yield(site, _il.RowOf(prc[i]), month).Yield;
            if (i == 0 || y < min)
                min = y;
        }
        return min;
    }

    /// <summary>
    /// CAPABILITY links (contract §6): every known process of the set that is not admitted or has <c>yield &lt; 0.7</c>. The link is the
    /// bottleneck dimension of the process (<c>H</c> or <c>M</c> source) or, when every <c>g_d = 1</c>, its <c>ramp</c>. For an H source it
    /// carries <c>limiter(site, p)</c> and <c>subject_id = subject_prc</c> (F10: the limiter of the process, no rupture). <c>yield_if_fixed</c>
    /// is the lowest yield of the set once only this link is closed. <paramref name="unitsPerMonth"/> is the rate of the process's line.
    /// </summary>
    private void CapabilityLinks(List<Link> links, int actor, int site, int[] alts, int gen, Fx6 unitsPerMonth, long month)
    {
        int[] prc = DesignRules.PrcSet(_pl, alts);
        var ys = new ProcessYield[prc.Length];
        for (int i = 0; i < prc.Length; i++)
            ys[i] = _i.Yield(site, _il.RowOf(prc[i]), month);

        for (int i = 0; i < prc.Length; i++)
        {
            ProcessYield y = ys[i];
            if (!Knows(actor, prc[i]) || y.Yield >= LowYield)
                continue;
            int row = _il.RowOf(prc[i]);
            int cluster = _il.ClusterIndex[row];
            int slot = VariantSlot(actor, alts, gen, prc[i]);

            Dimension? dim = null;
            Fx6 minG = Fx6.One;
            for (int d = 0; d < Dims3.Count; d++)
            {
                if (y.IsActive(d) && y.G[d] < minG)
                {
                    minG = y.G[d];
                    dim = (Dimension)d;
                }
            }

            Fx6 fixedYield, have, need;
            int? eta;
            WhyNotCause cause;
            int subject = prc[i];
            LimiterKind? limiterKind = null;
            ImmutableArray<Lever> levers;
            int linkCluster = -1, order = -1;
            Fx6 unmet = Fx6.Zero;
            if (dim is { } dm)
            {
                int d = (int)dm;
                Fx6 h = _i.CultureH(site, cluster, dm), m = _i.ToolingM(site, cluster, dm), req = _il.Req[row][d];
                bool fromM = y.Source(d) == CapabilitySource.M;
                need = req;
                Fx6 c2;
                if (fromM)
                {
                    have = m;
                    Fx6 m2 = Max(m, need);
                    c2 = m2 < h ? m2 : h;
                }
                else
                {
                    have = h;
                    Fx6 h2 = Max(h, need);
                    c2 = m < h2 ? m : h2;
                }
                Fx6 acc = Fx6.One;
                for (int d2 = 0; d2 < Dims3.Count; d2++)
                {
                    if (y.IsActive(d2))
                        acc *= d2 == d ? IndustryMath.G(c2 - req) : y.G[d2];
                }
                fixedYield = acc * y.Ramp;

                if (fromM)
                {
                    // r8 (F33): the plan of the last month left the cell short only because the pool ran out. The key and the levers are those of ToolingM.
                    unmet = _i.ToolingUnmet(site, cluster, dm);
                    cause = unmet > Fx6.Zero ? WhyNotCause.ToolingPoolExhausted : WhyNotCause.ToolingM;
                    if (cause == WhyNotCause.ToolingPoolExhausted)
                    {
                        linkCluster = cluster;
                        order = _in.PoolQueuePlace(cluster);
                    }
                    else
                    {
                        unmet = Fx6.Zero;
                    }
                    eta = Eta(have, need, _in.ToolingRate(site, cluster, dm, month));
                    levers = Offer(Wanted([Lever.InvestToolingM], dm, slot));
                }
                else
                {
                    cause = WhyNotCause.CultureH;
                    CultureLimiter lim = _i.Limiter(site, row, month);
                    limiterKind = lim.Value;
                    if (lim.SubjectProcess >= 0)
                        subject = lim.SubjectProcess;
                    Fx6 e = _i.ProcE(site, row);
                    Fx6 rate = lim.Value switch
                    {
                        LimiterKind.LabCeiling or LimiterKind.Saturation => Fx6.Zero,
                        LimiterKind.Effort when e <= Fx6.Zero => Fx6.Zero,
                        LimiterKind.Tooling => _in.ToolingRate(site, cluster, dm, month),
                        _ => _i.DHForecast(site, row, dm, e, _i.ProcV(site, row), month),
                    };
                    eta = Eta(have, need, rate);
                    // r8: the cause stays CultureH; a TOOLING limit only carries the cell's shortfall for the UI (the link's own cell, as the rate above).
                    if (lim.Value == LimiterKind.Tooling)
                        unmet = _i.ToolingUnmet(site, cluster, dm);
                    Lever[] byLimiter = lim.Value switch
                    {
                        LimiterKind.Effort => [Lever.ProcessDevelopment],
                        LimiterKind.Volume => _in.LicenceOffered(site, prc[i], month) ? [Lever.TrainingSeries, Lever.Licence] : [Lever.TrainingSeries],
                        LimiterKind.LabCeiling => [Lever.TrainingSeries],
                        LimiterKind.Tooling => [Lever.InvestToolingM],
                        _ => [],
                    };
                    levers = Offer(Wanted(byLimiter, dm, slot));
                }
            }
            else
            {
                // Only ramp limits: X must reach the point where ramp = 0.7 (the yield threshold), X = X½_eff·(0.7 − λ0)/(1 − 0.7).
                have = _i.ProcX(site, row);
                need = y.XHalfEff * (LowYield - _ip.Lambda0) / (Fx6.One - LowYield);
                Fx6 acc = Fx6.One;
                for (int d2 = 0; d2 < Dims3.Count; d2++)
                {
                    if (y.IsActive(d2))
                        acc *= y.G[d2];
                }
                fixedYield = acc * IndustryMath.Ramp(Max(have, need), y.XHalfEff, _ip.Lambda0);
                cause = WhyNotCause.Ramp;
                int k = _pl.ProcIndex(prc[i]);
                eta = Eta(have, need, k < 0 ? Fx6.Zero : unitsPerMonth * _pl.ProcN[k]);
                levers = Offer(Wanted(_in.LicenceOffered(site, prc[i], month) ? [Lever.Licence] : [], null, slot));
            }

            Fx6 lowest = fixedYield;
            for (int j = 0; j < prc.Length; j++)
            {
                if (j != i && ys[j].Yield < lowest)
                    lowest = ys[j].Yield;
            }
            links.Add(new Link(WhyNotLayer.Capability, cause, subject, have, need, levers, lowest, eta, prc[i], dim, limiterKind, slot, Cluster: linkCluster, Order: order, Unmet: unmet));
        }
    }

    /// <summary>
    /// The slot a «simplified_variant» would replace (contract §6): in the lightening order, a slot (never the engine, the airframe not
    /// below the platform threshold) whose alternative holds <paramref name="process"/> (any process if −1) and that has an available
    /// lower-rank alternative without it; −1 if there is none.
    /// </summary>
    private int VariantSlot(int actor, int[] alts, int gen, int process)
    {
        foreach (int s in _pl.LightenOrder)
        {
            int cur = alts[s];
            if (s == _pl.BaseSlot || cur < 0 || (process >= 0 && _pl.PrcOf(cur).IndexOf(process) < 0))
                continue;
            for (int a = _pl.AltStart[s]; a < _pl.AltStart[s + 1]; a++)
            {
                if (_pl.AltRank[a] < _pl.AltRank[cur] && Counts(s, a, gen) && DesignRules.Available(_pl, a, n => Knows(actor, n))
                    && (process < 0 || _pl.PrcOf(a).IndexOf(process) < 0))
                    return s;
            }
        }
        return -1;
    }
}
