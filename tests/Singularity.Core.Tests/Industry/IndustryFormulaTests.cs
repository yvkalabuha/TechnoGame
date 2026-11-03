using System.Text.Json;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Tests.Knowledge;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Industry;

/// <summary>Capability cases of lab/reference/jet_slice/out/formulas.json, exact (reference D1; contract §4.1, §4.2, §4.6).</summary>
public class IndustryFormulaTests
{
    private static JsonElement F => JetReference.Formulas;

    private static readonly string[] DimNames = ["precision", "purity", "quality_control"];

    private static Fx6 R(JsonElement e, string name) => JetReference.Raw(e.GetProperty(name));

    [Fact]
    public void CultureH_MatchesReference()
    {
        int cases = 0;
        foreach (JsonElement c in F.GetProperty("culture_H").EnumerateArray())
        {
            JsonElement i = c.GetProperty("input"), o = c.GetProperty("output");
            var p = new IndustryParams
            {
                Alpha = R(i, "alpha"), KQp = R(i, "k_qp"), ActNorm = R(i, "act_norm"), KCatch = R(i, "k_catch"),
                Sigma = R(i, "sigma"), RhoMax = R(i, "rho_max"), DeltaH = R(i, "delta_H"), PhiFloor = R(i, "phi_floor"),
            };
            CultureProcess[] procs = [.. i.GetProperty("procs").EnumerateArray()
                .Select(x => new CultureProcess(x.GetProperty("prc_id").GetInt32(), R(x, "v"), R(x, "r_d"), x.GetProperty("world_lead").GetBoolean()))
                .OrderBy(x => x.ProcessNumber)];
            CultureSpill[] spill = [.. i.GetProperty("spill_in").EnumerateArray().Select(x => new CultureSpill(R(x, "R"), R(x, "H_other")))];
            var cell = new CultureCell(R(i, "H"), R(i, "Hcap"), R(i, "act"), R(i, "w"),
                i.GetProperty("qp_active").GetBoolean() && i.GetProperty("d_is_qc").GetBoolean());
            CultureStep s = IndustryMath.Culture(cell, procs, spill, p);
            string n = c.GetProperty("name").GetString()!;
            Assert.True(o.GetProperty("V_c").GetInt64() == s.VC.Raw, n);
            Assert.Equal(o.GetProperty("drive").GetInt64(), s.Drive.Raw);
            Assert.Equal(o.GetProperty("catch").GetInt64(), s.Catch.Raw);
            Assert.Equal(o.GetProperty("maint").GetInt64(), s.Maint.Raw);
            Assert.Equal(o.GetProperty("spill").GetInt64(), s.Spill.Raw);
            Assert.Equal(o.GetProperty("H_floor").GetInt64(), s.HFloor.Raw);
            Assert.Equal(o.GetProperty("sat").GetInt64(), s.Sat.Raw);
            Assert.Equal(o.GetProperty("alpha_eff").GetInt64(), s.AlphaEff.Raw);
            Assert.Equal(o.GetProperty("dH_plus").GetInt64(), s.DHPlus.Raw);
            Assert.Equal(o.GetProperty("dH_plus_capped").GetInt64(), s.DHPlusCapped.Raw);
            Assert.Equal(o.GetProperty("dH_minus").GetInt64(), s.DHMinus.Raw);
            Assert.Equal(o.GetProperty("H_new").GetInt64(), s.HNew.Raw);
            // s_p is not kept in the result: it is the separate division v_p / V_c (contract §4.0 p. 3).
            foreach (JsonElement sp in o.GetProperty("s_p").EnumerateArray())
            {
                CultureProcess proc = procs.Single(x => x.ProcessNumber == sp.GetProperty("prc_id").GetInt32());
                Assert.Equal(sp.GetProperty("s").GetInt64(), (proc.V / s.VC).Raw);
            }
            cases++;
        }
        Assert.Equal(11, cases);
    }

    [Fact]
    public void ProductionProfile_MatchesReference()
    {
        int cases = 0;
        foreach (JsonElement c in F.GetProperty("production_profile").EnumerateArray())
        {
            JsonElement i = c.GetProperty("input"), o = c.GetProperty("output");
            Fx6 eps = R(i, "eps");
            var items = new List<ProfileItem>();
            foreach (JsonElement p in i.GetProperty("procs").EnumerateArray())
            {
                JsonElement lic = p.GetProperty("l_lic");
                ProcessYield y = IndustryMath.Yield(Dims(p.GetProperty("req")), Dims(p.GetProperty("H")), Dims(p.GetProperty("M")),
                    R(p, "X"), R(p, "Xh"), lic.ValueKind == JsonValueKind.Null ? Fx6.Zero : JetReference.Raw(lic), R(p, "lam0"));
                items.Add(new ProfileItem(p.GetProperty("prc_id").GetInt32(), y, p.GetProperty("known").GetBoolean(), R(p, "n_p"), R(p, "base_cost")));
            }
            foreach (JsonElement row in o.GetProperty("per_process").EnumerateArray())
            {
                ProcessYield y = items.Single(x => x.ProcessNumber == row.GetProperty("prc_id").GetInt32()).Yield;
                Assert.Equal(row.GetProperty("Xh_eff").GetInt64(), y.XHalfEff.Raw);
                Assert.Equal(row.GetProperty("ramp").GetInt64(), y.Ramp.Raw);
                Assert.Equal(row.GetProperty("yield").GetInt64(), y.Yield.Raw);
                Assert.Equal(row.GetProperty("q_p").GetInt64(), y.QP.Raw);
                Assert.Equal(row.GetProperty("yc").GetInt64(), (y.Yield > eps ? y.Yield : eps).Raw);
                var dims = row.GetProperty("dims").EnumerateArray().ToList();
                Assert.Equal(dims.Count, System.Numerics.BitOperations.PopCount((uint)y.ActiveMask));
                foreach (JsonElement d in dims)
                {
                    int k = Array.IndexOf(DimNames, d.GetProperty("dim").GetString());
                    Assert.True(y.IsActive(k));
                    Assert.Equal(d.GetProperty("c_d").GetInt64(), y.C[k].Raw);
                    Assert.Equal(d.GetProperty("g").GetInt64(), y.G[k].Raw);
                    Assert.Equal(d.GetProperty("src").GetString(), y.Source(k).ToString());
                }
            }
            ProductionProfile pr = IndustryMath.Profile(items.ToArray(), eps);
            Assert.Equal(o.GetProperty("admitted").GetBoolean(), pr.Admitted);
            Assert.Equal(o.GetProperty("unit_cost_mult").GetInt64(), pr.UnitCostMult.Raw);
            Assert.Equal(o.GetProperty("q").GetInt64(), pr.Q.Raw);
            JsonElement bn = o.GetProperty("bottleneck");
            Assert.Equal(bn.GetProperty("prc").GetInt32(), pr.BottleneckProcess);
            JsonElement dim = bn.GetProperty("dim");
            if (dim.ValueKind == JsonValueKind.Null)
            {
                Assert.Null(pr.BottleneckDim);
                Assert.Equal(BottleneckSource.Ramp, pr.BottleneckSource);
            }
            else
            {
                Assert.Equal((Dimension)Array.IndexOf(DimNames, dim.GetString()), pr.BottleneckDim);
                Assert.Equal(bn.GetProperty("src").GetString(), pr.BottleneckSource.ToString());
            }
            cases++;
        }
        Assert.Equal(8, cases);
    }

    [Fact]
    public void WorkforceSplit_MatchesReference()
    {
        int cases = 0;
        foreach (JsonElement c in F.GetProperty("workforce_split").EnumerateArray())
        {
            // [site, cat, requester_id, n]; one (site, cat) per case, sorted by requester_id as the merge does.
            var reqs = c.GetProperty("requests").EnumerateArray()
                .Select(r => (Id: r[2].GetInt32(), N: r[3].GetInt64())).OrderBy(r => r.Id).ToArray();
            var granted = new long[reqs.Length];
            IndustryMath.SplitWorkforce(c.GetProperty("avail").GetInt64(), reqs.Select(r => r.N).ToArray(), granted);
            var expected = c.GetProperty("output").EnumerateArray()
                .Select(o => (o.GetProperty("requester_id").GetInt32(), o.GetProperty("n").GetInt64(), o.GetProperty("granted").GetInt64())).ToList();
            Assert.Equal(expected, reqs.Select((r, k) => (r.Id, r.N, granted[k])).ToList());
            cases++;
        }
        Assert.Equal(3, cases);
    }

    private static Dims3 Dims(JsonElement e)
    {
        Fx6 D(string name) => e.TryGetProperty(name, out JsonElement v) && v.ValueKind != JsonValueKind.Null ? JetReference.Raw(v) : Fx6.Zero;
        return new Dims3(D("precision"), D("purity"), D("quality_control"));
    }
}
