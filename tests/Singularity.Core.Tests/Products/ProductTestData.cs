using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Products;
using Singularity.Core.Tests.Data;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Products;

/// <summary>A scripted yearly order: the generation only triggers it, the variant picks the preset (see the harness).</summary>
internal readonly record struct DesignOrder(int Gen, DesignVariant Variant);

/// <summary>Core mod data and the product layout of jet_fighter shared by the product tests.</summary>
internal static class ProductTestData
{
    public const int Categories = 2;
    public const int Bureaus = 2;
    public const int Years = 33;

    public static readonly GameData CoreData = ModLoader.Load(TestMods.CoreDir);
    public static readonly ScenarioData Scenario = CoreData.Scenario("jet_slice_1945");
    public static readonly ProductsLayout Probe = MakeLayout(out _);

    public static ProductsLayout MakeLayout(out IndustryLayout industry) => MakeLayout(out industry, Years, Scenario.MaxDesigns);

    public static ProductsLayout MakeLayout(out IndustryLayout industry, int years, int maxDesigns)
    {
        // Requesters of the capability layout must cover the product id space (lines, bureaus, fix teams).
        industry = IndustryLayout.FromData(CoreData.Nodes, Scenario, Categories, 64);
        return ProductsLayout.FromData(CoreData.Nodes, CoreData.ProductClasses, "jet_fighter", industry, Bureaus, Scenario.StartYear, years, maxDesigns);
    }

    /// <summary>Layout-global alternative index by slot and alternative id.</summary>
    public static int Alt(ProductsLayout l, string altId) => l.AltId.IndexOf(altId);

    /// <summary>A <c>compose_design</c> command from alternative ids in slot order.</summary>
    public static DesignCommand Compose(ProductsLayout l, int targetGen, params string[] altIds) =>
        new(DesignCommandKind.Compose, targetGen, [.. altIds.Select(id => Alt(l, id))]);

    public static string[] AltIds(ProductsLayout l, ReadOnlySpan<int> alts)
    {
        var ids = new string[alts.Length];
        for (int i = 0; i < alts.Length; i++)
            ids[i] = l.AltId[alts[i]];
        return ids;
    }
    /// <summary>
    /// Stand-in AI of the linked runs: one project per newest known APP (even actors «superior», odd «producible», falling back to
    /// «superior» when they coincide, F-PC3), series at the gate, a line closes when a newer-generation design is in series.
    /// </summary>
    public static IReadOnlyList<DesignCommand> StandInCommands(ProductsLayout l, ProductsSystem products, int actorRow, Func<int, bool> knows,
        Func<int, Fx6> yieldOf)
    {
        int g = 0;
        for (int i = 1; i <= l.Generations; i++)
        {
            if (knows(l.GenApp[i - 1]))
                g = i;
        }
        if (g == 0)
            return [];
        for (int d = 0; d < l.Designs; d++)
        {
            if (products.Stage(actorRow, d) != DesignStage.None && products.PlatformGen(actorRow, d) == g)
                return [];
        }
        var alts = new int[l.SlotCount];
        bool ok = actorRow % 2 == 0 && DesignRules.Superior(l, g, knows, alts)
            || actorRow % 2 != 0 && DesignRules.Producible(l, g, knows, yieldOf, new ProductsParams().YProd, null, alts)
            || DesignRules.Superior(l, g, knows, alts);
        return ok ? [new DesignCommand(DesignCommandKind.Compose, g, [.. alts])] : [];
    }

    public static bool StandInLaunch(ProductsSystem products, int actorRow, int design, long month) =>
        products.Stage(actorRow, design) == DesignStage.Prototype && products.GateOpen(actorRow, design, month) || (month == 0 && design == 0);

    public static bool StandInClose(ProductsLayout l, ProductsSystem products, int actorRow, int design)
    {
        for (int d = design + 1; d < l.Designs; d++)
        {
            if (products.Stage(actorRow, d) == DesignStage.Series && products.Gen(actorRow, d) > products.Gen(actorRow, design))
                return true;
        }
        return false;
    }
}
