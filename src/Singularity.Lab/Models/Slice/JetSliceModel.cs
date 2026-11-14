using System.Collections.Concurrent;
using System.Globalization;
using Singularity.Core.Data;
using Singularity.Kernel;
using Singularity.Session;

namespace Singularity.Lab.Models.Slice;

/// <summary>
/// The jet slice in the Lab (TASK-0044, T10): <c>JetSlice</c> (EXP-0002 series, slice-csv-format.md v0.5 §4) and <c>JetSliceCalib</c> (the
/// calibration build, §5). Both run the world through <see cref="SliceSession"/> and read it only through its read views (ADR-0014 п. 5–6); no
/// logic of the slice is repeated here. The calibration build runs the session in <see cref="SliceViewMode.Calibration"/>, so <c>TechKey</c>,
/// <c>FV</c> and <c>why_not</c> are not even computed (spec §8.2 п. 2).
/// </summary>
public sealed class JetSliceModelFactory(bool calibration) : ISessionModelFactory
{
    /// <summary>The version of the Session surface this model was written against (SLICE_COMPOSITION §13.7).</summary>
    public const int SurfaceVersion = 2;

    public string Id => calibration ? "JetSliceCalib" : "JetSlice";

    public string Description => calibration
        ? "Jet slice 1945-1977, calibration build (spec §8.2): node/generation times, PRC and actor months, A40, tooling ledger; no leadership."
        : "Jet slice 1945-1977 (spec v0.25), EXP-0002 tables of slice-csv-format.md v0.5.";

    public ISessionModel Create(SessionModelSetup setup)
    {
        if (SliceSession.TestApiVersion != SurfaceVersion)
            throw new InvalidOperationException($"The Session surface is v{SliceSession.TestApiVersion}, the slice models read v{SurfaceVersion}: update them first.");
        SliceParameters p = SliceParameters.Parse(setup.Parameters, setup.Seed, setup.Threads, setup.ChunkSize, calibration);
        GameData data = SliceData.Load(setup.ManifestDirectory);
        return new JetSliceModel(data, p, setup.Ticks, calibration);
    }

    /// <summary>The files <see cref="SliceData.Load"/> reads for this manifest: they go into the standard data_hash of the run.</summary>
    public IReadOnlyList<string> DataFiles(string manifestDirectory) => SliceData.Files(manifestDirectory);
}

/// <summary>The mod data of the slice, loaded once per process (a run is a process in a matrix).</summary>
public static class SliceData
{
    private static readonly ConcurrentDictionary<string, GameData> Cache = new(StringComparer.Ordinal);

    /// <summary><c>mods/core</c> of the repository that holds the manifest, or else of the Lab binary.</summary>
    public static GameData Load(string manifestDirectory)
    {
        string dir = FindMods(manifestDirectory) ?? FindMods(AppContext.BaseDirectory)
            ?? throw new InvalidOperationException($"mods/core not found above {manifestDirectory} or {AppContext.BaseDirectory}.");
        return Cache.GetOrAdd(dir, d => ModLoader.Load(d));
    }

    /// <summary>
    /// Absolute paths of the files behind <see cref="Load"/>, taken from the loaded mods themselves (<c>mod.yaml</c> and the files it lists,
    /// as <see cref="ModLoader"/> reads them), so the run's data_hash covers exactly the data the slice uses.
    /// </summary>
    public static IReadOnlyList<string> Files(string manifestDirectory)
    {
        var files = new List<string>();
        foreach (ModManifest mod in Load(manifestDirectory).Mods)
        {
            files.Add(Path.GetFullPath(Path.Combine(mod.Directory, ModManifest.FileName)));
            foreach (string f in mod.Files)
                files.Add(Path.GetFullPath(Path.Combine(mod.Directory, f)));
        }
        return files;
    }

    private static string? FindMods(string start)
    {
        for (var d = new DirectoryInfo(Path.GetFullPath(start)); d is not null; d = d.Parent)
        {
            string mods = Path.Combine(d.FullName, "mods", "core");
            if (File.Exists(Path.Combine(d.FullName, "Singularity.sln")) && Directory.Exists(mods))
                return mods;
        }
        return null;
    }
}

public sealed class JetSliceModel : ISessionModel
{
    private readonly GameData _data;
    private readonly SliceParameters _p;
    private readonly long _ticks;
    private readonly bool _calibration;

    internal JetSliceModel(GameData data, SliceParameters p, long ticks, bool calibration)
    {
        _data = data;
        _p = p;
        _ticks = ticks;
        _calibration = calibration;
        MetricTables = calibration ? SliceTables.Calibration : SliceTables.Full;
    }

    public IReadOnlyList<MetricTableSpec> MetricTables { get; }

    public SessionRunResult Run(IMetricSink sink)
    {
        SliceSession s = SliceSession.Create(_data, _p.Config);
        var observer = new SliceObserver(s, _data, _p.Config.ScenarioId, sink, MetricTables, _calibration, _p.Full);
        var calendar = new TickCalendar(s.Date);
        long months = 0;
        while (calendar.TickOfMonth(months) < _ticks)
            months++;
        ForceProxy? force = _p.ForceDivisor > 0 ? new ForceProxy(s, _p.ForceDivisor) : null;
        observer.Start(s.View);
        for (long t = 0; t < months; t++)
        {
            // The state the compute of month t reads (after any command tick of the FORCE proxy), then the monthly tick.
            s.StepTo(calendar.TickOfMonth(t));
            SliceView snapshot = s.View;
            observer.BeforeStep(t);
            s.Step(1);
            observer.Month(snapshot, s.View, t);
            force?.Issue(t);
        }
        s.StepTo(_ticks);
        observer.End(s.LastHash);
        var info = new List<KeyValuePair<string, string>>
        {
            new("test_api_version", SliceSession.TestApiVersion.ToString(CultureInfo.InvariantCulture)),
            new("data_hash", _data.DataHash),
            new("scenario_hash", _data.Scenario(_p.Config.ScenarioId).Hash),
            new("months", months.ToString(CultureInfo.InvariantCulture)),
        };
        if (force is not null)
            info.Add(new("force_sent", force.Sent.ToString(CultureInfo.InvariantCulture)));
        info.AddRange(observer.Counters());
        return new SessionRunResult(s.LastHash, s.Tick, info);
    }
}
