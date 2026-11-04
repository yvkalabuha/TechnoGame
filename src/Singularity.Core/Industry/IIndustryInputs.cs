using Singularity.Core.Data;
using Singularity.Kernel;

namespace Singularity.Core.Industry;

/// <summary>
/// What the capability layer reads from other layers in the compute of month t. Contract (as
/// <c>IKnowledgeInputs</c>): every method is a pure function of the snapshot t−1 (the state after month
/// t−1, holding the decisions in force at t) and of its arguments; no side effects, thread-safe. The layer
/// has no RNG (#28): nothing here may draw one on its behalf either. Every member is required: a missing
/// input must be a decision of the implementer, not a silent zero.
/// </summary>
public interface IIndustryInputs
{
    /// <summary><c>p ∈ known_processes(owner(site))</c> in the snapshot (contract §5).</summary>
    bool KnowsProcess(int siteRow, int processRow, long month);

    /// <summary><c>w[c]</c> (spec §3.2).</summary>
    Fx6 W(int siteRow, int cluster, long month);

    /// <summary><c>Hcap[site,d]</c> (contract §4.1); positive.</summary>
    Fx6 Hcap(int siteRow, Dimension dim, long month);

    /// <summary>A quality programme is active on the site (<c>α_eff</c> on quality_control, contract §4.1, §7).</summary>
    bool QualityProgramme(int siteRow, long month);

    /// <summary>
    /// Money invested into <c>tooling_M[site,c,d]</c> in month t (spec §3.5 A11, A39); ≥ 0. <paramref name="restActor"/> = <c>rest_a</c> of
    /// the effort allocation of the site's owner (contract §4.1b step 5): what the process-development item did not spend. The
    /// industry layer calls this overload for every cell of every site. <c>rest_a</c> is one value per actor: every site and every
    /// cell of the actor receives the same amount, so the rule must share it among them (it is not per site or per cell); what the
    /// rule does not take stays with the actor's budget (#118).
    /// </summary>
    Fx6 ToolingInvest(int siteRow, int cluster, Dimension dim, long month, Fx6 restActor);

    /// <summary>
    /// <see cref="ToolingInvest(int, int, Dimension, long, Fx6)"/> and, from the same plan, <c>tool_unmet</c> of the cell (contract r8 §3.1): what the
    /// cell was short only because the pool ran out; ≥ 0. A rule without a pool says 0.
    /// </summary>
    Fx6 ToolingInvest(int siteRow, int cluster, Dimension dim, long month, Fx6 restActor, out Fx6 unmet)
    {
        unmet = Fx6.Zero;
        return ToolingInvest(siteRow, cluster, dim, month, restActor);
    }

    /// <summary>
    /// <c>D_a(t) = share_D[a]·budget_a(t)</c>: the process-development item of the actor, reserved by the budget/AI
    /// system (contract §4.0, G4-5); ≥ 0.
    /// </summary>
    Fx6 EffortBudget(int actorRow, long month);

    /// <summary><c>effort_focus[actor, k]</c>, k = 0…2: process number of the player's k-th priority, −1 = empty (contract §3.1). No repeats.</summary>
    int EffortFocus(int actorRow, int k, long month);

    /// <summary><c>v_exp(p)</c> at the site: planned output of the lines and of the projects in stages П/О (contract §4.1b); ≥ 0.</summary>
    Fx6 PlannedVolume(int siteRow, int processRow, long month);

    /// <summary><c>mult_P</c> (spec A37): the weight factor of the doctrine for the process of the actor; 1 where the input has no doctrine.</summary>
    Fx6 EffortMultiplier(int actorRow, int processRow, long month) => Fx6.One;

    /// <summary><c>planned(site, p)</c>: p is in the <c>prc_set</c> of a project of the owner in stage П or О (contract §4.1a).</summary>
    bool Planned(int siteRow, int processRow, long month);

    /// <summary><c>l_lic</c> of an active licence on the process at the site, 0 without one (spec §1.4).</summary>
    Fx6 LicenceShare(int siteRow, int processRow, long month);

    /// <summary>People available in a workforce pool (contract §4.6).</summary>
    long WorkforceAvailable(int siteRow, int category, long month);

    /// <summary><c>alloy_cap_a</c> of the site's owner (spec §1.4).</summary>
    Fx6 AlloyCap(int siteRow, long month);

    /// <summary><c>deficit</c> of an active alloy disruption event, 0 without one (spec §4).</summary>
    Fx6 AlloyDeficit(int siteRow, long month);

    /// <summary>L_C only: month index <c>t_known</c> in whose merge the process became known to the owner, −1 if unknown (spec §6.1).</summary>
    long KnownSince(int siteRow, int processRow, long month);
}
