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
        "slice: L1", "slice: L2", "slice: matrix", "slice: data_hash", "slice: data cache", "slice: a40.force", "slice: COEXIST_MONEY", "slice: LIC_END", "slice: eng_short_months",
        "slice: licence track", "slice: control tables", "slice: design capacity", "slice: calibration points", "slice: switches", "slice: stage manifests", "slice: C3 distance",
        "slice: H8", "slice: full run to 2030-12", "slice: BUDGET_RULE", "slice: supply_month",
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
        string? problem = matches[0].Body();
        Assert.True(problem is null, problem); // a message is never truncated, unlike Assert.Null's value dump
    }

    [Fact] public void Calibration_tables_have_no_forbidden_file_or_column() => Check(Prefixes[0]);
    [Fact] public void Calibration_run_writes_only_8_tables_and_rejects_forbidden_parameters() => Check(Prefixes[1]);
    [Fact] public void Parameters_reject_unknown_key_bad_value_and_LK() => Check(Prefixes[2]);
    [Fact] public void Lab_path_gives_Session_golden_seed1_month149() => Check(Prefixes[3]);
    [Fact] public void L1_two_runs_384_months_same_csv_and_hash() => Check(Prefixes[4]);
    [Fact] public void L2_threads_1_vs_4_same_csv_and_hash() => Check(Prefixes[5]);
    [Fact] public void Matrix_2x2x2_every_run_has_all_14_tables() => Check(Prefixes[6]);
    [Fact] public void Data_hash_and_data_files_cover_the_loaded_mod_files() => Check(Prefixes[7]);
    [Fact] public void Changed_mod_file_is_reloaded_and_stale_data_files_fail_the_run() => Check(Prefixes[8]);
    [Fact] public void A40_force_marks_FORCE_series_and_never_the_AI() => Check(Prefixes[9]);
    [Fact] public void Coexist_money_needs_zero_left_and_no_training_in_every_month() => Check(Prefixes[10]);
    [Fact] public void Licence_lapse_is_the_Active_change_and_gives_t_end_and_LIC_END() => Check(Prefixes[11]);
    [Fact] public void Eng_short_months_counts_one_bureau_not_any() => Check(Prefixes[12]);
    [Fact] public void Licence_track_replaces_lapses_and_resumes_without_a_purchase() => Check(Prefixes[13]);
    [Fact] public void Control_tables_file_is_read_hashed_and_bad_files_are_rejected() => Check(Prefixes[14]);
    [Fact] public void Design_capacity_counters_are_in_model_info() => Check(Prefixes[15]);
    [Fact] public void Calibration_points_of_every_stage_and_the_LC_LP_layers_run() => Check(Prefixes[16]);
    [Fact] public void Switches_change_the_run_of_seed_1() => Check(Prefixes[17]);
    [Fact] public void Stage_manifests_stay_valid_after_freezing_and_one_point_runs() => Check(Prefixes[18]);
    [Fact] public void C3_distance_is_never_rounded_to_zero() => Check(Prefixes[19]);
    [Fact] public void Control_layer_past_384_months_is_rejected_and_FULL_is_not() => Check(Prefixes[20]);
    [Fact] public void Full_run_to_2030_12_is_ok_with_1024_months_and_thread_invariant() => Check(Prefixes[21]);
    [Fact] public void Budget_rule_key_parses_the_four_rules_and_NOW_keeps_the_main_golden() => Check(Prefixes[22]);
    [Fact] public void Supply_month_pools_follow_the_same_month_budget_and_agree_with_lines_and_ledger() => Check(Prefixes[23]);
}
