using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Industry;

/// <summary>TASK-0134: Hcap(t), the mastery programme in the culture rule and in the effort allocation (hcap-growth.md §7, production-mastery.md §2).</summary>
public class MasteryCoreTests
{
    private static readonly CbCultureRule Rule = new();
    private const int Pur = (int)Dimension.Purity;

    private static Fx6 D(long u) => Fx6.FromInt(u);

    private static Fx6 F(long raw) => Fx6.FromRaw(raw);

    private static Dims3 Req(long prec, long pur, long qc) => new(F(prec), F(pur), F(qc));

    // ---- Hcap(t)

    private static IndustryParams Growth(long k, long max, int onset = 388) => new() { HcapGrowthPerYear = F(k), HcapGrowthMax = F(max), HcapGrowthOnset = onset };

    [Fact]
    public void HcapGrowth_IsZeroBeforeAndAtTheOnset_LinearAfter_RoundedDown_AndClamped()
    {
        IndustryParams p = Growth(100_000, 1_000_000);
        Assert.Equal(Fx6.Zero, p.HcapGrowth(0));
        Assert.Equal(Fx6.Zero, p.HcapGrowth(387));
        Assert.Equal(Fx6.Zero, p.HcapGrowth(388));
        Assert.Equal(F(8_333), p.HcapGrowth(389));        // k = 0,1 per year, n = 1: ⌊100000/12⌋
        Assert.Equal(F(100_000), p.HcapGrowth(400));      // n = 12: exactly k
        Assert.Equal(Fx6.One, p.HcapGrowth(388 + 120));   // 10 years: the limit
        Assert.Equal(Fx6.One, p.HcapGrowth(1023));
    }

    [Fact]
    public void HcapGrowth_IsMonotone_BoundedByTheLimit_AndOffWithAZeroParameter()
    {
        IndustryParams p = Growth(137_000, 1_500_000);
        Fx6 last = Fx6.Zero;
        for (long m = 0; m < 1024; m++)
        {
            Fx6 g = p.HcapGrowth(m);
            Assert.True(g >= last && g <= p.HcapGrowthMax);
            last = g;
        }
        Assert.Equal(Fx6.Zero, Growth(0, 1_000_000).HcapGrowth(900));
        Assert.Equal(Fx6.Zero, Growth(100_000, 0).HcapGrowth(900));
    }

    [Fact]
    public void Validate_RefusesNegativeGrowth_AndAnUpkeepBelowOne()
    {
        Assert.Throws<ArgumentException>(() => (new IndustryParams { HcapGrowthPerYear = F(-1) }).Validate());
        Assert.Throws<ArgumentException>(() => (new IndustryParams { HcapGrowthMax = F(-1) }).Validate());
        Assert.Throws<ArgumentException>(() => (new IndustryParams { UpkeepMult = F(999_999) }).Validate());
        new IndustryParams().Validate();
    }

    [Fact]
    public void ProcessAtTheCeiling_IsSaturated_UntilHcapGrows()
    {
        // r_d = Hcap0 = 5.5 as process 73 of the USA: the limiter is Saturation until the ceiling rises above it.
        IndustryLayout l = CbFake.MakeLayout([0], (10, Cluster.MET, Req(0, 5_500_000, 0)));
        IndustryParams growth = Growth(100_000, 1_000_000);
        var s = new CbFake(l, growth) { HcapAt = m => F(5_500_000) + growth.HcapGrowth(m) };
        for (int d = 0; d < 3; d++)
            s.Ms[s.Idx(0, 0, d)] = D(6);
        s.SetH(0, 0, Pur, D(5));
        Assert.Equal(LimiterKind.Saturation, Rule.Limiter(s, 0, s.P(10), 387).Value);
        Assert.NotEqual(LimiterKind.Saturation, Rule.Limiter(s, 0, s.P(10), 500).Value);
    }

    // ---- the assessment of a programme

    private static CbFake OneProcessAtTheLabCeiling(out int a)
    {
        // Purity r_d 4,0; H = 3,6 >= r_d - ΔL (3,5) and no output: the limiter is LabCeiling.
        IndustryLayout l = CbFake.MakeLayout([0], (10, Cluster.MET, Req(0, 4_000_000, 0)), (11, Cluster.MET, Req(0, 4_100_000, 0)));
        var s = new CbFake(l);
        for (int d = 0; d < 3; d++)
            s.Ms[s.Idx(0, 0, d)] = D(6);
        s.SetH(0, 0, Pur, F(3_600_000));
        a = s.P(10);
        return s;
    }

    [Fact]
    public void Assess_ALoneProcessAtTheLabCeiling_LeadsItsCell_WithATime()
    {
        CbFake s = OneProcessAtTheLabCeiling(out int a);
        Assert.Equal(LimiterKind.LabCeiling, CbCultureRule.Limiter0(s, 0, a, Pur, 0));
        ProgrammeAssessment r = Rule.Assess(s, 0, a, D(10), domestic: false, 0);
        Assert.Equal(ProgrammeFit.Ok, r.Fit);
        Assert.Equal(a, r.LeadRow);
        Assert.True(r.EtaMonths > 0);
    }

    [Fact]
    public void Assess_AnotherProcessWithOutputLeadsTheCell_CellLed()
    {
        CbFake s = OneProcessAtTheLabCeiling(out int a);
        int other = s.P(11);
        s.Vs[other] = D(10);
        s.PlannedF[other] = true;
        ProgrammeAssessment r = Rule.Assess(s, 0, a, D(10), domestic: false, 0);
        Assert.Equal(ProgrammeFit.CellLed, r.Fit);
        Assert.Equal(other, r.LeadRow);
    }

    [Fact]
    public void Assess_WithOutput_Saturation_Tooling_AreNotAProgramme()
    {
        CbFake s = OneProcessAtTheLabCeiling(out int a);
        s.Vs[a] = D(10);
        s.PlannedF[a] = true;
        Assert.Equal(ProgrammeFit.NotLabCeiling, Rule.Assess(s, 0, a, D(10), false, 0).Fit);
        s.Vs[a] = Fx6.Zero;
        s.PlannedF[a] = false;
        s.HcapByDim[Pur] = D(4);   // Hcap = r_d
        Assert.Equal(ProgrammeFit.Saturation, Rule.Assess(s, 0, a, D(10), false, 0).Fit);
        s.HcapByDim[Pur] = D(5);
        s.Ms[s.Idx(0, 0, Pur)] = D(3);   // M < H
        Assert.Equal(ProgrammeFit.Tooling, Rule.Assess(s, 0, a, D(10), false, 0).Fit);
    }

    [Fact]
    public void Assess_AProcessInTheLaboratoryPhase_WithHcapAboveRd_AndNoOutput_IsEligible_AndGetsMoneyUnderTheProgramme()
    {
        // H = 2 is below r_d − ΔL (3,5): the plain limiter is Effort (the laboratory part), not LabCeiling, and nobody pays it: no output, not planned.
        IndustryLayout l = CbFake.MakeLayout([0], (10, Cluster.MET, Req(0, 4_000_000, 0)), (11, Cluster.CHEM, Req(0, 4_000_000, 0)));
        var s = new CbFake(l);
        for (int c = 0; c < 2; c++)
            for (int d = 0; d < 3; d++)
                s.Ms[s.Idx(0, c, d)] = D(6);
        int a = s.P(10), other = s.P(11);
        s.Vs[other] = D(10);
        s.PlannedF[other] = true;
        Assert.Equal(LimiterKind.Effort, CbCultureRule.Limiter0(s, 0, a, Pur, 0));
        Assert.Equal(Fx6.Zero, Rule.EUseful(s, 0, a, 0));
        ProgrammeAssessment r = Rule.Assess(s, 0, a, D(10), domestic: false, 0);
        Assert.Equal(ProgrammeFit.Ok, r.Fit);
        // The programme input as the layer gives it: planned and with the nominal volume. Now the process leads its cell and is paid.
        s.PlannedF[a] = true;
        s.Vgrow[a] = D(10);
        s.ProgStart[a] = 5;
        Fx6 eu = Rule.EUseful(s, 0, a, 0);
        Assert.True(eu > Fx6.Zero);
        EffortAllocation alloc = EffortAllocator.Allocate(Rule, s, 0, 0, s.Params.EFull * D(2), [-1, -1, -1]);
        Assert.Equal(eu, alloc.E[a]);
    }

    [Fact]
    public void Assess_AbroadWithADonorIsFasterThanOnTheOwnPilotSite()
    {
        CbFake s = OneProcessAtTheLabCeiling(out int a);
        s.Lead[a] = true;   // another country has mastered the process
        int abroad = Rule.Assess(s, 0, a, D(10), domestic: false, 0).EtaMonths;
        int home = Rule.Assess(s, 0, a, D(10), domestic: true, 0).EtaMonths;
        Assert.True(abroad > 0 && abroad < home, $"abroad {abroad}, home {home}");
        s.Lead[a] = false;
        Assert.Equal(home, Rule.Assess(s, 0, a, D(10), domestic: false, 0).EtaMonths);   // no donor: the same
    }

    [Fact]
    public void DomesticProgramme_DropsTheCatchUp_InTheCellStep()
    {
        CbFake s = OneProcessAtTheLabCeiling(out int a);
        s.Lead[a] = true;
        s.Vgrow[a] = D(10);
        s.PlannedF[a] = true;
        s.Es[a] = s.Params.EFull;
        Fx6 abroad = Rule.Step(s, 0, 0, Pur, 0).DHPlus;
        s.ProgDomestic[a] = true;
        Fx6 home = Rule.Step(s, 0, 0, Pur, 0).DHPlus;
        Assert.True(abroad > home);
    }

    // ---- the allocation of D

    private static CbFake TwoCellsWithProgrammeOnTheFirst(out int prog, out int other)
    {
        IndustryLayout l = CbFake.MakeLayout([0], (10, Cluster.MET, Req(0, 4_000_000, 0)), (11, Cluster.CHEM, Req(0, 4_000_000, 0)));
        var s = new CbFake(l);
        prog = s.P(10);
        other = s.P(11);
        for (int c = 0; c < 2; c++)
        {
            s.SetH(0, c, Pur, F(3_600_000));
            for (int d = 0; d < 3; d++)
                s.Ms[s.Idx(0, c, d)] = D(6);
        }
        s.Vs[other] = D(10);
        s.Vexp[other] = D(10);
        s.PlannedF[other] = true;
        // The programme input as the layer gives it: planned, with the nominal volume.
        s.PlannedF[prog] = true;
        s.Vgrow[prog] = D(10);
        s.Vexp[prog] = D(10);
        s.ProgStart[prog] = 5;
        return s;
    }

    [Fact]
    public void Allocation_ProgrammeFirst_UpkeepIsTakenFromD_AndTheBudgetIsConserved()
    {
        CbFake s = TwoCellsWithProgrammeOnTheFirst(out int prog, out int other);
        Fx6 euProg = Rule.EUseful(s, 0, prog, 0), euOther = Rule.EUseful(s, 0, other, 0);
        Assert.True(euProg > Fx6.Zero && euOther > Fx6.Zero);
        Fx6 budget = euProg + euProg / D(2) + euOther / D(2);   // not enough for everyone after the upkeep
        EffortAllocation a = EffortAllocator.Allocate(Rule, s, 0, 0, budget, [-1, -1, -1]);
        Assert.Equal(euProg, a.E[prog]);
        Assert.Equal((s.Params.UpkeepMult - Fx6.One) * euProg, a.Upkeep);
        Fx6 sum = a.E.Aggregate(Fx6.Zero, (x, y) => x + y);
        Assert.Equal(budget, sum + a.Rest + a.Upkeep);
        // Without the programme the same budget is split by weight and nothing is lost to upkeep.
        s.ProgStart[prog] = 0;
        EffortAllocation plain = EffortAllocator.Allocate(Rule, s, 0, 0, budget, [-1, -1, -1]);
        Assert.Equal(Fx6.Zero, plain.Upkeep);
        Assert.NotEqual(plain.E[prog], a.E[prog]);
    }

    [Fact]
    public void Allocation_ProgrammeBeforeThePlayersFocus_AndFocusNeverExceedsEUseful()
    {
        CbFake s = TwoCellsWithProgrammeOnTheFirst(out int prog, out int other);
        Fx6 euProg = Rule.EUseful(s, 0, prog, 0), euOther = Rule.EUseful(s, 0, other, 0);
        Fx6 budget = euProg + euProg / D(4);
        // The player focuses the programme process too: it must not be paid twice.
        EffortAllocation a = EffortAllocator.Allocate(Rule, s, 0, 0, budget, new[] { 10, -1, -1 });
        Assert.Equal(euProg, a.E[prog]);
        Assert.True(a.E[other] <= euOther);
        Assert.Equal(budget, a.E.Aggregate(Fx6.Zero, (x, y) => x + y) + a.Rest + a.Upkeep);
    }

    [Fact]
    public void Allocation_WithoutAProgramme_IsTheOldAllocation_BitForBit()
    {
        CbFake s = TwoCellsWithProgrammeOnTheFirst(out int prog, out _);
        s.ProgStart[prog] = 0;
        s.Vgrow[prog] = Fx6.Zero;
        EffortAllocation a = EffortAllocator.Allocate(Rule, s, 0, 0, F(1_500), [-1, -1, -1]);
        Assert.Equal(Fx6.Zero, a.Upkeep);
        Assert.Equal(F(1_500), a.E.Aggregate(Fx6.Zero, (x, y) => x + y) + a.Rest);
    }
}
