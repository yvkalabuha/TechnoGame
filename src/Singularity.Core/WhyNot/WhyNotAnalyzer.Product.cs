using Singularity.Core.Industry;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Core.WhyNot;

public sealed partial class WhyNotAnalyzer
{
    /// <summary>PRODUCT and ECONOMY links of a live design (contract §6 items 3 and 4).</summary>
    private void ProductLinks(List<Link> links, int actor, int site, int design, DesignStage stage, int[] alts, int[] prc, long month)
    {
        Fx6 yield = MinYield(site, prc, month);
        int gen = _p.PlatformGen(actor, design);

        // Workforce: granted/n < 0.9 (the HYP-0001 lesson: the cap acted while the explanation did not show it).
        if (stage == DesignStage.Project)
        {
            int bureau = _p.DesignBureau(actor, design);
            if (bureau >= 0)
                WorkforceLink(links, site, ProductsLayout.EngineerCategory, _pl.BureauRequester(bureau), WhyNotCause.Engineers, yield, month);
        }
        WorkforceLink(links, site, ProductsLayout.EngineerCategory, _pl.FixRequester(design), WhyNotCause.Engineers, yield, month);
        if (stage == DesignStage.Series)
        {
            for (int c = 0; c < _pl.ClusterCount; c++)
                WorkforceLink(links, site, _pl.SkilledCategory, _pl.SkilledRequester(design, c), WhyNotCause.SkilledWorkforce, yield, month);
        }

        Fx6 hidden = _p.DefectsHidden(actor, design), gateLevel = _pp.KGate * _p.DRef;
        if (stage == DesignStage.Prototype && !_p.GateOpen(actor, design, month))
            links.Add(new Link(WhyNotLayer.Product, WhyNotCause.SeriesGate, design, hidden, gateLevel, Offer(Lever.ForceSeries), yield, MonthsToGate(actor, design, stage, month)));
        else if (stage is DesignStage.Series or DesignStage.InService && hidden + _p.DefectsOpen(actor, design) > gateLevel)
        {
            Fx6 total = hidden + _p.DefectsOpen(actor, design);
            // Hidden above the level: testing brings it down (A31). Only open ones above it: the fix team does, at its granted hours.
            Fx6 fixRate = Fx6.FromInt(_i.Granted(site, ProductsLayout.EngineerCategory, _pl.FixRequester(design))) * _pl.HPm / _pp.FixHours;
            links.Add(new Link(WhyNotLayer.Product, WhyNotCause.DefectsHidden, design, total, gateLevel, [], yield,
                hidden > gateLevel ? MonthsToGate(actor, design, stage, month) : Eta(total, gateLevel, fixRate)));
        }

        Fx6 planned = stage == DesignStage.Series && _p.Rate(actor, design) > Fx6.Zero ? _p.Rate(actor, design) : _pl.RRef;
        if (stage != DesignStage.InService)
        {
            Fx6 kg = _pl.AltAlloyKg[alts[_pl.AlloySlot]];
            AlloyAvailability alloy = _i.Alloy(site, month);
            Fx6 needAlloy = kg * planned;
            // alloy_free is a flag, not kg = 0 (spec v0.14 §1.4 item 5): such a project has no alloy link.
            if (!DesignRules.AlloyFree(_pl, alts) && kg > Fx6.Zero && (alloy.Deficit || alloy.Supply < needAlloy))
                links.Add(new Link(WhyNotLayer.Product, WhyNotCause.AlloyDeficit, design, alloy.Supply, needAlloy, [], yield, null));
        }

        // ECONOMY: admitted but not affordable at the planned rate (stages О and С; П has no unit cost to pay yet).
        if (stage is DesignStage.Prototype or DesignStage.Series && _i.Profile(site, DesignRules.Items(_pl, prc), month).Admitted)
        {
            Fx6 cost = _p.UnitCostAt(actor, design, month) * planned + _p.UpkeepNeeded(actor);
            // Step 7 pays from the common money and then from the line's training money.
            Fx6 budget = _in.ProcurementBudget(actor, month) + _in.TrainingBudget(actor, design, month);
            if (cost > budget)
            {
                int slot = VariantSlot(actor, alts, gen, -1);
                links.Add(new Link(WhyNotLayer.Economy, WhyNotCause.NotAffordable, design, budget, cost, Offer(slot >= 0 ? [Lever.SimplifiedVariant, Lever.RetireCohort, Lever.SellCohort] : [Lever.RetireCohort, Lever.SellCohort]), yield, null, VariantSlot: slot));
            }
        }
    }

    /// <summary>
    /// <see cref="WhyNotCause.EngineGenLimit"/> (spec v0.14 §1.4): at the highest known engine some slot holds a known alternative that
    /// <c>max_eng_gen</c> rules out and has no non-substitute alternative. The lever is to learn the first unknown requirement of the
    /// lowest-rank non-substitute alternative of the slot (for the hot section: 30, then 33).
    /// </summary>
    private void EngineGenLimitLinks(List<Link> links, int actor)
    {
        int engine = -1;
        for (int a = _pl.AltStart[_pl.BaseSlot + 1] - 1; a >= _pl.AltStart[_pl.BaseSlot] && engine < 0; a--)
        {
            if (DesignRules.Available(_pl, a, n => Knows(actor, n)))
                engine = a;
        }
        if (engine < 0)
            return;
        for (int s = 0; s < _pl.SlotCount; s++)
        {
            int limited = -1, plain = -1, lever = -1;
            for (int a = _pl.AltStart[s]; a < _pl.AltStart[s + 1]; a++)
            {
                bool known = DesignRules.Available(_pl, a, n => Knows(actor, n));
                if (known && !DesignRules.EngineAllows(_pl, a, engine))
                    limited = a;
                else if (!_pl.AltSubstitute[a] && known)
                    plain = a;
                if (!_pl.AltSubstitute[a] && lever < 0)
                    lever = a;
            }
            if (limited < 0 || plain >= 0)
                continue;
            int node = FirstUnknownTarget(actor, lever);
            if (node < 0)
                continue;
            links.Add(new Link(WhyNotLayer.Product, WhyNotCause.EngineGenLimit, node, Fx6.FromInt(_pl.AltMaxEngGen[limited]),
                Fx6.FromInt(_pl.AltEngGen[engine]), Offer(Lever.ResearchPriority), Fx6.Zero, null, Slot: s));
        }
    }

    // First unknown requirement of an alternative: the first member of the first unmet group, then its processes; −1 if all are known.
    private int FirstUnknownTarget(int actor, int alt)
    {
        for (int g = _pl.AltGroupStart[alt]; g < _pl.AltGroupStart[alt + 1]; g++)
        {
            bool any = false;
            for (int m = _pl.GroupMemberStart[g]; m < _pl.GroupMemberStart[g + 1]; m++)
                any |= Knows(actor, _pl.GroupMembers[m]);
            if (!any)
                return _pl.GroupMembers[_pl.GroupMemberStart[g]];
        }
        foreach (int p in _pl.PrcOf(alt))
        {
            if (!Knows(actor, p))
                return p;
        }
        return -1;
    }

    private void WorkforceLink(List<Link> links, int site, int category, int requester, WhyNotCause cause, Fx6 yield, long month)
    {
        long n = _i.Requested(site, category, requester), granted = _i.Granted(site, category, requester);
        if (n <= 0 || granted * 10 >= n * 9)
            return;
        // The pool is a stock of people with no rate in the snapshot, so the link has no eta.
        links.Add(new Link(WhyNotLayer.Product, cause, requester, Fx6.FromInt(granted), Fx6.FromInt(n),
            cause == WhyNotCause.Engineers ? Offer(Lever.ReassignWorkforce) : [], yield, null));
    }

    /// <summary>
    /// Months until the gate opens at the current test rate (and fleet hours in series): the first month k ≥ 1 with
    /// <c>D0 − detected(T + k·step) ≤ k_gate·D_ref</c> and at least <c>t_test_min</c> months of testing (contract §4.4b); null if it
    /// does not within the horizon. The same formulas as the layer (A31), stepped forward on a copy; nothing is written.
    /// </summary>
    private int? MonthsToGate(int actor, int design, DesignStage stage, long month)
    {
        Fx6 d0 = _p.InitialDefects(actor, design), cap = _pp.KGate * _p.DRef;
        Fx6 step = (stage == DesignStage.InService ? Fx6.Zero : _pp.TestRate)
            + (stage is DesignStage.Series or DesignStage.InService ? _p.InService(actor, design) * _pp.FleetHoursPerUnit : Fx6.Zero);
        int o = _p.StageOMonth(actor, design);
        Fx6 hours = _p.Hours(actor, design);
        for (int k = 1; k <= GateHorizon; k++)
        {
            hours += step;
            Fx6 left = d0 - ProductsMath.DetectedTotal(hours, d0, _pp.LambdaD, _pp.BetaQ10);
            if (left <= cap && (o < 0 || month + k - o >= _pp.TTestMin))
                return k;
        }
        return null;
    }
}
