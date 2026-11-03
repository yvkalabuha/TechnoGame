using System.Globalization;

namespace Singularity.Core.Data.Yaml;

/// <summary>How a scalar was written. Only plain scalars can be numbers or booleans.</summary>
public enum ScalarKind
{
    /// <summary>Quoted, or plain text that is neither a number nor true/false.</summary>
    String,
    /// <summary>Plain <c>true</c> or <c>false</c>.</summary>
    Bool,
    /// <summary>Plain decimal: <c>-?(0|[1-9][0-9]*)(.[0-9]+)?</c>.</summary>
    Number,
}

/// <summary>Node of the strict YAML subset (ADR-0010): mappings, sequences and untyped scalars.</summary>
public abstract class YNode(string file, long line)
{
    public string File { get; } = file;

    /// <summary>1-based line of the node start, for error messages.</summary>
    public long Line { get; } = line;

    public string Where => string.Create(CultureInfo.InvariantCulture, $"{File}:{Line}");
}

public sealed class YScalar(string file, long line, string value, ScalarKind kind) : YNode(file, line)
{
    public string Value { get; } = value;
    public ScalarKind Kind { get; } = kind;
}

public sealed class YSeq(string file, long line, IReadOnlyList<YNode> items) : YNode(file, line)
{
    public IReadOnlyList<YNode> Items { get; } = items;
}

/// <summary>Mapping with keys in document order; duplicate keys are rejected by the reader.</summary>
public sealed class YMap(string file, long line, IReadOnlyList<KeyValuePair<string, YNode>> entries) : YNode(file, line)
{
    public IReadOnlyList<KeyValuePair<string, YNode>> Entries { get; } = entries;

    // Linear search keeps document order explicit; data maps are small.
    public YNode? Find(string key)
    {
        for (int i = 0; i < Entries.Count; i++)
        {
            if (string.Equals(Entries[i].Key, key, StringComparison.Ordinal))
                return Entries[i].Value;
        }
        return null;
    }
}
