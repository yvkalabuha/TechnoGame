using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Policy;
using Singularity.Kernel;
using Xunit.Abstractions;

namespace Singularity.Session.Tests;

/// <summary>
/// TASK-0029 (D2, spec v0.19 §1.1, §11): the start rule R-H-MS′ as data, validator S-2, the <c>TOOLING_TARGET</c> switch on the assembled slice
/// and the <c>RHMS_OFF</c> data set.
/// </summary>
public class D2StartRuleTests(ITestOutputHelper output)
{
    private static readonly string[] Pair = ["USA", "GBR"];
    private static readonly int[] Mastered = [38, 39, 44, 45, 47, 48, 49, 53, 54];

    private static GameData OffData { get; } = ModLoader.Load(Path.Combine(SliceTestData.RepoRoot, "mods", "core"), Path.Combine(SliceTestData.RepoRoot, "mods", "rhms_off"));

    private static SliceRunConfig Off(ulong seed) => new() { Seed = seed, SkipStartValidation = true };

    // ---- data

    // actor, cluster, dimension, H (R-H-MS′), M: the table of spec §1.1, column R-H-MS′.
    private static readonly (string A, Cluster C, Dimension D, long H, long M)[] Cells =
    [
        ("USA", Cluster.MET, Dimension.Purity, 3_950_000, 4_150_000),
        ("USA", Cluster.CHEM, Dimension.Purity, 4_450_000, 4_650_000),
        ("USA", Cluster.MECH, Dimension.Precision, 4_475_000, 4_675_000),
        ("USA", Cluster.MECH, Dimension.QualityControl, 3_975_000, 4_175_000),
        ("USA", Cluster.AERO, Dimension.QualityControl, 3_950_000, 4_150_000),
        ("GBR", Cluster.MET, Dimension.Purity, 3_950_000, 4_150_000),
        ("GBR", Cluster.CHEM, Dimension.Purity, 4_450_000, 4_650_000),
        ("GBR", Cluster.MECH, Dimension.Precision, 4_475_000, 4_675_000),
        ("GBR", Cluster.MECH, Dimension.QualityControl, 3_975_000, 4_175_000),
        ("GBR", Cluster.AERO, Dimension.QualityControl, 3_950_000, 4_150_000),
        ("SUN", Cluster.CHEM, Dimension.Purity, 3_475_000, 3_675_000),
        ("SUN", Cluster.CHEM, Dimension.QualityControl, 2_975_000, 3_175_000),
        ("SUN", Cluster.MECH, Dimension.Precision, 3_475_000, 3_675_000),
        ("SUN", Cluster.MECH, Dimension.QualityControl, 2_975_000, 3_300_000),
        ("FRA", Cluster.CHEM, Dimension.Purity, 3_450_000, 3_650_000),
        ("FRA", Cluster.MECH, Dimension.Precision, 3_450_000, 3_650_000),
    ];

    [Fact]
    public void Data_SixteenCellsHaveTheRhmsPrimeValues_AllOtherCellsAndHcapAreUnchanged()
    {
        ScenarioData now = SliceTestData.Data.Scenario("jet_slice_1945"), before = OffData.Scenario("jet_slice_1945");
        Assert.NotEqual(now.Hash, before.Hash);
        int changed = 0;
        for (int a = 0; a < now.ActorCount; a++)
        {
            Assert.Equal(before.Hcap[a], now.Hcap[a]);
            foreach (Cluster c in now.Clusters)
            {
                foreach (Dimension d in Enum.GetValues<Dimension>())
                {
                    var cell = Cells.Where(x => x.A == now.ActorId[a] && x.C == c && x.D == d).ToArray();
                    if (cell.Length == 0)
                    {
                        Assert.Equal(before.CultureOf(a, c, d), now.CultureOf(a, c, d));
                        Assert.Equal(before.ToolingOf(a, c, d), now.ToolingOf(a, c, d));
                        continue;
                    }
                    changed++;
                    Assert.Equal(Fx6.FromRaw(cell[0].H), now.CultureOf(a, c, d));
                    Assert.Equal(Fx6.FromRaw(cell[0].M), now.ToolingOf(a, c, d));
                    // M = max(M, H + 0.2) and H = max(H, ...): neither is lowered.
                    Assert.True(now.CultureOf(a, c, d) >= before.CultureOf(a, c, d));
                    Assert.True(now.ToolingOf(a, c, d) >= before.ToolingOf(a, c, d));
                }
            }
        }
        Assert.Equal(16, changed);
        Assert.Equal(before.ActorCount, now.ActorCount);
    }

    // ---- validator S-2

    private static IndustryLayout LayoutOf(GameData data, ScenarioData s) => IndustryLayout.FromData(data.Nodes, s, SliceWorld.WorkforceCategories, 64);

    private static List<MasteredYieldViolation> Check(GameData data, string scenarioId = "jet_slice_1945")
    {
        ScenarioData s = data.Scenario(scenarioId);
        return StartYieldValidator.Check(s, data.Nodes, LayoutOf(data, s), new IndustryParams());
    }

    [Fact]
    public void S2_D2Data_NoViolations_AndEveryMasteredYieldAtLeastTheThreshold() => Assert.Empty(Check(SliceTestData.Data));

    [Fact]
    public void S2_RhmsOffData_ViolatesByDesign_AndTheSessionRefusesItUnlessSkipped()
    {
        Assert.NotEmpty(Check(OffData));
        DataLoadException e = Assert.Throws<DataLoadException>(() => SliceSession.Create(OffData, new SliceRunConfig { Seed = 1 }));
        Assert.Contains("S-2", e.Message);
        SliceSession s = SliceSession.Create(OffData, Off(1));
        s.StepThroughMonth(0);
    }

    private static string Edit(string[] pairs)
    {
        string dir = Path.Combine(Path.GetTempPath(), "singularity-d2-tests", Guid.NewGuid().ToString("N"), "core");
        Directory.CreateDirectory(dir);
        foreach (string file in Directory.GetFiles(Path.Combine(SliceTestData.RepoRoot, "mods", "core")))
        {
            string text = File.ReadAllText(file);
            if (Path.GetFileName(file) == "scenario_1945.yaml")
            {
                for (int i = 0; i < pairs.Length; i += 2)
                {
                    Assert.Contains(pairs[i], text);
                    text = text.Replace(pairs[i], pairs[i + 1]);
                }
            }
            File.WriteAllText(Path.Combine(dir, Path.GetFileName(file)), text);
        }
        return dir;
    }

    [Fact]
    public void S2_LiteralRhms155Fixture_GivesExactlyFourErrors_AtYield0672()
    {
        // The literal #155 (r_d − 0.05 for every dimension) of 49 (USA, GBR) and 44, 47 (SUN).
        string dir = Edit(
        [
            "MECH: {precision: 4.475, purity: 3.80, quality_control: 3.975}", "MECH: {precision: 4.45, purity: 3.80, quality_control: 3.95}",
            "MECH: {precision: 4.675, purity: 4.00, quality_control: 4.175}", "MECH: {precision: 4.65, purity: 4.00, quality_control: 4.15}",
            "MECH: {precision: 4.475, purity: 3.62, quality_control: 3.975}", "MECH: {precision: 4.45, purity: 3.62, quality_control: 3.95}",
            "MECH: {precision: 4.675, purity: 3.82, quality_control: 4.175}", "MECH: {precision: 4.65, purity: 3.82, quality_control: 4.15}",
            "CHEM: {precision: 3.20, purity: 3.475, quality_control: 2.975}", "CHEM: {precision: 3.20, purity: 3.45, quality_control: 2.95}",
            "MECH: {precision: 3.475, purity: 3.00, quality_control: 2.975}", "MECH: {precision: 3.45, purity: 3.00, quality_control: 2.95}",
            "CHEM: {precision: 3.40, purity: 3.675, quality_control: 3.175}", "CHEM: {precision: 3.40, purity: 3.65, quality_control: 3.15}",
            "MECH: {precision: 3.675, purity: 3.40, quality_control: 3.30}", "MECH: {precision: 3.65, purity: 3.40, quality_control: 3.30}",
        ]);
        GameData data = ModLoader.Load(dir);
        List<MasteredYieldViolation> bad = Check(data);
        Assert.Equal(["USA/49", "GBR/49", "SUN/44", "SUN/47"], bad.Select(v => $"{v.Actor}/{v.ProcessNumber}").ToArray());
        foreach (MasteredYieldViolation v in bad)
        {
            // 0.9025² · 0.825 = 0.6720 (±1 raw of Fx6 per floor in g and in the product; checked to 1e-5).
            Assert.InRange(v.Yield.Raw, 671_900, 672_100);
            Assert.True(v.WeakestG < Fx6.One);
            output.WriteLine($"{v.Actor} {v.ProcessId}: {v.Yield} {v.WeakestDimension} g {v.WeakestG}");
        }
        DataLoadException e = Assert.Throws<DataLoadException>(() => SliceSession.Create(data, new SliceRunConfig { Seed = 1 }));
        Assert.Contains("4 mastered process", e.Message);
        Assert.Contains("SUN", e.Message);
    }

    [Fact]
    public void S2_MasteredProcessWithHThreeTenthsBelowTheRequirement_IsALoadError()
    {
        string dir = Edit(["MET: {precision: 4.00, purity: 3.95, quality_control: 3.70}", "MET: {precision: 4.00, purity: 3.70, quality_control: 3.70}"]);
        GameData data = ModLoader.Load(dir);
        MasteredYieldViolation v = Assert.Single(Check(data));
        Assert.Equal(("USA", 39), (v.Actor, v.ProcessNumber));
        Assert.Equal(Dimension.Purity, v.WeakestDimension);
        Assert.True(v.Yield < StartYieldValidator.MinYield);
        Assert.Throws<DataLoadException>(() => SliceSession.Create(data, new SliceRunConfig { Seed = 1 }));
    }

    [Fact]
    public void S2_StartPert_ScalingTheCultureDoesNotTripTheValidator()
    {
        // The validator reads the scenario as authored (before START_PERT scales the estimated fields).
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 3, Start = StartVariant.Perturbed });
        s.StepThroughMonth(0);
    }

    // ---- start

    [Fact]
    public void Start_UsaAndGbrMasterEveryScenarioProcessAtT0_AndKeepItAfterMonthZero_AndG1IsFielded()
    {
        foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5 })
        {
            SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = seed });
            SliceWorld w = s.World;
            foreach (string id in Pair)
            {
                int a = w.Scenario.ActorIndex(id), site = w.IndustryLayout.HomeSite(a);
                foreach (int n in Mastered)
                    Assert.Equal(6, w.Industry.MasteredRun(site, w.IndustryLayout.RowOf(n)));
                Assert.Equal(1, w.Products.FieldedGen(a, 0));
            }
            s.StepThroughMonth(0);
            foreach (string id in Pair)
            {
                int a = w.Scenario.ActorIndex(id), site = w.IndustryLayout.HomeSite(a);
                // The processes of the G1 fleet's prc_set keep mastered_run (they produce); the others without a line may lapse by the model's own rule.
                int fielded = 0;
                for (int d = 0; d < w.ProductsLayout.Designs; d++)
                {
                    if (!w.Products.Fielded(a, d, 1))
                        continue;
                    fielded++;
                    foreach (int n in w.Products.PrcSet(a, d))
                        Assert.True(w.Industry.MasteredRun(site, w.IndustryLayout.RowOf(n)) >= 6, $"seed {seed} {id} {n}");
                }
                Assert.True(fielded >= 1 && w.Products.FieldedGen(a, 1) >= 1, $"seed {seed} {id}");
            }
        }
    }

    // ---- TOOLING_TARGET

    private static string HashAfter(SliceRunConfig c, int months)
    {
        SliceSession s = SliceTestData.Create(c);
        s.StepThroughMonth(months - 1);
        return s.LastHash;
    }

    [Fact]
    public void Switch_DefaultIsV0_ExplicitV0GivesTheSameTrajectory()
    {
        Assert.Equal(ToolingTarget.V0, new SliceRunConfig().ToolingTarget.Variant);
        var c = new SliceRunConfig { Seed = 1 };
        Assert.Equal(HashAfter(c, 36), HashAfter(c with { ToolingTarget = ToolingTargetSpec.Parse("V0") }, 36));
    }

    [Fact]
    public void Switch_OtherArmsChangeTheTrajectory_AndAreDeterministicAcrossThreads()
    {
        var c = new SliceRunConfig { Seed = 1 };
        string v0 = HashAfter(c, 60);
        foreach (string arm in new[] { "V1", "V2", "V3", "V3b", "V4", "VINF" })
        {
            var a1 = c with { ToolingTarget = ToolingTargetSpec.Parse(arm), Threads = 1 };
            string h1 = HashAfter(a1, 60), h4 = HashAfter(a1 with { Threads = 4, ChunkSize = 2 }, 60);
            Assert.Equal(h1, h4);
            Assert.NotEqual(v0, h1);
        }
    }

    [Fact]
    public void Switch_NumbersAreInTheHash()
    {
        var c = new SliceRunConfig { Seed = 1, ToolingTarget = ToolingTargetSpec.Parse("V4") };
        Assert.NotEqual(HashAfter(c, 2), HashAfter(c with { ToolingTarget = c.ToolingTarget with { MLead = Fx6.FromRaw(100_000) } }, 2));
    }

    [Fact]
    public void Switch_BadManifestIsRefusedByTheSession()
    {
        var bad = new SliceRunConfig { Seed = 1, ToolingTarget = ToolingTargetSpec.Parse("V4") with { MLead = Fx6.Zero } };
        Assert.Throws<ArgumentException>(() => SliceTestData.Create(bad));
        Assert.Throws<ArgumentException>(() => SliceTestData.Create(new SliceRunConfig { Seed = 1, ToolingTarget = new ToolingTargetSpec { Variant = (ToolingTarget)42 } }));
    }

    // ---- print (F32 and the acceptance print of TASK-0029); runs only with SINGULARITY_D2_PRINT=1

    [Fact]
    public void Print_Seeds1To20_AtV0_WithAndWithoutRhms()
    {
        if (Environment.GetEnvironmentVariable("SINGULARITY_D2_PRINT") != "1")
            return;
        string path = Environment.GetEnvironmentVariable("SINGULARITY_D2_PRINT_FILE") ?? Path.Combine(Path.GetTempPath(), "d2-print.txt");
        var lines = new List<string>();
        foreach ((string name, GameData data, bool skip) in new[] { ("D2", SliceTestData.Data, false), ("RHMS_OFF", OffData, true) })
        {
            lines.Add($"== {name}: first fielded month (G>=1) USA/GBR/SUN/FRA/SWE; G_front by 48-month windows (months with G_front>=1/>=2/max); mean yield of 49 and 54 (USA,GBR) by windows");
            for (ulong seed = 1; seed <= 20; seed++)
            {
                SliceSession s = SliceSession.Create(data, new SliceRunConfig { Seed = seed, SkipStartValidation = skip });
                SliceWorld w = s.World;
                int actors = w.Scenario.ActorCount;
                var first = Enumerable.Repeat(-1, actors).ToArray();
                var ge1 = new int[8]; var ge2 = new int[8]; var max = new int[8];
                var y = new long[2, 2, 8]; // [process 49/54, actor USA/GBR, window]
                int[] procs = [49, 54];
                for (long m = 0; m < 384; m++)
                {
                    s.StepThroughMonth(m);
                    int win = (int)(m / 48);
                    for (int a = 0; a < actors; a++)
                    {
                        if (first[a] < 0 && w.Products.FieldedGen(a, m) >= 1)
                            first[a] = (int)m;
                    }
                    int gf = w.Products.GFront(m);
                    if (gf >= 1) ge1[win]++;
                    if (gf >= 2) ge2[win]++;
                    max[win] = Math.Max(max[win], gf);
                    for (int p = 0; p < 2; p++)
                    {
                        for (int k = 0; k < 2; k++)
                        {
                            int a = w.Scenario.ActorIndex(Pair[k]);
                            y[p, k, win] += w.Industry.ProcYield(w.IndustryLayout.HomeSite(a), w.IndustryLayout.RowOf(procs[p])).Raw;
                        }
                    }
                }
                string yl = string.Join(" | ", Enumerable.Range(0, 2).SelectMany(p => Enumerable.Range(0, 2).Select(k =>
                    $"{procs[p]}/{Pair[k]}:" + string.Join(",", Enumerable.Range(0, 8).Select(win => (y[p, k, win] / 48 / 10_000 / 100.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))))));
                lines.Add($"seed {seed}: first [{string.Join(",", first)}] gfront>=1 [{string.Join(",", ge1)}] >=2 [{string.Join(",", ge2)}] max [{string.Join(",", max)}] yield {yl}");
            }
        }
        File.WriteAllLines(path, lines);
        output.WriteLine(string.Join("\n", lines));
    }
}
