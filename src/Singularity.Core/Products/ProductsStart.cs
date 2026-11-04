using Singularity.Core.Data;
using Singularity.Kernel;

namespace Singularity.Core.Products;

/// <summary>
/// Start state of the product layer by actor row: design slots (stage, alternatives), cohort counts, bureau experience.
/// Flat arrays (D = <c>max_designs</c>): <c>Stage[a·D + d]</c>, <c>PlatformGen[a·D + d]</c>, <c>Alts[(a·D + d)·S + s]</c>,
/// <c>Cohort[(a·D + d)·Y + y]</c>, <c>BureauExp[a·B + b]</c>. Everything else (defects, hours, <c>line_E</c>, work) starts at 0.
/// </summary>
public sealed class ProductsStart
{
    public ProductsStart(ProductsLayout layout, int actors)
    {
        Layout = layout;
        Actors = actors;
        Stage = new DesignStage[actors * layout.Designs];
        PlatformGen = new int[Stage.Length];
        Alts = new int[Stage.Length * layout.SlotCount];
        Array.Fill(Alts, -1);
        Cohort = new Fx6[Stage.Length * layout.Years];
        BureauExp = new Fx6[actors * layout.Bureaus];
        LineE = new Fx6[Stage.Length];
    }

    public ProductsLayout Layout { get; }
    public int Actors { get; }
    public DesignStage[] Stage { get; }
    public int[] PlatformGen { get; }
    public int[] Alts { get; }
    public Fx6[] Cohort { get; }
    public Fx6[] BureauExp { get; }

    /// <summary><c>line_E(t0)</c> by design slot: the units the start line has built (E1: the fleet it produced), 0 for a design without a line.</summary>
    public Fx6[] LineE { get; }

    /// <summary>
    /// Start fleets of the scenario (spec §1.1, R-FL; §3.5 A32 v0.15, E1): each fleet row of this class becomes the next free design slot
    /// (scenario order) with the "superior" alternatives the actor knows at t0 among nodes of generations ≤ gen, <c>g_p</c> = gen, and its
    /// count goes into the cohort of <c>cohort_year</c>. The project with the largest <c>g_p</c> of the actor (ties: the lower slot) holds
    /// the actor's one line: stage С, <c>line_E</c> = its fleet (the line built it); any other start project is in stage В. [Предл.] Start
    /// designs are mature: no defects (<c>D0</c> = 0), bureau experience 0, the series gate counts as open. A "producible" start fleet needs
    /// t0 yields and is rejected.
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
                // already knows turbojet_g2 (GBR) [Предл.]. Start data only: for new projects A32 is cancelled (#81),
                // cost follows the actual components (R-PS2m).
                Func<int, bool> upTo = layout.KnownUpTo(gen);
                if (!DesignRules.Superior(layout, gen, n => Knows(n) && upTo(n), alts))
                    throw new ArgumentException($"Start fleet of {scenario.ActorId[a]}: the actor cannot build a G{gen} design at t0.", nameof(scenario));
                int y = scenario.FleetCohortYear[f] - layout.StartYear;
                if (y < 0 || y >= layout.Years)
                    throw new ArgumentException($"Start fleet of {scenario.ActorId[a]}: cohort year outside the layout years.", nameof(scenario));
                // Rows of one generation share the design (their cohorts differ by year).
                int slot = 0;
                while (slot < layout.Designs && start.Stage[a * layout.Designs + slot] != DesignStage.None
                    && start.PlatformGen[a * layout.Designs + slot] != gen)
                    slot++;
                if (slot == layout.Designs)
                    throw new ArgumentException($"Start fleets of {scenario.ActorId[a]} exceed max_designs ({layout.Designs}).", nameof(scenario));
                int d = a * layout.Designs + slot;
                start.Stage[d] = DesignStage.InService;
                start.PlatformGen[d] = gen;
                alts.CopyTo(start.Alts.AsSpan(d * layout.SlotCount, layout.SlotCount));
                start.Cohort[d * layout.Years + y] += Fx6.FromInt(scenario.FleetCount[f]);
            }
            // E1 (#137): one line per actor, held by the newest start project.
            int line = -1;
            for (int slot = 0; slot < layout.Designs; slot++)
            {
                int d = a * layout.Designs + slot;
                if (start.Stage[d] != DesignStage.None && (line < 0 || start.PlatformGen[d] > start.PlatformGen[line]))
                    line = d;
            }
            if (line >= 0)
            {
                start.Stage[line] = DesignStage.Series;
                for (int y = 0; y < layout.Years; y++)
                    start.LineE[line] += start.Cohort[line * layout.Years + y];
            }
        }
        return start;
    }
}
