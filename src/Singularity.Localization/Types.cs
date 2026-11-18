using Linguini.Shared.Types.Bundle;

namespace Singularity.Localization;

/// <summary>
/// Reference to a data entity that has a Fluent term (actor, country, region). Holds the term id without
/// the leading '-' (that is how Linguini looks terms up). Resolved by NAME and GENDER.
/// </summary>
public sealed class DataRef(string termId) : IFluentType
{
    public string TermId { get; } = termId;
    public string AsString() => TermId;
    public bool IsError() => false;
    public bool Matches(IFluentType other, IScope scope) => other is DataRef d && d.TermId == TermId;
    public IFluentType Copy() => this;
}

/// <summary>In-game date (ADR-0006: year, month, day only).</summary>
public sealed class GameDate(int year, int month, int day) : IFluentType
{
    public int Year { get; } = year;
    public int Month { get; } = month;
    public int Day { get; } = day;
    public string AsString() => $"{Year:D4}-{Month:D2}-{Day:D2}";
    public bool IsError() => false;
    public bool Matches(IFluentType other, IScope scope) => false;
    public IFluentType Copy() => this;
}

/// <summary>
/// A FluentNumber for display. Its options (from FluentNumber.FromString of the canonical text) keep the visible
/// fraction digits, so CLDR plural matching sees the operands of the formatted number ("1.0" is `other`).
/// AsString returns the deterministic locale text. Linguini's FormatterFunc is not used: with it set,
/// function results are stringified and lose plural matching (spike S1, DECISIONS_LOG #304).
/// </summary>
public sealed record FormattedNumber : FluentNumber, IFluentType
{
    public string Text { get; }

    public FormattedNumber(FluentNumber visible, string text) : base(visible)
    {
        Text = text;
    }

    string IFluentType.AsString() => Text;

    // Arguments are copied on resolution; keep the subclass (and its text) across the copy.
    IFluentType IFluentType.Copy() => this;
}
