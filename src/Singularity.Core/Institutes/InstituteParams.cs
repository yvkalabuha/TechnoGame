using System.Collections.Immutable;
using Singularity.Kernel;

namespace Singularity.Core.Institutes;

/// <summary>
/// Constants of the research priorities (research-institutes-priorities rev 4, [Предп.] until the Lab calibration). Dimensionless shares, no calendar:
/// they are not yearly norms.
/// </summary>
public sealed record InstituteParams
{
    /// <summary>Most the money of a priority group grows over its usual share (#487).</summary>
    public Fx6 Kappa { get; init; } = Fx6.FromRaw(1_500_000);

    /// <summary>Share of the money the other nodes keep at least while the rush is on (rev 4 §5).</summary>
    public Fx6 Rho { get; init; } = Fx6.FromRaw(250_000);

    /// <summary>Speed bonus of the goal node per institute level (#493): 0.05, positive only.</summary>
    public Fx6 LevelBonus { get; init; } = Fx6.FromRaw(50_000);

    /// <summary>Experience thresholds of levels 1..4 as percent of the nodes of the area (q = 0.08; 0.20; 0.35; 0.50).</summary>
    public ImmutableArray<int> ThresholdPercent { get; init; } = [8, 20, 35, 50];

    /// <summary>A slot taken by the player may be given another node at most once in this many months (rev 4 §4 item 3).</summary>
    public int ReassignMonths { get; init; } = 12;

    public void Validate()
    {
        if (Kappa < Fx6.One)
            throw new ArgumentOutOfRangeException(nameof(Kappa), Kappa, "kappa must be at least 1 (a priority never slows a node).");
        if (Rho < Fx6.Zero || Rho >= Fx6.One)
            throw new ArgumentOutOfRangeException(nameof(Rho), Rho, "rho lies in [0, 1).");
        if (LevelBonus < Fx6.Zero)
            throw new ArgumentOutOfRangeException(nameof(LevelBonus), LevelBonus, "the institute bonus is never negative (#493).");
        if (ThresholdPercent.IsDefault || ThresholdPercent.Length != 4)
            throw new ArgumentException("Four thresholds (levels 1..4) are needed.", nameof(ThresholdPercent));
    }
}
