// EXP-0003 (T6e) runner. Lab observer of the jet slice: reads the world after each month, never writes it.
// Built and run by lab/runners/t6e/run_t6e.sh inside an archive copy of the repo (lab/results/t6e/repo), dropped into
// tests/Singularity.Session.Tests: that assembly is the only one with read access to SliceWorld (Session has no read views yet,
// ADR-0014 п. 5; see EXP-0003 «Отклонения»). Nothing here is part of the product or of the committed test suite.
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;
using Xunit.Abstractions;

namespace Singularity.Session.Tests;

public class T6eHarness(ITestOutputHelper output)
{
    private static readonly Func<ICultureSnapshot, int, int, long, int> DStar =
        typeof(CbCultureRule).GetMethod("DStar", BindingFlags.NonPublic | BindingFlags.Static)!
            .CreateDelegate<Func<ICultureSnapshot, int, int, long, int>>();

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private sealed record Job(string Arm, string Config, int? Cell, ulong Seed, int Threads);

    [Fact]
    public void Run()
    {
        string? manifestPath = Environment.GetEnvironmentVariable("T6E_MANIFEST");
        if (manifestPath is null)
            return;
        string outRoot = Environment.GetEnvironmentVariable("T6E_OUT")!;
        string[] shard = (Environment.GetEnvironmentVariable("T6E_SHARD") ?? "0/1").Split('/');
        int si = int.Parse(shard[0], Inv), sn = int.Parse(shard[1], Inv);
        string? only = Environment.GetEnvironmentVariable("T6E_ONLY"); // "arm:config:seed:threads" for checks
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        JsonElement root = doc.RootElement;
        int months = root.GetProperty("months").GetInt32();
        int threads = root.GetProperty("threads").GetInt32();
        var arms = new Dictionary<string, ToolingTargetSpec>(StringComparer.Ordinal);
        foreach (JsonProperty a in root.GetProperty("arms").EnumerateObject())
            arms[a.Name] = SpecOf(a.Value);
        var jobs = new List<Job>();
        if (only is not null)
        {
            string[] p = only.Split(':');
            JsonElement cfg = root.GetProperty("configs").GetProperty(p[1]);
            jobs.Add(new Job(p[0], p[1], CellOf(cfg), ulong.Parse(p[2], Inv), int.Parse(p[3], Inv)));
        }
        else
        {
            foreach (JsonProperty c in root.GetProperty("configs").EnumerateObject())
                foreach (JsonElement s in c.Value.GetProperty("seeds").EnumerateArray())
                    foreach (string arm in arms.Keys.Order(StringComparer.Ordinal))
                        jobs.Add(new Job(arm, c.Name, CellOf(c.Value), s.GetUInt64(), threads));
        }
        for (int j = 0; j < jobs.Count; j++)
        {
            if (j % sn != si)
                continue;
            Job job = jobs[j];
            string dir = Path.Combine(outRoot, job.Config, job.Arm, $"seed{job.Seed}" + (only is null ? "" : $"_t{job.Threads}"));
            if (File.Exists(Path.Combine(dir, "run.json")))
                continue;
            Directory.CreateDirectory(dir);
            RunOne(job, arms[job.Arm], months, dir);
            output.WriteLine($"done {job}");
        }
    }

    private static int? CellOf(JsonElement cfg) =>
        cfg.GetProperty("profile_cell").ValueKind == JsonValueKind.Null ? null : cfg.GetProperty("profile_cell").GetInt32();

    private static Fx6 Num(JsonElement e, string name) => Fx6.FromRaw((long)Math.Round(e.GetProperty(name).GetDouble() * Fx6.Scale));

    private static ToolingTargetSpec SpecOf(JsonElement e)
    {
        var spec = ToolingTargetSpec.Parse(e.GetProperty("variant").GetString()!);
        var d = new ToolingTargetSpec();
        spec = spec with
        {
            RhoMax = e.TryGetProperty("rho_max", out _) ? Num(e, "rho_max") : d.RhoMax,
            HLo = e.TryGetProperty("h_lo", out _) ? Num(e, "h_lo") : d.HLo,
            HHi = e.TryGetProperty("h_hi", out _) ? Num(e, "h_hi") : d.HHi,
            MLead = e.TryGetProperty("m_lead", out _) ? Num(e, "m_lead") : d.MLead,
            HHiControl = e.TryGetProperty("h_hi_control", out _) ? Num(e, "h_hi_control") : d.HHiControl,
        };
        spec.Validate();
        return spec;
    }

    private static void RunOne(Job job, ToolingTargetSpec target, int months, string dir)
    {
        var config = new SliceRunConfig { Seed = job.Seed, ProfileCell = job.Cell, ToolingTarget = target, Threads = job.Threads };
        SliceSession session = SliceSession.Create(SliceTestData.Data, config);
        SliceWorld w = session.World;
        IndustrySystem ind = w.Industry;
        IndustryLayout L = w.IndustryLayout;
        IIndustryInputs inputs = w.IndustryInputs;
        ICultureSnapshot cs = ind;
        ProductsSystem pr = w.Products;
        IndustryParams ip = w.IndustryParameters;
        var vinf = new ToolingTargetSpec { Variant = ToolingTarget.VInf };
        Fx6 rhoPool = Fx6.FromRaw(21_000);
        int nA = w.Scenario.ActorCount, nC = L.Clusters.Length, nP = L.ProcessCount, designs = w.ProductsLayout.Designs;
        string[] ids = [.. Enumerable.Range(0, nA).Select(a => w.Scenario.ActorId[a])];
        int r49 = L.RowOf(49);

        var cells = new StringBuilder("t,actor,c,d,H,M,Mcap,pool,p2,notional,part,goal,gotE,lab\n");
        var acts = new StringBuilder("t,actor,profile,rest,bcap,total,restRet,ta,notionalTotal,p2total,output,G,Gfront\n");
        var lines = new StringBuilder("t,actor,design,gen,rate,unitcost,minyield\n");
        var diag = new StringBuilder("t,actor,yield,vexp,lim,limsubj,MminusH_prec,MminusH_pur,MminusH_qc,H_prec,H_pur,H_qc,M_prec,M_pur,M_qc,E\n");
        long mismatchGain = 0, mismatchRest = 0, mismatchCap = 0;
        var diagPendingE = new List<(int a, int s, string line)>();

        var firstMastered = new int[nA, nP];
        var tFirst = new int[nA, nP];
        var t07 = new int[nA, nP];
        var masteredT0 = new bool[nA, nP];
        for (int a = 0; a < nA; a++)
        {
            for (int r = 0; r < nP; r++)
            {
                firstMastered[a, r] = tFirst[a, r] = t07[a, r] = -1;
                masteredT0[a, r] = pr.Mastered(a, L.Number[r], 0);
            }
        }

        var h0 = new long[L.SiteCount * nC * 3];
        for (int s = 0; s < L.SiteCount; s++)
            for (int c = 0; c < nC; c++)
                for (int d = 0; d < 3; d++)
                    h0[(s * nC + c) * 3 + d] = ind.CultureH(s, c, (Dimension)d).Raw;

        for (long t = 0; t < months; t++)
        {
            // ---- snapshot of month t (what the compute of month t reads)
            var plans = new ToolingPlanResult[nA];
            var refs = new ToolingPlanResult[nA];
            var restUsed = new Fx6[nA];
            var mBefore = new Fx6[L.SiteCount * nC * 3];
            var mCapS = new Fx6[L.SiteCount];
            var pre = new List<(int a, int s, int c, int d, Fx6 h, Fx6 m, bool pool, int lab)>();
            var dstar = new int[L.SiteCount, nP];
            for (int a = 0; a < nA; a++)
            {
                Fx6 rest = ind.RestActor(a, t);
                restUsed[a] = rest;
                Fx6 bCap = w.SharesOf(a).Capability * w.Policy.Budget(a);
                plans[a] = ToolingPlan.Compute(ind, inputs, w.PolicyParameters.MKeep, a, t, rest, bCap, target);
                refs[a] = ToolingPlan.Compute(ind, inputs, w.PolicyParameters.MKeep, a, t, rest, bCap, vinf);
            }
            for (int s = 0; s < L.SiteCount; s++)
            {
                int a = L.SiteOwner[s];
                int tier = 0;
                foreach (int r in L.Order)
                {
                    if (inputs.KnowsProcess(s, r, t) && ind.Yield(s, r, t).Yield > Fx6.Zero && L.Clusters[L.ClusterIndex[r]] == Cluster.MECH && L.Tier[r] > tier)
                        tier = L.Tier[r];
                }
                mCapS[s] = Fx6.FromInt(1 + (tier > 3 ? tier : 3));
                for (int r = 0; r < nP; r++)
                    dstar[s, r] = inputs.KnowsProcess(s, r, t) ? DStar(cs, s, r, t) : -1;
                for (int c = 0; c < nC; c++)
                {
                    for (int d = 0; d < 3; d++)
                    {
                        Fx6 h = ind.CultureH(s, c, (Dimension)d), m = ind.ToolingM(s, c, (Dimension)d);
                        mBefore[(s * nC + c) * 3 + d] = m;
                        bool pool = false, anyProc = false;
                        bool limT = false, limE = false;
                        for (int r = 0; r < nP; r++)
                        {
                            if (L.ClusterIndex[r] != c || L.Req[r][d] <= Fx6.Zero || !inputs.KnowsProcess(s, r, t))
                                continue;
                            anyProc = true;
                            bool eligible = ind.ProcV(s, r) > Fx6.Zero || inputs.Planned(s, r, t);
                            if (!eligible || !(L.Req[r][d] - h > Fx6.Zero))
                                continue;
                            if (mCapS[s] >= h + rhoPool)
                                pool = true;
                            if (dstar[s, r] == d)
                            {
                                LimiterKind lk = ind.Limiter(s, r, t).Value;
                                limT |= lk == LimiterKind.Tooling;
                                limE |= lk == LimiterKind.Effort;
                            }
                        }
                        if (!anyProc)
                            continue;
                        pre.Add((a, s, c, d, h, m, pool, limT ? 2 : limE ? 1 : 0));
                    }
                }
            }
            // diagnostic of 49 at the home site of every actor that knows it
            for (int a = 0; a < nA; a++)
            {
                int s = L.HomeSite(a);
                if (r49 < 0 || !inputs.KnowsProcess(s, r49, t))
                    continue;
                int c = L.ClusterIndex[r49];
                CultureLimiter lim = ind.Limiter(s, r49, t);
                var sb = new StringBuilder();
                sb.Append(Inv, $"{t},{ids[a]},{ind.Yield(s, r49, t).Yield.Raw},{inputs.PlannedVolume(s, r49, t).Raw},{lim.Value},{lim.SubjectProcess}");
                for (int d = 0; d < 3; d++)
                    sb.Append(Inv, $",{(L.Req[r49][d] > Fx6.Zero ? (ind.ToolingM(s, c, (Dimension)d) - ind.CultureH(s, c, (Dimension)d)).Raw.ToString(Inv) : "")}");
                for (int d = 0; d < 3; d++)
                    sb.Append(Inv, $",{ind.CultureH(s, c, (Dimension)d).Raw}");
                for (int d = 0; d < 3; d++)
                    sb.Append(Inv, $",{ind.ToolingM(s, c, (Dimension)d).Raw}");
                diagPendingE.Add((a, s, sb.ToString()));
            }

            session.StepThroughMonth(t);

            // ---- after month t
            foreach ((int a, int s, string line) in diagPendingE)
                diag.Append(line).Append(Inv, $",{ind.ProcE(s, r49).Raw}\n");
            diagPendingE.Clear();
            foreach (var x in pre)
            {
                int i = (x.s * nC + x.c) * 3 + x.d;
                ToolingPlanResult pl = plans[x.a];
                Fx6 applied = pl.Applied(x.s, x.c, (Dimension)x.d);
                ToolingStep step = ind.LastTooling(x.s, x.c, (Dimension)x.d);
                if (step.Gain != applied / step.CostM)
                    mismatchGain++;
                if (step.MCap != mCapS[x.s])
                    mismatchCap++;
                Fx6 p2 = pl.Invest(x.s, x.c, (Dimension)x.d) - refs[x.a].Invest(x.s, x.c, (Dimension)x.d);
                bool gotE = false;
                for (int r = 0; r < nP; r++)
                {
                    if (L.ClusterIndex[r] == x.c && dstar[x.s, r] == x.d && ind.ProcE(x.s, r) > Fx6.Zero)
                        gotE = true;
                }
                cells.Append(Inv, $"{t},{ids[x.a]},{L.Clusters[x.c]},{x.d},{x.h.Raw},{x.m.Raw},{mCapS[x.s].Raw},{(x.pool ? 1 : 0)},{p2.Raw},{pl.Notional(x.s, x.c, (Dimension)x.d).Raw},");
                cells.Append(Inv, $"{(pl.Participates(x.s, x.c, (Dimension)x.d) ? 1 : 0)},{pl.Goal(x.s, x.c, (Dimension)x.d).Raw},{(gotE ? 1 : 0)},{x.lab}\n");
            }
            for (int s = 0; s < L.SiteCount; s++)
            {
                if (ind.LastRest(s) != restUsed[L.SiteOwner[s]])
                    mismatchRest++;
            }
            int gFront = pr.GFront(t);
            for (int a = 0; a < nA; a++)
            {
                long outp = 0;
                for (int d = 0; d < designs; d++)
                {
                    Fx6 rate = pr.Rate(a, d);
                    outp += rate.Raw;
                    if (rate > Fx6.Zero)
                    {
                        Fx6 minY = Fx6.One;
                        foreach (int q in pr.PrcSet(a, d))
                        {
                            Fx6 y = ind.ProcYield(L.HomeSite(a), L.RowOf(q));
                            if (y < minY)
                                minY = y;
                        }
                        lines.Append(Inv, $"{t},{ids[a]},{d},{pr.Gen(a, d)},{rate.Raw},{pr.UnitCost(a, d).Raw},{minY.Raw}\n");
                    }
                }
                ToolingPlanResult pl = plans[a];
                Fx6 p2tot = pl.Total - refs[a].Total;
                acts.Append(Inv, $"{t},{ids[a]},{w.Policy.Profile(a)},{pl.Rest.Raw},{pl.CapBudget.Raw},{pl.Total.Raw},{pl.RestReturned.Raw},{pl.TrainingBudget.Raw},{pl.NotionalTotal.Raw},{p2tot.Raw},{outp},{pr.FieldedGen(a, t)},{gFront}\n");
                int s = L.HomeSite(a);
                for (int r = 0; r < nP; r++)
                {
                    if (firstMastered[a, r] < 0 && pr.Mastered(a, L.Number[r], t + 1))
                        firstMastered[a, r] = (int)t;
                    Fx6 y = ind.ProcYield(s, r);
                    if (tFirst[a, r] < 0 && y > Fx6.Zero && ind.ProcV(s, r) > Fx6.Zero)
                        tFirst[a, r] = (int)t;
                    if (t07[a, r] < 0 && y >= ip.MasteredYield)
                        t07[a, r] = (int)t;
                }
            }
        }

        var procs = new StringBuilder("actor,p,tier,mastered_t0,first_mastered,t_first,t_07\n");
        for (int a = 0; a < nA; a++)
            for (int r = 0; r < nP; r++)
                procs.Append(Inv, $"{ids[a]},{L.Number[r]},{L.Tier[r]},{(masteredT0[a, r] ? 1 : 0)},{firstMastered[a, r]},{tFirst[a, r]},{t07[a, r]}\n");
        var hend = new StringBuilder("actor,c,d,H0,Hend\n");
        for (int s = 0; s < L.SiteCount; s++)
            for (int c = 0; c < nC; c++)
                for (int d = 0; d < 3; d++)
                    hend.Append(Inv, $"{ids[L.SiteOwner[s]]},{L.Clusters[c]},{d},{h0[(s * nC + c) * 3 + d]},{ind.CultureH(s, c, (Dimension)d).Raw}\n");

        File.WriteAllText(Path.Combine(dir, "cells.csv"), cells.ToString());
        File.WriteAllText(Path.Combine(dir, "actors.csv"), acts.ToString());
        File.WriteAllText(Path.Combine(dir, "lines.csv"), lines.ToString());
        File.WriteAllText(Path.Combine(dir, "diag49.csv"), diag.ToString());
        File.WriteAllText(Path.Combine(dir, "procs.csv"), procs.ToString());
        File.WriteAllText(Path.Combine(dir, "hend.csv"), hend.ToString());
        string json = JsonSerializer.Serialize(new
        {
            arm = job.Arm, config = job.Config, cell = job.Cell, seed = job.Seed, threads = job.Threads, months,
            hash = session.LastHash, tick = session.Tick,
            mismatch_gain = mismatchGain, mismatch_rest = mismatchRest, mismatch_mcap = mismatchCap,
            profiles = Enumerable.Range(0, nA).Select(a => $"{ids[a]}={w.Policy.Profile(a)}").ToArray(),
        });
        File.WriteAllText(Path.Combine(dir, "run.json"), json);
    }

}
