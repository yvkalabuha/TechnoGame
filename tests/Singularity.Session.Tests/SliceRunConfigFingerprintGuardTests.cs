using System.Collections.Immutable;
using System.Reflection;
using Singularity.Core.Policy;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>
/// TASK-0124 guard: a field added to <see cref="SliceRunConfig"/> (or to a record it holds) must either change the snapshot fingerprint or be
/// listed below with the reason it cannot matter. Otherwise a snapshot could be loaded into a session that runs another trajectory.
/// </summary>
public class SliceRunConfigFingerprintGuardTests
{
    private static readonly Dictionary<string, string> Excluded = new()
    {
        ["Threads"] = "execution only (L2): never changes the result",
        ["ChunkSize"] = "execution only (L2): never changes the result",
        ["ViewMode"] = "execution only: what the read views hold",
        ["ViewScope"] = "execution only (ADR-0025): which actors the views carry the node blocks of",
        ["DailyTickProbe"] = "execution only: an empty daily system, the outcome is that of the flag off",
        ["HashCheckpoints"] = "execution only: when a hash is taken",
        ["HashEveryTick"] = "execution only: when a hash is taken",
        ["LkTables"] = "tables are not fingerprinted: a snapshot of a run with them is refused (see the last test)",
        ["CapabilityTables"] = "tables are not fingerprinted: a snapshot of a run with them is refused (see the last test)",
    };

    // The nested types whose properties are checked one by one.
    private static readonly Type[] Nested = [typeof(ToolingTargetSpec), typeof(GridCell)];

    private static bool Settable(PropertyInfo p) => p.SetMethod is { IsPublic: true };

    private static object Changed(Type type, object? current)
    {
        Type? under = Nullable.GetUnderlyingType(type);
        if (under is not null)
            return current is null ? Changed(under, Activator.CreateInstance(under)!) : Changed(under, current);
        if (type == typeof(bool))
            return !(bool)current!;
        if (type.IsEnum)
        {
            foreach (object v in Enum.GetValues(type))
            {
                if (!v.Equals(current))
                    return v;
            }
            throw new InvalidOperationException($"enum {type.Name} has one value");
        }
        if (type == typeof(ulong))
            return (ulong)current! + 1;
        if (type == typeof(int))
            return (int)current! + 1;
        if (type == typeof(string))
            return (string)current! + "x";
        if (type == typeof(Fx6))
            return Fx6.FromRaw(((Fx6)current!).Raw + 7);
        if (type == typeof(ImmutableArray<Fx6>))
        {
            var a = (ImmutableArray<Fx6>)current!;
            return a.IsDefault ? ImmutableArray.Create(Fx6.One) : a.Add(Fx6.One);
        }
        throw new InvalidOperationException($"the guard does not know how to change a {type.Name}: extend it (and the fingerprint) for the new field");
    }

    private static object Clone(object o) =>
        o.GetType().IsValueType ? o : typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(o, null)!;

    private static IEnumerable<(string Path, SliceRunConfig Variant)> Variants(SliceRunConfig baseline)
    {
        foreach (PropertyInfo p in typeof(SliceRunConfig).GetProperties().Where(Settable))
        {
            if (Excluded.ContainsKey(p.Name))
                continue;
            object? current = p.GetValue(baseline);
            if (Nested.Contains(p.PropertyType))
            {
                foreach (PropertyInfo q in p.PropertyType.GetProperties().Where(Settable))
                {
                    // A fresh copy per property: GetValue boxes a struct anew each time, so no axis sees another axis's change.
                    object inner = Clone(p.GetValue(baseline)!);
                    q.SetValue(inner, Changed(q.PropertyType, q.GetValue(inner)));
                    var v = (SliceRunConfig)Clone(baseline);
                    p.SetValue(v, inner);
                    yield return ($"{p.Name}.{q.Name}", v);
                }
                continue;
            }
            var variant = (SliceRunConfig)Clone(baseline);
            p.SetValue(variant, Changed(p.PropertyType, current));
            yield return (p.Name, variant);
        }
    }

    [Fact]
    public void EveryField_ChangesTheFingerprint_OrIsExcludedWithAReason()
    {
        var baseline = new SliceRunConfig();
        byte[] expected = baseline.SnapshotFingerprint();
        var silent = new List<string>();
        int checkedFields = 0;
        foreach ((string path, SliceRunConfig variant) in Variants(baseline))
        {
            checkedFields++;
            if (variant.SnapshotFingerprint().AsSpan().SequenceEqual(expected))
                silent.Add(path);
        }
        Assert.True(checkedFields > 30, $"only {checkedFields} fields were checked: the reflection walk is broken");
        Assert.True(silent.Count == 0, "fields that do not change the snapshot fingerprint (add them to it, or to Excluded with a reason): " + string.Join(", ", silent));
        Assert.All(Excluded.Values, reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
        Assert.All(Excluded.Keys, name => Assert.NotNull(typeof(SliceRunConfig).GetProperty(name)));
    }

    [Fact]
    public void ExecutionOnlyFields_DoNotChangeTheFingerprint()
    {
        var baseline = new SliceRunConfig();
        var other = baseline with { Threads = 4, ChunkSize = 8, ViewMode = SliceViewMode.Calibration, DailyTickProbe = true, HashCheckpoints = false, HashEveryTick = true };
        Assert.Equal(baseline.SnapshotFingerprint(), other.SnapshotFingerprint());
    }

    [Fact]
    public void ControlTables_AreRefused_NotSilentlyIgnored()
    {
        var lk = (Singularity.Core.Knowledge.LkTables)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Singularity.Core.Knowledge.LkTables));
        Assert.Throws<NotSupportedException>(() => (new SliceRunConfig() with { LkTables = lk }).SnapshotFingerprint());
        Assert.Throws<NotSupportedException>(() => (new SliceRunConfig() with { CapabilityTables = new CapabilityControlTables([], [], []) }).SnapshotFingerprint());
    }
}
