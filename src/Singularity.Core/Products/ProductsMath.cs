using Singularity.Kernel;

namespace Singularity.Core.Products;

/// <summary>
/// The parts of <c>unit_cost</c> of one design frozen at a moment (spec §5.5: <c>production_profile</c> frozen,
/// only <c>line_E</c> moves): generation inputs and the process part <c>((Σ n_p·base_cost_p)·unit_cost_mult)·proc_scale</c>.
/// </summary>
public readonly record struct CostCard(Fx6 HFirst, Fx6 HFloor, Fx6 WageKh, Fx6 MaterialsFloor, Fx6 ProcessPart, int LearningQ10);

/// <summary>
/// Product formulas of the slice in canonical Fx6 form (contract v2.3 §4.0, §4.4; spec §3.3, §3.5 A28, §5.5): left
/// to right, floor on every <c>·</c> and <c>/</c>, powers only through <see cref="DetMath.PowFracFx"/> with integer
/// q10 (ADR-0012, ADR-0003 R2). No RNG here or anywhere in the layer (#28).
/// </summary>
public static class ProductsMath
{
    private static readonly Int128 WageScale = (Int128)Fx6.Scale * 1000;

    /// <summary><c>assembly_hours(E) = h_floor + (h_first − h_floor)·PowFrac(1 + E, −q10_b)</c> (contract §4.4).</summary>
    public static Fx6 AssemblyHours(Fx6 e, Fx6 hFirst, Fx6 hFloor, int learningQ10)
    {
        if (e < Fx6.Zero)
            throw new ArgumentOutOfRangeException(nameof(e), e, "line_E cannot be negative.");
        return hFloor + (hFirst - hFloor) * DetMath.PowFracFx(Fx6.One + e, -learningQ10);
    }

    /// <summary>
    /// <c>assembly_hours·wage</c> with <c>wage_kh</c> per 1000 h (spec §3.5 A28): one rounding,
    /// <c>⌊hours_raw·wage_kh_raw/10⁹⌋</c> in Int128.
    /// </summary>
    public static Fx6 AssemblyCost(Fx6 hours, Fx6 wageKh)
    {
        if (hours < Fx6.Zero || wageKh < Fx6.Zero)
            throw new ArgumentOutOfRangeException(nameof(hours), "Hours and wage cannot be negative.");
        return Fx6.FromRaw(checked((long)((Int128)hours.Raw * wageKh.Raw / WageScale)));
    }

    /// <summary>
    /// Process part of <c>unit_cost</c>: <c>((Σ n_p·base_cost_p)·unit_cost_mult)·proc_scale</c>. The contract's
    /// <c>Σ n_p·base_cost_p/yc_p</c> is read through <c>production_profile</c> (<c>unit_cost_mult</c>, contract §4.2),
    /// so the L_C controls replace it the same way; <c>proc_scale[g]</c> is R-PS (#78). [Предл.] order of the factors.
    /// </summary>
    public static Fx6 ProcessPart(Fx6 baseCostSum, Fx6 unitCostMult, Fx6 procScale) => baseCostSum * unitCostMult * procScale;

    /// <summary><c>unit_cost(E) = materials_floor + assembly_hours(E)·wage + process part</c> (contract §4.4).</summary>
    public static Fx6 UnitCost(in CostCard card, Fx6 e) =>
        card.MaterialsFloor + AssemblyCost(AssemblyHours(e, card.HFirst, card.HFloor, card.LearningQ10), card.WageKh) + card.ProcessPart;

    /// <summary><c>unit_cost_floor</c> (STATE_WORDS §3): yield 1 and E → ∞, i.e. <c>h_floor</c> and <c>unit_cost_mult</c> = 1.</summary>
    public static Fx6 UnitCostFloor(Fx6 materialsFloor, Fx6 hFloor, Fx6 wageKh, Fx6 baseCostSum, Fx6 procScale) =>
        materialsFloor + AssemblyCost(hFloor, wageKh) + baseCostSum * procScale;

    /// <summary>
    /// <c>upkeep = upkeep_base·PowFrac(complexity, q10_αu)/maintainability</c> per unit and month (contract §4.4) with
    /// <c>upkeep_base = share·unit_cost</c> (§8.1). [Предл.] <c>unit_cost</c> here is <c>unit_cost_floor</c>: a property of
    /// the design, not of the month's yield or line experience. [Предл.] order <c>((unit_cost·PowFrac)·share)/maintainability</c>:
    /// <c>share·unit_cost</c> first would be ≈ 5 raw for G1 and lose up to 20% to the floor.
    /// </summary>
    public static Fx6 Upkeep(Fx6 share, Fx6 unitCostFloor, int complexity, int q10, Fx6 maintainability) =>
        unitCostFloor * DetMath.PowFracFx(Fx6.FromInt(complexity), q10) * share / maintainability;

    /// <summary><c>W = ((W0·complexity)·design_scale)/(1 + bureau_exp/E_B)</c> engineer-months (spec §3.3; R-DS, #78).</summary>
    public static Fx6 DesignWork(Fx6 w0, int complexity, Fx6 designScale, Fx6 bureauExp, Fx6 eb) =>
        w0 * Fx6.FromInt(complexity) * designScale / (Fx6.One + bureauExp / eb);

    /// <summary><c>D0 = ((d0·novelty)·complexity)/(1 + bureau_exp/E_B)</c> (spec §3.3).</summary>
    public static Fx6 InitialDefects(Fx6 d0, Fx6 novelty, int complexity, Fx6 bureauExp, Fx6 eb) =>
        d0 * novelty * Fx6.FromInt(complexity) / (Fx6.One + bureauExp / eb);

    /// <summary>
    /// Defects found by cumulative hours <c>T = Σ (test_hours + k·fleet_hours)</c> (spec §3.5 A31):
    /// <c>D0·min(1, λ_D·PowFrac(T, q10_β))</c>, the cumulative count; floor on every <c>·</c>, monotone in T.
    /// </summary>
    public static Fx6 DetectedTotal(Fx6 hours, Fx6 d0, Fx6 lambdaD, int betaQ10)
    {
        if (hours < Fx6.Zero)
            throw new ArgumentOutOfRangeException(nameof(hours), hours, "Hours cannot be negative.");
        Fx6 frac = lambdaD * DetMath.PowFracFx(hours, betaQ10);
        return d0 * (frac < Fx6.One ? frac : Fx6.One);
    }

    /// <summary>
    /// <c>readiness = (min(1, paid/needed)·q)·(1 − (ψ_D·D)/D0)</c> (spec §3.3); <c>D</c> = defects not yet fixed
    /// (hidden + found, unfixed) [Предл.]; a design with <c>D0 = 0</c> has no defect factor.
    /// </summary>
    public static Fx6 Readiness(Fx6 paidRatio, Fx6 q, Fx6 defects, Fx6 d0, Fx6 psiD)
    {
        Fx6 paid = paidRatio < Fx6.One ? paidRatio : Fx6.One;
        Fx6 r = paid * q;
        return d0 <= Fx6.Zero ? r : r * (Fx6.One - psiD * defects / d0);
    }

    /// <summary>Paid share of the upkeep: 1 when nothing is needed.</summary>
    public static Fx6 PaidRatio(Fx6 paid, Fx6 needed) => needed <= Fx6.Zero ? Fx6.One : paid / needed;

    /// <summary>Whole people for <paramref name="hours"/> of work a month: <c>⌈hours/h_pm⌉</c> (spec §3.5 A26).</summary>
    public static long People(Fx6 hours, Fx6 hPm)
    {
        if (hours <= Fx6.Zero)
            return 0;
        // Ceiling of an exact ratio of raw values: both are in the same Fx6 scale.
        return (hours.Raw + hPm.Raw - 1) / hPm.Raw;
    }

    /// <summary>
    /// <c>n_v</c> of <c>fleet_value</c> at equal budget (spec §5.5): the largest n, a multiple of <paramref name="step"/>, with
    /// <c>Σ_{k=1..n} unit_cost(E + k) + (n·upkeep)·H ≤ B</c>. A block of <paramref name="step"/> units is priced at its
    /// first unit [Предл.: blocks of 10, §10; the block's dearest unit, so the sum is never under-estimated].
    /// </summary>
    public static int FleetCount(in CostCard card, Fx6 e, Fx6 upkeep, int horizon, Fx6 budget, int step, int maxUnits)
    {
        Fx6 buy = Fx6.Zero;
        int n = 0;
        while (n + step <= maxUnits)
        {
            Fx6 block = Fx6.FromInt(step) * UnitCost(card, e + Fx6.FromInt(n + 1));
            Fx6 total = buy + block + Fx6.FromInt(n + step) * upkeep * Fx6.FromInt(horizon);
            if (total > budget)
                break;
            buy += block;
            n += step;
        }
        return n;
    }

    /// <summary><c>FV_v = (n_v·readiness_v)·perf_rel_v</c> (spec §5.5).</summary>
    public static Fx6 FleetValue(int count, Fx6 readiness, Fx6 perfRel) => Fx6.FromInt(count) * readiness * perfRel;
}
