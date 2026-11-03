using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Products;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Products;

/// <summary>
/// Product invariants checked after every month of long scripted runs (reference README I3, I4, I6 in the product part;
/// spec §3.3, §3.4; contract §4.4, §4.6; #67 stages): no negative fleets, cohorts or line state; fleet units conserved
/// (cohort change = output of the month, minus whole cohorts written off at service life); <c>hidden + found = D0</c>,
/// <c>open ≤ found ≤ D0</c>; stages move only along П → О → С ⇄ В → «снят», «снят» is final in the full model;
/// <c>report_output</c> only for admitted processes of a series design and equal to <c>r·n_p</c>; engineer requests
/// within <c>bureau_engineers</c>; spending within the procurement budget; readiness in [0; 1].
/// </summary>
public class ProductsInvariantTests
{
    public static TheoryData<string> Scripts => ["busy", "starved", "blocked", "default"];

    [Theory]
    [MemberData(nameof(Scripts))]
    public void EveryMonth_ProductInvariantsHold(string script)
    {
        ProductsHarness h = Make(script, threads: 1, chunkSize: 1, perm: null);
        var check = new Checker(h);
        for (long t = 0; t < 200; t++)
        {
            check.Before();
            h.RunThroughMonth(t);
            check.After(t);
        }
        // The runs are not trivial: the busy one designs, launches and produces; the default one writes off its fleets.
        if (script == "busy")
        {
            Assert.True(check.Produced > Fx6.FromInt(1000), $"busy run produced only {check.Produced}");
            Assert.True(check.Orders > 0 && check.Prototypes > 0 && check.Relaunches > 0, $"orders {check.Orders}, prototypes {check.Prototypes}, relaunches {check.Relaunches}");
            Assert.True(check.FixRequests > 0, "no defect-fix team was ever requested");
        }
        if (script == "default")
            Assert.True(check.Withdrawals >= 2, "start fleets of USA and GBR are never written off");
        if (script == "blocked")
            Assert.True(check.BlockedSeriesMonths > 0, "the blocked run never had a series line without admission");
    }

    [Fact]
    public void Invariants_HoldWithPermutedActorsAndThreads()
    {
        ProductsHarness h = Make("busy", threads: 4, chunkSize: 2, perm: [3, 0, 4, 2, 1]);
        var check = new Checker(h);
        for (long t = 0; t < 120; t++)
        {
            check.Before();
            h.RunThroughMonth(t);
            check.After(t);
        }
        Assert.True(check.Produced > Fx6.Zero);
    }

    internal static ProductsHarness Make(string script, int threads, int chunkSize, int[]? perm, ProductsMode mode = ProductsMode.Full,
        ProductsParams? parameters = null)
    {
        var h = new ProductsHarness(threads, chunkSize, mode, parameters, perm);
        switch (script)
        {
            case "busy":
                h.Knows = (a, n) => Probe.KnownUpTo(a % 2 == 0 ? 3 : 2)(n);
                h.Order = (a, t) => t % 13 == a ? new DesignOrder(1 + (int)((t / 13 + a) % 3), (t / 13) % 2 == 0 ? DesignVariant.Superior : DesignVariant.Producible) : null;
                h.Launch = (a, d, t) => t % 7 == a;
                h.Close = (a, d, t) => t % 11 == a + 3;
                h.Rate = (a, d, t) => Fx6.FromInt(5 + (a + d + t) % 9);
                h.Budget = (a, t) => Fx6.FromRaw(20_000 + 10_000 * a + 500 * (t % 13));
                h.Alloy = (a, _) => Fx6.FromInt(3_000 + 1_000 * a);
                h.Yield = (a, p) => Fx6.FromRaw(300_000 + 50_000 * ((a + p) % 15));
                h.GrantShare = Fx6.FromRaw(700_000);
                break;
            case "starved":
                // Money below the upkeep and no alloy: nothing can be bought; upkeep is paid in part.
                h.Launch = (_, _, _) => true;
                h.Rate = (_, _, _) => Fx6.FromInt(40);
                h.Budget = (a, t) => Fx6.FromRaw(500 * (a + 1) * (t % 3));
                h.Alloy = (_, _) => Fx6.Zero;
                h.Order = (a, t) => t == 2 * a ? new DesignOrder(1, DesignVariant.Superior) : null;
                h.Knows = (a, n) => Probe.KnownUpTo(1)(n);
                h.GrantShare = Fx6.FromRaw(300_000);
                break;
            case "blocked":
                // Some processes have yield 0 at some sites: those lines must neither produce nor report (I3).
                h.Launch = (_, _, t) => t % 5 == 0;
                h.Rate = (_, _, _) => Fx6.FromInt(20);
                h.Yield = (a, p) => (a + p) % 4 == 0 ? Fx6.Zero : Fx6.One;
                h.Knows = (a, n) => Probe.KnownUpTo(2)(n);
                h.Order = (a, t) => t == a ? new DesignOrder(2, DesignVariant.Superior) : t == 40 + a ? new DesignOrder(1, DesignVariant.Producible) : null;
                break;
            case "default":
                break;
            default:
                throw new ArgumentException(script);
        }
        return h;
    }

    private static int Rank(DesignStage s) => s switch
    {
        DesignStage.None => 0,
        DesignStage.Project => 1,
        DesignStage.Prototype => 2,
        DesignStage.Series or DesignStage.InService => 3,
        DesignStage.Withdrawn => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };

    internal sealed class Checker(ProductsHarness h)
    {
        private readonly ProductsLayout _l = h.Layout;
        private DesignStage[,] _stage = new DesignStage[0, 0];
        private Fx6[,,] _cohort = new Fx6[0, 0, 0];
        private Fx6[,] _found = new Fx6[0, 0];

        public Fx6 Produced = Fx6.Zero;
        public int Orders, Prototypes, Relaunches, Withdrawals, FixRequests, BlockedSeriesMonths;

        public void Before()
        {
            ProductsSystem p = h.Products;
            int rows = h.Table.RowCount;
            _stage = new DesignStage[rows, _l.Designs];
            _cohort = new Fx6[rows, _l.Designs, _l.Years];
            _found = new Fx6[rows, _l.Designs];
            for (int a = 0; a < rows; a++)
            {
                for (int d = 0; d < _l.Designs; d++)
                {
                    _stage[a, d] = p.Stage(a, d);
                    _found[a, d] = p.DefectsFound(a, d);
                    for (int y = 0; y < _l.Years; y++)
                        _cohort[a, d, y] = p.Cohort(a, d, _l.StartYear + y);
                }
            }
        }

        public void After(long t)
        {
            ProductsSystem p = h.Products;
            ProductsParams pp = h.Params;
            int year = h.Calendar.DateOf(h.Calendar.TickOfMonth(t)).Year;
            CivilDate start = h.Calendar.Start;
            var expectedReports = new List<(int Site, int Process, Fx6 Units)>();
            for (int a = 0; a < h.Table.RowCount; a++)
            {
                int actor = h.ActorOf[a];
                string at = $"month {t}, actor {actor}";
                Fx6 budget = h.Budget(actor, t);
                Assert.True(p.Spent(a) >= Fx6.Zero && p.Spent(a) <= budget, $"{at}: spent {p.Spent(a)} of budget {budget} (I4)");
                Assert.InRange(p.PaidRatio(a).Raw, 0, Fx6.Scale);
                Assert.True(p.UpkeepNeeded(a) >= Fx6.Zero, $"{at}: negative upkeep need");
                for (int b = 0; b < _l.Bureaus; b++)
                    Assert.True(p.BureauExp(a, b) >= Fx6.Zero, $"{at}: negative bureau_exp");
                for (int d = 0; d < _l.Designs; d++)
                {
                    string ad = $"{at}, design {d}";
                    DesignStage prev = _stage[a, d], cur = p.Stage(a, d);
                    Assert.True(Rank(cur) >= Rank(prev), $"{ad}: stage went back {prev} → {cur}");
                    if (prev == DesignStage.Withdrawn)
                        Assert.Equal(DesignStage.Withdrawn, cur); // «снят» is final in the full model
                    if (prev == DesignStage.None && cur != DesignStage.None)
                        Orders++;
                    if (prev == DesignStage.Project && cur != DesignStage.Project)
                        Prototypes++;
                    if (prev == DesignStage.InService && cur == DesignStage.Series)
                        Relaunches++;
                    if (prev != DesignStage.Withdrawn && cur == DesignStage.Withdrawn)
                    {
                        Withdrawals++;
                        Assert.Equal(Fx6.Zero, p.InService(a, d));
                    }

                    // Defects: hidden + found = D0, 0 ≤ open ≤ found ≤ D0, found never falls.
                    Fx6 d0 = p.InitialDefects(a, d), hidden = p.DefectsHidden(a, d), found = p.DefectsFound(a, d), open = p.DefectsOpen(a, d);
                    Assert.True(hidden >= Fx6.Zero && open >= Fx6.Zero && found <= d0 && hidden + found == d0 && open <= found,
                        $"{ad}: D0 {d0}, hidden {hidden}, found {found}, open {open}");
                    if (prev != DesignStage.None)
                        Assert.True(found >= _found[a, d], $"{ad}: found fell {_found[a, d]} → {found}");

                    // Line state: no negatives; output and line_E only on a series line.
                    Fx6 rate = p.Rate(a, d);
                    Assert.True(rate >= Fx6.Zero && p.LineE(a, d) >= Fx6.Zero && p.WorkLeft(a, d) >= Fx6.Zero, $"{ad}: negative line state");
                    if (cur != DesignStage.Series)
                    {
                        Assert.Equal(Fx6.Zero, rate);
                        Assert.Equal(Fx6.Zero, p.LineE(a, d));
                    }
                    else
                    {
                        int[] prcNow = p.PrcSet(a, d);
                        if (prcNow.Any(n => h.Yield(actor, n) == Fx6.Zero))
                        {
                            BlockedSeriesMonths++;
                            Assert.Equal(Fx6.Zero, rate); // I3: not admitted ⇒ no output
                        }
                    }
                    Fx6 want = h.Rate(actor, d, t);
                    Assert.True(rate <= want, $"{ad}: rate {rate} above the asked {want}");

                    // Cohorts: no negatives; change = output into the cohort of this calendar year, or a write-off at service life.
                    for (int y = 0; y < _l.Years; y++)
                    {
                        int cy = _l.StartYear + y;
                        Fx6 c = p.Cohort(a, d, cy), before = _cohort[a, d, y];
                        Assert.True(c >= Fx6.Zero, $"{ad}: negative cohort {cy}");
                        long m0 = (long)(cy - start.Year) * 12 + (7 - start.Month); // A32: cohorts age from July
                        bool retired = t - m0 >= pp.ServiceLife;
                        Fx6 expected = retired ? Fx6.Zero : before + (cy == year ? rate : Fx6.Zero);
                        Assert.True(expected == c, $"{ad}: cohort {cy} is {c}, expected {expected} (before {before}, rate {rate}, retired {retired})");
                    }
                    Produced += rate;

                    if (rate > Fx6.Zero)
                    {
                        foreach (int n in p.PrcSet(a, d))
                            expectedReports.Add((a, n, rate * _l.ProcN[_l.ProcIndex(n)]));
                    }
                    if (cur != DesignStage.None)
                        Assert.InRange(p.Readiness(a, d, t + 1).Raw, 0, Fx6.Scale);
                }
            }

            // report_output: only admitted processes of producing series lines, units = r·n_p (I3, spec §3.2).
            foreach (OutputReport r in h.Reports)
            {
                int actor = h.ActorOf[r.Site];
                Assert.True(h.Inputs.AdmittedYield(r.Site, r.ProcessNumber, t) > Fx6.Zero, $"month {t}: report of process {r.ProcessNumber} at yield 0");
                Assert.True(r.LabourHours == r.Units * _l.ProcLabour[_l.ProcIndex(r.ProcessNumber)], $"month {t}: labour hours of actor {actor}");
            }
            Assert.Equal(expectedReports.Order(), h.Reports.Select(r => (r.Site, r.ProcessNumber, r.Units)).Order());

            // reserve_workforce: one request per (site, category, requester); engineers within bureau_engineers (I6 input side).
            var keys = new HashSet<(int, int, int)>();
            int skilledIds = _l.Designs * _l.ClusterCount;
            foreach (WorkforceRequest q in h.Requests)
            {
                Assert.True(keys.Add((q.Site, q.Category, q.RequesterId)), $"month {t}: duplicate request {q}");
                Assert.True(q.N > 0, $"month {t}: empty request {q}");
                if (q.Category == ProductsLayout.EngineerCategory)
                {
                    int id = q.RequesterId - skilledIds;
                    Assert.InRange(id, 0, _l.Bureaus + _l.Designs - 1);
                    if (id < _l.Bureaus)
                    {
                        int d = p.BureauDesign(q.Site, id);
                        Assert.True(d >= 0 && p.Stage(q.Site, d) == DesignStage.Project, $"month {t}: bureau {id} requests without a project");
                        Assert.Equal(p.BureauReq(ProductsLayout.GenOf(d)), q.N);
                    }
                    else
                    {
                        int d = id - _l.Bureaus;
                        FixRequests++;
                        Assert.True(q.N <= p.BureauReq(ProductsLayout.GenOf(d)), $"month {t}: fix team {q.N} over bureau_req");
                        Assert.True(p.DefectsOpen(q.Site, d) > Fx6.Zero, $"month {t}: fix team for design {d} without open defects");
                    }
                }
                else
                {
                    Assert.Equal(_l.SkilledCategory, q.Category);
                    Assert.InRange(q.RequesterId, 0, skilledIds - 1);
                    int line = q.RequesterId / _l.ClusterCount;
                    Assert.True(p.Rate(q.Site, line) > Fx6.Zero, $"month {t}: skilled request of idle line {line}");
                }
            }
        }
    }
}
