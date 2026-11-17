using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Singularity.Core.Commands;
using Singularity.Core.Data;
using Singularity.Core.Policy;
using Singularity.Core.WhyNot;
using Singularity.Kernel;
using Singularity.Session;

namespace Singularity.Client.Spike;

/// <summary>What the UI thread reads: one immutable reference, replaced by the simulation thread (ADR-0014 п. 4).</summary>
public sealed record HostSnapshot(
    long Tick, CivilDate Date, SliceView View, int Speed,
    string LastVerdict, string LastOutcome, long Seq,
    ImmutableArray<BudgetShares?> SharesInForce,
    int WhyActor, WhyNotTarget? Target, IReadOnlyList<Link> Links, string WhyError, double LastTickMs);

/// <summary>
/// Runs SliceSession on a background thread. The UI thread never touches the session: it reads <see cref="Snap"/>, and sends
/// commands and why_not requests through a queue that the simulation thread drains between ticks.
/// </summary>
public sealed class SimHost : IDisposable
{
    // Ticks are days (Calendar.DateOf = start day + tick). Tempo #269: slow 1 day/s, medium ~1 week/s, max ~1 month/s.
    public static readonly double[] TicksPerSecond = { 0, 1, 7, 30 };

    readonly ConcurrentQueue<Action> _jobs = new();
    readonly Thread _thread;
    volatile bool _stop;
    int _speed;
    int _whyActor;
    volatile HostSnapshot _snap;
    volatile string _status = "Загрузка сценария…";
    volatile string _fatal;

    // Simulation-thread state
    SliceSession _session;
    int _localN;
    string _lastVerdict = "", _lastOutcome = "";
    long _seq;
    SliceView _lastWhyView;
    int _lastWhyActor = -1;
    WhyNotTarget? _target; IReadOnlyList<Link> _links = Array.Empty<Link>(); string _whyErr = "";
    ImmutableArray<BudgetShares?> _shares;
    double _lastTickMs;

    public GameData Data { get; private set; }
    public HostSnapshot Snap => _snap;
    public string Status => _status;
    public string Fatal => _fatal;
    public SliceLayoutView Layout { get; private set; }
    public bool Ready => _snap != null;

    public SimHost() { _thread = new Thread(Run) { IsBackground = true, Name = "sim" }; }
    public void Start() => _thread.Start();

    public int Speed { get => Volatile.Read(ref _speed); set => Volatile.Write(ref _speed, value); }
    public int WhyActor { get => Volatile.Read(ref _whyActor); set { Volatile.Write(ref _whyActor, value); } }

    /// <summary>Queues a command of the player (source class Player, id 0). Result shows in <see cref="HostSnapshot.LastVerdict"/>.</summary>
    public void Submit(SliceCommand command, string label)
    {
        _jobs.Enqueue(() =>
        {
            CommandVerdict v = _session.Submit(SourceClass.Player, 0, ++_localN, command);
            _lastVerdict = v.Accepted ? $"{label}: принято, применится на ближайшем тике" : $"{label}: отказ — {v.Reason}: {v.Message}";
            // While paused, one tick applies the command so that the next View shows it (the journal holds it either way).
            if (v.Accepted && Speed == 0) Advance(1);
            else Publish();
        });
    }

    /// <summary>Test hook: run <paramref name="ticks"/> ticks on the simulation thread regardless of tempo.</summary>
    public void StepTicks(long ticks) => _jobs.Enqueue(() => Advance(ticks));

    static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Singularity.sln"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Singularity.sln not found above the binaries");
    }

    void Run()
    {
        try
        {
            string root = Environment.GetEnvironmentVariable("SINGULARITY_ROOT") ?? RepoRoot();
            Data = ModLoader.Load(Path.Combine(root, "mods", "core"));
            _status = "Создание сессии…";
            _session = SliceSession.Create(Data, new SliceRunConfig { Seed = 7 });
            Layout = _session.Layout;
            var arr = ImmutableArray.CreateBuilder<BudgetShares?>(Layout.ActorIds.Length);
            for (int i = 0; i < Layout.ActorIds.Length; i++) arr.Add(null);
            _shares = arr.MoveToImmutable();
            Publish();
            var sw = Stopwatch.StartNew();
            double owed = 0, last = sw.Elapsed.TotalSeconds;
            while (!_stop)
            {
                while (_jobs.TryDequeue(out var job)) job();
                double now = sw.Elapsed.TotalSeconds, dt = Math.Min(now - last, 0.25);
                last = now;
                int sp = Speed;
                if (sp == 0) { owed = 0; }
                else
                {
                    owed += dt * TicksPerSecond[sp];
                    long n = (long)owed;
                    if (n > 0) { owed -= n; Advance(n); }
                }
                if (WhyActor != _lastWhyActor) Publish();
                Thread.Sleep(sp == 0 ? 20 : 8);
            }
        }
        catch (Exception e)
        {
            _fatal = e.ToString();
        }
    }

    void Advance(long ticks)
    {
        var sw = Stopwatch.StartNew();
        for (long i = 0; i < ticks && !_stop; i++)
        {
            _session.Step(1);
            if (_session.View != _lastWhyView) Publish();
        }
        _lastTickMs = ticks > 0 ? sw.Elapsed.TotalMilliseconds / ticks : 0;
        Publish();
    }

    void Publish()
    {
        SliceView v = _session.View;
        if (v.Inputs != null && !ReferenceEquals(v, _lastWhyView))
        {
            var b = _shares.ToBuilder();
            for (int i = 0; i < v.Inputs.Actors.Length; i++) b[v.Inputs.Actors[i].Actor] = v.Inputs.Actors[i].Shares;
            _shares = b.MoveToImmutable();
        }
        int wa = WhyActor;
        if (!ReferenceEquals(v, _lastWhyView) || wa != _lastWhyActor)
        {
            try
            {
                _target = _session.CurrentRequirement(wa);
                _links = _target is { } t ? _session.WhyNot(wa, t) : Array.Empty<Link>();
                _whyErr = "";
            }
            catch (Exception e) { _target = null; _links = Array.Empty<Link>(); _whyErr = e.Message; }
            _lastWhyView = v; _lastWhyActor = wa;
        }
        _lastOutcome = _session.Outcomes.Count > 0 ? _session.Outcomes[^1] : "";
        _snap = new HostSnapshot(_session.Tick, _session.Date, v, Speed, _lastVerdict, _lastOutcome, ++_seq, _shares, wa, _target, _links, _whyErr, _lastTickMs);
    }

    public void Dispose() { _stop = true; _thread.Join(2000); }
}
