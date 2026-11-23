using Singularity.Core.Policy;

namespace Singularity.Session.Tests;

/// <summary>TASK-0133: the core mod with nodes 62-81 builds a world with six generations and the two-word licence mask.</summary>
public class G5G6DataTests
{
    [Fact]
    public void CoreMod_BuildsAWorldWith81Nodes_SixGenerations_AndTheWideLicenceMask()
    {
        SliceWorld w = SliceTestData.Create().World;
        Assert.Equal(81, w.Graph.Count);
        Assert.Equal(6, w.ProductsLayout.Generations);
        // PRC 73 (single-crystal casting) and 80 (CMC) are licensable and above 63: the second mask word is registered.
        Assert.Equal(LicenceMask.MaxNumber, w.Policy.LicenceCapacity);
        Assert.True(w.Policy.LicenceCapacity > LicenceMask.NarrowMaxNumber);
    }

    [Fact]
    public void LegacyData_StaysOnTheNarrowMask()
    {
        Assert.Equal(LicenceMask.NarrowMaxNumber, SliceTestData.CreateLegacy().World.Policy.LicenceCapacity);
    }
}
