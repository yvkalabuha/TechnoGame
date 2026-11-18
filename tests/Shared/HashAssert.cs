namespace Singularity.TestSupport;

/// <summary>
/// The only way tests compare state hashes (ADR-0017 п. 11). Fails when either side is null, so a view that skipped its hash
/// (<c>SliceView.StateHash == null</c>) can never pass a <c>null == null</c> comparison.
/// </summary>
public static class HashAssert
{
    public static void Equal(string? expected, string? actual, string? message = null)
    {
        Assert.True(expected is not null, "HashAssert: the expected hash is null" + Suffix(message));
        Assert.True(actual is not null, "HashAssert: the actual hash is null" + Suffix(message));
        Assert.True(string.Equals(expected, actual, StringComparison.Ordinal), $"hash differs: expected {expected}, actual {actual}" + Suffix(message));
    }

    public static void NotEqual(string? left, string? right, string? message = null)
    {
        Assert.True(left is not null, "HashAssert: the left hash is null" + Suffix(message));
        Assert.True(right is not null, "HashAssert: the right hash is null" + Suffix(message));
        Assert.True(!string.Equals(left, right, StringComparison.Ordinal), $"hash is the same: {left}" + Suffix(message));
    }

    private static string Suffix(string? message) => message is null ? "" : ": " + message;
}
