using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using Singularity.Core.Commands;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Core.WhyNot;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>One 384-month walk of the AI world (seed 1, lic_knows): at every month the published view is compared with direct calls of Core on the same state.</summary>
public sealed class ViewWalk
{
    public const int Months = 384;
    public readonly Dictionary<string, List<string>> Failures = new();
    public readonly Dictionary<string, int> Seen = new();

    public ViewWalk()
    {
        // Seed 1, and the A40 stress arm without effort money for the cancel (TASK-0058: with I-OC the main run gives no project up).
        SliceRunConfig[] configs =
        [
            new() { Seed = 1, Threads = 2, LicKnows = true },
            new() { Seed = 3, Threads = 2, LicKnows = true, EffortShare = EffortShareArm.X0 },
        ];
        foreach (SliceRunConfig config in configs)
        {
            SliceSession s = SliceTestData.Create(config);
            SliceWorld w = s.World;
            for (long t = 0; t < Months; t++)
            {
                Before before = Before.Take(w, t);
                s.StepThroughMonth(t);
                SliceView v = s.View;
                Check("core", v.Month == t && v.Inputs is { } i && i.Month == t && v.Tick == s.Tick && v.Date == s.Date
                    && v.Actors.Length == w.Scenario.ActorCount, $"month {t}: tick, date, month or inputs");
                HashAssert.Equal(s.LastHash, v.StateHash, $"month {t}: view hash");
                for (int a = 0; a < w.Scenario.ActorCount; a++)
                {
                    ActorView av = v.Actors[a];
                    CheckActor(w, av, t);
                    CheckNodes(w, av, t);
                    CheckDesigns(w, av, t);
                    CheckDecisions(w, av, t);
                    // A licence deal of the actor at t changes the alloy cap and the yields the step saw; ViewBuilder.Inputs reads after the deals and Before.Take before the step
                    // (seed 1 month 253, seed 3 month 217 of the X0 arm: alloy supply 30180 -> 31039). Those cells are skipped, and counted.
                    CheckInputs(w, v.Inputs!.Actors[a], before, a, t, afterDeal: w.Policy.LicenceStart(a) == t);
                }
            }
        }
    }

    private void Check(string group, bool ok, string message)
    {
        if (ok)
            return;
        if (!Failures.TryGetValue(group, out List<string>? list))
            Failures[group] = list = new List<string>();
        if (list.Count < 5)
            list.Add(message);
    }

    private void See(string what, bool condition)
    {
        if (condition)
            Seen[what] = Seen.GetValueOrDefault(what) + 1;
    }

    private sealed record Before(Fx6[] Budget, BudgetShares[] Shares, DesignStage[][] Stage, int[][] Idle, Fx6[][] Hidden, Fx6[] Deficit, Fx6[] AlloySupply,
        GiveUpClass?[][] GiveUp, int[][] ArgP, Fx6[][] YMin)
    {
        public static Before Take(SliceWorld w, long t)
        {
            int n = w.Scenario.ActorCount, dn = w.ProductsLayout.Designs;
            var b = new Before(new Fx6[n], new BudgetShares[n], new DesignStage[n][], new int[n][], new Fx6[n][], new Fx6[n], new Fx6[n], new GiveUpClass?[n][], new int[n][], new Fx6[n][]);
            for (int a = 0; a < n; a++)
            {
                b.Budget[a] = w.Policy.Budget(a);
                b.Shares[a] = w.SharesOf(a);
                b.Deficit[a] = w.Policy.AlloyDeficit(a, t);
                b.AlloySupply[a] = w.Industry.Alloy(a, t).Supply;
                b.Stage[a] = new DesignStage[dn];
                b.Idle[a] = new int[dn];
                b.Hidden[a] = new Fx6[dn];
                b.GiveUp[a] = new GiveUpClass?[dn];
                b.ArgP[a] = new int[dn];
                b.YMin[a] = new Fx6[dn];
                for (int d = 0; d < dn; d++)
                {
                    b.Stage[a][d] = w.Products.Stage(a, d);
                    b.Idle[a][d] = w.Products.IdleMonths(a, d);
                    b.Hidden[a][d] = w.Products.DefectsHidden(a, d);
                    b.GiveUp[a][d] = b.Stage[a][d] == DesignStage.Prototype ? w.Policy.ClassifyGiveUp(a, d, t) : null;
                    (b.ArgP[a][d], b.YMin[a][d]) = ArgMin(w, a, d, t);
                }
            }
            return b;
        }
    }

    private void CheckActor(SliceWorld w, ActorView av, long t)
    {
        int a = av.Actor;
        long next = t + 1;
        Check("actor", av.Profile == w.Policy.Profile(a), $"month {t} actor {a}: profile");
        Check("actor", av.KD.Length == w.Graph.DisciplineNames.Length && Enumerable.Range(0, av.KD.Length).All(d => av.KD[d] == w.Knowledge.KD(a, d)), $"month {t} actor {a}: KD");
        Check("actor", av.FieldedGen == w.Products.FieldedGen(a, next), $"month {t} actor {a}: fielded gen");
        Check("actor", av.Key == w.Products.KeyOf(a, next), $"month {t} actor {a}: key");
        bool yearStart = t % 12 == 0;
        Check("actor", av.FleetValue.HasValue == yearStart, $"month {t} actor {a}: fleet value presence");
        if (yearStart)
            Check("actor", av.FleetValue == w.Products.MaxFleetValue(a, Fx6.One, w.PolicyParameters.FvBudget, next), $"month {t} actor {a}: fleet value");
        See("fleet value", av.FleetValue is { } fv && fv > Fx6.Zero);
        See("fielded", av.FieldedGen > 0);

        Check("quality", av.Quality == new QualityView(w.Policy.QualityProgrammeActive(a), w.Policy.QualityProgrammeStart(a), w.Policy.QualityTriggerCount(a),
            w.Policy.TrainingTarget(a)), $"month {t} actor {a}: quality");
        See("quality programme", av.Quality.Active);

        LicenceView l = av.Licence;
        Check("licence", l.Gen == w.Policy.LicenceGen(a) && l.Seller == w.Policy.LicenceSeller(a) && l.Start == w.Policy.LicenceStart(a)
            && l.Mask == w.Policy.LicenceMask(a) && l.OfferMask == w.Policy.OfferMask(a) && l.Flow == w.Policy.LicenceFlow(a) && l.Active == w.LicenceActive(a), $"month {t} actor {a}: licence");
        Check("licence", l.Refusals.Length == 4 && l.Refusals[0] == w.Policy.BadLicenceCount(a, BadLicence.Self) && l.Refusals[1] == w.Policy.BadLicenceCount(a, BadLicence.Empty)
            && l.Refusals[2] == w.Policy.BadLicenceCount(a, BadLicence.Gen) && l.Refusals[3] == w.Policy.BadLicenceCount(a, BadLicence.Money), $"month {t} actor {a}: refusals");
        See("licence held", l.Gen > 0);
        See("licence bought this month", l.Start == t);
        See("licence offer", !l.OfferMask.IsEmpty);
        (int ds, int de) = w.Policy.Disruption(a);
        Check("licence", av.Disruption == new DisruptionView(ds, de), $"month {t} actor {a}: disruption");
        See("disruption", ds >= 0);

        Check("bureau", av.Bureaus.Length == w.ProductsLayout.Bureaus, $"month {t} actor {a}: bureaus");
        for (int b = 0; b < av.Bureaus.Length; b++)
        {
            BureauView bv = av.Bureaus[b];
            int design = w.Products.BureauDesign(a, b);
            long required = design < 0 ? 0 : w.Products.BureauReq(w.ProductsLayout.AltAfClass[w.Products.Alternatives(a, design)[w.ProductsLayout.AirframeSlot]]);
            Check("bureau", bv.Bureau == b && bv.Design == design && bv.Required == required
                && bv.Granted == w.Industry.Granted(a, ProductsLayout.EngineerCategory, w.ProductsLayout.BureauRequester(b)), $"month {t} actor {a} bureau {b}");
            See("bureau busy", design >= 0);
            See("bureau granted", bv.Granted > 0);
        }
    }

    private void CheckNodes(SliceWorld w, ActorView av, long t)
    {
        int a = av.Actor;
        long next = t + 1;
        Check("node", av.Nodes.Length == w.Graph.Count, $"month {t} actor {a}: node count");
        for (int r = 0; r < av.Nodes.Length; r++)
        {
            NodeView n = av.Nodes[r];
            int p = w.IndustryLayout.RowOf(w.Graph.Number[r]);
            bool granted = w.Knowledge.LastLicenceGrants.Any(g => g.ActorNumber == a && g.NodeNumber == n.Number);
            Check("node", n.Number == w.Graph.Number[r] && n.Known == w.Knowledge.IsKnown(a, r) && n.ByLicence == granted && (n.Process is null) == (p < 0),
                $"month {t} actor {a} node {n.Number}");
            See("node by licence", n.ByLicence);
            if (n.Process is not { } pv)
                continue;
            ProcessYield y = w.Industry.Yield(a, p, next);
            Check("process", pv.Yield == y.Yield && pv.V == w.Industry.ProcV(a, p) && pv.Mastered == w.Products.Mastered(a, n.Number, next)
                && pv.LicenceShare == w.IndustryInputs.LicenceShare(a, p, next) && pv.Admitted == (y.Yield > Fx6.Zero && n.Known),
                $"month {t} actor {a} process {n.Number}");
            // Assumption 1 (§12.2): ucm of a process = the multiplier of the prc_set made of it alone. With unit weights it is exactly 1/max(yield, eps).
            Fx6 eps = w.IndustryParameters.Eps;
            Check("ucm", pv.UnitCostMult == Fx6.One / (y.Yield > eps ? y.Yield : eps), $"month {t} actor {a} process {n.Number}: ucm {pv.UnitCostMult.Raw}");
            // With the real weights N·base_cost of the layout the fixed-point divisions round differently (the weight is a small number), so those are not used.
            int[] one = [n.Number];
            if (w.ProductsLayout.ProcIndex(n.Number) >= 0)
                See("ucm with layout weights differs", w.Industry.Profile(a, DesignRules.Items(w.ProductsLayout, one), next).UnitCostMult != pv.UnitCostMult);
            See("process admitted", pv.Admitted);
            See("process mastered", pv.Mastered);
            See("process licence share", pv.LicenceShare > Fx6.Zero);
        }
    }

    private void CheckDesigns(SliceWorld w, ActorView av, long t)
    {
        int a = av.Actor;
        ProductsSystem p = w.Products;
        Check("design", av.Designs.Length == w.ProductsLayout.Designs, $"month {t} actor {a}: design count");
        for (int d = 0; d < av.Designs.Length; d++)
        {
            DesignView dv = av.Designs[d];
            DesignStage stage = p.Stage(a, d);
            if (stage == DesignStage.None)
            {
                Check("design", dv.Design == d && dv.Stage == DesignStage.None && dv.PrcSet.IsEmpty && dv.Gen == 0 && dv.Perf == Fx6.Zero && dv.UnitCostFloor == Fx6.Zero
                    && dv.Readiness == Fx6.Zero && dv.InService == Fx6.Zero, $"month {t} actor {a} slot {d}: empty slot");
                continue;
            }
            Check("design", dv.Design == d && dv.Stage == stage && dv.PlatformGen == p.PlatformGen(a, d) && dv.Gen == p.Gen(a, d) && dv.Parent == p.LiveParent(a, d)
                && dv.StageOMonth == p.StageOMonth(a, d) && dv.BuiltAny == p.BuiltAny(a, d) && dv.Cancelled == p.Cancelled(a, d) && dv.Rate == p.Rate(a, d)
                && dv.UnitCost == p.UnitCost(a, d) && dv.UnitCostFloor == p.UnitCostFloor(a, d) && dv.InService == p.InService(a, d)
                && dv.Readiness == p.Readiness(a, d, t + 1) && dv.Perf == p.Perf(a, d) && dv.Bind == p.Bind(a, d) && dv.AiLine == w.Policy.AiLine(a, d, p.Generation(a, d))
                && dv.PrcSet.SequenceEqual(p.PrcSet(a, d)), $"month {t} actor {a} design {d}");
            See("design project", stage == DesignStage.Project);
            See("design prototype", stage == DesignStage.Prototype);
            See("design series", stage == DesignStage.Series);
            See("design in service", stage == DesignStage.InService);
            See("design cancelled", dv.Cancelled);
            See("design line bound", dv.Bind != LineBind.None);
            See("design ai line", dv.AiLine);
        }
    }

    private void CheckDecisions(SliceWorld w, ActorView av, long t)
    {
        int a = av.Actor;
        long next = t + 1;
        PolicyDecisionView dec = av.Decisions;
        IReadOnlyList<DesignCommand> cmds = w.Policy.AiDesignCommands(a, next);
        Check("decisions", dec.DesignCommands.Length == cmds.Count && Enumerable.Range(0, cmds.Count).All(i =>
            dec.DesignCommands[i].Kind == cmds[i].Kind && dec.DesignCommands[i].Target == cmds[i].Target && dec.DesignCommands[i].Handle == cmds[i].Design && dec.DesignCommands[i].Alts.SequenceEqual(cmds[i].Alts)),
            $"month {t} actor {a}: design commands");
        ImmutableArray<StartView> starts = [.. Enumerable.Range(0, w.ProductsLayout.Designs).Where(d => w.Policy.AiStart(a, d, next) is not null)
            .Select(d => new StartView(d, w.Policy.AiStart(a, d, next)!.Value.Rate, w.Policy.AiStart(a, d, next)!.Value.Design))];
        Check("decisions", dec.Starts.SequenceEqual(starts), $"month {t} actor {a}: starts");
        Check("decisions", dec.Closes.Select(c => (c.Design, c.Handle)).SequenceEqual(Enumerable.Range(0, w.ProductsLayout.Designs).Where(d => w.Policy.AiClose(a, d, next) is not null).Select(d => (d, w.Policy.AiClose(a, d, next)!.Value))), $"month {t} actor {a}: closes");
        Check("decisions", dec.Fleet.SequenceEqual(w.Policy.AiFleetCommands(a, next)), $"month {t} actor {a}: fleet");
        Check("decisions", dec.Repeats.Length == w.ProductsLayout.Generations && Enumerable.Range(1, dec.Repeats.Length).All(g => dec.Repeats[g - 1] == w.Policy.Repeats(a, g)),
            $"month {t} actor {a}: repeats");
        See("decision compose", dec.DesignCommands.Any(c => c.Kind == DesignCommandKind.Compose));
        See("decision cancel", dec.DesignCommands.Any(c => c.Kind == DesignCommandKind.Cancel));
        See("decision start", !dec.Starts.IsEmpty);
        See("decision close", !dec.Closes.IsEmpty);
        See("decision fleet", !dec.Fleet.IsEmpty);
        See("decision repeat", dec.Repeats.Any(r => r > 0));
    }

    private void CheckInputs(SliceWorld w, ActorInputsView iv, Before before, int a, long t, bool afterDeal)
    {
        Check("inputs", iv.Actor == a && iv.Budget == before.Budget[a] && iv.Shares == before.Shares[a], $"month {t} actor {a}: budget or shares of the step");
        // R-12-1: the plan of the view is the plan the compute used (same rest, after the deals and the commands of the tick).
        ToolingLedgerView tl = iv.Tooling;
        Check("tooling", tl.Rest == w.Industry.LastRest(a), $"month {t} actor {a}: rest {tl.Rest.Raw} vs LastRest {w.Industry.LastRest(a).Raw}");
        Check("tooling", tl.TakenRest + tl.TakenCap == tl.Total && tl.RestReturned == tl.Rest - tl.TakenRest && tl.TrainingBudget == tl.CapBudget - tl.TakenCap
            && tl.TakenRest <= tl.Rest && tl.Total >= Fx6.Zero, $"month {t} actor {a}: ledger identities");
        Check("tooling", tl.UnmetByCluster.Length == w.IndustryLayout.Clusters.Length && tl.Participating >= 0, $"month {t} actor {a}: ledger shape");
        Check("tooling", tl.CapBudget == iv.Shares.Capability * iv.Budget, $"month {t} actor {a}: cap budget");
        // Control: a plan taken after the step would not be the plan of the step (so the equality above can fail).
        See("post-step plan differs", w.IndustryInputs.Plan(a, t, w.Industry.RestActor(a, t)).Total != tl.Total);
        See("tooling total", tl.Total > Fx6.Zero);
        See("tooling participating", tl.Participating > 0);
        See("tooling unmet", tl.UnmetByCluster.Any(u => u > Fx6.Zero));
        See("tooling training budget", tl.TrainingBudget > Fx6.Zero);
        See("deal month", w.Policy.LicenceStart(a) == t);
        // The view carries share_P·budget of the step plus the licence flow of the deals; strict equality, no disjunction.
        Check("inputs", iv.ProcurementBudget == iv.Shares.Procurement * iv.Budget + w.Policy.LicenceFlow(a), $"month {t} actor {a}: procurement budget = base + licence flow");
        See("licence month", w.Policy.LicenceStart(a) == t);

        if (afterDeal)
            See("inputs cell skipped for a licence deal", true);
        else
            Check("capability", iv.Capability == new CapabilityView(before.AlloySupply[a], before.Deficit[a]), $"month {t} actor {a}: capability");
        See("alloy deficit", iv.Capability.AlloyDeficit > Fx6.Zero);

        Check("inputs", iv.Designs.Length == w.ProductsLayout.Designs, $"month {t} actor {a}: input designs");
        for (int d = 0; d < iv.Designs.Length; d++)
        {
            DesignInputsView di = iv.Designs[d];
            Check("inputs", di.Design == d && di.Stage == before.Stage[a][d] && di.IdleMonths == before.Idle[a][d] && di.Hidden == before.Hidden[a][d]
                && di.GiveUp == before.GiveUp[a][d], $"month {t} actor {a} design {d}: inputs of the step");
            Check("inputs", (di.GiveUp is not null) == (di.Stage == DesignStage.Prototype), $"month {t} actor {a} design {d}: give-up class only in stage О");
            if (!afterDeal)
                Check("inputs", di.ArgP == before.ArgP[a][d] && di.YMin == before.YMin[a][d] && (di.ArgP < 0) == (di.Stage == DesignStage.None),
                    $"month {t} actor {a} design {d}: ymin or argp");
            See("input give-up processes", di.GiveUp == GiveUpClass.Processes);
            See("input give-up defects", di.GiveUp == GiveUpClass.Defects);
            See("input gate open", di.GateOpen);
            See("input admitted", di.Admitted);
            See("input idle", di.IdleMonths > 0);
        }
    }

    // The first minimum of the admitted yield in prc_set order (ties keep the earlier process); (-1, 0) without a set.
    private static (int ArgP, Fx6 YMin) ArgMin(SliceWorld w, int a, int d, long t)
    {
        int[] prc = w.Products.PrcSet(a, d);
        if (prc.Length == 0)
            return (-1, Fx6.Zero);
        int best = prc[0];
        Fx6 min = w.ProductsInputs.AdmittedYield(a, best, t);
        foreach (int p in prc.Skip(1))
        {
            Fx6 y = w.ProductsInputs.AdmittedYield(a, p, t);
            if (y < min)
            {
                min = y;
                best = p;
            }
        }
        return (best, min);
    }
}

/// <summary>Tests of the read views (SLICE_COMPOSITION §12): each view against direct calls of Core, month by month over one run.</summary>
public class ViewTests(ViewWalk walk) : IClassFixture<ViewWalk>
{
    private void Clean(params string[] groups)
    {
        foreach (string g in groups)
            Assert.True(!walk.Failures.TryGetValue(g, out List<string>? f), $"{g}: {string.Join(" | ", f ?? [])}");
    }

    private void Seen(params string[] what)
    {
        foreach (string w in what)
            Assert.True(walk.Seen.GetValueOrDefault(w) > 0, $"the run never showed '{w}': the check is not exercised");
    }

    [Fact]
    public void SliceView_EveryMonthCarriesTheStateOfTheTick_AndTheInputsOfTheStep() => Clean("core");

    [Fact]
    public void ActorView_MatchesCore_AndExercisesTheKeyAndTheYearlyFleetValue()
    {
        Clean("actor", "quality");
        Seen("fleet value", "fielded", "quality programme");
    }

    [Fact]
    public void NodeView_AndProcessView_MatchCore()
    {
        Clean("node", "process");
        Seen("process admitted", "process mastered", "process licence share", "node by licence");
    }

    [Fact]
    public void ProcessView_UnitCostMult_IsTheMultiplierOfTheSetOfOneProcess()
    {
        // Assumption 1 of §12.2: for the set of one process with unit weights Profile is exactly 1/max(yield, eps). With the layout's weights the same set gives
        // another number by fixed-point rounding (seen up to 4%), so the view does not use them.
        Clean("ucm");
        Seen("ucm with layout weights differs");
    }

    [Fact]
    public void DesignView_MatchesProducts_ForEveryStage()
    {
        Clean("design");
        Seen("design project", "design prototype", "design series", "design in service", "design cancelled", "design line bound", "design ai line");
    }

    [Fact]
    public void PolicyDecisionView_MatchesThePolicyReads_ForEveryKindOfDecision()
    {
        Clean("decisions");
        Seen("decision compose", "decision cancel", "decision start", "decision close", "decision fleet", "decision repeat");
    }

    [Fact]
    public void ToolingLedgerView_IsThePlanOfTheStep_AlsoInTheMonthOfADeal()
    {
        Clean("tooling", "inputs");
        Seen("post-step plan differs", "tooling total", "tooling participating", "tooling unmet", "tooling training budget", "deal month");
    }

    [Fact]
    public void CapabilityView_FollowsTheAlloyAndTheDisruption()
    {
        Clean("capability");
        Seen("alloy deficit", "inputs cell skipped for a licence deal");
    }

    [Fact]
    public void BureauView_MatchesTheGrantsAndTheRequirement()
    {
        Clean("bureau");
        Seen("bureau busy", "bureau granted");
    }

    [Fact]
    public void LicenceView_AndDisruptionView_MatchPolicy()
    {
        Clean("licence");
        Seen("licence held", "licence bought this month", "licence offer", "disruption");
    }

    [Fact]
    public void LicenceView_Refusals_CarryTheMoneyReason()
    {
        // Assumption 2 of §12.2: BadLicence has a money reason (ProcurementBase < price), counted in pol.lic.bad.money (Core: LicenceEventsTests). The view holds the four
        // counters in the order Self, Empty, Gen, Money and equals them in every month of the walk; the AI never ran into a refusal in this run.
        Assert.Equal(4, (int)BadLicence.Money);
        Assert.Contains(SliceTestData.Create().World.Table.Columns, c => c.Name == "pol.lic.bad.money");
        Clean("licence");
    }

    [Fact]
    public void SliceLayoutView_DescribesTheWorld()
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 3 });
        SliceWorld w = s.World;
        SliceLayoutView l = s.Layout;
        Assert.True(w.Scenario.ActorId.SequenceEqual(l.ActorIds));
        Assert.Equal(w.Graph.Number, l.NodeNumbers);
        Assert.Equal(w.Graph.Gen, l.NodeGen);
        Assert.Equal(w.IndustryLayout.Number, l.ProcessNumbers);
        Assert.Equal(w.IndustryLayout.Tier, l.ProcessTier);
        Assert.Equal(w.IndustryLayout.Clusters.Select(c => c.ToString()), l.Clusters);
        Assert.Equal(w.ProductsLayout.GenApp, l.GenApp);
        Assert.Equal((w.ProductsLayout.Designs, w.ProductsLayout.Bureaus, w.ProductsLayout.Generations, w.PolicyParameters.TAb),
            (l.Designs, l.Bureaus, l.Generations, l.TAb));
        Assert.Equal(w.IndustryParameters.MasteredYield, l.MasteredYield);
        Assert.Equal(w.PolicyParameters.FvBudget, l.FvBudget);
        Assert.Equal(l.ActorIds.Length, s.View.Actors.Length);
        Assert.Equal(l.NodeNumbers.Length, s.View.Actors[0].Nodes.Length);
        Assert.Equal(l.Designs, s.View.Actors[0].Designs.Length);
        Assert.Same(l, s.Layout);
    }

    [Fact]
    public void View_BeforeTheFirstStep_IsTheInitialState()
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 3 });
        SliceView v = s.View;
        Assert.Equal(-1, v.Month);
        Assert.Null(v.Inputs);
        HashAssert.Equal(s.LastHash, v.StateHash);
        Assert.Equal(0, v.Tick);
        Assert.Equal(s.Date, v.Date);
        Assert.All(v.Actors, a => Assert.Null(a.FleetValue));
        Assert.All(v.Actors, a => Assert.NotNull(a.Key));
    }

    [Fact]
    public void View_IsReplacedByANonEmptyTickOnly()
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 3 });
        s.StepThroughMonth(0);
        SliceView afterMonth = s.View;
        Assert.Equal(0, afterMonth.Month);
        Assert.NotNull(afterMonth.Inputs);
        s.Step(1);   // the second day of the month: nothing runs
        Assert.Same(afterMonth, s.View);

        // A tick that only applies a command: new hash and tick, the month and the step inputs stay (there was no monthly step).
        var shares = new BudgetShares(Fx6.FromRaw(300_000), Fx6.FromRaw(300_000), Fx6.FromRaw(300_000), Fx6.FromRaw(100_000), Fx6.Zero);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new SetBudgetShares(0, shares)).Accepted);
        s.Step(1);
        SliceView afterCommand = s.View;
        Assert.NotSame(afterMonth, afterCommand);
        Assert.Equal(0, afterCommand.Month);
        Assert.Null(afterCommand.Inputs);
        HashAssert.Equal(s.LastHash, afterCommand.StateHash);
        HashAssert.NotEqual(afterMonth.StateHash, afterCommand.StateHash);
        Assert.Equal(s.Tick, afterCommand.Tick);
        s.StepThroughMonth(1);
        Assert.Equal(1, s.View.Month);
        Assert.Equal(1, s.View.Inputs!.Month);
        // The player's shares are in force for the step: the inputs show them.
        Assert.Equal(shares, s.View.Inputs.Actors[0].Shares);
    }

    [Fact]
    public void Views_AreImmutable_NoMutableCollectionsNoSetters()
    {
        Type[] viewTypes = [.. typeof(SliceView).Assembly.GetExportedTypes()
            .Where(t => t.Namespace == "Singularity.Session" && t.Name.EndsWith("View", StringComparison.Ordinal) && !t.IsEnum)];
        Assert.True(viewTypes.Length >= 18);
        var offenders = new List<string>();
        foreach (Type t in viewTypes)
        {
            foreach (PropertyInfo p in t.GetProperties())
            {
                bool init = p.SetMethod is null || p.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));
                if (!init)
                    offenders.Add($"{t.Name}.{p.Name} has a setter");
                if (!IsImmutableShape(p.PropertyType, viewTypes))
                    offenders.Add($"{t.Name}.{p.Name}: {p.PropertyType} is not on the immutable whitelist");
            }
            offenders.AddRange(t.GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => $"{t.Name}.{f.Name} is a public field"));
        }
        Assert.Empty(offenders);
    }

    // Whitelist: primitives, Fx6, enums, view types, NodeList, FleetCommand and DesignHandle (readonly record structs of an enum, numbers and a handle), ImmutableArray of the same.
    private static bool IsImmutableShape(Type t, Type[] viewTypes)
    {
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ImmutableArray<>))
            return IsImmutableShape(t.GetGenericArguments()[0], viewTypes);
        if (Nullable.GetUnderlyingType(t) is { } inner)
            return IsImmutableShape(inner, viewTypes);
        if (t == typeof(DesignHandle))
            return typeof(DesignHandle).GetProperties().All(p => p.SetMethod is null || p.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)))
                && typeof(DesignHandle).IsValueType;
        if (t == typeof(Singularity.Core.Products.AlloyCoverage))   // a readonly record struct of three Fx6 values
            return typeof(Singularity.Core.Products.AlloyCoverage).GetProperties().All(p => p.SetMethod is null || p.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)))
                && typeof(Singularity.Core.Products.AlloyCoverage).IsValueType;
        if (t == typeof(FleetCommand))
            return typeof(FleetCommand).GetProperties().All(p => p.SetMethod is null || p.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)))
                && typeof(FleetCommand).IsValueType;
        return t.IsPrimitive || t == typeof(string) || t == typeof(Fx6) || t.IsEnum || viewTypes.Contains(t) || t == typeof(BudgetShares) || t == typeof(CivilDate) || t == typeof(TechKey) || t == typeof(LicenceMask) || t == typeof(NodeList);   // NodeList: a readonly struct over arrays that are never written after the build
    }

    [Fact]
    public void Views_DoNotChangeTheHash_AndAreEqualAcrossThreads_L1()
    {
        // The published view is a function of the state: two runs at different thread counts give the same hash and the same view every month,
        // and the hash is the golden of seed 1 (F17b, 150 months), which was recorded before the views existed.
        SliceSession a = SliceTestData.Create(new SliceRunConfig { Seed = 1, Threads = 2 });
        SliceSession b = SliceTestData.Create(new SliceRunConfig { Seed = 1, Threads = 1, ChunkSize = 2 });
        SliceSession c = SliceTestData.Create(new SliceRunConfig { Seed = 1, Threads = 4, ChunkSize = 1, ViewMode = SliceViewMode.Calibration });
        for (long t = 0; t < 150; t++)
        {
            a.StepThroughMonth(t);
            b.StepThroughMonth(t);
            c.StepThroughMonth(t);
            HashAssert.Equal(a.LastHash, b.LastHash);
            HashAssert.Equal(a.LastHash, c.LastHash);
            Assert.True(ViewSupport.Dump(a.View) == ViewSupport.Dump(b.View), $"month {t}: the view differs between thread counts");
        }
        // Re-recorded in TASK-0110 (DEBT-0054), reason: schema v10 (new columns prd.altReached.*, novelty memory per actor); the old columns are unchanged per the projection tests in this branch (T6dOff, FleetAblation, ToolingUnmet), so only the new columns change the hash.
        // Re-recorded in TASK-0115: schema v11 (cmd.*.gen, pol.*.gen columns); the old columns are unchanged (projection tests).
        // Re-recorded in TASK-0121 (#423), reason: schema v12 (new columns prd.d*.cohort.1978…2030 and prd.d*.q.1: the cohort years to 2030, the second block of the q mask); the old columns are unchanged (HorizonTests projection at 48 points of the build before the change, GoldenProjectionAgainstMainTests), so only the new columns change the hash.
        // Re-recorded in TASK-0129 (fleet ageing, #431/#435-#437), reason: schema v13 (new column prd.d*.replCredit, in the hash from month 0); the old columns are unchanged to month 176 (projection tests: HorizonTests, T6dTests, FleetAblationTests, ToolingUnmetTests, GoldenProjectionAgainstMainTests), so only the new column changes these hashes.
        // Re-recorded in TASK-0133 (train 2: G5/G6 data + fleet ageing): nodes 62-81 and the G5/G6 alternatives change every trajectory; reason and list in the task file.
        // Re-recorded in TASK-0147 (train 2): nodes 82-99 (G5/G6 rev 3.1 chains) and the ahead-of-time cost defaults k 0.02, pow 2, max 10 (#465) change every trajectory on the current data; explanation: docs/simulation/golden-rerecord-train2.md.
        HashAssert.Equal("7720220e04795975c4e9e8e7b11d50c51976106a68cb89f0464c416db3baa087", a.LastHash);
    }

    [Fact]
    public void CalibrationMode_HasNoKeyNoFleetValue_AndNoWhyNot()
    {
        SliceSession full = SliceTestData.Create(new SliceRunConfig { Seed = 2 });
        SliceSession calib = SliceTestData.Create(new SliceRunConfig { Seed = 2, ViewMode = SliceViewMode.Calibration });
        for (long t = 0; t < 61; t++)
        {
            full.StepThroughMonth(t);
            calib.StepThroughMonth(t);
            Assert.All(calib.View.Actors, a => Assert.True(a.Key is null && a.FleetValue is null));
            HashAssert.Equal(full.LastHash, calib.LastHash);
            // Everything else of the view is the same as in Full mode.
            for (int a = 0; a < full.View.Actors.Length; a++)
                Assert.True(ViewSupport.Dump(full.View.Actors[a] with { Key = null, FleetValue = null }) == ViewSupport.Dump(calib.View.Actors[a]), $"month {t} actor {a}");
            Assert.True(ViewSupport.Dump(WithoutRequirement(full.View.Inputs!)) == ViewSupport.Dump(calib.View.Inputs));
            Assert.All(calib.View.Inputs!.Actors, a => Assert.Null(a.Requirement));   // FV is not read in the calibration mode
        }
        Assert.Contains(full.View.Actors, a => a.FleetValue is not null);
        Assert.Throws<InvalidOperationException>(() => calib.WhyNot(0, WhyNotTarget.Requirement(2)));
        Assert.Throws<InvalidOperationException>(() => calib.CurrentRequirement(0));
        Assert.NotEmpty(full.WhyNot(0, WhyNotTarget.Requirement(2)));   // Full mode still answers
    }

    private static MonthInputsView WithoutRequirement(MonthInputsView v) => v with { Actors = [.. v.Actors.Select(a => a with { Requirement = null })] };

    [Fact]
    public void DesignInputsView_GiveUp_IsNullWithoutTheProductAi()
    {
        // A scripted player drives the product layer alone: the AI's classifier has nothing to read, the views must still be built.
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 7, ProductAi = false });
        var player = new PlayerScript(s);
        bool prototype = false;
        for (long t = 0; t < 60; t++)
        {
            player.Issue(t);
            s.StepThroughMonth(t);
            prototype |= s.View.Inputs!.Actors.SelectMany(a => a.Designs).Any(d => d.Stage == DesignStage.Prototype);
            Assert.All(s.View.Inputs.Actors.SelectMany(a => a.Designs), d => Assert.Null(d.GiveUp));
        }
        Assert.True(prototype, "no design reached stage О: the case is not exercised");
    }
}
