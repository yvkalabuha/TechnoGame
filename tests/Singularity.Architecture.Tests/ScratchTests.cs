using System.Reflection;
using System.Reflection.Emit;
using Singularity.Core;
using Singularity.Kernel;

namespace Singularity.Architecture.Tests;

/// <summary>
/// ADR-0013 §4 (DEBT-0011): a system keeps no state outside its tables except working buffers marked <see cref="ScratchAttribute"/>.
/// A buffer is private, never of a table type (so never hashed or saved), and read only by its own class: a member that hands it out is
/// callable from <c>Singularity.Core</c> code only inside that class (Session, Lab and tests may read it: they are outside the simulation).
/// Every other field of a system is a safe form (see <see cref="IsSafeType"/>): a column handle, an injected service, an immutable value, or listed below as precomputed. A field that is not read-only is written only in a constructor or an <c>Add*Columns</c> binding method.
/// </summary>
public class ScratchTests
{
    private static readonly Assembly Core = typeof(Singularity.Core.Industry.IndustrySystem).Assembly;

    // Immutable data computed once in the constructor (never in a tick), so they are not scratch and not state.
    private static readonly string[] PrecomputedFields =
    [
        "ProductsSystem._bureauReq",
        "ProductsSystem._m0",
        "IndustrySystem.DimNames",
        // Memo of prc_set per (actor, design), a pure function of Current: filled in the constructor and in Post (single-threaded), never on read, never hashed or saved (TASK-0068 F2).
        "ProductsSystem._prcMemo",
        // Configuration objects with constant arrays (cost tables, the start state): read by the system, never written.
        "KnowledgeSystem._p",
        "KnowledgeSystem._start",
        // Slot model: T_n of every node, computed once from the graph and the parameters in AddSlotColumns (TASK-0187).
        "KnowledgeSystem._tn",
        // The per-tick channel to IndustrySystem: filled in compute, drained and cleared by IndustrySystem (whose own list of mailboxes is [Scratch]).
        "ProductsSystem._mailbox",
        "PolicySystem._mailbox",
        // An adapter over the toy model, which owns its own tables.
        "FunnelSystem.<model>P",
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

    // Setup methods that bind a system to a table or to its inputs once, before the first tick (a constructor always counts as setup).
    private static bool IsBinding(MethodBase m) => m.IsConstructor || m.Name.StartsWith("Add", StringComparison.Ordinal) && m.Name.EndsWith("Columns", StringComparison.Ordinal);

    /// <summary>
    /// The allowlist of safe forms for a field that is not [Scratch]: a primitive, enum or string; a column descriptor or table handle;
    /// an injected service (interface); an immutable value (<c>ImmutableArray</c>, <c>Nullable</c>, tuple, struct or class whose instance
    /// fields are all read-only or init-only and themselves safe). Anything else (arrays, <c>List</c>, a class with a mutable field) is a buffer.
    /// </summary>
    internal static bool IsSafeType(Type t, HashSet<Type>? seen = null)
    {
        if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t.IsInterface || IsTableType(t) && !t.HasElementType && !t.IsGenericType
            || IsColumnDescriptor(t))
            return true;
        if (t.IsArray)
            return false;
        if (t.IsGenericType)
        {
            Type d = t.GetGenericTypeDefinition();
            bool immutableShape = d == typeof(Nullable<>) || d == typeof(System.Collections.Immutable.ImmutableArray<>) || t.FullName!.StartsWith("System.ValueTuple`", StringComparison.Ordinal);
            if (immutableShape)
                return t.GetGenericArguments().All(a => IsSafeType(a, seen));
            if (d.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
                return false;
        }
        // A struct can change itself through `this = ...` in a method, so only a readonly struct is a value.
        if (t.IsValueType && !IsReadOnlyStruct(t))
            return false;
        seen ??= [];
        if (!seen.Add(t))
            return true;
        if (t.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
            return false;
        return t.GetFields(IlScanner.AllDeclared).Where(f => !f.IsStatic && !f.IsLiteral).All(f => f.IsInitOnly && IsSafeType(f.FieldType, seen));
    }

    // The runtime Nullable<T> and ImmutableArray<T> are readonly structs, but the runtime assemblies do not carry the attribute.
    internal static bool IsReadOnlyStruct(Type t) =>
        t.GetCustomAttributesData().Any(a => a.AttributeType.Name == "IsReadOnlyAttribute")
        || t.IsGenericType && t.GetGenericTypeDefinition() is var d && (d == typeof(Nullable<>) || d == typeof(System.Collections.Immutable.ImmutableArray<>));

    /// <summary>Methods of <paramref name="type"/> and its nested types (closures included).</summary>
    private static IEnumerable<MethodBase> MethodsWithNested(Type type) =>
        new[] { type }.Concat(type.GetNestedTypes(IlScanner.AllDeclared))
            .SelectMany(t => t.GetMethods(IlScanner.AllDeclared).Cast<MethodBase>().Concat(t.GetConstructors(IlScanner.AllDeclared)));

    // An address of a scalar or of a non-readonly struct field is a write path (Interlocked.Increment(ref _n), a method that assigns `this`).
    // The address of a readonly struct cannot change it.
    private static bool IsAddressOfMutable(OpCode op, FieldInfo f) =>
        (op == OpCodes.Ldflda || op == OpCodes.Ldsflda) && (f.FieldType.IsPrimitive || f.FieldType.IsEnum || f.FieldType.IsValueType && !IsReadOnlyStruct(f.FieldType));

    private static IEnumerable<MethodBase> Writers(Type type, FieldInfo f) =>
        MethodsWithNested(type).Where(m => IlScanner.Read(m).Any(i => i.Operand is FieldInfo x && (i.OpCode == OpCodes.Stfld || i.OpCode == OpCodes.Stsfld || IsAddressOfMutable(i.OpCode, x))
            && x.MetadataToken == f.MetadataToken && x.Module == f.Module));

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
                if (scratch || f.IsLiteral)
                    continue;
                if (!IsSafeType(f.FieldType) && Array.IndexOf(PrecomputedFields, name) < 0)
                    found.Add($"{name}: a mutable buffer must be marked [Scratch] (or listed as precomputed)");
                // A field that can be rebound or changed is allowed only when written in a constructor or an <c>Add*Columns</c> binding method.
                if (!f.IsInitOnly)
                {
                    foreach (MethodBase w in Writers(type, f).Where(w => !IsBinding(w)))
                        found.Add($"{name}: written in {w.DeclaringType!.Name}.{w.Name} (mutable state outside a table must be a table column or [Scratch])");
                }
            }
        }
        return found;
    }

    /// <summary>
    /// Non-private methods that give back a value computed from a scratch field of their own class: they load it themselves or call a
    /// method of the class that does (a fixed point over the helpers, so a private helper does not hide the read).
    /// </summary>
    private static List<MemberInfo> Exposers(IEnumerable<Type> types)
    {
        var result = new List<MemberInfo>();
        foreach (Type type in types)
        {
            List<FieldInfo> own = ScratchFields([type]);
            if (own.Count == 0)
                continue;
            var reading = new List<MethodInfo>();
            MethodInfo[] methods = [.. type.GetMethods(IlScanner.AllDeclared).Where(m => m.ReturnType != typeof(void))];
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (MethodInfo m in methods.Where(m => !reading.Contains(m)))
                {
                    if (IlScanner.Read(m).Any(i => i.Operand is FieldInfo f && own.Any(o => o.MetadataToken == f.MetadataToken && o.Module == f.Module)
                        || i.Operand is MethodInfo c && reading.Contains(c)))
                    {
                        reading.Add(m);
                        grew = true;
                    }
                }
            }
            result.AddRange(reading.Where(m => !m.IsPrivate));
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
    public void TheSystems_HaveNoBaseClass_SoDeclaredOnlyScansSeeEveryField() =>
        Assert.All(Systems(Core).Where(t => t.IsClass), t => Assert.Equal(typeof(object), t.BaseType));

    [Fact]
    public void Scratch_IsPrivate_NotATable_AndEveryMutableBufferIsMarked()
    {
        List<string> found = FieldViolations(Systems(Core));
        Assert.True(found.Count == 0, string.Join("\n", found));
    }

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
        Assert.Contains(found, f => f.Contains("Helped") && f.Contains("ViaHelper")); // a private helper reads the buffer
    }

    [Fact]
    public void TheFieldRules_SeeAnUnmarkedBuffer_AScratchColumn_AndAnInternalScratch()
    {
        List<string> found = FieldViolations([typeof(MutantSystem)]);
        Assert.Contains(found, f => f.StartsWith("MutantSystem.Unmarked"));
        Assert.Contains(found, f => f.StartsWith("MutantSystem.Hashed") && f.Contains("hashed"));
        Assert.Contains(found, f => f.StartsWith("MutantSystem.Buffer") && f.Contains("private"));
        Assert.Contains(found, f => f.StartsWith("MutantSystem._counter") && f.Contains("written in MutantSystem.Compute")); // a mutable scalar
        Assert.Contains(found, f => f.StartsWith("MutantSystem._viaRef") && f.Contains("written in")); // a write through a field address
        Assert.Contains(found, f => f.StartsWith("MutantSystem._self") && f.Contains("[Scratch]")); // a struct that is not readonly
        Assert.Contains(found, f => f.StartsWith("MutantSystem._self") && f.Contains("written in")); // its address is taken to call Bump
        Assert.Contains(found, f => f.StartsWith("MutantSystem._blob") && f.Contains("[Scratch]")); // a mutable custom object
        Assert.Contains(found, f => f.Contains("Total") && f.Contains("written in")); // an auto-property backing field
    }
}

// Mutants of Core: the scans must see them. Top-level types, so that none counts as «inside» the other.

internal sealed class MutantSystem : ITickSystem
{
    [Scratch] internal readonly long[] Buffer = new long[4];
    internal readonly List<int> Unmarked = [];
    [Scratch] internal readonly LongColumn[] Hashed = [];
    [Scratch] private readonly int[] _hidden = new int[2];
    private int _counter;
    private int _viaRef;
    private SelfAssigning _self;
    private readonly Blob _blob = new();
    public int Total { get; set; }

    public int SystemId => 0;
    public long[] Peek() => Buffer;
    public int[] Own() => _hidden;
    public int[] ViaHelper() => Help();
    private int[] Help() => _hidden;
    public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd) { _counter++; Total += _blob.N; Interlocked.Increment(ref _viaRef); _self.Bump(); }
    public void Merge(in TickContext context, int chunkCount) { }
    public void Post(in TickContext context) { }
}

// Every field is readonly, yet a call through the field address changes the field.
internal struct SelfAssigning
{
    private readonly int _n;

    public void Bump() => this = new SelfAssigning(_n + 1);

    private SelfAssigning(int n) => _n = n;
}

internal sealed class Blob { public int N = 1; }

internal sealed class Intruder(MutantSystem s)
{
    public long Direct() => s.Buffer[0];
    public long ViaAccessor() => s.Peek()[0];
    public Func<long> ViaLambda() => () => s.Buffer[1];
    public int Fine() => s.Own().Length;
    public int Helped() => s.ViaHelper().Length;
}

