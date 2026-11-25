using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Singularity.Client.Audio.Tests;

/// <summary>TASK-0132 round 2: one name per action in the focus text, words (not marks) for the states under «opens next», legend lines for focus and counters.</summary>
public class TreeWordsTests
{
    static string[] Lines()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "Singularity.Client.Spike", "locale", "ui.csv"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllLines(Path.Combine(dir!.FullName, "src", "Singularity.Client.Spike", "locale", "ui.csv"));
    }

    static string Row(string key) => Lines().First(l => l.StartsWith(key + ","));

    [Fact]
    public void TheFocusHintNamesBothButtonsAsTheyAreCalled()
    {
        string add = Row("inset.focus.add").Split(',').Last(), send = Row("inset.focus.send").Split(',').Last(), hint = Row("inset.focus.empty");
        Assert.Contains("«" + add + "»", hint);
        Assert.Contains("«" + send + "»", hint);
    }

    [Theory]
    [InlineData("card.next.known")]
    [InlineData("card.next.open")]
    [InlineData("card.next.closed")]
    [InlineData("legend.keyfocus")]
    [InlineData("legend.counts")]
    public void TheNewLinesExistInBothLanguagesAndCarryNoMarkGlyphs(string key)
    {
        var row = Row(key);
        Assert.DoesNotContain("◐", row); Assert.DoesNotContain("●", row); Assert.DoesNotContain("○", row);
        Assert.Contains(row.Split(',').Last(), row);
        Assert.True(row.Any(c => c >= 'А' && c <= 'я'), key + " has no Russian text");
    }
}
