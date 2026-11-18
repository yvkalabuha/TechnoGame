#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Singularity.Client.Spike;

// Audio decisions without Godot (AUDIO_DIRECTION §2, §3, §7): which sound plays, at what gain, who is dropped. Time is passed in
// (seconds, real time), the random choice uses its own generator: nothing here touches the simulation or its determinism.

/// <summary>P0 always audible (alert bus); P1 important; P2 confirmations; P3 micro feedback.</summary>
public enum Prio { P0 = 0, P1 = 1, P2 = 2, P3 = 3 }

/// <summary>
/// A sound of the map. <paramref name="Class"/> is the aggregation class; <paramref name="Seconds"/> is the playback length (checked against the .ogg in tests).
/// <paramref name="Jitter"/>: pitch ±3% and gain ±1.5 dB per play (one file per cue). <paramref name="Source"/>: shared file name; <paramref name="Repeats"/>: clicks of one file.
/// </summary>
public sealed record Cue(string Id, Prio Prio, string Class, double Seconds, double MinGap = 0, double GainDb = 0,
    bool Jitter = false, string? Source = null, int Repeats = 1)
{
    public const double RepeatGap = 0.06;
    public string Bus => Prio <= Prio.P1 ? "Alert" : "UI";
    public string File => Source ?? Id;
}

public static class AudioCatalog
{
    public static readonly IReadOnlyList<Cue> All = new[]
    {
        new Cue("order_ok", Prio.P2, "order", 0.25, Jitter: true),
        new Cue("order_reject", Prio.P1, "reject", 0.19),
        new Cue("queue_tick", Prio.P3, "tick", 0.08, Jitter: true),
        new Cue("order_done", Prio.P2, "order", 0.25),
        new Cue("signal_attention", Prio.P1, "signal", 1.2),
        new Cue("signal_alert", Prio.P0, "signal", 1.5),
        new Cue("signal_critical", Prio.P0, "signal", 1.5, MinGap: 60),
        new Cue("signal_clear", Prio.P1, "signal", 1.2),
        new Cue("node_discovered", Prio.P1, "discovery", 1.2),
        new Cue("breakthrough", Prio.P1, "discovery", 1.15),
        new Cue("line_commissioned", Prio.P1, "line", 2.0, GainDb: 6),
        new Cue("war_declared", Prio.P0, "war", 1.5),
        new Cue("launch_space", Prio.P1, "launch", 1.09),
        new Cue("launch_nuclear", Prio.P0, "launch", 1.8, MinGap: 5),
        new Cue("tempo_1", Prio.P2, "tempo", 0.25, Source: "tempo"),
        new Cue("tempo_2", Prio.P2, "tempo", 0.31, Source: "tempo", Repeats: 2),
        new Cue("tempo_3", Prio.P2, "tempo", 0.37, Source: "tempo", Repeats: 3),
        new Cue("pause", Prio.P2, "pause", 0.25),
        new Cue("unpause", Prio.P2, "pause", 0.25),
        new Cue("ui_tick", Prio.P3, "tick", 0.08, Jitter: true),
    };

    public static Cue Get(string id) => All.First(c => c.Id == id);
}

/// <summary>One sound to start. <paramref name="Evicted"/> are voices to stop first. <paramref name="Collective"/> = several events folded into one, a step higher.</summary>
public sealed record Play(int Voice, Cue Cue, double GainDb, double Pitch, bool Collective, IReadOnlyList<int> Evicted)
{
    public string File => Cue.File;
}

/// <summary>Fatigue rules for 4–10 hour sessions: aggregation window, voice budget, pitch jitter, ducking, slow adaptation.</summary>
public sealed class AudioPlanner
{
    public const double Window = 0.4, TempoWindow = 0.15, CollectiveSpan = 2.0, DuckDbValue = -6.0, DuckTail = 0.4, ReminderDb = -6.0;
    public const int MaxVoices = 3, CollectiveCount = 3;
    public const double AdaptAfter = 90 * 60;

    sealed record Voice(int Id, Prio Prio, double End, double Start);

    readonly List<Voice> _voices = new();
    readonly Dictionary<string, double> _lastPlayed = new();      // by class or cue id: lookups only
    readonly Dictionary<string, double> _lastCollective = new();
    readonly Dictionary<string, List<double>> _history = new();
    readonly Dictionary<string, double> _lastCue = new();
    ulong _rng;
    int _nextVoice;
    double _duckUntil = -1;
    double _active;
    double _lastNow = double.NaN;

    public AudioPlanner(ulong seed = 1910) { _rng = seed * 0x9E3779B97F4A7C15UL + 1; }

    int Next(int n)
    {
        _rng ^= _rng >> 12; _rng ^= _rng << 25; _rng ^= _rng >> 27;
        return (int)(((_rng * 0x2545F4914F6CDD1DUL) >> 33) % (ulong)n);
    }

    /// <summary>Continuous play time in seconds (gaps over 5 minutes do not count as play).</summary>
    public double ActiveSeconds => _active;

    public void Touch(double now)
    {
        if (!double.IsNaN(_lastNow) && now - _lastNow < 300) _active += Math.Max(0, now - _lastNow);
        _lastNow = now;
    }

    /// <summary>Resets the hidden adaptation (settings button).</summary>
    public void ResetAdaptation() => _active = 0;

    /// <summary>Hidden adaptation after 90 minutes of continuous play: P3 -3 dB, P2 -1.5 dB.</summary>
    public double AdaptDb(Prio p) => _active < AdaptAfter ? 0 : p switch { Prio.P3 => -3.0, Prio.P2 => -1.5, _ => 0 };

    /// <summary>Music and ambient dip while a P0/P1 sound plays and 400 ms after it; P2/P3 do not duck.</summary>
    public double DuckDb(double now) => now < _duckUntil ? DuckDbValue : 0;

    public Play? Request(string cueId, double now, double extraGainDb = 0)
    {
        Touch(now);
        var cue = AudioCatalog.Get(cueId);
        // P0 is folded only against itself (the same cue), so a different alarm is never lost to a neighbour.
        string key = cue.Prio == Prio.P0 ? cue.Id : cue.Class;
        double gap = cue.Class == "tempo" ? TempoWindow : Window;

        if (cue.MinGap > 0 && _lastCue.TryGetValue(cue.Id, out var lc) && now - lc < cue.MinGap) return null;

        if (!_history.TryGetValue(key, out var h)) _history[key] = h = new List<double>();
        h.RemoveAll(t => now - t > CollectiveSpan);
        h.Add(now);
        bool collective = h.Count >= CollectiveCount && cue.Prio >= Prio.P2 && cue.Class != "tick"
            && (!_lastCollective.TryGetValue(key, out var lcol) || now - lcol > CollectiveSpan);
        if (!collective && _lastPlayed.TryGetValue(key, out var lp) && now - lp < gap) return null;

        _voices.RemoveAll(v => v.End <= now);
        var evicted = new List<int>();
        if (_voices.Count >= MaxVoices)
        {
            var victim = _voices.OrderByDescending(v => (int)v.Prio).ThenBy(v => v.Start).First();
            if (cue.Prio == Prio.P0 || cue.Prio < victim.Prio) { _voices.Remove(victim); evicted.Add(victim.Id); }
            else return null;
        }

        _lastPlayed[key] = now; _lastCue[cue.Id] = now;
        if (collective) _lastCollective[key] = now;
        int id = ++_nextVoice;
        _voices.Add(new Voice(id, cue.Prio, now + cue.Seconds, now));
        if (cue.Prio <= Prio.P1) _duckUntil = Math.Max(_duckUntil, now + cue.Seconds + DuckTail);
        double gain = cue.GainDb + extraGainDb + AdaptDb(cue.Prio) + (collective ? 2.0 : 0);
        double pitch = collective ? 1.122462 : 1.0;
        if (cue.Jitter) { pitch *= 1 + (Next(1001) / 1000.0 * 0.06 - 0.03); gain += Next(1001) / 1000.0 * 3.0 - 1.5; }
        return new Play(id, cue, gain, pitch, collective, evicted);
    }
}

/// <summary>A signal that stays raised is not repeated by sound; one quiet reminder comes after 60 s of real time.</summary>
public sealed class SignalReminder
{
    public const double After = 60;
    readonly Dictionary<string, (double At, bool Done)> _raised = new();
    readonly List<string> _order = new();

    public void Raise(string key, double now)
    {
        if (_raised.ContainsKey(key)) return;
        _raised[key] = (now, false); _order.Add(key);
    }

    public void Clear(string key)
    {
        _raised.Remove(key); _order.Remove(key);
    }

    /// <summary>Keys whose reminder is due now (each returned once, in raise order).</summary>
    public IReadOnlyList<string> Due(double now)
    {
        var res = new List<string>();
        foreach (var k in _order)
        {
            var s = _raised[k];
            if (s.Done || now - s.At < After) continue;
            _raised[k] = (s.At, true); res.Add(k);
        }
        return res;
    }
}

public enum Bus { Music, Ambient, UI, Alert }

/// <summary>Slider values 0..1. Alerts can be turned down but not off (#298): the floor is 20% (-14 dB).</summary>
public static class AudioSettings
{
    public const double AlertFloor = 0.2;

    public static double Clamp(Bus bus, double v)
    {
        v = Math.Clamp(v, 0, 1);
        return bus == Bus.Alert ? Math.Max(v, AlertFloor) : v;
    }

    public static double ToDb(Bus bus, double v)
    {
        v = Clamp(bus, v);
        return v < 0.001 ? -80 : 20 * Math.Log10(v);
    }
}

/// <summary>Music in sessions with pauses (#298): 3–5 minutes of tracks, then 30–90 s of silence (scaled by the amount setting).</summary>
public sealed class MusicScheduler
{
    public enum Kind { None, Start }
    public readonly record struct Step(Kind Kind, int Track);

    readonly double[] _length;
    ulong _rng;
    bool _playing;
    double _trackEnd, _sessionEnd, _silenceEnd;
    int _last = -1;

    /// <summary>0 = few (silence x2), 1 = normal, 2 = many (silence x0.5).</summary>
    public int Amount { get; set; } = 1;
    public bool Playing => _playing;

    public MusicScheduler(double[] trackSeconds, double firstStart = 8, ulong seed = 7)
    {
        _length = trackSeconds; _rng = seed * 0x9E3779B97F4A7C15UL + 3; _silenceEnd = firstStart;
    }

    double U(double a, double b)
    {
        _rng ^= _rng >> 12; _rng ^= _rng << 25; _rng ^= _rng >> 27;
        return a + (b - a) * (((_rng * 0x2545F4914F6CDD1DUL) >> 11) / (double)(1UL << 53));
    }

    public Step Update(double now)
    {
        if (_length.Length == 0) return default;
        if (_playing)
        {
            if (now < _trackEnd) return default;
            if (now >= _sessionEnd)
            {
                _playing = false;
                _silenceEnd = now + U(30, 90) * (Amount == 0 ? 2.0 : Amount == 2 ? 0.5 : 1.0);
                return default;
            }
            return StartTrack(now);
        }
        if (now < _silenceEnd) return default;
        _sessionEnd = now + U(180, 300);
        _playing = true;
        return StartTrack(now);
    }

    Step StartTrack(double now)
    {
        int t = _length.Length == 1 ? 0 : (_last + 1) % _length.Length;
        _last = t; _trackEnd = now + _length[t];
        return new Step(Kind.Start, t);
    }
}

/// <summary>Ambient bed starts once the camera has stood still for 2 s and then stays (fades are the player's job).</summary>
public sealed class AmbientGate
{
    public const double StillFor = 2.0;
    double _since;
    bool _on;

    public bool Update(double now, bool cameraMoved)
    {
        if (cameraMoved) _since = now;
        if (!_on && now - _since >= StillFor) _on = true;
        return _on;
    }
}
