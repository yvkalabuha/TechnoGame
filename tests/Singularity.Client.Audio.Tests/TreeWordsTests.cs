using System;
using System.IO;
using System.Linq;
using Singularity.Client.Spike;
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

    [Fact]
    public void TheFocusHintOnlyNamesButtonsTheInspectorAlwaysShows()
    {
        // The two names are the two buttons of the focus section (add is always drawn there, disabled with a reason when nothing fits).
        Assert.Contains("inset.focus.addwhy,", string.Join("\n", Lines()));
        Assert.Contains("«В фокус освоения»", Row("inset.focus.empty"));
        Assert.Equal("В фокус освоения", Row("inset.focus.add").Split(',').Last());
    }

    [Theory]
    [InlineData(1, 1, 1, 1)]
    [InlineData(3, 2, 3, 2)]
    [InlineData(6, 4, 5, 4)]
    public void TheLegendCloseLineStandsBelowTheLastParagraphForAnyLineCount(int root, int focus, int counts, int dial)
    {
        var (h, close) = LegendLayout.TreeLegendHeight(root, focus, counts, dial);
        float countsBottom = 12 + 15 + 10 * 22 + 16 + 19 + root * 18 + 12 + focus * 18 + 8 + counts * 18;
        Assert.True(close >= countsBottom + 14 - 0.01f, "close line overlaps the counters line");
        Assert.True(h >= close + 12 - 0.01f, "sheet ends above the close line");
        Assert.True(h >= 352f);
    }

    [Fact]
    public void ALongerTextMakesATallerSheet() =>
        Assert.True(LegendLayout.TreeLegendHeight(2, 2, 4, 2).Height > LegendLayout.TreeLegendHeight(2, 2, 1, 2).Height);

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
