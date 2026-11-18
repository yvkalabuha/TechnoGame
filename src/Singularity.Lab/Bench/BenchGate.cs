using System.Globalization;
using System.Text.Json;

namespace Singularity.Lab.Bench;

/// <summary>How a process whose every attempt is flagged is resolved (ADR-0023 gate addition).</summary>
public enum ExclusionRule
{
    /// <summary>EXP-0008 П5.4 text: every attempt with any flag (foreign_cpu or median) → excluded.</summary>
    AnyFlag,
    /// <summary>
    /// The median flag only triggers a repeat; exclusion needs independent evidence of foreign load (foreign_cpu or run delay above its
    /// limit) on every attempt. Otherwise the process is accepted with its first attempt without such evidence.
    /// </summary>
    IndependentEvidence,
}

/// <summary>
/// Gate parameters of a series (manifest key <c>gate</c>; EXP-0008 П5.2–П5.4, ADR-0023 п. 1.9). Defaults are the EXP-0008 pre-registration;
/// the ADR-level numbers (10 % noise, 20 % exclusions, 2 repeats, majority of replicas) are parameters too so the gate can change them
/// without code, but every value is fixed in the manifest and its hash before the first measurement.
/// </summary>
public sealed record BenchGate
{
    public double ForeignCpuMax { get; init; } = 0.05;
    public double MedianRatioMax { get; init; } = 1.10;
    public double MedianGateMinMs { get; init; } = 50;
    public int MaxRepeats { get; init; } = 2;
    public double ExcludeMaxFrac { get; init; } = 0.20;
    /// <summary>A seed of the outcome block needs more than this fraction of its replicas accepted (ADR-0023 п. 1.7: more than half).</summary>
    public double SeedAcceptMinFrac { get; init; } = 0.5;
    public string OutcomeBlock { get; init; } = "A";
    public ExclusionRule ExclusionRule { get; init; } = ExclusionRule.IndependentEvidence;
    /// <summary>[Предл.] Run-queue wait share of the own threads above which an attempt has evidence of foreign load (independent-evidence).</summary>
    public double RunDelayMax { get; init; } = 0.05;
    public double ThermalRatio { get; init; } = 0.85;
    public double NoiseMax { get; init; } = 0.10;
    public int QuietSampleSeconds { get; init; } = 10;
    public int QuietStepSeconds { get; init; } = 30;
    public int QuietMaxSeconds { get; init; } = 300;
    public int PreSeriesSeconds { get; init; } = 60;
    public double PreSeriesForeignMax { get; init; } = 0.05;
    public int FRefSeconds { get; init; } = 300;
    public int FRefTailSeconds { get; init; } = 180;

    internal static readonly string[] Keys =
    [
        "foreign_cpu_max", "median_ratio_max", "median_gate_min_ms", "max_repeats", "exclude_max_frac", "seed_accept_min_frac", "outcome_block",
        "exclusion_rule", "run_delay_max", "thermal_ratio", "noise_max", "quiet_sample_s", "quiet_step_s", "quiet_max_s", "pre_series_s",
        "pre_series_foreign_max", "fref_seconds", "fref_tail_s", "note",
    ];

    public static BenchGate Parse(JsonElement e)
    {
        BenchManifest.CheckKeys(e, Keys, "gate");
        var d = new BenchGate();
        double F(string k, double def, double min, double max)
        {
            if (!e.TryGetProperty(k, out JsonElement v))
                return def;
            return v.ValueKind == JsonValueKind.Number && v.GetDouble() is double x && x >= min && x <= max
                ? x : throw new ManifestException($"gate.{k} must be a number in [{min.ToString(CultureInfo.InvariantCulture)}, {max.ToString(CultureInfo.InvariantCulture)}]");
        }
        int I(string k, int def, int min, int max)
        {
            if (!e.TryGetProperty(k, out JsonElement v))
                return def;
            return v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int x) && x >= min && x <= max
                ? x : throw new ManifestException($"gate.{k} must be an integer in [{min}, {max}]");
        }
        string rule = e.TryGetProperty("exclusion_rule", out JsonElement r) ? r.GetString() ?? "" : "independent-evidence";
        var g = new BenchGate
        {
            ForeignCpuMax = F("foreign_cpu_max", d.ForeignCpuMax, 0, 1),
            MedianRatioMax = F("median_ratio_max", d.MedianRatioMax, 1, 100),
            MedianGateMinMs = F("median_gate_min_ms", d.MedianGateMinMs, 0, 1e9),
            MaxRepeats = I("max_repeats", d.MaxRepeats, 0, 10),
            ExcludeMaxFrac = F("exclude_max_frac", d.ExcludeMaxFrac, 0, 1),
            SeedAcceptMinFrac = F("seed_accept_min_frac", d.SeedAcceptMinFrac, 0, 1),
            OutcomeBlock = e.TryGetProperty("outcome_block", out JsonElement ob) && ob.ValueKind == JsonValueKind.String ? ob.GetString()! : d.OutcomeBlock,
            ExclusionRule = rule switch
            {
                "any-flag" => ExclusionRule.AnyFlag,
                "independent-evidence" => ExclusionRule.IndependentEvidence,
                _ => throw new ManifestException("gate.exclusion_rule must be \"any-flag\" or \"independent-evidence\""),
            },
            RunDelayMax = F("run_delay_max", d.RunDelayMax, 0, 1e6),
            ThermalRatio = F("thermal_ratio", d.ThermalRatio, 0, 1),
            NoiseMax = F("noise_max", d.NoiseMax, 0, 10),
            QuietSampleSeconds = I("quiet_sample_s", d.QuietSampleSeconds, 1, 3600),
            QuietStepSeconds = I("quiet_step_s", d.QuietStepSeconds, 1, 3600),
            QuietMaxSeconds = I("quiet_max_s", d.QuietMaxSeconds, 0, 86_400),
            PreSeriesSeconds = I("pre_series_s", d.PreSeriesSeconds, 1, 86_400),
            PreSeriesForeignMax = F("pre_series_foreign_max", d.PreSeriesForeignMax, 0, 1),
            FRefSeconds = I("fref_seconds", d.FRefSeconds, 2, 86_400),
            FRefTailSeconds = I("fref_tail_s", d.FRefTailSeconds, 1, 86_400),
        };
        if (g.FRefTailSeconds > g.FRefSeconds)
            throw new ManifestException("gate.fref_tail_s must not exceed gate.fref_seconds");
        return g;
    }

    public void Write(Utf8JsonWriter j)
    {
        j.WriteStartObject("gate");
        j.WriteNumber("foreign_cpu_max", ForeignCpuMax);
        j.WriteNumber("median_ratio_max", MedianRatioMax);
        j.WriteNumber("median_gate_min_ms", MedianGateMinMs);
        j.WriteNumber("max_repeats", MaxRepeats);
        j.WriteNumber("exclude_max_frac", ExcludeMaxFrac);
        j.WriteNumber("seed_accept_min_frac", SeedAcceptMinFrac);
        j.WriteString("outcome_block", OutcomeBlock);
        j.WriteString("exclusion_rule", ExclusionRule == ExclusionRule.AnyFlag ? "any-flag" : "independent-evidence");
        j.WriteNumber("run_delay_max", RunDelayMax);
        j.WriteNumber("thermal_ratio", ThermalRatio);
        j.WriteNumber("noise_max", NoiseMax);
        j.WriteNumber("quiet_sample_s", QuietSampleSeconds);
        j.WriteNumber("quiet_step_s", QuietStepSeconds);
        j.WriteNumber("quiet_max_s", QuietMaxSeconds);
        j.WriteNumber("pre_series_s", PreSeriesSeconds);
        j.WriteNumber("pre_series_foreign_max", PreSeriesForeignMax);
        j.WriteNumber("fref_seconds", FRefSeconds);
        j.WriteNumber("fref_tail_s", FRefTailSeconds);
        j.WriteEndObject();
    }
}

/// <summary>A П9 п. 1 reference variant: processes of one block's point with execution switches (e.g. <c>hc</c>, <c>noop</c>).</summary>
public sealed record BenchVariant(string Id, string Block, IReadOnlyList<ulong> Seeds, IReadOnlyList<(string Key, string Value)> Switches);

/// <summary>A П9 п. 2–5 reference mode run as one child: <c>lab bench --mode M --key value …</c>.</summary>
public sealed record BenchModeRun(string Id, string Mode, IReadOnlyList<(string Key, string Value)> Args);

/// <summary>The П6 profile process: one point of a block under a sampling tool.</summary>
public sealed record BenchProfile(string Block, ulong Seed, int Warmup, int Measure, string Runs, string Tool, IReadOnlyList<string> ToolArgs);

/// <summary>Reference part of a series (manifest key <c>reference</c>; EXP-0008 П6, П9): validity before the main pass, variants, R modes, profile.</summary>
public sealed record BenchReference
{
    public IReadOnlyList<(string Key, string Value)>? Validity { get; init; }
    public IReadOnlyList<BenchVariant> Variants { get; init; } = [];
    public IReadOnlyList<BenchModeRun> Modes { get; init; } = [];
    public BenchProfile? Profile { get; init; }

    private static readonly string[] Keys = ["validity", "variants", "modes", "profile", "note"];
    private static readonly string[] SwitchKeys = ["hash-checkpoints", "hash-every-tick", "daily-noop", "full-copy"];
    private static readonly string[] ModeNames = ["r1", "r2", "kcopy", "r3"];
    private static readonly string[] ForbiddenArgs = ["out", "mode", "manifest", "allow-debug"];

    public static BenchReference Parse(JsonElement e, IReadOnlyList<BenchBlock> blocks)
    {
        BenchManifest.CheckKeys(e, Keys, "reference");
        var r = new BenchReference();
        if (e.TryGetProperty("validity", out JsonElement v))
            r = r with { Validity = Args(v, "reference.validity") };
        if (e.TryGetProperty("variants", out JsonElement vs))
        {
            var list = new List<BenchVariant>();
            foreach (JsonElement x in Arr(vs, "reference.variants"))
            {
                BenchManifest.CheckKeys(x, ["id", "block", "seeds", "switches", "note"], "reference variant");
                string id = Str(x, "id");
                string block = Str(x, "block");
                if (!blocks.Any(b => b.Id == block))
                    throw new ManifestException($"reference variant {id}: unknown block '{block}'");
                ulong[] seeds = [.. Arr(Get(x, "seeds"), $"variant {id} seeds").Select(s => s.GetUInt64())];
                var sw = Args(Get(x, "switches"), $"variant {id} switches");
                if (sw.Any(a => Array.IndexOf(SwitchKeys, a.Key) < 0 || a.Value is not ("true" or "false")))
                    throw new ManifestException($"variant {id}: switches are {string.Join(", ", SwitchKeys)} with \"true\"|\"false\"");
                list.Add(new BenchVariant(id, block, seeds, sw));
            }
            r = r with { Variants = list };
        }
        if (e.TryGetProperty("modes", out JsonElement ms))
        {
            var list = new List<BenchModeRun>();
            foreach (JsonElement x in Arr(ms, "reference.modes"))
            {
                BenchManifest.CheckKeys(x, ["id", "mode", "args", "note"], "reference mode");
                string id = Str(x, "id"), mode = Str(x, "mode");
                if (Array.IndexOf(ModeNames, mode) < 0)
                    throw new ManifestException($"reference mode {id}: mode must be one of {string.Join(", ", ModeNames)}");
                list.Add(new BenchModeRun(id, mode, x.TryGetProperty("args", out JsonElement a) ? Args(a, $"mode {id} args") : []));
            }
            r = r with { Modes = list };
        }
        if (e.TryGetProperty("profile", out JsonElement p))
        {
            BenchManifest.CheckKeys(p, ["block", "seed", "warmup_months", "measure_months", "runs", "tool", "tool_args", "note"], "reference profile");
            string block = Str(p, "block");
            if (!blocks.Any(b => b.Id == block))
                throw new ManifestException($"reference profile: unknown block '{block}'");
            string runs = Str(p, "runs");
            if (runs is not ("daily" or "monthly" or "both"))
                throw new ManifestException("reference profile: runs must be daily, monthly or both");
            r = r with
            {
                Profile = new BenchProfile(block, Get(p, "seed").GetUInt64(), Get(p, "warmup_months").GetInt32(), Get(p, "measure_months").GetInt32(),
                    runs, Str(p, "tool"), [.. Arr(Get(p, "tool_args"), "profile tool_args").Select(a => a.GetString() ?? "")]),
            };
        }
        var ids = r.Variants.Select(x => x.Id).Concat(r.Modes.Select(x => x.Id)).ToList();
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
            throw new ManifestException("reference ids must be distinct");
        return r;
    }

    private static List<(string Key, string Value)> Args(JsonElement e, string what)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new ManifestException($"{what} must be an object of option → string");
        var list = new List<(string Key, string Value)>();
        foreach (JsonProperty p in e.EnumerateObject())
        {
            if (p.Value.ValueKind != JsonValueKind.String)
                throw new ManifestException($"{what}.{p.Name} must be a string");
            if (Array.IndexOf(ForbiddenArgs, p.Name) >= 0)
                throw new ManifestException($"{what}: '{p.Name}' is set by the series driver");
            list.Add((p.Name, p.Value.GetString()!));
        }
        return list;
    }

    private static JsonElement Get(JsonElement e, string key) =>
        e.TryGetProperty(key, out JsonElement v) ? v : throw new ManifestException($"missing key '{key}'");

    private static string Str(JsonElement e, string key) =>
        Get(e, key) is { ValueKind: JsonValueKind.String } v ? v.GetString()! : throw new ManifestException($"'{key}' must be a string");

    private static JsonElement.ArrayEnumerator Arr(JsonElement e, string what) =>
        e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : throw new ManifestException($"{what} must be an array");
}
