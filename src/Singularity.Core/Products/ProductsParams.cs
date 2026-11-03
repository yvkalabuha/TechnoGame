using System.Collections.Immutable;
using Singularity.Kernel;

namespace Singularity.Core.Products;

/// <summary>
/// Product-layer parameters of the jet slice (spec docs/simulation/slice-jet-aviation.md §8.1 "изделие", §5.5,
/// §6.1; all per month). Defaults are the spec values; for a range ("К") the default is a point inside it,
/// calibration replaces it later. Values the spec does not give are marked [Предл.] and reported.
/// </summary>
public sealed record ProductsParams
{
    private static Fx6 F(long raw) => Fx6.FromRaw(raw);

    /// <summary>Wright exponent of the class as q10 (ADR-0012): LR 0.80 → b = 0.3219 → 330.</summary>
    public int LearningQ10 { get; init; } = 330;

    /// <summary><c>δ_E</c>, range 0.01–0.04 (К); 0.025 [Предл.: mid-range until calibration].</summary>
    public Fx6 DeltaE { get; init; } = F(25_000);

    public Fx6 D0Factor { get; init; } = Fx6.FromInt(20);

    /// <summary><c>λ_D</c> of <c>detected = D0·min(1, λ_D·PowFrac(T, q10_β))</c> (spec §3.5 A31): share of <c>D0</c> per h^β; 0.0087 (З).</summary>
    public Fx6 LambdaD { get; init; } = F(8_700);

    /// <summary><c>q10_β</c>: β = 0.5.</summary>
    public int BetaQ10 { get; init; } = 512;

    public Fx6 PsiD { get; init; } = F(500_000);

    /// <summary>Engineer hours to fix one detected defect.</summary>
    public Fx6 FixHours { get; init; } = Fx6.FromInt(2000);

    /// <summary><c>upkeep_base = UpkeepShare·unit_cost</c> (§8.1: 0.004·unit_cost).</summary>
    public Fx6 UpkeepShare { get; init; } = F(4_000);

    /// <summary><c>q10_αu</c> of <c>PowFrac(complexity, q10_αu)</c> in upkeep: α = 0.5.</summary>
    public int UpkeepQ10 { get; init; } = 512;

    public int ServiceLife { get; init; } = 180;

    /// <summary><c>B0</c> of <c>bureau_req[g] = ⌊B0·design_scale[g]^κ⌋</c> engineers (spec §3.3, §3.5 A34; З).</summary>
    public int B0 { get; init; } = 500;

    /// <summary><c>κ</c> as q10 (ADR-0012): κ ∈ [0; 1] → 0…1024; centre 0.5 = 512 (К, C4).</summary>
    public int KappaQ10 { get; init; } = 512;

    /// <summary>
    /// <c>bureau_req = ⌊B0·PowFrac(design_scale, q10_κ)⌋</c> whole engineers (§3.3). Evaluated once per generation when the
    /// system is built (manifest time), never inside a tick.
    /// </summary>
    public int BureauReq(Fx6 designScale) => (int)((Fx6.FromInt(B0) * DetMath.PowFracFx(designScale, KappaQ10)).Raw / Fx6.Scale);

    /// <summary><c>W0</c>, engineer-months per unit of complexity.</summary>
    public Fx6 W0 { get; init; } = Fx6.FromInt(400);
    public Fx6 EB { get; init; } = Fx6.FromInt(3);
    public Fx6 DeltaB { get; init; } = F(5_000);

    /// <summary>Test hours added per month while a design is in testing or series.</summary>
    public Fx6 TestRate { get; init; } = Fx6.FromInt(200);

    /// <summary><c>y_prod</c>: the "producible" variant takes alternatives whose bottleneck yield reaches it.</summary>
    public Fx6 YProd { get; init; } = F(500_000);

    /// <summary><c>k</c> in <c>test_hours + k·fleet_hours</c> (contract §4.4). [Предл.] 1: the spec gives no value.</summary>
    public Fx6 KFleet { get; init; } = Fx6.One;

    /// <summary>Flight hours per unit in service per month. [Предл.] 20 (≈ 240 h/year): the spec gives no value.</summary>
    public Fx6 FleetHoursPerUnit { get; init; } = Fx6.FromInt(20);

    /// <summary><c>H</c> of <c>fleet_value</c> (#34), months of upkeep.</summary>
    public int FvHorizon { get; init; } = 180;

    /// <summary><c>fleet_value</c> counts units in blocks of this size (§5.5, "шаг 10").</summary>
    public int FvStep { get; init; } = 10;

    /// <summary>Upper bound of <c>n_v</c> (§10: up to 2000 PowFrac steps of 10 units per design).</summary>
    public int FvMaxUnits { get; init; } = 20_000;

    /// <summary>L_P: <c>T_design[g]</c>, months from APP g known to the first canonical unit, index g − 1 (control tables, §6.1).</summary>
    public ImmutableArray<int> LpTDesign { get; init; } = [];

    /// <summary>L_P: <c>ρ[g]</c>, monthly units per unit of procurement budget, index g − 1 (control tables, §6.1).</summary>
    public ImmutableArray<Fx6> LpRho { get; init; } = [];

    /// <summary>Throws if a value would break a formula.</summary>
    public void Validate()
    {
        if (EB <= Fx6.Zero || FixHours <= Fx6.Zero || ServiceLife < 1 || FvStep < 1 || FvMaxUnits < FvStep)
            throw new ArgumentException("E_B, fix_hours, service_life and the fleet_value step must be positive.");
        if (DeltaE < Fx6.Zero || DeltaE > Fx6.One || DeltaB < Fx6.Zero || DeltaB > Fx6.One || PsiD < Fx6.Zero || PsiD > Fx6.One)
            throw new ArgumentException("δ_E, δ_B and ψ_D must be in [0; 1].");
        if (LearningQ10 <= 0 || BetaQ10 <= 0 || BetaQ10 >= DetMath.FracOne || UpkeepQ10 < 0)
            throw new ArgumentException("q10_b > 0, 0 < q10_β < 1024 (β < 1), q10_αu ≥ 0.");
        if (B0 < 1 || KappaQ10 < 0 || KappaQ10 > DetMath.FracOne)
            throw new ArgumentException("B0 ≥ 1 and κ in [0; 1] (q10 0…1024).");
        if (LpTDesign.Length != LpRho.Length)
            throw new ArgumentException("L_P tables need one T_design and one ρ per generation.");
    }
}
