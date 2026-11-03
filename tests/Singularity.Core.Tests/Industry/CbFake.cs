using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Industry;

/// <summary>A hand-set snapshot for the culture rule C-B and the effort allocation (no tick system involved).</summary>
internal sealed class CbFake : ICultureSnapshot
{
    private readonly int _c, _n;
    public readonly Fx6[] Hs, Ms, Vs, Es, Vexp;
    public readonly bool[] Known, PlannedF, Lic, Lead;
    public readonly Fx6[] HcapByDim = [D(5), D(5), D(5)];
    public bool Qp;
    public Fx6 ActValue = Fx6.One, WValue = Fx6.One;

    public CbFake(IndustryLayout layout, IndustryParams? p = null)
    {
        Layout = layout;
        Params = p ?? new IndustryParams();
        _c = layout.Clusters.Length;
        _n = layout.ProcessCount;
        int sites = layout.SiteCount;
        Hs = new Fx6[sites * _c * 3];
        Ms = new Fx6[Hs.Length];
        Vs = new Fx6[sites * _n];
        Es = new Fx6[Vs.Length];
        Vexp = new Fx6[Vs.Length];
        Known = new bool[Vs.Length];
        PlannedF = new bool[Vs.Length];
        Lic = new bool[Vs.Length];
        Lead = new bool[Vs.Length];
        Array.Fill(Known, true);
        for (int i = 0; i < Hs.Length; i++)
        {
            Hs[i] = D(2);
            Ms[i] = D(3);
        }
    }

    public static Fx6 D(long units) => Fx6.FromInt(units);

    public static Fx6 F(long raw) => Fx6.FromRaw(raw);

    public IndustryLayout Layout { get; }

    public IndustryParams Params { get; }

    public int P(int number) => Layout.RowOf(number);

    public int Idx(int site, int c, int d) => (site * _c + c) * 3 + d;

    public void SetH(int site, int c, int d, Fx6 h) => Hs[Idx(site, c, d)] = h;

    public Fx6 H(int site, int cluster, int dim) => Hs[Idx(site, cluster, dim)];

    public Fx6 M(int site, int cluster, int dim) => Ms[Idx(site, cluster, dim)];

    public Fx6 Hcap(int site, int dim, long month) => HcapByDim[dim];

    public Fx6 Act(int site, int cluster) => ActValue;

    public Fx6 W(int site, int cluster, long month) => WValue;

    public bool QualityProgramme(int site, long month) => Qp;

    public Fx6 V(int site, int processRow) => Vs[site * _n + processRow];

    public Fx6 E(int site, int processRow) => Es[site * _n + processRow];

    public bool Licensed(int site, int processRow, long month) => Lic[site * _n + processRow];

    public bool WorldLead(int site, int processRow) => Lead[site * _n + processRow];

    public bool Knows(int site, int processRow, long month) => Known[site * _n + processRow];

    public bool Planned(int site, int processRow, long month) => PlannedF[site * _n + processRow];

    public Fx6 PlannedVolume(int site, int processRow, long month) => Vexp[site * _n + processRow];

    /// <summary>Layout of one actor with the given sites; processes (number, cluster, tier, req precision/purity/qc, labour).</summary>
    public static IndustryLayout MakeLayout(int[] siteOwner, params (int Number, Cluster Cluster, Dims3 Req)[] procs)
    {
        Cluster[] clusters = [.. procs.Select(x => x.Cluster).Distinct().Order()];
        return IndustryLayout.Create(clusters,
            [.. procs.Select(x => new IndustryProcessSpec(x.Number, x.Cluster, 0, 3, x.Req, Fx6.One))],
            siteOwner, 2, 1, 4);
    }
}
