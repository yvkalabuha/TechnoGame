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

    // Hashes of the columns that existed before T6d, taken from the build of the commit before T6d (seed, month: all columns but run.scenario, whose hash changed with «exposure to data»), for T6D_OFF.
    private static readonly (ulong Seed, int Month, string Hash)[] Before =
    [
        (1, 60, "9dba9bd804113b16b56a0e799c88544d742cacd843e836ff2a9969633632f6f0"), (1, 180, "252f662a5c5af1e15e0e23e77330bed86e786272aa9a3112874cfdd732e97653"),
        (1, 383, "f3acebd02cf048c19f16fdbd85df0ddde46c073b3d17af0b3c4a583b4aaabcc1"), (2, 60, "59d7a8278bee7c76853af0ea86c25a8818f3e352ab326ce6f7e6e7ba5737acb1"),
        (2, 180, "b6d9b295210df5b4900f33b8c7681e5af5d7c16a46eeb5923e7cb133eb8ee413"), (2, 383, "dc180a0b161f965145cd17897a113b09fa7a162cfa877bd91a1bb4431c28bb88"),
        (3, 60, "4eeef32ed7491d595c090b281f46cb4c6a1eefb828b2d5f1ce83d93a6283d2ca"), (3, 180, "26df03dbd3b5b7247053f14e53f8b7d0bc5e2f712c6c766a360dfcfe1782ab95"),
        (3, 383, "84adbe52ec14e5617957f638bad0300fbeaecefe9f41be8b2dace15ef80daed3"), (7, 60, "4ad56b2006514023d7286531bbc7df2251cc136ddd5a06b095c09afd7f49ad73"),
        (7, 180, "af2f6158e9c86131ca1eb4f7593d44ed3cc1d581024ce2437980d4276eeabd1b"), (7, 383, "786ab86ceebdc501d221378ff0919deadd4fbb864cdc9afb3c9cf1ab600d8ecb"),
    ];

    private static bool Existed(string name) =>
        !name.StartsWith("pol.lic.", StringComparison.Ordinal) && !name.StartsWith("pol.dis.", StringComparison.Ordinal) && name != "run.t6d" && name != "run.scenario";   // run.scenario: the scenario hash changed with «exposure to data» (TASK-0033)

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
