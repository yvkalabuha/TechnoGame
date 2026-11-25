using System.Text.Json;
using System.Collections.Immutable;
using Singularity.Core.Industry;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;
using Singularity.Session;

namespace Singularity.Lab.Models.Slice;

/// <summary>
/// Manifest parameters of the slice models → <see cref="SliceRunConfig"/> (slice-csv-format.md §3). An unknown key or value is a parameter error;
/// every key that has a default is optional, and the default is the behaviour of main.
/// </summary>
public sealed record SliceParameters(SliceRunConfig Config, int ForceDivisor, bool Full, string? ControlTablesPath = null, LeverSpec? Levers = null,
    InstituteSpec? Institutes = null, bool MarketProbe = false)
{
    /// <summary>Keys of §3 (ordinal order). <c>detail</c> is a key of <c>JetSlice</c> only.</summary>
    public static readonly string[] Keys =
    [
        "A41_FORM", "AHEAD_K", "AHEAD_MAX", "AHEAD_POW", "ALPHA", "BUDGET_RULE", "C_M", "CHANNELS", "COST_MULT_G5", "COST_MULT_G6", "CONTROL_TABLES", "DELTA_H", "DERIV", "DISC_WEIGHT", "EFFORT_SHARE", "FIX_A", "FIX_E", "FIX_K", "FLEET_ARM", "FLEET_LINE_COND", "FORCE", "GRID", "H_MAX",
        "HCAP_K", "HCAP_MAX", "INST_BUILD", "INST_FUND", "INST_GOALS", "INST_PLAYER", "INST_PROBE", "INSTITUTES", "INTEL", "K_DIV", "KAPPA_Q10", "LAYER", "LEVER_ACTOR", "LEVER_DIAG", "LEVER_ETA", "LEVER_SHIFT", "LEVERS", "LIC_KNOWS", "LIC_MARKET", "LIC_OFF", "LIC_PROBE", "LINE_CLOSE", "MASTERY_RULES", "POOL_ORDER", "PILOT_PROFILES", "PROFILE_CELL", "RESERVE", "RESERVE_ACCT", "RESERVE_SPEND", "SALE_ARM", "SLOTS", "START", "T6D_OFF",
        "TOOLING_H_HI", "TOOLING_H_HI_CONTROL", "TOOLING_H_LO", "TOOLING_M_LEAD", "TOOLING_RHO_MAX", "TOOLING_TARGET", "THETA_START", "UPKEEP_MULT", "X_PROG", "X_SCALE", "detail",
    ];

    /// <summary>Substrings no column (and no parameter of runs.csv) of the calibration build may contain (spec §8.2 п. 2, format §5).</summary>
    public static readonly string[] CalibrationForbidden = ["lead", "chg", "fv", "why", "key_", "volume"];

    public static SliceParameters Parse(ParamSet p, ulong seed, int threads, int chunkSize, bool calibration)
    {
        foreach (var kv in p.All)
        {
            if (Array.IndexOf(Keys, kv.Key) < 0)
                throw new ParamException($"Unknown parameter '{kv.Key}' of the slice model (slice-csv-format.md §3).");
            if (calibration && kv.Key == "detail")
                throw new ParamException("JetSliceCalib writes every table in full: 'detail' is a parameter of JetSlice only.");
            if (calibration && CalibrationForbidden.Any(s => kv.Key.Contains(s, StringComparison.OrdinalIgnoreCase)))
                throw new ParamException($"Parameter '{kv.Key}' may not be passed to the calibration build (spec §8.2 п. 2: forbidden substring).");
        }
        // CONTROL_TABLES (format §3.1, TASK-0054/0055) goes exactly with LAYER = LK, LC or LP: LK takes the schedule tables, LC and LP the
        // curves and T_design/rho of the same file (the model factory passes them to the Session).
        string layer = p.GetString("LAYER", "FULL");
        if ((layer is "LK" or "LC" or "LP") != p.Contains("CONTROL_TABLES"))
            throw new ParamException("CONTROL_TABLES is given exactly with LAYER = LK, LC or LP (slice-csv-format.md §3).");
        string? tables = p.Contains("CONTROL_TABLES") ? p.GetString("CONTROL_TABLES") : null;

        var defaults = new ToolingTargetSpec();
        ToolingTargetSpec tooling = Enum(p, "TOOLING_TARGET", "V5", ToolingTargetSpec.Parse) with
        {
            RhoMax = Fx6.FromRaw(p.GetFx6Raw("TOOLING_RHO_MAX", defaults.RhoMax.Raw)),
            HLo = Fx6.FromRaw(p.GetFx6Raw("TOOLING_H_LO", defaults.HLo.Raw)),
            HHi = Fx6.FromRaw(p.GetFx6Raw("TOOLING_H_HI", defaults.HHi.Raw)),
            MLead = Fx6.FromRaw(p.GetFx6Raw("TOOLING_M_LEAD", defaults.MLead.Raw)),
            HHiControl = Fx6.FromRaw(p.GetFx6Raw("TOOLING_H_HI_CONTROL", defaults.HHiControl.Raw)),
            PoolOrder = Enum(p, "POOL_ORDER", "CAN", ToolingTargetSpec.ParsePoolOrder),
        };
        Try("TOOLING_TARGET", tooling.Validate);

        int? cell = null;
        if (p.Contains("PROFILE_CELL"))
        {
            long c = p.GetLong("PROFILE_CELL");
            if (c is < 0 or > 26)
                throw new ParamException($"PROFILE_CELL = {c} is not in 0…26.");
            cell = (int)c;
        }

        var config = new SliceRunConfig
        {
            Seed = seed,
            Threads = threads,
            ChunkSize = chunkSize,
            ViewMode = calibration ? SliceViewMode.Calibration : SliceViewMode.Full,
            FixK = p.GetBool("FIX_K", false),
            FixE = p.GetBool("FIX_E", false),
            FixA = p.GetBool("FIX_A", false),
            Start = Enum(p, "START", "SCENARIO", v => v switch
            {
                "SCENARIO" => StartVariant.Scenario,
                "PERT" => StartVariant.Perturbed,
                "SYM" => StartVariant.Symmetric,
                _ => throw Bad("START", v),
            }),
            ProfileCell = cell,
            Channels = Channels(p),
            Derive = p.GetBool("DERIV", false),
            FleetArm = Enum(p, "FLEET_ARM", "MAIN", v => v switch
            {
                "MAIN" => FleetArm.Main,
                "NO_RETIRE" => FleetArm.NoRetire,
                "NO_SALE" => FleetArm.NoSale,
                "PURCHASE_CAP" => FleetArm.PurchaseCap,
                _ => throw Bad("FLEET_ARM", v),
            }),
            FleetLineCond = Enum(p, "FLEET_LINE_COND", "GEN", v => v switch
            {
                "GEN" => FleetLineCond.Gen,
                "KAPPA" => FleetLineCond.Kappa,
                "NONE" => FleetLineCond.None,
                _ => throw Bad("FLEET_LINE_COND", v),
            }),
            SaleArm = Enum(p, "SALE_ARM", "NONE", v => v switch
            {
                "NONE" => SaleArm.None,
                "SIGMA_LO" => SaleArm.SigmaLo,
                "PHI_FLAT" => SaleArm.PhiFlat,
                "QUOTA_LO" => SaleArm.QuotaLo,
                "QUOTA_HI" => SaleArm.QuotaHi,
                "CHI_OFF" => SaleArm.ChiOff,
                _ => throw Bad("SALE_ARM", v),
            }),
            LineClose = Enum(p, "LINE_CLOSE", "YPROD", LineCloseRules.Parse),
            BudgetRule = Enum(p, "BUDGET_RULE", "NOW", BudgetRules.Parse),
            PilotForm = Enum(p, "A41_FORM", "SLOT", v => v switch
            {
                "SLOT" => PilotForm.Slot,
                "WHOLE" => PilotForm.Whole,
                _ => throw Bad("A41_FORM", v),
            }),
            PilotProfiles = Enum(p, "PILOT_PROFILES", "P2P3", v => v switch
            {
                "P2P3" => PilotProfileSet.P2P3,
                "P3" => PilotProfileSet.P3,
                _ => throw Bad("PILOT_PROFILES", v),
            }),
            LicOff = p.GetBool("LIC_OFF", false),
            T6dOff = p.GetBool("T6D_OFF", false),
            LicKnows = p.GetBool("LIC_KNOWS", false),
            ToolingTarget = tooling,
            Layer = Enum(p, "LAYER", "FULL", v => v switch
            {
                "FULL" => LayerControl.Full,
                "LK" => LayerControl.Lk,
                "LK0" => LayerControl.Lk0,
                "LC0" => LayerControl.Lc0,
                "LC" => LayerControl.Lc,
                "LP" => LayerControl.Lp,
                _ => throw Bad("LAYER", v),
            }),
            CostDivisor = Fx6.FromRaw(p.GetFx6Raw("K_DIV", Fx6.One.Raw)),
            ThetaStart = Optional(p, "THETA_START"),
            HMax = Optional(p, "H_MAX"),
            KappaQ10 = p.Contains("KAPPA_Q10") ? checked((int)p.GetLong("KAPPA_Q10")) : null,
            Alpha = Optional(p, "ALPHA"),
            CostMultG5 = Positive(p, "COST_MULT_G5"),
            CostMultG6 = Positive(p, "COST_MULT_G6"),
            AheadCostK = AtLeast(p, "AHEAD_K", Fx6.Zero),
            AheadCostPow = AheadPow(p),
            AheadCostMax = AtLeast(p, "AHEAD_MAX", Fx6.One),
            ResearchWeightByDiscipline = DiscWeight(p),
            HcapGrowthPerYear = AtLeast(p, "HCAP_K", Fx6.Zero),
            HcapGrowthMax = AtLeast(p, "HCAP_MAX", Fx6.Zero),
            MasteryXProg = XProg(p),
            MasteryUpkeepMult = AtLeast(p, "UPKEEP_MULT", Fx6.One),
            MasteryRules = Enum(p, "MASTERY_RULES", "MAIN", ParseMasteryRules),
            XHalfByTier = XHalf(p),
            DeltaH = Optional(p, "DELTA_H"),
            CM = Optional(p, "C_M"),
            Grid = Enum(p, "GRID", "MID_MID_MID", Grid),
            EffortShare = Enum(p, "EFFORT_SHARE", "X1", v => v switch
            {
                "X1" => EffortShareArm.X1,
                "X0" => EffortShareArm.X0,
                "X05" => EffortShareArm.X05,
                "X2" => EffortShareArm.X2,
                "X4" => EffortShareArm.X4,
                _ => throw Bad("EFFORT_SHARE", v),
            }),
        };
        int force = Enum(p, "FORCE", "OFF", v => v switch
        {
            "OFF" => 0,
            "X1" => 1,
            "Q" => 4,
            _ => throw Bad("FORCE", v),
        });
        bool full = calibration || Enum(p, "detail", "full", v => v switch
        {
            "full" => true,
            "lite" => false,
            _ => throw Bad("detail", v),
        });
        // The levers of wave 1 (TASK-0171 turned them on in main; TASK-0176): LEVERS = LEGACY (default: all off, the build of every manifest before
        // TASK-0171, so their goldens stay) or ON (the defaults of SliceRunConfig, the game). INSTITUTES, LIC_MARKET, INTEL, RESERVE_ACCT override one flag.
        // The adviser needs a player (none in Lab unless INST_PLAYER), so it is not a key.
        // TASK-0187: SLOTS (bool) is the slot science model (SliceRunConfig.ScienceSlots); default = the levers flag, so LEVERS = ON is the game (slots on)
        // and LEGACY keeps the shares model even when INSTITUTES = true (the manifests of TASK-0173..0185 and their goldens). It needs the institutes.
        bool on = Enum(p, "LEVERS", "LEGACY", v => v switch
        {
            "LEGACY" => false,
            "ON" => true,
            _ => throw Bad("LEVERS", v),
        });
        config = config with
        {
            Institutes = p.GetBool("INSTITUTES", on),
            LicenceMarket = p.GetBool("LIC_MARKET", on),
            Intelligence = p.GetBool("INTEL", on),
            Reserve = p.GetBool("RESERVE_ACCT", on),
            AdviserForPlayer = on,
            ScienceSlots = p.GetBool("SLOTS", on),
        };
        if (p.Contains("SLOTS") && config.ScienceSlots && !config.Institutes)
            throw new ParamException("SLOTS = true needs the institutes (INSTITUTES = true or LEVERS = ON).");
        InstituteSpec? inst = ParseInstitutes(p, calibration, config.Institutes);
        LeverSpec? levers = ParseLevers(p, calibration);
        if (inst is { Fund: var fund } && fund < Fx6.One && levers?.Actor is not null)
            throw new ParamException("INST_FUND sets the shares of the player: it does not go with LEVER_ACTOR.");
        // TASK-0179: LIC_PROBE writes the table market (MarketProbe); it reads views only, so the world and its golden are unchanged.
        bool marketProbe = p.GetBool("LIC_PROBE", false);
        if (marketProbe && (calibration || !config.LicenceMarket))
            throw new ParamException("LIC_PROBE needs JetSlice with the licence market on (LIC_MARKET = true or LEVERS = ON).");
        return new SliceParameters(config, force, full, tables, levers, inst, marketProbe);
    }

    /// <summary>
    /// The institute keys of TASK-0173 (institutes-p7-measurement.md): INSTITUTES (bool, the run flag), INST_PLAYER (an actor id: the script plays its institutes,
    /// the AI only proposes), INST_BUILD (NONE, EARLY, HIST, HIST_M5, HIST_P5; default HIST), INST_GOALS (ADVISER, NONE; default ADVISER), INST_FUND (share of the
    /// planned research in (0; 1] while a project of the player runs; default 1), INST_PROBE (bool: inst_goal, inst_build, P-8 counters). The INST_* keys need
    /// INSTITUTES = true; INST_BUILD, INST_GOALS and INST_FUND need INST_PLAYER. None of them = null (no script).
    /// </summary>
    internal static InstituteSpec? ParseInstitutes(ParamSet p, bool calibration, bool on)
    {
        string[] keys = ["INST_BUILD", "INST_FUND", "INST_GOALS", "INST_PLAYER", "INST_PROBE"];
        if (!keys.Any(p.Contains))
            return null;
        if (calibration)
            throw new ParamException("The institute keys (INST_*) are parameters of JetSlice only.");
        if (!on)
            throw new ParamException("The INST_* keys need INSTITUTES = true.");
        string? player = p.Contains("INST_PLAYER") ? p.GetString("INST_PLAYER") : null;
        if (player is null && (p.Contains("INST_BUILD") || p.Contains("INST_GOALS") || p.Contains("INST_FUND")))
            throw new ParamException("INST_BUILD, INST_GOALS and INST_FUND need INST_PLAYER.");
        InstituteBuild build = Enum(p, "INST_BUILD", "HIST", v => v switch
        {
            "NONE" => InstituteBuild.None,
            "EARLY" => InstituteBuild.Early,
            "HIST" => InstituteBuild.Hist,
            "HIST_M5" => InstituteBuild.HistMinus5,
            "HIST_P5" => InstituteBuild.HistPlus5,
            _ => throw Bad("INST_BUILD", v),
        });
        bool goals = Enum(p, "INST_GOALS", "ADVISER", v => v switch
        {
            "ADVISER" => true,
            "NONE" => false,
            _ => throw Bad("INST_GOALS", v),
        });
        Fx6 fund = Fx6.FromRaw(p.GetFx6Raw("INST_FUND", Fx6.One.Raw));
        if (fund <= Fx6.Zero || fund > Fx6.One)
            throw new ParamException("INST_FUND must be in (0; 1].");
        return new InstituteSpec(player, build, goals, fund, p.GetBool("INST_PROBE", false));
    }

    /// <summary>
    /// The player lever keys of TASK-0148 (levers-probes.md §3): LEVER_ACTOR (an actor id, checked against the scenario when the run starts) with
    /// exactly one of LEVER_SHIFT (R/C/E/P then +10, -10 or 0) and RESERVE (a share in (0; 0,9]); RESERVE_SPEND (NONE, PROC, RES; PROC_DIRECT, RES_DIRECT = the same slot paid every month from month 0 with no account) only with
    /// RESERVE; LEVER_DIAG (bool) prints the research allocation by discipline. None given = null (no script, the run of main).
    /// </summary>
    internal static LeverSpec? ParseLevers(ParamSet p, bool calibration)
    {
        string[] keys = ["LEVER_ACTOR", "LEVER_DIAG", "LEVER_ETA", "LEVER_SHIFT", "RESERVE", "RESERVE_SPEND"];
        if (!keys.Any(p.Contains))
            return null;
        if (calibration)
            throw new ParamException("The lever keys (LEVER_*, RESERVE*) are parameters of JetSlice only.");
        string? actor = p.Contains("LEVER_ACTOR") ? p.GetString("LEVER_ACTOR") : null;
        bool shift = p.Contains("LEVER_SHIFT"), reserve = p.Contains("RESERVE");
        if (actor is null != (!shift && !reserve))
            throw new ParamException("LEVER_ACTOR goes with LEVER_SHIFT or RESERVE, and they need LEVER_ACTOR.");
        if (shift && reserve)
            throw new ParamException("LEVER_SHIFT and RESERVE are separate probes (P1, P2): give one of them.");
        if (p.Contains("RESERVE_SPEND") && !reserve)
            throw new ParamException("RESERVE_SPEND needs RESERVE.");
        LeverItem? item = null;
        LeverOp op = LeverOp.Plus10;
        if (shift)
            (item, op) = LeverSpec.ParseShift(p.GetString("LEVER_SHIFT"));
        Fx6 r = reserve ? Fx6.FromRaw(p.GetFx6Raw("RESERVE")) : Fx6.Zero;
        if (reserve && (r <= Fx6.Zero || r > LeverSpec.ReserveMax))
            throw new ParamException($"RESERVE must be in (0; {LeverSpec.ReserveMax}].");
        ReserveSpend spend = Enum(p, "RESERVE_SPEND", "NONE", v => v switch
        {
            "NONE" => ReserveSpend.None,
            "PROC" => ReserveSpend.Procurement,
            "RES" => ReserveSpend.Research,
            "PROC_DIRECT" => ReserveSpend.ProcurementDirect,
            "RES_DIRECT" => ReserveSpend.ResearchDirect,
            _ => throw Bad("RESERVE_SPEND", v),
        });
        return new LeverSpec(actor, item, op, r, spend, p.GetBool("LEVER_DIAG", false), p.GetBool("LEVER_ETA", false));
    }

    private static Fx6? Optional(ParamSet p, string key) => p.Contains(key) ? Fx6.FromRaw(p.GetFx6Raw(key)) : null;

    // COST_MULT_G5/G6 (TASK-0131): c_n multipliers of generations 5 and 6; the Session refuses a non-positive one with ArgumentException, the
    // Lab refuses it earlier as a parameter error.
    private static Fx6? Positive(ParamSet p, string key)
    {
        Fx6? v = Optional(p, key);
        if (v is { } x && x <= Fx6.Zero)
            throw new ParamException($"{key} must be positive.");
        return v;
    }

    // HCAP_K, HCAP_MAX (TASK-0134, #438): k and ΔHmax of Hcap(t), ≥ 0; UPKEEP_MULT: upkeep_mult of the mastery programme, ≥ 1. Absent = null
    // (the defaults of IndustryParams); the Session refuses a bad value with ArgumentException, the Lab refuses it earlier as a parameter error.
    private static Fx6? AtLeast(ParamSet p, string key, Fx6 min)
    {
        Fx6? v = Optional(p, key);
        if (v is { } x && x < min)
            throw new ParamException($"{key} must be at least {min}.");
        return v;
    }

    // AHEAD_POW (TASK-0147, #458): integer exponent of the ahead-of-time cost, 1…4 (AHEAD_K ≥ 0, AHEAD_MAX ≥ 1 go through AtLeast). Absent = null
    // (the defaults of KnowledgeParams: k 0.02, pow 2, max 10, owner #465; k 0 = off); the Session refuses a bad value with ArgumentException, the Lab earlier.
    private static int? AheadPow(ParamSet p)
    {
        if (!p.Contains("AHEAD_POW"))
            return null;
        long v = p.GetLong("AHEAD_POW");
        if (v is < 1 or > 4)
            throw new ParamException("AHEAD_POW must be an integer in 1…4.");
        return (int)v;
    }

    /// <summary>
    /// DISC_WEIGHT (TASK-0147, §10.5b): per-discipline multiplier of the research priority weight, a string <c>"prop:1.15,metal:0.90"</c>. Names are
    /// lower-case scenario disciplines, each at most once; a weight is a positive decimal with at most 6 fractional digits (exact in Fx6). Absent =
    /// default (weight 1 everywhere, hash of main). An unknown discipline is refused by the Session when the world is built.
    /// </summary>
    internal static ImmutableArray<DisciplineWeight> DiscWeight(ParamSet p)
    {
        if (!p.Contains("DISC_WEIGHT"))
            return default;
        string v = p.GetString("DISC_WEIGHT");
        var list = ImmutableArray.CreateBuilder<DisciplineWeight>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string part in v.Split(','))
        {
            string[] kv = part.Split(':');
            if (kv.Length != 2 || kv[0].Length == 0 || !kv[0].All(c => c is >= 'a' and <= 'z'))
                throw new ParamException($"DISC_WEIGHT: '{part}' is not 'discipline:weight' (e.g. \"prop:1.15,metal:0.90\").");
            if (!seen.Add(kv[0]))
                throw new ParamException($"DISC_WEIGHT: discipline '{kv[0]}' given twice.");
            if (!decimal.TryParse(kv[1], System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out decimal w))
                throw new ParamException($"DISC_WEIGHT: weight '{kv[1]}' of '{kv[0]}' is not a decimal number.");
            decimal raw = w * 1_000_000m;
            if (raw != decimal.Truncate(raw))
                throw new ParamException($"DISC_WEIGHT: weight '{kv[1]}' has more than 6 fractional digits.");
            if (raw <= 0 || raw > long.MaxValue / 2)
                throw new ParamException($"DISC_WEIGHT: weight of '{kv[0]}' must be positive.");
            list.Add(new DisciplineWeight(kv[0], Fx6.FromRaw((long)raw)));
        }
        return list.ToImmutable();
    }

    // X_PROG (TASK-0134, O-045): x_prog of the mastery programme in (0; 1].
    private static Fx6? XProg(ParamSet p)
    {
        Fx6? v = Optional(p, "X_PROG");
        if (v is { } x && (x <= Fx6.Zero || x > Fx6.One))
            throw new ParamException("X_PROG must be in (0; 1].");
        return v;
    }

    /// <summary>
    /// MASTERY_RULES (TASK-0134): <c>MAIN</c> (default: <see cref="MasteryRules.Main"/> = FORESIGHT+PROGRAMME, owner decision #452), <c>ALL</c>, <c>NONE</c>, or parts joined by <c>+</c>: <c>GATE</c>, <c>CLOSE</c>, <c>FORESIGHT</c>,
    /// <c>PROGRAMME</c> (arms B0-B3 of the probe, g5-g6-probe.md §9). A part given twice is an error.
    /// </summary>
    internal static MasteryRules ParseMasteryRules(string v)
    {
        if (v == "MAIN")
            return MasteryRules.Main;
        if (v == "ALL")
            return MasteryRules.All;
        if (v == "NONE")
            return MasteryRules.None;
        MasteryRules r = MasteryRules.None;
        foreach (string part in v.Split('+'))
        {
            MasteryRules one = part switch
            {
                "GATE" => MasteryRules.Gate,
                "CLOSE" => MasteryRules.CandidateClose,
                "FORESIGHT" => MasteryRules.Foresight,
                "PROGRAMME" => MasteryRules.Programme,
                _ => throw new ArgumentException($"unknown part '{part}' of '{v}' (ALL, NONE or GATE+CLOSE+FORESIGHT+PROGRAMME)."),
            };
            if ((r & one) != 0)
                throw new ArgumentException($"part '{part}' given twice in '{v}'.");
            r |= one;
        }
        return r;
    }

    // X_SCALE (calibration stage C1): X½ of tiers 3, 4, 5 times the factor, their ratio 1 : 4 : 12,5 kept; floor on the raw value.
    private static ImmutableArray<Fx6> XHalf(ParamSet p)
    {
        if (!p.Contains("X_SCALE"))
            return default;
        Fx6 scale = Fx6.FromRaw(p.GetFx6Raw("X_SCALE"));
        if (scale <= Fx6.Zero)
            throw new ParamException("X_SCALE must be positive.");
        return [.. new IndustryParams().XHalfByTier.Select(x => x * scale)];
    }

    private static SliceChannels Channels(ParamSet p)
    {
        if (!p.Contains("CHANNELS"))
            return SliceChannels.None;
        using JsonDocument doc = JsonDocument.Parse(p.GetJson("CHANNELS"));
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new ParamException("CHANNELS must be an array of channel names.");
        SliceChannels c = SliceChannels.None;
        foreach (JsonElement e in doc.RootElement.EnumerateArray())
        {
            string name = e.ValueKind == JsonValueKind.String ? e.GetString()! : throw new ParamException("CHANNELS entries must be strings.");
            SliceChannels one = name switch
            {
                "ALPHA" => SliceChannels.Alpha,
                "RAMP" => SliceChannels.Ramp,
                "B" => SliceChannels.B,
                "ETA" => SliceChannels.Eta,
                "KCATCH" => SliceChannels.KCatch,
                "SIGMA" => SliceChannels.Sigma,
                "KDIFF" => SliceChannels.KDiff,
                "E0" => SliceChannels.EffortZero,
                "KTR" => SliceChannels.KTr,
                "KQP" => SliceChannels.KQp,
                "R0" => SliceChannels.CultureR0,
                _ => throw Bad("CHANNELS", name),
            };
            if ((c & one) != 0)
                throw new ParamException($"CHANNELS lists '{name}' twice.");
            c |= one;
        }
        return c;
    }

    private static GridCell Grid(string value)
    {
        string[] parts = value.Split('_');
        if (parts.Length != 3)
            throw Bad("GRID", value);
        GridStep Step(string s) => s switch
        {
            "LO" => GridStep.Lo,
            "MID" => GridStep.Mid,
            "HI" => GridStep.Hi,
            _ => throw Bad("GRID", value),
        };
        return new GridCell(Step(parts[0]), Step(parts[1]), Step(parts[2]));
    }

    private static T Enum<T>(ParamSet p, string key, string fallback, Func<string, T> parse)
    {
        string v = p.GetString(key, fallback);
        try
        {
            return parse(v);
        }
        catch (ArgumentException e)
        {
            throw new ParamException($"{key}: {e.Message}");
        }
    }

    private static void Try(string key, Action check)
    {
        try
        {
            check();
        }
        catch (ArgumentException e)
        {
            throw new ParamException($"{key}: {e.Message}");
        }
    }

    private static ParamException Bad(string key, string value) => new($"{key}: unknown value '{value}' (slice-csv-format.md §3).");
}
