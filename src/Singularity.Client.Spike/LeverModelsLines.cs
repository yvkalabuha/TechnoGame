#nullable disable
using System;
using System.Collections.Immutable;
using System.Linq;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;
using Singularity.Session;

namespace Singularity.Client.Spike;

// Pure models of the production-line window and the licence-market window (TASK-0156 part 2, docs/ui/specs/levers-windows.md §4, §5). Same rules as LeverModels.cs:
// the live window and the tests call these functions; a value the view does not carry is left out, never inferred. Texts are locale keys; money goes through the function passed in.

/// <summary>A project that stands on a line or in the design office: stage, cost per machine, readiness, fleet and the line rate, all taken from the DesignView.</summary>
public sealed record LineRow(int Design, DesignHandle Handle, int Gen, int StageNo, string UnitCost, int ReadinessPct, double InService, double Rate, string BindKey, string LicensedFrom, bool CanClose, bool CanCancel);

/// <summary>An advice of the adviser (#479): what it asks, what it costs per month and why, and whether Accept can go (the line limit says why not in words).</summary>
public sealed record ProposalRow(ProposalKind Kind, int Design, DesignHandle Handle, int Gen, double Rate, string MonthlyMoney, string ReasonKey, string AcceptDisabledKey, int AcceptDisabledArg, ProposalView Source);

public sealed record LinesModel(ImmutableArray<LineRow> Lines, ImmutableArray<ProposalRow> Proposals, int SeriesLines, int MaxLines, bool AtLimit);

public static class LinesWindowModel
{
    /// <summary>Column headers of the line table (locale keys): what the machine is called, its stage, cost, readiness, fleet, output and what limits it.</summary>
    public static readonly string[] ColumnKeys = { "lines.col.project", "lines.col.stage", "lines.col.cost", "lines.col.ready", "lines.col.fleet", "lines.col.rate", "lines.col.bind" };

    /// <summary>The cells of a line in the order of <see cref="ColumnKeys"/>; a column that does not apply to the stage (output and limit before a series) is «—».</summary>
    public static string[] Cells(LineRow l, System.Func<string, object[], string> t, System.Func<double, int, string> n)
    {
        bool series = l.StageNo == (int)DesignStage.Series;
        return new[]
        {
            t("lines.cell.project", new object[] { l.Design + 1, l.Gen }),
            t("stage." + l.StageNo, System.Array.Empty<object>()),
            l.UnitCost,
            l.ReadinessPct + " %",
            t("lines.cell.fleet", new object[] { n(l.InService, 0) }),
            series ? t("lines.cell.rate", new object[] { n(l.Rate, 1) }) : "—",
            series ? t(l.BindKey, System.Array.Empty<object>()) : "—",
        };
    }

    /// <summary>The lines of the actor as the window shows them; a project without a stage (an empty slot) is not a row.</summary>
    public static LinesModel Build(ImmutableArray<DesignView> designs, ImmutableArray<ProposalView> proposals, int maxLines,
        MoneyFormat.Scale scale, Func<double, string> fmt, Func<int, string> sellerName)
    {
        var rows = ImmutableArray.CreateBuilder<LineRow>();
        int series = 0;
        if (!designs.IsDefault)
            foreach (var d in designs)
            {
                if (d.Stage == DesignStage.None) continue;
                if (d.Stage == DesignStage.Series) series++;
                rows.Add(new LineRow(d.Design, d.Handle, d.Gen, (int)d.Stage, fmt(MoneyFormat.Million1990(d.UnitCost.ToDouble(), scale)),
                    (int)Math.Round(d.Readiness.ToDouble() * 100), d.InService.ToDouble(), d.Rate.ToDouble(), "bind." + d.Bind,
                    d.LicensedFrom >= 0 ? sellerName(d.LicensedFrom) : "", d.Stage == DesignStage.Series, d.Stage is DesignStage.Project or DesignStage.Prototype));
            }
        var props = ImmutableArray.CreateBuilder<ProposalRow>();
        if (!proposals.IsDefault)
            foreach (var p in proposals)
            {
                var d = designs.IsDefault ? null : designs.FirstOrDefault(x => x.Design == p.Design);
                string monthly = d == null ? "" : fmt(MoneyFormat.Million1990(p.Rate.ToDouble() * d.UnitCost.ToDouble(), scale));
                bool limit = p.Kind == ProposalKind.Open && series >= maxLines;
                props.Add(new ProposalRow(p.Kind, p.Design, p.Handle, d?.Gen ?? 0, p.Rate.ToDouble(), monthly,
                    p.Kind == ProposalKind.Close ? "line.reason." + p.Reason : "line.reason.open", limit ? "why.line.limit" : "", limit ? maxLines : 0, p));
            }
        return new LinesModel(rows.ToImmutable(), props.ToImmutable(), series, maxLines, series >= maxLines);
    }
}

public sealed record OfferRow(int Seller, string SellerName, int Gen, string Price, string Reference, int PremiumPct, string WorldPrice, bool FewOffers, SaleCircle Circle,
    string RefusalKey, bool CanBuy, string ReserveAfter, double ReserveAfterMillion, bool ReserveSame);
public sealed record OwnRow(int Gen, string Reference, double M, string Price, SaleCircle Circle);
public sealed record HeldRow(int Seller, string SellerName, int Gen, int StartMonth);

/// <summary>The licence market of the player (null market view = Enabled false: the run has none).</summary>
public sealed record MarketModel(bool Enabled, bool External, ImmutableArray<OfferRow> Offers, ImmutableArray<OwnRow> Own, ImmutableArray<HeldRow> Held,
    string IncomeYear, string IncomeCap, string ReserveBalance, string LastRefusalKey, string LastRefusalSeller, int LastRefusalGen);

public static class MarketWindowModel
{
    /// <summary>Lowest and highest multiplier of the asking price the core takes (OfferLicence.M in [0.5; 2]); shown, never exceeded by the control.</summary>
    public const double MMin = 0.5, MMax = 2.0, MStep = 0.1;

    /// <summary>Locale key of why a deal is refused now (the reason is the core's own BadLicence); empty = the deal goes.</summary>
    public static string RefusalKey(BadLicence r) => r == BadLicence.None ? "" : "licence.bad." + r;

    /// <summary>One rule for the button and for the reason under it: a deal is open when the core's own check on the live view says so and the market is open to the power.</summary>
    public static bool CanBuy(OfferRow o, bool external) => o.CanBuy && external;

    /// <summary>The «Причина» line: only while the Buy button is inactive (never under an active button), always the core's own reason from the view.</summary>
    public static string ReasonKey(OfferRow o, bool external) =>
        CanBuy(o, external) ? "" : o.RefusalKey != "" ? o.RefusalKey : "mkt.notyours.short";

    /// <summary>The offer the card opens on: the first one that can be bought, else the first; -1 without offers.</summary>
    public static int DefaultOffer(ImmutableArray<OfferRow> offers, bool external)
    {
        for (int i = 0; i < offers.Length; i++) if (CanBuy(offers[i], external)) return i;
        return offers.IsEmpty ? -1 : 0;
    }

    public static string OfferRefusalKey(OfferRefusal r) => r == OfferRefusal.None ? "" : "licence.offer." + r;

    public static MarketModel Build(LicenceMarketView m, ReserveView? reserve, MoneyFormat.Scale scale, Func<double, string> fmt, Func<int, string> sellerName)
    {
        if (m == null) return new MarketModel(false, false, ImmutableArray<OfferRow>.Empty, ImmutableArray<OwnRow>.Empty, ImmutableArray<HeldRow>.Empty, "", "", "", "", "", 0);
        string Money(Fx6 idx) => fmt(MoneyFormat.Million1990(idx.ToDouble(), scale));
        double balance = reserve?.Balance.ToDouble() ?? 0;
        var offers = ImmutableArray.CreateBuilder<OfferRow>();
        foreach (var o in m.Offers.OrderBy(x => x.Gen).ThenBy(x => x.Seller))
        {
            var wp = m.WorldPrices.FirstOrDefault(w => w.Gen == o.Gen);
            bool few = wp.Average is null;   // fewer than two sellers: only the reference price is shown (#478)
            double afterM = MoneyFormat.Million1990(balance - o.Price.ToDouble(), scale);
            string after = reserve is null ? "" : fmt(afterM);
            bool same = reserve is not null && after == fmt(MoneyFormat.Million1990(balance, scale));   // the price vanishes in the rounding of the balance
            offers.Add(new OfferRow(o.Seller, sellerName(o.Seller), o.Gen, Money(o.Price), Money(o.Reference), (int)Math.Round((o.M.ToDouble() - 1) * 100),
                few ? "" : Money(wp.Average.Value), few, o.Circle, RefusalKey(o.Refusal), o.Refusal == BadLicence.None, after, afterM, same));
        }
        var own = m.Own.Select(o => new OwnRow(o.Gen, Money(o.Reference), o.M.ToDouble(), Money(o.Price), o.Circle)).ToImmutableArray();
        var held = m.Held.Select(h => new HeldRow(h.Seller, sellerName(h.Seller), h.Gen, h.Start)).ToImmutableArray();
        return new MarketModel(true, m.External, offers.ToImmutable(), own, held, Money(m.IncomeThisYear), Money(m.IncomeCap), reserve is null ? "" : fmt(MoneyFormat.Million1990(balance, scale)),
            RefusalKey(m.LastRefusal), m.LastRefusal == BadLicence.None ? "" : sellerName(m.LastRefusalSeller), m.LastRefusalGen);
    }

    /// <summary>The next multiplier of an offer for a click on − or +: one step, clamped to the range the core takes, rounded to a tenth.</summary>
    public static double StepM(double m, int dir) => Math.Round(Math.Clamp(m + dir * MStep, MMin, MMax), 1);
}
