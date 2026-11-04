using Singularity.Kernel;

namespace Singularity.Core.Knowledge;

/// <summary>
/// What the knowledge layer reads from other layers in the compute of month t.
/// Contract: every method is a pure function of the snapshot t−1 (the Table state after month t−1, which
/// already holds the decisions in force at t, ADR-0006) and of its arguments. No hidden state, no side
/// effects, same answer on every call, safe to call from several threads (compute runs in parallel over
/// actor chunks). Implementations read Table columns; they never see this month's compute output.
/// </summary>
public interface IKnowledgeInputs
{
    /// <summary><c>S_a(t) = share_R·budget_a(t)</c> (spec §3.1, §3.4).</summary>
    Fx6 ResearchBudget(int actorRow, long month);

    /// <summary><c>prio(profile, type_n, ★)</c> in force at t (§4).</summary>
    Fx6 Priority(int actorRow, int nodeRow, long month);

    /// <summary>Spread multiplier <c>j</c> of the year in force at t (§3.1, §3.5 A15).</summary>
    Fx6 J(int actorRow, int nodeRow, long month);

    /// <summary><c>discipline_volume(actor, d)</c> of month t−1 (contract §4.3, §5); see <see cref="KnowledgeMath.DisciplineVolume"/>.</summary>
    Fx6 DisciplineVolume(int actorRow, int discipline, long month);

    /// <summary>
    /// Node rows (the seller's engine ENG node and the PRC nodes of the licence mask) of the licence the actor bought in month t−1; used only by the
    /// <c>lic_knows</c> diagnostic (§6.7), applied in the merge of month t.
    /// </summary>
    ReadOnlySpan<int> LicensedEngines(int actorRow, long month);
}
