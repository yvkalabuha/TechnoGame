using System;
using System.Collections.Generic;
using System.IO;
using Singularity.Client.Spike;
using Singularity.Core.Data;
using Singularity.Core.Products;
using Singularity.Session;
using Xunit;

namespace Singularity.Client.Audio.Tests;

/// <summary>The Production flask over the alloy state the Session emits (TASK-0119): real sessions reach all four states, and the client only maps the state to a flask.</summary>
public class AlloyStatesFromSessionTests
{
    static string Root()
    {
        string? d = AppContext.BaseDirectory;
        while (d != null && !File.Exists(Path.Combine(d, "Singularity.sln"))) d = Path.GetDirectoryName(d);
        return d ?? throw new InvalidOperationException("repo root not found");
    }

    [Fact]
    public void EveryAlloyState_IsEmittedByARealSession_AndTheFlaskCarriesThatState()
    {
        GameData data = ModLoader.Load(Path.Combine(Root(), "mods", "core"));
        var seen = new Dictionary<AlloyState, int>();
        foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5, 6, 7, 8 })
        {
            var s = SliceSession.Create(data, SliceRunConfig.Legacy with { Seed = seed });
            for (long t = 0; t < 200; t++)
            {
                s.StepThroughMonth(t);
                foreach (var a in s.View.Actors)
                {
                    AlloyState state = a.Alloy.State;
                    Assert.NotEqual(AlloyState.Unknown, state);   // a stepped session always has a trace
                    seen[state] = seen.GetValueOrDefault(state) + 1;
                    Assert.Equal(state, InstrumentLogic.AlloyFlask(a.Alloy).State);   // the client adds no case of its own
                }
            }
        }
        foreach (var state in new[] { AlloyState.Normal, AlloyState.Disrupted, AlloyState.Shortage, AlloyState.NoSmelting })
            Assert.True(seen.GetValueOrDefault(state) > 0, $"no real session emitted {state}; seen: {string.Join(", ", seen)}");
    }
}
