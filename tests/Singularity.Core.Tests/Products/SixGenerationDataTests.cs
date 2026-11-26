using Singularity.Core.Data;
using Singularity.Core.Knowledge;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Products;

/// <summary>
/// TASK-0131 + TASK-0133 (train 2): the core mod itself carries six generations (81 nodes, G5/G6 alternatives, airframe class 6,
/// the two-word licence mask) and the Core prices and lays them out.
/// </summary>
public class SixGenerationDataTests
{
    private static GameData Core => ProductTestData.CoreData;

    [Fact]
    public void CoreMod_HasEightyOneNodes_AirframeClass6_AndGenerations5And6()
    {
        GameData d = Core;
        Assert.Equal(156, d.Nodes.Count);
        ProductsLayout l = ProductTestData.Probe;
        Assert.Equal(6, l.Generations);
        Assert.Equal(6, l.AltAfClass[l.AltId.IndexOf("af_lo_composite")]);
        Assert.Equal(5, l.AltAfClass[l.AltId.IndexOf("af_composite")]);
        Assert.Equal(6, l.AltEngGen[l.AltId.IndexOf("eng_g6")]);
        Assert.Equal(6, l.AltEngGen[l.AltId.IndexOf("eng_g6_adaptive")]);
    }

    [Fact]
    public void LicensablePrcAbove63_NeedsTheWideMask()
    {
        NodeTable n = Core.Nodes;
        int wide = 0;
        for (int r = 0; r < n.Count; r++)
            wide += n.Type[r] == NodeType.PRC && n.Number[r] > 63 ? 1 : 0;
        Assert.Equal(13, wide);   // processes 73, 74, 75, 80 and the 9 of #575
    }

    [Fact]
    public void NewNodes_AnchorsNeverPrecedeTheirPrerequisites()
    {
        NodeTable n = Core.Nodes;
        for (int r = 0; r < n.Count; r++)
        {
            if (n.Number[r] < 62)
                continue;   // G1-G4 nodes carry "before" anchors (Brayton 1945 after gas_turbine 1937): the rule is for the new nodes
            for (int g = n.Requires.GroupStart[r]; g < n.Requires.GroupStart[r + 1]; g++)
            {
                foreach (int parent in n.Requires.GroupMembers(g))
                    Assert.True(n.AnchorYear[parent] <= n.AnchorYear[r], $"{n.Id[parent]} ({n.AnchorYear[parent]}) is anchored after {n.Id[r]} ({n.AnchorYear[r]})");
            }
        }
    }

    [Fact]
    public void NodeCost_OfGenerations5And6_IsDefined_G7IsStillLoud()
    {
        var p = new KnowledgeParams();
        Assert.True(p.CostN(NodeType.APP, 5) > p.CostN(NodeType.APP, 4));
        Assert.True(p.CostN(NodeType.APP, 6) > p.CostN(NodeType.APP, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => p.CostN(NodeType.APP, 7));
    }

    [Fact]
    public void KnowledgeGraph_OfCoreData_PricesTheNewNodes()
    {
        KnowledgeGraph g = KnowledgeGraph.FromData(Core.Nodes, Core.Scenario("jet_slice_1945").Disciplines, new KnowledgeParams());
        Assert.Equal(156, g.Count);
        Assert.Equal(new KnowledgeParams().CostN(NodeType.APP, 5), g.CostN[g.RowOf(76)]);
        Assert.Equal(new KnowledgeParams().CostN(NodeType.APP, 6), g.CostN[g.RowOf(77)]);
    }
}
