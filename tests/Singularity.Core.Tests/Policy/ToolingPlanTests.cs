using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Policy;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Policy;

/// <summary>
/// Spec §3.5 A39 and A30 on a fixture of the "2 site" kind (A27): actor 0 owns sites 0 and 1, actor 1 owns site 2; clusters MET and MECH with one
/// process each (5 in MET, 6 in MECH). Only the MET process has a planned volume, so MET.purity is the bottleneck cell of M.
/// </summary>
public class ToolingPlanTests
{
    private const int Met = 5, Mech = 6;
    private static readonly Fx6 MKeep = Fx6.FromRaw(750_000);
    private static Fx6 F(long raw) => Fx6.FromRaw(raw);

    private sealed class Inputs : IIndustryInputs
    {
        public Fx6 Rest = Fx6.Zero, Cap = Fx6.Zero, Budget = Fx6.Zero;
        public IndustrySystem? System;
        public readonly Fx6[] Called = new Fx6[3 * 2 * 3];
        public readonly List<Fx6> Rests = new();
        public bool PlanInvest = true;

        public Fx6 EffortBudget(int actorRow, long month) => Budget;
        public int EffortFocus(int actorRow, int k, long month) => -1;
        public bool NoPlanned;
        public bool Planned(int siteRow, int processRow, long month) => !NoPlanned && processRow == 0;
        public Fx6 PlannedVolume(int siteRow, int processRow, long month) => !NoPlanned && processRow == 0 ? Fx6.FromInt(10) : Fx6.Zero;
        public bool KnowsProcess(int siteRow, int processRow, long month) => true;
        public Fx6 W(int siteRow, int cluster, long month) => Fx6.One;
        public Fx6 Hcap(int siteRow, Dimension dim, long month) => Fx6.FromInt(5);
        public bool QualityProgramme(int siteRow, long month) => false;

        public Fx6 ToolingInvest(int siteRow, int cluster, Dimension dim, long month, Fx6 restActor)
        {
            lock (Rests)
                Rests.Add(restActor);
            if (!PlanInvest)
                return Fx6.Zero;
            int owner = System!.Layout.SiteOwner[siteRow];
            Fx6 v = ToolingPlan.Compute(System, this, MKeep, owner, month, restActor, Cap).Invest(siteRow, cluster, dim);
            Called[(siteRow * 2 + cluster) * 3 + (int)dim] = v;
            return v;
        }

        public Fx6 LicenceShare(int siteRow, int processRow, long month) => Fx6.Zero;
        public long WorkforceAvailable(int siteRow, int category, long month) => 100;
        public Fx6 AlloyCap(int siteRow, long month) => Fx6.Zero;
        public Fx6 AlloyDeficit(int siteRow, long month) => Fx6.Zero;
        public long KnownSince(int siteRow, int processRow, long month) => -1;
    }

    private sealed class Rig
    {
        public Rig(Func<int, int, int, Fx6>? m = null, int threads = 1)
        {
            Layout = IndustryLayout.Create([Cluster.MET, Cluster.MECH],
                [
                    new IndustryProcessSpec(Met, Cluster.MET, 0, 3, new Dims3(Fx6.FromInt(3), Fx6.FromInt(3), F(2_500_000))),
                    new IndustryProcessSpec(Mech, Cluster.MECH, 0, 3, new Dims3(Fx6.FromInt(3), Fx6.FromInt(3), F(2_500_000))),
                ],
                siteOwner: [0, 0, 1], workforceCategories: 2, skilledCategory: 1, requesters: 2);
            var start = new IndustryStart(Layout);
            for (int s = 0; s < 3; s++)
            {
                for (int c = 0; c < 2; c++)
                {
                    for (int d = 0; d < 3; d++)
                    {
                        start.H[start.Cell(s, c, d)] = Fx6.FromInt(4);
                        // Default M: purity of MET is short (bottleneck), the rest is comfortable.
                        start.M[start.Cell(s, c, d)] = m?.Invoke(s, c, d) ?? (c == 0 && d == 1 ? F(2_800_000) : F(3_400_000));
                    }
                }
                start.X[s * 2] = Fx6.FromInt(10_000);
                start.X[s * 2 + 1] = Fx6.FromInt(10_000);
            }
            Table = new Table(3);
            Calendar = new TickCalendar(new CivilDate(1945, 9, 1));
            System = new IndustrySystem(Table, Layout, start, new IndustryParams(), Inputs, TickRunner.ChunkCountFor(3, 1), new V23CultureRule());
            Inputs.System = System;
            Runner = new TickRunner(Table, 7, [System], 1, threads, Calendar);
        }

        public IndustryLayout Layout { get; }
        public Table Table { get; }
        public TickCalendar Calendar { get; }
        public Inputs Inputs { get; } = new();
        public IndustrySystem System { get; }
        public TickRunner Runner { get; }

        public ToolingPlanResult Plan(int actor, Fx6 rest, Fx6 cap, long month = 0) =>
            ToolingPlan.Compute(System, Inputs, MKeep, actor, month, rest, cap);

        public void RunThroughMonth(long month)
        {
            long last = Calendar.TickOfMonth(month);
            while (Runner.Tick <= last)
                Runner.RunTick();
        }
    }

    private static Fx6 Sum(ToolingPlanResult plan, int sites)
    {
        Fx6 sum = Fx6.Zero;
        for (int s = 0; s < sites; s++)
        {
            for (int c = 0; c < 2; c++)
            {
                for (int d = 0; d < 3; d++)
                    sum += plan.Invest(s, c, (Dimension)d);
            }
        }
        return sum;
    }

    [Fact]
    public void TwoSites_RestIsSharedNotSpentOncePerSite_Debt0015()
    {
        var rig = new Rig();
        Fx6 rest = F(150_000);
        ToolingPlanResult plan = rig.Plan(0, rest, Fx6.Zero);
        // Both sites need ≈ 0,1214 on MET.purity; the pool 0,15 is one pool: Σ = rest, not 2·rest.
        Assert.Equal(rest, Sum(plan, 3));
        Assert.Equal(rest, plan.TakenRest);
        Assert.Equal(Fx6.Zero, plan.TakenCap);
        Assert.Equal(Fx6.Zero, plan.RestReturned);
        // Waterfall: the lower site row first.
        Assert.True(plan.Invest(0, 0, Dimension.Purity) > plan.Invest(1, 0, Dimension.Purity));
        Assert.True(plan.Invest(1, 0, Dimension.Purity) > Fx6.Zero);
        // Site 2 belongs to the other actor.
        Assert.Equal(Fx6.Zero, plan.Invest(2, 0, Dimension.Purity));
    }

    [Fact]
    public void RestFirst_ThenCapBudget_AndBalanceHolds()
    {
        var rig = new Rig();
        ToolingPlanResult plan = rig.Plan(0, F(50_000), Fx6.FromInt(1));
        // Needs: two cells of ≈ 0,1214 = 0,2428 in total; the 0,05 of rest goes first, the rest of the need comes from B_cap.
        Assert.Equal(F(50_000), plan.TakenRest);
        Assert.Equal(plan.Total - F(50_000), plan.TakenCap);
        Assert.Equal(Fx6.FromInt(1) - plan.TakenCap, plan.TrainingBudget);
        Assert.True(plan.TakenCap > Fx6.Zero);
        Assert.Equal(plan.Rest + plan.CapBudget, plan.Total + plan.RestReturned + plan.TrainingBudget);
    }

    [Fact]
    public void Balance_SumTakenPlusUnspent_EqualsPool_ForManyPools()
    {
        var rig = new Rig();
        ulong x = 12345;
        for (int i = 0; i < 300; i++)
        {
            x = x * 6364136223846793005UL + 1442695040888963407UL;
            Fx6 rest = F((long)(x >> 44) % 400_000), cap = F((long)((x >> 20) & 0xFFFFF) % 600_000);
            ToolingPlanResult plan = rig.Plan(0, rest, cap);
            Assert.True(plan.Total <= rest + cap);
            Assert.True(plan.TakenRest <= rest);
            Assert.Equal(plan.Total, plan.TakenRest + plan.TakenCap);
            Assert.Equal(rest + cap, plan.Total + plan.RestReturned + plan.TrainingBudget);
            Assert.True(plan.TrainingBudget >= Fx6.Zero && plan.RestReturned >= Fx6.Zero);
        }
    }

    [Fact]
    public void CellCeiling_NoInvestmentAtOrAboveTheCap_AndNeverMoreThanRoom()
    {
        // MET.purity of site 0 sits at the cap already: M_dec ≥ M_cap = 4.
        var rig = new Rig((s, c, d) => c == 0 && d == 1 ? (s == 0 ? Fx6.FromInt(5) : F(2_800_000)) : F(3_400_000));
        ToolingPlanResult plan = rig.Plan(0, Fx6.FromInt(10), Fx6.FromInt(10));
        Assert.Equal(Fx6.Zero, plan.Invest(0, 0, Dimension.Purity));
        Assert.True(plan.Invest(1, 0, Dimension.Purity) > Fx6.Zero);
        // The pool is far larger than the need: what the cells could take is all that is taken (nothing burns above the cap).
        Assert.True(plan.Total < Fx6.FromInt(1));
        Assert.True(plan.TrainingBudget > Fx6.FromInt(9));
    }

    [Fact]
    public void PassOne_A30_ServesTheMechProcessBeforeEarlierBottleneckCells()
    {
        // MECH.purity of site 0 is close to the loss of admission: M_dec < r − m_keep. MET (earlier in the order) has a bottleneck too.
        var rig = new Rig((s, c, d) => c == 1 && d == 1 && s == 0 ? F(2_100_000) : c == 0 && d == 1 ? F(2_800_000) : F(3_400_000));
        IndustryParams p = new();
        Fx6 m = F(2_100_000), dec = m - p.DeltaM * m;
        Fx6 need = (Fx6.FromInt(3) - MKeep - dec) * p.CostM(3);
        Assert.True(need > Fx6.Zero);
        ToolingPlanResult plan = rig.Plan(0, need, Fx6.Zero);
        Assert.Equal(need, plan.Invest(0, 1, Dimension.Purity));
        Assert.Equal(Fx6.Zero, plan.Invest(0, 0, Dimension.Purity));
    }

    [Fact]
    public void PassTwo_OnlyBottleneckCellsOfPlannedProcesses_GetMoney()
    {
        var rig = new Rig();
        ToolingPlanResult plan = rig.Plan(0, Fx6.FromInt(10), Fx6.Zero);
        for (int s = 0; s < 2; s++)
        {
            for (int c = 0; c < 2; c++)
            {
                for (int d = 0; d < 3; d++)
                {
                    bool expected = c == 0 && d == 1;
                    Assert.Equal(expected, plan.Invest(s, c, (Dimension)d) > Fx6.Zero);
                }
            }
        }
    }

    [Fact]
    public void SameInvestmentInEveryCallOfTheMonth_AndMStaysUnderTheCap_Threads1And4()
    {
        string[] hashes = new string[2];
        for (int run = 0; run < 2; run++)
        {
            var rig = new Rig(threads: run == 0 ? 1 : 4);
            rig.Inputs.Budget = F(200_000);   // D: with no useful E the whole item is rest_a
            rig.Inputs.Cap = F(20_000);
            for (long t = 0; t < 40; t++)
            {
                rig.RunThroughMonth(t);
                Fx6 rest = rig.System.LastRest(0);
                Assert.Equal(rest, rig.System.LastRest(1));
                // Every call of the month got the same rest_a, and the cells it returned form one plan (Σ within the pool).
                Fx6 sum = Fx6.Zero;
                for (int s = 0; s < 2; s++)
                {
                    for (int c = 0; c < 2; c++)
                    {
                        for (int d = 0; d < 3; d++)
                        {
                            sum += rig.Inputs.Called[(s * 2 + c) * 3 + d];
                            Assert.True(rig.System.ToolingM(s, c, (Dimension)d) <= Fx6.FromInt(4) + Fx6.FromRaw(1), $"month {t}: M above M_cap");
                        }
                    }
                }
                Assert.True(sum <= rest + rig.Inputs.Cap, $"month {t}: Σ {sum} above the pool");
            }
            hashes[run] = StateHash.ComputeHex(rig.Table, 1, rig.Runner.Tick);
        }
        Assert.Equal(hashes[0], hashes[1]);
    }

    [Fact]
    public void A30_KeepsTheMechProcessAdmitted_For120Months_WithoutOtherInvestment()
    {
        static bool Admitted(Rig rig, int site) => rig.System.ProcYield(site, 1) > Fx6.Zero;
        // No planned volume anywhere: pass 2 has no cells, only pass 1 spends B_cap.
        Rig Make(Fx6 cap)
        {
            var rig = new Rig((s, c, d) => F(3_200_000));
            rig.Inputs.Cap = cap;
            return rig;
        }
        Rig kept = Make(F(10_000)), unkept = Make(Fx6.Zero);
        kept.Inputs.NoPlanned = true;
        unkept.Inputs.NoPlanned = true;
        int lostAt = -1;
        for (int t = 0; t < 120; t++)
        {
            kept.RunThroughMonth(t);
            unkept.RunThroughMonth(t);
            Assert.True(Admitted(kept, 0), $"month {t}: the MECH process lost admission despite A30");
            if (lostAt < 0 && !Admitted(unkept, 0))
                lostAt = t;
        }
        Assert.True(lostAt is > 60 and < 120, $"control: without A30 the process should be lost within 6–10 years, lost at {lostAt}");
    }
}
