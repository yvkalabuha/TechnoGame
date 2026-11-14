using Singularity.Lab.Models.Slice;

namespace Singularity.Lab.Tests;

/// <summary>
/// JetSlice / JetSliceCalib checks (TASK-0044, T10). The bodies live in <see cref="SliceChecks"/> and are shared with
/// <c>Singularity.Lab selftest</c>; each returns null or the problem. One class, so xunit runs them one after another.
/// </summary>
public sealed class SliceModelTests : IDisposable
{
    private static readonly string[] Prefixes =
    [
        "slice: JetSliceCalib tables", "slice: JetSliceCalib run", "slice: parameters", "slice: Lab path gives",
        "slice: L1", "slice: L2", "slice: matrix", "slice: licence lapse",
    ];

    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "sing-lab-tests-" + Guid.NewGuid().ToString("N"));

    public SliceModelTests() => Directory.CreateDirectory(_tmp);

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch (IOException) { }
    }

    private void Check(string prefix)
    {
        var matches = SliceChecks.All(_tmp).Where(c => c.Name.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        Assert.Single(matches);
        Assert.Null(matches[0].Body());
    }

    [Fact] public void Calibration_tables_have_no_forbidden_file_or_column() => Check(Prefixes[0]);
    [Fact] public void Calibration_run_writes_only_8_tables_and_rejects_forbidden_parameters() => Check(Prefixes[1]);
    [Fact] public void Parameters_reject_unknown_key_bad_value_and_LK() => Check(Prefixes[2]);
    [Fact] public void Lab_path_gives_Session_golden_seed1_month149() => Check(Prefixes[3]);
    [Fact] public void L1_two_runs_384_months_same_csv_and_hash() => Check(Prefixes[4]);
    [Fact] public void L2_threads_1_vs_4_same_csv_and_hash() => Check(Prefixes[5]);
    [Fact] public void Matrix_2x2x2_every_run_has_all_14_tables() => Check(Prefixes[6]);
    [Fact] public void Licence_lapse_tracking_is_reset_by_a_new_purchase() => Check(Prefixes[7]);
}
