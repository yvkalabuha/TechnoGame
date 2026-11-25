using Singularity.Kernel;

namespace Singularity.Core.Knowledge;

/// <summary>
/// What the knowledge layer reads from other layers in the compute of month t.
/// Contract: every method is a pure function of the snapshot t−1 (the Table state after month t−1, which
/// already holds the decisions in force at t, ADR-0006) and of its arguments. No hidden state, no side
/// effects, same answer on every call, safe to call from several threads (compute runs in parallel over
/// actor chunks). Implementations read Table columns; they never see this month's compute output.
/// </summary>
/// <summary>One working institute slot of a month in the slot model: the node row, the institute multiplier `m_inst` and the focus level 0..2.</summary>
public readonly record struct SlotWork(int Slot, int NodeRow, Fx6 InstituteMultiplier, int Focus);

public interface IKnowledgeInputs
{
    /// <summary>
    /// Slot model: the working slots of the actor in month t, one entry per slot that has a node (at most the length of <paramref name="work"/>); returns the count.
    /// A pure function of the snapshot, the same as the institutes decide for the next one. Used only by a system built with slot parameters.
    /// </summary>
    int SlotWork(int actorRow, long month, Span<SlotWork> work) => 0;

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

    /// <summary>
    /// The priority goals of the actor in month t (research-institutes-priorities §4): node rows in slot order with the speed multiplier of the institute
    /// level in the area of each. Returns the count (at most the length of the spans); 0 = no priorities, the knowledge layer then runs its old path.
    /// Used only by a system built with priority parameters.
    /// </summary>
    int PriorityGoals(int actorRow, long month, Span<int> nodeRows, Span<Fx6> multipliers) => 0;

    /// <summary>
    /// The theft discount of the actor on the node in month t, a share of the node cost (<c>s_steal</c>, INTELLIGENCE.md §2.2); zero without intelligence.
    /// It enters <c>c_eff</c> as <c>max(k_diff·known, s_steal)</c> and nowhere else (O-051).
    /// </summary>
    Fx6 StealShare(int actorRow, int nodeRow, long month) => Fx6.Zero;

    /// <summary>
    /// The one-off programme of the actor in month t (the player's <c>fund_programme</c> paid from the reserve): node row and the amount, already
    /// cut to the reserve balance of the snapshot. False when there is none. Used only by a system built with programmes.
    /// </summary>
    bool FundProgramme(int actorRow, long month, out int nodeRow, out Fx6 amount)
    {
        nodeRow = -1;
        amount = Fx6.Zero;
        return false;
    }
}
