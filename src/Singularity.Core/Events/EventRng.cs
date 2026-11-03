using Singularity.Kernel;

namespace Singularity.Core.Events;

/// <summary>
/// RNG streams of the events (spec §9.1), source E: <c>EVT_DIS</c> (system 3, entity = actor; n = 0 onset, n = 1 duration) and
/// <c>EVT_LIC</c> (system 4, entity = <c>seller·8 + buyer</c>, n = 0). <c>tick</c> is the month index t, not the day tick (#60).
/// Only the key functions live here; the event rules arrive with T6d.
/// </summary>
public static class EventRng
{
    public const int SystemDisruption = 3;
    public const int SystemLicence = 4;

    public static ulong DisruptionOnset(CounterRng e, long month, int actorNumber) =>
        e.NextU64(month, SystemDisruption, actorNumber, 0);

    public static ulong DisruptionDuration(CounterRng e, long month, int actorNumber) =>
        e.NextU64(month, SystemDisruption, actorNumber, 1);

    public static ulong LicenceOffer(CounterRng e, long month, int sellerNumber, int buyerNumber) =>
        e.NextU64(month, SystemLicence, sellerNumber * 8 + buyerNumber, 0);
}
