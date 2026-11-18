using System.Text.RegularExpressions;

namespace Singularity.Architecture.Tests;

/// <summary>
/// ADR-0017 п. 11: a hash is compared in tests only through <c>HashAssert</c>, which fails on null. A plain <c>Assert.Equal(a.StateHash, b.StateHash)</c>
/// or <c>==</c> would pass for two views that both skipped their hash (<c>null == null</c>).
/// </summary>
public class HashAssertUsageTests
{
    private static readonly Regex Strings = new("\\$?@?\"(?:[^\"\\\\\\n]|\\\\.)*\"", RegexOptions.Compiled);
    private static readonly Regex Comments = new("//[^\\n]*", RegexOptions.Compiled);
    private static readonly Regex HashToken = new(@"\b(StateHash|LastHash)\b", RegexOptions.Compiled);
    private static readonly Regex Comparison = new(@"Assert\.(Equal|NotEqual|Same|NotSame|StrictEqual)\b|==|!=|\.Equals\(", RegexOptions.Compiled);

    [Fact]
    public void HashComparisonsInTests_GoOnlyThroughHashAssert()
    {
        string tests = Path.Combine(RepoRoot(), "tests");
        var offenders = new List<string>();
        foreach (string file in Directory.EnumerateFiles(tests, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string rel = Path.GetRelativePath(tests, file).Replace('\\', '/');
            if (rel.Contains("/obj/") || rel.Contains("/bin/") || rel.StartsWith("Shared/", StringComparison.Ordinal) || rel.EndsWith("HashAssertUsageTests.cs", StringComparison.Ordinal))
                continue;
            string text = Comments.Replace(Strings.Replace(File.ReadAllText(file), "\"\""), "");
            foreach (string statement in text.Split(';'))
            {
                if (HashToken.IsMatch(statement) && Comparison.IsMatch(statement) && !statement.Contains("HashAssert", StringComparison.Ordinal))
                    offenders.Add($"{rel}: {Regex.Replace(statement.Trim(), @"\s+", " ")}");
            }
        }
        Assert.True(offenders.Count == 0, "hash comparison outside HashAssert:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void Scanner_SeesAPlainComparison()
    {
        string bad = "Assert.Equal(a.StateHash, b.StateHash)";
        Assert.True(HashToken.IsMatch(bad) && Comparison.IsMatch(bad) && !bad.Contains("HashAssert", StringComparison.Ordinal));
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Singularity.sln")))
                return dir.FullName;
        }
        throw new DirectoryNotFoundException("Singularity.sln not found above the test output directory.");
    }
}
