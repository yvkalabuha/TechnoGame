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

    // Re-recorded in TASK-0058 step 5, reason: I-OC + A41 SLOT as the build, fielded_L and prd.d*.q (#242–#247): these are now hashes of the new build's old columns under T6D_OFF, no longer equal to the build before T6d.
    // Hashes of the columns that existed before T6d, taken from the build of the commit before T6d (seed, month: all columns but run.scenario, whose hash changed with «exposure to data»), for T6D_OFF.
    private static readonly (ulong Seed, int Month, string Hash)[] Before =
    [
        (1, 60, "fea45cb3aa39eef44c9e96fdb00bc18b6fd4e2c898b1f385b90e8e94282ae940"), (1, 180, "9161a5df6618fe6605da17e2afc6d298f0311103cb55619e60097d88f01db03d"),
        (1, 383, "8a11dfe6664809bf8418cf5affa52cc47e76a1e82c0d44b96b7f9a40891857da"), (2, 60, "d1f6b6a3d3834dd428840fdfceb2b80f98b91e5232c5d3f99796fc55ee4a8dcd"),
        (2, 180, "0c2e72d58b73f1c5012aaee5e2358af5561ccfda84419514649951e932598aa8"), (2, 383, "facde3433d9d12b26e3d73a906690ec96aeaa5c59c9bc9212aff51324f4c1586"),
        (3, 60, "a190c568b98b8ed2979031f20f8ef0115eb836dc84c59168848b7ce73e8cee86"), (3, 180, "9ef0d73b273eeb2b1275af5344db49ac1453e0b4f3ca6067ebdf8558728bd8cb"),
        (3, 383, "89b88dcef20fe65dbf7b8ca924d973de17929aaf805bd70b4b5a5ea7dbd728fa"), (7, 60, "1a2b0677ad31df81f664bc624fc3c3a76e0f23935ddfe1ad5d068d549595d3ee"),
        (7, 180, "63f8516782d3ba1b55d190dff4ae82baf3a065042e3b3be924317c3baa62454b"), (7, 383, "0aa8c7eb749e8877f8be8c292f1fcb58fa64d345027d8c785be690470c7d78c5"),
    ];

    private static bool Existed(string name) =>
        !ColumnProjection.IsSlotGenerationColumn(name) && !name.StartsWith("pol.lic.", StringComparison.Ordinal) && !name.StartsWith("pol.dis.", StringComparison.Ordinal) && name != "run.t6d" && name != "run.scenario";   // run.scenario: the scenario hash changed with «exposure to data» (TASK-0033)

    [Fact]
    public void T6dOff_GivesTheTrajectoryOfTheBuildBeforeT6d_ByProjectionOnTheOldColumns()
    {
        foreach (ulong seed in new ulong[] { 1, 2, 3, 7 })
        {
            SliceSession s = Create(seed, new SliceRunConfig { T6dOff = true });
            foreach (var (_, month, hash) in Before.Where(b => b.Seed == seed))
            {
                s.StepThroughMonth(month);
                if (ColumnProjection.SlotReused(s))
                    break;   // the build before T6d never retook a slot (TASK-0099)
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
        HashAssert.NotEqual(off.LastHash, lic.LastHash);
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
