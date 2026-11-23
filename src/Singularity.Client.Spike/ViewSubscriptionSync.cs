using Singularity.Session;

namespace Singularity.Client.Spike;

/// <summary>
/// Which actors the client's view carries the node blocks of (ADR-0025 п. 4): the played one (the audio reads it) and the shown one (the tree reads it).
/// No Godot here, so it is tested. Simulation thread only, like the session it drives.
/// </summary>
public sealed class ViewSubscriptionSync
{
    int _player = -1, _shown = -1;

    /// <summary>True when the wanted actors differ from the ones the session was last subscribed to.</summary>
    public bool Stale(int player, int shown) => player != _player || shown != _shown;

    /// <summary>
    /// Subscribes the session to <paramref name="player"/> and <paramref name="shown"/> (a negative id = none) and rebuilds the current view at once, also while paused
    /// (no tick, the state and the hash do not change). Does nothing when already subscribed so. Returns <paramref name="shown"/>.
    /// </summary>
    public int Sync(SliceSession session, int player, int shown)
    {
        if (!Stale(player, shown)) return shown;
        System.Span<int> set = stackalloc int[2];
        int n = 0;
        if (player >= 0) set[n++] = player;
        if (shown >= 0 && shown != player) set[n++] = shown;
        session.SetViewSubscription(set[..n]);
        session.RebuildView();
        _player = player; _shown = shown;
        return shown;
    }
}
