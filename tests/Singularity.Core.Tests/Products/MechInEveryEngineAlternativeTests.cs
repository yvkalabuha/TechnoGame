using Singularity.Core.Data;
using Singularity.Core.Industry;

namespace Singularity.Core.Tests.Products;

/// <summary>
/// Spec §3.5 A30 [Предп.; «проверяет T8»]: MECH-процессы входят в <c>prc_set</c> всех альтернатив горячей части и компрессора, поэтому любая серия
/// двигателя даёт MECH <c>v &gt; 0</c> (v = труд на годные единицы по процессам prc_set, A37), и культура MECH не держится правилом A30, а растёт
/// от выпуска. The check is on the data: a design whose every hot-section and compressor alternative has a MECH process cannot run an engine
/// series without MECH labour (<c>labour_p &gt; 0</c> for the MECH process).
/// </summary>
public class MechInEveryEngineAlternativeTests
{
    [Fact]
    public void EveryHotSectionAndCompressorAlternative_HasAMechProcessWithLabour()
    {
        var l = ProductTestData.Probe;
        IndustryLayout il = IndustryLayout.FromData(ProductTestData.CoreData.Nodes, ProductTestData.Scenario, ProductTestData.Categories, 64);
        int mech = il.Clusters.IndexOf(Cluster.MECH);
        Assert.True(mech >= 0);
        int checkedAlts = 0;
        // Hot section and compressor are found by the alternative ids of the data («hot_*», «comp_*»), not by slot arithmetic.
        for (int a = 0; a < l.AltId.Length; a++)
        {
            if (!l.AltId[a].StartsWith("hot_", StringComparison.Ordinal) && !l.AltId[a].StartsWith("comp_", StringComparison.Ordinal))
                continue;
            bool hasMech = false;
            for (int k = l.AltPrcStart[a]; k < l.AltPrcStart[a + 1]; k++)
            {
                int p = l.ProcNumber.IndexOf(l.AltPrc[k]);
                if (l.ProcCluster[p] == mech)
                {
                    hasMech = true;
                    Assert.True(l.ProcLabour[p] > Singularity.Kernel.Fx6.Zero, $"{l.AltId[a]}: MECH process {l.AltPrc[k]} without labour gives v = 0");
                }
            }
            Assert.True(hasMech, $"{l.AltId[a]}: no MECH process in prc_set");
            checkedAlts++;
        }
        Assert.Equal(7, checkedAlts);   // 5 hot-section + 2 compressor alternatives of the core mod
    }
}
