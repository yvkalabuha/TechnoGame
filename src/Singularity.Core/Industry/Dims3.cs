using Singularity.Core.Data;
using Singularity.Kernel;

namespace Singularity.Core.Industry;

/// <summary>
/// One Fx6 value per capability dimension in the canonical order <c>precision, purity, quality_control</c>
/// (contract v2.3 §4.0 p. 5). Index = <see cref="Dimension"/>.
/// </summary>
public readonly record struct Dims3(Fx6 Precision, Fx6 Purity, Fx6 QualityControl)
{
    public const int Count = 3;

    public Fx6 this[int dim] => dim switch
    {
        0 => Precision,
        1 => Purity,
        2 => QualityControl,
        _ => throw new ArgumentOutOfRangeException(nameof(dim), dim, "Dimension index is 0…2."),
    };

    public Fx6 this[Dimension dim] => this[(int)dim];
}
