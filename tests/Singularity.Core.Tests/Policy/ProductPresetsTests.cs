using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;
using static Singularity.Core.Tests.Products.ProductTestData;

namespace Singularity.Core.Tests.Policy;

/// <summary>A41 in the general form (contract §4.4a r12, #242 п. 2; TASK-0058): invariants R1–R3 of the pilot choice and the profile set of #244.</summary>
public class ProductPresetsTests
{
    private static readonly ProductsLayout L = Probe;

    private sealed class Snap(int known, Func<int, Fx6> yield, IReadOnlyList<(DesignStage Stage, bool Cancelled, int[] Alts)> designs, Func<int[], Fx6> value) : IPolicyProducts
    {
        public ProductsLayout Layout => L;
        public Fx6 DRef => Fx6.One;
        public PolicyDesign Design(int actorRow, int design, long month) => design < designs.Count
            ? new PolicyDesign(designs[design].Stage, 1, designs[design].Cancelled, false, 0, -1, -1, false, Fx6.Zero, Fx6.Zero)
            : default;
        public int[] Alternatives(int actorRow, int design) => design < designs.Count ? designs[design].Alts : [.. Enumerable.Repeat(-1, L.SlotCount)];
        public bool Knows(int actorRow, int nodeNumber, long month) => L.KnownUpTo(known)(nodeNumber);
        public bool Admitted(int actorRow, int design, long month) => false;
        public Fx6 AdmittedYield(int actorRow, int processNumber, long month) => yield(processNumber);
        public Fx6 AlloySupply(int actorRow, long month) => Fx6.FromInt(1_000_000);
        public int FreeBureaus(int actorRow) => 1;
        public Fx6 CandidateValue(int actorRow, ReadOnlySpan<int> alts, Fx6 budget, long month) => value(alts.ToArray());
    }

    private static PolicyParams Params(PilotChoice pilot, bool perSlot, PilotProfileSet set = PilotProfileSet.P2P3) =>
        new() { PilotChoice = pilot, PilotPerSlot = perSlot, PilotProfiles = set, FvBudget = Fx6.FromInt(1) };

    private static int[] Compose(PolicyProfile profile, PolicyParams p, Snap s, int gen)
    {
        var alts = new int[L.SlotCount];
        Assert.True(ProductPresets.Compose(profile, s, p, 0, gen, 0, alts));
        return alts;
    }

    private static Fx6 Hash(int[] alts) => Fx6.FromRaw(alts.Aggregate(17L, (h, a) => (h * 31 + a + 7) % 10_007));

    private static (DesignStage, bool, int[]) Live(int hotRank, DesignStage stage = DesignStage.Series, bool cancelled = false)
    {
        // A design whose alloy-slot alternative has the given rank (the first alternative of that slot with it).
        int alt = Enumerable.Range(L.AltStart[L.AlloySlot], L.AltStart[L.AlloySlot + 1] - L.AltStart[L.AlloySlot]).First(a => L.AltRank[a] == hotRank);
        int[] alts = [.. Enumerable.Repeat(-1, L.SlotCount)];
        alts[L.AlloySlot] = alt;
        return (stage, cancelled, alts);
    }

    // The contract oracle from the variants (pure functions of the snapshot), independent of ProductPresets.Compose.
    private static int[] Oracle(PolicyProfile profile, Snap s, int gen, IReadOnlyList<(DesignStage Stage, bool Cancelled, int[] Alts)> designs)
    {
        PresetVariants v = ProductPresets.Variants(s, Params(PilotChoice.Off, false), 0, gen, 0);
        int[] sup = [.. v.Superior];
        if (profile == PolicyProfile.Quality || v.Count != 2)
            return sup;
        int[] prod = [.. v.Producible];
        int[] chosen = profile == PolicyProfile.Mass || v.FvProducible > v.FvSuperior ? prod : sup;
        if (chosen != prod)
            return chosen;
        int slot = L.AlloySlot, cur = 0;
        foreach ((DesignStage st, bool cancelled, int[] alts) in designs)
        {
            if (!cancelled && st is DesignStage.Project or DesignStage.Prototype or DesignStage.Series or DesignStage.InService)
                cur = Math.Max(cur, alts[slot] >= 0 ? L.AltRank[alts[slot]] : 0);
        }
        bool fires = L.AltRank[sup[slot]] > L.AltRank[prod[slot]] && !L.PrcOf(prod[slot]).ToArray().ToHashSet().IsSupersetOf(L.PrcOf(sup[slot]).ToArray())
            && L.AltRank[prod[slot]] <= cur;
        int[] result = [.. prod];
        if (fires)
            result[slot] = sup[slot];
        return result;
    }

    [Fact]
    public void Pilot_QualityProfileIsNeverChanged()
    {
        var s = new Snap(3, _ => Fx6.FromRaw(100_000), [Live(1)], Hash2);
        int[] off = Compose(PolicyProfile.Quality, Params(PilotChoice.Off, false), s, 3);
        foreach (PilotProfileSet set in new[] { PilotProfileSet.P2P3, PilotProfileSet.P3 })
            Assert.Equal(off, Compose(PolicyProfile.Quality, Params(PilotChoice.Rank, true, set), s, 3));
    }

    private static Fx6 Hash2(int[] alts) => Hash(alts);

    [Fact]
    public void Pilot_KeepsSuperiorIffRankRuleHolds()
    {
        var rnd = new Random(4242);
        int fired = 0, notFired = 0, noProd = 0;
        var counts = new int[3];
        for (int i = 0; i < 400; i++)
        {
            int known = rnd.Next(1, 5), gen = rnd.Next(1, known + 1);
            Fx6 lowYield = rnd.Next(3) == 0 ? Fx6.One : Fx6.FromRaw(rnd.Next(0, 3) * 150_000);   // all processes admitted: producible may equal superior (F-PC3)
            var designs = new List<(DesignStage, bool, int[])>();
            for (int k = rnd.Next(0, 3); k > 0; k--)
                designs.Add(Live(rnd.Next(1, 4), (DesignStage)rnd.Next((int)DesignStage.Project, (int)DesignStage.InService + 1), rnd.Next(4) == 0));
            int salt = rnd.Next(1, 50);
            var s = new Snap(known, p => p % 3 == 0 ? lowYield : Fx6.One, designs, a => Hash(a) * Fx6.FromInt(1) + Fx6.FromRaw(salt * a.Sum()));
            foreach (PolicyProfile profile in new[] { PolicyProfile.Mass, PolicyProfile.Adaptive })
            {
                PresetVariants v = ProductPresets.Variants(s, Params(PilotChoice.Off, false), 0, gen, 0);
                counts[v.Count]++;
                if (v.Count == 0)
                    continue;
                int[] expected = Oracle(profile, s, gen, designs);
                int[] actual = Compose(profile, Params(PilotChoice.Rank, true), s, gen);
                Assert.Equal(expected, actual);                                      // R2: slot by slot
                if (v.Count == 1)
                {
                    noProd++;
                    Assert.True(v.Superior.SequenceEqual(actual));
                }                                                        // no prod or prod = sup: the result is chosen = sup
                int[] baseline = Compose(profile, Params(PilotChoice.Off, false), s, gen);
                if (!actual.SequenceEqual(baseline))
                    fired++;
                else
                    notFired++;
                for (int slot = 0; slot < L.SlotCount; slot++)
                    Assert.True(actual[slot] == baseline[slot] || slot == L.AlloySlot, $"slot {slot} differs outside S_impl");
            }
        }
        Assert.True(fired > 0 && notFired > 0 && noProd > 0, $"vacuous: counts {string.Join(',', counts)} fired {fired}, not fired {notFired}, no prod {noProd}");
    }

    // (R3) cur ignores «снят» (cancelled) designs: a cancelled design of a high rank does not stop the rule from firing.
    [Fact]
    public void Pilot_CurrentRankIgnoresWithdrawnDesigns()
    {
        var rnd = new Random(7);
        int checkedCases = 0, differs = 0;
        for (int i = 0; i < 200; i++)
        {
            int known = rnd.Next(3, 5);
            int salt = rnd.Next(1, 50);
            Func<int[], Fx6> value = a => Hash(a) + Fx6.FromRaw(salt);
            var without = new Snap(known, p => p % 3 == 0 ? Fx6.Zero : Fx6.One, [], value);
            var withGhost = new Snap(known, p => p % 3 == 0 ? Fx6.Zero : Fx6.One, [Live(3, DesignStage.Withdrawn, cancelled: true)], value);
            var withLive = new Snap(known, p => p % 3 == 0 ? Fx6.Zero : Fx6.One, [Live(3, DesignStage.Project)], value);
            PolicyParams on = Params(PilotChoice.Rank, true);
            int[] a = Compose(PolicyProfile.Mass, on, without, known);
            Assert.Equal(a, Compose(PolicyProfile.Mass, on, withGhost, known));
            // a live project of the highest rank does count: the result is the contract's
            Assert.Equal(Oracle(PolicyProfile.Mass, withLive, known, [Live(3, DesignStage.Project)]), Compose(PolicyProfile.Mass, on, withLive, known));
            differs += a.SequenceEqual(Compose(PolicyProfile.Mass, on, withLive, known)) ? 0 : 1;
            checkedCases++;
        }
        Assert.Equal(200, checkedCases);
        Assert.True(differs > 0, "a live project never changed the result: the test is vacuous");
    }

    // (#244) P3 only: P2 never keeps superior; P3 behaves as with P2+P3; P1 is unchanged either way.
    [Fact]
    public void PilotProfilesP3_NeverChangesTheMassProfile_AndKeepsTheAdaptiveOne()
    {
        var rnd = new Random(99);
        int changedP3 = 0;
        for (int i = 0; i < 300; i++)
        {
            int known = rnd.Next(2, 5);
            int salt = rnd.Next(1, 50);
            var designs = new List<(DesignStage, bool, int[])> { Live(rnd.Next(1, 4)) };
            var s = new Snap(known, p => p % 3 == 0 ? Fx6.Zero : Fx6.One, designs, a => Hash(a) + Fx6.FromRaw(salt));
            PolicyParams p23 = Params(PilotChoice.Rank, true), p3 = Params(PilotChoice.Rank, true, PilotProfileSet.P3);
            Assert.Equal(Compose(PolicyProfile.Mass, Params(PilotChoice.Off, false), s, known), Compose(PolicyProfile.Mass, p3, s, known));
            Assert.Equal(Compose(PolicyProfile.Adaptive, p23, s, known), Compose(PolicyProfile.Adaptive, p3, s, known));
            Assert.Equal(Compose(PolicyProfile.Quality, p23, s, known), Compose(PolicyProfile.Quality, p3, s, known));
            changedP3 += Compose(PolicyProfile.Mass, p23, s, known).SequenceEqual(Compose(PolicyProfile.Mass, p3, s, known)) ? 0 : 1;
        }
        Assert.True(changedP3 > 0, "P2+P3 never differed from P3-only for P2: the test is vacuous");
    }

    // (#244 print) WHOLE takes the whole superior when A41 fires, SLOT only the alloy slot: they differ where sup and prod differ in a non-alloy slot.
    [Fact]
    public void A41Form_WholeAndSlotDiffer_WhereSupAndProdDifferOutsideTheAlloySlot()
    {
        var rnd = new Random(5);
        int differing = 0;
        for (int i = 0; i < 300; i++)
        {
            int known = rnd.Next(2, 5);
            var s = new Snap(known, p => p % 3 == 0 ? Fx6.Zero : Fx6.One, [Live(rnd.Next(1, 4))], a => Hash(a));
            PresetVariants v = ProductPresets.Variants(s, Params(PilotChoice.Off, false), 0, known, 0);
            if (v.Count != 2)
                continue;
            PolicyParams slot = Params(PilotChoice.Rank, true) with { PilotForm = PilotForm.Slot }, whole = slot with { PilotForm = PilotForm.Whole };
            int[] a41Slot = Compose(PolicyProfile.Mass, slot, s, known), a41Whole = Compose(PolicyProfile.Mass, whole, s, known);
            bool outside = Enumerable.Range(0, L.SlotCount).Any(k => k != L.AlloySlot && v.Superior[k] != v.Producible[k]);
            if (a41Whole.SequenceEqual(v.Superior))                    // A41 fired in the WHOLE form
            {
                Assert.Equal(outside, !a41Slot.SequenceEqual(a41Whole));
                for (int k = 0; k < L.SlotCount; k++)
                    Assert.Equal(k == L.AlloySlot ? v.Superior[k] : v.Producible[k], a41Slot[k]);
                differing += outside ? 1 : 0;
            }
            else
            {
                Assert.Equal(a41Whole, a41Slot);                      // not fired: both forms are the profile's choice
            }
        }
        Assert.True(differing > 0, "no fixture where WHOLE and SLOT differ: the test is vacuous");
    }
}
