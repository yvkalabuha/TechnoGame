using System.Text.Json;

namespace Singularity.Lab;

/// <summary>
/// Builds Core <see cref="ParamValue"/>s from manifest JSON (Core does not depend on the manifest
/// format): Number → canonical decimal text (<see cref="CanonicalJson.CanonicalNumber"/>, exact),
/// String → raw string, Bool → "true"/"false", Array/Object → canonical JSON.
/// </summary>
public static class ParamValues
{
    public static ParamValue FromJson(JsonElement e, string path) => e.ValueKind switch
    {
        JsonValueKind.Number => new(ParamKind.Number, CanonicalJson.CanonicalNumber(e.GetRawText(), path)),
        JsonValueKind.String => new(ParamKind.String, e.GetString()!),
        JsonValueKind.True => new(ParamKind.Bool, "true"),
        JsonValueKind.False => new(ParamKind.Bool, "false"),
        JsonValueKind.Array or JsonValueKind.Object => new(ParamKind.Json, CanonicalJson.Canonicalize(e)),
        _ => throw new ManifestException($"{path}: null is not a valid parameter value."),
    };
}
