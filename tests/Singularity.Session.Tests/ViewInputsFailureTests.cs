using System.Collections.Immutable;
using System.Reflection;
using Singularity.Core.Data;
using Singularity.Session;

namespace Singularity.Session.Tests;

/// <summary>TASK-0079 review: a failing actor must give the same exception at any thread count.</summary>
public sealed class ViewInputsFailureTests
{
    // Phantom actors past the real ones: their reads of the state columns fail, the real actors do not.
    private static Exception Fail(int threads)
    {
        // Own copy of the data: the scenario is mutated below and the shared one is used by tests running in parallel.
        GameData data = ModLoader.Load(Path.Combine(SliceTestData.RepoRoot, "mods", "core"));
        SliceSession s = SliceSession.Create(data, new SliceRunConfig { Seed = 7, Threads = threads });
        SliceWorld w = s.World;
        FieldInfo field = typeof(ScenarioData).GetField("<ActorId>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(w.Scenario, w.Scenario.ActorId.AddRange(ImmutableArray.Create("P1", "P2", "P3", "P4", "P5", "P6")));
        return Record.Exception(() => ViewBuilder.Inputs(w, 0, threads))
            ?? throw new InvalidOperationException("Inputs did not fail for the phantom actors.");
    }

    [Fact]
    public void FailureType_DoesNotDependOnThreads()
    {
        Exception one = Fail(1);
        Exception eight = Fail(8);
        Assert.IsNotType<AggregateException>(eight);
        Assert.Equal(one.GetType(), eight.GetType());
        Assert.Equal(one.Message, eight.Message);
    }
}
