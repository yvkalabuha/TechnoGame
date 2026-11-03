using System.Reflection;
using System.Xml.Linq;
using Singularity.Core;
using Singularity.Core.Industry;
using Singularity.Core.Knowledge;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;
using Singularity.Session;

namespace Singularity.Architecture.Tests;

/// <summary>
/// ADR-0014 п. 1, SLICE_COMPOSITION §10: the slice world is assembled in <c>Singularity.Session</c> only; Core does not know Session;
/// the slice systems do not read <c>TickContext.Rng</c> (their streams are explicit, so FIX_* cannot be bypassed); the adapters of
/// the capability and product layers have no RNG.
/// </summary>
public class SessionArchitectureTests
{
    private static readonly Assembly Session = typeof(SliceSession).Assembly;
    private static readonly Assembly Core = typeof(IndustrySystem).Assembly;
    private static readonly Assembly Lab = typeof(Singularity.Lab.Program).Assembly;

    [Fact]
    public void Session_ReferencesOnlyCoreAndKernel()
    {
        string[] references = [.. Session.GetReferencedAssemblies().Select(a => a.Name ?? "")
            .Where(n => n.StartsWith("Singularity", StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal)];
        Assert.Equal(["Singularity.Core", "Singularity.Kernel"], references);
        string csproj = Path.Combine(RepoRoot(), "src", "Singularity.Session", "Singularity.Session.csproj");
        string[] projects = [.. XDocument.Load(csproj).Descendants("ProjectReference")
            .Select(r => ((string?)r.Attribute("Include") ?? "").Replace('\\', '/'))
            .Select(path => path[(path.LastIndexOf('/') + 1)..]).OrderBy(n => n, StringComparer.Ordinal)];
        Assert.Equal(["Singularity.Core.csproj", "Singularity.Kernel.csproj"], projects);
    }

    [Fact]
    public void Session_FollowsDeterminismRules_AndHasNoDouble()
    {
        // ADR-0014: no double in Session; the index of local_n is looked up, never enumerated.
        var v = DeterminismRules.Check(Session.GetTypes(), forbidDouble: true); Assert.True(v.Count == 0, string.Join("; ", v));
    }

    [Fact]
    public void Core_DoesNotReferenceSession_OrLab()
    {
        Assert.DoesNotContain(Core.GetReferencedAssemblies(), a => a.Name is "Singularity.Session" or "Singularity.Lab");
    }

    private static readonly Type[] SliceSystems = [typeof(KnowledgeSystem), typeof(IndustrySystem), typeof(ProductsSystem), typeof(PolicySystem)];

    [Fact]
    public void SliceSystems_AreConstructedOnlyInSession_OrInMarkedSingleLayerStands()
    {
        var offenders = new List<string>();
        foreach (Assembly assembly in new[] { Core, Lab, Session })
        {
            foreach (Type type in assembly.GetTypes())
            {
                if (assembly == Session || IsStand(type))
                    continue;
                foreach (MethodBase m in Methods(type))
                {
                    foreach (IlScanner.Instruction i in IlScanner.Read(m))
                    {
                        if (i.OpCode == System.Reflection.Emit.OpCodes.Newobj && i.Operand is ConstructorInfo c && Array.IndexOf(SliceSystems, c.DeclaringType) >= 0)
                            offenders.Add($"{type}.{m.Name} constructs {c.DeclaringType!.Name}");
                    }
                }
            }
        }
        Assert.Empty(offenders);
    }

    [Fact]
    public void SingleLayerStands_AreMarked_AndExist()
    {
        Type[] stands = [.. Lab.GetTypes().Where(t => t.GetCustomAttribute<SingleLayerStandAttribute>() is not null)];
        Assert.Contains(stands, t => t.Name == "LineStand");
        Assert.Contains(stands, t => t.Name == "C3Stand");
        Assert.All(stands, t => Assert.False(string.IsNullOrWhiteSpace(t.GetCustomAttribute<SingleLayerStandAttribute>()?.Reason ?? "")));
    }

    [Fact]
    public void SessionWorld_IsReachedOnlyThroughTheSession()
    {
        Assert.False(typeof(SliceSession).Assembly.GetType("Singularity.Session.SliceWorld")!.IsPublic);
        Assert.True(typeof(SliceSession).IsPublic);
        foreach (string name in new[] { "KnowledgeInputsAdapter", "IndustryInputsAdapter", "ProductsInputsAdapter", "PolicyInputsAdapter" })
            Assert.False(Session.GetType("Singularity.Session." + name)!.IsPublic);
    }

    [Fact]
    public void SliceSystems_DoNotReadTheTickContextRng()
    {
        Type[] types = [.. Core.GetTypes().Where(t => t.Namespace is "Singularity.Core.Policy" or "Singularity.Core.Knowledge" or "Singularity.Core.Commands"
            or "Singularity.Core.Events" or "Singularity.Core.Industry" or "Singularity.Core.Products")];
        Assert.Contains(typeof(PolicySystem), types);
        Assert.Contains(typeof(KnowledgeSystem), types);
        var reads = new List<string>();
        foreach (Type type in types.Concat(Session.GetTypes()))
        {
            foreach (MethodBase m in Methods(type))
            {
                foreach (IlScanner.Instruction i in IlScanner.Read(m))
                {
                    if (i.Operand is MemberInfo { DeclaringType: var d, Name: "get_Rng" } && d == typeof(TickContext))
                        reads.Add($"{type}.{m.Name}");
                }
            }
        }
        Assert.Empty(reads);
    }

    [Fact]
    public void InputAdapters_OfIndustryAndProducts_UseNoRng()
    {
        var uses = new List<string>();
        foreach (Type type in Session.GetTypes().Where(t => t.FullName!.Contains("IndustryInputsAdapter") || t.FullName.Contains("ProductsInputsAdapter") || t.FullName.Contains("PolicyInputsAdapter")))
        {
            foreach (FieldInfo f in type.GetFields(IlScanner.AllDeclared))
            {
                if (f.FieldType == typeof(CounterRng))
                    uses.Add($"{type}.{f.Name} (field)");
            }
            foreach (MethodBase m in Methods(type))
            {
                if (m.GetParameters().Any(p => p.ParameterType == typeof(CounterRng)) || (m is MethodInfo mi && mi.ReturnType == typeof(CounterRng)))
                    uses.Add($"{type}.{m.Name} (signature)");
                foreach (IlScanner.Instruction i in IlScanner.Read(m))
                {
                    if (i.Operand is MemberInfo member && (member.DeclaringType == typeof(CounterRng) || member as Type == typeof(CounterRng)
                        || member.DeclaringType == typeof(KnowledgeRng) || member.DeclaringType == typeof(PolicyRng)))
                        uses.Add($"{type}.{m.Name} → {member.DeclaringType?.Name}.{member.Name}");
                }
            }
        }
        Assert.Empty(uses);
        // The check sees the knowledge adapter's draw (canary): it is the one adapter allowed to call the K stream.
        Type knowledge = Session.GetType("Singularity.Session.KnowledgeInputsAdapter")!;
        Assert.Contains(Methods(knowledge).SelectMany(IlScanner.Read), i => i.Operand is MemberInfo { DeclaringType: var d } && d == typeof(KnowledgeRng));
    }

    private static bool IsStand(Type type)
    {
        for (Type? t = type; t is not null; t = t.DeclaringType)
        {
            if (t.GetCustomAttribute<SingleLayerStandAttribute>() is not null)
                return true;
        }
        return false;
    }

    private static IEnumerable<MethodBase> Methods(Type type) =>
        type.GetMethods(IlScanner.AllDeclared).Cast<MethodBase>().Concat(type.GetConstructors(IlScanner.AllDeclared));

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Singularity.sln")))
                return dir.FullName;
        }
        throw new DirectoryNotFoundException("Singularity.sln not found above the test output directory.");
    }
}
