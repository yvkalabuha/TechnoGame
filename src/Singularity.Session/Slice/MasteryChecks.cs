using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session;

/// <summary>
/// The one check «may a mastery programme start on this process now» (TASK-0134, production-mastery.md §1, §2): used by the player's command at
/// application, by the AI and by the product layer at application of either. A pure function of the snapshot; no RNG.
/// </summary>
internal static class MasteryChecks
{
    public static ProgrammeVerdict Verdict(SliceWorld w, int actor, int number, bool abroad, long month)
    {
        ProductsLayout pl = w.ProductsLayout;
        int k = pl.ProcIndex(number);
        int row = w.IndustryLayout.RowOf(number);
        if (k < 0 || row < 0)
            return new(MasteryRefusal.UnknownProcess);
        if (pl.ProcTier[k] < MasteryParams.MinTier)
            return new(MasteryRefusal.TierLtMin);
        MasteryParams m = w.ProductsParameters.Mastery;
        if (!m.On(month, MasteryRules.Programme))
            return new(MasteryRefusal.OutOfScope);
        if (!w.ProductsInputs.Knows(actor, number, month))
            return new(MasteryRefusal.UnknownProcess);
        if (w.Products.ProgrammeStamp(actor, number) > 0)
            return new(MasteryRefusal.AlreadyActive);
        if (w.ProductsInputs.CultureYield(actor, number, month) >= w.ProductsParameters.YProd)
            return new(MasteryRefusal.AlreadyReady);
        if (w.Products.ProgrammeCount(actor) >= m.SMax)
            return new(MasteryRefusal.SlotsFull);
        // The donor is the one that makes «abroad» faster; the check of the mode is the same test the culture rule's catch-up uses.
        if (abroad && !w.Industry.WorldLead(actor, row))
            return new(MasteryRefusal.NoDonor);
        ProgrammeAssessment a = w.Industry.AssessProgramme(actor, row, w.Products.ProgrammeNominal(number), domestic: !abroad, month);
        switch (a.Fit)
        {
            case ProgrammeFit.NotLabCeiling:
                return new(MasteryRefusal.LimiterNotLabCeiling);
            case ProgrammeFit.Tooling:
                return new(MasteryRefusal.Tooling);
            case ProgrammeFit.Saturation:
                return new(MasteryRefusal.SaturationWait);
            case ProgrammeFit.CellLed:
                return new(MasteryRefusal.CellLedBy, a.LeadRow < 0 ? 0 : w.IndustryLayout.Number[a.LeadRow]);
            case ProgrammeFit.NoRate:
                return new(MasteryRefusal.EtaGtTLook, int.MaxValue);
        }
        return a.EtaMonths > m.TLook ? new(MasteryRefusal.EtaGtTLook, a.EtaMonths) : new(MasteryRefusal.None, a.EtaMonths);
    }

    /// <summary>
    /// <c>Hcap(month + horizon) &gt; r_d</c> in every active dimension of the process at the actor's site (static foresight, rev 2 p. 3).
    /// Tooling reachability is not part of it yet.
    /// </summary>
    public static bool Masterable(SliceWorld w, int actor, int number, long month, int horizon)
    {
        int row = w.IndustryLayout.RowOf(number);
        if (row < 0)
            return false;
        for (int d = 0; d < Dims3.Count; d++)
        {
            Fx6 req = w.IndustryLayout.Req[row][d];
            if (req > Fx6.Zero && w.IndustryInputs.Hcap(actor, (Dimension)d, month + horizon) <= req)
                return false;
        }
        return true;
    }
}
