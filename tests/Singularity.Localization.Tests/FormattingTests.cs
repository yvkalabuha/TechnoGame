using System.Globalization;
using Linguini.Shared.Types.Bundle;

namespace Singularity.Localization.Tests;

/// <summary>ADR-0018 §3.3 and spike S1: plurals (CLDR), NUMBER, DATE, cases, gender, isolation, threads.</summary>
public class FormattingTests : IDisposable
{
    readonly TestKit _kit = new();
    public void Dispose() => _kit.Dispose();

    const string EnItems = "ui-items = { NUMBER($n) ->\n    [one] { NUMBER($n) } item\n   *[other] { NUMBER($n) } items\n}\n";
    const string RuItems = "ui-items = { NUMBER($n) ->\n    [one] { NUMBER($n) } предмет\n    [few] { NUMBER($n) } предмета\n    [many] { NUMBER($n) } предметов\n   *[other] { NUMBER($n) } предмета\n}\n";
    const string RuBare = "ui-bare = { $n ->\n    [one] { $n } предмет\n    [few] { $n } предмета\n    [many] { $n } предметов\n   *[other] { $n } предмета\n}\n";

    Loc Ru(string extraRu = "", string extraEn = "") => _kit.LocWithFormats(EnItems + extraEn, RuItems + extraRu, "ru");

    [Theory]
    [InlineData(1, "1 предмет")]
    [InlineData(2, "2 предмета")]
    [InlineData(5, "5 предметов")]
    [InlineData(11, "11 предметов")]
    [InlineData(21, "21 предмет")]
    [InlineData(111, "111 предметов")]
    [InlineData(22, "22 предмета")]
    [InlineData(0, "0 предметов")]
    public void RussianCardinalPlurals_CoverTheCldrCategories(int n, string expected) =>
        Assert.Equal(expected, Ru().T("ui-items", ("n", n)));

    [Fact]
    public void RussianFraction_IsOther()
    {
        Assert.Equal("1,5 предмета", Ru().T("ui-items", ("n", 1.5)));
        Assert.Equal("0,5 предмета", Ru().T("ui-items", ("n", 0.5)));
    }

    [Fact]
    public void OneWithVisibleFraction_IsOther_NotOne()
    {
        // «1,0» has a visible fraction digit: CLDR operand v=1 gives `other` (spike S1, #304).
        string text = _kit.LocWithFormats(EnItems.Replace("NUMBER($n) ->", "NUMBER($n, minimumFractionDigits: 1) ->"),
            RuItems.Replace("NUMBER($n) ->", "NUMBER($n, minimumFractionDigits: 1) ->"), "ru").T("ui-items", ("n", 1));
        Assert.Contains("предмета", text, StringComparison.Ordinal);
        Assert.DoesNotContain("предмет ", text + " ", StringComparison.Ordinal);
    }

    [Fact]
    public void BareNumberArguments_AreWrapped_AndSelectByVisibleNumber()
    {
        Loc loc = _kit.LocWithFormats("ui-x = x\n", RuBare, "ru");
        Assert.Equal("21 предмет", loc.T("ui-bare", ("n", 21)));
        Assert.Equal("1\u00A0234 предмета", loc.T("ui-bare", ("n", 1234.0)));
    }

    [Fact]
    public void EnglishPlurals_OneAndOther()
    {
        Loc loc = _kit.LocWithFormats(EnItems, RuItems, "en");
        Assert.Equal("1 item", loc.T("ui-items", ("n", 1)));
        Assert.Equal("2 items", loc.T("ui-items", ("n", 2)));
        Assert.Equal("1,234.5 items", loc.T("ui-items", ("n", 1234.5)));
        Assert.Equal("0 items", loc.T("ui-items", ("n", 0)));
    }

    [Theory]
    [InlineData("ru", 1234567.891, "1 234 567,891")]
    [InlineData("en", 1234567.891, "1,234,567.891")]
    [InlineData("ru", -1234.5, "−1 234,5")]
    [InlineData("en", -0.5, "−0.5")]
    [InlineData("ru", 12.0, "12")]
    public void NumberFacade_UsesLocaleSeparators(string lang, double value, string expected) =>
        Assert.Equal(expected, _kit.LocWithFormats("ui-x = x\n", "ui-x = x\n", lang).Number(value));

    [Fact]
    public void NumberFunction_PercentCompactAndGrouping()
    {
        const string Tpl = "ui-p = { NUMBER($v, style: \"percent\", maximumFractionDigits: 1) }\nui-c = { NUMBER($v, notation: \"compact\") }\nui-g = { NUMBER($v, useGrouping: \"false\") }\nui-f = { NUMBER($v, minimumFractionDigits: 2) }\n";
        Loc ru = _kit.LocWithFormats(Tpl, Tpl, "ru");
        Loc en = _kit.LocWithFormats(Tpl, Tpl, "en");
        Assert.Equal("25,5 %", ru.T("ui-p", ("v", 0.255)));
        Assert.Equal("25.5%", en.T("ui-p", ("v", 0.255)));
        Assert.Equal("12,5 тыс.", ru.T("ui-c", ("v", 12500)));
        Assert.Equal("3,1 млн", ru.T("ui-c", ("v", 3_100_000)));
        Assert.Equal("2 млрд", ru.T("ui-c", ("v", 2_000_000_000L)));
        Assert.Equal("12.5K", en.T("ui-c", ("v", 12500)));
        Assert.Equal("3.1M", en.T("ui-c", ("v", 3_100_000)));
        Assert.Equal("2B", en.T("ui-c", ("v", 2_000_000_000L)));
        Assert.Equal("999", en.T("ui-c", ("v", 999)));
        Assert.Equal("1234567", en.T("ui-g", ("v", 1234567)));
        Assert.Equal("1.50", en.T("ui-f", ("v", 1.5)));
    }

    [Fact]
    public void NumberAndDate_DoNotDependOnThreadCulture()
    {
        const string Tpl = "ui-n = { NUMBER($v) } | { $v } | { DATE($d, format: \"dmy\") }\n";
        Loc loc = _kit.LocWithFormats(Tpl, Tpl, "ru");
        var args = new (string, object)[] { ("v", 1234567.891), ("d", new GameDate(1945, 3, 9)) };
        string baseline = loc.T("ui-n", args);
        Assert.Equal("1 234 567,891 | 1 234 567,891 | 9 марта 1945", baseline);
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            foreach (string name in new[] { "", "ru-RU", "en-US", "de-DE", "tr-TR", "ar-SA", "fa-IR", "ja-JP", "th-TH" })
            {
                CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
                Assert.Equal(baseline, loc.T("ui-n", args));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("ru", "dmy", 1945, 3, 9, "9 марта 1945")]
    [InlineData("ru", "dmy", 1945, 2, 28, "28 февраля 1945")]
    [InlineData("ru", "my", 1945, 5, 1, "май 1945")]
    [InlineData("ru", "y", 1945, 5, 1, "1945")]
    [InlineData("en", "dmy", 1945, 3, 9, "March 9, 1945")]
    [InlineData("en", "my", 1910, 12, 31, "December 1910")]
    public void DateFacade_GameCalendar(string lang, string format, int y, int m, int d, string expected) =>
        Assert.Equal(expected, _kit.LocWithFormats("ui-x = x\n", "ui-x = x\n", lang).Date(new GameDate(y, m, d), format));

    [Fact]
    public void DateFunction_InsideAMessage_AndMessageReferenceSeesCallerArguments()
    {
        const string Tpl = "ui-when = { DATE($d, format: \"dmy\") }\n";
        Assert.Equal("9 марта 1945", _kit.LocWithFormats(Tpl, Tpl, "ru").T("ui-when", ("d", new GameDate(1945, 3, 9))));
    }

    // ---- DataRef: case and gender ----

    const string RuTerms = """
        -actor-SUN = { $case ->
           *[nom] СССР
            [ins] СССР
        }
            .gender = masc
        -actor-FRA = { $case ->
           *[nom] Франция
            [gen] Франции
            [ins] Францией
        }
            .gender = fem
        ui-war = война с { NAME($c, case: "ins") }
        ui-declared = { GENDER($a) ->
            [fem] { NAME($a) } объявила
           *[masc] { NAME($a) } объявил
        }
        ui-static = { -actor-FRA(case: "gen") }
        """;

    const string EnTerms = """
        -actor-SUN = USSR
            .gender = neut
        -actor-FRA = France
            .gender = neut
        ui-war = war with { NAME($c, case: "ins") }
        ui-declared = { NAME($a) } declared
        ui-static = { -actor-FRA }
        """;

    [Fact]
    public void RussianCaseAndGender_ThroughDataRef()
    {
        Loc loc = _kit.LocWithFormats(EnTerms, RuTerms, "ru");
        Assert.Equal("война с Францией", loc.T("ui-war", ("c", new DataRef("actor-FRA"))));
        Assert.Equal("Франция объявила", loc.T("ui-declared", ("a", new DataRef("actor-FRA"))));
        Assert.Equal("СССР объявил", loc.T("ui-declared", ("a", new DataRef("actor-SUN"))));
        Assert.Equal("Франции", loc.T("ui-static"));
        Assert.Equal("Франции", loc.Name(new DataRef("actor-FRA"), Field.Name, "gen"));
    }

    [Fact]
    public void EnglishIgnoresCase()
    {
        Loc loc = _kit.LocWithFormats(EnTerms, RuTerms, "en");
        Assert.Equal("war with France", loc.T("ui-war", ("c", new DataRef("actor-FRA"))));
        Assert.Equal("France", loc.Name(new DataRef("actor-FRA"), Field.Name, "gen"));
    }

    [Fact]
    public void MissingTerm_UsesEnglish_ThenMarker()
    {
        var warnings = new List<string>();
        Loc loc = _kit.LocWithFormats("-actor-SWE = Sweden\n    .gender = neut\n" + EnTerms, RuTerms, "ru", warnings);
        Assert.Equal("Sweden", loc.Name(new DataRef("actor-SWE")));
        Assert.Equal("[[-actor-XXX]]", loc.Name(new DataRef("actor-XXX")));
        Assert.Single(warnings, w => w.Contains("actor-XXX", StringComparison.Ordinal));
    }

    [Fact]
    public void IsolationMarks_AreOff()
    {
        Loc loc = _kit.LocWithFormats("ui-hi = Hi, { $who }!\n", "ui-hi = Привет, { $who }!\n", "ru");
        string text = loc.T("ui-hi", ("who", "Мир"));
        Assert.Equal("Привет, Мир!", text);
        Assert.DoesNotContain('⁨', text);
    }

    [Fact]
    public void ConcurrentReads_GiveNoDifferences()
    {
        Loc loc = _kit.LocWithFormats(EnItems + EnTerms, RuItems + RuTerms, "ru");
        string Run(int i) => loc.T("ui-items", ("n", i % 120)) + "|" + loc.T("ui-war", ("c", new DataRef("actor-FRA")))
            + "|" + loc.Number(i * 1.5) + "|" + loc.Date(new GameDate(1945, 1 + i % 12, 1 + i % 28));
        string[] expected = [.. Enumerable.Range(0, 400).Select(Run)];
        var bad = new System.Collections.Concurrent.ConcurrentBag<int>();
        Parallel.For(0, 16, new ParallelOptions { MaxDegreeOfParallelism = 16 }, t =>
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(t % 2 == 0 ? "tr-TR" : "ar-SA");
            for (int rep = 0; rep < 10; rep++)
                for (int i = 0; i < 400; i++)
                    if (Run(i) != expected[i])
                        bad.Add(i);
        });
        Assert.Empty(bad);
    }

    [Fact]
    public void UnsupportedArgumentType_IsAnError()
    {
        Loc loc = _kit.LocWithFormats("ui-a = { $x }\n", "ui-a = { $x }\n", "en");
        Assert.Throws<ArgumentException>(() => loc.T("ui-a", ("x", new object())));
    }

    [Fact]
    public void NonFiniteAndHugeNumbers_ShowAMarker_NotACrash()
    {
        Loc loc = _kit.LocWithFormats("ui-n = { $v }\nui-f = { NUMBER($v) }\n", "ui-n = { $v }\n", "en");
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1e300, -1e29 })
        {
            Assert.Equal("[[number]]", loc.Number(bad));
            Assert.Equal("[[number]]", loc.T("ui-n", ("v", bad)));
            Assert.Equal("[[number]]", loc.T("ui-f", ("v", bad)));
        }
        Assert.Equal("1,000", loc.Number(1000));
    }
}
