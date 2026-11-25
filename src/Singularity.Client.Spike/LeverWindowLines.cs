using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Godot;
using Singularity.Core.Commands;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;
using Singularity.Session;

namespace Singularity.Client.Spike;

/// <summary>The tabs «Производственные линии» and «Рынок лицензий» of the lever window (TASK-0156 part 2). The content is rebuilt only when its signature changes (a month, a click),
/// never per frame; every control is focusable; the models of LeverModelsLines.cs decide what is shown.</summary>
public sealed partial class LeverWindow
{
    public const int TabLines = 2, TabMarket = 3;

    readonly VBoxContainer _lineBox = new(), _lineBody = new(), _marketBox = new(), _marketBody = new();
    string _sigLines = "", _sigMarket = "";
    DesignHandle? _confirmLine; bool _confirmCancel;
    int _selOffer = -1; (int Seller, int Gen)? _confirmBuy;
    readonly Dictionary<int, double> _ownM = new();   // lookup only, never enumerated
    readonly Dictionary<int, SaleCircle> _ownC = new();
    LinesModel _lm; MarketModel _mm;
    ProposalView? _shotProposal; CatalogueLineView? _shotOffer;   // shot run only: fixture stand-ins when the live state has none (logged)

    /// <summary>Most lines the adviser actor may run at once: the one setting of the run (SliceRunConfig.AdviserMaxLines, wave 1: two).</summary>
    static int MaxLines => new SliceRunConfig().AdviserMaxLines;
    static int RefuseMonths => new SliceRunConfig().AdviserRefuseYears * 12;

    string SellerName(int i) => i >= 0 && i < _host.Layout.ActorIds.Length ? Names.Actor(_host.Layout.ActorIds[i]) : "—";

    ActorView MyActor(SliceView v) => v.Actors.FirstOrDefault(x => x.Actor == Player);

    void InitLinesAndMarket(VBoxContainer stack)
    {
        _lineBox.AddChild(_lineBody); _marketBox.AddChild(_marketBody);
        stack.AddChild(_lineBox); stack.AddChild(_marketBox);
        _lineBody.AddThemeConstantOverride("separation", 8); _marketBody.AddThemeConstantOverride("separation", 8);
    }

    static Control Card(Control inner, bool strong = false)
    {
        var box = new PanelContainer(); box.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Paper, strong ? Ui.Ink : Ui.Rule, 1, 10, Ui.R2, 8));
        box.AddChild(inner); return box;
    }

    static VBoxContainer Col() { var c = new VBoxContainer(); c.AddThemeConstantOverride("separation", 4); return c; }

    CaptionButton Act(string key, Action a, bool primary = false, bool enabled = true)
    {
        var b = Ui.Btn(key, a, 14, primary && enabled, true);
        b.CustomMinimumSize = new Vector2(0, 32); b.SizeFlagsHorizontal = SizeFlags.ShrinkBegin; b.Disabled = !enabled;
        return b;
    }

    // ---- lines ----

    LinesModel BuildLines(SliceView v)
    {
        var a = MyActor(v);
        var props = a?.Decisions?.Proposals ?? ImmutableArray<ProposalView>.Empty;
        if (_shotProposal is { } sp) props = props.Add(sp);
        return LinesWindowModel.Build(a?.Designs ?? default, props, MaxLines, MoneyFormat.Current, MoneyMillion, SellerName);
    }

    void FillLines()
    {
        _lm = BuildLines(_host.Snap.View);
        var m = _lm;
        string sig = Loc.Lang + "|" + _confirmLine + _confirmCancel + "|" + m.SeriesLines + "|" + string.Join(";", m.Lines.Select(l => l.Handle.Slot + "." + l.Handle.Generation + l.StageNo + l.InService + l.ReadinessPct + l.Rate))
            + "|" + string.Join(";", m.Proposals.Select(p => p.Kind + "." + p.Handle.Slot + "." + p.Handle.Generation + p.Rate + p.AcceptDisabledKey));
        if (sig == _sigLines) return;
        _sigLines = sig;
        foreach (var c in _lineBody.GetChildren()) { _lineBody.RemoveChild(c); c.QueueFree(); }
        _lineBody.AddChild(Ui.Lbl(Loc.T("lines.count", m.SeriesLines, m.MaxLines), 16, Ui.Ink2, FontKind.Draft));
        if (m.AtLimit) _lineBody.AddChild(Ui.MarkedRow(Ui.Warn, Ui.Wrap(Loc.T("why.line.limit", m.MaxLines), 14)));
        // adviser strip
        _lineBody.AddChild(Ui.Head("lines.adviser"));
        if (m.Proposals.IsEmpty) _lineBody.AddChild(Ui.Wrap("lines.adviser.none", 14, Ui.Ink2));
        foreach (var p in m.Proposals)
        {
            var col = Col();
            bool open = p.Kind == ProposalKind.Open;
            col.AddChild(Ui.Wrap(open ? Loc.T("lines.prop.open", p.Design + 1, p.Gen, Loc.N(p.Rate, 1)) : Loc.T("lines.prop.close", p.Design + 1, p.Gen), 16, Ui.Ink));
            col.AddChild(Ui.Wrap(Loc.T(p.ReasonKey), 14, Ui.Ink2));
            if (open && p.MonthlyMoney != "") col.AddChild(Ui.Wrap(Loc.T("lines.prop.cost", p.MonthlyMoney, Loc.T("lever.money.suffix")), 14, Ui.Ink));
            if (!open) col.AddChild(Ui.Wrap(Loc.T("design.close.q", p.Design + 1), 14, Ui.Ink2));
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10);
            var src = p.Source; string kind = open ? "cmd.lineopen" : "cmd.lineclose"; int d = p.Design + 1;
            row.AddChild(Act("lines.accept", () => _host.Submit(src.Accept(Player), kind, d), true, p.AcceptDisabledKey == ""));
            row.AddChild(Act(Loc.T("lines.refuse", RefuseMonths), () => _host.Submit(src.Refuse(Player), "cmd.refuse", d)));
            col.AddChild(row);
            if (p.AcceptDisabledKey != "") col.AddChild(Ui.Wrap(Loc.T("inst.build.cant", Loc.T(p.AcceptDisabledKey, p.AcceptDisabledArg)), 16, Ui.Ink));
            _lineBody.AddChild(Card(col, true));
        }
        // the lines of the power
        _lineBody.AddChild(new HSeparator());
        _lineBody.AddChild(Ui.Head("lines.list"));
        if (m.Lines.IsEmpty) _lineBody.AddChild(Ui.Wrap("design.none", 14, Ui.Ink2));
        foreach (var l in m.Lines)
        {
            var col = Col();
            string text = Loc.T("design.line", l.Design + 1, l.Gen, Loc.T("stage." + l.StageNo), l.UnitCost, l.ReadinessPct, Loc.N(l.InService, 0))
                + (l.StageNo == (int)DesignStage.Series ? Loc.T("design.series", Loc.N(l.Rate, 1), Loc.T(l.BindKey)) : "")
                + (l.LicensedFrom != "" ? " · " + Loc.T("lines.licensed", l.LicensedFrom) : "");
            col.AddChild(Ui.Wrap(text, 14, Ui.Ink));
            var h = l.Handle; int dn = l.Design + 1;
            if (_confirmLine == h)
            {
                col.AddChild(Ui.MarkedRow(Ui.Warn, Ui.Wrap(Loc.T(_confirmCancel ? "design.cancel.q" : "design.close.q", dn), 14)));
                var br = new HBoxContainer(); br.AddThemeConstantOverride("separation", 8);
                var keep = Act("design.close.no", () => { _confirmLine = null; _sigLines = ""; Refresh(); });
                br.AddChild(keep);
                bool cancel = _confirmCancel;
                br.AddChild(Act(cancel ? "ui.cancel" : "design.close.yes", () => { _confirmLine = null; _sigLines = ""; _host.Submit(cancel ? new CancelDesign(Player, h) : new CloseLine(Player, h), cancel ? "cmd.cancel" : "cmd.close", dn); Refresh(); }));
                col.AddChild(br);
                keep.CallDeferred(Control.MethodName.GrabFocus);   // the safe button first
            }
            else if (l.CanClose || l.CanCancel)
            {
                bool c = l.CanCancel;
                col.AddChild(Act(c ? "ui.cancel" : "ui.closeline", () => { _confirmLine = h; _confirmCancel = c; _sigLines = ""; Refresh(); }));
            }
            _lineBody.AddChild(Card(col));
        }
        _lineBody.AddChild(Ui.Wrap("lines.later", 14, Ui.Ink3));
    }

    // ---- licence market ----

    MarketModel BuildMarket(SliceView v)
    {
        var a = MyActor(v);
        var mk = a?.Market;
        if (mk != null && _shotOffer is { } so) mk = mk with { Offers = mk.Offers.Add(so) };
        var inp = v.Inputs?.Actors.FirstOrDefault(x => x.Actor == Player);
        return MarketWindowModel.Build(mk, inp?.Reserve, MoneyFormat.Current, MoneyMillion, SellerName);
    }

    string CircleWord(SaleCircle c) => Loc.T("licence.circle." + c);

    void FillMarket()
    {
        _mm = BuildMarket(_host.Snap.View);
        var m = _mm;
        string sig = Loc.Lang + "|" + _selOffer + "|" + _confirmBuy + "|" + m.Enabled + m.External + "|" + string.Join(";", m.Offers.Select(o => o.Seller + "." + o.Gen + o.Price + o.RefusalKey + o.WorldPrice))
            + "|" + string.Join(";", m.Own.Select(o => o.Gen + o.Price + o.Circle)) + "|" + string.Join(";", m.Held.Select(h => h.Seller + "." + h.Gen)) + "|" + m.IncomeYear + m.ReserveBalance + m.LastRefusalKey
            + "|" + string.Join(";", _ownM.Select(k => k.Key + "=" + k.Value)) + string.Join(";", _ownC.Select(k => k.Key + "=" + k.Value));
        if (sig == _sigMarket) return;
        _sigMarket = sig;
        foreach (var c in _marketBody.GetChildren()) { _marketBody.RemoveChild(c); c.QueueFree(); }
        if (!m.Enabled) { _marketBody.AddChild(Ui.Wrap("mkt.off", 16)); return; }
        if (!m.External) _marketBody.AddChild(Ui.MarkedRow(Ui.Warn, Ui.Wrap("mkt.notyours", 14)));
        if (m.ReserveBalance != "") _marketBody.AddChild(Ui.Lbl(Loc.T("res.balance", m.ReserveBalance), 16, null, FontKind.Draft));
        _marketBody.AddChild(Ui.Wrap(Loc.T("mkt.income", m.IncomeYear, m.IncomeCap, Loc.T("lever.money.suffix")), 14, Ui.Ink2));
        if (m.LastRefusalKey != "") _marketBody.AddChild(Ui.MarkedRow(Ui.Warn, Ui.Wrap(Loc.T("mkt.lastrefusal", m.LastRefusalSeller, m.LastRefusalGen, Loc.T(m.LastRefusalKey)), 14)));
        // offers of the sellers
        _marketBody.AddChild(Ui.Head("mkt.offers"));
        if (m.Offers.IsEmpty) _marketBody.AddChild(Ui.Wrap("mkt.offers.none", 14, Ui.Ink2));
        if (_selOffer < 0 || _selOffer >= m.Offers.Length) _selOffer = m.Offers.IsEmpty ? -1 : 0;
        for (int i = 0; i < m.Offers.Length; i++)
        {
            var o = m.Offers[i]; int k = i;
            string cmp = o.PremiumPct == 0 ? Loc.T("mkt.cmp.same") : Loc.T(o.PremiumPct > 0 ? "mkt.cmp.up" : "mkt.cmp.down", Math.Abs(o.PremiumPct));
            string world = o.FewOffers ? Loc.T("mkt.world.few") : Loc.T("mkt.world", o.WorldPrice);
            var b = Ui.Btn((i == _selOffer ? "■ " : "□ ") + Loc.T("mkt.offer", o.SellerName, o.Gen, o.Price, o.Reference, cmp, world, CircleWord(o.Circle)), () => { _selOffer = k; _confirmBuy = null; _sigMarket = ""; Refresh(); }, focusable: true);
            b.CustomMinimumSize = new Vector2(0, 32); b.Alignment = HorizontalAlignment.Left; b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _marketBody.AddChild(b);
        }
        if (_selOffer >= 0)
        {
            var o = m.Offers[_selOffer]; var col = Col();
            col.AddChild(Ui.Lbl(Loc.T("mkt.card", o.SellerName, o.Gen), 16, null, FontKind.Draft));
            col.AddChild(Ui.Wrap(Loc.T("mkt.card.gives"), 14));
            col.AddChild(Ui.Wrap(Loc.T("mkt.card.price", o.Price, o.Reference, Loc.T("lever.money.suffix")), 14, Ui.Ink));
            if (o.ReserveAfter != "") col.AddChild(Ui.Wrap(Loc.T("mkt.card.after", m.ReserveBalance, o.ReserveAfter), 14));
            bool can = o.CanBuy && m.External; int seller = o.Seller, gen = o.Gen;
            if (_confirmBuy is { } cb && cb.Seller == seller && cb.Gen == gen)
            {
                col.AddChild(Ui.MarkedRow(Ui.Warn, Ui.Wrap(Loc.T("mkt.confirm", o.Price, Loc.T("lever.money.suffix")), 14)));
                var br = new HBoxContainer(); br.AddThemeConstantOverride("separation", 8);
                var no = Act("design.close.no", () => { _confirmBuy = null; _sigMarket = ""; Refresh(); }); br.AddChild(no);
                br.AddChild(Act("mkt.buy", () => { _confirmBuy = null; _sigMarket = ""; _host.Submit(new BuyLicence(Player, seller, gen), "cmd.buy", gen); Refresh(); }, true));
                col.AddChild(br);
                no.CallDeferred(Control.MethodName.GrabFocus);
            }
            else col.AddChild(Act("mkt.buy", () => { _confirmBuy = (seller, gen); _sigMarket = ""; Refresh(); }, true, can));
            if (!can) col.AddChild(Ui.Wrap(Loc.T("inst.build.cant", o.RefusalKey != "" ? Loc.T(o.RefusalKey) : Loc.T("mkt.notyours.short")), 16, Ui.Ink));
            _marketBody.AddChild(Card(col, true));
        }
        // what the power holds
        _marketBody.AddChild(new HSeparator());
        _marketBody.AddChild(Ui.Head("mkt.held"));
        if (m.Held.IsEmpty) _marketBody.AddChild(Ui.Wrap("mkt.held.none", 14, Ui.Ink2));
        foreach (var h in m.Held) _marketBody.AddChild(Ui.Wrap(Loc.T("mkt.held.row", h.SellerName, h.Gen), 14));
        // own catalogue
        _marketBody.AddChild(new HSeparator());
        _marketBody.AddChild(Ui.Head("mkt.own"));
        if (m.Own.IsEmpty) _marketBody.AddChild(Ui.Wrap("mkt.own.none", 14, Ui.Ink2));
        foreach (var o in m.Own)
        {
            int gen = o.Gen;
            double em = _ownM.TryGetValue(gen, out var mm) ? mm : o.M;
            SaleCircle ec = _ownC.TryGetValue(gen, out var cc) ? cc : o.Circle;
            var col = Col();
            col.AddChild(Ui.Wrap(Loc.T("mkt.own.row", gen, o.Reference, o.Price, Loc.N(o.M, 1), CircleWord(o.Circle)), 14, Ui.Ink));
            var r1 = new HBoxContainer(); r1.AddThemeConstantOverride("separation", 8);
            r1.AddChild(Ui.Lbl(Loc.T("mkt.own.mult", Loc.N(em, 1), Loc.N(MarketWindowModel.MMin, 1), Loc.N(MarketWindowModel.MMax, 1)), 14));
            r1.AddChild(Act("−", () => { _ownM[gen] = MarketWindowModel.StepM(em, -1); _sigMarket = ""; Refresh(); }));
            r1.AddChild(Act("+", () => { _ownM[gen] = MarketWindowModel.StepM(em, 1); _sigMarket = ""; Refresh(); }));
            col.AddChild(r1);
            var r2 = new HBoxContainer(); r2.AddThemeConstantOverride("separation", 6);
            foreach (SaleCircle c in new[] { SaleCircle.Default, SaleCircle.Nobody, SaleCircle.Bloc, SaleCircle.Friends, SaleCircle.All })
            {
                SaleCircle cv = c;
                r2.AddChild(Act((ec == c ? "■ " : "□ ") + CircleWord(c), () => { _ownC[gen] = cv; _sigMarket = ""; Refresh(); }));
            }
            col.AddChild(r2);
            bool changed = Math.Abs(em - o.M) > 1e-9 || ec != o.Circle;
            col.AddChild(Act("mkt.offer.send", () => { _host.Submit(new OfferLicence(Player, gen, Fx6.FromRaw((long)Math.Round(em * 1_000_000)), ec), "cmd.offer", gen); }, true, changed && m.External));
            col.AddChild(Ui.Wrap("mkt.own.warn", 14, Ui.Ink2));
            _marketBody.AddChild(Card(col));
        }
    }

    /// <summary>Shot run: puts a fixture advice in the strip when the live state has none. Returns true when the state had a real one.</summary>
    public bool ShotProposal()
    {
        var a = MyActor(_host.Snap.View);
        if (a?.Decisions?.Proposals.IsDefaultOrEmpty == false) return true;
        var d = a?.Designs.FirstOrDefault(x => x.Stage is DesignStage.Prototype or DesignStage.InService) ?? a?.Designs.FirstOrDefault(x => x.Stage != DesignStage.None);
        _shotProposal = new ProposalView(ProposalKind.Open, d?.Design ?? 0, d?.Handle ?? new DesignHandle(0, 1), Fx6.FromRaw(6_000_000), LineCloseReason.None);
        _sigLines = ""; Refresh();
        return false;
    }

    /// <summary>Shot run: selects an offer to buy and opens the confirmation; a fixture offer stands in when the live catalogue is empty. Returns true when the offer is real.</summary>
    public bool ShotBuy()
    {
        var a = MyActor(_host.Snap.View);
        bool real = a?.Market?.Offers.IsDefaultOrEmpty == false;
        if (!real && a?.Market != null)
            _shotOffer = new CatalogueLineView(a.Actor == 0 ? 1 : 0, 2, Fx6.FromRaw(300_000), Fx6.FromRaw(1_300_000), Fx6.FromRaw(390_000), SaleCircle.All, BadLicence.None);
        _sigMarket = ""; Refresh();
        if (_mm != null && _mm.Offers.Length > 0) { _selOffer = 0; var o = _mm.Offers[0]; _confirmBuy = (o.Seller, o.Gen); _sigMarket = ""; Refresh(); }
        return real;
    }

    public void ShotClear() { _shotProposal = null; _shotOffer = null; _confirmBuy = null; _confirmLine = null; _sigLines = _sigMarket = ""; }

    /// <summary>Live assertion for the two tabs: the models run on the real view and agree with it (every refusal word has a key; Enabled follows the run).</summary>
    string LiveCheckLinesMarket(SliceView v)
    {
        var lm = BuildLines(v);
        var a = MyActor(v);
        if (a != null && !a.Designs.IsDefault && lm.Lines.Length != a.Designs.Count(d => d.Stage != DesignStage.None)) return "lines model disagrees with the designs of the view";
        var mm = BuildMarket(v);
        if (mm.Enabled != (a?.Market != null)) return "market model Enabled disagrees with the view";
        foreach (var o in mm.Offers) if (!o.CanBuy && o.RefusalKey == "") return "an offer that cannot be bought has no reason";
        return null;
    }
}
