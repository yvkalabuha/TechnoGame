using Singularity.Kernel;

namespace Singularity.Core.Industry;

/// <summary>What a mastery programme on a process would meet in its cell (TASK-0134, production-mastery.md §1, §2).</summary>
public enum ProgrammeFit
{
    /// <summary>The process would lead its cell and grow; <see cref="ProgrammeAssessment.EtaMonths"/> is set.</summary>
    Ok = 0,

    /// <summary>The limiter is not <c>LabCeiling</c> (nothing limits the process, or it already has output).</summary>
    NotLabCeiling = 1,

    /// <summary><c>Hcap ≤ r_d</c>: the programme could only wait for the ceiling; no money would be spent.</summary>
    Saturation = 2,

    /// <summary>The tooling <c>M</c> is below <c>H</c>: tooling first.</summary>
    Tooling = 3,

    /// <summary>Another process leads the cell, so the programme would grow nothing; <see cref="ProgrammeAssessment.LeadRow"/> says which.</summary>
    CellLed = 4,

    /// <summary>The forecast growth is zero: the programme would never end.</summary>
    NoRate = 5,
}

/// <summary>Result of <c>IndustrySystem.AssessProgramme</c>: the fit, the leading process row of the cell (−1 if none) and the time to <c>r_d</c> in months (whole, rounded up).</summary>
public readonly record struct ProgrammeAssessment(ProgrammeFit Fit, int LeadRow, int EtaMonths);

/// <summary>
/// A snapshot in which one process has the programme's nominal input: the same snapshot, with <c>growth_nominal</c> = max(own, v) and
/// <c>planned</c> for that process. Lets the pure functions of the culture rule answer «what if the programme started» without state.
/// </summary>
internal sealed class ProgrammeSnapshot(ICultureSnapshot inner, int row, Fx6 v, bool domestic) : ICultureSnapshot
{
    public IndustryLayout Layout => inner.Layout;
    public IndustryParams Params => inner.Params;
    public Fx6 H(int site, int cluster, int dim) => inner.H(site, cluster, dim);
    public Fx6 M(int site, int cluster, int dim) => inner.M(site, cluster, dim);
    public Fx6 Hcap(int site, int dim, long month) => inner.Hcap(site, dim, month);
    public Fx6 Act(int site, int cluster) => inner.Act(site, cluster);
    public Fx6 W(int site, int cluster, long month) => inner.W(site, cluster, month);
    public bool QualityProgramme(int site, long month) => inner.QualityProgramme(site, month);
    public Fx6 V(int site, int processRow) => inner.V(site, processRow);
    public Fx6 E(int site, int processRow) => inner.E(site, processRow);
    public bool Licensed(int site, int processRow, long month) => inner.Licensed(site, processRow, month);
    public bool WorldLead(int site, int processRow) => inner.WorldLead(site, processRow);
    public bool Knows(int site, int processRow, long month) => inner.Knows(site, processRow, month);
    public bool Planned(int site, int processRow, long month) => processRow == row || inner.Planned(site, processRow, month);
    public Fx6 PlannedVolume(int site, int processRow, long month) => inner.PlannedVolume(site, processRow, month);
    public Fx6 EffortMultiplier(int actor, int processRow, long month) => inner.EffortMultiplier(actor, processRow, month);
    public long ProgrammeStart(int site, int processRow, long month) => inner.ProgrammeStart(site, processRow, month);
    public bool ProgrammeDomestic(int site, int processRow, long month) => processRow == row ? domestic : inner.ProgrammeDomestic(site, processRow, month);

    public Fx6 GrowthNominal(int site, int processRow, long month)
    {
        Fx6 own = inner.GrowthNominal(site, processRow, month);
        return processRow == row && v > own ? v : own;
    }
}
