using System.Reflection;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Products;
using Singularity.Kernel;
using Singularity.Session;

namespace Singularity.Lab.Models.Slice;

/// <summary>
/// Variants of the «introduction channel» of a new process (HYP-0004 sketch, docs/simulation/introduction-channel-sketch.md §1). SKETCH TOOL ONLY:
/// none of them is a rule of the contract; the default of every model is <see cref="None"/> (C-B of Core, untouched).
/// </summary>
public enum IntroVariant
{
    /// <summary>C-B of Core (no substitution).</summary>
    None,

    /// <summary>The Lab copy of C-B without changes (checks that the copy equals Core bit for bit).</summary>
    Cb,

    /// <summary>I-A: <c>ṽ_grow = (1 + k_tr·lic)·max(v, v_exp)</c>.</summary>
    VExp,

    /// <summary>I-C: <c>ṽ_grow = (1 + k_tr·lic)·max(v, V_C)</c> on home(a) — the nominal of the series line only.</summary>
    VExpC,

    /// <summary>I-B: for a planned process the laboratory ceiling is <c>r_d</c> (no ΔL).</summary>
    Lab,
}

/// <summary>
/// HYP-0004 sketch: a copy of <see cref="CbCultureRule"/> (contract v2.4 §4.1, §4.1a) with the variant branches of the sketch §1. Growth reads
/// <c>ṽ_grow</c> (x_V of t_p, t̂_p, E_useful, k*, and v in limiter steps 4–5); maintenance reads the real <c>ṽ</c>. Canonical order as in Core.
/// </summary>
public sealed class IntroCultureRule(IntroVariant variant, Func<int, int, long, Fx6>? seriesNominal = null) : ICultureRule
{
    private static readonly Fx6 GapOffset = Fx6.FromRaw(250_000);

    public string Name => "C-B v2.4 + HYP-0004 " + variant;

    /// <summary>Replaces the culture rule of the session's industry system (reflection; Lab sketch only, before the first step).</summary>
    public static void Install(SliceSession session, IntroVariant variant)
    {
        if (variant == IntroVariant.None)
            return;
        object world = typeof(SliceSession).GetProperty("World", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session)!;
        var ind = (IndustrySystem)world.GetType().GetProperty("Industry")!.GetValue(world)!;
        var prod = (ProductsSystem)world.GetType().GetProperty("Products")!.GetValue(world)!;
        IndustryLayout il = ind.Layout;
        Func<int, int, long, Fx6>? vc = variant != IntroVariant.VExpC ? null : (site, row, month) =>
        {
            // V_C(a, p) of contract §4.1a r4 on home(a): Σ over own designs in stage С of (r_ref·n_p)·labour_p, ascending design id.
            int actor = il.SiteOwner[site];
            if (il.HomeSite(actor) != site)
                return Fx6.Zero;
            ProductsLayout pl = prod.Layout;
            int k = pl.ProcIndex(il.Number[row]);
            if (k < 0)
                return Fx6.Zero;
            Fx6 sum = Fx6.Zero;
            for (int d = 0; d < pl.Designs; d++)
            {
                if (prod.Stage(actor, d) == DesignStage.Series && prod.PrcSet(actor, d).AsSpan().IndexOf(il.Number[row]) >= 0)
                    sum += pl.RRef * pl.ProcN[k] * pl.ProcLabour[k];
            }
            return sum;
        };
        FieldInfo f = typeof(IndustrySystem).GetField("_rule", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("IndustrySystem._rule not found: the HYP-0004 sketch hook needs updating.");
        f.SetValue(ind, new IntroCultureRule(variant, vc));
    }

    public static IntroVariant Parse(string v) => v switch
    {
        "CB" => IntroVariant.Cb,
        "VEXP" => IntroVariant.VExp,
        "VEXPC" => IntroVariant.VExpC,
        "LAB" => IntroVariant.Lab,
        _ => throw new ArgumentException($"CULTURE_VARIANT '{v}' is not CB, VEXP, VEXPC or LAB."),
    };

    // ---- variant hooks

    /// <summary><c>v</c> that growth reads instead of <paramref name="v"/> (before the licence factor).</summary>
    private Fx6 GrowV(ICultureSnapshot s, int site, int r, Fx6 v, long month)
    {
        Fx6 x = variant switch
        {
            IntroVariant.VExp => s.PlannedVolume(site, r, month),
            IntroVariant.VExpC => seriesNominal!(site, r, month),
            _ => Fx6.Zero,
        };
        return x > v ? x : v;
    }

    private Fx6 DeltaL(ICultureSnapshot s, int site, int r, long month) =>
        variant == IntroVariant.Lab && s.Planned(site, r, month) ? Fx6.Zero : s.Params.DeltaL;

    // ---- the rule (copy of CbCultureRule with the hooks)

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
            if (Max0(l.Req[processRow][d] + GapOffset - s.H(site, c, d)) == Fx6.Zero)
                return new CultureLimiter(LimiterKind.None, number);
            int lead = Lead(s, site, c, d, month);
            if (lead >= 0)
                return Limiter(s, site, lead, month);
            LimiterKind l0 = Limiter0(s, site, processRow, d, month);
            return new CultureLimiter(l0 == LimiterKind.Effort ? LimiterKind.LabCeiling : l0, number);
        }
        LimiterKind? first = FirstFour(s, site, processRow, d, month);
        if (first is not null)
            return new CultureLimiter(first.Value, number);
        Fx6 kStar = KStar(s, site, processRow, d, month);
        bool volume = GrowV(s, site, processRow, s.V(site, processRow), month) > Fx6.Zero
            && s.E(site, processRow) >= EUseful(s, site, processRow, month) && kStar < Fx6.One;
        return new CultureLimiter(volume ? LimiterKind.Volume : LimiterKind.Effort, number);
    }

    public Fx6 DHForecast(ICultureSnapshot s, int site, int processRow, int dim, Fx6 e, Fx6 v, long month) =>
        CellStep(s, site, s.Layout.ClusterIndex[processRow], dim, month, processRow, e, v).DHPlusCapped;

    private CultureStep CellStep(ICultureSnapshot s, int site, int c, int d, long month, int overrideRow, Fx6 overrideE, Fx6 overrideV)
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
                continue;
            Fx6 vt = TildeV(s, site, r, v, month);
            Fx6 vg = TildeV(s, site, r, GrowV(s, site, r, v, month), month);
            CbParts2 parts = Parts(s, site, r, d, vg, e, month);
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
        return new CultureStep(Fx6.Zero, drive, catchUp, maint, spillSum, hFloor, sat, alphaEff, dPlus, capped, dMinus, h + capped - dMinus);
    }

    private static Fx6 TildeV(ICultureSnapshot s, int site, int r, Fx6 v, long month) =>
        (Fx6.One + s.Params.KTr * (s.Licensed(site, r, month) ? Fx6.One : Fx6.Zero)) * v;

    private readonly record struct CbParts2(Fx6 Cp, Fx6 Gap, Fx6 GapL, Fx6 T, Fx6 THat);

    private CbParts2 Parts(ICultureSnapshot s, int site, int r, int d, Fx6 vTilde, Fx6 e, long month)
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
        Fx6 gapL = Max0(rd - DeltaL(s, site, r, month) - h);
        Fx6 t = Max(eV * gap, eL * gapL);
        Fx6 tHat = Max(cp * gap, p.Ell * gapL);
        return new CbParts2(cp, gap, gapL, t, tHat);
    }

    private static bool Eligible(ICultureSnapshot s, int site, int r, long month) =>
        s.Knows(site, r, month) && (s.V(site, r) > Fx6.Zero || s.Planned(site, r, month));

    private CbParts2 FullEffort(ICultureSnapshot s, int site, int r, int d, long month) =>
        Parts(s, site, r, d, TildeV(s, site, r, GrowV(s, site, r, s.V(site, r), month), month), Fx6.Zero, month);

    private LimiterKind? FirstFour(ICultureSnapshot s, int site, int r, int d, long month)
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
        if (GrowV(s, site, r, s.V(site, r), month) == Fx6.Zero && (p.Ell == Fx6.Zero || h >= rd - DeltaL(s, site, r, month)))
            return LimiterKind.LabCeiling;
        return null;
    }

    private LimiterKind Limiter0(ICultureSnapshot s, int site, int r, int d, long month)
    {
        LimiterKind? first = FirstFour(s, site, r, d, month);
        if (first is not null)
            return first.Value;
        return FullEffort(s, site, r, d, month).THat > Fx6.Zero ? LimiterKind.Effort : LimiterKind.Volume;
    }

    private int Lead(ICultureSnapshot s, int site, int c, int d, long month)
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

    private bool LeadsAny(ICultureSnapshot s, int site, int r, long month)
    {
        int c = s.Layout.ClusterIndex[r];
        for (int d = 0; d < Dims3.Count; d++)
        {
            if (s.Layout.Req[r][d] > Fx6.Zero && Lead(s, site, c, d, month) == r)
                return true;
        }
        return false;
    }

    private int DStar(ICultureSnapshot s, int site, int r, long month)
    {
        int c = s.Layout.ClusterIndex[r];
        int best = Pick(s, site, r, c, month, ledOnly: true);
        return best >= 0 ? best : Pick(s, site, r, c, month, ledOnly: false);
    }

    private int Pick(ICultureSnapshot s, int site, int r, int c, long month, bool ledOnly)
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

    private Fx6 KStar(ICultureSnapshot s, int site, int r, int d, long month)
    {
        CbParts2 f = FullEffort(s, site, r, d, month);
        return s.Params.Ell * f.GapL > f.Cp * f.Gap ? s.Params.Ell : f.Cp;
    }

    private static Fx6 Max0(Fx6 x) => x > Fx6.Zero ? x : Fx6.Zero;

    private static Fx6 Max(Fx6 a, Fx6 b) => a > b ? a : b;

    private static Fx6 Min(Fx6 a, Fx6 b) => a < b ? a : b;
}
