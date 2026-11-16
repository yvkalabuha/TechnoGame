using Singularity.Core.Policy;

namespace Singularity.Session.Tests;

/// <summary>A41 (contract §4.4a r13, #245): SLOT for P2+P3 is the build; <c>A41_FORM</c> and <c>PILOT_PROFILES</c> are options that add a <c>run.a41</c> column.</summary>
public class A41OptionsTests
{
    private static readonly ulong[] Seeds = [1, 2, 3, 4, 5];

    private static SliceSession Run(ulong seed, int threads, int months, PilotForm form = PilotForm.Slot, PilotProfileSet set = PilotProfileSet.P2P3)
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = seed, Threads = threads, PilotForm = form, PilotProfiles = set });
        for (long t = 0; t < months; t++)
            s.StepThroughMonth(t);
        return s;
    }

    [Fact]
    public void Default_IsSlotForP2AndP3_AndCarriesNoRunColumn()
    {
        Assert.Equal(PilotForm.Slot, new SliceRunConfig().PilotForm);
        Assert.Equal(PilotProfileSet.P2P3, new SliceRunConfig().PilotProfiles);
        Assert.DoesNotContain("run.a41", Run(1, 1, 1).World.Table.Columns.Select(c => c.Name));
        Assert.Contains("run.a41", Run(1, 1, 1, PilotForm.Whole).World.Table.Columns.Select(c => c.Name));
        Assert.Contains("run.a41", Run(1, 1, 1, set: PilotProfileSet.P3).World.Table.Columns.Select(c => c.Name));
    }

    [Fact]
    public void OutOfRangeOptions_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => SliceTestData.Create(new SliceRunConfig { Seed = 1, PilotForm = (PilotForm)7 }));
        Assert.Throws<ArgumentException>(() => SliceTestData.Create(new SliceRunConfig { Seed = 1, PilotProfiles = (PilotProfileSet)9 }));
    }

    // The compositions the AI chose (alternatives of every design of every actor, 384 months of snapshots): not affected by the run.a41 column.
    private static string Compositions(ulong seed, PilotForm form = PilotForm.Slot, PilotProfileSet set = PilotProfileSet.P2P3)
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = seed, Threads = 2, PilotForm = form, PilotProfiles = set });
        var sb = new System.Text.StringBuilder();
        for (long t = 0; t < 384; t++)
        {
            s.StepThroughMonth(t);
            if (t % 12 != 0)
                continue;
            for (int a = 0; a < s.World.Scenario.ActorCount; a++)
            {
                for (int d = 0; d < s.World.ProductsLayout.Designs; d++)
                    sb.Append(string.Join(',', s.World.Products.Alternatives(a, d))).Append(';');
            }
        }
        return sb.ToString();
    }

    [Fact]
    public void Options_ChangeTheCompositionsTheAiChooses_AndL1L2Hold()
    {
        string a = Run(3, 1, 384).LastHash;
        Assert.Equal(a, Run(3, 1, 384).LastHash);
        Assert.Equal(a, Run(3, 4, 384).LastHash);
        int whole = 0, p3 = 0;
        foreach (ulong seed in Seeds)
        {
            string main = Compositions(seed);
            whole += main != Compositions(seed, form: PilotForm.Whole) ? 1 : 0;
            p3 += main != Compositions(seed, set: PilotProfileSet.P3) ? 1 : 0;
        }
        Assert.True(whole > 0, "WHOLE never gave another composition than SLOT in 5 seeds");
        Assert.True(p3 > 0, "P3-only never gave another composition than P2+P3 in 5 seeds");
    }
}
