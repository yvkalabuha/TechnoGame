using System.Collections.Immutable;
using Singularity.Core.Industry;
using Singularity.Core.Knowledge;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session;

/// <summary>Channel ablations of the slice (spec §6.2): each switch zeroes exactly one quantity (CbCultureRule → V23CultureRule for the last).</summary>
[Flags]
public enum SliceChannels
{
    None = 0,

    /// <summary><c>α = 0</c>.</summary>
    Alpha = 1 << 0,

    /// <summary><c>ramp ≡ 1</c> (<c>λ0 = 1</c>).</summary>
    Ramp = 1 << 1,

    /// <summary><c>b = 0</c>: no assembly learning (<c>q10_b = 0</c>).</summary>
    B = 1 << 2,

    /// <summary><c>η = 0</c>.</summary>
    Eta = 1 << 3,

    /// <summary><c>k_catch = 0</c>.</summary>
    KCatch = 1 << 4,

    /// <summary><c>σ = 0</c>.</summary>
    Sigma = 1 << 5,

    /// <summary><c>k_diff = 0</c>: the catching-up channel of knowledge.</summary>
    KDiff = 1 << 6,

    /// <summary><c>E ≡ 0</c> (ZERO): no process-development distribution, the whole item D stays in <c>rest_a</c> (<c>E_full = 0</c>).</summary>
    EffortZero = 1 << 7,

    /// <summary><c>k_tr = 0</c>: the licence acts only through <c>X½</c>.</summary>
    KTr = 1 << 8,

    /// <summary><c>k_qp = 0</c>: the quality programme's contribution.</summary>
    KQp = 1 << 9,

    /// <summary>C-B → R0: the culture rule of contract v2.3 (<see cref="Singularity.Core.Industry.V23CultureRule"/>).</summary>
    CultureR0 = 1 << 10,
}

/// <summary>Sensitivity arms of the sale terms (spec §6.10, A42.8): each changes exactly one number of the sale.</summary>
public enum SaleArm
{
    None = 0,
    /// <summary><c>SALE_SIGMA_LO</c>: <c>σ0</c> = 0,15.</summary>
    SigmaLo = 1,
    /// <summary><c>SALE_PHI_FLAT</c>: <c>φ_gen</c> = 1 / 1 / 1.</summary>
    PhiFlat = 2,
    /// <summary><c>SALE_QUOTA_LO</c>: <c>ς</c> = 0,05.</summary>
    QuotaLo = 3,
    /// <summary><c>SALE_QUOTA_HI</c>: <c>ς</c> = 0,20.</summary>
    QuotaHi = 4,
    /// <summary><c>SALE_CHI_OFF</c>: no revenue ceiling.</summary>
    ChiOff = 5,
}

/// <summary>Layer controls of the series <c>layer/*</c> (spec §6.1, SLICE_COMPOSITION §12.4, §13.2). <c>L_C</c> and <c>L_P</c> read <c>t_known</c> from the columns <c>know.since.N</c>.</summary>
public enum LayerControl
{
    /// <summary>The full model (main).</summary>
    Full = 0,

    /// <summary><c>L_K</c>: the schedule of <c>known</c> from the moments of the full model; needs <see cref="SliceRunConfig.LkTables"/>.</summary>
    Lk = 1,

    /// <summary><c>L_K0</c> (print): fixed lag after the middle of the generation window.</summary>
    Lk0 = 2,

    /// <summary><c>L_C0</c> (print): <c>yield = 1</c>, <c>unit_cost_mult = 1</c>, <c>q = 1</c>.</summary>
    Lc0 = 3,

    /// <summary>
    /// <c>L_C</c>: one median curve per tier after <c>t_known</c>; needs <see cref="SliceRunConfig.CapabilityTables"/>.
    /// </summary>
    Lc = 4,

    /// <summary>
    /// <c>L_P</c>: the canonical project of the newest due generation; needs <see cref="SliceRunConfig.CapabilityTables"/>.
    /// </summary>
    Lp = 5,
}

/// <summary>
/// Tables of the controls <c>L_C</c> and <c>L_P</c> (spec §6.1, format <c>control_tables/1</c> §3.1): the curves <c>Y_k</c>, <c>U_k</c> of tiers 3, 4, 5,
/// and <c>T_design[g]</c>, <c>ρ[g]</c> (index g − 1). Both layers read the same set, so one set is given with either.
/// </summary>
public sealed record CapabilityControlTables(ImmutableArray<CapabilityCurve> Curves, ImmutableArray<int> TDesign, ImmutableArray<Fx6> Rho);

/// <summary>Step of an axis of the P5 grid (spec §7): ×1, ×0,5, ×2 of the frozen value.</summary>
public enum GridStep
{
    Mid = 0,
    Lo = 1,
    Hi = 2,
}

/// <summary>A point of the P5 grid; the default is <c>mid_mid_mid</c> = main.</summary>
public readonly record struct GridCell(GridStep KCatch, GridStep KDiff, GridStep HMax)
{
    /// <summary><c>9·kc + 3·kd + hm</c>; 0 = main.</summary>
    public int Code => 9 * (int)KCatch + 3 * (int)KDiff + (int)HMax;
}

/// <summary><c>EFFORT_SHARE</c> (spec §6.8): <c>share_D′ = x·share_D</c>; the other shares are scaled to keep the sum 1.</summary>
public enum EffortShareArm
{
    X1 = 0,
    X0 = 1,
    X05 = 2,
    X2 = 3,
    X4 = 4,
}

/// <summary>Which actors a view carries the node blocks (<see cref="ActorView.Nodes"/>) of (ADR-0025).</summary>
public enum ViewScope
{
    /// <summary>Every actor.</summary>
    All = 0,

    /// <summary>Only the actors set by <see cref="SliceSession.SetViewSubscription"/>; the others have an absent <see cref="NodeList"/>.</summary>
    Subscribed = 1,
}

/// <summary>Start variant of the scenario (spec §6.5).</summary>
public enum StartVariant
{
    Scenario = 0,

    /// <summary><c>START_PERT</c>: estimated fields scaled by ±20% multipliers from the <c>START</c> stream.</summary>
    Perturbed = 1,

    /// <summary><c>START_SYM</c>: USA, GBR and SUN start identical.</summary>
    Symmetric = 2,
}

/// <summary>
/// What defines a run of the slice besides the data (spec §6): the seed, which RNG sources are fixed (<c>FIX_*</c>, §6.3), the start
/// variant, the factorial profile cell (§6.6) and the channel switches (§6.2). All of it except the execution options is written to
/// the <c>run.*</c> columns, so it is part of the state hash.
/// </summary>
public sealed partial record SliceRunConfig
{
    /// <summary>The seed of a source under <c>FIX_*</c> (spec §6.3).</summary>
    public const ulong FixedSeed = 0x5EED;

    public const string DefaultScenario = "jet_slice_1945";

    public ulong Seed { get; init; }

    public string ScenarioId { get; init; } = DefaultScenario;

    /// <summary><c>FIX_K</c>: streams <c>KNOW</c>, <c>PRIO_J</c> run on <see cref="FixedSeed"/>.</summary>
    public bool FixK { get; init; }

    /// <summary><c>FIX_E</c>: streams <c>EVT_LIC</c>, <c>EVT_DIS</c>.</summary>
    public bool FixE { get; init; }

    /// <summary><c>FIX_A</c>: stream <c>AI</c> (the profiles).</summary>
    public bool FixA { get; init; }

    public StartVariant Start { get; init; }

    /// <summary>Factorial cell <c>9u + 3g + s</c> of the profiles of USA, GBR, SUN (§6.6); null = all profiles from the stream (main).</summary>
    public int? ProfileCell { get; init; }

    public SliceChannels Channels { get; init; }

    /// <summary>
    /// The AI makes the product decisions (Requirement, launch, line rate, give-up, spec A40/A41). On in main. Off for a scripted player that
    /// drives the product layer alone (tests of the player's line switch, spec A41 «Переключение линии у прокси игрока»).
    /// </summary>
    public bool ProductAi { get; init; } = true;

    /// <summary>
    /// Actors (rows) the player controls and whose line decisions the AI only proposes (TASK-0153, #479). Empty = off: the AI leads every actor, as before.
    /// Needs <see cref="ProductAi"/>.
    /// </summary>
    public System.Collections.Immutable.ImmutableArray<int> AdviserActors { get; init; }

    /// <summary>How long a refused proposal stays silent, in years (a yearly norm; the policy turns it into months).</summary>
    public int AdviserRefuseYears { get; init; } = 1;

    /// <summary>The most lines an adviser actor may run at once (levers doc 1.2: 2 in wave 1).</summary>
    public int AdviserMaxLines { get; init; } = 2;

    /// <summary>Arm <c>DERIV</c> (spec §6.8): once a year the AI also derives a design from its newest root project. Diagnostic; off in main.</summary>
    public bool Derive { get; init; }

    /// <summary>Arm of the fleet rule (spec §6.10): main, or <c>NO_RETIRE</c>, <c>NO_SALE</c>, <c>PURCHASE_CAP</c>.</summary>
    public FleetArm FleetArm { get; init; } = FleetArm.Main;

    /// <summary><c>FLEET_LINE_COND</c> (spec A42.4, #147): <c>GEN</c> in main.</summary>
    public FleetLineCond FleetLineCond { get; init; } = FleetLineCond.Gen;

    /// <summary><c>LINE_CLOSE</c> (spec A41 F17b, #160): <c>YPROD</c> in main; <c>ADMIT</c> is the rule before F17b (regression, diagnostics).</summary>
    public LineCloseRule LineClose { get; init; } = LineCloseRule.YProd;

    /// <summary>
    /// The budget reserve account (#475, TASK-0150): shares may sum to less than 1, the remainder goes to <c>res.balance</c> (leaks about 10 % a year),
    /// and <c>fund_programme</c> pays from it. Off = the table, the hash and every golden are those of a build without the reserve.
    /// </summary>
    public bool Reserve { get; init; }

    /// <summary>
    /// Research priorities through institutes (TASK-0151, #484-#496): the institutes of the data stand in the table, <c>set_priority_node</c> gives a slot a goal
    /// (money first, institute bonus), the AI picks goals by the same rules. Off = no institute columns, the old knowledge path, the hash and every golden of a
    /// build without them.
    /// </summary>
    public bool Institutes { get; init; }

    /// <summary>
    /// Intelligence v1 (#515, TASK-0160): the seventh budget item security, up to three targets with a mode, networks with exposure, counter-intelligence, the theft
    /// channel in <c>c_eff</c>, estimates with confidence. Needs the <c>intelligence</c> object in the data. Off = the table, the hash and every golden are those of
    /// a build without it.
    /// </summary>
    public bool Intelligence { get; init; }

    /// <summary>
    /// The actor the player controls (null = none: Lab, tests, the views of the whole world). Not part of the data. With institutes the AI neither sets the priorities nor
    /// starts the projects of this actor: it only proposes (<c>InstituteView.ProposedGoal</c>, <c>ProposesBuild</c>), O-067, so it enters the fingerprint then. With
    /// intelligence on, the licence of another actor is shown to it only through intelligence (a visible estimate and the confidence of the hidden event,
    /// EVENTS_POLITICS.md §10); otherwise the view carries an empty licence.
    /// </summary>
    public int? PlayerActor { get; init; }

    /// <summary><c>BUDGET_RULE</c> (#430): <c>NOW</c> (constant growth) in main; <c>A</c>, <c>B1</c>, <c>B2</c> are measurement variants of slice-budget-convergence.md.</summary>
    public BudgetRule BudgetRule { get; init; } = BudgetRule.Now;

    /// <summary><c>A41_FORM</c> (#244, #245): SLOT = per slot (contract, main), WHOLE = the whole <c>superior</c> as the HYP-0004 hook. A switch of the Lab, not a default.</summary>
    public PilotForm PilotForm { get; init; } = PilotForm.Slot;

    /// <summary><c>PILOT_PROFILES</c> (#244): the profiles A41 applies to: P2+P3 in main (#245), P3 only as an option; P1 is never changed.</summary>
    public PilotProfileSet PilotProfiles { get; init; } = PilotProfileSet.P2P3;

    /// <summary><c>LIC_OFF</c> (spec §6.11, contract §8.6 п. 6): no licence offers and no <c>EVT_LIC</c> draws; disruptions and the why_not triggers stay.</summary>
    public bool LicOff { get; init; }

    /// <summary>
    /// <c>T6D_OFF</c> (spec §6.11, contract §8.6 п. 6): no licences, no disruptions, no why_not triggers and no <c>mult_P</c> — the world of the build before T6d
    /// except for the new columns.
    /// </summary>
    public bool T6dOff { get; init; }

    /// <summary><c>lic_knows</c> (spec §6.7, #47): a bought licence also makes known the seller's engine and the PRC of its mask with their closure. Diagnostic; off in main.</summary>
    public bool LicKnows { get; init; }

    /// <summary>
    /// The licence market (TASK-0152; production-lines-and-licence-market.md §2): several licences per country keyed (seller, gen), a permanent catalogue,
    /// <c>BuyLicence</c> / <c>OfferLicence</c>, AI buying by the rules of O-062. Off in main: with it off the world, the columns and the hash are those of main.
    /// </summary>
    public bool LicenceMarket { get; init; }

    /// <summary>Actor rows whose licences move only by commands (the player's); the AI buys nothing for them and sells at m = 1. Empty = every actor is AI.</summary>
    public ImmutableArray<int> LicenceExternalActors { get; init; } = [];

    /// <summary>Blocs and friends by year for the buyer circle (O-056), loaded from <c>mods/core/scenarios/1945/alliances.yaml</c> by the host; null = none.</summary>
    public AllianceTable? Alliances { get; init; }

    /// <summary>Sensitivity arm of the sale terms (spec §6.10).</summary>
    public SaleArm SaleArm { get; init; } = SaleArm.None;

    /// <summary><c>TOOLING_TARGET</c> (spec §11 T6e v0.19, #156): pass 2 of the tooling plan. V5 by default (#163); V0 is the ablation arm.</summary>
    public ToolingTargetSpec ToolingTarget { get; init; } = new();

    /// <summary>
    /// Skips validator S-2 (spec §1.1, <c>yield(t0) ≥ 0,7</c> of every mastered process). Only for the <c>RHMS_OFF</c> data set (mod <c>rhms_off</c>),
    /// which violates S-2 by design and is used for the before/after print (F32); never in a calibration or an experiment.
    /// </summary>
    public bool SkipStartValidation { get; init; }

    /// <summary>Layer control (spec §6.1); <see cref="LayerControl.Full"/> in main.</summary>
    public LayerControl Layer { get; init; }

    /// <summary>Moments of the full model for <c>L_K</c>: required exactly with <see cref="LayerControl.Lk"/>.</summary>
    public LkTables? LkTables { get; init; }

    /// <summary>Tables of <see cref="LayerControl.Lc"/> and <see cref="LayerControl.Lp"/>: required exactly with them.</summary>
    public CapabilityControlTables? CapabilityTables { get; init; }

    // Parameters of class «К» (spec §8.1, §8.2): the calibration moves them. Null / One / default array = the value of §8.1 (Core defaults),
    // and then no run.k.* column is written, so the hash is that of main.

    /// <summary>Common divisor <c>k</c> of <c>c_n</c> (#177): <c>c_n(type, g) = c_n§8.1(type, g) / k</c>, applied to the base by type; must be positive.</summary>
    public Fx6 CostDivisor { get; init; } = Fx6.One;

    /// <summary><c>θ_start</c> (knowledge); null = §8.1.</summary>
    public Fx6? ThetaStart { get; init; }

    /// <summary><c>h_max</c> (knowledge); null = §8.1. The grid axis <see cref="GridCell.HMax"/> scales this value.</summary>
    public Fx6? HMax { get; init; }

    /// <summary>Multiplier of <c>c_n</c> for generation 5 nodes (TASK-0131); null = the default of <c>KnowledgeParams</c> (6.4). Must be positive. G0…G4 are not overridable.</summary>
    public Fx6? CostMultG5 { get; init; }

    /// <summary>Multiplier of <c>c_n</c> for generation 6 nodes (TASK-0131); null = the default of <c>KnowledgeParams</c> (10.2). Must be positive.</summary>
    public Fx6? CostMultG6 { get; init; }

    /// <summary><c>k</c> of <c>Hcap(t)</c> (TASK-0134, #438): growth per year after 1978-01; null = the default of <c>IndustryParams</c>. 0 switches the growth off. Not negative.</summary>
    public Fx6? HcapGrowthPerYear { get; init; }

    /// <summary><c>ΔHmax</c> of <c>Hcap(t)</c>; null = the default of <c>IndustryParams</c>. 0 switches the growth off. Not negative.</summary>
    public Fx6? HcapGrowthMax { get; init; }

    /// <summary><c>k</c> of the ahead-of-time cost (TASK-0147, #458): multiplier <c>1 + k·ahead^pow</c> of a node's price, <c>ahead</c> in years before its anchor year; null = the default of <c>KnowledgeParams</c> (0.02; 0 = off, #465). Not negative.</summary>
    public Fx6? AheadCostK { get; init; }

    /// <summary>Integer exponent of the ahead-of-time cost, 1…4; null = the default (2).</summary>
    public int? AheadCostPow { get; init; }

    /// <summary>Ceiling of the ahead-of-time multiplier, at least 1; null = the default (10).</summary>
    public Fx6? AheadCostMax { get; init; }

    /// <summary><c>x_prog</c> of the mastery programme (O-045); null = the default (1). In (0; 1].</summary>
    public Fx6? MasteryXProg { get; init; }

    /// <summary><c>upkeep_mult</c> of the mastery programme; null = the default of <c>IndustryParams</c>. At least 1.</summary>
    public Fx6? MasteryUpkeepMult { get; init; }

    /// <summary>Parts of the mastery rules that run (arms B0-B3 of the probe); <see cref="MasteryRules.Main"/> in main (#452: foresight and programme).</summary>
    public MasteryRules MasteryRules { get; init; } = MasteryRules.Main;

    /// <summary><c>κ</c> as q10 (0…1024) of the bureau requirement; null = §8.1 (512).</summary>
    public int? KappaQ10 { get; init; }

    /// <summary><c>α</c> (culture growth); null = §8.1. The channel <see cref="SliceChannels.Alpha"/> still zeroes it.</summary>
    public Fx6? Alpha { get; init; }

    /// <summary><c>X½</c> of tiers 3, 4, 5; default (unset) array = §8.1.</summary>
    public ImmutableArray<Fx6> XHalfByTier { get; init; }

    /// <summary>
    /// Per-discipline multiplier of the research priority weight (<c>prio</c>) of every ENG/PRC/CMP/... node of that discipline (TASK-0147); default
    /// (unset) = 1 everywhere, no column and the hash of main. Names are scenario disciplines (aero, prop, metal, chem, mech), each at most once,
    /// weights positive. APP nodes have no discipline and keep weight 1.
    /// </summary>
    public ImmutableArray<DisciplineWeight> ResearchWeightByDiscipline { get; init; }

    /// <summary><c>δ_H</c> (culture forgetting); null = §8.1.</summary>
    public Fx6? DeltaH { get; init; }

    /// <summary><c>c_M</c> (tooling); null = §8.1.</summary>
    public Fx6? CM { get; init; }

    /// <summary>Point of the P5 grid (spec §7); <c>mid_mid_mid</c> = main.</summary>
    public GridCell Grid { get; init; }

    /// <summary>Arm <c>EFFORT_SHARE</c> (spec §6.8); <see cref="EffortShareArm.X1"/> = main.</summary>
    public EffortShareArm EffortShare { get; init; }

    /// <summary>Execution only (like <see cref="Threads"/>): what the read views hold. Not state, not hashed.</summary>
    public SliceViewMode ViewMode { get; init; }

    /// <summary>Execution only (ADR-0025): which actors the read views carry the node blocks of. <see cref="ViewScope.All"/> by default (Lab, tests); the client subscribes. Not state, not hashed.</summary>
    public ViewScope ViewScope { get; init; }

    /// <summary>
    /// Execution only (ADR-0016 T1a): adds an empty daily system, so every day copies the tables and every day takes a hash and a view snapshot.
    /// Not state, not hashed, no columns; the outcome of the run is that of the flag off. Off in main.
    /// </summary>
    public bool DailyTickProbe { get; init; }

    /// <summary>
    /// Execution only (ADR-0017 п. 3): the state hash is taken right after post on a tick with a monthly step and on a tick with applied
    /// external commands. On by default so that tests and Lab keep their checks; the game host sets it off. Start, save, load and the end
    /// of <c>Replay</c> always hash. Not state, not hashed.
    /// </summary>
    public bool HashCheckpoints { get; init; } = true;

    /// <summary>Execution only (ADR-0017 п. 4): a hash on every tick that publishes a view; implies <see cref="HashCheckpoints"/>. To find the first diverging day.</summary>
    public bool HashEveryTick { get; init; }

    /// <summary>Execution only (L2): threads and rows per chunk. They never change the result.</summary>
    public int Threads { get; init; } = 1;

    public int ChunkSize { get; init; } = 1;

    /// <summary><c>FIX_ALL</c>: every source fixed; with it all seeds give one trajectory (a determinism check).</summary>
    public static SliceRunConfig FixAll(ulong seed) => new() { Seed = seed, FixK = true, FixE = true, FixA = true };

    internal ulong SeedOf(bool fixedSource) => fixedSource ? FixedSeed : Seed;
}

/// <summary>The RNG streams of a run, one <see cref="CounterRng"/> per source (spec §9.1), the seed of a source taken from <c>FIX_*</c>.</summary>
public sealed record SliceStreams(CounterRng Know, CounterRng Events, CounterRng Ai, CounterRng Start)
{
    public static SliceStreams Create(SliceRunConfig config) => new(
        new CounterRng(config.SeedOf(config.FixK)), new CounterRng(config.SeedOf(config.FixE)),
        new CounterRng(config.SeedOf(config.FixA)), new CounterRng(config.Seed));
}

/// <summary>One entry of <see cref="SliceRunConfig.ResearchWeightByDiscipline"/>.</summary>
public readonly record struct DisciplineWeight(string Discipline, Fx6 Weight);
