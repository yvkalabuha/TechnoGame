using System.Text.RegularExpressions;

namespace Singularity.Localization;

/// <summary>
/// L5 (ADR-0018 §3.2): literal ids in facade calls exist, and scenes carry no visible text.
/// A regex scan over the facade method names (<c>T</c>, <c>A</c>, <c>new DataRef</c>); dynamic ids go through typed helpers.
/// </summary>
public static partial class ClientReferences
{
    [GeneratedRegex("""\bLoc\.(?:T|A)\(\s*"([^"]*)"\s*[,)]""")]
    private static partial Regex MessageCall();

    [GeneratedRegex("""\bnew\s+DataRef\(\s*"([^"]*)"\s*\)""")]
    private static partial Regex TermRef();

    [GeneratedRegex("""^\s*(text|tooltip_text|placeholder_text)\s*=\s*"(.+)"\s*$""")]
    private static partial Regex SceneText();

    /// <summary>Problems in C# sources: ids that do not exist in the source language.</summary>
    public static List<string> CheckCode(string fileName, string source, LocaleCatalog catalog)
    {
        var problems = new List<string>();
        IReadOnlyDictionary<string, LocaleEntity> en = catalog.Entities(LocaleCatalog.SourceLanguage);
        foreach (Match m in MessageCall().Matches(source))
        {
            if (!en.ContainsKey(m.Groups[1].Value))
                problems.Add($"L5 {fileName}: Loc call uses unknown id '{m.Groups[1].Value}'");
        }
        foreach (Match m in TermRef().Matches(source))
        {
            if (!en.ContainsKey("-" + m.Groups[1].Value))
                problems.Add($"L5 {fileName}: DataRef uses unknown term '{m.Groups[1].Value}'");
        }
        return problems;
    }

    /// <summary>Problems in a *.tscn or *.tres file: non-empty visible text properties.</summary>
    public static List<string> CheckScene(string fileName, string source)
    {
        var problems = new List<string>();
        int line = 0;
        foreach (string l in source.Split('\n'))
        {
            line++;
            if (SceneText().Match(l.TrimEnd('\r')) is { Success: true } m)
                problems.Add($"L5 {fileName}:{line}: scene sets {m.Groups[1].Value} to a literal");
        }
        return problems;
    }
}
