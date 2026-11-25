using Singularity.Kernel;

namespace Singularity.Core.Intelligence;

/// <summary>What an actor does against one target network (INTELLIGENCE.md §1): the value 0 is "no network work".</summary>
public enum IntelMode
{
    None = 0,
    Observe = 1,
    Steal = 2,
    FalseTrails = 3,
}

/// <summary>One of the (at most three) targets of an actor: the target's actor row and the mode against it.</summary>
public readonly record struct IntelTarget(int Target, IntelMode Mode);

/// <summary>The risk of exposure of a network in words (#367): the number is shown only after an event.</summary>
public enum RiskWord { Low = 0, Noticeable = 1, High = 2 }

/// <summary>The trace a network leaves in the target, in words (INTELLIGENCE.md §3): no number.</summary>
public enum TraceWord { None = 0, Some = 1, Many = 2 }

/// <summary>The security item in words (the three presets of INTELLIGENCE.md §6).</summary>
public enum SecurityWord { None = 0, Low = 1, Normal = 2, Strong = 3, Custom = 4 }

/// <summary>The strength of a network in words.</summary>
public enum NetworkWord { None = 0, Weak = 1, Reliable = 2 }

/// <summary>
/// Intelligence parameters of mods/core (<c>intelligence.yaml</c>). Rates are yearly norms (ADR-0027): the monthly step takes a twelfth,
/// the exposure hazard is converted from its yearly value by <see cref="IntelMath.MonthlyFromYearly"/>. Shares are shares of the actor's budget,
/// the stand-in for "share of GDP" until the core has a GDP column [Предп.]. Every value is a placeholder of the design (INTELLIGENCE.md §5.2, Lab I-1…I-6).
/// </summary>
public sealed record IntelParams
{
    /// <summary>Yearly pull of a network towards its target size when it grows.</summary>
    public required Fx6 GrowthPerYear { get; init; }

    /// <summary>Yearly pull of a network towards its target size when it shrinks (no money or no mode).</summary>
    public required Fx6 DecayPerYear { get; init; }

    /// <summary>Yearly pull of the maturity <c>A</c> towards the network size.</summary>
    public required Fx6 MaturityPerYear { get; init; }

    /// <summary>Share of the budget in the net (not counter-intelligence) part of the security item that gives a network of full size.</summary>
    public required Fx6 NetShareRef { get; init; }

    /// <summary>Part of the security item that goes to counter-intelligence.</summary>
    public required Fx6 CiShare { get; init; }

    /// <summary>Share of the budget in counter-intelligence that gives <c>ci_eff = 1</c>.</summary>
    public required Fx6 CiShareRef { get; init; }

    /// <summary><c>k</c>: growth of a foreign network is multiplied by <c>1 − k·ci_eff</c>.</summary>
    public required Fx6 CiGrowthCut { get; init; }

    /// <summary><c>g</c>: the hazard is multiplied by <c>1 + g·ci_eff</c>.</summary>
    public required Fx6 CiHazardGain { get; init; }

    /// <summary><c>a0</c>: yearly exposure hazard of a full network with activity 1.</summary>
    public required Fx6 HazardBasePerYear { get; init; }

    /// <summary>The yearly cap of the exposure hazard: the word "high" is at most this (gate п. 6, 25 % a year).</summary>
    public required Fx6 HazardCapPerYear { get; init; }

    public required Fx6 ActObserve { get; init; }
    public required Fx6 ActSteal { get; init; }
    public required Fx6 ActFalseTrails { get; init; }

    /// <summary>Yearly decay of the trace accumulator.</summary>
    public required Fx6 TraceDecayPerYear { get; init; }

    /// <summary>Trace above this is the word "some", above <see cref="TraceMany"/> "many".</summary>
    public required Fx6 TraceSome { get; init; }

    public required Fx6 TraceMany { get; init; }

    /// <summary>Hazard per year above this is the word "noticeable", above <see cref="RiskHigh"/> "high".</summary>
    public required Fx6 RiskNoticeable { get; init; }

    public required Fx6 RiskHigh { get; init; }

    /// <summary>What an exposed network keeps: <c>N ·= LossKeepN</c>, <c>A ·= LossKeepA</c>.</summary>
    public required Fx6 LossKeepN { get; init; }

    public required Fx6 LossKeepA { get; init; }

    /// <summary><c>σ</c>: the multiplier of the theft channel, <c>s_steal = min(s_max, σ·N·A)</c>.</summary>
    public required Fx6 Sigma { get; init; }

    /// <summary><c>s_max</c>: the own ceiling of the theft channel, a share of the node cost; outside the ±15 % corridor (O-051).</summary>
    public required Fx6 SMax { get; init; }

    /// <summary><c>S_max</c>: independent sources of a full network.</summary>
    public required int SourcesMax { get; init; }

    /// <summary>The layer of estimates is visible at <c>intel_conf</c> at least this.</summary>
    public required Fx6 ConfVisible { get; init; }

    /// <summary>The smallest relative half-width of an estimate range.</summary>
    public required Fx6 RangeMin { get; init; }

    /// <summary><c>κ</c>: false trails corrupt <c>φ = min(1, κ·N)</c> of the sources.</summary>
    public required Fx6 Kappa { get; init; }

    /// <summary><c>b</c>: the bias of a corrupted estimate (a share of the true value).</summary>
    public required Fx6 BiasMax { get; init; }

    /// <summary>The preset words of the security item as shares of the budget: low / normal / strong. The AI takes the normal one.</summary>
    public required Fx6 PresetLow { get; init; }

    public required Fx6 PresetNormal { get; init; }
    public required Fx6 PresetStrong { get; init; }

    /// <summary>Most targets an actor may have (#214).</summary>
    public const int MaxTargets = 3;
}
