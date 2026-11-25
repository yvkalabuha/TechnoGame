namespace Singularity.Localization;

/// <summary>Visible fields of an entity (ADR-0018 §1). The facade accepts only these, so no field escapes check L1.</summary>
public enum Field { Name, Short, Desc, Anchor, Title, Body, Tip, Adj, History }

public static class FieldNames
{
    public static string Attribute(Field f) => f switch
    {
        Field.Name => "name",
        Field.Short => "short",
        Field.Desc => "desc",
        Field.Anchor => "anchor",
        Field.Title => "title",
        Field.Body => "body",
        Field.Tip => "tip",
        Field.Adj => "adj",
        Field.History => "history",
        _ => throw new ArgumentOutOfRangeException(nameof(f), f, null),
    };
}
