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
/// <paramref name="Jitter"/>: pitch ±3% and gain ±1.5 dB per play (one file per cue). <paramref name="Source"/>: shared file name; <paramref name="Semitones"/>: fixed pitch shift (tempo clicks).
/// </summary>
public sealed record Cue(string Id, Prio Prio, string Class, double Seconds, double MinGap = 0, double GainDb = 0,
    bool Jitter = false, string? Source = null, int Semitones = 0)
{
    public string Bus => Prio <= Prio.P1 ? "Alert" : "UI";
    public string File => Source ?? Id;
}

public static class AudioCatalog
{
    public static readonly IReadOnlyList<Cue> All = new[]
    {
        new Cue("order_ok", Prio.P2, "order", 0.25, Jitter: true),
        new Cue("order_reject", Prio.P2, "reject", 0.19),
        new Cue("queue_tick", Prio.P3, "order", 0.08, Jitter: true),
        new Cue("order_done", Prio.P2, "order", 0.25),
        new Cue("signal_attention", Prio.P1, "signal", 0.9),
        new Cue("signal_alert", Prio.P0, "signal", 1.5),
        new Cue("signal_critical", Prio.P0, "signal", 1.5, MinGap: 60),
        new Cue("signal_clear", Prio.P1, "signal", 1.2),   // not played by the client since TASK-0140 (the file still ships)
        new Cue("node_discovered", Prio.P2, "discovery", 0.9),
        new Cue("breakthrough", Prio.P1, "discovery", 1.0),
        new Cue("line_commissioned", Prio.P1, "line", 1.5),
        new Cue("war_declared", Prio.P0, "war", 1.5),
        new Cue("launch_space", Prio.P1, "launch", 1.09),
        new Cue("launch_nuclear", Prio.P0, "launch", 1.8, MinGap: 5),
        new Cue("tempo_1", Prio.P2, "tempo", 0.25, Source: "tempo", Semitones: -1),
        new Cue("tempo_2", Prio.P2, "tempo", 0.25, Source: "tempo"),
        new Cue("tempo_3", Prio.P2, "tempo", 0.25, Source: "tempo", Semitones: 1),
        new Cue("pause", Prio.P2, "pause", 0.25),
        new Cue("unpause", Prio.P2, "pause", 0.25),
        new Cue("ui_tick", Prio.P3, "tick", 0.07, Jitter: true),
    };

    public static Cue Get(string id) => All.First(c => c.Id == id);
}

/// <summary>One sound to start. <paramref name="Evicted"/> are voices to stop first.</summary>
public sealed record Play(int Voice, Cue Cue, double GainDb, double Pitch, IReadOnlyList<int> Evicted)
{
    public string File => Cue.File;
}

/// <summary>Per-group rule (TASK-0140 audit §4): minimum interval and a cap per minute; <paramref name="PerKey"/> counts each key (a signal) on its own.</summary>
public readonly record struct Limit(string Group, double MinInterval, int PerMinute, bool PerKey = false);

/// <summary>
/// Fatigue rules for 4–10 hour sessions: aggregation window, per-group limits, a global fuse, voice budget, pitch jitter, ducking, slow adaptation.
/// Folded sounds are dropped (never made louder). All times are real-time seconds passed in.
/// </summary>
public sealed class AudioPlanner
{
    public const double Window = 0.4, TickWindow = 0.25, DuckDbValue = -4.0, DuckTail = 0.8;
    public const int MaxVoices = 3, FusePerMinute = 6, FuseDrops = 3;
    public const double FuseQuiet = 30, Minute = 60;
    public const double AdaptAfter = 90 * 60;

    static readonly Dictionary<string, Limit> Limits = new()
    {
        ["ui_tick"] = new("ui_tick", 0.25, 12),
        ["tempo_1"] = new("tempo", 0.4, 8), ["tempo_2"] = new("tempo", 0.4, 8), ["tempo_3"] = new("tempo", 0.4, 8),
        ["pause"] = new("tempo", 0.4, 8), ["unpause"] = new("tempo", 0.4, 8),
        ["order_ok"] = new("order", 0.6, 10), ["queue_tick"] = new("order", 0.6, 10),
        ["order_done"] = new("order_done", 1.5, 4),
        ["order_reject"] = new("order_reject", 1.0, 6, PerKey: true),
        ["node_discovered"] = new("node_discovered", 10, 4),
        ["breakthrough"] = new("breakthrough", 10, 3),
        ["line_commissioned"] = new("line_commissioned", 10, 3),
        ["signal_attention"] = new("signal_attention", 20, 3, PerKey: true),
        ["signal_critical"] = new("signal_critical", 60, 1),
    };

    sealed record Voice(int Id, Prio Prio, double End, double Start);

    readonly List<Voice> _voices = new();
    readonly Dictionary<string, double> _lastPlayed = new();      // by class: lookups only
    readonly Dictionary<string, double> _lastCue = new();
    readonly Dictionary<string, List<double>> _groupPlays = new(); // play times of the last minute per group (or group+key)
    readonly List<double> _fusePlays = new();                      // non-P0 plays of the last minute
    ulong _rng;
    int _nextVoice, _limitDrops;
    double _duckUntil = -1, _quietUntil = -1;
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

    /// <summary>Music and ambient dip only for P0 and a breakthrough (-4 dB), and 0.8 s after it; everything else does not duck.</summary>
    public double DuckDb(double now) => now < _duckUntil ? DuckDbValue : 0;

    // ui_tick has its own gap and cap and must not crowd real confirmations out of the fuse.
    static bool InFuse(Cue c) => c.Prio != Prio.P0 && c.Id != "ui_tick";

    static bool Ducks(Cue c) => c.Prio == Prio.P0 || c.Id == "breakthrough";

    static int CountSince(List<double> l, double now)
    {
        l.RemoveAll(t => now - t >= Minute);
        return l.Count;
    }

    /// <summary>Asks for a sound. <paramref name="key"/> separates repeated sounds of one group (a signal key, a refusal reason).</summary>
    public Play? Request(string cueId, double now, double extraGainDb = 0, string? key = null)
    {
        Touch(now);
        var cue = AudioCatalog.Get(cueId);
        bool p0 = cue.Prio == Prio.P0;
        // P0 is folded only against itself (the same cue), so a different alarm is never lost to a neighbour.
        string cls = p0 ? cue.Id : cue.Class;
        double gap = cue.Class == "tick" ? TickWindow : Window;

        if (cue.MinGap > 0 && _lastCue.TryGetValue(cue.Id, out var lc) && now - lc < cue.MinGap) return null;
        if (_lastPlayed.TryGetValue(cls, out var lp) && now - lp < gap) return null;

        if (!p0 && now < _quietUntil) return null;   // a quiet period drops everything, uncounted

        List<double>? plays = null;
        if (Limits.TryGetValue(cue.Id, out var lim))
        {
            string gk = lim.PerKey && key != null ? lim.Group + "|" + key : lim.Group;
            if (!_groupPlays.TryGetValue(gk, out plays)) _groupPlays[gk] = plays = new List<double>();
            int n = CountSince(plays, now);
            if (n > 0 && now - plays[n - 1] < lim.MinInterval) return null;
            if (n >= lim.PerMinute) { if (InFuse(cue)) CountDrop(now); return null; }
        }

        if (InFuse(cue) && CountSince(_fusePlays, now) >= FusePerMinute) { CountDrop(now); return null; }

        _voices.RemoveAll(v => v.End <= now);
        var evicted = new List<int>();
        if (_voices.Count >= MaxVoices)
        {
            var victim = _voices.OrderByDescending(v => (int)v.Prio).ThenBy(v => v.Start).First();
            if (p0 || cue.Prio < victim.Prio) { _voices.Remove(victim); evicted.Add(victim.Id); }
            else return null;
        }

        _lastPlayed[cls] = now; _lastCue[cue.Id] = now;
        plays?.Add(now);
        if (InFuse(cue)) _fusePlays.Add(now);
        if (!p0) _limitDrops = 0;
        int id = ++_nextVoice;
        double gain = cue.GainDb + extraGainDb + AdaptDb(cue.Prio);
        double pitch = PitchOf(cue);
        if (cue.Jitter) { pitch *= 1 + (Next(1001) / 1000.0 * 0.06 - 0.03); gain += Next(1001) / 1000.0 * 3.0 - 1.5; }
        // A lower pitch plays longer: the voice is held for the stretched length.
        _voices.Add(new Voice(id, cue.Prio, now + cue.Seconds / pitch, now));
        if (Ducks(cue)) _duckUntil = Math.Max(_duckUntil, now + cue.Seconds + DuckTail);
        return new Play(id, cue, gain, pitch, evicted);
    }

    /// <summary>The fixed pitch of a cue (tempo clicks), before jitter.</summary>
    public static double PitchOf(Cue cue) => cue.Semitones == 0 ? 1.0 : SemitoneRatio[cue.Semitones + 1];

    /// <summary>Ducks the music for a cue played outside the planner (the audio demo).</summary>
    public void DuckFor(Cue cue, double now) { if (Ducks(cue)) _duckUntil = Math.Max(_duckUntil, now + cue.Seconds + DuckTail); }

    static readonly double[] SemitoneRatio = { 0.943874, 1.0, 1.059463 };   // -1, 0, +1 semitone

    // A drop by a per-minute cap or the fuse; three in a row silence all non-P0 sounds for 30 s.
    void CountDrop(double now)
    {
        if (++_limitDrops < FuseDrops) return;
        _limitDrops = 0; _quietUntil = now + FuseQuiet;
    }
}

/// <summary>Order sounds (audit §2): paused = one soft tick, running = one confirmation; "done" only when it comes noticeably later, once for a whole queue.</summary>
public sealed class OrderSounds
{
    public const double DoneAfter = 1.5;
    double _acceptedAt = double.NaN;
    long _acceptedSeq = -1;

    // A command given on pause records no time: after unpause its application is not news, the queue_tick was enough.
    public string Accepted(double now, int speed, long seq)
    {
        _acceptedAt = speed == 0 ? double.NaN : now; _acceptedSeq = seq;
        return speed == 0 ? "queue_tick" : "order_ok";
    }

    /// <summary>Only the command whose accept time was recorded can be "done": another command's application does not borrow its time.</summary>
    public string? Applied(double now, int speed, long seq)
    {
        if (seq != _acceptedSeq || speed == 0 || double.IsNaN(_acceptedAt) || now - _acceptedAt < DoneAfter) return null;
        _acceptedAt = double.NaN;   // the rest of a queue applied together stays silent
        return "order_done";
    }
}

/// <summary>Speed changes (audit §2): the first reading after start or load is the baseline, not a click; program-set changes (game menu) are silent.</summary>
public sealed class SpeedSounds
{
    // Five speed steps, three tempo clicks: 3 days and 1 week share the middle pitch.
    static readonly string[] Tempo = { "", "tempo_1", "tempo_2", "tempo_2", "tempo_3" };
    bool _init;
    int _last, _quiet = -1;

    public void Rebase() { _init = false; _quiet = -1; }

    /// <summary>The program (not the player) is about to set <paramref name="target"/>, for example the game menu: that change stays silent.</summary>
    public void Quiet(int target) { if (!_init || target != _last) _quiet = target; }

    public string? Observe(int speed)
    {
        if (!_init) { _init = true; _last = speed; return null; }
        if (speed == _last) return null;
        int prev = _last; _last = speed;
        bool quiet = _quiet == speed; _quiet = -1;
        if (quiet) return null;
        if (speed == 0) return "pause";
        return prev == 0 ? "unpause" : Tempo[Math.Clamp(speed, 1, SpeedSteps.Max)];
    }
}

/// <summary>The opening tick answers a mouse action on a new object: not the keyboard, not the same object twice in a row.</summary>
public sealed class OpenTickGate
{
    int _last = -1;

    public bool Allow(int key, bool fromKeyboard)
    {
        bool same = key == _last;
        _last = key;
        return !fromKeyboard && !same;
    }

    /// <summary>An object was closed; only a really open one makes its next opening new.</summary>
    public void Closed(bool wasOpen) { if (wasOpen) _last = -1; }
}

/// <summary>Which world cues fire for one view change: a breakthrough already says it, so a new line in the same frame stays silent and a node does not sound over a breakthrough.</summary>
public static class WorldCues
{
    /// <summary>
    /// A new known node is the player's own discovery unless licences explain the growth. The licence flag is visible for one month only,
    /// so the total is compared: a licensed node raises both counts when it appears, and the later flag drop changes only the licensed count.
    /// Diffusion and espionage gains look like research in the view (no source field).
    /// </summary>
    public static bool NodeGain(int known, int baseKnown, int licensed, int baseLicensed)
        => known - baseKnown > Math.Max(0, licensed - baseLicensed);

    public static (bool Breakthrough, bool Node, bool Line) Pick(bool fieldedUp, bool knownUp, bool seriesUp)
        => (fieldedUp, !fieldedUp && knownUp, !fieldedUp && seriesUp);
}

public enum Bus { Music, Ambient, UI, Alert }

/// <summary>Slider values 0..1. Alerts can be turned down but not off (#298): the floor is 20% (-14 dB).</summary>
/// <summary>Effects a bus carries.</summary>
public enum BusEffect { LowPass, HardLimiter, Record }

/// <summary>Pure plan of the bus layout (Godot-free): which buses to create and which effects each carries. Used so a second director start adds nothing.</summary>
public static class AudioBusPlan
{
    public static readonly string[] Names = { "Music", "Ambient", "UI", "Alert" };

    /// <summary>Buses of <see cref="Names"/> missing from <paramref name="existing"/>, in creation order.</summary>
    public static IReadOnlyList<string> ToCreate(IEnumerable<string> existing)
    {
        var have = new HashSet<string>(existing, StringComparer.Ordinal);   // lookups only
        return Names.Where(n => !have.Contains(n)).ToArray();
    }

    /// <summary>Effects to install on Master given how many it already has: none on a second start, so the limiter and the recorder exist once and the recorder is reused.</summary>
    public static IReadOnlyList<BusEffect> MasterToInstall(int existingEffects) =>
        existingEffects > 0 ? Array.Empty<BusEffect>() : Effects("Master");

    /// <summary>Effects of a bus. Only UI (P2, P3) is low-passed; Alert (P0, P1) is never filtered.</summary>
    public static IReadOnlyList<BusEffect> Effects(string bus) => bus switch
    {
        "UI" => new[] { BusEffect.LowPass },
        "Master" => new[] { BusEffect.HardLimiter, BusEffect.Record },
        _ => Array.Empty<BusEffect>(),
    };
}

public static class AudioSettings
{
    public const double AlertFloor = 0.2;

    /// <summary>Low-pass cutoff of the UI bus (P2, P3): the muted character, owner decision #455. Alert (P0, P1) is never filtered.</summary>
    public const double UiCutoffHz = 3200;

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
