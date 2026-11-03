using System.Collections.Immutable;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Knowledge;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Core.WhyNot;

/// <summary>
/// <c>why_not(actor, target)</c> (contract §6, F6): a pure read of the snapshot of the knowledge, capability and product layers
/// (state after month t−1 plus the inputs in force at month t). It writes nothing, draws no random number and keeps no state, so the
/// result does not depend on call order or thread count; the list is sorted by <see cref="Link.Compare"/> and holds every link, the
/// UI shows the first three and Lab reads all of them. It is not part of any layer's compute: Session hands the result out.
///
/// A site is the actor's first site (spec A27: one site per actor). The parts live in <c>WhyNotAnalyzer.*.cs</c>.
/// </summary>
public sealed partial class WhyNotAnalyzer
{
    /// <summary>Contract §6: a bottleneck process is a link when <c>yield &lt; 0.7</c> (or not admitted).</summary>
    public static readonly Fx6 LowYield = Fx6.FromRaw(700_000);

    private const int GateHorizon = 1200;

    private readonly KnowledgeGraph _kg;
    private readonly KnowledgeParams _kp;
    private readonly KnowledgeSystem _k;
    private readonly IndustrySystem _i;
    private readonly ProductsSystem _p;
    private readonly ProductsParams _pp;
    private readonly IWhyNotInputs _in;
    private readonly LeverRegistry _levers;
    private readonly IndustryLayout _il;
    private readonly ProductsLayout _pl;
    private readonly IndustryParams _ip;

    public WhyNotAnalyzer(KnowledgeGraph graph, KnowledgeParams knowledgeParams, KnowledgeSystem knowledge, IndustrySystem industry,
        ProductsSystem products, ProductsParams productsParams, IWhyNotInputs inputs, LeverRegistry levers)
    {
        _kg = graph;
        _kp = knowledgeParams;
        _k = knowledge;
        _i = industry;
        _p = products;
        _pp = productsParams;
        _in = inputs;
        _levers = levers;
        _il = industry.Layout;
        _pl = products.Layout;
        _ip = ((ICultureSnapshot)industry).Params;
    }

    /// <summary>All links of the target for <paramref name="actor"/> in month <paramref name="month"/>, sorted; empty when nothing stands in the way.</summary>
    public IReadOnlyList<Link> WhyNot(int actor, WhyNotTarget target, long month)
    {
        var links = new List<Link>();
        switch (target.Kind)
        {
            case WhyNotTargetKind.Design:
                if (target.Id < 0 || target.Id >= _pl.Designs)
                    throw new ArgumentOutOfRangeException(nameof(target), target.Id, "No such design slot.");
                ExplainDesign(links, actor, target.Id, month);
                break;
            case WhyNotTargetKind.App:
                int gen = _pl.GenApp.IndexOf(target.Id) + 1;
                if (gen < 1)
                    throw new ArgumentException($"Node {target.Id} is not the APP node of a generation.", nameof(target));
                ExplainRequirement(links, actor, gen, month);
                break;
            default:
                if (target.Id < 1 || target.Id > _pl.Generations)
                    throw new ArgumentOutOfRangeException(nameof(target), target.Id, "Generation out of range.");
                ExplainRequirement(links, actor, target.Id, month);
                break;
        }
        links.Sort(Link.Compare);
        return links;
    }

    /// <summary>
    /// The yearly target of spec §5.6: the requirement of the newest APP the actor knows and has not fielded (no unit of a project of that
    /// generation in service); null if there is none.
    /// </summary>
    public WhyNotTarget? CurrentRequirement(int actor)
    {
        for (int g = _pl.Generations; g >= 1; g--)
        {
            if (!Knows(actor, _pl.GenApp[g - 1]))
                continue;
            bool fielded = false;
            for (int d = 0; d < _pl.Designs && !fielded; d++)
                fielded = _p.PlatformGen(actor, d) == g && _p.Stage(actor, d) is DesignStage.Series or DesignStage.InService && _p.InService(actor, d) > Fx6.Zero;
            return fielded ? null : WhyNotTarget.Requirement(g);
        }
        return null;
    }

    private bool Knows(int actor, int nodeNumber)
    {
        int row = _kg.RowOf(nodeNumber);
        return row >= 0 && _k.IsKnown(actor, row);
    }

    private int SiteOf(int actor)
    {
        for (int s = 0; s < _il.SiteCount; s++)
        {
            if (_il.SiteOwner[s] == actor)
                return s;
        }
        throw new ArgumentException($"Actor {actor} has no site.", nameof(actor));
    }

    private ImmutableArray<Lever> Offer(params Lever[] wanted)
    {
        var list = ImmutableArray.CreateBuilder<Lever>();
        foreach (Lever l in wanted)
        {
            if (_levers.IsEnabled(l) && !list.Contains(l))
                list.Add(l);
        }
        return list.ToImmutable();
    }

    /// <summary><c>ceil((need − have)/rate)</c> in months; 0 if already reached, null without a positive rate (contract §6).</summary>
    private static int? Eta(Fx6 have, Fx6 need, Fx6 rate)
    {
        if (have >= need)
            return 0;
        if (rate <= Fx6.Zero)
            return null;
        long months = ((need - have).Raw + rate.Raw - 1) / rate.Raw;
        return months > int.MaxValue ? null : (int)months;
    }

    private static Fx6 Max(Fx6 a, Fx6 b) => a > b ? a : b;

    private static Fx6 Min(Fx6 a, Fx6 b) => a < b ? a : b;

    // ---- targets

    private void ExplainRequirement(List<Link> links, int actor, int gen, long month)
    {
        int app = _pl.GenApp[gen - 1];
        if (!Knows(actor, app))
        {
            AddNearestMissing(links, actor, [_kg.RowOf(app)], WhyNotCause.MissingNode, month);
            return;
        }
        int best = -1;
        for (int d = 0; d < _pl.Designs; d++)
        {
            if (_p.PlatformGen(actor, d) != gen || _p.Stage(actor, d) is not (DesignStage.Project or DesignStage.Prototype or DesignStage.Series))
                continue;
            if (best < 0 || _p.Stage(actor, d) > _p.Stage(actor, best))
                best = d;
        }
        if (best >= 0)
        {
            ExplainDesign(links, actor, best, month);
            return;
        }

        // No project: the slots must have alternatives, and a hypothetical «superior» set shows what the capability would face.
        bool slotsOpen = true;
        for (int s = 0; s < _pl.SlotCount; s++)
        {
            if (!HasAvailable(actor, s, gen))
            {
                slotsOpen = false;
                AddSlotMissing(links, actor, s, gen, month);
            }
        }
        Fx6 hypothetical = Fx6.Zero;
        var alts = new int[_pl.SlotCount];
        if (slotsOpen && DesignRules.Superior(_pl, gen, n => Knows(actor, n), alts))
        {
            int site = SiteOf(actor);
            CapabilityLinks(links, actor, site, alts, gen, Fx6.Zero, month);
            hypothetical = MinYield(site, DesignRules.PrcSet(_pl, alts), month);
        }
        links.Add(new Link(WhyNotLayer.Product, WhyNotCause.NoDesign, gen, Fx6.Zero, Fx6.One, [], hypothetical, null));
    }

    private void ExplainDesign(List<Link> links, int actor, int design, long month)
    {
        // A withdrawn project (after a series or cancelled from П/О, contract §4.4e) and an empty slot give no links: for why_not they do not exist.
        DesignStage stage = _p.Stage(actor, design);
        if (stage is DesignStage.None or DesignStage.Withdrawn)
            return;
        int[] alts = _p.Alternatives(actor, design);
        int gen = _p.PlatformGen(actor, design);
        int site = SiteOf(actor);
        for (int s = 0; s < alts.Length; s++)
        {
            if (alts[s] >= 0 && !DesignRules.Available(_pl, alts[s], n => Knows(actor, n)))
                AddAltMissing(links, actor, alts[s], month);
        }
        int[] prc = _p.PrcSet(actor, design);
        CapabilityLinks(links, actor, site, alts, gen, _p.Rate(actor, design), month);
        ProductLinks(links, actor, site, design, stage, alts, prc, month);
    }
}
