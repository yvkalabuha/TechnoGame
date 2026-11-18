using Singularity.Client.Spike;

namespace Singularity.Client.Audio.Tests;

public class AudioPlannerTests
{
    [Fact]
    public void Same_class_within_the_window_plays_once()
    {
        var p = new AudioPlanner();
        Assert.NotNull(p.Request("order_ok", 10.0));
        Assert.Null(p.Request("order_done", 10.2));       // same class "order", inside 400 ms
        Assert.NotNull(p.Request("order_done", 10.5));
    }

    [Fact]
    public void Three_events_in_two_seconds_fold_into_one_collective_sound_a_step_higher()
    {
        var p = new AudioPlanner();
        Assert.NotNull(p.Request("order_done", 5.0));
        Assert.Null(p.Request("order_done", 5.2));
        var c = p.Request("order_done", 5.4);
        Assert.NotNull(c);
        Assert.True(c!.Collective);
        Assert.True(c.Pitch > 1.1);
        Assert.Null(p.Request("order_done", 5.6));         // the burst keeps folding: no second collective inside two seconds
    }

    [Fact]
    public void Voice_budget_is_three_and_p0_evicts_the_lowest_priority_voice()
    {
        var p = new AudioPlanner();
        Assert.NotNull(p.Request("line_commissioned", 1.0));   // P1, 0.75 s
        Assert.NotNull(p.Request("node_discovered", 1.0));     // P1
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
        p.Request("line_commissioned", 1.0); p.Request("node_discovered", 1.0); p.Request("launch_space", 1.0);
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
            var pl = p.Request("order_ok", i * 3.0);
            Assert.NotNull(pl);
            Assert.Equal(0, pl!.Variant);
            Assert.InRange(pl.Pitch, 0.97, 1.03);
            Assert.InRange(pl.GainDb, -1.5, 1.5);
            pitches.Add(pl.Pitch);
        }
        Assert.True(pitches.Count > 50);
        Assert.Equal(1.0, new AudioPlanner().Request("order_reject", 0)!.Pitch);
    }

    [Fact]
    public void Signal_attention_is_p1_and_tempo_clicks_follow_the_speed()
    {
        Assert.Equal(Prio.P1, AudioCatalog.Get("signal_attention").Prio);
        Assert.Equal(new[] { 1, 2, 3 }, new[] { "tempo_1", "tempo_2", "tempo_3" }.Select(c => AudioCatalog.Get(c).Repeats));
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
    public void Ducking_applies_to_p0_and_p1_only_and_lasts_sound_plus_400_ms()
    {
        var p = new AudioPlanner();
        p.Request("order_ok", 1.0);
        Assert.Equal(0, p.DuckDb(1.05));
        var s = AudioCatalog.Get("signal_alert");
        p.Request("signal_alert", 2.0);
        Assert.Equal(-6, p.DuckDb(2.0 + s.Seconds + 0.3));
        Assert.Equal(0, p.DuckDb(2.0 + s.Seconds + 0.5));
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

public class SignalReminderTests
{
    [Fact]
    public void A_raised_signal_reminds_once_after_sixty_seconds()
    {
        var r = new SignalReminder();
        r.Raise("sig.alloy", 100);
        Assert.Empty(r.Due(150));
        Assert.Equal(new[] { "sig.alloy" }, r.Due(161));
        Assert.Empty(r.Due(300));
    }

    [Fact]
    public void A_cleared_signal_does_not_remind()
    {
        var r = new SignalReminder();
        r.Raise("sig.alloy", 0);
        r.Clear("sig.alloy");
        Assert.Empty(r.Due(120));
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
}
