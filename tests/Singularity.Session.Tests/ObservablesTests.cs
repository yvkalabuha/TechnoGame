using System.Collections.Immutable;
using Singularity.Core.Commands;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>
/// The step traces and the Requirement variants in the views (TASK-0047, SLICE_COMPOSITION §13): one walk per profile cell (all three great powers on
/// one profile) checks the invariants of §13.8 against direct calls of Core on the same state.
/// </summary>
public sealed class ObservablesWalk
{
    public const int Months = 200;

    public readonly Dictionary<string, List<string>> Failures = new();
    public readonly Dictionary<string, int> Seen = new();

    public ObservablesWalk(int cell)
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 5, ProfileCell = cell, Threads = 2, LicKnows = true });
        SliceWorld w = s.World;
        for (long t = 0; t < Months; t++)
        {
            s.StepThroughMonth(t);
            SliceView v = s.View;
            if (v.Step is not { } step || v.Inputs is not { } inputs || step.Month != t || step.Actors.Length != w.Scenario.ActorCount)
            {
                Check("step", false, $"month {t}: step missing or misdated");
                continue;
            }
            for (int a = 0; a < w.Scenario.ActorCount; a++)
            {
                CheckMoney(w, step.Actors[a], a, t);
                CheckRefusals(step.Actors[a], a, t);
                CheckCloses(v.Actors[a], a, t);
                CheckRequirement(w, v.Actors[a], inputs.Actors[a], a, t);
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

    private void CheckMoney(SliceWorld w, ActorStepView av, int a, long t)
    {
        if (av.Money is not { } money)
        {
            Check("money", false, $"month {t} actor {a}: no money trace in the full model");
            return;
        }
        ProductsSystem p = w.Products;
        Fx6 left = money.LeftStart, fromLeft = Fx6.Zero, fromTraining = Fx6.Zero;
        int previous = -1;
        foreach (LineMoneyView l in money.Lines)
        {
            Check("money", l.Design > previous && p.Stage(a, l.Design) == DesignStage.Series, $"month {t} actor {a}: lines out of order or not in С");
            previous = l.Design;
            Check("money", l.LeftIn == left - fromLeft, $"month {t} actor {a} design {l.Design}: LeftIn {l.LeftIn} != LeftStart − Σ FromLeft");
            Check("money", l.FromLeft >= Fx6.Zero && l.FromLeft <= l.LeftIn && l.FromTraining >= Fx6.Zero && l.FromTraining <= l.Training,
                $"month {t} actor {a} design {l.Design}: a payment above what the line saw");
            Check("money", l.FromLeft + l.FromTraining == p.Rate(a, l.Design) * p.UnitCost(a, l.Design), $"month {t} actor {a} design {l.Design}: payment != rate · unit cost");
            See("money line", true);
            See("training money", l.FromTraining > Fx6.Zero);
            See("money bound", p.Bind(a, l.Design) == LineBind.Money);
            fromLeft += l.FromLeft;
            fromTraining += l.FromTraining;
        }
        Check("money", fromLeft <= money.LeftStart, $"month {t} actor {a}: Σ FromLeft above LeftStart");
        Check("money", fromTraining == p.TrainingSpent(a), $"month {t} actor {a}: Σ FromTraining != prd.tbSpent");
        Check("money", p.Spent(a) - p.UpkeepNeeded(a) >= Fx6.Zero && money.LeftStart >= Fx6.Zero, $"month {t} actor {a}: negative money");
    }

    private void CheckRefusals(ActorStepView av, int a, long t)
    {
        int previous = -1;
        foreach (LicenceRefusalView r in av.LicenceMoneyRefusals)
        {
            Check("refusal", r.Seller > previous && r.Seller != a && r.Price > r.Room && r.Gen > 0, $"month {t} actor {a}: refusal {r}");
            previous = r.Seller;
            See("refusal", true);
        }
    }

    private void CheckCloses(ActorView av, int a, long t)
    {
        foreach (CloseView c in av.Decisions.Closes)
        {
            Check("close", c.Reason != LineCloseReason.None, $"month {t} actor {a} design {c.Design}: a close without a reason");
            See("close " + c.Reason, true);
        }
    }

    private void CheckRequirement(SliceWorld w, ActorView av, ActorInputsView inputs, int a, long t)
    {
        if (t % 12 != 0)
        {
            Check("requirement", inputs.Requirement is null, $"month {t} actor {a}: a Requirement outside a yearly month");
            return;
        }
        if (inputs.Requirement is not { } r)
        {
            Check("requirement", false, $"month {t} actor {a}: no Requirement in a yearly month");
            return;
        }
        DesignCommandView? compose = av.Decisions.DesignCommands.FirstOrDefault(c => c.Kind == DesignCommandKind.Compose);
        See("requirement count " + r.Count, true);
        if (compose is not { } command)
            return;
        Check("requirement", r.Count > 0 && command.Target == r.Gen, $"month {t} actor {a}: compose target {command.Target} against variants of G{r.Gen}");
        if (r.Count == 0)
            return;
        ImmutableArray<int> chosen = av.Profile switch
        {
            PolicyProfile.Quality => r.Superior,
            PolicyProfile.Mass => r.Count == 2 ? r.Producible : r.Superior,
            _ => r.Count == 2 && r.FvProducible > r.FvSuperior ? r.Producible : r.Superior,
        };
        // A41 SLOT for P2 and P3 (contract §4.4a r13): Core's firing slots F say which slots take superior's alternative.
        if (av.Profile != PolicyProfile.Quality && r.Count == 2 && chosen.SequenceEqual(r.Producible) && !r.PilotSlots.IsEmpty)
        {
            chosen = [.. chosen.Select((alt, slot) => r.PilotSlots.Contains(slot) ? r.Superior[slot] : alt)];
            See("a41 fired", true);
        }
        Check("requirement", command.Alts.SequenceEqual(chosen), $"month {t} actor {a} ({av.Profile}): the compose command is not the profile's choice among the variants");
        See("compose " + av.Profile, true);
    }
}

public class ObservablesTests
{
    private static readonly Lazy<ObservablesWalk>[] Walks = [.. new[] { 0, 13, 26 }.Select(c => new Lazy<ObservablesWalk>(() => new ObservablesWalk(c)))];

    private static IEnumerable<ObservablesWalk> All => Walks.Select(w => w.Value);

    [Fact]
    public void MoneyTrace_KeepsItsInvariants_InEveryMonthOfTheWalks()
    {
        foreach (ObservablesWalk walk in All)
        {
            Assert.True(walk.Failures.GetValueOrDefault("step") is null, string.Join("; ", walk.Failures.GetValueOrDefault("step") ?? []));
            Assert.True(walk.Failures.GetValueOrDefault("money") is null, string.Join("; ", walk.Failures.GetValueOrDefault("money") ?? []));
            Assert.True(walk.Seen.GetValueOrDefault("money line") > 100, "the walk sees too few lines in С");
        }
        Assert.Contains(All, w => w.Seen.GetValueOrDefault("training money") > 0);
    }

    [Fact]
    public void CloseReasons_AreSetForEveryCloseOfAStepTick()
    {
        foreach (ObservablesWalk walk in All)
            Assert.True(walk.Failures.GetValueOrDefault("close") is null, string.Join("; ", walk.Failures.GetValueOrDefault("close") ?? []));
        Assert.Contains(All, w => w.Seen.Keys.Any(k => k.StartsWith("close ", StringComparison.Ordinal)));
    }

    [Fact]
    public void RequirementVariants_AreInYearlyMonthsOnly_AndTheProfilesChoiceEqualsTheComposeCommandOfTheSameStep()
    {
        foreach (ObservablesWalk walk in All)
            Assert.True(walk.Failures.GetValueOrDefault("requirement") is null, string.Join("; ", walk.Failures.GetValueOrDefault("requirement") ?? []));
        foreach (string profile in new[] { "Quality", "Mass", "Adaptive" })
            Assert.Contains(All, w => w.Seen.GetValueOrDefault("compose " + profile) > 0);
        Assert.Contains(All, w => w.Seen.GetValueOrDefault("a41 fired") > 0);
    }

    [Fact]
    public void Month0Compose_IsInTheInitialViewsDecisions_AndPairsWithTheRequirementOfTheMonth0Inputs()
    {
        // Initialize decides month 0 before tick 0; the initial view (month −1) shows those commands (they act from month 0), the month-0 view
        // shows the Requirement the step saw. Later yearly months are paired inside one view (Decisions of month t and Requirement of t).
        foreach (int cell in Enumerable.Range(0, 27))
        {
            SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 5, ProfileCell = cell });
            SliceView initial = s.View;
            Assert.Equal(-1, initial.Month);
            s.StepThroughMonth(0);
            int paired = 0;
            for (int a = 0; a < s.View.Actors.Length; a++)
            {
                DesignCommandView? compose = initial.Actors[a].Decisions.DesignCommands.FirstOrDefault(c => c.Kind == DesignCommandKind.Compose);
                if (compose is not { } command)
                    continue;
                RequirementView r = s.View.Inputs!.Actors[a].Requirement!;
                ImmutableArray<int> chosen = initial.Actors[a].Profile switch
                {
                    PolicyProfile.Quality => r.Superior,
                    PolicyProfile.Mass => r.Count == 2 ? r.Producible : r.Superior,
                    _ => r.Count == 2 && r.FvProducible > r.FvSuperior ? r.Producible : r.Superior,
                };
                Assert.Equal(r.Gen, command.Target);
                Assert.True(command.Alts.SequenceEqual(chosen), $"cell {cell} actor {a}");
                paired++;
            }
            for (int a = 0; a < initial.Actors.Length; a++)
                Assert.Equal(s.World.Policy.AiDesignCommands(a, 0).Count, initial.Actors[a].Decisions.DesignCommands.Length);   // the initial view shows the commands of month 0
        }
        // The core data gives no compose at month 0 (every actor with an APP has a live G1 project, E1), so the pairing itself is proved on a snapshot fixture
        // in ProductAiTests.Variants_ListBothCandidatesWithTheirFV_*: RequirementVariants(0, 0) against the compose command Initialize made for month 0.
    }

    [Fact]
    public void MoneyRefusals_AreWellFormed()
    {
        foreach (ObservablesWalk walk in All)
            Assert.True(walk.Failures.GetValueOrDefault("refusal") is null, string.Join("; ", walk.Failures.GetValueOrDefault("refusal") ?? []));
        Assert.Contains(All, w => w.Seen.GetValueOrDefault("refusal") > 0);
    }

    [Fact]
    public void Step_IsNullBeforeTheFirstStep_InAnEmptyTick_AndInACommandTick_AndSetInAMonthlyTick()
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 3 });
        Assert.Null(s.View.Step);
        Assert.Null(s.View.Inputs);
        s.StepThroughMonth(0);
        Assert.Equal(0, s.View.Step!.Month);
        SliceView afterMonth = s.View;
        s.Step(1);
        Assert.Same(afterMonth, s.View);                // an empty tick publishes nothing
        var shares = new BudgetShares(Fx6.FromRaw(300_000), Fx6.FromRaw(300_000), Fx6.FromRaw(300_000), Fx6.FromRaw(100_000), Fx6.Zero);
        Assert.True(s.Submit(SourceClass.Player, 0, 0, new SetBudgetShares(0, shares)).Accepted);
        s.Step(1);
        Assert.Null(s.View.Step);                        // the trace of month 0 is not re-issued by a command tick
        Assert.All(s.View.Actors, a => Assert.All(a.Decisions.Closes, c => Assert.Equal(LineCloseReason.None, c.Reason)));
        s.StepThroughMonth(1);
        Assert.Equal(1, s.View.Step!.Month);
    }

    [Fact]
    public void Step_AndRequirement_InCalibrationMode_StepIsThereAndRequirementIsNot()
    {
        SliceSession calib = SliceTestData.Create(new SliceRunConfig { Seed = 3, ViewMode = SliceViewMode.Calibration });
        SliceSession full = SliceTestData.Create(new SliceRunConfig { Seed = 3 });
        for (long t = 0; t < 25; t++)
        {
            calib.StepThroughMonth(t);
            full.StepThroughMonth(t);
            Assert.NotNull(calib.View.Step);
            Assert.All(calib.View.Inputs!.Actors, a => Assert.Null(a.Requirement));
            if (t % 12 == 0)
                Assert.All(full.View.Inputs!.Actors, a => Assert.NotNull(a.Requirement));
            HashAssert.Equal(full.LastHash, calib.LastHash);     // the mode changes no state
        }
    }

    [Fact]
    public void WithoutTheProductAi_ThereIsNoRequirement_ButTheMoneyStepIsThere()
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 3, ProductAi = false });
        s.StepThroughMonth(0);
        Assert.All(s.View.Inputs!.Actors, a => Assert.Null(a.Requirement));
        Assert.All(s.View.Step!.Actors, a => Assert.NotNull(a.Money));
        Assert.All(s.View.Step!.Actors, a => Assert.Empty(a.LicenceMoneyRefusals));
    }

    [Fact]
    public void NodeView_KnownSince_IsNullInEveryReachableSeries()
    {
        foreach (LayerControl layer in new[] { LayerControl.Full, LayerControl.Lk0, LayerControl.Lc0 })
        {
            SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 3, Layer = layer });
            s.StepThroughMonth(2);
            Assert.All(s.View.Actors, a => Assert.All(a.Nodes, n => Assert.Null(n.KnownSince)));
            Assert.DoesNotContain(s.World.Table.Columns, c => c.Name.StartsWith("know.since.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Step_IsL1AndL2Deterministic_ThreadsAndChunksDoNotChangeIt()
    {
        string Run(int threads, int chunk)
        {
            SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = 9, LicKnows = true, Threads = threads, ChunkSize = chunk });
            var sb = new System.Text.StringBuilder();
            for (long t = 0; t < 30; t++)
            {
                s.StepThroughMonth(t);
                sb.Append(ViewSupport.Dump(s.View.Step)).Append(ViewSupport.Dump(s.View.Inputs!.Actors.Select(a => a.Requirement)))
                  .Append(ViewSupport.Dump(s.View.Actors.Select(a => a.Decisions.Closes)));
            }
            return sb.ToString();
        }
        string one = Run(1, 1);
        Assert.Equal(one, Run(1, 1));
        Assert.True(one == Run(4, 1), "4 threads");
        Assert.True(one == Run(8, 2), "8 threads, chunk 2");
    }
}
