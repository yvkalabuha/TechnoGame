using Singularity.Core.Data.Yaml;

namespace Singularity.Core.Data;

/// <summary>
/// Loud data-load failure (ADR-0010): broken reference, ID conflict, schema violation, implicit
/// type. The message starts with "file:line:" when the place is known.
/// </summary>
public sealed class DataLoadException : Exception
{
    public DataLoadException(string message) : base(message) { }

    public DataLoadException(string message, Exception inner) : base(message, inner) { }

    internal static DataLoadException At(YNode node, string message) => new($"{node.Where}: {message}");
}
