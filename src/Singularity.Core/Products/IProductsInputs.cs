using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Kernel;

namespace Singularity.Core.Products;

/// <summary>A design order in force at month t (AI/player command, spec §4): generation and chosen variant.</summary>
public readonly record struct DesignOrder(int Gen, DesignVariant Variant);

/// <summary>
/// What the product layer reads from other layers and from commands in the compute of month t. Contract (as
/// <c>IIndustryInputs</c>): every method is a pure function of the snapshot t−1 (the state after month t−1, holding the
/// decisions in force at t) and of its arguments; no side effects, thread-safe. The layer has no RNG (#28): nothing
/// here may draw one on its behalf either.
/// </summary>
public interface IProductsInputs
{
    /// <summary><c>known(actor, node)</c> by stable node number in the knowledge snapshot (contract §5).</summary>
    bool Knows(int actorRow, int nodeNumber, long month);

    /// <summary>
    /// <c>yield_p</c> of the process on the site for month t, 0 when the owner does not know it (the value capability
    /// runs output at, contract §4.2). Read by the «технологичный» variant rule (spec §1.4).
    /// </summary>
    Fx6 AdmittedYield(int siteRow, int processNumber, long month);

    /// <summary><c>production_profile(site, prc_set)</c> of month t (contract §5).</summary>
    ProductionProfile Profile(int siteRow, ReadOnlySpan<PrcSetItem> prcSet, long month);

    /// <summary>Alloy supply of the site in month t, kg (spec §1.4, §3.5 A23).</summary>
    Fx6 AlloySupply(int siteRow, long month);

    /// <summary>
    /// <c>granted</c> of the request this layer made in month t−1 (contract §4.6: visible at t+1). Category and
    /// requester id as in <see cref="ProductsLayout"/>.
    /// </summary>
    long Granted(int siteRow, int category, int requesterId, long month);

    /// <summary>Procurement budget of month t: <c>share_P(profile)·budget_a(t)</c> (spec §3.3, §3.4); ≥ 0.</summary>
    Fx6 ProcurementBudget(int actorRow, long month);

    /// <summary>A design order in force at month t, or null (spec §4: yearly <c>Requirement</c> + variant choice).</summary>
    DesignOrder? Order(int actorRow, long month);

    /// <summary>Launch the series of a design in stage О or В (spec §4 "запуск серии").</summary>
    bool LaunchSeries(int actorRow, int design, long month);

    /// <summary>Close the line of a design in series (stage С → В).</summary>
    bool CloseLine(int actorRow, int design, long month);

    /// <summary>Line rate the AI asks for in month t, units per month (spec §4 "темп линий"); ≥ 0.</summary>
    Fx6 LineRate(int actorRow, int design, long month);

    /// <summary>L_P only: month index in whose merge the APP node of the generation became known to the actor, −1 if unknown.</summary>
    long AppKnownSince(int actorRow, int gen, long month);
}
