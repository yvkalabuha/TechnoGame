namespace Singularity.Lab.Models.Slice;

/// <summary>The CSV tables of the slice models (slice-csv-format.md v0.5 §4, §5). The host prepends the column "run".</summary>
public static class SliceTables
{
    public static readonly MetricTableSpec Summary = new("summary",
        ["prof_USA", "prof_GBR", "prof_SUN", "prof_FRA", "prof_SWE", "lead", "lead_all", "lead_field", "hash_final", "hash_years"]);

    public static readonly MetricTableSpec ActorYear = new("actor_year",
        ["t", "actor", "key_G", "key_T", "key_N", "key_P", "fv", "fv_fleet", "volume", "budget", "eng_short_months",
         "kd_aero", "kd_prop", "kd_metal", "kd_chem", "kd_mech"]);

    public static readonly MetricTableSpec WhyNot = new("whynot",
        ["t", "actor", "target", "rank", "layer", "cause", "subject", "eta", "lever", "lever_valid", "closed_t", "cluster", "order", "unmet"]);

    public static readonly MetricTableSpec NodeTimes = new("node_times", ["actor", "node", "t_known", "t_first", "t_07", "t_mastered", "lic"]);

    public static readonly MetricTableSpec GenTimes = new("gen_times", ["actor", "gen", "t_req", "t_first_unit", "t_fielded", "cost_u100"]);

    public static readonly MetricTableSpec Requirements = new("requirements",
        ["req", "actor", "gen", "t", "n_variants", "fv_superior", "fv_producible"]);

    public static readonly MetricTableSpec Events = new("events", ["t", "kind", "actor", "other", "value"]);

    public static readonly MetricTableSpec PrcYear = new("prc_year", ["t", "actor", "node", "known", "admitted", "yield", "ucm", "licence_share"]);

    public static readonly MetricTableSpec A40 = new("a40",
        ["t", "actor", "design", "g_p", "profile", "preset", "branch", "cls", "t_stage_o", "t_launch", "force", "idle", "rep_before"]);

    public static readonly MetricTableSpec A40CMonths = new("a40_c_months",
        ["t_fire", "actor", "design", "t", "min_yield", "argp", "alloy_supply", "deficit", "t_a", "left", "w_after_n", "training"]);

    public static readonly MetricTableSpec ToolingLedger = new("tooling_ledger",
        ["t", "actor", "rest", "b_cap", "invest", "taken_rest", "taken_cap", "rest_unspent", "t_a", "rest_returned", "n_part",
         "tool_unmet_MET", "tool_unmet_CHEM", "tool_unmet_MECH", "tool_unmet_AERO", "pool_exh"]);

    public static readonly MetricTableSpec Lines = new("lines",
        ["t", "actor", "design", "g_p", "role", "y_min", "argp", "rate", "unit_cost", "idle", "close", "start", "close_rule", "close_core",
         "left_share", "w_after_n"]);

    public static readonly MetricTableSpec FleetCmd = new("fleet_cmd", ["t", "actor", "kind", "design", "gen_d", "gen_line", "count", "g_front", "line_bind"]);

    public static readonly MetricTableSpec Licences = new("licences", ["t", "seller", "buyer", "gen", "profile", "price", "t_end"]);

    public static readonly MetricTableSpec CalibSummary = new("calib_summary",
        ["prof_USA", "prof_GBR", "prof_SUN", "prof_FRA", "prof_SWE", "hash_final"]);

    public static readonly MetricTableSpec PrcMonth = new("prc_month", ["t", "actor", "node", "known", "admitted", "yield", "ucm", "v"]);

    public static readonly MetricTableSpec ActorMonth = new("actor_month",
        ["t", "actor", "budget", "proc_budget", "out_g1", "out_g2", "out_g3", "out_g4", "active_g1", "active_g2", "active_g3", "active_g4"]);

    /// <summary><c>supply_month</c> (§4.17, TASK-0123): workforce, alloy, line output and its limiter, unused tooling money; JetSlice only, always written.</summary>
    public static readonly MetricTableSpec SupplyMonth = new("supply_month",
        ["t", "actor", "budget", "proc_budget", "wf_eng", "wf_skilled", "alloy_cap", "alloy_supply", "eng_n", "eng_granted", "sk_n", "sk_granted",
         "lines", "rate_max", "rate_sum", "bind_rate", "bind_alloy", "bind_money", "tool_rest", "tool_unspent"]);

    /// <summary><c>JetSlice</c>: 15 tables (§4; <c>supply_month</c> since v0.5 r14).</summary>
    public static readonly MetricTableSpec[] Full =
        [Summary, ActorYear, WhyNot, NodeTimes, GenTimes, Requirements, Events, PrcYear, A40, A40CMonths, ToolingLedger, Lines, FleetCmd, Licences, SupplyMonth];

    /// <summary><c>JetSliceCalib</c>: 8 tables (§5).</summary>
    public static readonly MetricTableSpec[] Calibration = [CalibSummary, NodeTimes, GenTimes, PrcMonth, ActorMonth, A40, A40CMonths, ToolingLedger];

    /// <summary>Tables that keep only their header in <c>detail = lite</c> (§4).</summary>
    public static readonly string[] HeavyInLite = ["whynot", "prc_year", "node_times", "gen_times", "requirements", "tooling_ledger", "lines"];

    /// <summary>Files that must not exist in a calibration directory (spec §8.2 п. 2, format §5).</summary>
    public static readonly string[] CalibrationForbiddenFiles = ["summary.csv", "actor_year.csv", "whynot.csv", "requirements.csv"];
}
