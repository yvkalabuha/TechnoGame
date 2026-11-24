using System;
using System.Collections.Generic;
using System.IO;
using Singularity.Client.Spike;
using Singularity.Core.Data;
using Singularity.Kernel;
using Singularity.Session;
using Xunit;

namespace Singularity.Client.Audio.Tests;

/// <summary>The Production flask over the alloy coverage emitted by real sessions (TASK-0119): the four display states each come from session data, not from hand-built values.</summary>
public class AlloyStatesFromSessionTests
{
    static string Root()
    {
        string? d = AppContext.BaseDirectory;
        while (d != null && !File.Exists(Path.Combine(d, "Singularity.sln"))) d = Path.GetDirectoryName(d);
        return d ?? throw new InvalidOperationException("repo root not found");
    }

    [Fact]
    public void EveryAlloyState_IsReachedByARealSession_AndMatchesItsDefinition()
    {
        GameData data = ModLoader.Load(Path.Combine(Root(), "mods", "core"));
        var seen = new Dictionary<InstrumentLogic.AlloyState, int>();
        foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5, 6, 7, 8 })
        {
            var s = SliceSession.Create(data, new SliceRunConfig { Seed = seed });
            for (long t = 0; t < 200; t++)
            {
                s.StepThroughMonth(t);
                foreach (var a in s.View.Actors)
                {
                    if (!a.Alloy.Known) continue;
                    var c = a.Alloy;
                    var f = InstrumentLogic.AlloyFlask(c);
                    seen[f.State] = seen.GetValueOrDefault(f.State) + 1;
                    // each state against its own definition, from the same field
                    Assert.Equal(c.Supply <= Fx6.Zero ? InstrumentLogic.AlloyState.NoSmelting
                        : c.Need > Fx6.Zero && c.Supply < c.Need ? InstrumentLogic.AlloyState.Shortage
                        : c.DeficitFraction > Fx6.Zero ? InstrumentLogic.AlloyState.Disrupted
                        : InstrumentLogic.AlloyState.Normal, f.State);
                }
            }
        }
        foreach (var state in Enum.GetValues<InstrumentLogic.AlloyState>())
            Assert.True(seen.GetValueOrDefault(state) > 0, $"no real session reached {state}; seen: {string.Join(", ", seen)}");
    }
}
