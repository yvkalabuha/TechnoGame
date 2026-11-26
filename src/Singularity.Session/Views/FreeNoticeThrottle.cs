namespace Singularity.Session;

/// <summary>
/// S12: the notice «an institute is free» may pause the game at most once in 25 seconds of real time, besides the once-a-year rule of the simulation (<c>SlotScienceView.FreeNoticeMonth</c>).
/// The client asks it with the notice month of each new view and the real clock; a notice that arrives too early is dropped, not delayed. Not state: it never touches the simulation.
/// </summary>
public sealed class FreeNoticeThrottle(long minRealMilliseconds = FreeNoticeThrottle.DefaultMinMilliseconds)
{
    public const long DefaultMinMilliseconds = 25_000;

    private long _lastPassMs = long.MinValue;
    private long _lastNoticeMonth = -1;

    /// <summary>True when the notice of <paramref name="noticeMonth"/> (−1 = none) is a new one and enough real time has passed since the last one that paused.</summary>
    public bool ShouldPause(long noticeMonth, long realMilliseconds)
    {
        if (noticeMonth < 0 || noticeMonth == _lastNoticeMonth)
            return false;
        _lastNoticeMonth = noticeMonth;
        if (_lastPassMs != long.MinValue && realMilliseconds - _lastPassMs < minRealMilliseconds)
            return false;
        _lastPassMs = realMilliseconds;
        return true;
    }
}
