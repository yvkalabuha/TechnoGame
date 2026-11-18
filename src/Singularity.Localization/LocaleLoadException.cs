namespace Singularity.Localization;

/// <summary>A locale file or catalog is invalid (syntax, id grammar, conflicts, unknown attribute, bad language tag).</summary>
public sealed class LocaleLoadException(string message) : Exception(message);
