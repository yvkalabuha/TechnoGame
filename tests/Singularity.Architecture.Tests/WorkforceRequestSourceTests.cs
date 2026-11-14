using System.Reflection;
using Singularity.Core.Industry;
using Singularity.Core.Products;

namespace Singularity.Architecture.Tests;

/// <summary>
/// DEBT-0012, spec §3.5 A26: <c>act[site, c]</c> reads the skilled grants by <c>requester_id mod C</c>, so only a request whose id is
/// <c>ProductsLayout.SkilledRequester(line, cluster)</c> may name the skilled category. Instead of tracing data flow, the production code
/// has one factory per category (<c>ProductsLayout.SkilledRequest</c>, <c>EngineerRequest</c>) and this test allows no other method of Core
/// to call the <see cref="WorkforceRequest"/> constructor; <c>WorkforceRequestFactoryTests</c> (Core.Tests) pins what the factories build.
/// </summary>
public class WorkforceRequestSourceTests
{
    private static readonly Type[] Core = [.. typeof(IndustrySystem).Assembly.GetTypes()];

    private static readonly string[] Factories = [nameof(ProductsLayout.SkilledRequest), nameof(ProductsLayout.EngineerRequest)];

    private static IEnumerable<MethodBase> Methods(Type t) =>
        t.GetMethods(IlScanner.AllDeclared).Cast<MethodBase>().Concat(t.GetConstructors(IlScanner.AllDeclared));

    private static List<MethodBase> Creators(IEnumerable<Type> types) =>
        [.. types.SelectMany(Methods).Where(m => IlScanner.Read(m).Any(i => i.Operand is MethodBase c && c.DeclaringType == typeof(WorkforceRequest) && c.IsConstructor))];

    private static bool IsFactory(MethodBase m) => m.DeclaringType == typeof(ProductsLayout) && Factories.Contains(m.Name);

    private static List<string> Violations(IEnumerable<Type> types) =>
        [.. Creators(types).Where(m => !IsFactory(m)).Select(m => $"{m.DeclaringType}.{m.Name} creates a WorkforceRequest outside the factories")];

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

    [Fact]
    public void TheCheck_SeesARequestWithAFreeCategoryOrRequesterId()
    {
        Assert.Contains(Violations([typeof(Rogue)]), f => f.Contains("Rogue.Ask"));
    }
}
