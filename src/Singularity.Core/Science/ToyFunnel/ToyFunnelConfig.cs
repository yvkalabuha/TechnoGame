using System.Text.Json;
using Singularity.Core.Simulation;

namespace Singularity.Core.Science.ToyFunnel;

/// <summary>Model A (funnel) and controls B (single level τ) and C (fixed lag), spec §2, §2.8, §2.9.</summary>
public enum ToyFunnelVariant { A, B, C }

/// <summary>Training level for ρ_R or ρ_E: a constant level or the A6 schedule (spec §1).</summary>
public readonly record struct EducationLevel(double Level, bool Scheduled)
{
    public static EducationLevel Fixed(double level) => new(level, false);

    public static readonly EducationLevel Schedule = new(0, true);
}

/// <summary>Archetype row of HYP-0001 rev 2 with letter levels already resolved to numbers.</summary>
public sealed record ToyArchetype(
    string Id, double S, double E, double Pl, double Nl, EducationLevel EduR, EducationLevel EduE, double Iota);

/// <summary>
/// Parameters of one ToyFunnel run (spec §8; keys = manifest "params", same names as
/// lab/proto/model.py PARAMS). All behavioural coefficients are doubles by the spec; exponents
/// β, γ are kept as eighths for <c>DetMath.PowDyadic</c>.
/// </summary>
public sealed record ToyFunnelConfig
{
    public required ToyFunnelVariant Variant { get; init; }
    public required double Nu { get; init; }
    public required double Dt { get; init; }
    public required double DeltaK { get; init; }
    public required double P0 { get; init; }
    public required double G0 { get; init; }
    public required double Sigma0 { get; init; }
    public required double Mu { get; init; }
    public required double Eta { get; init; }
    public required double Theta { get; init; }
    public required double DeltaA { get; init; }
    public required int BetaEighths { get; init; }
    public required int GammaEighths { get; init; }
    public required double TRef { get; init; }
    public required double Z { get; init; }
    public required double Kappa { get; init; }
    public required double DeltaP { get; init; }
    public required double W { get; init; }
    public required double Rho0 { get; init; }
    public required double SR0 { get; init; }
    public required double SE0 { get; init; }
    public required double SP0 { get; init; }
    public required double SetupMultLo { get; init; }
    public required double SetupMultSpan { get; init; }
    public required double EduScheduleFrom { get; init; }
    public required double EduScheduleTo { get; init; }
    public required int EduScheduleMonths { get; init; }
    public required long MilestoneQ0Raw { get; init; }
    public required int MilestoneCount { get; init; }
    public required int StartYear { get; init; }
    public required double BCTau0 { get; init; }
    public required double BDeltaTau { get; init; }
    public required int CLagMonths { get; init; }
    public required double CPsi { get; init; }
    public required IReadOnlyList<ToyArchetype> Archetypes { get; init; }

    /// <summary>Number of countries; country i uses archetype i mod Archetypes.Count.</summary>
    public required int CountryCount { get; init; }

    /// <summary>
    /// Reads every key the model uses. "model" (A/B/C) and "nu" select the variant and noise;
    /// "archetypes" (array of {id, S, E, Pl, Nl, edu: [ρ_R, ρ_E], iota}) and "levels" ({l, m, h})
    /// are JSON; "countries" is optional (default: one country per archetype).
    /// </summary>
    public static ToyFunnelConfig FromParams(ParamSet p)
    {
        string model = p.GetString("model");
        ToyFunnelVariant variant = model switch
        {
            "A" => ToyFunnelVariant.A,
            "B" => ToyFunnelVariant.B,
            "C" => ToyFunnelVariant.C,
            _ => throw new ParamException($"Parameter 'model' must be A, B or C, got '{model}'."),
        };

        (double schedFrom, double schedTo, int schedMonths) = ReadSchedule(p.GetJson("edu_sched"));
        IReadOnlyList<ToyArchetype> archetypes = ReadArchetypes(p.GetJson("archetypes"), p.GetJson("levels"));

        var config = new ToyFunnelConfig
        {
            Variant = variant,
            Nu = p.GetDouble("nu"),
            Dt = p.GetDouble("dt"),
            DeltaK = p.GetDouble("delta_K"),
            P0 = p.GetDouble("p0"),
            G0 = p.GetDouble("g0"),
            Sigma0 = p.GetDouble("sigma0"),
            Mu = p.GetDouble("mu"),
            Eta = p.GetDouble("eta"),
            Theta = p.GetDouble("theta"),
            DeltaA = p.GetDouble("delta_A"),
            BetaEighths = Eighths(p.GetDouble("beta"), "beta"),
            GammaEighths = Eighths(p.GetDouble("gamma"), "gamma"),
            TRef = p.GetDouble("t_ref"),
            Z = p.GetDouble("z"),
            Kappa = p.GetDouble("kappa"),
            DeltaP = p.GetDouble("delta_P"),
            W = p.GetDouble("w"),
            Rho0 = p.GetDouble("rho0"),
            SR0 = p.GetDouble("s_R0"),
            SE0 = p.GetDouble("s_E0"),
            SP0 = p.GetDouble("s_P0"),
            SetupMultLo = p.GetDouble("setup_mult_lo"),
            SetupMultSpan = p.GetDouble("setup_mult_span"),
            EduScheduleFrom = schedFrom,
            EduScheduleTo = schedTo,
            EduScheduleMonths = schedMonths,
            MilestoneQ0Raw = p.GetFx6Raw("milestone_q0"),
            MilestoneCount = ToInt(p.GetLong("milestone_count"), "milestone_count"),
            StartYear = ToInt(p.GetLong("start_year"), "start_year"),
            BCTau0 = p.GetDouble("B_c_tau0"),
            BDeltaTau = p.GetDouble("B_delta_tau"),
            CLagMonths = ToInt(p.GetLong("C_lag_months"), "C_lag_months"),
            CPsi = p.GetDouble("C_psi"),
            Archetypes = archetypes,
            CountryCount = ToInt(p.GetLong("countries", archetypes.Count), "countries"),
        };
        config.Validate();
        return config;
    }

    /// <summary>Range checks that keep the model well defined; the dynamics themselves are checked by invariants.</summary>
    public void Validate()
    {
        Require(Nu >= 0, "nu must be >= 0");
        Require(Dt > 0, "dt must be > 0");
        Require(BetaEighths is >= 0 and <= 8, "beta must be in [0, 1]");
        Require(GammaEighths >= 0, "gamma must be >= 0");
        Require(TRef > 0, "t_ref must be > 0");
        Require(W > 0, "w must be > 0");
        Require(EduScheduleMonths >= 1, "edu_sched months must be >= 1");
        Require(MilestoneQ0Raw > 0, "milestone_q0 must be > 0");
        Require(MilestoneCount >= 1, "milestone_count must be >= 1");
        Require(CLagMonths >= 1, "C_lag_months must be >= 1");
        Require(Archetypes.Count >= 1, "archetypes must not be empty");
        Require(CountryCount >= 1, "countries must be >= 1");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new ParamException($"toy-funnel: {message}.");
    }

    /// <summary>x as a whole number of eighths (PowDyadic grid); anything else is an error, never rounded.</summary>
    public static int Eighths(double x, string key)
    {
        double k = x * 8.0;
        if (k != Math.Floor(k) || k < 0 || k > 1024)
            throw new ParamException($"Parameter '{key}' = {x} is not a non-negative multiple of 1/8.");
        return (int)k;
    }

    private static int ToInt(long value, string key) =>
        value is >= int.MinValue and <= int.MaxValue ? (int)value
        : throw new ParamException($"Parameter '{key}' = {value} is out of the 32-bit range.");

    private static (double From, double To, int Months) ReadSchedule(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement a = doc.RootElement;
        if (a.ValueKind != JsonValueKind.Array || a.GetArrayLength() != 3 || !a[2].TryGetInt32(out int months))
            throw new ParamException("Parameter 'edu_sched' must be [from, to, months].");
        return (a[0].GetDouble(), a[1].GetDouble(), months);
    }

    private static List<ToyArchetype> ReadArchetypes(string archetypesJson, string levelsJson)
    {
        using JsonDocument levelsDoc = JsonDocument.Parse(levelsJson);
        using JsonDocument doc = JsonDocument.Parse(archetypesJson);
        JsonElement levels = levelsDoc.RootElement;
        if (levels.ValueKind != JsonValueKind.Object)
            throw new ParamException("Parameter 'levels' must be an object {l, m, h}.");
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new ParamException("Parameter 'archetypes' must be an array.");

        var list = new List<ToyArchetype>();
        for (int i = 0; i < doc.RootElement.GetArrayLength(); i++)
        {
            JsonElement a = doc.RootElement[i];
            string where = $"archetypes[{i}]";
            JsonElement edu = Property(a, "edu", where);
            if (edu.ValueKind != JsonValueKind.Array || edu.GetArrayLength() != 2)
                throw new ParamException($"{where}.edu must be [rho_R level, rho_E level].");
            list.Add(new ToyArchetype(
                Id: Property(a, "id", where).GetString() ?? throw new ParamException($"{where}.id must be a string."),
                S: Level(levels, Property(a, "S", where), where),
                E: Level(levels, Property(a, "E", where), where),
                Pl: Level(levels, Property(a, "Pl", where), where),
                Nl: Level(levels, Property(a, "Nl", where), where),
                EduR: Education(levels, edu[0], where),
                EduE: Education(levels, edu[1], where),
                Iota: Property(a, "iota", where).GetDouble()));
        }
        return list;
    }

    private static JsonElement Property(JsonElement obj, string name, string where) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out JsonElement value) ? value
        : throw new ParamException($"{where}.{name} is required.");

    private static EducationLevel Education(JsonElement levels, JsonElement letter, string where) =>
        letter.ValueKind == JsonValueKind.String && letter.GetString() == "sched"
            ? EducationLevel.Schedule
            : EducationLevel.Fixed(Level(levels, letter, where));

    private static double Level(JsonElement levels, JsonElement letter, string where)
    {
        string? key = letter.ValueKind == JsonValueKind.String ? letter.GetString() : null;
        if (key is null || !levels.TryGetProperty(key, out JsonElement value))
            throw new ParamException($"{where}: level '{letter}' is not in 'levels'.");
        return value.GetDouble();
    }
}
