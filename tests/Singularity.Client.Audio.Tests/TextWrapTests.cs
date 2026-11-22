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
    public void OneCharacterWiderThanTheLineDoesNotLoop() => Assert.Equal(3, TextWrap.Lines("WWW", 3, W).Count);
}
