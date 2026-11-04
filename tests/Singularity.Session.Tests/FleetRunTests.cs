using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;
using Xunit.Abstractions;

namespace Singularity.Session.Tests;

/// <summary>
/// TASK-0028 (T-fleet, spec §11 п. 1–6, §6.10): seeds 1…20 for 384 months with the rule on, month by month — the P0 checks as properties —
/// and the information print of §6.10 (E-gen, R2″, sales, «парк на keep», Δ, windows, u). The print is information, not acceptance.
/// </summary>
public class FleetRunTests(ITestOutputHelper output)
{
    private const int Seeds = 20;

    private static readonly Lazy<FleetRunStats[]> Main = new(() => Runs(new SliceRunConfig()));
    private static readonly Lazy<FleetRunStats[]> NoRetire = new(() => Runs(new SliceRunConfig { FleetArm = FleetArm.NoRetire }));
    private static readonly Lazy<FleetRunStats[]> NoSale = new(() => Runs(new SliceRunConfig { FleetArm = FleetArm.NoSale }));
    private static readonly Lazy<FleetRunStats[]> PurchaseCap = new(() => Runs(new SliceRunConfig { FleetArm = FleetArm.PurchaseCap }));

    private static FleetRunStats[] Runs(SliceRunConfig template)
    {
        var runs = new FleetRunStats[Seeds];
        Parallel.For(0, Seeds, i => runs[i] = FleetRunStats.Run((ulong)(i + 1), template));
        return runs;
    }

    [Fact]
    public void Seeds1To20_Main_NoCheckIsViolated_AndNoCommandOfTheAiIsRefused()
    {
        foreach (FleetRunStats r in Main.Value)
            Assert.True(r.Violations.Count == 0, $"seed {r.Seed}: {r.Violations.Count} violations, first: {r.Violations.FirstOrDefault()}");
    }

    [Fact]
    public void Seeds1To20_NoSaleArm_NeverSells_AndPurchaseCapIssuesNoCommand()
    {
        foreach (FleetRunStats r in NoSale.Value)
        {
            Assert.True(r.Violations.Count == 0, $"seed {r.Seed}: {r.Violations.FirstOrDefault()}");
            Assert.All(r.SaleIncomeTotal, x => Assert.Equal(0, x));
        }
        foreach (FleetRunStats r in PurchaseCap.Value.Concat(NoRetire.Value))
        {
            Assert.All(r.Sold, x => Assert.Equal(0, x));
            Assert.All(r.Retired, x => Assert.Equal(0, x));
        }
    }

    private static long Pct(List<long> sorted, int p) => sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, sorted.Count * p / 100)];

    private static List<long> Merge(IEnumerable<FleetRunStats> runs, Func<FleetRunStats, IEnumerable<long>> pick) => [.. runs.SelectMany(pick).Order()];

    [Fact]
    public void Print_Seeds1To20_Section610_Information()
    {
        FleetRunStats[] main = Main.Value, none = NoRetire.Value, nosale = NoSale.Value, cap = PurchaseCap.Value;
        string[] ids = main[0].ActorIds;
        int n = ids.Length;

        output.WriteLine("== Output by 48-month windows (units, p50 over seeds 1…20): main | NO_RETIRE");
        for (int a = 0; a < n; a++)
        {
            string Row(FleetRunStats[] set) => string.Join(" ", Enumerable.Range(0, FleetRunStats.Windows)
                .Select(k => $"{Pct(Merge(set, r => [r.Output[a, k] / Fx6.Scale]), 50),6}"));
            output.WriteLine($"{ids[a],-4} {Row(main)}   |   {Row(none)}");
        }

        output.WriteLine("== u (permille of ProcurementBase, months with a fleet) by profile: p50 / p90 / max, main | NO_RETIRE");
        foreach (PolicyProfile pr in Enum.GetValues<PolicyProfile>())
        {
            string Row(FleetRunStats[] set)
            {
                var l = Merge(set, r => Enumerable.Range(0, n).Where(a => r.Profiles[a] == pr).SelectMany(a => Enumerable.Range(0, FleetRunStats.Windows).SelectMany(k => r.UPermille[a, k])));
                return $"n={l.Count,6} {Pct(l, 50),4} / {Pct(l, 90),4} / {(l.Count == 0 ? 0 : l[^1]),4}";
            }
            output.WriteLine($"{pr,-9} {Row(main)}   |   {Row(none)}");
        }

        output.WriteLine("== R2″: median u (permille) of USA with P1 or P3, windows 1–2 (months 48–143) and 6–7 (288–383), per seed pairs main vs NO_RETIRE");
        int usa = 0;
        foreach (int[] win in new[] { new[] { 1, 2 }, new[] { 6, 7 } })
        {
            var diffs = new List<long>();
            for (int i = 0; i < Seeds; i++)
            {
                if (main[i].Profiles[usa] == PolicyProfile.Mass)
                    continue;
                long Med(FleetRunStats r) => Pct([.. win.SelectMany(k => r.UPermille[usa, k]).Order()], 50);
                diffs.Add(Med(main[i]) - Med(none[i]));
            }
            diffs.Sort();
            output.WriteLine($"windows {string.Join("–", win)}: seeds {diffs.Count}, median of (main − NO_RETIRE) = {Pct(diffs, 50)}‰, mean = {(diffs.Count == 0 ? 0 : diffs.Sum() / diffs.Count)}‰");
            var out7 = new List<long>();
            for (int i = 0; i < Seeds; i++)
            {
                if (main[i].Profiles[usa] != PolicyProfile.Mass)
                    out7.Add((main[i].Output[usa, win[^1]] - none[i].Output[usa, win[^1]]) / Fx6.Scale);
            }
            out7.Sort();
            output.WriteLine($"   USA output, last window: median of (main − NO_RETIRE) = {Pct(out7, 50)} units");
        }

        output.WriteLine("== E-gen: AI commands by (gen(L), gen(d)), seeds 1…20, main");
        long total = 0;
        for (int gl = 1; gl <= 4; gl++)
        {
            output.WriteLine($"gen(L) {gl}: " + string.Join("  ", Enumerable.Range(1, 4).Select(gd => $"d{gd}:{main.Sum(r => r.CommandsByPair[gl, gd])}")));
            total += Enumerable.Range(1, 4).Sum(gd => main.Sum(r => r.CommandsByPair[gl, gd]));
        }
        long highLine = Enumerable.Range(3, 2).Sum(gl => Enumerable.Range(1, 4).Sum(gd => main.Sum(r => r.CommandsByPair[gl, gd])));
        output.WriteLine($"commands {total}; with gen(L) ≥ 3: {highLine}; pair (2,1): {main.Sum(r => r.CommandsByPair[2, 1])}");
        output.WriteLine($"armed months (u ≥ U_HI with a line) {main.Sum(r => r.Armed.Sum())}, silent because line_bind != MONEY {main.Sum(r => r.SilentByBind.Sum())};"
            + " USA in windows 6–7: armed " + main.Sum(r => r.ArmedByWindow[usa, 6] + r.ArmedByWindow[usa, 7]) + ", silent " + main.Sum(r => r.SilentByWindow[usa, 6] + r.SilentByWindow[usa, 7]));

        output.WriteLine("== R3″ and sales");
        int withCmd = 0, eligible = 0;
        for (int i = 0; i < Seeds; i++)
        {
            for (int a = 0; a < 2; a++)
            {
                if (main[i].Profiles[a] != PolicyProfile.Mass)
                {
                    eligible++;
                    if (main[i].Sold[a] + main[i].Retired[a] > 0)
                        withCmd++;
                }
            }
        }
        output.WriteLine($"actors USA/GBR with P1 or P3 and ≥ 1 command: {withCmd}/{eligible}; P2 actors with a command: "
            + $"{Enumerable.Range(0, Seeds).Sum(i => Enumerable.Range(0, 2).Count(a => main[i].Profiles[a] == PolicyProfile.Mass && main[i].Sold[a] + main[i].Retired[a] > 0))}");
        output.WriteLine($"seeds with ≥ 1 sell_cohort by USA or GBR: {main.Count(r => r.SaleIncomeTotal[0] + r.SaleIncomeTotal[1] > 0)}/{Seeds}; "
            + $"sold units by gen(d) 1…4: {string.Join(" ", Enumerable.Range(1, 4).Select(g => main.Sum(r => r.SoldByGen[g])))}; "
            + $"seeds with sold gen ≥ 2: {main.Count(r => r.SoldByGen[2] + r.SoldByGen[3] + r.SoldByGen[4] > 0)}/{Seeds}");
        output.WriteLine($"#146 sales by a laggard of its own front (gen(d) ≥ G(seller), gap ≥ 1): {main.Sum(r => r.LaggardSales)}");
        output.WriteLine($"G_front ≥ 1 in {main.Sum(r => r.FrontMonths)} of {Seeds * FleetRunStats.Months} seed-months; largest G_front {main.Max(r => r.FrontMax)}");
        output.WriteLine($"ext_total by actor (units, sum over seeds): {string.Join(" ", Enumerable.Range(0, n).Select(a => $"{ids[a]} {main.Sum(r => r.SaleIncomeTotal[a]) / Fx6.Scale}"))}");

        output.WriteLine("== «Парк на keep»: actor-years in windows 5–7 with every non-line project outside Def(K) empty for 24 months and Def(K) non-empty");
        foreach (PolicyProfile pr in Enum.GetValues<PolicyProfile>())
        {
            long keep = 0, years = 0;
            foreach (FleetRunStats r in main)
            {
                for (int a = 0; a < n; a++)
                {
                    if (r.Profiles[a] == pr && ids[a] is "USA" or "GBR" or "SUN")
                    {
                        keep += r.KeepActorYears[a];
                        years += r.ActorYears[a];
                    }
                }
            }
            output.WriteLine($"{pr,-9} {keep}/{years}");
        }

        output.WriteLine("== Δ (#143): share of seeds with a leader change (held 24 months) in the first 360 months; paired difference main − NO_RETIRE");
        double Share(FleetRunStats[] set) => set.Count(r => r.LeaderChanges > 0) / (double)Seeds;
        var pair = Enumerable.Range(0, Seeds).Select(i => (main[i].LeaderChanges > 0 ? 1 : 0) - (none[i].LeaderChanges > 0 ? 1 : 0)).ToList();
        double mean = pair.Average(), sd = Math.Sqrt(pair.Sum(x => (x - mean) * (x - mean)) / (Seeds - 1));
        output.WriteLine($"chg main {Share(main):F2}, NO_RETIRE {Share(none):F2}, NO_SALE {Share(nosale):F2}, PURCHASE_CAP {Share(cap):F2}; Δ = {mean:F2} (95% CI ±{1.96 * sd / Math.Sqrt(Seeds):F2}, n = {Seeds})");
        output.WriteLine($"changes by deciding component G/T/N/P: main {string.Join("/", Enumerable.Range(0, 4).Select(c => main.Sum(r => r.ChangesByComponent[c])))}, "
            + $"NO_RETIRE {string.Join("/", Enumerable.Range(0, 4).Select(c => none.Sum(r => r.ChangesByComponent[c])))}");
        output.WriteLine("(the leader metric here is the K key of contract §9.1 over USA, GBR, SUN with the 24-month hold; the Lab metric §5.2 does not exist yet)");

        output.WriteLine("== Counters: units retired / sold by the AI, sum over seeds, by actor (main | NO_SALE retired)");
        output.WriteLine(string.Join("  ", Enumerable.Range(0, n).Select(a => $"{ids[a]} ret {main.Sum(r => r.Retired[a])} sold {main.Sum(r => r.Sold[a])} | {nosale.Sum(r => r.Retired[a])}")));
        Assert.Equal(0, main.Sum(r => r.Violations.Count));
    }
}
