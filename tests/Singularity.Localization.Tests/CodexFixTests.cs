using System.Buffers.Binary;
using Singularity.Core.Data;
using Linguini.Shared.Types.Bundle;

namespace Singularity.Localization.Tests;

/// <summary>Regression tests for the Codex review of TASK-0074 (TASK-0076).</summary>
public class CodexFixTests : IDisposable
{
    readonly TestKit _kit = new();
    public void Dispose() => _kit.Dispose();

    [Fact]
    public void NumberAndDate_FallBackToEnglishFormats_InAnIncompleteLocale()
    {
        string enFmt = File.ReadAllText(Path.Combine(TestKit.CoreDir, "locale", "en", "fmt.ftl"));
        ModLocaleSource en = _kit.Mod("fmt", [], ("en", "fmt.ftl", enFmt));
        ModLocaleSource de = _kit.Mod("de", ["fmt"], ("de", "t.ftl", "ui-x = X\n"));
        var loc = new Loc(LocaleCatalog.Load([en, de]), "de");
        Assert.Equal("1,234.5", loc.Number(1234.5, 0, 1));
        Assert.DoesNotContain("[[", loc.Date(new GameDate(1945, 2, 9)), StringComparison.Ordinal);
    }

    [Fact]
    public void NumberFunctionInMessage_FallsBackToEnglishFormats()
    {
        string enFmt = File.ReadAllText(Path.Combine(TestKit.CoreDir, "locale", "en", "fmt.ftl"));
        ModLocaleSource en = _kit.Mod("fmt", [], ("en", "fmt.ftl", enFmt));
        ModLocaleSource de = _kit.Mod("de", ["fmt"], ("de", "t.ftl", "ui-n = { NUMBER($n) }\n"));
        var loc = new Loc(LocaleCatalog.Load([en, de]), "de");
        Assert.Equal("12,345", loc.T("ui-n", ("n", 12345)));
    }

    [Fact]
    public void RuntimeLoad_SkipsOrphans_AndWarnsAboutOrphansAndStaleTranslations()
    {
        ModLocaleSource core = ModLocaleSource.From(TestKit.Data).Single(m => m.ModId == "core");
        ModLocaleSource mod = _kit.Mod("extra", ["core"],
            ("en", "t.ftl", "node-zz-orphan = Orphan\n    .name = Orphan\n    .short = O\n    .anchor = A\nui-extra = Extra\n"),
            ("ru", "t.ftl", "ui-extra = Доп\n"));
        var warnings = new List<string>();
        LocaleCatalog c = LocaleCatalog.Load([core, mod], TestKit.Data, warnings.Add);
        Assert.False(c.Has("en", "node-zz-orphan"));
        Assert.True(c.Has("en", "ui-extra"));
        Assert.Contains(warnings, w => w.Contains("node-zz-orphan", StringComparison.Ordinal) && w.StartsWith("L2", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("stale", StringComparison.Ordinal) && w.Contains("ui-extra", StringComparison.Ordinal));
    }

    [Fact]
    public void L3_SeesVariablesUsedOnlyInSelectors()
    {
        LocaleCatalog c = _kit.Single(
            "ui-x = { $n ->\n    [one] item\n   *[other] items\n}\n",
            "ui-x = { $count ->\n    [one] предмет\n   *[other] предмета\n}\n");
        Assert.Contains(LocaleChecks.Variables(c), p => p.Contains("ui-x", StringComparison.Ordinal));
    }

    [Fact]
    public void Cmap_Format4_TreatsNotdefSegmentsAsMissing()
    {
        // One segment 0x41..0x43, idRangeOffset 0, idDelta -0x41 + 0: 0x41 -> glyph 0 (.notdef), 0x42 -> 1, 0x43 -> 2.
        string path = Path.Combine(_kit.Root, "t.ttf");
        File.WriteAllBytes(path, Font4(0x41, 0x43, unchecked((short)-0x41)));
        var cmap = new FontCmap(path);
        Assert.False(cmap.Has(0x41));
        Assert.True(cmap.Has(0x42));
        Assert.True(cmap.Has(0x43));
        Assert.False(cmap.Has(0x44));
    }

    [Fact]
    public void Date_PatternAndMonthName_FallBackIndependently()
    {
        string enFmt = File.ReadAllText(Path.Combine(TestKit.CoreDir, "locale", "en", "fmt.ftl"));
        ModLocaleSource en = _kit.Mod("fmt", [], ("en", "fmt.ftl", enFmt));
        // A German pattern that refers to the month name, which the locale does not define.
        ModLocaleSource de = _kit.Mod("de", ["fmt"], ("de", "fmt.ftl", "fmt-date-dmy = { $day }. { date-month-nom } { $year }\n"));
        var loc = new Loc(LocaleCatalog.Load([en, de]), "de");
        Assert.Equal("9. February 1945", loc.Date(new GameDate(1945, 2, 9)));
    }

    [Fact]
    public void RuntimeLoad_RunsPluralAndLengthChecks()
    {
        ModLocaleSource core = ModLocaleSource.From(TestKit.Data).Single(m => m.ModId == "core");
        ModLocaleSource mod = _kit.Mod("extra", ["core"],
            ("en", "t.ftl", "ui-p = { NUMBER($n) ->\n   *[one] a\n    [other] b\n}\nui-very_long-btn = This button caption is far too long for its place\n"));
        var warnings = new List<string>();
        LocaleCatalog.Load([core, mod], TestKit.Data, warnings.Add);
        Assert.Contains(warnings, w => w.StartsWith("L4", StringComparison.Ordinal) && w.Contains("ui-p", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.StartsWith("L6", StringComparison.Ordinal) && w.Contains("ui-very_long-btn", StringComparison.Ordinal));
    }

    [Fact]
    public void RuntimeLoad_HasNoSilentDefault()
    {
        foreach (var m in typeof(LocaleCatalog).GetMethods().Where(m => m.Name == "Load"))
        {
            foreach (var prm in m.GetParameters().Where(p => p.ParameterType == typeof(Action<string>)))
                Assert.False(prm.IsOptional, "warn must be required");
        }
        Assert.DoesNotContain(typeof(LocaleCatalog).GetMethods(), m => m.Name == "Load" && m.GetParameters() is [{ ParameterType: var t }] && t == typeof(GameData));
    }

    [Fact]
    public void L3_CaseVariableOfAMessage_IsNotExempt()
    {
        LocaleCatalog c = _kit.Single(
            "ui-x = { $case ->\n    [a] A\n   *[b] B\n}\n",
            "ui-x = Fixed\n");
        Assert.Contains(LocaleChecks.Variables(c), p => p.Contains("ui-x", StringComparison.Ordinal));
    }

    static byte[] Font4(int start, int end, short delta)
    {
        var b = new List<byte>();
        void U16(int v) { b.Add((byte)(v >> 8)); b.Add((byte)v); }
        void U32(uint v) { U16((int)(v >> 16)); U16((int)(v & 0xFFFF)); }
        // sfnt header, one table record 'cmap' at offset 28.
        U32(0x00010000); U16(1); U16(16); U16(0); U16(0);
        b.AddRange("cmap"u8.ToArray()); U32(0); U32(28); U32(0);
        U16(0); U16(1); U16(3); U16(1); U32(12);              // cmap header + record -> subtable at +12
        int len = 16 + 8 * 2;
        U16(4); U16(len); U16(0); U16(4); U16(0); U16(0); U16(0); // format, length, lang, segX2, searchRange.. 
        U16(end); U16(0xFFFF); U16(0);                       // endCode[], reservedPad
        U16(start); U16(0xFFFF);                             // startCode[]
        U16((ushort)delta); U16(1);                          // idDelta[]
        U16(0); U16(0);                                      // idRangeOffset[]
        return [.. b];
    }
}
