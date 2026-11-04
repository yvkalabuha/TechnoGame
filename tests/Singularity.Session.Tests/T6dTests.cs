using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>
/// T6d on the assembled world (spec §3.5 A43, §11 «T6d — приёмка», contract §8.6 п. 6): the fixture of month 0 (E1), the invariants L-M1, L-M2, L-S1, L-D1
/// over whole runs, the construction check E-LIC-0, the regression of <c>T6D_OFF</c> by projection on the columns that existed before T6d, and
/// the knowledge of the <c>lic_knows</c> diagnostic.
/// </summary>
public class T6dTests
{
    private static SliceSession Create(ulong seed, SliceRunConfig? template = null) =>
        SliceTestData.Create((template ?? new SliceRunConfig()) with { Seed = seed });

    private static int Actor(SliceSession s, string id) => s.World.Scenario.ActorIndex(id);

    // ---- A43.2, A43.3: the fixture of month 0 and the price (item 2, item 4)

    [Fact]
    public void E1_AtT0_OnlyUsaAndGbrHaveASeriesEngine_SoTheOfferablePairsAreExactlyThoseToSunFraSwe_AndG1CostsTwentyThousandSevenHundredThirtyRaw()
    {
        SliceWorld w = Create(7).World;
        var ev = new PolicyEventsAdapter(w);
        string[] sellers = ["USA", "GBR"], buyers = ["SUN", "FRA", "SWE"];
        var pairs = new List<(string, string)>();
        foreach (string s in sellers.Concat(buyers))
        {
            int g = ev.EngineGen(Actor(Create(7), s), 0);
            Assert.Equal(sellers.Contains(s) ? 1 : 0, g);
        }
        foreach (string s in sellers)
        {
            foreach (string b in buyers)
            {
                if (ev.EngineGen(w.Scenario.ActorIndex(s), 0) > ev.EngineGen(w.Scenario.ActorIndex(b), 0))
                    pairs.Add((s, b));
            }
        }
        Assert.Equal(6, pairs.Count);
        Assert.DoesNotContain(("USA", "GBR"), pairs);
        LicenceSubject subject = ev.Subject(w.Scenario.ActorIndex("USA"), 1, 0)!.Value;
        Assert.Equal(20_730, LicenceRules.Price(subject.UnitCostFloor, 50, w.PolicyParameters.LicenceShare).Raw);
        // The mask of E1 (spec A43.1): the project of the G1 fleet (eng_g1 + hot_nicr_forged) → nodes 39, 44, 45, 49, 54.
        ulong expected = 0;
        foreach (int n in (int[])[39, 44, 45, 49, 54])
            expected |= LicenceRules.Bit(n);
        Assert.Equal(expected, subject.Mask);
    }

    [Fact]
    public void ThePolicyHasNoLicenceAtStart_AndTheFirstOneActsFromMonthOne()
    {
        // A licence is an event, not a decision of the initialisation phase (A8): nothing at tick 0, and a deal of compute 0 is in force from month 1.
        bool found = false;
        for (ulong seed = 1; seed <= 12 && !found; seed++)
        {
            SliceSession s = Create(seed);
            for (int a = 0; a < s.World.Scenario.ActorCount; a++)
                Assert.Equal(0, s.World.Policy.LicenceGen(a));
            s.StepThroughMonth(0);
            for (int a = 0; a < s.World.Scenario.ActorCount; a++)
                Assert.Equal(0, s.World.Policy.LicenceGen(a));   // decided in compute 0, applied at the start of month 1
            s.StepThroughMonth(1);
            for (int a = 0; a < s.World.Scenario.ActorCount; a++)
            {
                if (s.World.Policy.LicenceGen(a) == 0)
                    continue;
                found = true;
                Assert.Equal(1, s.World.Policy.LicenceStart(a));
                Assert.Equal(1, s.World.Policy.LicenceGen(a));
                Assert.True(s.World.Policy.LicenceSeller(a) is 0 or 1, "the sellers of month 0 are USA and GBR");
                Assert.Equal(-20_730, s.World.Policy.LicenceFlow(a).Raw);   // the buyer paid the price of the G1 project
                Assert.True(s.World.Policy.LicenceFlow(s.World.Policy.LicenceSeller(a)).Raw >= 20_730);
            }
        }
        Assert.True(found, "no licence was bought at month 0 for seeds 1…12: the fixture does not exercise the purchase");
    }

    // ---- invariants over runs (L-M1, L-M2, L-S1, L-D1, ext_total, item 4, item 5)

    [Fact]
    public void Runs_KeepTheMoneyAndTheStateInvariantsOfLicenceAndDisruption_EveryMonth()
    {
        int licences = 0, disruptions = 0, shares = 0;
        foreach (ulong seed in new ulong[] { 1, 2, 3, 4 })
        {
            SliceSession s = Create(seed);
            SliceWorld w = s.World;
            int n = w.Scenario.ActorCount;
            var lastEnd = Enumerable.Repeat(-1, n).ToArray();
            var lastStart = Enumerable.Repeat(-1, n).ToArray();
            var sale = new Fx6[n];
            for (int m = 0; m < 384; m++)
            {
                s.StepThroughMonth(m);
                long month = m + 1, now = m;   // the flows and the licence state are those applied at the start of month m; the disruption window is that of m + 1
                Fx6 sum = Fx6.Zero;
                for (int a = 0; a < n; a++)
                {
                    sum += w.Policy.LicenceFlow(a);
                    sale[a] += w.Products.SaleIncome(a);
                    Assert.True(w.ProcurementBudget(a) >= Fx6.Zero, $"seed {seed} month {month}: L-M2, actor {a}");
                    Assert.Equal(w.Policy.LicenceFlow(a), w.ProcurementBudget(a) - w.ProcurementBase(a));
                    // The flow is non-zero only in the month a deal was applied.
                    if (w.Policy.LicenceFlow(a) != Fx6.Zero)
                        Assert.True(Enumerable.Range(0, n).Any(b => w.Policy.LicenceStart(b) == now), $"seed {seed} month {now}: a flow without a deal");
                    (int start, int end) = w.Policy.Disruption(a);
                    // L-D1: the deficit is non-zero exactly inside [start; end]; a new window is drawn only after the last one is over.
                    Assert.Equal(start >= 0 && month >= start && month <= end, w.Policy.AlloyDeficit(a, month) != Fx6.Zero);
                    if (end != lastEnd[a])
                    {
                        disruptions++;
                        Assert.Equal(m + 1, start);
                        Assert.InRange(end - m, 6, 18);
                        Assert.True(lastEnd[a] < m, $"seed {seed} actor {a}: a draw while a disruption was in force");
                        lastEnd[a] = end;
                    }
                    if (w.Policy.LicenceStart(a) != lastStart[a])
                    {
                        licences++;
                        Assert.Equal(now, w.Policy.LicenceStart(a));
                        lastStart[a] = w.Policy.LicenceStart(a);
                    }
                    // L-S1: a share only for an active licence, a bit of the mask and a known process.
                    int site = a;
                    foreach (int r in w.IndustryLayout.Order)
                    {
                        if (w.IndustryInputs.LicenceShare(site, r, now) <= Fx6.Zero)
                            continue;
                        shares++;
                        Assert.True(w.LicenceActive(a));
                        Assert.True(LicenceRules.Has(w.Policy.LicenceMask(a), w.IndustryLayout.Number[r]));
                        Assert.Equal(w.PolicyParameters.LicenceProcessShare, w.IndustryInputs.LicenceShare(site, r, now));
                        Assert.True(w.IndustryInputs.KnowsProcess(site, r, now));
                    }
                }
                Assert.Equal(Fx6.Zero, sum);   // L-M1
            }
            // ext_total follows the money that came from outside the actors' own budgets: the licences net out, the sale revenue stays (FL6 with L-M1).
            Fx6 ext = Fx6.Zero, income = Fx6.Zero;
            for (int a = 0; a < n; a++)
            {
                ext += w.Products.ExternalTotal(a);
                income += sale[a];
            }
            Assert.Equal(income, ext);
        }
        Assert.True(licences > 0 && disruptions > 0 && shares > 0, $"vacuous run: {licences} licences, {disruptions} disruptions, {shares} shares");
    }

    // ---- item 7 (E-LIC-0): while FRA has no project, the licence cannot move its culture

    [Fact]
    public void ELic0_CultureHOfFra_IsBitwiseEqualInMainAndLicOff_UntilTheFirstProject()
    {
        foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5 })
        {
            SliceSession main = Create(seed), off = Create(seed, new SliceRunConfig { LicOff = true });
            int fra = Actor(main, "FRA");
            string[] cols = [.. main.World.Table.Columns.Where(c => c.Name.StartsWith("ind.H.", StringComparison.Ordinal)).Select(c => c.Name)];
            for (int m = 0; m < 384; m++)
            {
                main.StepThroughMonth(m);
                off.StepThroughMonth(m);
                for (int d = 0; d < main.World.ProductsLayout.Designs; d++)
                {
                    if (main.World.Products.Stage(fra, d) != DesignStage.None)
                        goto next;
                }
                foreach (string c in cols)
                    Assert.True(Value(main, c, fra) == Value(off, c, fra), $"seed {seed} month {m}: {c} of FRA differs");
            }
        next:;
        }
    }

    private static long Value(SliceSession s, string column, int row) =>
        ((LongColumn)s.World.Table.Columns.First(c => c.Name == column)).Current[row];

    // ---- item 9: T6D_OFF is the T-fleet build, by projection

    // Hashes of the columns that existed before T6d, taken from the build of the commit before T6d (seed, month: all columns), for T6D_OFF.
    private static readonly (ulong Seed, int Month, string Hash)[] Before =
    [
        (1, 60, "700eff1cad875790be7c68d85ad3b83fcaa65475ad4923b627873841fd4a18a0"), (1, 180, "47fb7a39ba433f3c12d378cca80a5f10afe00f8ea1599d57d521d07b7aceb35e"),
        (1, 383, "58c133b4039da48f0d9c8041a3bbefa17e51acb5aa0de4eab9e012197ac12b8c"), (2, 60, "ce180c01071e8869f59d19682c9db22212664b0e9a531583a40fff752de82a4d"),
        (2, 180, "bc9fcca8d96aa41515659adfc90c3bb64d902a73b939495f8cfb3b8c7d1ebb71"), (2, 383, "3cd988a8be96953c4eadf38be38cfc88fde815329c67409c6344d5e4e4325769"),
        (3, 60, "897aca2063c954927248898140525164eed9bc9c17067a3490709e36aecf0ce8"), (3, 180, "74a76ef7a21350265a558d4ec2c2400d3a6cfe9aecc7f946985c8c81a7e6ddde"),
        (3, 383, "953bb22adaa9be30a87d72db4a43ddf580303cd681605dd437931294e77f2db3"), (7, 60, "2241efb9a5db73119a2088f1010353457d2721be0c1e42c1ec7339f8ca432c6d"),
        (7, 180, "d1296e7b68289090317e07dfa1f7cbe3dd5c9bb100c37d9f9baad9145bac2cd1"), (7, 383, "5d7da33caad96100121d24bc576161b75bba14a726dde8be9c7bc0fd290e92f5"),
    ];

    private static bool Existed(string name) =>
        !name.StartsWith("pol.lic.", StringComparison.Ordinal) && !name.StartsWith("pol.dis.", StringComparison.Ordinal) && name != "run.t6d";

    [Fact]
    public void T6dOff_GivesTheTrajectoryOfTheBuildBeforeT6d_ByProjectionOnTheOldColumns()
    {
        foreach (ulong seed in new ulong[] { 1, 2, 3, 7 })
        {
            SliceSession s = Create(seed, new SliceRunConfig { T6dOff = true });
            foreach (var (_, month, hash) in Before.Where(b => b.Seed == seed))
            {
                s.StepThroughMonth(month);
                Assert.True(hash == ColumnProjection.Hash(s, Existed), $"T6D_OFF seed {seed} month {month}: the old columns differ from the build before T6d");
            }
        }
    }

    [Fact]
    public void T6dOff_HasNoLicenceNoDisruptionAndNoFlow_AndLicOffKeepsTheDisruptions()
    {
        SliceSession off = Create(2, new SliceRunConfig { T6dOff = true }), lic = Create(2, new SliceRunConfig { LicOff = true });
        int n = off.World.Scenario.ActorCount;
        bool disrupted = false;
        for (int m = 0; m < 384; m++)
        {
            off.StepThroughMonth(m);
            lic.StepThroughMonth(m);
            for (int a = 0; a < n; a++)
            {
                Assert.Equal(0, off.World.Policy.LicenceGen(a));
                Assert.Equal(-1, off.World.Policy.Disruption(a).End);
                Assert.Equal(0, lic.World.Policy.LicenceGen(a));
                Assert.Equal(Fx6.Zero, lic.World.Policy.LicenceFlow(a));
                disrupted |= lic.World.Policy.Disruption(a).End >= 0;
            }
        }
        Assert.True(disrupted, "LIC_OFF must keep the alloy disruptions");
        Assert.NotEqual(off.LastHash, lic.LastHash);
    }

    // ---- item 8: lic_knows

    [Fact]
    public void LicKnows_ABuyerLearnsTheEngineAndTheProcessesOfTheMaskWithTheirClosure_NotTheAppNodesOfTheSeller()
    {
        for (ulong seed = 1; seed <= 40; seed++)
        {
            SliceSession s = Create(seed, new SliceRunConfig { LicKnows = true });
            SliceWorld w = s.World;
            int sun = Actor(s, "SUN");
            bool Knows(int node) => w.Knowledge.IsKnown(sun, w.Graph.RowOf(node));
            s.StepThroughMonth(0);
            Assert.False(Knows(39) && Knows(45) && Knows(49) && Knows(30) && Knows(33), "the nodes of the licence are not known before the merge of month 1");
            s.StepThroughMonth(1);
            if (w.Policy.LicenceGen(sun) == 0)
                continue;
            foreach (int node in (int[])[30, 33, 39, 45, 49])
                Assert.True(Knows(node), $"seed {seed}: node {node} is known to SUN after the licence");
            Assert.DoesNotContain(w.Knowledge.LastLicenceGrants, g => g.ActorNumber == sun && g.NodeNumber is 18 or 48 or 58);
            return;
        }
        Assert.Fail("no seed in 1…40 gave SUN a licence at month 0");
    }
}
