using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Singularity.Client.Spike;
using Xunit;

namespace Singularity.Client.Audio.Tests;

/// <summary>TASK-0149: speeds by owner #481 and no service identifiers in the Russian texts.</summary>
public class PaceAndLabelsTests
{
    [Fact]
    public void Speeds_are_pause_1_3_7_30_ticks_per_second_and_default_is_three_days()
    {
        Assert.Equal(new double[] { 0, 1, 3, 7, 30 }, SpeedSteps.TicksPerSecond);
        Assert.Equal(3, SpeedSteps.TicksPerSecond[SpeedSteps.Default]);
        Assert.Equal(SpeedSteps.Max + 1, SpeedSteps.Words.Length);
        Assert.Equal(4, SpeedSteps.Clamp(9)); Assert.Equal(0, SpeedSteps.Clamp(-1));
    }

    [Fact]
    public void Five_speed_steps_make_three_tempo_clicks_that_never_fall_in_pitch()
    {
        var s = new SpeedSounds(); s.Observe(1);
        var cues = new[] { 2, 3, 4 }.Select(x => s.Observe(x)).ToArray();
        Assert.Equal(new[] { "tempo_2", "tempo_2", "tempo_3" }, cues);
    }

    static string[] RuValues()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "Singularity.Client.Spike", "locale", "ui.csv"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllLines(Path.Combine(dir!.FullName, "src", "Singularity.Client.Spike", "locale", "ui.csv")).Skip(1).ToArray();
    }

    [Fact]
    public void Russian_texts_carry_no_number_sign_before_a_placeholder_and_no_G_codes()
    {
        // «№{0}» and «G{1}» were the service labels the owner could not read (M-play 2026-11-24).
        var bad = RuValues().Where(l => Regex.IsMatch(l, @"№\s*\{\d\}|\bG\{\d\}")).Select(l => l.Split(',')[0]).ToArray();
        Assert.Empty(bad);
    }

    [Fact]
    public void The_why_not_heading_says_what_blocks_and_the_draft_wording_is_gone()
    {
        var all = string.Join("\n", RuValues());
        Assert.Contains("ЧТО МЕШАЕТ ПОЛУЧИТЬ", all);
        Assert.DoesNotContain("ПОЧЕМУ НЕ", all);
        Assert.DoesNotContain("Черновик", all);
    }
}
