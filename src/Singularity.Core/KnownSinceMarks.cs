namespace Singularity.Core;

/// <summary>Marks of <c>t_known</c> shared by the layers (slice spec §6.1 v0.24 r2): «unknown» is exactly −1, never «any negative value».</summary>
public static class KnownSinceMarks
{
    /// <summary>The node is not known to the actor.</summary>
    public const long Unknown = -1;

    /// <summary><c>startKnownSince</c> of the spec: <c>−H</c>, H = 384 months of the horizon; the value of a node known at the start in L_C and L_P.</summary>
    public const long StartKnown = -384;
}
