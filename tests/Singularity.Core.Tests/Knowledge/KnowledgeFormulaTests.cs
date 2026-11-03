using System.Text.Json;
using Singularity.Core.Knowledge;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Knowledge;

/// <summary>Knowledge cases of lab/reference/jet_slice/out/formulas.json, exact (reference D1, D2, D3).</summary>
public class KnowledgeFormulaTests
{
    private static JsonElement F => JetReference.Formulas;

    [Fact]
    public void CEffAndProgress_MatchReference()
    {
        int cases = 0;
        foreach (JsonElement c in F.GetProperty("knowledge").EnumerateArray())
        {
            JsonElement i = c.GetProperty("input");
            Fx6 informs = Fx6.Zero;
            foreach (JsonElement w in i.GetProperty("informs_w_known").EnumerateArray())
                informs += JetReference.Raw(w);
            Fx6 ceff = KnowledgeMath.CEff(JetReference.Raw(i.GetProperty("c_money")), JetReference.Raw(i.GetProperty("k_diff")),
                i.GetProperty("known_elsewhere").GetBoolean(), JetReference.Raw(i.GetProperty("k_K")),
                JetReference.Raw(i.GetProperty("Kmix")), JetReference.Raw(i.GetProperty("K_ref")), informs);
            Assert.Equal(c.GetProperty("c_eff").GetInt64(), ceff.Raw);
            Assert.Equal(c.GetProperty("dprog").GetInt64(), (JetReference.Raw(c.GetProperty("alloc")) / ceff).Raw);
            cases++;
        }
        Assert.Equal(3, cases);
    }

    [Fact]
    public void CMoney_IsCnTimesTwelveSRef()
    {
        // A9: c_n = 1.5 S_ref·years at S_ref = 0.1 → 1.8 per month of money.
        Assert.Equal(1_800_000, KnowledgeMath.CMoney(Fx6.FromRaw(1_500_000), Fx6.FromRaw(100_000)).Raw);
    }

    [Fact]
    public void Hazard_MatchesReference()
    {
        int cases = 0;
        foreach (JsonElement c in F.GetProperty("hazard").EnumerateArray())
        {
            JsonElement key = c.GetProperty("key"), o = c.GetProperty("output");
            HazardStep h = KnowledgeMath.Hazard(JetReference.Raw(c.GetProperty("prog")), JetReference.Raw(c.GetProperty("theta_start")),
                JetReference.Raw(c.GetProperty("h_max")), JetReference.Raw(c.GetProperty("y_half")));
            Assert.Equal(o.GetProperty("active").GetBoolean(), h.Active);
            Assert.Equal(o.GetProperty("h").GetInt64(), h.H.Raw);
            if (!h.Active)
            {
                Assert.False(o.GetProperty("opened").GetBoolean());
                cases++;
                continue;
            }
            Assert.Equal(o.GetProperty("y").GetInt64(), h.Y.Raw);
            ulong u = o.GetProperty("check").GetProperty("u").GetUInt64();
            if (key.GetProperty("tick").ValueKind != JsonValueKind.Null)
            {
                // entity = actor·64 + node with actor 0 (A7)
                var rng = new CounterRng(key.GetProperty("seed").GetUInt64());
                Assert.Equal(u, KnowledgeRng.Hazard(rng, key.GetProperty("tick").GetInt64(), 0, key.GetProperty("entity").GetInt32()));
            }
            Assert.Equal(o.GetProperty("opened").GetBoolean(), CounterRng.Chance(u, h.H));
            cases++;
        }
        Assert.Equal(7, cases);
    }

    [Fact]
    public void JDraws_MatchReference()
    {
        int cases = 0;
        foreach (JsonElement c in F.GetProperty("draws").EnumerateArray())
        {
            if (c.GetProperty("kind").GetString() != "j")
                continue;
            JsonElement key = c.GetProperty("key"); // [seed, 12y, PRIO_J, entity, 0]
            Assert.Equal(KnowledgeRng.SystemPrioJ, key[2].GetInt32());
            Fx6 j = KnowledgeRng.J(new CounterRng(key[0].GetUInt64()), (int)(key[1].GetInt64() / 12), 0, key[3].GetInt32());
            Assert.Equal(c.GetProperty("value").GetInt64(), j.Raw);
            cases++;
        }
        Assert.Equal(3, cases);
    }

    [Fact]
    public void JYear0_MatchesTrajectorySetup()
    {
        JsonElement t = JetReference.Trajectory;
        var rng = new CounterRng(t.GetProperty("seed").GetUInt64());
        foreach (JsonProperty p in t.GetProperty("setup").GetProperty("j_year0").EnumerateObject())
        {
            int node = int.Parse(p.Name, System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(p.Value.GetProperty("j").GetInt64(), KnowledgeRng.J(rng, 0, 0, node).Raw);
        }
    }

    [Fact]
    public void Calendar_MatchesReference()
    {
        JsonElement cal = F.GetProperty("calendar");
        var c = new TickCalendar(JetReference.StartDate);
        Assert.Equal(cal.GetProperty("start_date").GetString(), c.DateOf(0).ToString());
        foreach (JsonElement x in cal.GetProperty("cases").EnumerateArray())
        {
            long m = x.GetProperty("month").GetInt64();
            long d = c.TickOfMonth(m);
            Assert.Equal(x.GetProperty("day_tick").GetInt64(), d);
            Assert.Equal(x.GetProperty("date").GetString(), c.DateOf(d).ToString());
            Assert.Equal(x.GetProperty("days_in_month").GetInt64(), c.TickOfMonth(m + 1) - d);
            Assert.Equal(m, c.MonthIndex(d));
            Assert.True(c.IsDue(Cadence.Monthly, d));
        }
        foreach (string span in new[] { "window", "horizon" })
        {
            JsonElement w = cal.GetProperty(span);
            Assert.Equal(w.GetProperty("days").GetInt64(), c.TickOfMonth(w.GetProperty("months").GetInt64()));
            Assert.Equal(w.GetProperty("last_date").GetString(), c.DateOf(w.GetProperty("last_day_tick").GetInt64()).ToString());
        }
    }

    [Fact]
    public void KIndStep_MatchesReference()
    {
        int cases = 0;
        foreach (JsonElement c in F.GetProperty("k_ind").EnumerateArray())
        {
            JsonElement i = c.GetProperty("input");
            Fx6 r = KnowledgeMath.KIndStep(JetReference.Raw(i.GetProperty("K_ind")), JetReference.Raw(i.GetProperty("K_d")),
                JetReference.Raw(i.GetProperty("V_d")), JetReference.Raw(i.GetProperty("eta")), JetReference.Raw(i.GetProperty("V_ref")),
                JetReference.Raw(i.GetProperty("theta_ind")), JetReference.Raw(i.GetProperty("delta_ind")));
            Assert.Equal(c.GetProperty("output").GetProperty("K_ind_new").GetInt64(), r.Raw);
            cases++;
        }
        Assert.Equal(6, cases);
    }

    [Theory]
    [InlineData(0, 300_000, 0)]          // no output: K_ind decays (0 stays 0)
    [InlineData(1_000_000_000, 0, 0)]    // K_d = 0: no growth (v2.1)
    [InlineData(1_000_000_000, 3, 0)]    // θ_ind·K_d floors to 0 raw: no growth instead of a division by zero
    public void KIndStep_EdgeCases(long volume, long kd, long expected)
    {
        Fx6 r = KnowledgeMath.KIndStep(Fx6.Zero, Fx6.FromRaw(kd), Fx6.FromRaw(volume), Fx6.FromRaw(10_000),
            Fx6.FromInt(1000), Fx6.FromRaw(250_000), Fx6.FromRaw(3_000));
        Assert.Equal(expected, r.Raw);
    }

    [Fact]
    public void KIndStep_DecaysAndSaturates()
    {
        Fx6 eta = Fx6.FromRaw(10_000), vRef = Fx6.FromInt(1000), theta = Fx6.FromRaw(250_000), delta = Fx6.FromRaw(3_000);
        // decay x − δ·x
        Assert.Equal(1_000_000 - 3_000, KnowledgeMath.KIndStep(Fx6.One, Fx6.One, Fx6.Zero, eta, vRef, theta, delta).Raw);
        // at the cap θ_ind·K_d there is no further growth
        Assert.Equal(250_000, KnowledgeMath.KIndStep(Fx6.FromRaw(250_000), Fx6.One, Fx6.FromInt(1000), eta, vRef, theta, delta).Raw);
    }

    [Fact]
    public void DisciplineVolume_CountsOnlyYieldAtLeast03()
    {
        Fx6[] v = [Fx6.FromInt(100), Fx6.FromInt(50), Fx6.FromInt(7)];
        Fx6[] y = [Fx6.FromRaw(300_000), Fx6.FromRaw(299_999), Fx6.One];
        int[] d = [0, 0, 1];
        Assert.Equal(Fx6.FromInt(100), KnowledgeMath.DisciplineVolume(v, y, d, 0));
        Assert.Equal(Fx6.FromInt(7), KnowledgeMath.DisciplineVolume(v, y, d, 1));
    }
}
