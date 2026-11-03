using Singularity.Core.Data;
using Singularity.Kernel;

namespace Singularity.Core.Industry;

/// <summary>
/// What the capability layer reads from other layers in the compute of month t. Contract (as
/// <c>IKnowledgeInputs</c>): every method is a pure function of the snapshot t−1 (the state after month
/// t−1, holding the decisions in force at t) and of its arguments; no side effects, thread-safe. The layer
/// has no RNG (#28): nothing here may draw one on its behalf either.
/// </summary>
public interface IIndustryInputs
{
    /// <summary><c>p ∈ known_processes(owner(site))</c> in the snapshot (contract §5).</summary>
    bool KnowsProcess(int siteRow, int processRow, long month);

    /// <summary><c>act[c]</c> (spec §3.2).</summary>
    Fx6 Act(int siteRow, int cluster, long month);

    /// <summary><c>w[c]</c> (spec §3.2).</summary>
    Fx6 W(int siteRow, int cluster, long month);

    /// <summary><c>Hcap[site,d]</c> (contract §4.1); positive.</summary>
    Fx6 Hcap(int siteRow, Dimension dim, long month);

    /// <summary>A quality programme is active on the site (<c>α_eff</c> on quality_control, contract §4.1, §7).</summary>
    bool QualityProgramme(int siteRow, long month);

    /// <summary>Money invested into <c>tooling_M[site,c,d]</c> in month t (AI decision in force, spec §3.5 A11); ≥ 0.</summary>
    Fx6 ToolingInvest(int siteRow, int cluster, Dimension dim, long month);

    /// <summary><c>l_lic</c> of an active licence on the process at the site, 0 without one (spec §1.4).</summary>
    Fx6 LicenceShare(int siteRow, int processRow, long month) => Fx6.Zero;

    /// <summary>People available in a workforce pool (contract §4.6).</summary>
    long WorkforceAvailable(int siteRow, int category, long month) => 0;

    /// <summary><c>alloy_cap_a</c> of the site's owner (spec §1.4).</summary>
    Fx6 AlloyCap(int siteRow, long month) => Fx6.Zero;

    /// <summary><c>deficit</c> of an active alloy disruption event, 0 without one (spec §4).</summary>
    Fx6 AlloyDeficit(int siteRow, long month) => Fx6.Zero;

    /// <summary>L_C only: month index <c>t_known</c> in whose merge the process became known to the owner, −1 if unknown (spec §6.1).</summary>
    long KnownSince(int siteRow, int processRow, long month) => -1;
}
