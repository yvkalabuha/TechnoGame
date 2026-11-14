namespace Singularity.Core;

/// <summary>
/// Marks a working buffer of a system (ADR-0013 §4): a field rewritten in the compute or merge of every step that carries nothing from one
/// tick to the next. It is never hashed or saved and no other class reads it; the architecture test checks both. State that influences the
/// next tick belongs in a <c>Table</c>, not in a field.
/// </summary>
[AttributeUsage(AttributeTargets.Field, Inherited = false)]
public sealed class ScratchAttribute : Attribute;
