using Singularity.Core.Data;
using Singularity.Kernel;

namespace Singularity.Core.Industry;

/// <summary>
/// Start stocks of the capability layer by storage row: <c>culture_H</c>, <c>tooling_M</c>
/// <c>[(site·C + c)·3 + d]</c>, <c>proc_X</c> <c>[site·P + p]</c>. <c>proc_v</c>, <c>proc_yield</c> start at 0
/// (no output in month −1, reference fixture condition).
/// </summary>
public sealed class IndustryStart
{
    public IndustryStart(IndustryLayout layout)
    {
        Layout = layout;
        H = new Fx6[layout.SiteCount * layout.Clusters.Length * Dims3.Count];
        M = new Fx6[H.Length];
        X = new Fx6[layout.SiteCount * layout.ProcessCount];
        MasteredRun = new int[X.Length];
    }

    public IndustryLayout Layout { get; }
    public Fx6[] H { get; }
    public Fx6[] M { get; }
    public Fx6[] X { get; }

    /// <summary><c>mastered_run[site·P + p]</c> at t0: <c>min(6, −since_month)</c> for the mastered processes of the scenario, else 0 (contract r6 §9.1).</summary>
    public int[] MasteredRun { get; }

    public int Cell(int site, int cluster, int dim) => (site * Layout.Clusters.Length + cluster) * Dims3.Count + dim;

    /// <summary>
    /// Start from the scenario (spec §1.1): <c>culture_H</c> and <c>tooling_M</c> per (cluster, dimension) (R-M per cell,
    /// §3.5 A20); <c>proc_X = proc_x_half_lives·X½(tier)</c> for the
    /// mastered processes (R-MS), 0 otherwise. The layout must come from <see cref="IndustryLayout.FromData"/>.
    /// </summary>
    public static IndustryStart FromScenario(ScenarioData scenario, NodeTable nodes, IndustryLayout layout, IndustryParams p)
    {
        var start = new IndustryStart(layout);
        for (int s = 0; s < layout.SiteCount; s++)
        {
            int a = layout.SiteOwner[s];
            for (int c = 0; c < layout.Clusters.Length; c++)
            {
                for (int d = 0; d < Dims3.Count; d++)
                {
                    start.H[start.Cell(s, c, d)] = scenario.CultureOf(a, layout.Clusters[c], (Dimension)d);
                    start.M[start.Cell(s, c, d)] = scenario.ToolingOf(a, layout.Clusters[c], (Dimension)d);
                }
            }
            foreach (int nodeRow in scenario.MasteredOf(a))
            {
                int r = layout.RowOf(nodes.Number[nodeRow]);
                if (r < 0)
                    throw new ArgumentException($"Mastered node {nodes.Id[nodeRow]} is not a process of the layout.", nameof(layout));
                start.X[s * layout.ProcessCount + r] = Fx6.FromInt(scenario.ProcXHalfLives) * p.XHalf(layout.Tier[r]);
                start.MasteredRun[s * layout.ProcessCount + r] = Math.Min(Math.Max(-scenario.MasteredSinceMonth, 0), IndustryParams.MasteredMonths);
            }
        }
        return start;
    }
}
