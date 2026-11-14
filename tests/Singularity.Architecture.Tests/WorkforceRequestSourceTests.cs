using System.Reflection;
using Singularity.Core.Industry;
using Singularity.Core.Products;

namespace Singularity.Architecture.Tests;

/// <summary>
/// DEBT-0012, spec §3.5 A26: <c>act[site, c]</c> reads the skilled grants by <c>requester_id mod C</c>, so only a request whose id is
/// <c>ProductsLayout.SkilledRequester(line, cluster)</c> may name the skilled category. Instead of tracing data flow, the production code
/// has one factory per category (<c>ProductsLayout.SkilledRequest</c>, <c>EngineerRequest</c>) and this test closes every other way to get a request:
/// no Core method but the factories calls the <see cref="WorkforceRequest"/> constructor, an init setter (what a <c>with</c> expression compiles to)
/// or <c>initobj</c> (<c>default</c>, <c>new()</c>); the factories are called only from the listed methods.
/// <c>WorkforceRequestFactoryTests</c> (Core.Tests) pins what the factories build.
/// </summary>
public class WorkforceRequestSourceTests
{
    private static readonly Type[] Core = [.. typeof(IndustrySystem).Assembly.GetTypes()];

    private static readonly string[] Factories = [nameof(ProductsLayout.SkilledRequest), nameof(ProductsLayout.EngineerRequest)];

    // Owner type and method (a closure's «<Method>b__» counts as Method) that may call each factory.
    private static readonly string[] CallSites =
    [
        "SkilledRequest:ProductsSystem.Produce",
        "EngineerRequest:ProductsSystem.BureausRequestOrIdle",
        "EngineerRequest:ProductsSystem.DetectDefects",
        "EngineerRequest:PolicySystem.Compute",
    ];

    private static IEnumerable<MethodBase> Methods(Type t) =>
        t.GetMethods(IlScanner.AllDeclared).Cast<MethodBase>().Concat(t.GetConstructors(IlScanner.AllDeclared));

    private static bool Makes(MethodBase m) => IlScanner.Read(m).Any(i =>
        i.Operand is MethodBase c && c.DeclaringType == typeof(WorkforceRequest) && (c.IsConstructor || c.Name.StartsWith("set_", StringComparison.Ordinal) || c.Name == "<Clone>$")
        || i.OpCode == System.Reflection.Emit.OpCodes.Initobj && i.Operand as Type == typeof(WorkforceRequest));

    private static List<MethodBase> Creators(IEnumerable<Type> types) => [.. types.SelectMany(Methods).Where(Makes)];

    private static bool IsFactory(MethodBase m) => m.DeclaringType == typeof(ProductsLayout) && Factories.Contains(m.Name);

    private static string Site(string factory, MethodBase m)
    {
        Type t = m.DeclaringType!;
        while (t.DeclaringType is { } outer)
            t = outer;
        string name = m.Name.StartsWith('<') ? m.Name[1..m.Name.IndexOf('>')] : m.Name;
        return $"{factory}:{t.Name}.{name}";
    }

    private static List<string> Violations(IEnumerable<Type> types) =>
    [
        .. Creators(types).Where(m => !IsFactory(m)).Select(m => $"{m.DeclaringType}.{m.Name} makes a WorkforceRequest outside the factories"),
        .. types.SelectMany(Methods).SelectMany(m => IlScanner.Read(m).Select(i => i.Operand).OfType<MethodBase>()
                .Where(c => c.DeclaringType == typeof(ProductsLayout) && Factories.Contains(c.Name)).Select(c => (Method: m, Site: Site(c.Name, m))))
            .Where(x => !CallSites.Contains(x.Site)).Select(x => $"{x.Method.DeclaringType}.{x.Method.Name} calls a request factory ({x.Site})").Distinct(),
    ];

    [Fact]
    public void WorkforceRequests_AreCreatedOnlyByTheTwoProductsLayoutFactories()
    {
        // Guards against a vacuous scan: both factories are seen, and the callers go through them.
        List<MethodBase> creators = Creators(Core);
        Assert.Equal([.. Factories.Order(StringComparer.Ordinal)], [.. creators.Select(m => m.Name).Order(StringComparer.Ordinal)]);
        Assert.Empty(Violations(Core));
    }

    private sealed class Rogue(IndustryMailbox box)
    {
        public void Ask(int site, int category) => box.ReserveWorkforce(0, new WorkforceRequest(site, category, 3, 1));
    }

    private sealed class FactoryCaller(ProductsLayout l)
    {
        public WorkforceRequest Skilled() => l.SkilledRequest(0, 99, 99, 1);

        public WorkforceRequest Engineer() => ProductsLayout.EngineerRequest(0, 3, 1);
    }

    private sealed class Cloner
    {
        public WorkforceRequest Retarget(WorkforceRequest r) => r with { Category = 1 };

        public WorkforceRequest Blank() => default;
    }

    [Fact]
    public void TheCheck_SeesARequestWithAFreeCategoryOrRequesterId() =>
        Assert.Contains(Violations([typeof(Rogue)]), f => f.Contains("Rogue.Ask"));

    [Fact]
    public void TheCheck_SeesAFactoryCallFromAnUnlistedMethod()
    {
        List<string> found = Violations([typeof(FactoryCaller)]);
        Assert.Contains(found, f => f.Contains("FactoryCaller.Skilled") && f.Contains("calls a request factory"));
        Assert.Contains(found, f => f.Contains("FactoryCaller.Engineer") && f.Contains("calls a request factory"));
    }

    [Fact]
    public void TheCheck_SeesAWithExpressionAndADefaultRequest()
    {
        List<string> found = Violations([typeof(Cloner)]);
        Assert.Contains(found, f => f.Contains("Cloner.Retarget"));
        Assert.Contains(found, f => f.Contains("Cloner.Blank"));
    }
}
