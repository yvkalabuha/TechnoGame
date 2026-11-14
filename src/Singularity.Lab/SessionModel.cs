namespace Singularity.Lab;

/// <summary>What a Session model gets for one run: the point's parameters and the execution options of the run.</summary>
/// <param name="ManifestDirectory">Directory of the manifest; relative paths of parameters and the mod data are found from it.</param>
/// <param name="RecordedDataFiles">The run's data_files (provenance): a model checks the files it loads against them, so its data and data_hash agree.</param>
public sealed record SessionModelSetup(ParamSet Parameters, ulong Seed, int Threads, int ChunkSize, long Ticks, string ManifestDirectory,
    IReadOnlyList<DataFileHash>? RecordedDataFiles = null);

/// <summary>
/// A model that runs a whole world through <c>Singularity.Session</c> (ADR-0014): the session owns the tick runner and the state, the Lab only
/// steps it and reads its read views. Unlike <see cref="ILabModel"/> it has no table of its own, so the run and the golden hash are the session's.
/// </summary>
public interface ISessionModelFactory
{
    string Id { get; }

    string Description { get; }

    ISessionModel Create(SessionModelSetup setup);

    /// <summary>
    /// Absolute paths of the data files the model loads for a manifest in <paramref name="manifestDirectory"/>. The Lab hashes them into the
    /// standard <c>data_hash</c> / <c>data_files</c> of run.json and matrix.json next to the manifest's own <c>data</c>.
    /// </summary>
    IReadOnlyList<string> DataFiles(string manifestDirectory) => [];
}

/// <summary>One run of a Session model.</summary>
public interface ISessionModel
{
    /// <summary>CSV files of the model, all of them for every run (the matrix merge needs the same set). The host prepends "run".</summary>
    IReadOnlyList<MetricTableSpec> MetricTables { get; }

    /// <summary>Runs the manifest's ticks and writes the rows. A broken check of the output format throws: the run is failed, not partial.</summary>
    SessionRunResult Run(IMetricSink sink);
}

/// <summary>The end of a run: the session's state hash after the last non-empty tick, the final tick and extra fields for run.json.</summary>
public sealed record SessionRunResult(string GoldenHash, long FinalTick, IReadOnlyList<KeyValuePair<string, string>> Info);
