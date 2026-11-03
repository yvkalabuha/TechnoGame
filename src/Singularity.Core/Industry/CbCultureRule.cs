using Singularity.Core.Data;
using Singularity.Kernel;

namespace Singularity.Core.Industry;

/// <summary>The parts of <c>t_p</c> of one process in one active dimension (contract v2.4 §4.1).</summary>
internal readonly record struct CbParts(Fx6 Cp, Fx6 Gap, Fx6 GapL, Fx6 T, Fx6 THat);

/// <summary>
/// Temporary culture rule C-B (contract v2.4 §4.1, §4.1a; decisions #99, #111, #115, #117): money for process
/// development works through a ceiling that grows with output (<c>c_p</c>, smoothstep) plus a small laboratory
/// part (<c>ℓ</c>); a cell takes the best process (<c>max</c>), not a volume share. Canonical form: do not
/// reorder (contract §4.0). <c>lead</c>, <c>limiter₀</c>, <c>d*</c>, <c>eligible</c> are internal.
/// </summary>
public sealed class CbCultureRule : ICultureRule
{
    private static readonly Fx6 GapOffset = Fx6.FromRaw(250_000);

    public string Name => "C-B v2.4";

    public CultureStep Step(ICultureSnapshot s, int site, int cluster, int dim, long month) =>
        CellStep(s, site, cluster, dim, month, -1, Fx6.Zero, Fx6.Zero);

    public Fx6 EUseful(ICultureSnapshot s, int site, int processRow, long month)
    {
        int d = DStar(s, site, processRow, month);
        if (d < 0)
            return Fx6.Zero;
        int c = s.Layout.ClusterIndex[processRow];
        if (Lead(s, site, c, d, month) != processRow || Limiter0(s, site, processRow, d, month) != LimiterKind.Effort)
            return Fx6.Zero;
        return DetMath.MulCeil(KStar(s, site, processRow, d, month), s.Params.EFull);
    }

    public CultureLimiter Limiter(ICultureSnapshot s, int site, int processRow, long month)
    {
        IndustryLayout l = s.Layout;
        int number = l.Number[processRow];
        int d = DStar(s, site, processRow, month);
        if (d < 0)
            return new CultureLimiter(LimiterKind.None, number);
        int c = l.ClusterIndex[processRow];
        if (!LeadsAny(s, site, processRow, month))
        {
            int lead = Lead(s, site, c, d, month);
            if (lead >= 0)
                return Limiter(s, site, lead, month); // leads a cell, so the recursion ends there
            // No lead: by construction p is not an EFFORT candidate, or not eligible (stand only).
            LimiterKind l0 = Limiter0(s, site, processRow, d, month);
            return new CultureLimiter(l0 == LimiterKind.Effort ? LimiterKind.LabCeiling : l0, number);
        }
        LimiterKind? first = FirstFour(s, site, processRow, d, month);
        if (first is not null)
            return new CultureLimiter(first.Value, number);
        Fx6 kStar = KStar(s, site, processRow, d, month);
        bool volume = s.V(site, processRow) > Fx6.Zero && s.E(site, processRow) >= EUseful(s, site, processRow, month) && kStar < Fx6.One;
        return new CultureLimiter(volume ? LimiterKind.Volume : LimiterKind.Effort, number);
    }

    public Fx6 DHForecast(ICultureSnapshot s, int site, int processRow, int dim, Fx6 e, Fx6 v, long month) =>
        CellStep(s, site, s.Layout.ClusterIndex[processRow], dim, month, processRow, e, v).DHPlusCapped;

    // ---- the cell step (contract §4.1)

    // overrideRow >= 0: that process runs with overrideE, overrideV instead of proc_E, proc_v (dH_forecast).
    private static CultureStep CellStep(ICultureSnapshot s, int site, int c, int d, long month, int overrideRow, Fx6 overrideE, Fx6 overrideV)
    {
        IndustryLayout l = s.Layout;
        IndustryParams p = s.Params;
        Fx6 h = s.H(site, c, d), hcap = s.Hcap(site, d, month), act = s.Act(site, c);
        Fx6 hFloor = p.PhiFloor * hcap;
        Fx6 aR = Min(Fx6.One, act / p.ActNorm);
        Fx6 drive = Fx6.Zero, catchUp = Fx6.Zero, maint = hFloor;
        foreach (int r in l.Order)
        {
            if (l.ClusterIndex[r] != c || l.Req[r][d] <= Fx6.Zero)
                continue;
            Fx6 v = r == overrideRow ? overrideV : s.V(site, r);
            Fx6 e = r == overrideRow ? overrideE : s.E(site, r);
            if (v <= Fx6.Zero && e <= Fx6.Zero)
                continue; // P(c,d): output or money
            Fx6 vt = TildeV(s, site, r, v, month);
            CbParts parts = Parts(s, site, r, d, vt, e);
            if (parts.T > drive)
                drive = parts.T;
            if (parts.T > catchUp && s.WorldLead(site, r))
                catchUp = parts.T;
            Fx6 vKeep = p.RKeep * l.Labour[r];
            Fx6 mp = vKeep == Fx6.Zero ? Fx6.Zero : Min(Fx6.One, vt / vKeep);
            Fx6 maintP = hFloor + (Max(l.Req[r][d] * aR, hFloor) - hFloor) * mp;
            if (maintP > maint)
                maint = maintP;
        }
        Fx6 spillSum = Fx6.Zero;
        for (int c2 = 0; c2 < l.Clusters.Length; c2++)
        {
            if (c2 != c)
                spillSum += p.Spill(l.Clusters[c], l.Clusters[c2]) * Max0(s.H(site, c2, d) - h);
        }
        Fx6 sat = Max0(Fx6.One - h / hcap);
        Fx6 alphaEff = s.QualityProgramme(site, month) && d == (int)Dimension.QualityControl ? p.Alpha * (Fx6.One + p.KQp) : p.Alpha;
        Fx6 inner = alphaEff * act * s.W(site, c, month) * drive + p.KCatch * catchUp + p.Sigma * spillSum;
        Fx6 dPlus = sat * inner;
        Fx6 capped = dPlus < p.RhoMax ? dPlus : p.RhoMax;
        Fx6 dMinus = p.DeltaH * Max0(h - Max(maint, hFloor));
        // VC (volume share of v2.3) does not exist in C-B.
        return new CultureStep(Fx6.Zero, drive, catchUp, maint, spillSum, hFloor, sat, alphaEff, dPlus, capped, dMinus, h + capped - dMinus);
    }

    /// <summary><c>ṽ_p = (1 + k_tr·lic_p)·v_p</c>, lic ∈ {0, 1}.</summary>
    private static Fx6 TildeV(ICultureSnapshot s, int site, int r, Fx6 v, long month) =>
        (Fx6.One + s.Params.KTr * (s.Licensed(site, r, month) ? Fx6.One : Fx6.Zero)) * v;

    /// <summary><c>c_p</c>, gaps, <c>t_p</c> and <c>t̂_p</c> of process r in dimension d for the given <c>ṽ</c> and <c>E</c>.</summary>
    private static CbParts Parts(ICultureSnapshot s, int site, int r, int d, Fx6 vTilde, Fx6 e)
    {
        IndustryLayout l = s.Layout;
        IndustryParams p = s.Params;
        Fx6 h = s.H(site, l.ClusterIndex[r], d), rd = l.Req[r][d];
        Fx6 vCap = p.RCap * l.Labour[r];
        Fx6 xV = vCap == Fx6.Zero ? Fx6.Zero : Min(Fx6.One, vTilde / vCap);
        Fx6 cp = DetMath.SmoothStep01(xV);
        Fx6 xE = p.EFull == Fx6.Zero ? Fx6.Zero : Min(Fx6.One, e / p.EFull);
        Fx6 eV = Min(xE, cp);
        Fx6 eL = p.Ell == Fx6.Zero ? Fx6.Zero : Min(xE, p.Ell);
        Fx6 gap = Max0(rd + GapOffset - h);
        Fx6 gapL = Max0(rd - p.DeltaL - h);
        Fx6 t = Max(eV * gap, eL * gapL);
        Fx6 tHat = Max(cp * gap, p.Ell * gapL);
        return new CbParts(cp, gap, gapL, t, tHat);
    }

    // ---- internal functions of §4.1a (pure functions of the snapshot)

    /// <summary><c>eligible(site, p)</c> (G4-2): known, and with output or planned.</summary>
    internal static bool Eligible(ICultureSnapshot s, int site, int r, long month) =>
        s.Knows(site, r, month) && (s.V(site, r) > Fx6.Zero || s.Planned(site, r, month));

    private static CbParts FullEffort(ICultureSnapshot s, int site, int r, int d, long month) =>
        Parts(s, site, r, d, TildeV(s, site, r, s.V(site, r), month), Fx6.Zero);

    /// <summary>Steps 1–4 of <c>limiter</c> for dimension d; null when none fires.</summary>
    private static LimiterKind? FirstFour(ICultureSnapshot s, int site, int r, int d, long month)
    {
        IndustryLayout l = s.Layout;
        IndustryParams p = s.Params;
        int c = l.ClusterIndex[r];
        Fx6 h = s.H(site, c, d), rd = l.Req[r][d];
        if (Max0(rd + GapOffset - h) == Fx6.Zero)
            return LimiterKind.None;
        if (s.M(site, c, d) < h)
            return LimiterKind.Tooling;
        if (s.Hcap(site, d, month) <= rd)
            return LimiterKind.Saturation;
        if (s.V(site, r) == Fx6.Zero && (p.Ell == Fx6.Zero || h >= rd - p.DeltaL))
            return LimiterKind.LabCeiling;
        return null;
    }

    /// <summary><c>limiter₀(site, p, d)</c>: own limit at E = 0; independent of <c>lead</c>, <c>d*</c> and E.</summary>
    internal static LimiterKind Limiter0(ICultureSnapshot s, int site, int r, int d, long month)
    {
        LimiterKind? first = FirstFour(s, site, r, d, month);
        if (first is not null)
            return first.Value;
        return FullEffort(s, site, r, d, month).THat > Fx6.Zero ? LimiterKind.Effort : LimiterKind.Volume;
    }

    /// <summary><c>lead(site, c, d)</c> (#115, G4-2): the eligible EFFORT candidate with the largest <c>t̂_p</c>, tie by lower number; −1 for none.</summary>
    internal static int Lead(ICultureSnapshot s, int site, int c, int d, long month)
    {
        IndustryLayout l = s.Layout;
        int best = -1;
        Fx6 bestT = Fx6.Zero;
        foreach (int r in l.Order)
        {
            if (l.ClusterIndex[r] != c || l.Req[r][d] <= Fx6.Zero || !Eligible(s, site, r, month)
                || Limiter0(s, site, r, d, month) != LimiterKind.Effort)
                continue;
            Fx6 t = FullEffort(s, site, r, d, month).THat;
            if (best < 0 || t > bestT)
            {
                best = r;
                bestT = t;
            }
        }
        return best;
    }

    private static bool LeadsAny(ICultureSnapshot s, int site, int r, long month)
    {
        int c = s.Layout.ClusterIndex[r];
        for (int d = 0; d < Dims3.Count; d++)
        {
            if (s.Layout.Req[r][d] > Fx6.Zero && Lead(s, site, c, d, month) == r)
                return true;
        }
        return false;
    }

    /// <summary>
    /// <c>d*(p)</c>: the dimension with the largest <c>r_d − H</c> among the cells p leads, else among all
    /// its active dimensions; ties by canonical order; −1 when p has no active dimension.
    /// </summary>
    internal static int DStar(ICultureSnapshot s, int site, int r, long month)
    {
        int c = s.Layout.ClusterIndex[r];
        int best = Pick(s, site, r, c, month, ledOnly: true);
        return best >= 0 ? best : Pick(s, site, r, c, month, ledOnly: false);
    }

    private static int Pick(ICultureSnapshot s, int site, int r, int c, long month, bool ledOnly)
    {
        int best = -1;
        Fx6 bestGap = Fx6.Zero;
        for (int d = 0; d < Dims3.Count; d++)
        {
            if (s.Layout.Req[r][d] <= Fx6.Zero || (ledOnly && Lead(s, site, c, d, month) != r))
                continue;
            Fx6 gap = s.Layout.Req[r][d] - s.H(site, c, d);
            if (best < 0 || gap > bestGap)
            {
                best = d;
                bestGap = gap;
            }
        }
        return best;
    }

    /// <summary><c>k*</c>: ℓ when the laboratory part of <c>t̂</c> wins, else <c>c_p</c> (the kink of <c>t_p</c> over E).</summary>
    internal static Fx6 KStar(ICultureSnapshot s, int site, int r, int d, long month)
    {
        CbParts f = FullEffort(s, site, r, d, month);
        return s.Params.Ell * f.GapL > f.Cp * f.Gap ? s.Params.Ell : f.Cp;
    }

    private static Fx6 Max0(Fx6 x) => x > Fx6.Zero ? x : Fx6.Zero;

    private static Fx6 Max(Fx6 a, Fx6 b) => a > b ? a : b;

    private static Fx6 Min(Fx6 a, Fx6 b) => a < b ? a : b;
}
