using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Products;
using Singularity.Core.Tests.Data;

namespace Singularity.Core.Tests.Products;

/// <summary>Core mod data and the product layout of jet_fighter shared by the product tests.</summary>
internal static class ProductTestData
{
    public const int Categories = 2;
    public const int Bureaus = 2;
    public const int Years = 33;

    public static readonly GameData CoreData = ModLoader.Load(TestMods.CoreDir);
    public static readonly ScenarioData Scenario = CoreData.Scenario("jet_slice_1945");
    public static readonly ProductsLayout Probe = MakeLayout(out _);

    public static ProductsLayout MakeLayout(out IndustryLayout industry) => MakeLayout(out industry, Years);

    public static ProductsLayout MakeLayout(out IndustryLayout industry, int years)
    {
        // Requesters of the capability layout must cover the product id space (lines, bureaus, fix teams).
        industry = IndustryLayout.FromData(CoreData.Nodes, Scenario, Categories, 64);
        return ProductsLayout.FromData(CoreData.Nodes, CoreData.ProductClasses, "jet_fighter", industry, Bureaus, Scenario.StartYear, years);
    }

    /// <summary>Layout-global alternative index by slot and alternative id.</summary>
    public static int Alt(ProductsLayout l, string altId) => l.AltId.IndexOf(altId);

    public static string[] AltIds(ProductsLayout l, ReadOnlySpan<int> alts)
    {
        var ids = new string[alts.Length];
        for (int i = 0; i < alts.Length; i++)
            ids[i] = l.AltId[alts[i]];
        return ids;
    }
}
