using Singularity.Core.Products;

namespace Singularity.Session.Tests;

/// <summary>
/// How tests name a design: the handle a client would read from the view (ADR-0005 p. 3), never a constant generation chosen to make a check pass.
/// </summary>
internal static class DesignHandles
{
    /// <summary>The handle of the project in <paramref name="slot"/> as the latest published view shows it.</summary>
    public static DesignHandle HandleOf(this SliceSession session, int actor, int slot) => session.View.Actors[actor].Designs[slot].Handle;
}
