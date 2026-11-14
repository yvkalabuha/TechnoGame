namespace Singularity.Core;

/// <summary>
/// Marks an accessor of a step trace (SLICE_COMPOSITION §13.1, #189): a value computed by a monthly step that no compute or merge reads afterwards.
/// A trace is scratch outside the table: rewritten in every step, never hashed or saved. The architecture test forbids any call of a marked
/// member from <c>Singularity.Core</c> code outside its own class, so observing cannot change the dynamics.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class ObservationAttribute : Attribute;
