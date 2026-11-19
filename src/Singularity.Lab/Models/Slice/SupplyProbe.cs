using Singularity.Core.Products;
using Singularity.Session;

namespace Singularity.Lab.Models.Slice;

/// <summary>
/// Reads of the world state that the read views do not carry (slice-csv-format.md §4.17, TASK-0123): the workforce pool and the alloy cap the
/// Industry step of month t reads, and the workforce requested and granted in that step. Every value is a pure getter of the Session's own world
/// (<c>IndustryInputsAdapter.WorkforceAvailable</c>, <c>AlloyCap</c>, <c>IndustrySystem.Requested</c>, <c>Granted</c>); nothing is recomputed
/// here and nothing is written. One probe per run (instance buffers, never static); called on the owner thread between ticks only.
/// </summary>
internal sealed class SupplyProbe
{
    private readonly SliceWorld _w;
    private readonly int[] _site;
    private readonly long[] _budget, _eng, _skilled, _alloyCap;
    private long _month = -1;

    public SupplyProbe(SliceSession s, int actors)
    {
        _w = s.World;
        _site = new int[actors];
        for (int a = 0; a < actors; a++)
        {
            // A27: a site is an actor. WorkforceAvailable gives the whole pool of the owner to each of its sites, so a second site would be ambiguous.
            ReadOnlySpan<int> sites = _w.IndustryLayout.SitesOf(a);
            if (sites.Length != 1)
                throw new SliceFormatException($"Actor {a} has {sites.Length} sites; supply_month needs exactly one home site (A27).");
            _site[a] = sites[0];
        }
        _budget = new long[actors];
        _eng = new long[actors];
        _skilled = new long[actors];
        _alloyCap = new long[actors];
    }

    /// <summary>Before the monthly tick of <paramref name="t"/>: the budget column holds budget(t), so these are the values the step of t reads.</summary>
    public void BeforeStep(long t)
    {
        for (int a = 0; a < _site.Length; a++)
        {
            int site = _site[a];
            _budget[a] = _w.Policy.Budget(a).Raw;
            _eng[a] = _w.IndustryInputs.WorkforceAvailable(site, ProductsLayout.EngineerCategory, t);
            _skilled[a] = _w.IndustryInputs.WorkforceAvailable(site, _w.IndustryLayout.SkilledCategory, t);
            _alloyCap[a] = _w.IndustryInputs.AlloyCap(site, t).Raw;
        }
        _month = t;
    }

    public long Month => _month;

    /// <summary>The budget the probe saw before the step: must equal <c>ActorInputsView.Budget</c> of the same month (timing guard).</summary>
    public long Budget(int a) => _budget[a];

    public long Engineers(int a) => _eng[a];

    public long Skilled(int a) => _skilled[a];

    public long AlloyCap(int a) => _alloyCap[a];

    /// <summary>After the monthly tick of t: Σ requested and Σ granted of the category over all requesters at the actor's site (step of t).</summary>
    public (long Requested, long Granted) Workforce(int a, bool skilled)
    {
        int site = _site[a];
        int category = skilled ? _w.IndustryLayout.SkilledCategory : ProductsLayout.EngineerCategory;
        long n = 0, g = 0;
        for (int q = 0; q < _w.IndustryLayout.Requesters; q++)
        {
            n = checked(n + _w.Industry.Requested(site, category, q));
            g = checked(g + _w.Industry.Granted(site, category, q));
        }
        return (n, g);
    }
}
