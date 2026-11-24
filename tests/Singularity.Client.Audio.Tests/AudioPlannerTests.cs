using Singularity.Client.Spike;

namespace Singularity.Client.Audio.Tests;

public class AudioPlannerTests
{
    [Fact]
    public void Same_class_within_the_window_plays_once()
    {
        // order_ok and order_done share the class "order" (the window); "order_done" is only the name of their limit group.
        Assert.Equal(AudioCatalog.Get("order_ok").Class, AudioCatalog.Get("order_done").Class);
        var p = new AudioPlanner();
        Assert.NotNull(p.Request("order_ok", 10.0));
        Assert.Null(p.Request("order_done", 10.2));       // same class "order", inside 400 ms
        Assert.NotNull(p.Request("order_done", 10.5));
    }

    [Fact]
    public void A_burst_is_folded_by_dropping_never_by_playing_louder_or_higher()
    {
        var p = new AudioPlanner();
        var first = p.Request("order_ok", 5.0);
        Assert.NotNull(first);
        Assert.Null(p.Request("order_ok", 5.2));
        Assert.Null(p.Request("order_ok", 5.4));
        var third = p.Request("order_ok", 5.7);   // past the 0.6 s interval
        Assert.NotNull(third);
        Assert.InRange(third!.Pitch, 0.97, 1.03);  // no collective pitch step
        Assert.InRange(third.GainDb, -1.5, 1.5);   // no collective +2 dB
    }

    [Fact]
    public void Voice_budget_is_three_and_p0_evicts_the_lowest_priority_voice()
    {
        var p = new AudioPlanner();
        Assert.NotNull(p.Request("line_commissioned", 1.0));   // P1
        Assert.NotNull(p.Request("breakthrough", 1.0));        // P1
        Assert.NotNull(p.Request("pause", 1.0));               // P2
        Assert.Null(p.Request("ui_tick", 1.1));                // P3 does not displace anything
        var alarm = p.Request("war_declared", 1.1);            // P0 evicts the P2 voice
        Assert.NotNull(alarm);
        Assert.Single(alarm!.Evicted);
    }

    [Fact]
    public void P0_is_never_lost_to_a_full_pool_of_important_voices()
    {
        var p = new AudioPlanner();
        p.Request("line_commissioned", 1.0); p.Request("breakthrough", 1.0); p.Request("launch_space", 1.0);
        var a = p.Request("signal_alert", 1.05);
        Assert.NotNull(a);
        Assert.Single(a!.Evicted);
    }

    [Fact]
    public void Voices_free_up_when_their_sound_ends()
    {
        var p = new AudioPlanner();
        p.Request("order_ok", 0.0); p.Request("pause", 0.0); p.Request("tempo_1", 0.0);
        Assert.NotNull(p.Request("order_reject", 1.0));        // all three ended
    }

    [Fact]
    public void Jittered_cues_vary_pitch_and_gain_within_bounds_and_others_do_not()
    {
        var p = new AudioPlanner();
        var pitches = new HashSet<double>();
        for (int i = 0; i < 200; i++)
        {
            var pl = p.Request("order_ok", i * 11.0);   // 11 s apart: inside the fuse
            Assert.NotNull(pl);
            Assert.InRange(pl!.Pitch, 0.97, 1.03);
            Assert.InRange(pl.GainDb, -1.5, 1.5);
            pitches.Add(pl!.Pitch);
        }
        Assert.True(pitches.Count > 50);
        Assert.Equal(1.0, new AudioPlanner().Request("order_reject", 0)!.Pitch);
    }

    [Fact]
    public void Tempo_is_one_click_whose_pitch_follows_the_speed_and_routine_successes_left_the_alert_bus()
    {
        Assert.Equal(Prio.P1, AudioCatalog.Get("signal_attention").Prio);
        Assert.Equal(new[] { 0.943874, 1.0, 1.059463 },
            new[] { "tempo_1", "tempo_2", "tempo_3" }.Select(c => new AudioPlanner().Request(c, 1.0)!.Pitch), new DoubleCmp());
        Assert.Equal(Prio.P2, AudioCatalog.Get("node_discovered").Prio);
        Assert.Equal(Prio.P2, AudioCatalog.Get("order_reject").Prio);
        Assert.Equal("UI", AudioCatalog.Get("order_reject").Bus);
    }

    sealed class DoubleCmp : IEqualityComparer<double>
    {
        public bool Equals(double a, double b) => Math.Abs(a - b) < 1e-5;
        public int GetHashCode(double v) => 0;
    }

    [Fact]
    public void Same_seed_gives_the_same_sequence()
    {
        var a = new AudioPlanner(5); var b = new AudioPlanner(5);
        for (int i = 0; i < 50; i++)
            Assert.Equal(a.Request("queue_tick", i * 1.0)?.File, b.Request("queue_tick", i * 1.0)?.File);
    }

    [Fact]
    public void Critical_and_nuclear_have_a_minimum_gap()
    {
        var p = new AudioPlanner();
        Assert.NotNull(p.Request("signal_critical", 0));
        Assert.Null(p.Request("signal_critical", 30));
        Assert.NotNull(p.Request("signal_critical", 61));
        Assert.NotNull(p.Request("launch_nuclear", 100));
        Assert.Null(p.Request("launch_nuclear", 103));
    }

    [Fact]
    public void Different_alarms_do_not_fold_into_each_other()
    {
        var p = new AudioPlanner();
        Assert.NotNull(p.Request("signal_attention", 1.0));
        Assert.NotNull(p.Request("war_declared", 1.1));
    }

    [Fact]
    public void Only_p0_and_breakthrough_duck_by_four_db_with_a_tail_of_eight_tenths()
    {
        var p = new AudioPlanner();
        p.Request("order_ok", 1.0);
        p.Request("order_reject", 2.0); p.Request("node_discovered", 3.0); p.Request("line_commissioned", 4.0); p.Request("signal_attention", 5.0);
        for (double t = 1.0; t < 7.0; t += 0.1) Assert.Equal(0, p.DuckDb(t));
        var s = AudioCatalog.Get("signal_alert");
        p.Request("signal_alert", 20.0);
        Assert.Equal(-4, p.DuckDb(20.0 + s.Seconds + 0.7));
        Assert.Equal(0, p.DuckDb(20.0 + s.Seconds + 0.9));
        var b = AudioCatalog.Get("breakthrough");
        p.Request("breakthrough", 40.0);
        Assert.Equal(-4, p.DuckDb(40.0 + b.Seconds + 0.7));
        Assert.Equal(0, p.DuckDb(40.0 + b.Seconds + 0.9));
    }

    [Fact]
    public void Ui_tick_has_a_quarter_second_gap_and_its_own_cap_of_twelve_a_minute()
    {
        var p = new AudioPlanner();
        Assert.NotNull(p.Request("ui_tick", 0.0));
        Assert.Null(p.Request("ui_tick", 0.2));
        Assert.NotNull(p.Request("ui_tick", 0.3));
        var q = new AudioPlanner();
        int played = 0;
        for (int k = 0; k < 40; k++) if (q.Request("ui_tick", k * 0.3) != null) played++;   // 12 s of clicks every 0.3 s
        Assert.Equal(12, played);                                                          // its own cap; the global fuse does not apply
    }

    [Fact]
    public void Order_and_tempo_classes_keep_their_minimum_intervals()
    {
        var p = new AudioPlanner();
        Assert.NotNull(p.Request("order_ok", 0.0));
        Assert.Null(p.Request("queue_tick", 0.5));
        Assert.NotNull(p.Request("queue_tick", 0.7));
        Assert.NotNull(p.Request("tempo_2", 10.0));
        Assert.Null(p.Request("pause", 10.3));
        Assert.NotNull(p.Request("pause", 10.45));
        Assert.NotNull(p.Request("order_done", 20.0));
        Assert.Null(p.Request("order_done", 21.0));      // inside 1.5 s
        Assert.NotNull(p.Request("order_done", 21.6));
    }

    [Fact]
    public void Reject_has_a_one_second_gap_per_reason_and_routine_successes_keep_ten_seconds()
    {
        var p = new AudioPlanner();
        Assert.NotNull(p.Request("order_reject", 0.0, 0, "Funds"));
        Assert.Null(p.Request("order_reject", 0.6, 0, "Funds"));
        Assert.NotNull(p.Request("order_reject", 0.9, 0, "Other"));   // another reason is its own group
        Assert.NotNull(p.Request("order_reject", 1.5, 0, "Funds"));
        Assert.NotNull(p.Request("node_discovered", 100.0));
        Assert.Null(p.Request("node_discovered", 105.0));
        Assert.NotNull(p.Request("node_discovered", 110.5));
        Assert.NotNull(p.Request("breakthrough", 200.0));
        Assert.Null(p.Request("breakthrough", 205.0));
        Assert.NotNull(p.Request("line_commissioned", 300.0));
        Assert.Null(p.Request("line_commissioned", 305.0));
    }

    [Fact]
    public void Signal_attention_has_twenty_seconds_per_key_and_no_gain_boost()
    {
        var p = new AudioPlanner();
        Assert.NotNull(p.Request("signal_attention", 0.0, 0, "sig.alloy"));
        Assert.Null(p.Request("signal_attention", 10.0, 0, "sig.alloy"));
        Assert.NotNull(p.Request("signal_attention", 10.5, 0, "sig.licence"));
        Assert.NotNull(p.Request("signal_attention", 21.0, 0, "sig.alloy"));
        Assert.Equal(0, new AudioPlanner().Request("line_commissioned", 1.0)!.GainDb);
    }

    [Fact]
    public void Per_minute_caps_apply_and_reopen_after_a_minute()
    {
        var p = new AudioPlanner(); int played = 0;
        for (int k = 0; k < 4; k++) if (p.Request("breakthrough", k * 11.0) != null) played++;   // 3 per minute, 11 s apart
        Assert.Equal(3, played);
        Assert.NotNull(p.Request("breakthrough", 60.5));   // the first play left the minute
    }

    [Fact]
    public void Fuse_allows_six_non_p0_sounds_a_minute_and_p0_is_exempt()
    {
        var p = new AudioPlanner(); int played = 0;
        string[] ids = { "order_ok", "order_done", "node_discovered", "breakthrough", "line_commissioned", "order_reject", "pause", "unpause" };
        for (int k = 0; k < ids.Length; k++) if (p.Request(ids[k], k * 3.0) != null) played++;
        Assert.Equal(6, played);
        Assert.NotNull(p.Request("signal_alert", 25.0));
    }

    [Fact]
    public void Three_cap_drops_in_a_row_silence_non_p0_for_thirty_seconds()
    {
        var p = new AudioPlanner();
        for (int k = 0; k < 3; k++) Assert.NotNull(p.Request("breakthrough", k * 11.0));   // cap reached
        Assert.Null(p.Request("breakthrough", 34.0));   // cap drop 1
        Assert.Null(p.Request("breakthrough", 45.0));   // cap drop 2
        Assert.Null(p.Request("breakthrough", 46.0 + 0.0)); // cap drop 3 -> quiet until 76
        Assert.Null(p.Request("order_ok", 50.0));
        Assert.Null(p.Request("pause", 70.0));
        Assert.NotNull(p.Request("war_declared", 60.0));  // P0 is never silenced
        Assert.NotNull(p.Request("order_ok", 80.0));
    }

    [Fact]
    public void Ui_tick_does_not_use_the_fuse_and_does_not_crowd_out_confirmations()
    {
        var p = new AudioPlanner();
        for (int k = 0; k < 8; k++) p.Request("ui_tick", k * 0.3);       // eight ticks in 2.4 s
        Assert.NotNull(p.Request("order_ok", 3.0));
        Assert.NotNull(p.Request("pause", 4.0));
        string[] ids = { "order_done", "breakthrough", "line_commissioned", "node_discovered" };
        for (int k = 0; k < ids.Length; k++) Assert.NotNull(p.Request(ids[k], 10.0 + k * 11.0));   // six fuse slots in all
        Assert.Null(p.Request("unpause", 55.0));                           // the fuse still binds the others
    }

    [Fact]
    public void Ui_tick_cap_drops_and_drops_inside_the_quiet_period_do_not_count()
    {
        var p = new AudioPlanner();
        for (int k = 0; k < 12; k++) Assert.NotNull(p.Request("ui_tick", k * 0.3));
        for (int k = 0; k < 6; k++) Assert.Null(p.Request("ui_tick", 4.0 + k * 0.3));   // over the cap: dropped, uncounted
        Assert.NotNull(p.Request("order_ok", 6.0));                                      // no quiet period came of it
        var q = new AudioPlanner();
        for (int k = 0; k < 3; k++) Assert.NotNull(q.Request("breakthrough", k * 11.0));
        for (int k = 0; k < 3; k++) Assert.Null(q.Request("breakthrough", 34.0 + k * 11.0));   // 3 cap drops (the last at 56): quiet until 86
        Assert.Null(q.Request("breakthrough", 60.5));           // inside quiet: not counted, not extended
        Assert.Null(q.Request("breakthrough", 70.0));
        Assert.Null(q.Request("order_ok", 85.0));
        Assert.NotNull(q.Request("order_ok", 87.0));            // the quiet period ended on schedule
    }

    [Fact]
    public void A_slowed_tempo_click_holds_its_voice_for_the_stretched_length()
    {
        var p = new AudioPlanner();
        Assert.NotNull(p.Request("line_commissioned", 0.0));
        Assert.NotNull(p.Request("breakthrough", 0.0));
        Assert.NotNull(p.Request("tempo_1", 0.0));          // 0.25 s / 0.943874 = 0.265 s
        Assert.Null(p.Request("ui_tick", 0.257));           // still three voices
        Assert.NotNull(p.Request("ui_tick", 0.27));
    }

    [Fact]
    public void Tempo_pitch_is_public_for_the_demo_and_ducking_can_be_started_outside_the_planner()
    {
        Assert.Equal(1.059463, AudioPlanner.PitchOf(AudioCatalog.Get("tempo_3")), 6);
        var p = new AudioPlanner();
        p.DuckFor(AudioCatalog.Get("order_ok"), 1.0);
        Assert.Equal(0, p.DuckDb(1.1));
        p.DuckFor(AudioCatalog.Get("war_declared"), 1.0);
        Assert.Equal(-4, p.DuckDb(1.1));
    }

    [Fact]
    public void Interval_drops_do_not_count_toward_the_quiet_period()
    {
        var p = new AudioPlanner();
        Assert.NotNull(p.Request("ui_tick", 0.0));
        for (int k = 1; k <= 4; k++) Assert.Null(p.Request("ui_tick", 0.05 * k));
        Assert.NotNull(p.Request("order_ok", 1.0));
    }
    [Fact]
    public void Hidden_adaptation_lowers_p3_and_p2_after_ninety_minutes_and_resets()
    {
        var p = new AudioPlanner();
        for (double t = 0; t <= 91 * 60; t += 60) p.Touch(t);
        Assert.Equal(-3.0, p.AdaptDb(Prio.P3));
        Assert.Equal(-1.5, p.AdaptDb(Prio.P2));
        Assert.Equal(0, p.AdaptDb(Prio.P0));
        Assert.Equal(0, p.AdaptDb(Prio.P1));
        p.ResetAdaptation();
        Assert.Equal(0, p.AdaptDb(Prio.P3));
    }

    [Fact]
    public void A_long_break_does_not_count_as_play_time()
    {
        var p = new AudioPlanner();
        p.Touch(0); p.Touch(3600 * 5);
        Assert.Equal(0, p.ActiveSeconds);
    }

    [Fact]
    public void Gain_follows_the_adaptation()
    {
        var p = new AudioPlanner();
        for (double t = 0; t <= 100 * 60; t += 60) p.Touch(t);
        var pl = p.Request("ui_tick", 100 * 60 + 1);
        Assert.InRange(pl!.GainDb, -4.5, -1.5);   // -3 dB adaptation, +-1.5 dB jitter
    }
}

public class OrderSoundsTests
{
    [Fact]
    public void Paused_gives_only_a_queue_tick_and_running_only_an_ok()
    {
        var o = new OrderSounds();
        Assert.Equal("queue_tick", o.Accepted(1.0, 0, 1));
        Assert.Equal("order_ok", o.Accepted(2.0, 2, 2));
    }

    [Fact]
    public void Done_sounds_only_when_late_and_running_and_once_per_queue()
    {
        var o = new OrderSounds();
        o.Accepted(10.0, 2, 3);
        Assert.Null(o.Applied(10.3, 2, 3));                  // applied at once: the ok was enough
        o.Accepted(20.0, 2, 4);
        Assert.Equal("order_done", o.Applied(22.0, 2, 4));   // noticeably later
        Assert.Null(o.Applied(22.1, 2, 4));                  // the rest stays silent
    }

    [Fact]
    public void A_command_given_on_pause_is_silent_when_applied_after_unpause()
    {
        var o = new OrderSounds();
        Assert.Equal("queue_tick", o.Accepted(1.0, 0, 5));
        Assert.Null(o.Applied(300.0, 1, 5));
        o.Accepted(310.0, 2, 6); o.Accepted(311.0, 0, 7);       // a later paused command also clears the pending accept time
        Assert.Null(o.Applied(400.0, 1, 7));
    }

    [Fact]
    public void Done_without_an_accepted_order_is_silent()
        => Assert.Null(new OrderSounds().Applied(5.0, 1, 1));

    [Fact]
    public void Another_commands_application_does_not_borrow_the_accept_time()
    {
        var o = new OrderSounds();
        o.Accepted(10.0, 2, 1);
        Assert.Null(o.Applied(20.0, 2, 2));               // command 2 was applied, command 1 was the one recorded
        Assert.Equal("order_done", o.Applied(20.1, 2, 1));
    }
}

public class SpeedSoundsTests
{
    [Fact]
    public void The_first_reading_is_a_baseline_not_an_unpause()
    {
        var s = new SpeedSounds();
        Assert.Null(s.Observe(2));
        Assert.Equal("tempo_3", s.Observe(3));
        s.Rebase();
        Assert.Null(s.Observe(1));       // after a load
    }

    [Fact]
    public void One_cue_per_change_by_the_new_speed()
    {
        var s = new SpeedSounds();
        s.Observe(1);
        Assert.Equal("tempo_2", s.Observe(2));
        Assert.Equal("pause", s.Observe(0));
        Assert.Equal("unpause", s.Observe(3));
        Assert.Null(s.Observe(3));
    }

    [Fact]
    public void A_program_set_speed_such_as_the_game_menu_is_silent_once()
    {
        var s = new SpeedSounds();
        s.Observe(2);
        s.Quiet(0);
        Assert.Null(s.Observe(0));           // menu opened
        s.Quiet(2);
        Assert.Null(s.Observe(2));           // menu closed
        Assert.Equal("pause", s.Observe(0)); // the player's own pause sounds again
        s.Quiet(0);                                         // already paused: nothing will change, no stale mute
        Assert.Equal("unpause", s.Observe(1));
    }

}

public class OpenTickGateTests
{
    [Fact]
    public void Mouse_on_a_new_object_sounds_keyboard_and_repeat_do_not()
    {
        var g = new OpenTickGate();
        Assert.True(g.Allow(1, false));
        Assert.False(g.Allow(1, false));    // same object again
        Assert.False(g.Allow(2, true));     // keyboard
        Assert.True(g.Allow(3, false));
        g.Closed(true);
        Assert.True(g.Allow(3, false));     // closed in between
    }

    [Fact]
    public void Closing_nothing_does_not_make_the_same_object_new()
    {
        var g = new OpenTickGate();
        Assert.True(g.Allow(7, false));
        g.Closed(false);                    // CloseArea with the map: no panel was open
        Assert.False(g.Allow(7, false));    // re-click of the same power stays silent
    }
}

public class WorldCuesTests
{
    [Fact]
    public void Licensed_nodes_do_not_count_as_discoveries_even_after_the_flag_drops()
    {
        Assert.False(WorldCues.NodeGain(5, 4, 1, 0));    // a licensed node appears: both counts grow
        Assert.False(WorldCues.NodeGain(5, 5, 0, 1));    // a month later the flag is gone: total unchanged
        Assert.True(WorldCues.NodeGain(5, 4, 0, 0));     // own research
        Assert.True(WorldCues.NodeGain(5, 4, 0, 1));     // own research in the month the old licence flag drops
        Assert.False(WorldCues.NodeGain(4, 5, 0, 0));
    }

    [Fact]
    public void Fuse_is_one_rolling_minute_over_all_non_p0_cues_except_ui_tick()
    {
        var p = new AudioPlanner();
        string[] ids = { "order_ok", "pause", "node_discovered", "breakthrough", "line_commissioned", "order_reject" };
        for (int k = 0; k < ids.Length; k++) Assert.NotNull(p.Request(ids[k], k * 11.0));
        Assert.Null(p.Request("order_done", 59.0));          // seventh in the same 60 s
        Assert.NotNull(p.Request("order_done", 60.5));       // the first left the window
    }

    [Fact]
    public void A_breakthrough_silences_the_line_and_node_of_the_same_frame()
    {
        Assert.Equal((true, false, false), WorldCues.Pick(true, true, true));
        Assert.Equal((false, true, true), WorldCues.Pick(false, true, true));
        Assert.Equal((false, false, false), WorldCues.Pick(false, false, false));
    }
}

public class AudioSettingsTests
{
    [Fact]
    public void Alerts_can_be_turned_down_but_never_off()
    {
        Assert.Equal(AudioSettings.AlertFloor, AudioSettings.Clamp(Bus.Alert, 0));
        Assert.True(AudioSettings.ToDb(Bus.Alert, 0) > -20);
        Assert.Equal(-80, AudioSettings.ToDb(Bus.Music, 0));
        Assert.Equal(0, AudioSettings.ToDb(Bus.Alert, 1), 6);
    }

    [Fact]
    public void Alert_cues_use_the_alert_bus_and_the_rest_the_ui_bus()
    {
        foreach (var c in AudioCatalog.All)
            Assert.Equal(c.Prio <= Prio.P1 ? "Alert" : "UI", c.Bus);
        Assert.Equal("Alert", AudioCatalog.Get("war_declared").Bus);
    }
}

public class MusicSchedulerTests
{
    [Fact]
    public void Plays_sessions_of_three_to_five_minutes_separated_by_silence()
    {
        var m = new MusicScheduler(new[] { 110.0, 76.0 }, firstStart: 8);
        var starts = new List<double>(); var ends = new List<double>();
        bool was = false;
        for (double t = 0; t < 1800; t += 0.5)
        {
            var s = m.Update(t);
            if (s.Kind == MusicScheduler.Kind.Start && !was) starts.Add(t);
            if (was && !m.Playing) ends.Add(t);
            was = m.Playing;
        }
        Assert.InRange(starts[0], 8, 9);
        Assert.True(starts.Count >= 3);
        for (int i = 0; i < ends.Count; i++)
        {
            double len = ends[i] - starts[i];
            Assert.InRange(len, 180, 300 + 110);                   // a session ends at a track end
            if (i + 1 < starts.Count) Assert.InRange(starts[i + 1] - ends[i], 29.5, 90.5);
        }
    }

    [Fact]
    public void The_amount_setting_scales_the_silence()
    {
        double Gap(int amount)
        {
            // Silence length after the first session: from the moment the music stops to the next start.
            var m = new MusicScheduler(new[] { 100.0 }, firstStart: 0) { Amount = amount };
            double end = -1;
            for (double t = 0; t < 2000; t += 0.5)
            {
                bool was = m.Playing;
                m.Update(t);
                if (was && !m.Playing) end = t;
                if (end >= 0 && m.Playing) return t - end;
            }
            return -1;
        }
        Assert.True(Gap(0) > Gap(2));
    }
}

public class AmbientGateTests
{
    [Fact]
    public void Starts_after_two_still_seconds_and_stays()
    {
        var g = new AmbientGate();
        Assert.False(g.Update(0, true));
        Assert.False(g.Update(1.5, false));
        Assert.False(g.Update(1.9, true));
        Assert.False(g.Update(3.0, false));
        Assert.True(g.Update(4.0, false));
        Assert.True(g.Update(4.1, true));
    }

    [Fact]
    public void No_cue_has_a_gain_offset()
    {
        foreach (var c in AudioCatalog.All) Assert.Equal(0, c.GainDb);
        Assert.Equal(0, new AudioPlanner().Request("line_commissioned", 1.0)!.GainDb);
        Assert.Equal(0, new AudioPlanner().Request("order_done", 1.0)!.GainDb);
    }
}
