using Singularity.Core.Commands;
using Singularity.Core.Data;
using Singularity.Core.Policy;
using Singularity.Kernel;
using Singularity.Lab.Models.Slice;
using Singularity.Lab.Simulation;
using Singularity.Session;

namespace Singularity.Lab.Tests;

/// <summary>TASK-0148 (docs/simulation/levers-probes.md §3): the scripted player levers of the Lab — share arithmetic, the reserve account, keys, and a short run.</summary>
public sealed class LeverScriptTests
{
    private static Fx6 F(long raw) => Fx6.FromRaw(raw);

    // The Mass profile shares of PolicyParams: R 0,20, capability 0,18, effort 0,12, procurement 0,45, quality 0,05.
    private static readonly BudgetShares Mass = new(F(200_000), F(180_000), F(120_000), F(450_000), F(50_000));

    private static ParamSet Params(params (string Key, ParamValue Value)[] kv) =>
        new(kv.Select(x => new KeyValuePair<string, ParamValue>(x.Key, x.Value)));

    private static ParamValue S(string v) => new(ParamKind.String, v);

    [Fact]
    public void Plus10_takes_the_share_from_the_other_three_items_in_proportion()
    {
        BudgetShares s = LeverSpec.Shift(Mass, LeverItem.Research, LeverOp.Plus10);
        Assert.True(s.IsValid);
        Assert.Equal(300_000, s.Research.Raw);
        // others 0,75: capability gives ⌊0,1·0,18/0,75⌋ = 0,024, effort 0,016, procurement 0,06 (the residue, 0 here, would go to procurement).
        Assert.Equal(156_000, s.Capability.Raw);
        Assert.Equal(104_000, s.Effort.Raw);
        Assert.Equal(390_000, s.Procurement.Raw);
        Assert.Equal(Mass.Quality, s.Quality);
    }

    [Fact]
    public void Minus10_and_zero_give_the_share_to_the_other_three_and_keep_quality()
    {
        BudgetShares m = LeverSpec.Shift(Mass, LeverItem.Procurement, LeverOp.Minus10);
        Assert.True(m.IsValid);
        Assert.Equal(350_000, m.Procurement.Raw);
        Assert.Equal(Mass.Quality, m.Quality);
        Assert.Equal(200_000 + 40_000, m.Research.Raw); // 0,1·0,2/0,5; the rounding residue of a procurement shift goes to research
        BudgetShares z = LeverSpec.Shift(Mass, LeverItem.Effort, LeverOp.Zero);
        Assert.True(z.IsValid);
        Assert.Equal(0, z.Effort.Raw);
        Assert.Equal(Mass.Quality, z.Quality);
        // −10 of an item below 0,10 takes only what it has.
        BudgetShares small = LeverSpec.Shift(new BudgetShares(F(50_000), F(300_000), F(100_000), F(500_000), F(50_000)), LeverItem.Research, LeverOp.Minus10);
        Assert.Equal(0, small.Research.Raw);
        Assert.True(small.IsValid);
    }

    [Fact]
    public void Withhold_scales_the_items_and_pays_at_most_the_reserve_into_the_spend_item()
    {
        BudgetShares kept = LeverSpec.Withhold(Mass, F(400_000), ReserveSpend.None, Fx6.Zero);
        Assert.True(kept.IsValid);
        Assert.Equal(120_000, kept.Research.Raw);
        Assert.Equal(270_000, kept.Procurement.Raw);
        Assert.Equal(30_000 + 400_000, kept.Quality.Raw); // the AI quality share ·0,6 plus the reserve: the unread slot
        BudgetShares paid = LeverSpec.Withhold(Mass, F(400_000), ReserveSpend.Research, F(400_000));
        Assert.Equal(520_000, paid.Research.Raw);
        Assert.Equal(30_000, paid.Quality.Raw);
        Assert.True(paid.IsValid);
        Assert.Throws<ArgumentOutOfRangeException>(() => LeverSpec.Withhold(Mass, F(400_000), ReserveSpend.Research, F(400_001)));
        Assert.Throws<ArgumentException>(() => LeverSpec.Withhold(Mass, F(400_000), ReserveSpend.None, F(1)));
    }

    [Fact]
    public void Ledger_leaks_10_percent_a_year_and_spends_from_month_12_no_more_than_it_withholds()
    {
        Fx6 budget = Fx6.FromInt(10);
        var kept = new ReserveLedger(F(200_000), ReserveSpend.None);
        long expect = 0;
        for (long m = 0; m < 24; m++)
        {
            Assert.Equal(Fx6.Zero, kept.Month(m, budget));
            expect = expect * 991_258 / 1_000_000 + 2_000_000; // floor of the keep factor, then +r·budget
            Assert.Equal(expect, kept.Balance);
        }
        Assert.Equal(kept.Accrued - kept.Leaked, kept.Balance);
        var spend = new ReserveLedger(F(200_000), ReserveSpend.Research);
        for (long m = 0; m < 12; m++)
            Assert.Equal(Fx6.Zero, spend.Month(m, budget));
        Fx6 share = spend.Month(12, budget);
        Assert.Equal(200_000, share.Raw); // the balance is above one month's slot: the whole slot is paid out
        Assert.Equal(spend.Accrued - spend.Leaked - spend.Spent, spend.Balance);
        Assert.True(spend.Balance > 0);
    }

    [Fact]
    public void Keys_parse_and_bad_combinations_are_parameter_errors()
    {
        Assert.Null(SliceParameters.ParseLevers(Params(), calibration: false));
        LeverSpec p1 = SliceParameters.ParseLevers(Params(("LEVER_ACTOR", S("SUN")), ("LEVER_SHIFT", S("C-10"))), false)!;
        Assert.Equal((LeverItem?)LeverItem.Capability, p1.Item);
        Assert.Equal(LeverOp.Minus10, p1.Op);
        LeverSpec p2 = SliceParameters.ParseLevers(Params(("LEVER_ACTOR", S("FRA")), ("RESERVE", new ParamValue(ParamKind.Number, "0.4")), ("RESERVE_SPEND", S("PROC"))), false)!;
        Assert.Equal(400_000, p2.Reserve.Raw);
        Assert.Equal(ReserveSpend.Procurement, p2.Spend);
        Assert.True(SliceParameters.ParseLevers(Params(("LEVER_DIAG", new ParamValue(ParamKind.Bool, "true"))), false)!.Diag);
        (string, ParamValue)[][] bad =
        [
            [("LEVER_ACTOR", S("USA"))],
            [("LEVER_SHIFT", S("R+10"))],
            [("LEVER_ACTOR", S("USA")), ("LEVER_SHIFT", S("R+10")), ("RESERVE", new ParamValue(ParamKind.Number, "0.2"))],
            [("LEVER_ACTOR", S("USA")), ("LEVER_SHIFT", S("Q+10"))],
            [("LEVER_ACTOR", S("USA")), ("LEVER_SHIFT", S("R+20"))],
            [("LEVER_ACTOR", S("USA")), ("RESERVE", new ParamValue(ParamKind.Number, "0.95"))],
            [("LEVER_ACTOR", S("USA")), ("RESERVE", new ParamValue(ParamKind.Number, "0"))],
            [("LEVER_ACTOR", S("USA")), ("RESERVE", new ParamValue(ParamKind.Number, "0.2")), ("RESERVE_SPEND", S("ALL"))],
            [("LEVER_ACTOR", S("USA")), ("LEVER_SHIFT", S("R+10")), ("RESERVE_SPEND", S("RES"))],
            [("LEVER_ACTOR", S("USA")), ("LEVER_SHIFT", S("R+10")), ("RESERVE_SPEND", S("RES_DIRECT"))],
        ];
        foreach ((string, ParamValue)[] b in bad)
            Assert.Throws<ParamException>(() => SliceParameters.ParseLevers(Params(b), false));
        Assert.Throws<ParamException>(() => SliceParameters.ParseLevers(Params(("LEVER_DIAG", new ParamValue(ParamKind.Bool, "true"))), calibration: true));
    }

    // A short run of the slice with the script in the place JetSliceModel gives it: Issue(0) before month 0, Observe(t) and Issue(t + 1) after each month.
    private sealed class Rows : IMetricSink
    {
        public readonly List<string[]> All = [];
        public void Row(int table, params string[] cells)
        {
            Assert.Equal(0, table);
            All.Add(cells);
        }
    }

    private static (SliceSession S, LeverScript? L) Run(LeverSpec? spec, int months, Rows? sink = null)
    {
        GameData data = SliceData.Load(AppContext.BaseDirectory);
        SliceSession s = SliceSession.Create(data, new SliceRunConfig { Seed = 1, Threads = 1, ChunkSize = 1 });
        var calendar = new TickCalendar(s.Date);
        LeverScript? l = spec is null ? null : new LeverScript(s, spec, sink, sink is null ? -1 : 0);
        l?.Issue(0);
        for (long t = 0; t < months; t++)
        {
            s.StepTo(calendar.TickOfMonth(t));
            s.Step(1);
            l?.Observe(t);
            l?.Issue(t + 1);
        }
        s.StepTo(calendar.TickOfMonth(months));
        return (s, l);
    }

    [Fact]
    public void Shift_is_in_force_for_the_lever_actor_only_and_the_diagnostic_does_not_change_the_world()
    {
        const int months = 14;
        (SliceSession plain, _) = Run(null, months);
        (SliceSession diag, LeverScript? d) = Run(new LeverSpec(null, null, LeverOp.Plus10, Fx6.Zero, ReserveSpend.None, Diag: true), months);
        HashAssert.Equal(plain.LastHash, diag.LastHash);
        Assert.Contains(d!.Counters(), kv => kv.Key == "alloc_1980_USA_prop" && long.Parse(kv.Value, System.Globalization.CultureInfo.InvariantCulture) > 0);

        (SliceSession shifted, LeverScript? l) = Run(new LeverSpec("SUN", LeverItem.Research, LeverOp.Plus10, Fx6.Zero, ReserveSpend.None, Diag: false), months);
        SliceWorld w = shifted.World;
        int sun = shifted.Layout.ActorIds.IndexOf("SUN");
        Assert.True(l!.Commands >= 1);
        Assert.Equal(LeverSpec.Shift(w.Policy.Shares(sun), LeverItem.Research, LeverOp.Plus10), w.SharesOf(sun));
        for (int a = 0; a < shifted.Layout.ActorIds.Length; a++)
        {
            if (a != sun)
                Assert.Equal(w.Policy.Shares(a), w.SharesOf(a));
        }
        HashAssert.NotEqual(plain.LastHash, shifted.LastHash);
    }

    [Fact]
    public void Reserve_run_withholds_and_its_account_matches_the_shares_in_force()
    {
        (SliceSession s, LeverScript? l) = Run(new LeverSpec("FRA", null, LeverOp.Plus10, F(400_000), ReserveSpend.Research, Diag: false), 14);
        int fra = s.Layout.ActorIds.IndexOf("FRA");
        BudgetShares ai = s.World.Policy.Shares(fra), now = s.World.SharesOf(fra);
        Assert.True(now.IsValid);
        // Month 14 is past month 12: the slot is paid into research, so research is above its kept part and quality is below the full slot.
        Assert.True(now.Research.Raw > ai.Research.Raw * 6 / 10);
        Assert.True(now.Quality.Raw < ai.Quality.Raw * 6 / 10 + 400_000);
        var c = l!.Counters().ToDictionary(kv => kv.Key, kv => long.Parse(kv.Value, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(c["reserve_accrued"] - c["reserve_leaked"] - c["reserve_spent"], c["reserve_end"]);
        Assert.True(c["reserve_spent"] > 0);

        // The direct control pays the whole slot into research from month 0 and keeps no account.
        (SliceSession d, LeverScript? ld) = Run(new LeverSpec("FRA", null, LeverOp.Plus10, F(400_000), ReserveSpend.ResearchDirect, Diag: false), 3);
        Assert.Equal(LeverSpec.Withhold(d.World.Policy.Shares(fra), F(400_000), ReserveSpend.ResearchDirect, F(400_000)), d.World.SharesOf(fra));
        Assert.DoesNotContain(ld!.Counters(), kv => kv.Key.StartsWith("reserve_", StringComparison.Ordinal));
    }

    [Fact]
    public void Eta_rows_cover_the_frontier_in_month_0_and_12_and_are_the_ceiling_of_need_minus_prog_over_rate()
    {
        var sink = new Rows();
        (SliceSession s, _) = Run(new LeverSpec(null, null, LeverOp.Plus10, Fx6.Zero, ReserveSpend.None, Diag: false, Eta: true), 13, sink);
        Assert.NotEmpty(sink.All);
        Assert.All(sink.All, r => Assert.Contains(r[0], new[] { "0", "12" }));
        int checkedRows = 0;
        foreach (string[] r in sink.All)
        {
            if (r[7].Length == 0)
                continue;
            decimal prog = decimal.Parse(r[4], System.Globalization.CultureInfo.InvariantCulture), need = decimal.Parse(r[5], System.Globalization.CultureInfo.InvariantCulture);
            decimal rate = decimal.Parse(r[6], System.Globalization.CultureInfo.InvariantCulture);
            long eta = long.Parse(r[7], System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(prog >= need ? 0 : (long)Math.Ceiling((need - prog) / rate), eta);
            checkedRows++;
        }
        Assert.True(checkedRows > 0);
        Assert.Throws<ArgumentException>(() => new LeverScript(s, new LeverSpec(null, null, LeverOp.Plus10, Fx6.Zero, ReserveSpend.None, false, Eta: true)));
    }
}
