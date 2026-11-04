using System.Collections.Immutable;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Kernel;

namespace Singularity.Core.WhyNot;

/// <summary>Layer of a link (contract §6); the declaration order is the sort order.</summary>
public enum WhyNotLayer
{
    Knowledge,
    Capability,
    Product,
    Economy,
}

/// <summary>Cause of a link (contract §6 «Причины по слоям»; v2.4: <see cref="SeriesGate"/>).</summary>
public enum WhyNotCause
{
    /// <summary>KNOWLEDGE: the nearest unknown node on the path to the APP node.</summary>
    MissingNode,

    /// <summary>KNOWLEDGE (v2.4): a slot has no available alternative (<c>avail = false</c>); the link is the nearest unknown node of one.</summary>
    AltUnavailable,

    /// <summary>CAPABILITY, bottleneck source <c>H</c>: culture below the requirement.</summary>
    CultureH,

    /// <summary>CAPABILITY, bottleneck source <c>M</c> (<c>c_d = M &lt; H</c>): tooling.</summary>
    ToolingM,

    /// <summary>CAPABILITY: all <c>g_d = 1</c>, only <c>ramp</c> (<c>proc_X ≪ X½</c>) limits the process.</summary>
    Ramp,

    /// <summary>PRODUCT: no project for the requirement.</summary>
    NoDesign,

    /// <summary>PRODUCT: <c>granted/n &lt; 0.9</c> in the engineer pool (bureau, fix team).</summary>
    Engineers,

    /// <summary>PRODUCT: <c>granted/n &lt; 0.9</c> in the skilled pool of a line.</summary>
    SkilledWorkforce,

    /// <summary>PRODUCT (v2.4): the series is not admitted, «скрыто N при допуске M».</summary>
    SeriesGate,

    /// <summary>PRODUCT: hidden and open defects of a design in series or service above the gate level.</summary>
    DefectsHidden,

    /// <summary>PRODUCT: alloy (a CMP) is short for the planned rate, or a disruption is on.</summary>
    AlloyDeficit,

    /// <summary>
    /// PRODUCT (spec v0.14 §1.4, STATE_WORDS §9): a slot has an alternative reachable only below the known engine (<c>max_eng_gen</c>) and no
    /// non-substitute one; <c>subject</c> = the node to learn (lever «изучить»), <c>have</c> = <c>max_eng_gen</c>, <c>need</c> = <c>eng_gen</c>
    /// of the highest known engine, <c>Slot</c> = the slot. Not <see cref="AlloyDeficit"/>: the lever is knowledge, not supply.
    /// </summary>
    EngineGenLimit,

    /// <summary>ECONOMY: admitted but <c>unit_cost·n_plan + upkeep</c> is above the procurement budget.</summary>
    NotAffordable,
}

/// <summary>
/// The levers of contract §6 (registry of the build, §10). <c>ForceSeries</c> is «только игрок»: the AI sees it and does not apply it.
/// </summary>
public enum Lever
{
    ResearchPriority,
    TrainingSeries,
    InvestToolingM,
    Licence,
    QualityProgramme,
    SimplifiedVariant,
    ReassignWorkforce,
    ProcessDevelopment,
    ForceSeries,

    /// <summary><c>retire_cohort</c> (contract r6 §6, ECONOMY): the upkeep of old machines eats the procurement item.</summary>
    RetireCohort,

    /// <summary><c>sell_cohort</c> (contract r6 §6, ECONOMY): old machines of stage В go outside the slice for money.</summary>
    SellCohort,
}

/// <summary>
/// One link of <c>why_not</c> (contract §6: <c>layer, cause, subject_id, have, need, lever, yield_if_fixed, eta_months</c>).
/// Extensions kept beside the contract fields: <see cref="Levers"/> is a list (the contract's own example offers two), and the
/// narrow process, dimension, <c>limiter</c> and variant slot of the CAPABILITY link are carried for the UI and for the validator.
/// </summary>
/// <param name="SubjectId">Node number (KNOWLEDGE); for H-source CAPABILITY <c>limiter.subject_prc</c> (§6); design or requester id (PRODUCT); design (ECONOMY).</param>
/// <param name="YieldIfFixed">Yield of the bottleneck process of the product if only this link is closed (<c>have := need</c>); 0 where there is no product set.</param>
/// <param name="EtaMonths"><c>ceil((need − have)/rate)</c> at the current speed; null when the rate is not positive («само не дойдёт»).</param>
/// <param name="Process">The narrow process of a CAPABILITY link, −1 otherwise.</param>
/// <param name="Slot">Product slot of an <see cref="WhyNotCause.EngineGenLimit"/> link, −1 otherwise.</param>
/// <param name="VariantSlot">Slot to replace for <see cref="Lever.SimplifiedVariant"/> (contract §6 «читается как заменить в слоте s»), −1 without it.</param>
public readonly record struct Link(
    WhyNotLayer Layer,
    WhyNotCause Cause,
    int SubjectId,
    Fx6 Have,
    Fx6 Need,
    ImmutableArray<Lever> Levers,
    Fx6 YieldIfFixed,
    int? EtaMonths,
    int Process = -1,
    Dimension? Dim = null,
    LimiterKind? Limiter = null,
    int VariantSlot = -1,
    int Slot = -1)
{
    /// <summary>
    /// The contract order (§6): layer, then <c>yield_if_fixed</c> descending, then <c>eta_months</c> ascending (null last), then
    /// <c>subject_id</c>; the remaining fields only make the order total, so it does not depend on how the list was built.
    /// </summary>
    public static int Compare(Link a, Link b)
    {
        int c = a.Layer.CompareTo(b.Layer);
        if (c != 0)
            return c;
        c = b.YieldIfFixed.CompareTo(a.YieldIfFixed);
        if (c != 0)
            return c;
        c = (a.EtaMonths ?? int.MaxValue).CompareTo(b.EtaMonths ?? int.MaxValue);
        if (c != 0)
            return c;
        c = a.SubjectId.CompareTo(b.SubjectId);
        if (c != 0)
            return c;
        c = a.Cause.CompareTo(b.Cause);
        if (c != 0)
            return c;
        c = a.Process.CompareTo(b.Process);
        return c != 0 ? c : Nullable.Compare(a.Dim, b.Dim);
    }
}

/// <summary>What the target of <c>why_not</c> is: <c>app_id</c> (an APP node number), <c>design_id</c> (a slot) or <c>requirement_id</c> (the generation <c>g*</c>).</summary>
public enum WhyNotTargetKind
{
    App,
    Design,
    Requirement,
}

public readonly record struct WhyNotTarget(WhyNotTargetKind Kind, int Id)
{
    public static WhyNotTarget App(int nodeNumber) => new(WhyNotTargetKind.App, nodeNumber);

    public static WhyNotTarget Design(int design) => new(WhyNotTargetKind.Design, design);

    public static WhyNotTarget Requirement(int generation) => new(WhyNotTargetKind.Requirement, generation);
}

/// <summary>
/// The levers enabled in the build (contract §6: «рычаги берутся только из реестра, включённых в сборку»). The analyzer offers no
/// other lever; <see cref="LeverValidator"/> checks it (spec §5.6 d).
/// </summary>
public sealed class LeverRegistry
{
    private readonly int _mask;

    public LeverRegistry(IEnumerable<Lever> enabled)
    {
        foreach (Lever l in enabled)
            _mask |= 1 << (int)l;
    }

    /// <summary>All levers of the slice (contract §10).</summary>
    public static LeverRegistry Slice { get; } = new(Enum.GetValues<Lever>());

    public bool IsEnabled(Lever lever) => (_mask & (1 << (int)lever)) != 0;

    /// <summary>The «только игрок» marking (contract §6, #94): the AI sees such a lever and never applies it.</summary>
    public static bool PlayerOnly(Lever lever) => lever == Lever.ForceSeries;
}

/// <summary>
/// Inputs <c>why_not</c> needs from outside the three layers; all pure functions of the snapshot and the month (as the layer inputs).
/// </summary>
public interface IWhyNotInputs
{
    /// <summary>Procurement budget of the actor in month t (the program budget of the ECONOMY link); ≥ 0.</summary>
    Fx6 ProcurementBudget(int actorRow, long month);

    /// <summary>Training-series money of the line for the month (<c>TrainingBudget</c> of the product layer, step 7 pays from left + tb); ≥ 0.</summary>
    Fx6 TrainingBudget(int actorRow, int design, long month);

    /// <summary>A licence for the process is on offer to the owner of the site (contract §6: the lever <c>licence</c> for <c>VOLUME</c>).</summary>
    bool LicenceOffered(int siteRow, int processNumber, long month);

    /// <summary><c>tooling_M</c> growth per month of the cell at the planned investment, ≥ 0 (the speed of <c>eta</c> of an M-source link).</summary>
    Fx6 ToolingRate(int siteRow, int cluster, Dimension dim, long month);
}
