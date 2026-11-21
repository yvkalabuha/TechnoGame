using System.Linq;
using Singularity.Client.Spike;
using Xunit;

namespace Singularity.Client.Audio.Tests;

public class TextWrapTests
{
    static float W(string s) => s.Length * 7f;   // a fixed-pitch measure

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
