using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Knowledge;
using Singularity.Kernel;

namespace Singularity.Lab.Models.Slice;

/// <summary>
/// The file of the manifest key <c>CONTROL_TABLES</c> (slice-csv-format.md §3.1, format <c>control_tables/1</c>; TASK-0054): the tables of the
/// layer controls of spec §6.1, written by <c>exp0002.py --calib … --tables-out</c> from the calibration build. Only integers (Fx6 raw values
/// and months), canonical JSON (sorted keys, no whitespace), so <see cref="Sha256"/> of the bytes is <c>control_tables_hash</c> in Python and
/// here alike. <see cref="Lk"/> goes to the Session (<c>LAYER = LK</c>); <see cref="Lc"/> and the <c>L_P</c> tables are read and checked, but the
/// Session takes them with <c>LAYER = LC/LP</c> (<c>CapabilityControlTables</c>, test API v4).
/// </summary>
public sealed class ControlTables
{
    public const string Format = "control_tables/1";
    public const int Generations = 4;
    public static readonly int[] Tiers = [3, 4, 5];

    private ControlTables(string sha256, LkTables lk, ImmutableArray<CapabilityCurve> lc, ImmutableArray<int> tDesign, ImmutableArray<Fx6> rho)
    {
        Sha256 = sha256;
        Lk = lk;
        Lc = lc;
        TDesign = tDesign;
        Rho = rho;
    }

    /// <summary>SHA-256 of the file bytes = <c>control_tables_hash</c> (manifest meta, run.json model_info).</summary>
    public string Sha256 { get; }

    public LkTables Lk { get; }

    /// <summary><c>Y_k</c>, <c>U_k</c> for tiers 3, 4, 5 on every integer τ 0…2·H−1 (spec §6.1 v0.24 r2).</summary>
    public ImmutableArray<CapabilityCurve> Lc { get; }

    /// <summary><c>T_design[g]</c>, g = 1…4 (index g − 1), months.</summary>
    public ImmutableArray<int> TDesign { get; }

    /// <summary><c>ρ[g]</c>, g = 1…4 (index g − 1).</summary>
    public ImmutableArray<Fx6> Rho { get; }

    public static ControlTables Load(string path, ScenarioData scenario, NodeTable nodes, int horizon) =>
        Parse(File.ReadAllBytes(path), scenario, nodes, horizon, path);

    public static ControlTables Parse(byte[] bytes, ScenarioData scenario, NodeTable nodes, int horizon, string name = "CONTROL_TABLES")
    {
        string text = Encoding.UTF8.GetString(bytes);
        string canonical;
        try
        {
            canonical = CanonicalJson.Canonicalize(text);
        }
        catch (Exception e) when (e is JsonException or ManifestException)
        {
            throw Bad(name, $"not JSON: {e.Message}");
        }
        if (canonical != text)
            throw Bad(name, "not in canonical form (sorted keys, no whitespace, no trailing newline): the hash would depend on the writer");
        using JsonDocument doc = JsonDocument.Parse(text);
        JsonElement root = doc.RootElement;
        if (Str(root, "format", name) != Format)
            throw Bad(name, $"format is not {Format}");
        if (Int(root, "horizon", name) != horizon)
            throw Bad(name, $"horizon is not {horizon}");
        string[] actors = [.. Prop(root, "actors", name).EnumerateArray().Select(e => e.GetString() ?? "")];
        if (!actors.SequenceEqual(scenario.ActorId))
            throw Bad(name, $"actors [{string.Join(",", actors)}] are not the scenario's [{string.Join(",", scenario.ActorId)}] in its order");
        int maxNode = nodes.Number.Max();
        if (Int(root, "max_node", name) != maxNode)
            throw Bad(name, $"max_node is not {maxNode}");

        var lk = new LkTables(actors.Length, maxNode);
        JsonElement lkRoot = Prop(root, "lk", name), delta = Prop(root, "delta", name);
        for (int a = 0; a < actors.Length; a++)
        {
            JsonElement byGen = Prop(lkRoot, actors[a], name);
            for (int g = 1; g <= Generations; g++)
            {
                JsonElement e = Prop(byGen, g.ToString(System.Globalization.CultureInfo.InvariantCulture), name);
                long pc = Int(e, "p_cens", name);
                int half = checked((int)Int(e, "half", name));
                if (pc < 0 || pc > Fx6.Scale || half < 0)
                    throw Bad(name, $"lk {actors[a]}:{g}: p_cens {pc} or half {half} out of range");
                int k = a * Generations + g - 1;
                lk.PCens[k] = Fx6.FromRaw(pc);
                lk.Mean[k] = checked((int)Int(e, "mean", name));
                lk.HalfWidth[k] = half;
            }
            JsonElement byNode = Prop(delta, actors[a], name);
            for (int r = 0; r < nodes.Count; r++)
            {
                if (nodes.Gen[r] < 1 || nodes.Gen[r] > Generations)
                    continue;
                JsonElement d = Prop(byNode, nodes.Number[r].ToString(System.Globalization.CultureInfo.InvariantCulture), name);
                lk.Delta[lk.DeltaIndex(a, nodes.Number[r])] = d.ValueKind == JsonValueKind.Null ? LkTables.Never : checked((int)d.GetInt64());
            }
            foreach (int app in AppNodes(nodes))
            {
                if (lk.Delta[lk.DeltaIndex(a, app)] != 0)
                    throw Bad(name, $"delta {actors[a]}:{app}: an APP node has δ = 0 against itself");
            }
        }

        JsonElement lc = Prop(root, "lc", name);
        var curves = ImmutableArray.CreateBuilder<CapabilityCurve>(Tiers.Length);
        ImmutableArray<int> tau = [.. Enumerable.Range(0, 2 * horizon)];
        foreach (int tier in Tiers)
        {
            string t = tier.ToString(System.Globalization.CultureInfo.InvariantCulture);
            long[] y = [.. Prop(Prop(lc, "Y", name), t, name).EnumerateArray().Select(e => e.GetInt64())];
            long[] u = [.. Prop(Prop(lc, "U", name), t, name).EnumerateArray().Select(e => e.GetInt64())];
            if (y.Length != tau.Length || u.Length != tau.Length)
                throw Bad(name, $"lc tier {tier}: {y.Length}/{u.Length} knots, not {tau.Length}");
            try
            {
                curves.Add(new CapabilityCurve(tau, [.. y.Select(Fx6.FromRaw)], [.. u.Select(Fx6.FromRaw)]));
            }
            catch (ArgumentException e)
            {
                throw Bad(name, $"lc tier {tier}: {e.Message}");
            }
        }

        JsonElement lp = Prop(root, "lp", name);
        var tDesign = new int[Generations];
        var rho = new Fx6[Generations];
        for (int g = 1; g <= Generations; g++)
        {
            string gs = g.ToString(System.Globalization.CultureInfo.InvariantCulture);
            tDesign[g - 1] = checked((int)Int(Prop(lp, "T_design", name), gs, name));
            rho[g - 1] = Fx6.FromRaw(Int(Prop(lp, "rho", name), gs, name));
            if (tDesign[g - 1] < 0 || rho[g - 1] < Fx6.Zero)
                throw Bad(name, $"lp G{g}: T_design {tDesign[g - 1]} or rho {rho[g - 1]} negative");
        }
        return new ControlTables(Convert.ToHexStringLower(SHA256.HashData(bytes)), lk, curves.MoveToImmutable(), [.. tDesign], [.. rho]);
    }

    /// <summary>APP node of each generation 1…4 (one per generation in the slice graph).</summary>
    public static IEnumerable<int> AppNodes(NodeTable nodes)
    {
        for (int r = 0; r < nodes.Count; r++)
        {
            if (nodes.Type[r] == NodeType.APP && nodes.Gen[r] >= 1 && nodes.Gen[r] <= Generations)
                yield return nodes.Number[r];
        }
    }

    private static JsonElement Prop(JsonElement e, string key, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out JsonElement v) ? v : throw Bad(name, $"missing '{key}'");

    private static long Int(JsonElement e, string key, string name)
    {
        JsonElement v = Prop(e, key, name);
        return v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long x) ? x : throw Bad(name, $"'{key}' is not an integer");
    }

    private static string Str(JsonElement e, string key, string name)
    {
        JsonElement v = Prop(e, key, name);
        return v.ValueKind == JsonValueKind.String ? v.GetString()! : throw Bad(name, $"'{key}' is not a string");
    }

    private static ParamException Bad(string name, string why) => new($"CONTROL_TABLES {name}: {why} (slice-csv-format.md §3.1).");
}
