using Singularity.Kernel;

namespace Singularity.Core.Industry;

/// <summary>What limits growth of the culture of a process (contract v2.4 §4.1a, §6). Order = priority of the checks.</summary>
public enum LimiterKind
{
    /// <summary>No gap in the shown dimension: the link is not about culture.</summary>
    None,

    /// <summary><c>tooling_M &lt; culture_H</c>.</summary>
    Tooling,

    /// <summary><c>Hcap ≤ r_d</c>: people cap the culture.</summary>
    Saturation,

    /// <summary>No output and the laboratory alone cannot lift the culture further.</summary>
    LabCeiling,

    /// <summary>Money is not the limit: output is (the effort reached its useful level).</summary>
    Volume,

    /// <summary>Money for process development would speed the culture up.</summary>
    Effort,
}

/// <summary><c>limiter(site, p)</c>: the value and the process whose limit it is (<c>subject_prc</c>, a process number).</summary>
public readonly record struct CultureLimiter(LimiterKind Value, int SubjectProcess);

/// <summary>
/// The snapshot (state after month t−1 plus the inputs in force at t) a culture rule reads. All values are
/// pure functions of the snapshot; processes are storage rows of <see cref="IndustryLayout"/>.
/// </summary>
public interface ICultureSnapshot
{
    IndustryLayout Layout { get; }

    IndustryParams Params { get; }

    Fx6 H(int site, int cluster, int dim);

    Fx6 M(int site, int cluster, int dim);

    Fx6 Hcap(int site, int dim, long month);

    Fx6 Act(int site, int cluster);

    Fx6 W(int site, int cluster, long month);

    bool QualityProgramme(int site, long month);

    /// <summary><c>proc_v</c> of month t−1.</summary>
    Fx6 V(int site, int processRow);

    /// <summary><c>proc_E</c> of month t−1.</summary>
    Fx6 E(int site, int processRow);

    /// <summary><c>licensed(site, p) = LicenceShare &gt; 0</c> (contract §3.1).</summary>
    bool Licensed(int site, int processRow, long month);

    bool WorldLead(int site, int processRow);

    bool Knows(int site, int processRow, long month);

    bool Planned(int site, int processRow, long month);

    /// <summary><c>v_exp(p)</c> at the site (contract §4.1b).</summary>
    Fx6 PlannedVolume(int site, int processRow, long month);

    /// <summary><c>mult_P</c> of the actor's profile for the process (spec A37, DEBT-0019): 2 for the processes the doctrine favours, else 1; the last factor of the weight.</summary>
    Fx6 EffortMultiplier(int actor, int processRow, long month) => Fx6.One;
}

/// <summary>
/// The replaceable culture rule (RISK-0016, contract v2.4 §4.1a): one compute step of a cell plus the three
/// external pure functions the budget/AI, <c>why_not</c> and UI may read. Everything else (<c>lead</c>,
/// <c>limiter₀</c>, <c>d*</c>, <c>eligible</c>) stays inside the rule.
/// </summary>
public interface ICultureRule
{
    string Name { get; }

    /// <summary>One <c>culture_H</c> step of the cell <c>(site, cluster, dim)</c> (contract §4.1).</summary>
    CultureStep Step(ICultureSnapshot s, int site, int cluster, int dim, long month);

    /// <summary><c>E_useful(site, p)</c>: money per month that the rule can still use for p (0 if none).</summary>
    Fx6 EUseful(ICultureSnapshot s, int site, int processRow, long month);

    CultureLimiter Limiter(ICultureSnapshot s, int site, int processRow, long month);

    /// <summary><c>dH_forecast(site, p, d, E, v)</c> = <c>min(ΔH⁺, ρ_max)</c> of the cell with E and v substituted for p. UI and why_not only.</summary>
    Fx6 DHForecast(ICultureSnapshot s, int site, int processRow, int dim, Fx6 e, Fx6 v, long month);
}
