using System.Globalization;
using Singularity.Core.Data;
using Singularity.Kernel;

namespace Singularity.Core.Industry;

/// <summary>A pair (actor, process) of <c>mastered</c> whose <c>yield(t0)</c> is below the S-2 threshold, with the dimension of the smallest <c>g</c>.</summary>
public sealed record MasteredYieldViolation(string Actor, int ProcessNumber, string ProcessId, Fx6 Yield, Dimension WeakestDimension, Fx6 WeakestG);

/// <summary>
/// Validator S-2 (spec §1.1, #155): for every actor and every process of its <c>mastered</c> list <c>yield(t0) ≥ 0,7</c>, computed by the same
/// function as the model (<see cref="IndustryMath.Yield"/>) from the start state (<c>culture_H</c>, <c>tooling_M</c>, <c>proc_X = proc_x_half_lives·X½</c>).
/// A violation is a data error: "mastered by the scenario" would contradict "not mastered by the model". The condition <c>proc_v ≥ v_min</c> is not checked (no output before t0).
/// </summary>
public static class StartYieldValidator
{
    /// <summary>The threshold of S-2.</summary>
    public static readonly Fx6 MinYield = Fx6.FromRaw(700_000);

    public static List<MasteredYieldViolation> Check(ScenarioData scenario, NodeTable nodes, IndustryLayout layout, IndustryParams p)
    {
        var result = new List<MasteredYieldViolation>();
        IndustryStart start = IndustryStart.FromScenario(scenario, nodes, layout, p);
        for (int s = 0; s < layout.SiteCount; s++)
        {
            int a = layout.SiteOwner[s];
            foreach (int nodeRow in scenario.MasteredOf(a))
            {
                int r = layout.RowOf(nodes.Number[nodeRow]);
                int c = layout.ClusterIndex[r];
                Dims3 h = Cells(start.H, start, s, c), m = Cells(start.M, start, s, c);
                ProcessYield y = IndustryMath.Yield(layout.Req[r], h, m, start.X[s * layout.ProcessCount + r], p.XHalf(layout.Tier[r]), Fx6.Zero, p.Lambda0);
                if (y.Yield >= MinYield)
                    continue;
                int weakest = 0;
                for (int d = 1; d < Dims3.Count; d++)
                {
                    if (y.IsActive(d) && (!y.IsActive(weakest) || y.G[d] < y.G[weakest]))
                        weakest = d;
                }
                result.Add(new MasteredYieldViolation(scenario.ActorId[a], layout.Number[r], nodes.Id[nodeRow], y.Yield, (Dimension)weakest, y.G[weakest]));
            }
        }
        return result;
    }

    /// <summary>Throws <see cref="DataLoadException"/> naming every violating actor, process, <c>yield(t0)</c> and weakest dimension.</summary>
    public static void Validate(ScenarioData scenario, NodeTable nodes, IndustryLayout layout, IndustryParams p)
    {
        List<MasteredYieldViolation> bad = Check(scenario, nodes, layout, p);
        if (bad.Count == 0)
            return;
        IEnumerable<string> lines = bad.Select(v => string.Create(CultureInfo.InvariantCulture,
            $"{v.Actor} {v.ProcessId} (#{v.ProcessNumber}): yield(t0) {v.Yield} < 0.7, weakest dimension {v.WeakestDimension} (g {v.WeakestG})"));
        throw new DataLoadException($"scenario '{scenario.Id}' violates S-2 (spec §1.1) for {bad.Count} mastered process(es): " + string.Join("; ", lines));
    }

    private static Dims3 Cells(Fx6[] v, IndustryStart start, int site, int cluster) =>
        new(v[start.Cell(site, cluster, 0)], v[start.Cell(site, cluster, 1)], v[start.Cell(site, cluster, 2)]);
}
