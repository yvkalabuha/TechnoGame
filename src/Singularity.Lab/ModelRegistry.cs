using Singularity.Lab.Models.Test;

namespace Singularity.Lab;

/// <summary>Models known to the Lab, in a fixed order. TASK-0004 adds the toy model here.</summary>
public static class ModelRegistry
{
    private static readonly ILabModelFactory[] Factories =
    [
        new RandomWalkTestModelFactory(),
    ];

    public static IReadOnlyList<ILabModelFactory> All => Factories;

    public static ILabModelFactory Find(string id)
    {
        foreach (ILabModelFactory f in Factories)
        {
            if (string.Equals(f.Id, id, StringComparison.Ordinal))
                return f;
        }
        throw new ManifestException($"Unknown model '{id}'. Known: {string.Join(", ", Factories.Select(f => f.Id))}.");
    }
}
