using Singularity.Kernel;

namespace Singularity.Core.Products;

/// <summary>
/// The parts of <c>unit_cost</c> of one design frozen at a moment (spec §5.5: <c>production_profile</c> frozen,
/// only <c>line_E</c> moves): airframe-class inputs and the R-PS2m process part.
/// </summary>
public readonly record struct CostCard(Fx6 HFirst, Fx6 HFloor, Fx6 WageKh, Fx6 MaterialsFloor, Fx6 ProcessPart, int LearningQ10);

/// <summary>
/// Product formulas of the slice in canonical Fx6 form (contract v2.4 §4.0, §4.4; spec §3.3, §3.5 A28, §5.5): left
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
    /// R-PS2m process part (contract §4.4a): <c>((ΣPU·U_PU)·proc_scale_pu[r_PU]) + ((ΣAF·U_AF)·proc_scale_af[af_class])</c>, floor on
    /// every <c>·</c>. <c>ΣB = 0</c> gives a zero term (branch before the product, §4.0 item 9). <c>U_B</c> is
    /// <c>production_profile(site, prc_set ∩ B).unit_cost_mult</c> of the block (П-8).
    /// </summary>
    public static Fx6 ProcessPart(Fx6 sumPu, Fx6 uPu, Fx6 scalePu, Fx6 sumAf, Fx6 uAf, Fx6 scaleAf) =>
        BlockPart(sumPu, uPu, scalePu) + BlockPart(sumAf, uAf, scaleAf);

    private static Fx6 BlockPart(Fx6 sum, Fx6 u, Fx6 scale) => sum == Fx6.Zero ? Fx6.Zero : sum * u * scale;

    /// <summary><c>unit_cost(E) = materials_floor + assembly_hours(E)·wage + process part</c> (contract §4.4).</summary>
    public static Fx6 UnitCost(in CostCard card, Fx6 e) =>
        card.MaterialsFloor + AssemblyCost(AssemblyHours(e, card.HFirst, card.HFloor, card.LearningQ10), card.WageKh) + card.ProcessPart;

    /// <summary><c>unit_cost_floor</c> (STATE_WORDS §3): yield 1 and E → ∞, i.e. <c>h_floor</c> and <c>U_PU = U_AF</c> = 1.</summary>
    public static Fx6 UnitCostFloor(Fx6 materialsFloor, Fx6 hFloor, Fx6 wageKh, Fx6 sumPu, Fx6 scalePu, Fx6 sumAf, Fx6 scaleAf) =>
        materialsFloor + AssemblyCost(hFloor, wageKh) + ProcessPart(sumPu, Fx6.One, scalePu, sumAf, Fx6.One, scaleAf);

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

    /// <summary><c>D_ref = d0·c_ref</c> (contract §4.4b): a class constant.</summary>
    public static Fx6 DRef(Fx6 d0Factor, int cRef) => d0Factor * Fx6.FromInt(cRef);

    /// <summary>
    /// <c>readiness = (min(1, paid/needed)·q)·(1 − (ψ_D·D)/D_ref)</c> (contract §4.4b, A35); <c>D</c> = defects not yet fixed
    /// (hidden + found, unfixed). <c>ψ_D·c_max/c_ref &lt; 1</c> (<see cref="ProductsSystem"/> validator) keeps it ≥ 0 without a clamp.
    /// </summary>
    public static Fx6 Readiness(Fx6 paidRatio, Fx6 q, Fx6 defects, Fx6 dRef, Fx6 psiD)
    {
        Fx6 paid = paidRatio < Fx6.One ? paidRatio : Fx6.One;
        return paid * q * (Fx6.One - psiD * defects / dRef);
    }

    /// <summary><c>dev_scale = 1 − D/D_ref</c> (contract §4.4b, #98): the «доводка» scale for the UI only; negative for a new complex design (П-10), never used in formulas.</summary>
    public static Fx6 DevScale(Fx6 defects, Fx6 dRef) => Fx6.One - defects / dRef;

    /// <summary><c>gate_open = (D_hidden ≤ k_gate·D_ref) ∧ (months of testing ≥ t_test_min)</c> (contract §4.4b).</summary>
    public static bool GateOpen(Fx6 dHidden, Fx6 kGate, Fx6 dRef, long testMonths, int tTestMin) =>
        dHidden <= kGate * dRef && testMonths >= tTestMin;

    /// <summary>
    /// Preliminary <c>d_gate = min(k_gate·D_ref, D0 − detected(t_test_min·test_rate))</c> on entering stage О (contract §4.4c step 1):
    /// the expected <c>D_hidden</c> after the minimum tests.
    /// </summary>
    public static Fx6 PreliminaryGate(Fx6 kGate, Fx6 dRef, Fx6 d0, int tTestMin, Fx6 testRate, Fx6 lambdaD, int betaQ10)
    {
        Fx6 expected = d0 - DetectedTotal(Fx6.FromInt(tTestMin) * testRate, d0, lambdaD, betaQ10);
        Fx6 cap = kGate * dRef;
        return expected < cap ? expected : cap;
    }

    /// <summary>Excess defects <c>X = max(0, D_hidden − d_gate)</c> (contract §4.4c).</summary>
    public static Fx6 Excess(Fx6 dHidden, Fx6 dGate) => dHidden > dGate ? dHidden - dGate : Fx6.Zero;

    /// <summary>
    /// Accident loss added in a month (contract §4.4c): <c>(((units·fleet_hours)·a_acc)·X)/D_ref</c>, floor on every operation;
    /// <paramref name="fleetHours"/> is flight hours per machine per month.
    /// </summary>
    public static Fx6 AccidentLoss(Fx6 units, Fx6 fleetHours, Fx6 aAcc, Fx6 excess, Fx6 dRef) =>
        units * fleetHours * aAcc * excess / dRef;

    /// <summary>Whole machines struck off: <c>min(⌊loss_acc⌋, ⌊units_in_service⌋)</c> (contract §4.4c).</summary>
    public static long WholeLosses(Fx6 lossAcc, Fx6 units) => Math.Min(lossAcc.Raw / Fx6.Scale, units.Raw / Fx6.Scale);

    /// <summary>
    /// Retrofit while <c>retro_pool &gt; 0</c> (contract §4.4d step 5): <c>k_r = min(fix_n, pool)</c>, <c>k_paid = (c_u = 0) ? k_r :
    /// min(k_r, left/c_u)</c>, <c>fixed = (k_paid &lt; k_r) ? k_paid : fix_n</c> — when money is short, only the paid defects are fixed.
    /// </summary>
    public static Fx6 RetrofitFixed(Fx6 fixN, Fx6 retroPool, Fx6 cU, Fx6 left, out Fx6 kPaid)
    {
        Fx6 kR = fixN < retroPool ? fixN : retroPool;
        kPaid = cU == Fx6.Zero ? kR : (left / cU < kR ? left / cU : kR);
        return kPaid < kR ? kPaid : fixN;
    }

    /// <summary>
    /// <c>c_u = ((unit_cost_floor·ρ_retro)·units)/D_ref</c> (contract §4.4c, erratum F9): one division at the end, one floor,
    /// in Int128 over raw values.
    /// </summary>
    public static Fx6 RetrofitCost(Fx6 unitCostFloor, Fx6 rhoRetro, Fx6 units, Fx6 dRef) =>
        Fx6.FromRaw(checked((long)((Int128)unitCostFloor.Raw * rhoRetro.Raw * units.Raw / ((Int128)Fx6.Scale * dRef.Raw))));

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

    /// <summary>
    /// Alloy cap of <c>n_v</c> (contract §9.1): <c>⌊(supply/alloy_kg)·H_build⌋</c> units, and
    /// <c>n_v = min(n_budget, cap)</c> stepped down to a multiple of <paramref name="step"/>.
    /// </summary>
    public static int AlloyLimitedCount(int nBudget, Fx6 supply, Fx6 alloyKg, int hBuild, int step)
    {
        long cap = (supply / alloyKg * Fx6.FromInt(hBuild)).Raw / Fx6.Scale;
        long n = Math.Min(nBudget, cap);
        return (int)(n - n % step);
    }

    /// <summary><c>FV_v = (n_v·readiness_v)·PowFrac(perf_rel_v, q10_γ)</c> (contract §9.1, #91).</summary>
    public static Fx6 FleetValue(int count, Fx6 readiness, Fx6 perfRel, int gammaQ10) =>
        Fx6.FromInt(count) * readiness * DetMath.PowFracFx(perfRel, gammaQ10);
}
