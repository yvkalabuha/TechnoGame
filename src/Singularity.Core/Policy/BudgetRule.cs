using System.Collections.Immutable;
using Singularity.Kernel;

namespace Singularity.Core.Policy;

/// <summary>
/// <c>BUDGET_RULE</c> (slice-budget-convergence.md §3, §4, #430): a measurement switch of the Lab, not the accepted rule.
/// <see cref="Now"/> is the constant growth of main; <see cref="A"/> pulls the budget to a share of the frontier (USA); <see cref="B1"/> and
/// <see cref="B2"/> cap the budget at a share of the frontier and otherwise grow as before.
/// </summary>
public enum BudgetRule
{
    Now = 0,
    A = 1,
    B1 = 2,
    B2 = 3,
}

/// <summary>Parsing of the <c>BUDGET_RULE</c> manifest switch.</summary>
public static class BudgetRules
{
    /// <summary>Parses <c>NOW</c>, <c>A</c>, <c>B1</c> or <c>B2</c>; any other value is a manifest error.</summary>
    public static BudgetRule Parse(string value) => value switch
    {
        "NOW" => BudgetRule.Now,
        "A" => BudgetRule.A,
        "B1" => BudgetRule.B1,
        "B2" => BudgetRule.B2,
        _ => throw new ArgumentException($"Unknown BUDGET_RULE value '{value}' (expected NOW, A, B1 or B2).", nameof(value)),
    };
}

/// <summary>
/// The variant constants of a <see cref="BudgetRule"/> by table row, resolved once at world build from the actor ids. A row without a preset
/// (the frontier itself and any actor the note does not list) follows the growth of main. The constants are measurement constants of the note,
/// not data of the scenario and not columns.
/// </summary>
public sealed class BudgetRulePlan
{
    private BudgetRulePlan(BudgetRule rule, int frontierRow, ImmutableArray<Fx6> share, ImmutableArray<Fx6> lambdaPerYear, ImmutableArray<Fx6> ceiling)
    {
        Rule = rule;
        FrontierRow = frontierRow;
        Share = share;
        LambdaPerYear = lambdaPerYear;
        Ceiling = ceiling;
    }

    public BudgetRule Rule { get; }

    /// <summary>Row of the frontier actor (USA), found by its id at build, never by a bare index.</summary>
    public int FrontierRow { get; }

    /// <summary><c>s_a</c> of rule A by row; zero = no preset.</summary>
    public ImmutableArray<Fx6> Share { get; }

    /// <summary><c>λ_pa</c> of rule A by row.</summary>
    public ImmutableArray<Fx6> LambdaPerYear { get; }

    /// <summary>Ceiling <c>c_a</c> of rule B by row; zero = no ceiling.</summary>
    public ImmutableArray<Fx6> Ceiling { get; }

    /// <summary>Resolves the presets of <paramref name="rule"/> for the actors of the scenario (row order).</summary>
    public static BudgetRulePlan For(BudgetRule rule, ImmutableArray<string> actorIds)
    {
        if (!Enum.IsDefined(rule))
            throw new ArgumentOutOfRangeException(nameof(rule), rule, "Unknown budget rule.");
        int frontier = actorIds.IndexOf("USA");
        if (rule != BudgetRule.Now && frontier < 0)
            throw new ArgumentException("BUDGET_RULE other than NOW needs the actor USA as the frontier.", nameof(actorIds));
        var share = new Fx6[actorIds.Length];
        var lambda = new Fx6[actorIds.Length];
        var ceiling = new Fx6[actorIds.Length];
        for (int row = 0; row < actorIds.Length; row++)
        {
            switch (rule)
            {
                case BudgetRule.A:
                    (share[row], lambda[row]) = PresetA(actorIds[row]);
                    break;
                case BudgetRule.B1:
                    ceiling[row] = PresetB(actorIds[row], b2: false);
                    break;
                case BudgetRule.B2:
                    ceiling[row] = PresetB(actorIds[row], b2: true);
                    break;
            }
        }
        return new BudgetRulePlan(rule, frontier, [.. share], [.. lambda], [.. ceiling]);
    }

    // Note §3 table: s and λ_pa (per year) by actor.
    private static (Fx6 Share, Fx6 Lambda) PresetA(string actor) => actor switch
    {
        "GBR" => (Fx6.FromRaw(120_000), Fx6.FromRaw(25_000)),
        "SUN" => (Fx6.FromRaw(440_000), Fx6.FromRaw(60_000)),
        "FRA" => (Fx6.FromRaw(170_000), Fx6.FromRaw(80_000)),
        "SWE" => (Fx6.FromRaw(27_000), Fx6.FromRaw(60_000)),
        _ => default,
    };

    // Note §4 table: B1 "just above the old 1977", B2 "the historical peak". GBR has no ceiling.
    private static Fx6 PresetB(string actor, bool b2) => actor switch
    {
        "SUN" => Fx6.FromRaw(b2 ? 430_000 : 520_000),
        "FRA" => Fx6.FromRaw(b2 ? 196_000 : 230_000),
        "SWE" => Fx6.FromRaw(b2 ? 32_000 : 36_000),
        _ => default,
    };
}
