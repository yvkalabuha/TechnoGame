using System;
using System.Collections.Generic;
using Godot;
using Singularity.Session;

namespace Singularity.Client.Spike;

/// <summary>
/// Maps simulation and UI events to sounds (AUDIO_DIRECTION §2): decisions are made by <see cref="AudioPlanner"/> (pure, tested);
/// this node only owns buses, players, ducking and music/ambient playback. Client layer: it never writes to the simulation.
/// Buses: Master (hard limiter, -1 dB) -> Music, Ambient, UI (P2, P3), Alert (P0, P1; never ducked, never muted).
/// </summary>
public sealed partial class AudioDirector : Node
{
    const string UiDir = "res://assets/audio/ui/";
    static readonly string[] MusicFiles = { "res://assets/audio/music/irradiated_skies.ogg", "res://assets/audio/music/led_twilight.ogg" };
    const string AmbientFile = "res://assets/audio/ambient/pale_window.ogg";

    readonly SimHost _host;
    readonly AudioPlanner _planner = new();
    readonly OrderSounds _orders = new();
    readonly SpeedSounds _speedSounds = new();
    readonly OpenTickGate _openGate = new();
    long _lastTick = -1; int _testVoice;
    readonly AmbientGate _gate = new();
    readonly Dictionary<string, AudioStream> _streams = new();
    readonly Dictionary<int, AudioStreamPlayer> _voices = new();
    readonly double[] _vol = { 0.8, 0.8, 0.9, 1.0 };           // Music, Ambient, UI, Alert (slider values)
    AudioStreamPlayer _music, _ambient;
    MusicScheduler _sched;
    AudioStream[] _tracks;
    double _musicFade, _ambFade;                                   // 0..1 fade-in progress of the players
    double _duckDb, _pauseDb;                                      // smoothed offsets of the Music and Ambient buses
    long _lastCmdSeq; CmdStatus _lastStatus; object _lastView;
    int _baseActor = -1, _baseKnown, _baseLicensed, _baseFielded, _baseSeries;
    Vector2 _lastCam; bool _camInit;
    AudioEffectRecord _rec;
    bool _muted;                                                   // headless or no audio device: the logic still runs, nothing is played
    public int Actor { get; set; }
    public bool Active => !_muted;
    public double Volume(Bus b) => _vol[(int)b];

    public AudioDirector(SimHost host) { _host = host; Name = "AudioDirector"; }

    public override void _Ready()
    {
        // Buses are created in code (no layout file to keep in sync).
        // The director is recreated on every game entry while the AudioServer keeps its buses: create each bus and effect once.
        var existing = new List<string>();
        for (int b = 0; b < AudioServer.BusCount; b++) existing.Add(AudioServer.GetBusName(b));
        foreach (var n in AudioBusPlan.ToCreate(existing))
        {
            AudioServer.AddBus(); int i = AudioServer.BusCount - 1;
            AudioServer.SetBusName(i, n); AudioServer.SetBusSend(i, "Master");
            foreach (var fx in AudioBusPlan.Effects(n))
                if (fx == BusEffect.LowPass)
                    AudioServer.AddBusEffect(i, new AudioEffectLowPassFilter { CutoffHz = (float)AudioSettings.UiCutoffHz, Resonance = 0.5f });
        }
        foreach (var fx in AudioBusPlan.MasterToInstall(AudioServer.GetBusEffectCount(0)))
            AudioServer.AddBusEffect(0, fx == BusEffect.HardLimiter ? new AudioEffectHardLimiter { CeilingDb = -1f } : new AudioEffectRecord());
        AudioEffectRecord rec = null;
        for (int e = 0; e < AudioServer.GetBusEffectCount(0); e++) if (AudioServer.GetBusEffect(0, e) is AudioEffectRecord r) rec = r;
        _rec = rec!;
        LoadSettings();

        _tracks = new AudioStream[MusicFiles.Length];
        var len = new double[MusicFiles.Length];
        for (int i = 0; i < MusicFiles.Length; i++) { _tracks[i] = GD.Load<AudioStream>(MusicFiles[i]); len[i] = _tracks[i].GetLength(); }
        _sched = new MusicScheduler(len) { Amount = _pendingAmount };
        _music = new AudioStreamPlayer { Bus = "Music" }; AddChild(_music);
        var amb = GD.Load<AudioStream>(AmbientFile);
        if (amb is AudioStreamOggVorbis o) o.Loop = true;
        _ambient = new AudioStreamPlayer { Bus = "Ambient", Stream = amb, VolumeDb = -80 }; AddChild(_ambient);
        ApplyBusVolumes(1);
        _muted = DisplayServer.GetName() == "headless";
    }

    // ---------- settings ----------
    // Settings sheet order: Master, Music, Ambient, UI, Sfx, Alert; Sfx has no bus of its own yet.
    static readonly int[] SheetIndex = { 1, 2, 3, 5 };
    /// <summary>Sets a bus volume. <paramref name="persist"/> false is for the settings sheet: it saves the file itself, only after «keep».</summary>
    public void SetVolume(Bus b, double v, bool persist = true)
    {
        _vol[(int)b] = AudioSettings.Clamp(b, v);
        Settings.Current.Volume[SheetIndex[(int)b]] = _vol[(int)b] * 100;
        if (!persist) return;
        SaveSettings(); Settings.Save();
    }
    public int MusicAmount { get => _sched.Amount; set { _sched.Amount = value; SaveSettings(); } }
    public void ResetFatigue() => _planner.ResetAdaptation();

    const string CfgPath = "user://audio.cfg";
    void LoadSettings()
    {
        var cf = new ConfigFile();
        bool have = cf.Load(CfgPath) == Error.Ok;
        if (Settings.Loaded) for (int i = 0; i < 4; i++) _vol[i] = AudioSettings.Clamp((Bus)i, Settings.Current.Volume[SheetIndex[i]] / 100.0);   // the settings sheet is authoritative
        else if (have) for (int i = 0; i < 4; i++) _vol[i] = AudioSettings.Clamp((Bus)i, (double)cf.GetValue("volume", ((Bus)i).ToString(), _vol[i]));
        if (have) _pendingAmount = (int)cf.GetValue("music", "amount", 1);
    }
    int _pendingAmount = 1;
    void SaveSettings()
    {
        if (Settings.ReadOnly) return;
        var cf = new ConfigFile();
        for (int i = 0; i < 4; i++) cf.SetValue("volume", ((Bus)i).ToString(), _vol[i]);
        cf.SetValue("music", "amount", _sched?.Amount ?? _pendingAmount);
        cf.Save(CfgPath);
    }

    void ApplyBusVolumes(double dt)
    {
        // Duck and pause offsets move smoothly (40 dB/s down, 20 dB/s up); Alert and UI are never touched by them.
        double duckT = _planner.DuckDb(Now());
        double pauseT = _host != null && _host.Speed == 0 ? -6 : 0;
        _duckDb = Approach(_duckDb, duckT, (duckT < _duckDb ? 40 : 20) * dt);
        _pauseDb = Approach(_pauseDb, pauseT, 30 * dt);
        SetBus("Music", AudioSettings.ToDb(Bus.Music, _vol[0]) + _duckDb + _pauseDb);
        SetBus("Ambient", AudioSettings.ToDb(Bus.Ambient, _vol[1]) + _duckDb + _pauseDb);
        SetBus("UI", AudioSettings.ToDb(Bus.UI, _vol[2]));
        SetBus("Alert", AudioSettings.ToDb(Bus.Alert, _vol[3]));
    }

    static double Approach(double v, double t, double step) => v < t ? Math.Min(t, v + step) : Math.Max(t, v - step);
    static void SetBus(string n, double db) => AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex(n), (float)db);
    static double Now() => Time.GetTicksMsec() / 1000.0;

    // ---------- cues ----------
    AudioStream Stream(string file)
    {
        if (!_streams.TryGetValue(file, out var s)) _streams[file] = s = GD.Load<AudioStream>(UiDir + file + ".ogg");
        return s;
    }

    /// <summary>Asks for a sound; the planner may drop, fold or replace it by the fatigue rules.</summary>
    public void Cue(string id, double extraGainDb = 0, string key = null)
    {
        var p = _planner.Request(id, Now(), extraGainDb, key);
        if (p == null || _muted) return;
        foreach (var v in p.Evicted) if (_voices.Remove(v, out var old)) { old.Stop(); old.QueueFree(); }
        var pl = Spawn(p);
        int voice = p.Voice;
        pl.Finished += () => { _voices.Remove(voice); pl.QueueFree(); };
        _voices[voice] = pl;
        pl.Play();
    }

    AudioStreamPlayer Spawn(Play p)
    {
        var pl = new AudioStreamPlayer { Stream = Stream(p.File), Bus = p.Cue.Bus, VolumeDb = (float)p.GainDb, PitchScale = (float)p.Pitch };
        AddChild(pl);
        return pl;
    }

    /// <summary>The program is about to set this speed (game menu open/close): no pause or tempo click for it.</summary>
    public void QuietSpeed(int target) => _speedSounds.Quiet(target);

    /// <summary>Settings test button and audio demo: plays the cue straight, outside the planner (no limits, nothing counted toward the fuse or quiet period), at its own pitch and with its ducking.</summary>
    public void TestCue(string id)
    {
        if (_muted) return;
        var cue = AudioCatalog.Get(id);
        _planner.DuckFor(cue, Now());
        var pl = Spawn(new Play(--_testVoice, cue, 0, AudioPlanner.PitchOf(cue), System.Array.Empty<int>()));
        pl.Finished += pl.QueueFree;
        pl.Play();
    }

    /// <summary>Opening tick for a mouse action on object <paramref name="key"/>; silent from the keyboard and for the same object twice.</summary>
    public void OpenTick(int key, bool fromKeyboard) { if (_openGate.Allow(key, fromKeyboard)) Cue("ui_tick"); }

    /// <summary>The opened object was closed: opening it again sounds.</summary>
    public void OpenClosed(bool wasOpen) => _openGate.Closed(wasOpen);

    /// <summary>Signal raised or cleared (from the signal field). A raised signal sounds once; clearing is visual only, there is no reminder.</summary>
    public void OnSignal(string key, bool raised)
    {
        if (!raised) return;
        if (key == "sig.fail") Cue("signal_critical");
        else if (key is "sig.alloy" or "sig.licence") Cue("signal_attention", 0, key);
        else return;
    }

    // ---------- per frame ----------
    public override void _Process(double delta)
    {
        if (_host == null || _sched == null) return;
        double now = Now();
        _planner.Touch(now);
        ObserveHost();
        ApplyBusVolumes(delta);
        if (_muted) return;
        RunMusic(now, delta);
    }

    public void CameraMoved(Vector2 cam)
    {
        _camMoved = !_camInit || (cam - _lastCam).LengthSquared() > 1e-6f;
        _lastCam = cam; _camInit = true;
    }
    bool _camMoved;

    void RunMusic(double now, double dt)
    {
        var step = _sched.Update(now);
        if (step.Kind == MusicScheduler.Kind.Start) { _music.Stream = _tracks[step.Track]; _musicFade = 0; _music.Play(); }
        _musicFade = Math.Min(1, _musicFade + dt / 4);
        _music.VolumeDb = (float)(-60 + 60 * Math.Sqrt(_musicFade));
        bool amb = _gate.Update(now, _camMoved);
        _camMoved = false;
        if (amb && !_ambient.Playing) { _ambFade = 0; _ambient.Play(); }
        _ambFade = Math.Min(1, _ambFade + dt / 3);
        _ambient.VolumeDb = (float)(-60 + 60 * Math.Sqrt(_ambFade));
    }

    /// <summary>Starts music and the bed at once (the audio demo), without the usual silence before the first session.</summary>
    public void ForceMusic()
    {
        if (_muted) return;
        _music.Stream = _tracks[0]; _musicFade = 0; _music.Play();
        _ambient.Play(); _ambFade = 0;
    }

    void ObserveHost()
    {
        var s = _host.Snap;
        if (s == null) return;
        int sp = _host.Speed;
        double now = Now();
        // A tick that went back is a loaded save: speed and world baselines start over, nothing sounds for it.
        if (s.Tick < _lastTick) { _speedSounds.Rebase(); _baseActor = -1; }
        _lastTick = s.Tick;
        var speedCue = _speedSounds.Observe(sp);
        if (speedCue != null) Cue(speedCue);
        if (s.CmdSeq != _lastCmdSeq)
        {
            _lastCmdSeq = s.CmdSeq;
            if (s.Last.Status == CmdStatus.Pending) Cue(_orders.Accepted(now, sp, s.CmdSeq));
            else if (s.Last.Status is CmdStatus.Rejected or CmdStatus.Error) Cue("order_reject", 0, s.Last.Reason);
        }
        if (s.Last.Status != _lastStatus)
        {
            if (_lastStatus == CmdStatus.Pending && s.Last.Status == CmdStatus.Applied)
            {
                var done = _orders.Applied(now, sp, s.CmdSeq);
                if (done != null) Cue(done);
            }
            _lastStatus = s.Last.Status;
        }
        if (!ReferenceEquals(s.View, _lastView)) { _lastView = s.View; ObserveWorld(s.View); }
    }

    /// <summary>Events of the played actor from the view: a node became known, a gen was fielded (breakthrough), a line went into series.</summary>
    void ObserveWorld(SliceView v)
    {
        if (Actor < 0 || Actor >= v.Actors.Length) return;
        var a = v.Actors[Actor];
        if (!a.Nodes.IsPresent) return;   // the played actor is always subscribed (SimHost.PlayerActor)
        int known = 0, licensed = 0, series = 0;
        foreach (var n in a.Nodes) if (n.Known) { known++; if (n.ByLicence) licensed++; }
        foreach (var d in a.Designs) if (d.Stage == Singularity.Core.Products.DesignStage.Series) series++;
        if (_baseActor == Actor)
        {
            var (bt, node, line) = WorldCues.Pick(a.FieldedGen > _baseFielded, WorldCues.NodeGain(known, _baseKnown, licensed, _baseLicensed), series > _baseSeries);
            if (bt) Cue("breakthrough");
            if (node) Cue("node_discovered");
            if (line) Cue("line_commissioned");
        }
        _baseActor = Actor; _baseKnown = known; _baseLicensed = licensed; _baseFielded = a.FieldedGen; _baseSeries = series;
    }

    // ---------- audio demo (--shots): a scripted event sequence recorded from the Master bus ----------
    // The demo plays each cue straight (TestCue, outside the planner), so the limits and the fuse cannot swallow the later ones.
    static readonly (double T, string Cue)[] Demo =
    {
        (0.3, "order_ok"), (0.9, "queue_tick"), (1.5, "order_done"), (2.3, "signal_attention"), (3.3, "signal_alert"),
        (4.2, "order_reject"), (4.9, "node_discovered"), (6.2, "breakthrough"), (7.7, "line_commissioned"),
        (8.7, "war_declared"), (9.9, "launch_space"), (11.6, "launch_nuclear"),
        (13.4, "tempo_1"), (13.8, "tempo_2"), (14.2, "tempo_3"), (14.7, "pause"), (15.2, "unpause"),
        (16.9, "signal_critical"),
    };
    public const double DemoSeconds = 19.0;
    double _demoT; int _demoI = -1;

    public void StartDemo()
    {
        _demoT = 0; _demoI = 0;
        ForceMusic();
        _rec.SetRecordingActive(true);
    }

    /// <summary>Advances the demo script; returns true while it runs.</summary>
    public bool StepDemo(double dt)
    {
        if (_demoI < 0) return false;
        _demoT += dt;
        while (_demoI < Demo.Length && _demoT >= Demo[_demoI].T) TestCue(Demo[_demoI++].Cue);
        return _demoT < DemoSeconds;
    }

    /// <summary>Saves the recording of the Master bus as WAV (empty when there is no audio device).</summary>
    public int SaveDemo(string path)
    {
        _demoI = -1;
        _rec.SetRecordingActive(false);
        var wav = _rec.GetRecording();
        if (wav == null) return 0;
        wav.SaveToWav(path);
        return wav.Data.Length;
    }
}
