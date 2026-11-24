using System;
using System.IO;
using System.Linq;
using Singularity.Client.Spike;
using Xunit;

namespace Singularity.Client.Audio.Tests;

/// <summary>TASK-0136: node mark grammar, root mark shape, effects block, menu reasons, words banned from player text, label ink contrast.</summary>
public class TreeClarityTests
{
    static string[] UiCsv()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "Singularity.Client.Spike", "locale", "ui.csv"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllLines(Path.Combine(dir!.FullName, "src", "Singularity.Client.Spike", "locale", "ui.csv"));
    }

    static string[] Keys() => UiCsv().Select(l => l.Split(',')[0]).ToArray();

    [Fact]
    public void Mark_grammar_gives_every_state_its_own_stroke_and_the_unknown_one_is_not_a_dot()
    {
        var u = TreeClarity.Mark(MarkState.Unknown); var f = TreeClarity.Mark(MarkState.Frontier);
        var k = TreeClarity.Mark(MarkState.Known); var m = TreeClarity.Mark(MarkState.Mastered);
        Assert.False(u.Filled); Assert.False(u.Dashed); Assert.Equal(1f, u.StrokeDu);
        Assert.True(f.Dashed); Assert.True(f.Backing); Assert.Equal(1.5f, f.StrokeDu);
        Assert.Equal(2f, k.StrokeDu); Assert.False(k.Filled);
        Assert.True(m.Filled);
        Assert.Equal(4, new[] { u, f, k, m }.Distinct().Count());   // no two states share one drawing (A5)
        Assert.True(TreeClarity.MarkDu >= 16f);   // A2
    }

    [Fact]
    public void State_of_a_node_follows_known_frontier_mastered()
    {
        Assert.Equal(MarkState.Unknown, TreeClarity.StateOf(false, false, true));
        Assert.Equal(MarkState.Frontier, TreeClarity.StateOf(false, true, false));
        Assert.Equal(MarkState.Known, TreeClarity.StateOf(true, false, false));
        Assert.Equal(MarkState.Mastered, TreeClarity.StateOf(true, false, true));
    }

    [Fact]
    public void Legend_lists_every_state_once_and_each_has_text_in_the_locale()
    {
        Assert.Equal(Enum.GetValues<MarkState>().OrderBy(x => x), TreeClarity.LegendStates.OrderBy(x => x));
        var keys = Keys();
        foreach (var s in TreeClarity.LegendStates) Assert.Contains(TreeClarity.LegendKey(s), keys);
    }

    [Fact]
    public void Root_frame_has_a_cut_corner_so_it_differs_from_a_filled_square_in_grey()
    {
        var f = TreeClarity.RootFrame(16);
        Assert.Equal(5, f.Length);                      // a square has 4 corners; the cut makes 5 vertices
        Assert.DoesNotContain(f, p => p.X == 8 && p.Y == -8);   // the top-right corner is gone
        Assert.All(f, p => Assert.InRange(Math.Abs(p.X), 0, 8));
        double a = 0; for (int i = 0; i < f.Length; i++) { var p = f[i]; var q = f[(i + 1) % f.Length]; a += p.X * q.Y - q.X * p.Y; }
        Assert.True(Math.Abs(a) / 2 < 16 * 16 - 1);     // smaller than the full square
    }

    [Fact]
    public void Effects_block_shows_only_what_the_core_has()
    {
        Assert.Equal(new[] { TreeClarity.ParamLine.KnowledgeStep }, TreeClarity.ParamLines(false, false, false));
        Assert.Equal(new[] { TreeClarity.ParamLine.ProductGeneration }, TreeClarity.ParamLines(false, true, false));
        Assert.Equal(new[] { TreeClarity.ParamLine.Yield, TreeClarity.ParamLine.Capability }, TreeClarity.ParamLines(true, false, true));
    }

    [Fact]
    public void Menu_reasons_sit_on_the_items_that_cannot_work()
    {
        Assert.Null(TreeClarity.MenuWhy(TreeClarity.MenuItem.Continue, true));
        Assert.Equal("ui.menu.continue.why", TreeClarity.MenuWhy(TreeClarity.MenuItem.Continue, false));
        Assert.Equal("ui.menu.load.why", TreeClarity.MenuWhy(TreeClarity.MenuItem.Load, true));   // the client has no load path at all
        Assert.Equal("ui.menu.load.why", TreeClarity.MenuWhy(TreeClarity.MenuItem.Load, false));
        Assert.Equal("ui.menu.save.why.nogame", TreeClarity.MenuWhy(TreeClarity.MenuItem.Save, false));
        Assert.Equal("ui.menu.save.why.notready", TreeClarity.MenuWhy(TreeClarity.MenuItem.Save, true));
        foreach (var it in new[] { TreeClarity.MenuItem.NewGame, TreeClarity.MenuItem.Settings, TreeClarity.MenuItem.Credits, TreeClarity.MenuItem.ToMain, TreeClarity.MenuItem.Quit })
            Assert.Null(TreeClarity.MenuWhy(it, false));
        var keys = Keys();
        foreach (var it in Enum.GetValues<TreeClarity.MenuItem>())
            foreach (var inGame in new[] { false, true })
            {
                var k = TreeClarity.MenuWhy(it, inGame);
                if (k != null) Assert.Contains(k, keys);
            }
    }

    [Fact]
    public void Tree_inspector_reports_only_its_own_command()
    {
        Assert.True(TreeClarity.ShowsCommand("cmd.focus"));
        Assert.False(TreeClarity.ShowsCommand("cmd.budget"));   // "resources allocated" under the focus button was the budget command of another screen
        Assert.False(TreeClarity.ShowsCommand("cmd.close"));
    }

    [Theory]
    [InlineData("нужен API NodeEffects(node) → (параметр, прирост)")]
    [InlineData("принято, применено на тике.")]
    [InlineData("сроки P10–P90 — тоже макет")]
    [InlineData("в симуляции нет такого рычага")]
    [InlineData("v0.5b · client spike")]
    public void Forbidden_vocabulary_is_caught(string text) => Assert.Matches(TreeClarity.Forbidden, text);

    [Theory]
    [InlineData("Историческая дата: 1960")]
    [InlineData("Ресурсы не распределены: доли в сумме не дают 100 %.")]
    [InlineData("характеристика статистика")]   // "стик" inside a word is not a tick
    public void Ordinary_words_are_not_caught(string text) => Assert.DoesNotMatch(TreeClarity.Forbidden, text);

    [Fact]
    public void Tree_and_menu_texts_have_no_developer_vocabulary()
    {
        // The window of the pass: tree screen, its card and legend, the goal statement, the inspector command lines, the game menu. Menu keys menu.* are checked as well.
        string[] prefixes = { "ui.tree.", "card.", "inset.", "legend.", "tree.", "stmt.", "menu.", "ui.menu.", "cmd.", "why.", "cause.", "refusal." };
        var bad = UiCsv().Where(l => prefixes.Any(p => l.StartsWith(p, StringComparison.Ordinal)) && TreeClarity.Forbidden.IsMatch(l)).ToList();
        Assert.True(bad.Count == 0, "developer words in player text:\n" + string.Join("\n", bad));
    }

    [Fact]
    public void Legend_has_rows_for_the_goal_frame_and_the_selected_links_the_map_draws()
    {
        var keys = Keys();
        Assert.Contains("legend.goal", keys); Assert.Contains("legend.links", keys);
    }

    [Fact]
    public void Root_outline_is_built_at_side_one_so_that_it_scales_without_a_cache()
    {
        var f = TreeClarity.RootFrame(1f);
        Assert.All(f, p => Assert.InRange(Math.Abs(p.X), 0, 0.5001));
        Assert.Contains(f, p => p.X == -0.5f && p.Y == -0.5f);
    }

    [Fact]
    public void Removed_dev_only_keys_stay_removed()
    {
        var keys = Keys();
        Assert.DoesNotContain("inset.prio", keys);
        Assert.DoesNotContain("card.param.api", keys);
    }

    [Fact]
    public void Discipline_text_inks_hold_4_5_on_sheet_and_paper_at_the_darkest_texture()
    {
        foreach (var hex in Palette.C.Text)
            foreach (var bg in new[] { Palette.C.Sheet, Palette.C.Paper })
                Assert.True(ColorMath.Contrast(ColorMath.FromHex(hex), ColorMath.FromHex(bg, 0.94)) >= 4.5, $"{hex} on {bg} x0.94");
    }
}

public class LabelPriorityTests
{
    [Fact]
    public void Named_nodes_rank_first_so_every_other_label_yields_and_they_draw_on_top()
    {
        int named = LabelGeometry.Rank(true, false, false);
        Assert.True(named < LabelGeometry.Rank(false, true, true));   // even a route node that is also frontier
        Assert.True(LabelGeometry.Rank(false, true, false) < LabelGeometry.Rank(false, false, true));
        Assert.True(LabelGeometry.Rank(false, false, true) < LabelGeometry.Rank(false, false, false));
    }

    [Fact]
    public void Leader_that_runs_through_a_box_is_detected_and_one_that_passes_beside_it_is_not()
    {
        Assert.True(LabelGeometry.SegmentHitsRect(0, 0, 100, 0, 40, -5, 20, 10));
        Assert.False(LabelGeometry.SegmentHitsRect(0, 0, 100, 0, 40, 5, 20, 10));
        Assert.False(LabelGeometry.SegmentHitsRect(0, 0, 100, 0, 150, -5, 20, 10));   // beyond the end
    }
}
