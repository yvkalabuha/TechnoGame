using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Core.WhyNot;
using Singularity.Kernel;
using Xunit.Abstractions;

namespace Singularity.Session.Tests;

/// <summary>
/// TASK-0027 (T6c2, spec v0.15 §11): E1 (the start line of USA/GBR), W1 (the nearest node of <c>why_not</c> inside the closure of the
/// cheapest alternative) on the assembled slice, plus the information print of the acceptance (seeds 1…20, 384 months).
/// </summary>
public class T6c2Tests(ITestOutputHelper output)
{
    private static SliceSession New(SliceRunConfig? config = null) => SliceTestData.Create(config);

    private static int Actor(SliceSession s, string id) => s.World.Scenario.ActorIndex(id);

    private static void SetLong(SliceSession s, string name, int row, long value)
    {
        LongColumn c = s.World.Table.GetLong(name);
        long[] all = c.Current.ToArray();
        all[row] = value;
        c.Load(all);
    }

    private static void SetInt(SliceSession s, string name, int row, int value)
    {
        IntColumn c = s.World.Table.GetInt(name);
        int[] all = c.Current.ToArray();
        all[row] = value;
        c.Load(all);
    }

    private static int Number(SliceSession s, string id) => s.World.Data.Nodes.Number[s.World.Data.Nodes.IndexOf(id)];

    // ---- E1

    [Fact]
    public void E1_T0_UsaAndGbrHoldTheirFleetAsALineInStageC_OthersHaveNoFleet()
    {
        SliceSession s = New();
        SliceWorld w = s.World;
        ProductsSystem p = w.Products;
        foreach (string id in (string[])["USA", "GBR"])
        {
            int a = Actor(s, id);
            Assert.Equal(DesignStage.Series, p.Stage(a, 0));
            Assert.Equal(1, p.PlatformGen(a, 0));
            Assert.Equal(["eng_g1", "hot_nicr_forged", "comp_centrifugal", "af_straight", "ab_none"],
                p.Alternatives(a, 0).Select(x => w.ProductsLayout.AltId[x]));
            Assert.True(p.BuiltAny(a, 0));
            Assert.Equal(0, p.IdleMonths(a, 0));
            Assert.True(p.GateOpened(a, 0));
            Assert.Equal(Fx6.Zero, p.DGate(a, 0));
            Assert.Equal(Fx6.Zero, p.InitialDefects(a, 0));
            Assert.Equal(-1, p.StageOMonth(a, 0));
            Assert.False(p.SeriesForced(a, 0));
            Assert.False(p.Cancelled(a, 0));
            Assert.Equal(Fx6.FromInt(100), p.LineE(a, 0));
            Assert.Equal(Fx6.FromInt(100), p.Cohort(a, 0, 1945));
            Assert.True(w.Policy.AiLine(a, 0));
            for (int d = 1; d < w.ProductsLayout.Designs; d++)
                Assert.Equal(DesignStage.None, p.Stage(a, d));
        }
        foreach (string id in (string[])["SUN", "FRA", "SWE"])
        {
            for (int d = 0; d < w.ProductsLayout.Designs; d++)
                Assert.Equal(DesignStage.None, p.Stage(Actor(s, id), d));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void E1_Tick0_TheLineProducesAtRAi_ForTheAiAndForThePlayerScriptRun(bool productAi)
    {
        SliceSession s = New(new SliceRunConfig { Seed = 3, ProductAi = productAi });
        SliceWorld w = s.World;
        int usa = Actor(s, "USA");
        // r_AI(0) = ⌊B_proc(0)/unit_cost_floor⌋ by the shares chosen in the initialisation phase.
        Fx6 expected = PolicyRules.LineRate(w.SharesOf(usa).Procurement * w.Policy.Budget(usa), w.Products.UnitCostFloor(usa, 0));
        Assert.True(expected > Fx6.Zero);
        s.StepThroughMonth(0);
        Fx6 rate = w.Products.Rate(usa, 0);
        Assert.True(rate > Fx6.Zero, "the USA line produces in tick 0");
        Assert.True(rate <= expected);
        Assert.Equal(Fx6.FromInt(100) + rate, w.Products.InService(usa, 0));
    }

    [Fact]
    public void E1_NoRequirementForG1_WhileTheFleetLineLives_AndNoStartRequirementCommand()
    {
        SliceSession s = New(new SliceRunConfig { Seed = 5 });
        SliceWorld w = s.World;
        for (long t = 0; t < 60; t++)
        {
            s.StepThroughMonth(t);
            foreach (string id in (string[])["USA", "GBR"])
            {
                int a = Actor(s, id);
                Assert.Equal(DesignStage.Series, w.Products.Stage(a, 0));
                Assert.DoesNotContain(w.Policy.AiDesignCommands(a, t + 1), c => c.Kind == DesignCommandKind.Compose && c.Target == 1);
            }
        }
    }

    [Fact]
    public void E1_L_C3a_RejectsAManifestWhoseStartUpkeepIsNotBelowTheProcurementItem()
    {
        var policy = new PolicyParams();
        Fx6[] budget = [Fx6.FromInt(10), Fx6.FromInt(10)];
        PolicyLiveness.ValidateStartUpkeep(policy, [Fx6.FromInt(1), Fx6.Zero], budget);
        var ex = Assert.Throws<LivenessException>(() => PolicyLiveness.ValidateStartUpkeep(policy, [Fx6.FromInt(1), Fx6.FromInt(10)], budget));
        Assert.Equal("L-C3a", ex.Code);
        // The share is the smallest procurement share of any profile: need just below a smaller share passes, at it fails.
        Fx6 share = Fx6.One;
        foreach (BudgetShares b in policy.SharesByProfile.Append(policy.MassProgrammeShares))
            share = b.Procurement < share ? b.Procurement : share;
        Assert.Throws<LivenessException>(() => PolicyLiveness.ValidateStartUpkeep(policy, [share * budget[0], Fx6.Zero], budget));
        PolicyLiveness.ValidateStartUpkeep(policy, [share * budget[0] - Fx6.FromRaw(1), Fx6.Zero], budget);
    }

    // ---- W1

    [Fact]
    public void W1_Sun_T0_TheCompressorSlotLinkIsNode48_Not19()
    {
        SliceSession s = New();
        int sun = Actor(s, "SUN");
        int centrifugalPrc = Number(s, "s6.prc.centrifugal_impeller_machining");
        Assert.Equal(48, centrifugalPrc);
        // SUN knows turbojet_g2's neighbourhood only through node 19, which the old rule chose.
        var links = s.WhyNot(sun, WhyNotTarget.Requirement(1));
        Link first = links.First(l => l.Layer == WhyNotLayer.Knowledge && l.Cause == WhyNotCause.AltUnavailable);
        Assert.Equal(48, first.SubjectId);
        Assert.DoesNotContain(links, l => l.Cause == WhyNotCause.AltUnavailable && l.SubjectId == 19);
    }

    [Fact]
    public void W1_ACheapFrontierNodeBehindAnExpensiveClosure_DoesNotWin()
    {
        SliceSession s = New();
        int sun = Actor(s, "SUN");
        int axialGroupNode = Number(s, "s8.eng.turbojet_g2");
        // The node of the axial closure on the frontier is nearly done (left ≈ 0): the old rule would link it.
        SetLong(s, $"know.prog.{axialGroupNode}", sun, Fx6.FromRaw(990_000).Raw);
        var links = s.WhyNot(sun, WhyNotTarget.Requirement(1));
        Link first = links.First(l => l.Cause == WhyNotCause.AltUnavailable);
        Assert.Equal(48, first.SubjectId);
    }

    [Fact]
    public void W1_AnAnyOfGroup_TakesTheMemberWithTheSmallerClosureCost_NotTheSmallerNumber()
    {
        int area = Number(SliceTestData.Create(), "s8.eng.area_ruled_airframe"), delta = Number(SliceTestData.Create(), "s8.eng.delta_wing_airframe");
        foreach ((int cheap, int dear) in new[] { (area, delta), (delta, area) })
        {
            SliceSession s = New();
            int usa = Actor(s, "USA");
            foreach (Column col in s.World.Table.Columns)
            {
                if (col.Name.StartsWith("know.known.", StringComparison.Ordinal))
                    SetInt(s, col.Name, usa, 1);
            }
            // The group members are the only unknown nodes of the airframe slot for generation 3; the one closer to done must be linked.
            // af_mach2 is out of the way as well (its own node unknown and far), so that the slot has no available alternative for generation 3.
            foreach (int n in new[] { area, delta, Number(s, "s8.eng.mach2_airframe_g4") })
                SetInt(s, $"know.known.{n}", usa, 0);
            SetLong(s, $"know.prog.{cheap}", usa, Fx6.FromRaw(800_000).Raw);
            SetLong(s, $"know.prog.{dear}", usa, 0);
            var links = s.WhyNot(usa, WhyNotTarget.Requirement(3));
            Link first = links.First(l => l.Cause == WhyNotCause.AltUnavailable);
            Assert.Equal(cheap, first.SubjectId);
        }
    }

    [Fact]
    public void W1_APPTarget_UnchangedFromTheCodeBeforeW1_OnTheD1Scenario()
    {
        SliceSession s = New();
        // SubjectId of the first link, taken from the code of 2b1873b^ (before W1): without any_of groups in the APP closure W1 changes nothing.
        (string Actor, int Gen, int Node)[] pinned =
        [
            ("USA", 2, 19), ("USA", 3, 5), ("USA", 4, 5), ("GBR", 2, 20), ("GBR", 3, 5), ("GBR", 4, 5), ("SUN", 2, 19), ("SUN", 3, 11), ("SUN", 4, 5),
            ("FRA", 2, 10), ("FRA", 3, 11), ("FRA", 4, 10), ("SWE", 2, 17), ("SWE", 3, 17), ("SWE", 4, 17),
        ];
        foreach ((string actor, int gen, int node) in pinned)
        {
            var links = s.WhyNot(Actor(s, actor), WhyNotTarget.Requirement(gen));
            Assert.Equal(WhyNotCause.MissingNode, links[0].Cause);
            Assert.Equal(node, links[0].SubjectId);
            Assert.Equal(Render(links), Render(s.WhyNot(Actor(s, actor), WhyNotTarget.Requirement(gen))));   // pure
        }
    }

    private static string Render(IEnumerable<Link> links) => string.Join("\n", links.Select(l => $"{l.Layer}|{l.Cause}|{l.SubjectId}|{l.Have}|{l.Need}"));

    [Fact]
    public void L_C3a_SliceWorldAssembly_RejectsAScenarioWhoseStartFleetUpkeepIsTooHigh()
    {
        // 1,3 million units in the 1945 cohorts of a 100-unit fleet make the upkeep of the start fleet exceed the procurement item at t0.
        string dir = Path.Combine(Path.GetTempPath(), "t6c2-lc3a-" + Guid.NewGuid().ToString("N"));
        CopyDir(Path.Combine(SliceTestData.RepoRoot, "mods", "core"), dir);
        try
        {
            string file = Path.Combine(dir, "scenario_1945.yaml");
            File.WriteAllText(file, File.ReadAllText(file).Replace("count: 100, cohort_year: 1945", "count: 100000, cohort_year: 1945"));
            var data = Singularity.Core.Data.ModLoader.Load(dir);
            var ex = Assert.Throws<LivenessException>(() => SliceSession.Create(data, new SliceRunConfig { Seed = 1 }));
            Assert.Equal("L-C3a", ex.Code);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string f in Directory.GetFiles(from))
            File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (string d in Directory.GetDirectories(from))
            CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }

    // ---- the information print of the acceptance (not a check of values)

    private static string Pct(List<long> sorted, int pct) => sorted.Count == 0 ? "-" : sorted[Math.Min(sorted.Count - 1, sorted.Count * pct / 100)].ToString();

    [Fact]
    public void Print_Seeds1To20_384Months_OutputWindows_UpkeepShare_GiveUpClasses_SunFirstSerialG1()
    {
        const int seeds = 20, months = 384, window = 48;
        string[] ids = [];
        var outputs = new List<long>[5, months / window];            // per actor and window: the sum of the line rates, raw/1e6 units, per seed
        var upkeep = new Dictionary<PolicyProfile, List<long>>();     // u in permille: months of actors with a fleet, by profile
        var upkeepMax = new Dictionary<PolicyProfile, long>();
        var oClass = new int[2];
        var oByActor = new int[5, 2];
        var cByActor = new int[5];
        var sunFirst = new List<long>();
        int sunNever = 0;
        int startsNotAdmitted = 0;
        var twoLines = new long[5];                                   // F17b: months of two-line coexistence per actor, summed over seeds
        var closeYProd = new int[5];                                  // F17b rule 2: the working line closed with no launch in the month
        var closeBundle = new int[5];                                 // closed in the launch bundle (replace or y_min ≥ y_prod)
        for (int a = 0; a < 5; a++)
        {
            for (int k = 0; k < months / window; k++)
                outputs[a, k] = new List<long>();
        }
        foreach (PolicyProfile profile in Enum.GetValues<PolicyProfile>())
        {
            upkeep[profile] = new List<long>();
            upkeepMax[profile] = 0;
        }
        for (ulong seed = 1; seed <= seeds; seed++)
        {
            SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = seed, Threads = 4, ChunkSize = 2 });
            SliceWorld w = s.World;
            int n = w.Scenario.ActorCount, designs = w.ProductsLayout.Designs;
            ids = [.. w.Scenario.ActorId];
            int sun = Actor(s, "SUN");
            var sum = new long[n, months / window];
            long sunMonth = -1;
            var adapter = new PolicyProductsAdapter(w);
            var cls = new GiveUpClass[n, designs];
            var admitted = new bool[n, designs];
            var idle = new int[n, designs];
            var stage = new DesignStage[n, designs];
            for (long t = 0; t < months; t++)
            {
                for (int a = 0; a < n; a++)
                {
                    for (int d = 0; d < designs; d++)
                    {
                        stage[a, d] = w.Products.Stage(a, d);
                        idle[a, d] = w.Products.IdleMonths(a, d);
                        if (stage[a, d] == DesignStage.Prototype)
                        {
                            cls[a, d] = w.Policy.ClassifyGiveUp(a, d, t);
                            admitted[a, d] = adapter.Admitted(a, d, t);
                        }
                    }
                }
                s.StepThroughMonth(t);
                for (int a = 0; a < n; a++)
                {
                    Fx6 fleet = Fx6.Zero;
                    for (int d = 0; d < designs; d++)
                    {
                        sum[a, t / window] += w.Products.Rate(a, d).Raw;
                        fleet += w.Products.InService(a, d);
                        if (a == sun && sunMonth < 0 && w.Products.PlatformGen(a, d) == 1 && w.Products.Stage(a, d) == DesignStage.Series
                            && w.Products.Rate(a, d) > Fx6.Zero)
                            sunMonth = t;
                        if (w.Policy.AiStart(a, d, t + 1) is not null && !admitted[a, d])
                            startsNotAdmitted++;
                        if (w.Policy.AiClose(a, d, t + 1) && stage[a, d] == DesignStage.Series && idle[a, d] >= 60)
                            cByActor[a]++;
                        else if (w.Policy.AiClose(a, d, t + 1) && stage[a, d] == DesignStage.Series)
                        {
                            bool launch = false;
                            for (int e = 0; e < designs; e++)
                                launch |= w.Policy.AiStart(a, e, t + 1) is not null;
                            if (launch)
                                closeBundle[a]++;
                            else
                                closeYProd[a]++;
                        }
                    }
                    int seriesNow = 0;
                    for (int d = 0; d < designs; d++)
                        seriesNow += w.Products.Stage(a, d) == DesignStage.Series ? 1 : 0;
                    if (seriesNow == 2)
                        twoLines[a]++;
                    foreach (DesignCommand c in w.Policy.AiDesignCommands(a, t + 1))
                    {
                        if (c.Kind != DesignCommandKind.Cancel)
                            continue;
                        int k = (int)cls[a, c.Target];
                        oClass[k]++;
                        oByActor[a, k]++;
                    }
                    Fx6 procurement = w.ProcurementBudget(a);
                    if (fleet > Fx6.Zero && procurement > Fx6.Zero)
                    {
                        long u = w.Products.UpkeepNeeded(a).Raw * 1000 / procurement.Raw;
                        PolicyProfile pr = w.Policy.Profile(a);
                        upkeep[pr].Add(u);
                        upkeepMax[pr] = Math.Max(upkeepMax[pr], u);
                    }
                }
            }
            for (int a = 0; a < n; a++)
            {
                for (int k = 0; k < months / window; k++)
                    outputs[a, k].Add(sum[a, k] / Fx6.Scale);
            }
            if (sunMonth >= 0)
                sunFirst.Add(sunMonth);
            else
                sunNever++;
        }
        output.WriteLine("Output by 48-month windows (units; p50 over seeds 1…20 | p10-p90 of the last window), AI A41, E1/F17/#140/W1:");
        for (int a = 0; a < 5; a++)
        {
            output.WriteLine($"{ids[a],-4} " + string.Join(" ", Enumerable.Range(0, months / window).Select(k =>
            {
                var l = outputs[a, k].Order().ToList();
                return $"{Pct(l, 50),6}";
            })) + "   zero-output seeds per window: " + string.Join(" ", Enumerable.Range(0, months / window).Select(k => outputs[a, k].Count(x => x == 0))));
        }
        output.WriteLine("Upkeep share of procurement u (permille, months of actors with a fleet), by profile: p50 / p90 / max");
        foreach (PolicyProfile pr in Enum.GetValues<PolicyProfile>())
        {
            var l = upkeep[pr].Order().ToList();
            output.WriteLine($"{pr,-9} n={l.Count,6}  {Pct(l, 50),5} / {Pct(l, 90),5} / {upkeepMax[pr],5}");
        }
        output.WriteLine($"A40 branch О by class: processes {oClass[(int)GiveUpClass.Processes]}, defects {oClass[(int)GiveUpClass.Defects]}; per actor "
            + string.Join(", ", Enumerable.Range(0, 5).Select(a => $"{ids[a]} {oByActor[a, 1]}/{oByActor[a, 0]}")) + " (processes/defects)");
        output.WriteLine("A40 branch С (close_line at idle_c ≥ 60) per actor: " + string.Join(", ", Enumerable.Range(0, 5).Select(a => $"{ids[a]} {cByActor[a]}")));
        output.WriteLine("F17b months of two-line coexistence (sum over seeds 1…20): " + string.Join(", ", Enumerable.Range(0, 5).Select(a => $"{ids[a]} {twoLines[a]}")));
        output.WriteLine("F17b closes by y_prod (no launch in the month): " + string.Join(", ", Enumerable.Range(0, 5).Select(a => $"{ids[a]} {closeYProd[a]}"))
            + "; in the launch bundle: " + string.Join(", ", Enumerable.Range(0, 5).Select(a => $"{ids[a]} {closeBundle[a]}")));
        var sorted = sunFirst.Order().ToList();
        output.WriteLine($"SUN first serial G1 (month with output > 0): p10 {Pct(sorted, 10)}, p50 {Pct(sorted, 50)}, p90 {Pct(sorted, 90)}, never {sunNever}/{seeds}");
        Assert.Equal(0, startsNotAdmitted);
        Assert.Equal(0, oClass[(int)GiveUpClass.Defects]);
        // USA and GBR produce G1 in the first window in every seed (K3, E1).
        Assert.All(new[] { Actor(SliceTestData.Create(), "USA"), Actor(SliceTestData.Create(), "GBR") }, a => Assert.All(outputs[a, 0], x => Assert.True(x > 0)));
    }
}
