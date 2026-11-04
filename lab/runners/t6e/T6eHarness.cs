// EXP-0003 (T6e), EXP-0004 (T6e2) and EXP-0005 (T6e3) runner. Lab observer of the jet slice: reads the world after each month, never writes it.
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

    // EXP-0005: read-only access to the AI's snapshot reader and its give-up gate, to print the A40 branch О guard (v0.21) firings.
    private static readonly FieldInfo ProdField = typeof(PolicySystem).GetField("_prod", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo GiveUpAllowedM = typeof(PolicySystem).GetMethod("GiveUpAllowed", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private sealed record Job(string Arm, string Config, int? Cell, ulong Seed, int Threads, LineCloseRule LineClose, Fx6 KCap);

    private const int TAb = 60;   // A40 T_ab (frozen); branch С = close of a line in С with idle_c >= T_ab

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
            jobs.Add(new Job(p[0], p[1], CellOf(cfg), ulong.Parse(p[2], Inv), int.Parse(p[3], Inv), LineCloseOf(cfg), KCapOf(cfg)));
        }
        else
        {
            foreach (JsonProperty c in root.GetProperty("configs").EnumerateObject())
            {
                // EXP-0004: a config may restrict the arms (ADMIT diagnostic: V0 and VINF only)
                string[] cfgArms = c.Value.TryGetProperty("arms", out JsonElement ca)
                    ? [.. ca.EnumerateArray().Select(x => x.GetString()!)]
                    : [.. arms.Keys];
                foreach (JsonElement s in c.Value.GetProperty("seeds").EnumerateArray())
                    foreach (string arm in cfgArms.Order(StringComparer.Ordinal))
                        jobs.Add(new Job(arm, c.Name, CellOf(c.Value), s.GetUInt64(), threads, LineCloseOf(c.Value), KCapOf(c.Value)));
            }
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
            ToolingTargetSpec spec = arms[job.Arm] with { KCap = job.KCap };
            spec.Validate();
            RunOne(job, spec, months, dir);
            output.WriteLine($"done {job}");
        }
    }

    private static int? CellOf(JsonElement cfg) =>
        cfg.GetProperty("profile_cell").ValueKind == JsonValueKind.Null ? null : cfg.GetProperty("profile_cell").GetInt32();

    private static LineCloseRule LineCloseOf(JsonElement cfg) =>
        cfg.TryGetProperty("line_close", out JsonElement lc) ? LineCloseRules.Parse(lc.GetString()!) : LineCloseRule.YProd;

    // exp-pool-order: the stress set is a config with k_cap (B_cap = ⌊k_cap·share_cap·budget⌋); 1 when absent.
    private static Fx6 KCapOf(JsonElement cfg) => cfg.TryGetProperty("k_cap", out _) ? Num(cfg, "k_cap") : Fx6.One;

    // B_cap as the Core adapter computes it: one floor over k_cap·share·budget (raw, non-negative); with k_cap = 1 equal to share·budget.
    private static Fx6 BCapOf(Fx6 kCap, Fx6 share, Fx6 budget) =>
        Fx6.FromRaw((long)((Int128)kCap.Raw * share.Raw * budget.Raw / ((Int128)Fx6.Scale * Fx6.Scale)));

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
            PoolOrder = e.TryGetProperty("pool_order", out JsonElement po) ? ToolingTargetSpec.ParsePoolOrder(po.GetString()!) : d.PoolOrder,
        };
        spec.Validate();
        return spec;
    }

    private static void RunOne(Job job, ToolingTargetSpec target, int months, string dir)
    {
        var config = new SliceRunConfig
        {
            Seed = job.Seed, ProfileCell = job.Cell, ToolingTarget = target, Threads = job.Threads, LineClose = job.LineClose,
        };
        SliceSession session = SliceSession.Create(SliceTestData.Data, config);
        SliceWorld w = session.World;
        IndustrySystem ind = w.Industry;
        IndustryLayout L = w.IndustryLayout;
        IIndustryInputs inputs = w.IndustryInputs;
        ICultureSnapshot cs = ind;
        ProductsSystem pr = w.Products;
        IndustryParams ip = w.IndustryParameters;
        var vinf = new ToolingTargetSpec { Variant = ToolingTarget.VInf };
        var v4Spec = target with { Variant = ToolingTarget.V4 };
        // exp-pool-order: the four pass-2 rules on the same snapshot (read only) — exhaustion N > P (rule-independent), Г1, PROP remainder
        PoolOrder[] orders = [PoolOrder.Can, PoolOrder.Rev, PoolOrder.Prop, PoolOrder.Sev];
        bool poolObs = target.Variant == ToolingTarget.V5;
        var poolCsv = new StringBuilder($"t,actor,exh,pool,total,{string.Join(',', L.Clusters.Select(c => $"u_{c}"))},g1_mismatch,prop_rem\n");
        long g1Mismatch = 0, exhMonths = 0, propRemMax = 0;
        Fx6 bigPool = Fx6.FromInt(1_000_000_000);
        var ledger = new StringBuilder("t,actor,n4,n5,ninf,v4not5,v5notinf,v5notinf_mdec_ge_mcap,goal_over_mcap\n");
        long ledV45 = 0, ledV5I = 0, ledV5Imdec = 0, ledGoal = 0, ledN4 = 0, ledN5 = 0, ledNI = 0;
        var guard = new StringBuilder("t,actor,design,gen,stage_o\n");
        long guardFirings = 0;
        var polProd = (IPolicyProducts)ProdField.GetValue(w.Policy)!;
        PolicyParams pp = w.PolicyParameters;
        Fx6 rhoPool = Fx6.FromRaw(21_000);
        int nA = w.Scenario.ActorCount, nC = L.Clusters.Length, nP = L.ProcessCount, designs = w.ProductsLayout.Designs;
        string[] ids = [.. Enumerable.Range(0, nA).Select(a => w.Scenario.ActorId[a])];
        int r49 = L.RowOf(49);

        var cells = new StringBuilder("t,actor,c,d,H,M,Mcap,pool,p2,notional,part,goal,gotE,lab,short,s,f5\n");
        var acts = new StringBuilder("t,actor,profile,rest,bcap,total,restRet,ta,notionalTotal,p2total,output,G,Gfront\n");
        var lines = new StringBuilder("t,actor,design,gen,rate,unitcost,minyield\n");
        var diag = new StringBuilder("t,actor,yield,vexp,lim,limsubj,MminusH_prec,MminusH_pur,MminusH_qc,H_prec,H_pur,H_qc,M_prec,M_pur,M_qc,E\n");
        long mismatchGain = 0, mismatchRest = 0, mismatchCap = 0;
        // EXP-0004 outputs: home-site capability, eligibility (for E* and D), F17b line print, A40 give-ups
        var homeCap = new StringBuilder("t,actor,c,d,H,M\n");
        var elig = new StringBuilder("t,actor,p\n");
        var lines2 = new StringBuilder("t,actor,design,gp,ymin,argp,g0,g1,g2,ramp,cultural,idle,rate,unitcost,close,start,stage_after,training\n");
        var events = new StringBuilder("t,actor,design,kind,cls\n");
        int maxLines = 0;
        var relevant = new bool[nC, 3];
        var reqTab = new StringBuilder("p,c,tier,r0,r1,r2\n");
        for (int r = 0; r < nP; r++)
        {
            reqTab.Append(Inv, $"{L.Number[r]},{L.Clusters[L.ClusterIndex[r]]},{L.Tier[r]},{L.Req[r][0].Raw},{L.Req[r][1].Raw},{L.Req[r][2].Raw}\n");
            for (int d = 0; d < 3; d++)
                relevant[L.ClusterIndex[r], d] |= L.Req[r][d] > Fx6.Zero;
        }
        var lnBefore = new List<(int a, int d, string pre, int idle)>();
        var protoClass = new Dictionary<(int, int), GiveUpClass>();
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

        long curT = 0;
        Fx6 MCapOf(int site)
        {
            int tier = 0;
            foreach (int r in L.Order)
            {
                if (inputs.KnowsProcess(site, r, curT) && ind.Yield(site, r, curT).Yield > Fx6.Zero && L.Clusters[L.ClusterIndex[r]] == Cluster.MECH && L.Tier[r] > tier)
                    tier = L.Tier[r];
            }
            return Fx6.FromInt(1 + (tier > 3 ? tier : 3));
        }

        for (long t = 0; t < months; t++)
        {
            curT = t;
            // ---- snapshot of month t (what the compute of month t reads)
            var plans = new ToolingPlanResult[nA];
            var refs = new ToolingPlanResult[nA];
            var unl = new ToolingPlanResult?[nA];   // EXP-0005 P-pool: same arm with an unlimited pool (room still binds)
            var v4 = new ToolingPlanResult?[nA];    // EXP-0005 (1ж): V4 on the same snapshot, V5 arm only
            var restUsed = new Fx6[nA];
            var mBefore = new Fx6[L.SiteCount * nC * 3];
            var mCapS = new Fx6[L.SiteCount];
            var pre = new List<(int a, int s, int c, int d, Fx6 h, Fx6 m, bool pool, int lab, bool f5)>();
            var dstar = new int[L.SiteCount, nP];
            for (int a = 0; a < nA; a++)
            {
                Fx6 rest = ind.RestActor(a, t);
                restUsed[a] = rest;
                Fx6 bCap = BCapOf(target.KCap, w.SharesOf(a).Capability, w.Policy.Budget(a));
                plans[a] = ToolingPlan.Compute(ind, inputs, w.PolicyParameters.MKeep, a, t, rest, bCap, target);
                refs[a] = ToolingPlan.Compute(ind, inputs, w.PolicyParameters.MKeep, a, t, rest, bCap, vinf);
                if (target.Variant != ToolingTarget.VInf)
                    unl[a] = ToolingPlan.Compute(ind, inputs, w.PolicyParameters.MKeep, a, t, bigPool, bigPool, target);
                if (target.Variant == ToolingTarget.V5)
                    v4[a] = ToolingPlan.Compute(ind, inputs, w.PolicyParameters.MKeep, a, t, rest, bCap, v4Spec);
                if (poolObs)
                {
                    var obs = new ToolingPlanResult[orders.Length];
                    for (int k = 0; k < orders.Length; k++)
                        obs[k] = ToolingPlan.Compute(ind, inputs, w.PolicyParameters.MKeep, a, t, rest, bCap, target with { PoolOrder = orders[k] });
                    // N > P before allocation ⇔ the canonical waterfall leaves some need unmet (pass 1 or 2); the same for every rule.
                    bool exh = false;
                    var u = new long[nC];
                    long mis = 0;
                    for (int s = 0; s < L.SiteCount; s++)
                    {
                        if (L.SiteOwner[s] != a)
                            continue;
                        for (int c = 0; c < nC; c++)
                            for (int d = 0; d < 3; d++)
                            {
                                exh |= obs[0].Unmet(s, c, (Dimension)d) > Fx6.Zero;
                                u[c] += plans[a].Unmet(s, c, (Dimension)d).Raw;
                            }
                    }
                    if (!exh)
                    {
                        for (int s = 0; s < L.SiteCount; s++)
                        {
                            if (L.SiteOwner[s] != a)
                                continue;
                            for (int c = 0; c < nC; c++)
                                for (int d = 0; d < 3; d++)
                                    for (int k = 1; k < orders.Length; k++)
                                        mis += obs[k].Invest(s, c, (Dimension)d) != obs[0].Invest(s, c, (Dimension)d) ? 1 : 0;
                        }
                    }
                    long propRem = exh ? (rest + bCap - obs[2].Total).Raw : 0;
                    g1Mismatch += mis;
                    exhMonths += exh ? 1 : 0;
                    propRemMax = Math.Max(propRemMax, propRem);
                    poolCsv.Append(Inv, $"{t},{ids[a]},{(exh ? 1 : 0)},{(rest + bCap).Raw},{plans[a].Total.Raw},{string.Join(',', u)},{mis},{propRem}\n");
                }
            }
            if (target.Variant == ToolingTarget.V5)
            {
                // (1ж): participation V4 ⊆ V5 ⊆ VINF on every cell of every site, and goal ≤ M_cap (ledger Participates of the same snapshot)
                for (int a = 0; a < nA; a++)
                {
                    long n4 = 0, n5 = 0, ni = 0, x45 = 0, x5i = 0, x5im = 0, xg = 0;
                    for (int s = 0; s < L.SiteCount; s++)
                    {
                        if (L.SiteOwner[s] != a)
                            continue;
                        Fx6 cap = MCapOf(s);
                        for (int c = 0; c < nC; c++)
                        {
                            for (int d = 0; d < 3; d++)
                            {
                                bool p4 = v4[a]!.Participates(s, c, (Dimension)d), p5 = plans[a].Participates(s, c, (Dimension)d), pi = refs[a].Participates(s, c, (Dimension)d);
                                n4 += p4 ? 1 : 0;
                                n5 += p5 ? 1 : 0;
                                ni += pi ? 1 : 0;
                                x45 += p4 && !p5 ? 1 : 0;
                                if (p5 && !pi)
                                {
                                    x5i++;
                                    Fx6 m = ind.ToolingM(s, c, (Dimension)d);
                                    x5im += m - ip.DeltaM * m >= cap ? 1 : 0;
                                }
                                xg += p5 && plans[a].Goal(s, c, (Dimension)d) > cap ? 1 : 0;
                            }
                        }
                    }
                    ledger.Append(Inv, $"{t},{ids[a]},{n4},{n5},{ni},{x45},{x5i},{x5im},{xg}\n");
                    ledN4 += n4; ledN5 += n5; ledNI += ni; ledV45 += x45; ledV5I += x5i; ledV5Imdec += x5im; ledGoal += xg;
                }
            }
            // P-A40: months where branch О would give up a project without the v0.21 guard while L(d, t) holds (emulation of the snapshot loop)
            for (int a = 0; a < nA; a++)
            {
                var designsA = new PolicyDesign[designs];
                for (int d = 0; d < designs; d++)
                    designsA[d] = polProd.Design(a, d, t);
                var gaveUpNow = new bool[w.ProductsLayout.Generations + 1];
                Fx6 launchLimit = pp.LaunchShare[(int)w.Policy.Profile(a)] * polProd.DRef;
                IReadOnlyList<DesignCommand> pendingCmds = w.Policy.AiDesignCommands(a, t);
                for (int d = 0; d < designs; d++)
                {
                    PolicyDesign x = designsA[d];
                    if (x.Stage != DesignStage.Prototype || x.StageOMonth < 0 || t - x.StageOMonth < pp.TAb
                        || !(bool)GiveUpAllowedM.Invoke(w.Policy, [a, x.PlatformGen, gaveUpNow, designsA])!)
                        continue;
                    if (w.Policy.AiStart(a, d, t) is not null || pendingCmds.Any(k => k.Kind == DesignCommandKind.Cancel && k.Target == d))
                        continue;
                    if (x.GateOpen && x.Hidden <= launchLimit && polProd.Admitted(a, d, t))
                    {
                        guard.Append(Inv, $"{t},{ids[a]},{d},{x.PlatformGen},{x.StageOMonth}\n");
                        guardFirings++;
                    }
                    gaveUpNow[x.PlatformGen] = true;
                }
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
                        bool f5 = false;   // EXP-0005: V5 participation formula (spec v0.21 §11), evaluated independently of ToolingPlan
                        for (int r = 0; r < nP; r++)
                        {
                            if (L.ClusterIndex[r] != c || L.Req[r][d] <= Fx6.Zero || !inputs.KnowsProcess(s, r, t))
                                continue;
                            anyProc = true;
                            f5 |= inputs.PlannedVolume(s, r, t) > Fx6.Zero && m <= h && m < L.Req[r][d];
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
                        pre.Add((a, s, c, d, h, m, pool, limT ? 2 : limE ? 1 : 0, f5));
                    }
                }
            }
            // EXP-0004: home-site H, M (relevant cells) and eligible processes (known ∧ (proc_v > 0 ∨ planned), r_d > 0 some d), snapshot t
            for (int a = 0; a < nA; a++)
            {
                int s = L.HomeSite(a);
                for (int c = 0; c < nC; c++)
                    for (int d = 0; d < 3; d++)
                        if (relevant[c, d])
                            homeCap.Append(Inv, $"{t},{ids[a]},{L.Clusters[c]},{d},{ind.CultureH(s, c, (Dimension)d).Raw},{ind.ToolingM(s, c, (Dimension)d).Raw}\n");
                for (int r = 0; r < nP; r++)
                {
                    if (!(L.Req[r][0] > Fx6.Zero || L.Req[r][1] > Fx6.Zero || L.Req[r][2] > Fx6.Zero) || !inputs.KnowsProcess(s, r, t))
                        continue;
                    if (ind.ProcV(s, r) > Fx6.Zero || inputs.Planned(s, r, t))
                        elig.Append(Inv, $"{t},{ids[a]},{L.Number[r]}\n");
                }
                // F17b print: every line in С by the snapshot, y_min split into Π g_d and ramp at the arg-min process
                int nLines = 0;
                for (int d = 0; d < designs; d++)
                {
                    DesignStage st = pr.Stage(a, d);
                    if (st == DesignStage.Prototype)
                        protoClass[(a, d)] = w.Policy.ClassifyGiveUp(a, d, t);
                    if (st != DesignStage.Series)
                        continue;
                    nLines++;
                    Fx6 min = Fx6.One;
                    int argp = -1;
                    foreach (int q in pr.PrcSet(a, d))
                    {
                        Fx6 y = w.ProductsInputs.AdmittedYield(a, q, t);
                        if (argp < 0 || y < min)
                        {
                            min = y;
                            argp = q;
                        }
                    }
                    if (argp < 0)
                        min = Fx6.Zero;
                    string parts = ",,,,";
                    if (argp >= 0 && L.RowOf(argp) >= 0 && inputs.KnowsProcess(s, L.RowOf(argp), t))
                    {
                        ProcessYield py = ind.Yield(s, L.RowOf(argp), t);
                        parts = string.Join(",", Enumerable.Range(0, 3).Select(k => py.IsActive(k) ? py.G[k].Raw.ToString(Inv) : "")) + "," + py.Ramp.Raw.ToString(Inv);
                    }
                    bool noSmelt = new[] { 39, 40, 41 }.All(p => w.ProductsInputs.AdmittedYield(a, p, t) == Fx6.Zero);
                    bool cultural = min == Fx6.Zero || (ind.Alloy(s, t).Supply == Fx6.Zero && noSmelt);
                    lnBefore.Add((a, d, $"{t},{ids[a]},{d},{pr.PlatformGen(a, d)},{min.Raw},{argp},{parts},{(cultural ? 1 : 0)},{pr.IdleMonths(a, d)}", pr.IdleMonths(a, d)));
                }
                maxLines = Math.Max(maxLines, nLines);
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
            var seen = new HashSet<(int, int)>();
            foreach (var (a, d, head, _) in lnBefore)
            {
                seen.Add((a, d));
                lines2.Append(head).Append(Inv, $",{pr.Rate(a, d).Raw},{pr.UnitCost(a, d).Raw},{(w.Policy.AiClose(a, d, t + 1) ? 1 : 0)},{(w.Policy.AiStart(a, d, t + 1) is null ? 0 : 1)},{(int)pr.Stage(a, d)},{pr.TrainingSpent(a).Raw}\n");
            }
            for (int a = 0; a < nA; a++)
            {
                for (int d = 0; d < designs; d++)
                {
                    // a start of month t on a design that was not a line in the snapshot (it shows up as a line from t+1)
                    if (!seen.Contains((a, d)) && w.Policy.AiStart(a, d, t + 1) is not null)
                        lines2.Append(Inv, $"{t},{ids[a]},{d},{pr.PlatformGen(a, d)},,,,,,,,,{pr.Rate(a, d).Raw},{pr.UnitCost(a, d).Raw},0,1,{(int)pr.Stage(a, d)},{pr.TrainingSpent(a).Raw}\n");
                }
                foreach (DesignCommand c in w.Policy.AiDesignCommands(a, t + 1))
                {
                    if (c.Kind == DesignCommandKind.Cancel)
                        events.Append(Inv, $"{t},{ids[a]},{c.Target},O,{(protoClass.TryGetValue((a, c.Target), out GiveUpClass gc) ? gc.ToString() : "?")}\n");
                }
            }
            lnBefore.Clear();
            protoClass.Clear();
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
                bool part = pl.Participates(x.s, x.c, (Dimension)x.d);
                long shortRaw = part && unl[x.a] is { } u ? (u.Invest(x.s, x.c, (Dimension)x.d) - pl.Invest(x.s, x.c, (Dimension)x.d)).Raw : 0;
                cells.Append(Inv, $"{(part ? 1 : 0)},{pl.Goal(x.s, x.c, (Dimension)x.d).Raw},{(gotE ? 1 : 0)},{x.lab},{shortRaw},{x.s},{(x.f5 ? 1 : 0)}\n");
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
        File.WriteAllText(Path.Combine(dir, "home.csv"), homeCap.ToString());
        File.WriteAllText(Path.Combine(dir, "elig.csv"), elig.ToString());
        File.WriteAllText(Path.Combine(dir, "lines2.csv"), lines2.ToString());
        File.WriteAllText(Path.Combine(dir, "events.csv"), events.ToString());
        File.WriteAllText(Path.Combine(dir, "req.csv"), reqTab.ToString());
        File.WriteAllText(Path.Combine(dir, "guard.csv"), guard.ToString());
        if (target.Variant == ToolingTarget.V5)
            File.WriteAllText(Path.Combine(dir, "ledger.csv"), ledger.ToString());
        if (poolObs)
            File.WriteAllText(Path.Combine(dir, "pool.csv"), poolCsv.ToString());
        string json = JsonSerializer.Serialize(new
        {
            arm = job.Arm, config = job.Config, cell = job.Cell, seed = job.Seed, threads = job.Threads, months,
            line_close = job.LineClose.ToString(), max_lines = maxLines, data_hash = SliceTestData.Data.DataHash,
            scenario_hashes = SliceTestData.Data.Scenarios.Select(x => $"{x.Id}={x.Hash}").ToArray(),
            hash = session.LastHash, tick = session.Tick,
            guard_firings = guardFirings,
            pool_order = target.PoolOrder.ToString(), k_cap = target.KCap.Raw,
            pool_obs = poolObs ? new { exh_actor_months = exhMonths, g1_mismatch = g1Mismatch, prop_remainder_max_raw = propRemMax } : null,
            ledger = target.Variant == ToolingTarget.V5 ? new { n4 = ledN4, n5 = ledN5, ninf = ledNI, v4_not_v5 = ledV45, v5_not_vinf = ledV5I, v5_not_vinf_mdec_ge_mcap = ledV5Imdec, goal_over_mcap = ledGoal } : null,
            mismatch_gain = mismatchGain, mismatch_rest = mismatchRest, mismatch_mcap = mismatchCap,
            profiles = Enumerable.Range(0, nA).Select(a => $"{ids[a]}={w.Policy.Profile(a)}").ToArray(),
        });
        File.WriteAllText(Path.Combine(dir, "run.json"), json);
    }

}
