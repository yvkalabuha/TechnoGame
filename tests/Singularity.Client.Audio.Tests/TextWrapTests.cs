using System.Linq;
using Singularity.Client.Spike;
using Xunit;

namespace Singularity.Client.Audio.Tests;

public class TextWrapTests
{
    static float W(string s) => s.Length * 7f;   // a fixed-pitch measure

    [Fact]
    public void WrapCacheHit_AllocatesNothing()
    {
        var c = new WrapCache(); string text = "aaa bbb ccc";
        c.Add(text, 49f, 14, 0, TextWrap.Lines(text, 49f, W));
        c.TryGet(text, 49f, 14, 0, out _);   // warm up
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) Assert.True(c.TryGet(text, 49f, 14, 0, out var l) && l.Count == 2);
        Assert.Equal(0, System.GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void WordsWrapAtSpaces() => Assert.Equal(new[] { "aaa bbb", "ccc" }, TextWrap.Lines("aaa bbb ccc", 7 * 7, W));

    [Fact]
    public void AWordWiderThanTheLineIsBrokenBetweenCharacters()
    {
        var l = TextWrap.Lines("x " + new string('w', 30) + " y", 10 * 7, W);
        Assert.All(l, s => Assert.True(W(s) <= 70, s));
        Assert.Equal("x" + new string('w', 30) + "y", string.Concat(l.Select(s => s.Trim())).Replace(" ", ""));
    }

    [Fact]
    public void AHardBreakEndsALineAndIsNeverMeasuredInAWord()
    {
        Assert.Equal(new[] { "aa", "bb", "cc" }, TextWrap.Lines("aa\nbb cc", 14, W));
    }

    [Fact]
    public void AWordIsNotBrokenInsideASurrogatePair()
    {
        var l = TextWrap.Lines("\U0001F600\U0001F600\U0001F600", 7, W);
        Assert.All(l, s => { Assert.False(char.IsHighSurrogate(s[^1])); Assert.False(char.IsLowSurrogate(s[0])); });
        Assert.Equal(3, l.Count);
    }

    [Fact]
    public void LeaderIsNeededOnlyWhenAnotherNodeIsAsNear()
    {
        var far = new (float, float)[] { (200, 0) };
        var near = new (float, float)[] { (30, 0) };
        Assert.False(LabelGeometry.NeedsLeader(0, 0, far, 10, -5, 60, 10));
        Assert.True(LabelGeometry.NeedsLeader(0, 0, near, 10, -5, 60, 10));
    }

    [Fact]
    public void OneCharacterWiderThanTheLineDoesNotLoop() => Assert.Equal(3, TextWrap.Lines("WWW", 3, W).Count);
}
