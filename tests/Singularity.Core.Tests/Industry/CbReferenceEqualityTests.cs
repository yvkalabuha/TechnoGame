using System.Globalization;
using System.Text.Json;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Tests.Knowledge;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Industry;

/// <summary>
/// Bit-for-bit equality of the C-B rule and the effort allocation with lab/reference/jet_slice/out
/// (<c>formulas.json</c>: culture_cb, cb_interface, effort_alloc; <c>trajectory_cb.json</c>), TASK-0020 acceptance.
/// </summary>
public class CbReferenceEqualityTests
{
    private static readonly string[] DimNames = ["precision", "purity", "quality_control"];

    private static JsonElement F => JetReference.Formulas;

    private static Fx6 R(JsonElement e, string name) => Fx6.FromRaw(e.GetProperty(name).GetInt64());

    private static Fx6 Opt(JsonElement e) => e.ValueKind == JsonValueKind.Null ? Fx6.Zero : Fx6.FromRaw(e.GetInt64());

    private record Proc(int Number, Cluster Cluster, Dims3 Req, Fx6 Labour, Fx6 V, Fx6 E, bool Known, bool Planned, bool Lic, bool WorldLead);

    private static Cluster ClusterOf(string name) => Enum.Parse<Cluster>(name);

    private static Dims3 ReqOf(JsonElement req) => new(Opt(req.GetProperty("precision")), Opt(req.GetProperty("purity")), Opt(req.GetProperty("quality_control")));

    private static IndustryParams ParamsOf(JsonElement i)
    {
        JsonElement cb = i.GetProperty("cb");
        var p = new IndustryParams
        {
            EFull = R(cb, "E_full"), DeltaL = R(cb, "dL"), Ell = R(cb, "ell"), KTr = R(cb, "k_tr"),
        };
        // r_cap and r_keep derive from r_ref: the reference must agree.
        Assert.Equal(cb.GetProperty("r_cap").GetInt64(), p.RCap.Raw);
        Assert.Equal(cb.GetProperty("r_keep").GetInt64(), p.RKeep.Raw);
        return p;
    }

    private static CbFake Snapshot(Cluster[] clusters, Proc[] procs, IndustryParams p)
    {
        IndustryLayout l = IndustryLayout.Create(clusters,
            [.. procs.Select(x => new IndustryProcessSpec(x.Number, x.Cluster, 0, 3, x.Req, x.Labour))], [0], 2, 1, 4);
        var s = new CbFake(l, p);
        foreach (Proc x in procs)
        {
            int r = s.P(x.Number);
            s.Vs[r] = x.V;
            s.Es[r] = x.E;
            s.Known[r] = x.Known;
            s.PlannedF[r] = x.Planned;
            s.Lic[r] = x.Lic;
            s.Lead[r] = x.WorldLead;
        }
        return s;
    }

    [Fact]
    public void CultureCb_CellStep_MatchesReference()
    {
        int cases = 0;
        foreach (JsonElement c in F.GetProperty("culture_cb").EnumerateArray())
        {
            JsonElement i = c.GetProperty("input"), o = c.GetProperty("output");
            string n = c.GetProperty("name").GetString()!;
            IndustryParams p0 = ParamsOf(i);
            var spill = i.GetProperty("spill_in").EnumerateArray().ToList();
            var p = p0 with
            {
                Alpha = R(i, "alpha"), KQp = R(i, "k_qp"), ActNorm = R(i, "act_norm"), KCatch = R(i, "k_catch"), Sigma = R(i, "sigma"),
                RhoMax = R(i, "rho_max"), DeltaH = R(i, "delta_H"), PhiFloor = R(i, "phi_floor"),
                SpillPairs = [.. spill.Select(x => (Cluster.MET, ClusterOf(x.GetProperty("cluster").GetString()!), R(x, "R")))],
            };
            int dim = i.GetProperty("d_is_qc").GetBoolean() ? 2 : 0;
            Proc[] procs = [.. i.GetProperty("procs").EnumerateArray().Select(x =>
            {
                var req = new Fx6[3];
                req[dim] = R(x, "r_d");
                return new Proc(x.GetProperty("prc_id").GetInt32(), Cluster.MET, new Dims3(req[0], req[1], req[2]), R(x, "labour"),
                    R(x, "v"), R(x, "E"), true, false, x.GetProperty("lic").GetBoolean(), x.GetProperty("world_lead").GetBoolean());
            }).OrderBy(x => x.Number)];
            Cluster[] clusters = [.. new[] { Cluster.MET }.Concat(spill.Select(x => ClusterOf(x.GetProperty("cluster").GetString()!))).Distinct().Order()];
            CbFake s = Snapshot(clusters, procs, p);
            s.Qp = i.GetProperty("qp_active").GetBoolean();
            s.ActValue = R(i, "act");
            s.WValue = R(i, "w");
            Array.Fill(s.HcapByDim, R(i, "Hcap"));
            int met = Array.IndexOf(s.Layout.Clusters.ToArray(), Cluster.MET);
            s.SetH(0, met, dim, R(i, "H"));
            foreach (JsonElement x in spill)
                s.SetH(0, Array.IndexOf(s.Layout.Clusters.ToArray(), ClusterOf(x.GetProperty("cluster").GetString()!)), dim, R(x, "H_other"));

            CultureStep step = new CbCultureRule().Step(s, 0, met, dim, 0);
            void Eq(string key, Fx6 got) => Assert.True(o.GetProperty(key).GetInt64() == got.Raw, $"{n}: {key} ref {o.GetProperty(key).GetInt64()} got {got.Raw}");
            Eq("drive", step.Drive);
            Eq("catch", step.Catch);
            Eq("maint", step.Maint);
            Eq("spill", step.Spill);
            Eq("H_floor", step.HFloor);
            Eq("sat", step.Sat);
            Eq("alpha_eff", step.AlphaEff);
            Eq("dH_plus", step.DHPlus);
            Eq("dH_plus_capped", step.DHPlusCapped);
            Eq("dH_minus", step.DHMinus);
            Eq("H_new", step.HNew);
            cases++;
        }
        Assert.Equal(F.GetProperty("culture_cb").GetArrayLength(), cases);
    }

    private static CbFake SiteSnapshot(JsonElement site, int[]? numbers = null)
    {
        IndustryParams p = ParamsOf(site);
        Proc[] procs = [.. site.GetProperty("procs").EnumerateArray().Select(x => new Proc(x.GetProperty("prc_id").GetInt32(),
            ClusterOf(x.GetProperty("cluster").GetString()!), ReqOf(x.GetProperty("req")), R(x, "labour"), R(x, "v"), R(x, "E"),
            x.GetProperty("known").GetBoolean(), x.GetProperty("planned").GetBoolean(), x.GetProperty("lic").GetBoolean(), false))
            .OrderBy(x => x.Number)];
        Cluster[] clusters = [.. site.GetProperty("H").EnumerateObject().Select(x => ClusterOf(x.Name.Split('.')[0])).Distinct().Order()];
        CbFake s = Snapshot(clusters, procs, p);
        foreach (JsonProperty h in site.GetProperty("H").EnumerateObject())
        {
            string[] k = h.Name.Split('.');
            int c = Array.IndexOf(clusters, ClusterOf(k[0])), d = Array.IndexOf(DimNames, k[1]);
            s.SetH(0, c, d, Fx6.FromRaw(h.Value.GetInt64()));
            s.Ms[s.Idx(0, c, d)] = Fx6.FromRaw(site.GetProperty("M").GetProperty(h.Name).GetInt64());
        }
        for (int d = 0; d < 3; d++)
            s.HcapByDim[d] = Fx6.FromRaw(site.GetProperty("Hcap").GetProperty(DimNames[d]).GetInt64());
        return s;
    }

    private static void AssertKind(string expected, LimiterKind got, string where) =>
        Assert.True(string.Equals(expected.Replace("_", ""), got.ToString(), StringComparison.OrdinalIgnoreCase), $"{where}: limiter ref {expected} got {got}");

    [Fact]
    public void CbInterface_LeadLimiterEUseful_MatchReference()
    {
        var rule = new CbCultureRule();
        int cases = 0;
        foreach (JsonElement c in F.GetProperty("cb_interface").EnumerateArray())
        {
            string n = c.GetProperty("name").GetString()!;
            JsonElement o = c.GetProperty("output");
            CbFake s = SiteSnapshot(c.GetProperty("input"));
            IndustryLayout l = s.Layout;
            foreach (JsonProperty lead in o.GetProperty("lead").EnumerateObject())
            {
                string[] k = lead.Name.Split('.');
                int cl = Array.IndexOf(l.Clusters.ToArray(), ClusterOf(k[0])), d = Array.IndexOf(DimNames, k[1]);
                int row = CbCultureRule.Lead(s, 0, cl, d, 0);
                int number = row < 0 ? -1 : l.Number[row];
                Assert.True((lead.Value.ValueKind == JsonValueKind.Null ? -1 : lead.Value.GetInt32()) == number, $"{n}: lead {lead.Name} got {number}");
            }
            foreach (JsonElement q in o.GetProperty("procs").EnumerateArray())
            {
                int r = s.P(q.GetProperty("prc_id").GetInt32());
                string w = $"{n} prc {l.Number[r]}";
                Assert.True(q.GetProperty("eligible").GetBoolean() == CbCultureRule.Eligible(s, 0, r, 0), $"{w}: eligible");
                int dStar = CbCultureRule.DStar(s, 0, r, 0);
                string? want = q.GetProperty("d_star").GetString();
                Assert.True(want == (dStar < 0 ? null : DimNames[dStar]), $"{w}: d_star");
                if (dStar >= 0)
                    Assert.True(q.GetProperty("k_star").GetInt64() == CbCultureRule.KStar(s, 0, r, dStar, 0).Raw, $"{w}: k_star");
                Assert.True(q.GetProperty("E_useful").GetInt64() == rule.EUseful(s, 0, r, 0).Raw, $"{w}: E_useful ref {q.GetProperty("E_useful").GetInt64()}");
                CultureLimiter lim = rule.Limiter(s, 0, r, 0);
                AssertKind(q.GetProperty("limiter").GetString()!, lim.Value, w);
                Assert.True(q.GetProperty("subject_prc").GetInt32() == lim.SubjectProcess, $"{w}: subject_prc got {lim.SubjectProcess}");
                foreach (JsonProperty l0 in q.GetProperty("limiter0").EnumerateObject())
                    AssertKind(l0.Value.GetString()!, CbCultureRule.Limiter0(s, 0, r, Array.IndexOf(DimNames, l0.Name), 0), $"{w} limiter0 {l0.Name}");
            }
            cases++;
        }
        Assert.Equal(F.GetProperty("cb_interface").GetArrayLength(), cases);
    }

    [Fact]
    public void EffortAlloc_MatchesReference()
    {
        var rule = new CbCultureRule();
        int cases = 0;
        foreach (JsonElement c in F.GetProperty("effort_alloc").EnumerateArray())
        {
            string n = c.GetProperty("name").GetString()!;
            JsonElement i = c.GetProperty("input"), o = c.GetProperty("output");
            Assert.Equal(i.GetProperty("r_ref").GetInt64(), new IndustryParams().RRef.Raw);
            CbFake s = SiteSnapshot(i.GetProperty("site"));
            foreach (JsonProperty v in i.GetProperty("v_exp").EnumerateObject())
                s.Vexp[s.P(int.Parse(v.Name, CultureInfo.InvariantCulture))] = Fx6.FromRaw(v.Value.GetInt64());
            int[] focus = [.. i.GetProperty("focus").EnumerateArray().Select(x => x.GetInt32())];
            EffortAllocation a = EffortAllocator.Allocate(rule, s, 0, 0, R(i, "D_a"), focus);
            foreach (JsonProperty e in o.GetProperty("E").EnumerateObject())
            {
                int r = s.P(int.Parse(e.Name, CultureInfo.InvariantCulture));
                Assert.True(e.Value.GetInt64() == a.E[r].Raw, $"{n}: E[{e.Name}] ref {e.Value.GetInt64()} got {a.E[r].Raw}");
            }
            foreach (JsonProperty e in o.GetProperty("E_useful").EnumerateObject())
                Assert.True(e.Value.GetInt64() == rule.EUseful(s, 0, s.P(int.Parse(e.Name, CultureInfo.InvariantCulture)), 0).Raw, $"{n}: E_useful[{e.Name}]");
            Assert.True(o.GetProperty("rest_a").GetInt64() == a.Rest.Raw, $"{n}: rest_a ref {o.GetProperty("rest_a").GetInt64()} got {a.Rest.Raw}");
            Assert.True(o.GetProperty("passes").GetArrayLength() == a.Passes, $"{n}: passes");
            cases++;
        }
        Assert.Equal(F.GetProperty("effort_alloc").GetArrayLength(), cases);
    }

    [Fact]
    public void TrajectoryCb_MatchesReference_EveryMonth()
    {
        var run = new JetIndustryRun(cb: true);
        IndustryLayout l = run.Layout;
        IndustrySystem ind = run.Industry;
        for (long t = 0; t < JetReference.RowCount; t++)
        {
            JsonElement row = JetReference.RowCb(t), ef = row.GetProperty("effort");
            // E_useful and limiter are pure functions of the snapshot of month t−1: read them before month t runs.
            foreach (JsonProperty q in ef.GetProperty("E_useful").EnumerateObject())
                Assert.True(q.Value.GetInt64() == ind.EUseful(0, l.RowOf(int.Parse(q.Name, CultureInfo.InvariantCulture)), t).Raw, $"t={t} E_useful {q.Name}");
            foreach (JsonProperty q in ef.GetProperty("limiter").EnumerateObject())
            {
                CultureLimiter lim = ind.Limiter(0, l.RowOf(int.Parse(q.Name, CultureInfo.InvariantCulture)), t);
                AssertKind(q.Value[0].GetString()!, lim.Value, $"t={t} prc {q.Name}");
                Assert.True(q.Value[1].GetInt32() == lim.SubjectProcess, $"t={t} prc {q.Name} subject got {lim.SubjectProcess}");
            }
            run.RunThroughMonth(t);
            AssertMonth(run, row, t);
        }
    }

    private static void AssertMonth(JetIndustryRun run, JsonElement row, long t)
    {
        IndustrySystem ind = run.Industry;
        IndustryLayout l = run.Layout;
        JsonElement after = row.GetProperty("state_after"), ef = row.GetProperty("effort");
        for (int c = 0; c < l.Clusters.Length; c++)
        {
            for (int d = 0; d < 3; d++)
            {
                string key = $"{l.Clusters[c]}.{DimNames[d]}";
                Assert.True(after.GetProperty("culture_H").GetProperty(key).GetInt64() == ind.CultureH(0, c, (Dimension)d).Raw, $"t={t} H {key}");
                Assert.True(after.GetProperty("tooling_M").GetProperty(key).GetInt64() == ind.ToolingM(0, c, (Dimension)d).Raw, $"t={t} M {key}");
                JsonElement det = row.GetProperty("culture_detail").GetProperty(key);
                CultureStep step = ind.LastCulture(0, c, (Dimension)d);
                Assert.True(det.GetProperty("drive").GetInt64() == step.Drive.Raw, $"t={t} drive {key}");
                Assert.True(det.GetProperty("maint").GetInt64() == step.Maint.Raw, $"t={t} maint {key}");
                Assert.True(det.GetProperty("dH_plus").GetInt64() == step.DHPlus.Raw, $"t={t} dH+ {key}");
                Assert.True(det.GetProperty("dH_minus").GetInt64() == step.DHMinus.Raw, $"t={t} dH- {key}");
            }
        }
        for (int r = 0; r < l.ProcessCount; r++)
        {
            string p = l.Number[r].ToString(CultureInfo.InvariantCulture);
            Assert.True(after.GetProperty("proc_X").GetProperty(p).GetInt64() == ind.ProcX(0, r).Raw, $"t={t} X {p}");
            Assert.True(after.GetProperty("proc_v").GetProperty(p).GetInt64() == ind.ProcV(0, r).Raw, $"t={t} v {p}");
            Assert.True(after.GetProperty("proc_yield").GetProperty(p).GetInt64() == ind.ProcYield(0, r).Raw, $"t={t} y {p}");
            long expectedE = after.GetProperty("proc_E").TryGetProperty(p, out JsonElement e) ? e.GetInt64() : 0;
            Assert.True(expectedE == ind.ProcE(0, r).Raw, $"t={t} proc_E {p} ref {expectedE} got {ind.ProcE(0, r).Raw}");
        }
        Assert.True(ef.GetProperty("rest_a").GetInt64() == ind.LastRest(0).Raw, $"t={t} rest_a");
        var expected = row.GetProperty("merge_report_output").EnumerateArray()
            .Select(x => (x[0].GetInt32(), x[1].GetInt32(), x[2].GetInt64(), x[3].GetInt64())).ToList();
        var actual = ind.LastReports.Select(x => (x.Site, x.ProcessNumber, x.Units.Raw, x.LabourHours.Raw)).ToList();
        Assert.Equal(expected, actual);
    }
}
