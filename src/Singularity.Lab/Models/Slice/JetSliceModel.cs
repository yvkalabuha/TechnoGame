using System.Globalization;
using System.Security.Cryptography;
using Singularity.Core.Commands;
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
    public const int SurfaceVersion = 18;

    /// <summary>Horizon H of the slice, months (spec §2): the control tables have curve knots τ 0…2·H−1.</summary>
    public const int SliceHorizonMonths = 384;

    public string Id => calibration ? "JetSliceCalib" : "JetSlice";

    public string Description => calibration
        ? "Jet slice 1945-1977, calibration build (spec §8.2): node/generation times, PRC and actor months, A40, tooling ledger; no leadership."
        : "Jet slice 1945-1977 (spec v0.25), EXP-0002 tables of slice-csv-format.md v0.5.";

    public ISessionModel Create(SessionModelSetup setup)
    {
        if (SliceSession.TestApiVersion != SurfaceVersion)
            throw new InvalidOperationException($"The Session surface is v{SliceSession.TestApiVersion}, the slice models read v{SurfaceVersion}: update them first.");
        SliceParameters p = SliceParameters.Parse(setup.Parameters, setup.Seed, setup.Threads, setup.ChunkSize, calibration);
        GameData data = SliceData.Load(setup.ManifestDirectory, setup.RecordedDataFiles ?? []);
        CheckControlHorizon(p.Config.Layer, setup.Ticks, data.Scenario(p.Config.ScenarioId));
        CheckControlGenerations(p.Config.Layer, data);
        ControlTables? tables = null;
        if (p.ControlTablesPath is string rel)
        {
            // Relative to the manifest (format §3); its SHA-256 is control_tables_hash in run.json model_info.
            tables = ControlTables.Load(Path.GetFullPath(Path.Combine(setup.ManifestDirectory, rel)), data.Scenario(p.Config.ScenarioId), data.Nodes, SliceHorizonMonths);
            p = p with
            {
                Config = p.Config.Layer == LayerControl.Lk
                    ? p.Config with { LkTables = tables.Lk }
                    : p.Config with { CapabilityTables = new CapabilityControlTables(tables.Lc, tables.TDesign, tables.Rho) },
            };
        }
        return new JetSliceModel(data, p, setup.Ticks, calibration, tables?.Sha256);
    }

    /// <summary>Months a run of <paramref name="ticks"/> ticks steps, as <see cref="JetSliceModel.Run"/> counts them (month m runs if its first tick is before the end).</summary>
    internal static long MonthsOf(long ticks, ScenarioData scenario)
    {
        var calendar = new TickCalendar(new CivilDate(scenario.StartYear, scenario.StartMonth, 1));
        long months = 0;
        while (calendar.TickOfMonth(months) < ticks)
            months++;
        return months;
    }

    /// <summary>
    /// H8 (slice-horizon-2030.md §2.2, TASK-0121): the control layers L_K, L_K0, L_C0, L_C and L_P are calibration stands of 1945-1977; their
    /// tables and lags are defined on the horizon of <see cref="SliceHorizonMonths"/> months only. A run of such a layer past that horizon fails
    /// here, before any month runs, instead of reading curves past their last knot.
    /// </summary>
    internal static void CheckControlHorizon(LayerControl layer, long ticks, ScenarioData scenario)
    {
        if (layer == LayerControl.Full)
            return;
        // O(1): a run steps more than H months exactly when it ends after the first tick of month H (MonthsOf counts months m with
        // TickOfMonth(m) < ticks). No month walk, so a huge ticks value (the manifest allows up to long.MaxValue) is refused at once.
        long end = new TickCalendar(new CivilDate(scenario.StartYear, scenario.StartMonth, 1)).TickOfMonth(SliceHorizonMonths);
        if (ticks > end)
            throw new ParamException($"LAYER = {LayerName(layer)} is a control layer calibrated to the 1945-1977 horizon ({SliceHorizonMonths} months, "
                + $"ticks ≤ {end}); ticks = {ticks} runs past it. Use LAYER = FULL past month {SliceHorizonMonths - 1} (slice-horizon-2030.md H8).");
    }

    /// <summary>
    /// TASK-0131, O-038 (slice-horizon-2030.md H9): L_K, L_K0 and L_P know generations 1…4 only (the L_K schedule tables, the L_K0 lags and
    /// window middles, the L_P T_design/rho per generation, ControlTables.Generations). Data with a node or a design generation above 4 fails
    /// here, before any table is read or any month runs, instead of failing deep in a schedule or, worse, quietly skipping the new generations.
    /// </summary>
    internal static void CheckControlGenerations(LayerControl layer, GameData data)
    {
        if (layer is not (LayerControl.Lk or LayerControl.Lk0 or LayerControl.Lp))
            return;
        int nodeGen = 0;
        for (int r = 0; r < data.Nodes.Count; r++)
            nodeGen = Math.Max(nodeGen, data.Nodes.Gen[r]);
        int designGen = SliceTables.Generations(data);
        if (nodeGen > ControlTables.Generations || designGen > ControlTables.Generations)
            throw new ParamException($"LAYER = {LayerName(layer)} is a control layer of generations 1…{ControlTables.Generations}; the data has "
                + $"nodes up to generation {nodeGen} and designs up to generation {designGen}. Use LAYER = FULL with G5/G6 data "
                + "(O-038, slice-horizon-2030.md H9).");
    }

    private static string LayerName(LayerControl layer) => layer switch
    {
        LayerControl.Lk => "LK",
        LayerControl.Lk0 => "LK0",
        LayerControl.Lc0 => "LC0",
        LayerControl.Lc => "LC",
        LayerControl.Lp => "LP",
        _ => layer.ToString(),
    };

    /// <summary>The files <see cref="SliceData.Load"/> reads for this manifest: they go into the standard data_hash of the run.</summary>
    public IReadOnlyList<string> DataFiles(string manifestDirectory) => SliceData.Files(manifestDirectory);
}

/// <summary>
/// The mod data of the slice. A load is cached per <c>mods/core</c> directory together with the SHA-256 of every file it read; the cache is used
/// only while those files still have the same bytes, so a changed mod file is loaded again and the data never lags behind the files that
/// <see cref="Provenance.HashData"/> hashes into data_hash (review Codex r3, TASK-0044).
/// </summary>
public static class SliceData
{
    private sealed record Entry(GameData Data, string[] Files, string[] Sha256);

    private static readonly Dictionary<string, Entry> Cache = new(StringComparer.Ordinal);
    private static readonly Lock Gate = new();

    /// <summary><c>mods/core</c> of the repository that holds the manifest, or else of the Lab binary.</summary>
    public static GameData Load(string manifestDirectory) => Get(manifestDirectory).Data;

    /// <summary>
    /// Absolute paths of the files behind <see cref="Load"/>, taken from the loaded mods themselves (<c>mod.yaml</c> and the files it lists,
    /// as <see cref="ModLoader"/> reads them), so the run's data_hash covers exactly the data the slice uses.
    /// </summary>
    public static IReadOnlyList<string> Files(string manifestDirectory) => Get(manifestDirectory).Files;

    /// <summary>
    /// The data for a run whose provenance recorded <paramref name="recorded"/> (paths relative to the manifest directory, as in run.json
    /// <c>data_files</c>): every file the data was loaded from must be recorded with the SHA-256 of the bytes loaded, else the run fails — the
    /// data and data_hash can then not disagree, even if a file changed between the hashing and the load. An empty list records no claim
    /// (selftest provenance) and is not checked.
    /// </summary>
    public static GameData Load(string manifestDirectory, IReadOnlyList<DataFileHash> recorded)
    {
        Entry e = Get(manifestDirectory);
        if (recorded.Count == 0)
            return e.Data;
        for (int i = 0; i < e.Files.Length; i++)
        {
            string rel = Path.GetRelativePath(manifestDirectory, e.Files[i]).Replace('\\', '/');
            DataFileHash? r = recorded.FirstOrDefault(x => x.Path == rel);
            if (r is null)
                throw new InvalidOperationException($"The mod file {rel} the slice loaded is not in the run's data_files.");
            if (r.Sha256 != e.Sha256[i])
                throw new InvalidOperationException($"The mod file {rel} changed after data_hash was recorded (recorded {r.Sha256}, loaded {e.Sha256[i]}).");
        }
        return e.Data;
    }

    private static Entry Get(string manifestDirectory)
    {
        string dir = FindMods(manifestDirectory) ?? FindMods(AppContext.BaseDirectory)
            ?? throw new InvalidOperationException($"mods/core not found above {manifestDirectory} or {AppContext.BaseDirectory}.");
        lock (Gate)
        {
            if (Cache.TryGetValue(dir, out Entry? cached) && Unchanged(cached))
                return cached;
            // Load, hash, load and hash again: equal data and equal hashes ⇒ the bytes hashed are the bytes loaded (else try again).
            for (int attempt = 0; attempt < 3; attempt++)
            {
                GameData data = ModLoader.Load(dir);
                string[] files = [.. FilesOf(data)];
                string[] sha = [.. files.Select(Sha256)];
                GameData again = ModLoader.Load(dir);
                if (again.DataHash == data.DataHash && FilesOf(again).SequenceEqual(files, StringComparer.Ordinal)
                    && files.Select(Sha256).SequenceEqual(sha, StringComparer.Ordinal))
                {
                    var e = new Entry(data, files, sha);
                    Cache[dir] = e;
                    return e;
                }
            }
            throw new InvalidOperationException($"The mod files in {dir} keep changing while they are loaded.");
        }
    }

    private static bool Unchanged(Entry e)
    {
        for (int i = 0; i < e.Files.Length; i++)
        {
            if (!File.Exists(e.Files[i]) || Sha256(e.Files[i]) != e.Sha256[i])
                return false;
        }
        return true;
    }

    private static IEnumerable<string> FilesOf(GameData data)
    {
        foreach (ModManifest mod in data.Mods)
        {
            yield return Path.GetFullPath(Path.Combine(mod.Directory, ModManifest.FileName));
            foreach (string f in mod.Files)
                yield return Path.GetFullPath(Path.Combine(mod.Directory, f));
        }
    }

    private static string Sha256(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    internal static string? FindMods(string start)
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
    private readonly string? _tablesHash;

    internal JetSliceModel(GameData data, SliceParameters p, long ticks, bool calibration, string? tablesHash = null)
    {
        _tablesHash = tablesHash;
        _data = data;
        _p = p;
        _ticks = ticks;
        _calibration = calibration;
        MetricTables = calibration ? SliceTables.CalibrationFor(SliceTables.Generations(data)) : SliceTables.Full;
    }

    public IReadOnlyList<MetricTableSpec> MetricTables { get; }

    public SessionRunResult Run(IMetricSink sink)
    {
        SliceSession s = SliceSession.Create(_data, _p.Config);
        var observer = new SliceObserver(s, _data, _p.Config.ScenarioId, sink, MetricTables, _calibration, _p.Full,
            _p.Config.PilotForm, _p.Config.PilotProfiles);
        var calendar = new TickCalendar(s.Date);
        long months = 0;
        while (calendar.TickOfMonth(months) < _ticks)
            months++;
        ForceProxy? force = _p.ForceDivisor > 0 ? new ForceProxy(s, _p.ForceDivisor) : null;
        observer.Start(s.View);
        int logSeen = 0;
        for (long t = 0; t < months; t++)
        {
            // The state the compute of month t reads (after any command tick of the FORCE proxy), then the monthly tick.
            s.StepTo(calendar.TickOfMonth(t));
            SliceView snapshot = s.View;
            observer.BeforeStep(t);
            s.Step(1);
            observer.Month(snapshot, s.View, t);
            if (force is not null)
            {
                int before = force.Sent;
                force.Issue(t);
                if (force.Sent != before)
                    logSeen = ForcedStarts(s, observer, t, logSeen);
            }
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
        if (_tablesHash is not null)
            info.Add(new("control_tables_hash", _tablesHash));
        if (force is not null)
            info.Add(new("force_sent", force.Sent.ToString(CultureInfo.InvariantCulture)));
        info.AddRange(observer.Counters());
        return new SessionRunResult(s.LastHash, s.Tick, info);
    }

    // The forced starts the FORCE proxy has sent, read from the session's command log (the proxy keeps its set to itself): a40.force (format §4.9).
    // The log is append-only, one entry per line: the lines already handled are skipped without decoding (DEBT-0034). Called only in months
    // when the proxy sent something.
    private static int ForcedStarts(SliceSession s, SliceObserver observer, long month, int seen)
    {
        // Line 0 is the header {"commands_log":2} (command log v2, TASK-0115): counted, never decoded as a command.
        int n = 0;
        foreach (string line in s.CommandLog.Split('\n'))
        {
            if (line.Length == 0 || n++ < seen || n == 1)
                continue;
            if (CommandCodec.Decode(line).Command is StartSeries { Force: true } start)
                observer.ForcedStart(start.Actor, start.Design, month);
        }
        return n;
    }
}
