using Singularity.Kernel;

namespace Singularity.Core.Intelligence;

/// <summary>
/// What the intelligence layer reads from the other layers in the compute of month t: pure functions of the snapshot t−1 (the same contract as
/// <c>IKnowledgeInputs</c>): no hidden state, safe to call from several threads.
/// </summary>
public interface IIntelInputs
{
    /// <summary>The security item in force: a share of the budget.</summary>
    Fx6 SecurityShare(int actorRow);

    /// <summary>The plan of the player for the slot, true when the player has set one (it replaces the AI plan as a whole).</summary>
    bool PlayerPlan(int actorRow, int slot, out int target, out IntelMode mode);

    /// <summary>Number of knowledge nodes the actor knows.</summary>
    int KnownCount(int actorRow);

    /// <summary>Number of knowledge nodes and of disciplines (the directions of a network).</summary>
    int NodeCount { get; }

    int DisciplineCount { get; }

    /// <summary>The discipline of the node, −1 for a node without one.</summary>
    int DisciplineOf(int nodeRow);

    /// <summary>The actor knows the node.</summary>
    bool Knows(int actorRow, int nodeRow);
}
