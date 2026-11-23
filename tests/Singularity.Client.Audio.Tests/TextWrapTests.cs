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
    public void ParagraphOverflow_IsMeasuredAgainstItsOwnWidth()
    {
        // 30 du narrower than the statement area: a line of 130 du in a 100 du paragraph overflows although it is inside the area
        Assert.True(TextWrap.Overflows("aaaa bbbb", 130f, 100f));
        Assert.False(TextWrap.Overflows("aaaa", 100.4f, 100f));
        Assert.True(TextWrap.Overflows("W", 120f, 100f));   // a single glyph wider than the column is still an overflow
        Assert.All(TextWrap.Lines("aa bb cc dd", 35, W), s => Assert.False(TextWrap.Overflows(s, W(s), 35)));
    }

    [Fact]
    public void IsApart_OnlyBeyondTheNodeRadiusPlusMargin()
    {
        Assert.False(LabelGeometry.IsApart(29f, 10f));
        Assert.True(LabelGeometry.IsApart(31f, 10f));
    }

    [Fact]
    public void OtherIsAsNear_IsTheCoreOfNeedsLeader()
    {
        Assert.True(LabelGeometry.OtherIsAsNear(20f, 30, 0, 10, -5, 60, 10));
        Assert.False(LabelGeometry.OtherIsAsNear(20f, 200, 0, 10, -5, 60, 10));
    }

    [Fact]
    public void OneCharacterWiderThanTheLineDoesNotLoop() => Assert.Equal(3, TextWrap.Lines("WWW", 3, W).Count);
}
