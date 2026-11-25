using Singularity.Core.Data;
using Singularity.Core.Institutes;
using Singularity.Core.Policy;
using Singularity.Kernel;
using Singularity.Lab.Models.Slice;
using Singularity.Lab.Simulation;
using Singularity.Session;

namespace Singularity.Lab.Tests;

/// <summary>TASK-0173 (docs/simulation/institutes-p7-measurement.md): the scripted institute player of the Lab — start rules, the funding cut, keys, and short runs.</summary>
public sealed class InstituteScriptTests
{
    private static Fx6 F(long raw) => Fx6.FromRaw(raw);

    private static ParamSet Params(params (string Key, ParamValue Value)[] kv) =>
        new(kv.Select(x => new KeyValuePair<string, ParamValue>(x.Key, x.Value)));

    private static ParamValue S(string v) => new(ParamKind.String, v);

    private static ParamValue B(bool v) => new(ParamKind.Bool, v ? "true" : "false");

    [Fact]
    public void Planned_start_follows_the_plan_and_never_precedes_the_window_or_month_0()
    {
        // window 28 (1948-01 for DARPA), AI start 106.
        Assert.Equal(-1, InstituteSpec.PlannedStart(InstituteBuild.None, 28, 106));
        Assert.Equal(28, InstituteSpec.PlannedStart(InstituteBuild.Early, 28, 106));
        Assert.Equal(106, InstituteSpec.PlannedStart(InstituteBuild.Hist, 28, 106));
        Assert.Equal(46, InstituteSpec.PlannedStart(InstituteBuild.HistMinus5, 28, 106));
        Assert.Equal(166, InstituteSpec.PlannedStart(InstituteBuild.HistPlus5, 28, 106));
        // A window before the start of the run (RRE: ref 1953, window 1943) starts at month 0; −5 years is clamped to the window.
        Assert.Equal(0, InstituteSpec.PlannedStart(InstituteBuild.Early, -32, 46));
        Assert.Equal(0, InstituteSpec.PlannedStart(InstituteBuild.HistMinus5, -32, 46));
        Assert.Equal(28, InstituteSpec.PlannedStart(InstituteBuild.HistMinus5, 28, 50));
    }

    [Fact]
    public void Cut_scales_research_and_leaves_the_rest_unspent_in_quality()
    {
        var ai = new BudgetShares(F(200_000), F(180_000), F(120_000), F(450_000), F(50_000));
        BudgetShares c = InstituteSpec.Cut(ai, F(350_000));
        Assert.True(c.IsValid);
        Assert.Equal(70_000, c.Research.Raw);
        Assert.Equal(ai.Capability, c.Capability);
        Assert.Equal(ai.Procurement, c.Procurement);
        Assert.Equal(50_000 + 130_000, c.Quality.Raw);
        Assert.Equal(ai, InstituteSpec.Cut(ai, Fx6.One));
        Assert.Throws<ArgumentOutOfRangeException>(() => InstituteSpec.Cut(ai, Fx6.Zero));
    }

    [Fact]
    public void Keys_parse_and_bad_combinations_are_parameter_errors()
    {
        Assert.Null(SliceParameters.ParseInstitutes(Params(), false, on: true));
        InstituteSpec p = SliceParameters.ParseInstitutes(Params(("INST_PLAYER", S("GBR")), ("INST_BUILD", S("EARLY")), ("INST_FUND", new ParamValue(ParamKind.Number, "0.35"))), false, true)!;
        Assert.Equal("GBR", p.Player);
        Assert.Equal(InstituteBuild.Early, p.Build);
        Assert.True(p.Goals);
        Assert.Equal(350_000, p.Fund.Raw);
        InstituteSpec probe = SliceParameters.ParseInstitutes(Params(("INST_PROBE", B(true))), false, true)!;
        Assert.Null(probe.Player);
        Assert.Equal(InstituteBuild.Hist, probe.Build);
        Assert.True(probe.Probe);
        (string, ParamValue)[][] bad =
        [
            [("INST_BUILD", S("EARLY"))],
            [("INST_GOALS", S("NONE"))],
            [("INST_FUND", new ParamValue(ParamKind.Number, "0.5"))],
            [("INST_PLAYER", S("GBR")), ("INST_BUILD", S("SOON"))],
            [("INST_PLAYER", S("GBR")), ("INST_GOALS", S("ALL"))],
            [("INST_PLAYER", S("GBR")), ("INST_FUND", new ParamValue(ParamKind.Number, "0"))],
            [("INST_PLAYER", S("GBR")), ("INST_FUND", new ParamValue(ParamKind.Number, "1.5"))],
        ];
        foreach ((string, ParamValue)[] b in bad)
            Assert.Throws<ParamException>(() => SliceParameters.ParseInstitutes(Params(b), false, true));
        Assert.Throws<ParamException>(() => SliceParameters.ParseInstitutes(Params(("INST_PROBE", B(true))), false, on: false));
        Assert.Throws<ParamException>(() => SliceParameters.ParseInstitutes(Params(("INST_PROBE", B(true))), calibration: true, on: true));
    }

    private sealed class Rows : IMetricSink
    {
        public readonly List<(int Table, string[] Cells)> All = [];

        public void Row(int table, params string[] cells) => All.Add((table, cells));
    }

    // A short run in the place JetSliceModel gives the script: Issue(0) before month 0, BeforeStep(t) before the step, Observe(t) and Issue(t + 1) after it.
    private static (SliceSession S, InstituteScript? I) Run(InstituteSpec? spec, int months, Rows? sink = null)
    {
        GameData data = SliceData.Load(AppContext.BaseDirectory);
        int? player = spec?.Player is { } id ? data.Scenario(SliceRunConfig.Legacy.ScenarioId).ActorId.IndexOf(id) : null;
        SliceSession s = SliceSession.Create(data, SliceRunConfig.Legacy with { Seed = 1, Threads = 1, ChunkSize = 1, Institutes = true, PlayerActor = player });
        var calendar = new TickCalendar(s.Date);
        InstituteScript? i = spec is null ? null : new InstituteScript(s, spec, sink, sink is null ? -1 : 0, sink is null ? -1 : 1);
        i?.Issue(0);
        for (long t = 0; t < months; t++)
        {
            s.StepTo(calendar.TickOfMonth(t));
            i?.BeforeStep(t);
            s.Step(1);
            i?.Observe(t);
            i?.Issue(t + 1);
        }
        s.StepTo(calendar.TickOfMonth(months));
        return (s, i);
    }

    private static InstituteHandle Slot(SliceSession s, string actor, string id)
    {
        InstituteLayout l = s.World.Institutes!.Layout;
        int a = s.Layout.ActorIds.IndexOf(actor);
        for (int k = 0; k < l.SlotCount(a); k++)
        {
            if (l.IdOf(new InstituteHandle(a, k)) == id)
                return new InstituteHandle(a, k);
        }
        throw new InvalidOperationException($"no {id} for {actor}");
    }

    [Fact]
    public void Early_player_starts_its_candidate_in_the_first_month_the_window_allows_and_the_ai_does_not()
    {
        (SliceSession early, InstituteScript? e) = Run(new InstituteSpec("GBR", InstituteBuild.Early, Goals: true, Fx6.One, Probe: false), 2);
        Assert.Equal(0, early.World.Institutes!.BuildStart(Slot(early, "GBR", "gbr_rre")));
        Assert.Equal(0, e!.Refused);
        (SliceSession hist, _) = Run(new InstituteSpec("GBR", InstituteBuild.Hist, Goals: true, Fx6.One, Probe: false), 2);
        Assert.Equal(-1, hist.World.Institutes!.BuildStart(Slot(hist, "GBR", "gbr_rre")));
        // The player took the adviser's goals (the path rule, #559, proposes only nodes of the path, so a slot may have none): some standing slot of GBR works on a node.
        InstituteSystem inst = early.World.Institutes!;
        int gbr = early.Layout.ActorIds.IndexOf("GBR");
        Assert.Contains(Enumerable.Range(0, inst.Layout.StartingCount(gbr)), k => inst.GoalNode(new InstituteHandle(gbr, k)) != 0);
    }

    [Fact]
    public void Fund_cut_is_in_force_only_while_the_project_runs_and_stretches_it()
    {
        (SliceSession s, InstituteScript? i) = Run(new InstituteSpec("GBR", InstituteBuild.Early, Goals: false, F(350_000), Probe: false), 3);
        int gbr = s.Layout.ActorIds.IndexOf("GBR");
        BudgetShares plan = s.World.Policy.Shares(gbr);
        Assert.Equal((plan.Research * F(350_000)).Raw, s.World.SharesOf(gbr).Research.Raw);
        Assert.True(s.World.Institutes!.Building(Slot(s, "GBR", "gbr_rre")));
        Assert.Equal(0, i!.Refused);
    }

    [Fact]
    public void Probe_does_not_change_the_world_and_finds_no_goal_slower_than_without_priority()
    {
        const int months = 13;
        (SliceSession plain, _) = Run(null, months);
        var sink = new Rows();
        (SliceSession probed, InstituteScript? i) = Run(new InstituteSpec(null, InstituteBuild.Hist, Goals: true, Fx6.One, Probe: true), months, sink);
        HashAssert.Equal(plain.LastHash, probed.LastHash);
        Assert.True(i!.P8GoalMonths > 0);
        Assert.Equal(0, i.P8Slower);
        Assert.Equal(0, i.Commands);
        i.End(months - 1);
        Assert.Contains(sink.All, r => r.Table == 0);
    }
}
