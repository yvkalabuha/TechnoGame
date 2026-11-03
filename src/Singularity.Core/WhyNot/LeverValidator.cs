using Singularity.Core.Data;
using Singularity.Core.Industry;

namespace Singularity.Core.WhyNot;

/// <summary>
/// Spec §5.6 (d): the levers of the links match the build. Every lever offered is enabled in the registry and is one the contract §6
/// table offers for that link; every enabled lever the table offers for the link is offered. Written from the table, not from the
/// analyzer, so it is an independent check.
/// </summary>
public static class LeverValidator
{
    /// <summary>Violations as text, empty when the links are consistent.</summary>
    public static IReadOnlyList<string> Check(IEnumerable<Link> links, LeverRegistry registry)
    {
        var problems = new List<string>();
        foreach (Link link in links)
        {
            foreach (Lever lever in Enum.GetValues<Lever>())
            {
                bool offered = link.Levers.Contains(lever);
                if (offered && !registry.IsEnabled(lever))
                    problems.Add($"{Describe(link)}: lever {lever} is not enabled in this build");
                else if (offered && !Applies(lever, link))
                    problems.Add($"{Describe(link)}: lever {lever} is not offered for such a link");
                else if (!offered && registry.IsEnabled(lever) && Applies(lever, link) && Always(lever, link))
                    problems.Add($"{Describe(link)}: lever {lever} is missing");
            }
        }
        return problems;
    }

    private static string Describe(Link l) => $"{l.Layer}/{l.Cause}/{l.SubjectId}";

    // The «когда предлагается» table of contract §6.
    private static bool Applies(Lever lever, Link l) => lever switch
    {
        Lever.ResearchPriority => l.Layer == WhyNotLayer.Knowledge || l.Cause == WhyNotCause.EngineGenLimit,
        Lever.TrainingSeries => l.Layer == WhyNotLayer.Capability && l.Cause == WhyNotCause.CultureH
            && l.Limiter is LimiterKind.Volume or LimiterKind.LabCeiling,
        Lever.InvestToolingM => l.Layer == WhyNotLayer.Capability && (l.Cause == WhyNotCause.ToolingM || l.Limiter == LimiterKind.Tooling),
        Lever.Licence => l.Layer == WhyNotLayer.Capability && (l.Cause == WhyNotCause.Ramp || l.Limiter == LimiterKind.Volume),
        Lever.QualityProgramme => l.Layer == WhyNotLayer.Capability && l.Dim == Dimension.QualityControl,
        Lever.SimplifiedVariant => l.Layer is WhyNotLayer.Capability or WhyNotLayer.Economy && l.VariantSlot >= 0,
        Lever.ReassignWorkforce => l.Layer == WhyNotLayer.Product && l.Cause == WhyNotCause.Engineers,
        Lever.ProcessDevelopment => l.Layer == WhyNotLayer.Capability && l.Limiter == LimiterKind.Effort,
        Lever.ForceSeries => l.Layer == WhyNotLayer.Product && l.Cause == WhyNotCause.SeriesGate,
        _ => false,
    };

    // The licence (for ramp as for VOLUME) is offered only if a licence is on offer, which the link does not record.
    private static bool Always(Lever lever, Link l) => lever != Lever.Licence;
}
