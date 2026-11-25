using System.Globalization;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Session;

namespace Singularity.Lab.Models.Slice;

/// <summary>
/// TASK-0179 (licence-market-calibration.md): what the AI buys on the licence market, from whom, when and at what price. Key LIC_PROBE (needs the market on)
/// adds the table <c>market</c>: DEAL when a new licence record (buyer, seller, gen) appears in <see cref="LicenceMarketView.Held"/>, with the asking price of the
/// seller's catalogue line in the view before the month; LDESIGN when a project with <see cref="DesignView.LicensedFrom"/> first appears, LBUILT when it has first
/// built a unit; OFFER once a year (month 0, 12, …) for every catalogue line a buyer sees, with the refusal it would meet now. Reads views only (no world state).
/// </summary>
internal sealed class MarketProbe
{
    public static readonly MetricTableSpec Table = new("market",
        ["t", "kind", "buyer", "seller", "gen", "price", "ref", "m", "circle", "refusal", "buyer_profile", "buyer_fielded", "design"]);

    private readonly IMetricSink _sink;
    private readonly int _table;
    private readonly string[] _ids;
    private readonly HashSet<(int Buyer, int Seller, int Gen)> _held = [];
    private readonly HashSet<(int Actor, int Design, int Seller, int Gen)> _designs = [], _built = [];

    public MarketProbe(SliceSession s, IMetricSink sink, int table)
    {
        _sink = sink;
        _table = table;
        _ids = [.. s.Layout.ActorIds];
    }

    private static string I(long v) => v.ToString(CultureInfo.InvariantCulture);

    private string Id(int a) => a >= 0 && a < _ids.Length ? _ids[a] : "";

    /// <summary>One month: <paramref name="before"/> is the view the compute of month <paramref name="t"/> read, <paramref name="after"/> the view after the tick.</summary>
    public void Observe(SliceView before, SliceView after, long t)
    {
        for (int b = 0; b < after.Actors.Length; b++)
        {
            ActorView av = after.Actors[b];
            if (av.Market is not { } mk)
                continue;
            string prof = I((int)av.Profile + 1), fielded = I(av.FieldedGen);
            foreach (LicenceHeldView h in mk.Held)
            {
                if (!_held.Add((b, h.Seller, h.Gen)))
                    continue;
                CatalogueLineView? line = null;
                foreach (CatalogueLineView c in before.Actors[b].Market?.Offers ?? [])
                {
                    if (c.Seller == h.Seller && c.Gen == h.Gen)
                        line = c;
                }
                _sink.Row(_table, I(t), "DEAL", Id(b), Id(h.Seller), I(h.Gen), line is { } l ? I(l.Price.Raw) : "", line is { } l2 ? I(l2.Reference.Raw) : "",
                    line is { } l3 ? I(l3.M.Raw) : "", line is { } l4 ? l4.Circle.ToString() : "", I(h.Start), prof, fielded, "");
            }
            foreach (DesignView d in av.Designs)
            {
                if (d.LicensedFrom < 0 || d.Stage == DesignStage.None)
                    continue;
                var key = (b, d.Design, d.LicensedFrom, d.Gen);
                if (_designs.Add(key))
                    _sink.Row(_table, I(t), "LDESIGN", Id(b), Id(d.LicensedFrom), I(d.Gen), "", "", "", "", d.Stage.ToString(), prof, fielded, I(d.Design));
                if (d.BuiltAny && _built.Add(key))
                    _sink.Row(_table, I(t), "LBUILT", Id(b), Id(d.LicensedFrom), I(d.Gen), "", "", "", "", d.Stage.ToString(), prof, fielded, I(d.Design));
            }
            if (t % 12 != 0)
                continue;
            foreach (CatalogueLineView c in mk.Offers)
                _sink.Row(_table, I(t), "OFFER", Id(b), Id(c.Seller), I(c.Gen), I(c.Price.Raw), I(c.Reference.Raw), I(c.M.Raw), c.Circle.ToString(), c.Refusal.ToString(),
                    prof, fielded, "");
        }
    }
}
