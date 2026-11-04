using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary><c>mult_P</c> (spec §3.5 A37, DEBT-0019): the doctrine factor of the weight of the process-development distribution, ×2 for the profile's processes.</summary>
public class MultPTests
{
    private static SliceSession Run(int cell, bool t6dOff = false) =>
        SliceTestData.Create(new SliceRunConfig { Seed = 7, ProfileCell = cell, T6dOff = t6dOff });

    // Cell 9u + 3g + s: USA, GBR, SUN profiles. USA is P1 in cell 0, P2 in cell 9, P3 in cell 18.
    private const int UsaP1 = 0, UsaP2 = 9, UsaP3 = 18;

    private static int Newest(SliceSession s, int actor)
    {
        for (int d = s.World.ProductsLayout.Designs - 1; d >= 0; d--)
        {
            if (s.World.Products.Stage(actor, d) == DesignStage.Series)
                return d;
        }
        return -1;
    }

    [Fact]
    public void Multiplier_IsTwoForTheProcessesOfTheProfilesProject_AndOneForP3AndForOtherProcesses()
    {
        foreach ((int cell, bool doubled) in new[] { (UsaP1, true), (UsaP2, true), (UsaP3, false) })
        {
            SliceSession s = Run(cell);
            SliceWorld w = s.World;
            int d = Newest(s, 0);
            Assert.True(d >= 0, "USA starts with a line (E1)");
            int[] set = w.Products.PrcSet(0, d);
            Assert.NotEmpty(set);
            foreach (int r in w.IndustryLayout.Order)
            {
                bool inSet = Array.IndexOf(set, w.IndustryLayout.Number[r]) >= 0;
                Assert.Equal(doubled && inSet ? Fx6.FromInt(2) : Fx6.One, w.IndustryInputs.EffortMultiplier(0, r, 0));
            }
        }
    }

    [Fact]
    public void T6dOff_HasNoMultiplier()
    {
        SliceSession s = Run(UsaP1, t6dOff: true);
        foreach (int r in s.World.IndustryLayout.Order)
            Assert.Equal(Fx6.One, s.World.IndustryInputs.EffortMultiplier(0, r, 0));
    }
}
