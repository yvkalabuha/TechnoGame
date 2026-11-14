using System.Reflection;
using Singularity.Core.Industry;
using Singularity.Core.Policy;
using Singularity.Core.Products;

namespace Singularity.Architecture.Tests;

/// <summary>
/// DEBT-0012, spec §3.5 A26: <c>act[site, c]</c> reads the skilled grants by <c>requester_id mod C</c>, so only a request whose id is
/// <c>ProductsLayout.SkilledRequester(line, cluster)</c> may name the skilled category. The layer that creates workforce requests is
/// fixed: Products (lines, bureaus, fixes) and Policy (the quality programme); a method that asks for the skilled category takes the id from
/// <c>SkilledRequester</c>; the other requesters ask for engineers.
/// </summary>
public class WorkforceRequestSourceTests
{
    private static readonly Type[] Core = [.. typeof(IndustrySystem).Assembly.GetTypes()];

    private static bool Calls(MethodBase m, Func<MethodBase, bool> pick) => IlScanner.Read(m).Any(i => i.Operand is MethodBase c && pick(c));

    private static IEnumerable<MethodBase> Methods(Type t) =>
        t.GetMethods(IlScanner.AllDeclared).Cast<MethodBase>().Concat(t.GetConstructors(IlScanner.AllDeclared));

    private static List<MethodBase> Creators(IEnumerable<Type> types) =>
        [.. types.SelectMany(Methods).Where(m => Calls(m, c => c.DeclaringType == typeof(WorkforceRequest) && c.IsConstructor))];

    private static List<string> Violations(IEnumerable<Type> types, IReadOnlyCollection<string> allowedTypes) =>
    [
        .. Creators(types).Where(m => !allowedTypes.Contains(WhoOwns(m))).Select(m => $"{m.DeclaringType}.{m.Name} creates a WorkforceRequest"),
        .. Creators(types).Where(m => Calls(m, c => c.DeclaringType == typeof(IndustryLayout) && c.Name == "get_SkilledCategory"
                || c.DeclaringType == typeof(ProductsLayout) && c.Name == "get_SkilledCategory")
                && !Calls(m, c => c.DeclaringType == typeof(ProductsLayout) && c.Name == nameof(ProductsLayout.SkilledRequester)))
            .Select(m => $"{m.DeclaringType}.{m.Name} asks for the skilled category without SkilledRequester"),
    ];

    private static string WhoOwns(MethodBase m)
    {
        Type t = m.DeclaringType!;
        while (t.DeclaringType is { } outer)
            t = outer;
        return t.Name;
    }

    [Fact]
    public void WorkforceRequests_AreCreatedOnlyByProductsAndPolicy_AndSkilledOnesCarrySkilledRequester()
    {
        List<MethodBase> creators = Creators(Core);
        // Guards against a vacuous scan: the three known sources are seen, and the skilled one is among them.
        Assert.Contains(creators, m => WhoOwns(m) == nameof(ProductsSystem));
        Assert.Contains(creators, m => WhoOwns(m) == nameof(PolicySystem));
        Assert.Contains(creators, m => Calls(m, c => c.Name == nameof(ProductsLayout.SkilledRequester)));
        Assert.Empty(Violations(Core, [nameof(ProductsSystem), nameof(PolicySystem)]));
    }

    private sealed class Rogue(ProductsLayout l, IndustryMailbox box)
    {
        public void Ask(int site) => box.ReserveWorkforce(0, new WorkforceRequest(site, l.SkilledCategory, 3, 1));
    }

    [Fact]
    public void TheCheck_SeesARequestWithAFreeRequesterId()
    {
        List<string> found = Violations([typeof(Rogue)], [nameof(ProductsSystem)]);
        Assert.Contains(found, f => f.Contains("Rogue.Ask") && f.Contains("without SkilledRequester"));
        Assert.Contains(found, f => f.Contains("Rogue.Ask") && f.Contains("creates"));
    }
}
