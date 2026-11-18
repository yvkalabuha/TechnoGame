using Singularity.Lab.Models.ToyFunnel;
using Singularity.Lab.Models.Slice;
using Singularity.Lab.Models.Test;

namespace Singularity.Lab;

/// <summary>
/// A registered model: either a table model (<see cref="ILabModelFactory"/>, the Lab runs its systems) or a Session model
/// (<see cref="ISessionModelFactory"/>, the session runs the world). Exactly one of the two is set.
/// </summary>
public sealed record RegisteredModel(string Id, bool IsTestModel, string Description, ILabModelFactory? Table, ISessionModelFactory? Session)
{
    public static RegisteredModel Of(ILabModelFactory f) => new(f.Id, f.IsTestModel, f.Description, f, null);

    public static RegisteredModel Of(ISessionModelFactory f) => new(f.Id, false, f.Description, null, f);
}

/// <summary>Models known to the Lab, in a fixed order: Core models, the Session models of the jet slice, then the Lab TEST MODEL.</summary>
public static class ModelRegistry
{
    private static readonly RegisteredModel[] Models =
    [
        RegisteredModel.Of(new ToyFunnelModelFactory()),
        RegisteredModel.Of(new JetSliceModelFactory(calibration: false)),
        RegisteredModel.Of(new JetSliceModelFactory(calibration: true)),
        RegisteredModel.Of(new RandomWalkTestModelFactory()),
    ];

    public static IReadOnlyList<RegisteredModel> All => Models;

    public static RegisteredModel Find(string id)
    {
        foreach (RegisteredModel f in Models)
        {
            if (string.Equals(f.Id, id, StringComparison.Ordinal))
                return f;
        }
        throw new ManifestException($"Unknown model '{id}'. Known: {string.Join(", ", Models.Select(f => f.Id))}.");
    }
}
