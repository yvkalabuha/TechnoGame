namespace Singularity.Core;

/// <summary>
/// Marks a test stand of one layer (a Lab smoke stand with fake inputs) that may build a system of Core outside
/// <c>Singularity.Session</c> (ADR-0014 п. 1; SLICE_COMPOSITION Р-4). Such a stand is not evidence for the assembled slice: its
/// reports say so, and it is not extended. The architecture test admits a system constructor outside Session only inside a marked type.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SingleLayerStandAttribute(string reason) : Attribute
{
    public string Reason { get; } = reason;
}
