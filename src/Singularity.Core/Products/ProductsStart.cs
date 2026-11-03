using Singularity.Core.Data;
using Singularity.Kernel;

namespace Singularity.Core.Products;

/// <summary>
/// Start state of the product layer by actor row: design slots (stage, alternatives), cohort counts, bureau experience.
/// Flat arrays: <c>Stage[a·D + d]</c>, <c>Alts[(a·D + d)·S + s]</c>, <c>Cohort[(a·D + d)·Y + y]</c>, <c>BureauExp[a·B + b]</c>.
/// Everything else (defects, hours, <c>line_E</c>, work) starts at 0.
/// </summary>
public sealed class ProductsStart
{
    public ProductsStart(ProductsLayout layout, int actors)
    {
        Layout = layout;
        Actors = actors;
        Stage = new DesignStage[actors * layout.Designs];
        Alts = new int[Stage.Length * layout.SlotCount];
        Array.Fill(Alts, -1);
        Cohort = new Fx6[Stage.Length * layout.Years];
        BureauExp = new Fx6[actors * layout.Bureaus];
    }

    public ProductsLayout Layout { get; }
    public int Actors { get; }
    public DesignStage[] Stage { get; }
    public int[] Alts { get; }
    public Fx6[] Cohort { get; }
    public Fx6[] BureauExp { get; }

    /// <summary>
    /// Start fleets of the scenario (spec §1.1, R-FL): each fleet row of this class becomes the design slot (gen, variant)
    /// in stage В (line closed, units in service) with the «превосходящий» alternatives the actor knows at t0 among nodes of
    /// generations ≤ gen, and its
    /// count goes into the cohort of <c>cohort_year</c>. [Предл.] Start designs are mature: no defects (<c>D0</c> = 0),
    /// bureau experience 0. A «технологичный» start fleet needs t0 yields and is rejected.
    /// </summary>
    public static ProductsStart FromScenario(ScenarioData scenario, NodeTable nodes, ProductClassTable classes, ProductsLayout layout)
    {
        var start = new ProductsStart(layout, scenario.ActorCount);
        int classRow = classes.IndexOf(layout.ClassId);
        Span<int> alts = stackalloc int[layout.SlotCount];
        for (int a = 0; a < scenario.ActorCount; a++)
        {
            var known = new List<int>();
            foreach (int row in scenario.KnownOf(a))
                known.Add(nodes.Number[row]);
            known.Sort();
            bool Knows(int number) => known.BinarySearch(number) >= 0;
            for (int f = scenario.FleetStart[a]; f < scenario.FleetStart[a + 1]; f++)
            {
                if (scenario.FleetClass[f] != classRow)
                    continue;
                int gen = scenario.FleetGen[f];
                if (gen < 1 || gen > layout.Generations)
                    throw new ArgumentException($"Start fleet of {scenario.ActorId[a]}: no generation {gen}.", nameof(scenario));
                if (scenario.FleetVariant[f] != DesignVariant.Superior)
                    throw new ArgumentException($"Start fleet of {scenario.ActorId[a]}: only superior start designs are supported.", nameof(scenario));
                // The fleet's generation bounds its technology: a G1 fleet of 1945 has G1 engines even if the actor
                // already knows turbojet_g2 (GBR) [Предл.].
                Func<int, bool> upTo = layout.KnownUpTo(gen);
                if (!DesignRules.Superior(layout, gen, n => Knows(n) && upTo(n), alts))
                    throw new ArgumentException($"Start fleet of {scenario.ActorId[a]}: the actor cannot build a G{gen} design at t0.", nameof(scenario));
                int y = scenario.FleetCohortYear[f] - layout.StartYear;
                if (y < 0 || y >= layout.Years)
                    throw new ArgumentException($"Start fleet of {scenario.ActorId[a]}: cohort year outside the layout years.", nameof(scenario));
                int d = a * layout.Designs + ProductsLayout.DesignId(gen, DesignVariant.Superior);
                start.Stage[d] = DesignStage.InService;
                alts.CopyTo(start.Alts.AsSpan(d * layout.SlotCount, layout.SlotCount));
                start.Cohort[d * layout.Years + y] += Fx6.FromInt(scenario.FleetCount[f]);
            }
        }
        return start;
    }
}
