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

    // ---- the two cases of the doctrine on a written state (as WhyNotTests): a newer project beside the series (P1), two lines W and N (P2)

    private static void SetInt(SliceSession s, string name, int row, int value)
    {
        IntColumn c = s.World.Table.GetInt(name);
        int[] all = c.Current.ToArray();
        all[row] = value;
        c.Load(all);
    }

    // A design of generation 2 in the free slot of USA: the superior composition of an actor that knows everything.
    private static int PutGen2(SliceSession s, DesignStage stage)
    {
        SliceWorld w = s.World;
        foreach (Column col in w.Table.Columns)
        {
            if (col.Name.StartsWith("know.known.", StringComparison.Ordinal))
                SetInt(s, col.Name, 0, 1);
        }
        int slot = Enumerable.Range(0, w.ProductsLayout.Designs).First(d => w.Products.Stage(0, d) == DesignStage.None);
        var alts = new int[w.ProductsLayout.SlotCount];
        Assert.True(DesignRules.Superior(w.ProductsLayout, 2, n => w.Knowledge.IsKnown(0, w.Graph.RowOf(n)), alts));
        string n = $"prd.d{slot}";
        SetInt(s, n + ".stage", 0, (int)stage);
        SetInt(s, n + ".gp", 0, 2);
        SetInt(s, n + ".parent", 0, -1);
        SetInt(s, n + ".bureau", 0, -1);
        for (int i = 0; i < alts.Length; i++)
            SetInt(s, n + ".alt." + i, 0, alts[i]);
        return slot;
    }

    private static (int[] OnlyNew, int[] OnlyOld) Difference(SliceSession s, int old, int fresh)
    {
        int[] a = s.World.Products.PrcSet(0, old), b = s.World.Products.PrcSet(0, fresh);
        return ([.. b.Except(a)], [.. a.Except(b)]);
    }

    private static Fx6 Mult(SliceSession s, int number) =>
        s.World.IndustryInputs.EffortMultiplier(0, s.World.IndustryLayout.RowOf(number), 0);

    [Fact]
    public void P1_WithAProjectNewerThanTheSeries_DoublesThePrcOfThatProject_NotOfTheSeries()
    {
        SliceSession s = Run(UsaP1);
        int series = Newest(s, 0);
        int project = PutGen2(s, DesignStage.Prototype);
        (int[] onlyNew, int[] onlyOld) = Difference(s, series, project);
        Assert.NotEmpty(onlyNew);
        Assert.NotEmpty(onlyOld);
        Assert.All(onlyNew, n => Assert.Equal(Fx6.FromInt(2), Mult(s, n)));
        Assert.All(onlyOld, n => Assert.Equal(Fx6.One, Mult(s, n)));
    }

    [Fact]
    public void P2_WithTwoLinesUnderF17b_DoublesThePrcOfTheCandidateN_NotOfTheWorkingLineW()
    {
        SliceSession s = Run(UsaP2);
        int w = Newest(s, 0);                                   // the start line, generation 1
        int n = PutGen2(s, DesignStage.Series);                 // the candidate: the larger platform generation
        Assert.Equal(2, Enumerable.Range(0, s.World.ProductsLayout.Designs).Count(d => s.World.Products.Stage(0, d) == DesignStage.Series));
        (int[] onlyN, int[] onlyW) = Difference(s, w, n);
        Assert.NotEmpty(onlyN);
        Assert.NotEmpty(onlyW);
        Assert.All(onlyN, p => Assert.Equal(Fx6.FromInt(2), Mult(s, p)));
        Assert.All(onlyW, p => Assert.Equal(Fx6.One, Mult(s, p)));
    }
}
