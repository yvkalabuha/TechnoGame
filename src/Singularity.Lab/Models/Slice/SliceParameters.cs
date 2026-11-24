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
public sealed record SliceParameters(SliceRunConfig Config, int ForceDivisor, bool Full, string? ControlTablesPath = null)
{
    /// <summary>Keys of §3 (ordinal order). <c>detail</c> is a key of <c>JetSlice</c> only.</summary>
    public static readonly string[] Keys =
    [
        "A41_FORM", "ALPHA", "BUDGET_RULE", "C_M", "CHANNELS", "COST_MULT_G5", "COST_MULT_G6", "CONTROL_TABLES", "DELTA_H", "DERIV", "EFFORT_SHARE", "FIX_A", "FIX_E", "FIX_K", "FLEET_ARM", "FLEET_LINE_COND", "FORCE", "GRID", "H_MAX",
        "HCAP_K", "HCAP_MAX", "K_DIV", "KAPPA_Q10", "LAYER", "LIC_KNOWS", "LIC_OFF", "LINE_CLOSE", "MASTERY_RULES", "POOL_ORDER", "PILOT_PROFILES", "PROFILE_CELL", "SALE_ARM", "START", "T6D_OFF",
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
            HcapGrowthPerYear = AtLeast(p, "HCAP_K", Fx6.Zero),
            HcapGrowthMax = AtLeast(p, "HCAP_MAX", Fx6.Zero),
            MasteryXProg = XProg(p),
            MasteryUpkeepMult = AtLeast(p, "UPKEEP_MULT", Fx6.One),
            MasteryRules = Enum(p, "MASTERY_RULES", "ALL", ParseMasteryRules),
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
        return new SliceParameters(config, force, full, tables);
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

    // X_PROG (TASK-0134, O-045): x_prog of the mastery programme in (0; 1].
    private static Fx6? XProg(ParamSet p)
    {
        Fx6? v = Optional(p, "X_PROG");
        if (v is { } x && (x <= Fx6.Zero || x > Fx6.One))
            throw new ParamException("X_PROG must be in (0; 1].");
        return v;
    }

    /// <summary>
    /// MASTERY_RULES (TASK-0134): <c>ALL</c> (default, main), <c>NONE</c>, or parts joined by <c>+</c>: <c>GATE</c>, <c>CLOSE</c>, <c>FORESIGHT</c>,
    /// <c>PROGRAMME</c> (arms B0-B3 of the probe, g5-g6-probe.md §9). A part given twice is an error.
    /// </summary>
    internal static MasteryRules ParseMasteryRules(string v)
    {
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
