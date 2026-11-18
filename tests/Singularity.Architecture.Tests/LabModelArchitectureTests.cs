using Singularity.Lab.Models.ToyFunnel;

namespace Singularity.Architecture.Tests;

/// <summary>ADR-0007 rev 3 p. 1: the EXP-0001 golden model follows the determinism rules (double stays allowed).</summary>
public class LabModelArchitectureTests
{
    [Fact]
    public void ToyFunnel_FollowsDeterminismRules()
    {
        const string ns = "Singularity.Lab.Models.ToyFunnel";
        Type[] types = typeof(ToyFunnelModel).Assembly.GetTypes()
            .Where(t => t.Namespace is { } n && (n == ns || n.StartsWith(ns + ".", StringComparison.Ordinal)))
            .ToArray();
        Assert.NotEmpty(types);
        List<string> violations = DeterminismRules.Check(types);
        Assert.True(violations.Count == 0, string.Join("; ", violations));
    }
}
