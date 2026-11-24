using System.Collections.Immutable;

namespace Singularity.Core.Policy;

/// <summary>
/// The player country AI as adviser (TASK-0153, #479; production-lines-and-licence-market.md rev 2, 1.2). For the actors in <see cref="Rows"/> the product AI
/// never composes, starts or closes anything: what it would start or close becomes a proposal the player accepts (an ordinary start or close command) or refuses.
/// Absent (null) = off: no columns, no change to any other run.
/// </summary>
/// <param name="Rows">Rows of the actors the player controls.</param>
/// <param name="RefuseYears">How long a refused proposal stays silent, in years (a yearly norm in data; the policy converts it to months).</param>
/// <param name="MaxLines">The most lines the player may run at once (levers doc 1.2: 2 in wave 1).</param>
public sealed record AdviserPlan(ImmutableArray<int> Rows, int RefuseYears = 1, int MaxLines = 2)
{
    public int RefuseMonths => RefuseYears * 12;
}

public enum ProposalKind
{
    /// <summary>Open the line of the design: accept = <c>StartSeries</c> with the proposal's rate.</summary>
    Open = 0,

    /// <summary>Close the line of the design: accept = <c>CloseLine</c>.</summary>
    Close = 1,
}

/// <summary>A proposal of the adviser for the month it was read at; <see cref="Handle"/> is the project it was made for, <see cref="Rate"/> is set for an open.</summary>
public readonly record struct AdviserProposal(ProposalKind Kind, int Design, Products.DesignHandle Handle, Kernel.Fx6 Rate, LineCloseReason Reason);
