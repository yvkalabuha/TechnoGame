#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Singularity.Client.Spike;

/// <summary>
/// Godot-free registry of the shot run (TASK-0192): the groups of steps of the size matrix with the frames each one saves, and the frames of the first (legacy) stage.
/// --only chooses groups from here, so a group never runs unless one of its frames is wanted; a test reads Main.cs and fails when a frame is missing from the registry.
/// </summary>
public static class ShotRegistry
{
    public static readonly (string Id, string[] Bases)[] Groups =
    {
        ("menus_areas", new[] { "menu_ingame", "main_menu", "credits", "credits_end", "new_game_empty", "new_game_selected", "settings_video", "settings_sound", "settings_general", "settings_rollback", "alert_tint5", "area_finance", "area_forces", "area_science", "area_production" }),
        ("tech_s1", new[] { "tech_s1_overview", "tech_s1_card", "tech_s1_card_disabled", "tech_s1_queue", "tech_s1_path", "tech_s1_plan", "tech_s1_confirm60", "tech_s1_notice" }),
        ("goal_statement", new[] { "tech_tree_b" }),
        ("icons_all", new[] { "tree_zoom_icons_all" }),
        ("captions", new[] { "tree_caption_turbine", "tree_caption_jet_test", "tree_caption_frontier" }),
        ("keyboard_walk", new[] { "tree_key_tip", "tree_key_enter" }),
        ("year_rings", new[] { "tree_year_overview", "tree_year_step2", "tree_year_step3", "tree_year_step5", "tree_year_step6", "tree_year_menu", "tree_year_focus", "tree_year_focus_plus", "tree_year_focus_tab_b" }),
        ("rail_tree_journal", new[] { "rail_narrow_normal", "rail_narrow_badges", "rail_wide_normal", "rail_wide_badges", "tree_nolegend", "tree_legend_closed_zoom", "tree_legend", "tree_legend_sel", "journal" }),
    };

    /// <summary>Frames of the large-size run (BuildLargeShots), not part of the size matrix.</summary>
    public static readonly string[] LargeBases = { "map" };

    public static string[] Group(string id) => Groups.First(g => g.Id == id).Bases;

    /// <summary>Frames of the first stage (no size suffix), and samples of the frames whose names are built at run time.</summary>
    public static readonly string[] Legacy =
    {
        "alert_tint5_night", "alert_tint1", "apply_focus", "area_finance_150", "area_finance_en", "area_forces_en", "area_none", "credits_en", "credits_ru", "flask_science_live",
        "globe_panel", "globe_panel_en", "globe_panel_pending", "globe_panel_refused", "journal", "main_menu", "main_menu_en", "menu_ingame", "menu_ingame_150", "new_game_empty",
        "new_game_selected", "new_game_selected_en", "rail_badge_finance", "rail_focus", "rail_hover", "rail_press", "rail_wide_finance", "rail_wide_finance_en", "rail_wide_forces_en",
        "settings_general", "settings_general_en", "settings_rollback", "settings_sound", "settings_video", "settings_video_150", "signal_active_acked", "signal_active_new", "signal_cleared",
        "signal_fail_acked", "signal_fail_new", "signal_field", "signal_field_en", "tech_tree_a", "tech_tree_a_en", "tech_tree_b", "tech_tree_b_en", "time_day", "time_month", "time_pause",
        "time_week", "tree_legend", "tree_legend_closed", "tree_nodes_frontier_names", "tree_zoom_icons_card", "tree_zoom_nodes",
        "full_alloy_x_100", "full_live_science_USA_1945", "full_live_production_USA_1945", "full_live_production_normal_USA_1945", "textures_x",
    };

    /// <summary>The names of every size-matrix frame a group could save at the base size.</summary>
    public static IEnumerable<string> SizedNames(string sfx = "_1920x1080") => Groups.SelectMany(g => g.Bases).Concat(LargeBases).Select(b => b + sfx);

    /// <summary>The groups a filter selects at a size suffix, in the order they run.</summary>
    public static string[] Plan(ShotFilter? only, string sfx = "_1920x1080") =>
        Groups.Where(g => only == null || only.WantsGroup(sfx, g.Bases)).Select(g => g.Id).ToArray();
}
