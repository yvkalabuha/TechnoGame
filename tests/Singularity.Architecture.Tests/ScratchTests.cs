using System.Reflection;
using Singularity.Core;
using Singularity.Core.Simulation;
using Singularity.Kernel;

namespace Singularity.Architecture.Tests;

/// <summary>
/// ADR-0013 §4 (DEBT-0011): a system keeps no state outside its tables except working buffers marked <see cref="ScratchAttribute"/>.
/// A buffer is private, never of a table type (so never hashed or saved), and read only by its own class: a member that hands it out is
/// callable from <c>Singularity.Core</c> code only inside that class (Session, Lab and tests may read it: they are outside the simulation).
/// Every other mutable reference field of a system is either a column descriptor or listed below as precomputed and never changed after construction.
/// </summary>
public class ScratchTests
{
    private static readonly Assembly Core = typeof(ParamSet).Assembly;

    // Immutable data computed once in the constructor (never in a tick), so they are not scratch and not state.
    private static readonly string[] PrecomputedFields =
    [
        "ProductsSystem._bureauReq",
        "ProductsSystem._m0",
        "IndustrySystem.DimNames",
    ];

    private static IEnumerable<Type> Systems(Assembly assembly) =>
        assembly.GetTypes().Where(t => typeof(ITickSystem).IsAssignableFrom(t) && !t.IsInterface);

    private static List<FieldInfo> ScratchFields(IEnumerable<Type> types) =>
        [.. types.SelectMany(t => t.GetFields(IlScanner.AllDeclared)).Where(f => f.GetCustomAttribute<ScratchAttribute>() is not null)];

    private static bool IsTableType(Type t)
    {
        while (t.HasElementType || (t.IsGenericType && t.GetGenericArguments().Length == 1))
            t = t.HasElementType ? t.GetElementType()! : t.GetGenericArguments()[0];
        return t == typeof(Table) || typeof(Column).IsAssignableFrom(t);
    }

    /// <summary>Column descriptors (arrays of them too) point into the table: they are state handles, not buffers.</summary>
    private static bool IsColumnDescriptor(Type t) => t.HasElementType ? IsColumnDescriptor(t.GetElementType()!) : typeof(Column).IsAssignableFrom(t);

    private static bool IsMutableReference(Type t) =>
        t.IsArray || (t.IsGenericType && t.GetGenericTypeDefinition() is { } d
            && (d == typeof(List<>) || d == typeof(Dictionary<,>) || d == typeof(HashSet<>) || d == typeof(Queue<>) || d == typeof(Stack<>)));

    /// <summary>Fields of <paramref name="types"/> that break the rules of the scratch contract.</summary>
    private static List<string> FieldViolations(IEnumerable<Type> types)
    {
        var found = new List<string>();
        foreach (Type type in types)
        {
            foreach (FieldInfo f in type.GetFields(IlScanner.AllDeclared))
            {
                string name = $"{type.Name}.{f.Name}";
                bool scratch = f.GetCustomAttribute<ScratchAttribute>() is not null;
                if (scratch && !f.IsPrivate)
                    found.Add($"{name}: a scratch field must be private");
                if (scratch && IsTableType(f.FieldType))
                    found.Add($"{name}: a scratch field must not be a table or a column (it would be hashed)");
                if (!scratch && !f.Name.StartsWith('<') && IsMutableReference(f.FieldType) && !IsColumnDescriptor(f.FieldType)
                    && Array.IndexOf(PrecomputedFields, name) < 0)
                    found.Add($"{name}: a mutable buffer must be marked [Scratch] (or listed as precomputed)");
            }
        }
        return found;
    }

    /// <summary>Non-private members that load a scratch field of their own class and give a value back: reading the buffer through them is reading the buffer.</summary>
    private static List<MemberInfo> Exposers(IEnumerable<Type> types)
    {
        var result = new List<MemberInfo>();
        foreach (Type type in types)
        {
            List<FieldInfo> own = ScratchFields([type]);
            if (own.Count == 0)
                continue;
            foreach (MethodInfo m in type.GetMethods(IlScanner.AllDeclared))
            {
                if (m.IsPrivate || m.ReturnType == typeof(void))
                    continue;
                if (IlScanner.Read(m).Any(i => i.Operand is FieldInfo f && own.Any(o => o.MetadataToken == f.MetadataToken && o.Module == f.Module)))
                    result.Add(m);
            }
        }
        return result;
    }

    private static List<string> ReadsOutsideTheOwner(IEnumerable<Type> scanned, IEnumerable<Type> owners) =>
        OwnerOnlyScan.Offenders(scanned, [.. ScratchFields(owners), .. Exposers(owners)]);

    [Fact]
    public void TheSystemsCarryScratchBuffers()
    {
        // A guard against a vacuous test: the four systems of the slice have buffers, and the scan sees the types.
        string[] owners = [.. ScratchFields(Systems(Core)).Select(f => f.DeclaringType!.Name).Distinct().Order(StringComparer.Ordinal)];
        Assert.Equal(["IndustrySystem", "KnowledgeSystem", "PolicySystem", "ProductsSystem"], owners);
    }

    [Fact]
    public void Scratch_IsPrivate_NotATable_AndEveryMutableBufferIsMarked() => Assert.Empty(FieldViolations(Systems(Core)));

    [Fact]
    public void Core_NeverReadsAScratchBuffer_FromOutsideTheOwningClass() => Assert.Empty(ReadsOutsideTheOwner(Core.GetTypes(), Systems(Core)));

    [Fact]
    public void TheScan_SeesADirectRead_AnAccessorRead_AndALambda()
    {
        List<string> found = ReadsOutsideTheOwner([typeof(Intruder), .. typeof(Intruder).GetNestedTypes(IlScanner.AllDeclared)], [typeof(MutantSystem)]);
        Assert.Contains(found, f => f.Contains("Direct") && f.Contains("Buffer"));
        Assert.Contains(found, f => f.Contains("ViaAccessor") && f.Contains("Peek"));
        Assert.Contains(found, f => f.Contains("Buffer") && !f.Contains("Direct")); // the lambda
        Assert.Contains(found, f => f.Contains("Fine")); // Own() hands out a scratch buffer too
    }

    [Fact]
    public void TheFieldRules_SeeAnUnmarkedBuffer_AScratchColumn_AndAnInternalScratch()
    {
        List<string> found = FieldViolations([typeof(MutantSystem)]);
        Assert.Contains(found, f => f.StartsWith("MutantSystem.Unmarked"));
        Assert.Contains(found, f => f.StartsWith("MutantSystem.Hashed") && f.Contains("hashed"));
        Assert.Contains(found, f => f.StartsWith("MutantSystem.Buffer") && f.Contains("private"));
    }
}

// Mutants of Core: the scans must see them. Top-level types, so that none counts as «inside» the other.

internal sealed class MutantSystem : ITickSystem
{
    [Scratch] internal readonly long[] Buffer = new long[4];
    internal readonly List<int> Unmarked = [];
    [Scratch] internal readonly LongColumn[] Hashed = [];
    [Scratch] private readonly int[] _hidden = new int[2];

    public int SystemId => 0;
    public long[] Peek() => Buffer;
    public int[] Own() => _hidden;
    public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd) { }
    public void Merge(in TickContext context, int chunkCount) { }
    public void Post(in TickContext context) { }
}

internal sealed class Intruder(MutantSystem s)
{
    public long Direct() => s.Buffer[0];
    public long ViaAccessor() => s.Peek()[0];
    public Func<long> ViaLambda() => () => s.Buffer[1];
    public int Fine() => s.Own().Length;
}

