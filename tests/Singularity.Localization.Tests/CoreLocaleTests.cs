namespace Singularity.Localization.Tests;

/// <summary>The real mods/core locale files against the real game data: checks L1-L4, L6 (ADR-0018 §3.2).</summary>
public class CoreLocaleTests
{
    static LocaleCatalog Catalog => TestKit.Catalog;

    [Fact]
    public void CoreHas_EnglishAndRussian_AndNoPseudoLocaleOnDisk()
    {
        Assert.Equal(["en", "ru"], Catalog.Languages);
    }

    [Fact]
    public void L1_L2_Completeness_IsClean() => Assert.Empty(LocaleChecks.Completeness(Catalog, TestKit.Data));

    [Fact]
    public void L3_VariablesAndFunctions_AreTheSameInBothLanguages() => Assert.Empty(LocaleChecks.Variables(Catalog));

    [Fact]
    public void L4_Plurals_AreWellFormed() => Assert.Empty(LocaleChecks.Plurals(Catalog, new Loc(Catalog, "en")));

    [Fact]
    public void L6_LengthBudgets_Hold() => Assert.Empty(LocaleChecks.Budgets(Catalog));

    [Fact]
    public void Core_HasAllExpectedEntities_ForSlotAndScenario()
    {
        // 81 nodes, 1 class + 5 slots + 26 alternatives, 1 scenario, 5 actors, 8 clusters, 5 disciplines, 6 types, 11 enum values.
        Assert.Equal(ExpectedKeys.MapCountries.Length, ExpectedKeys.MapCountries.Distinct().Count());
        Assert.Equal(99 + 32 + 1 + 5 + ExpectedKeys.MapCountries.Length + 8 + 5 + 6 + 11, ExpectedKeys.Of(TestKit.Data).Count);
        Assert.True(Catalog.Has("ru", "-actor-SUN"));
        Assert.Equal("СССР", new Loc(Catalog, "ru").Name(new DataRef("actor-SUN")));
        Assert.Equal("USSR", new Loc(Catalog, "en").Name(new DataRef("actor-SUN")));
    }

    [Fact]
    public void Actors_HaveRussianCasesAndGender()
    {
        var loc = new Loc(Catalog, "ru");
        Assert.Equal("Франции", loc.Name(new DataRef("actor-FRA"), Field.Name, "gen"));
        Assert.Equal("Великобританией", loc.Name(new DataRef("actor-GBR"), Field.Name, "ins"));
        Assert.Equal("Швецию", loc.Name(new DataRef("actor-SWE"), Field.Name, "acc"));
        Assert.Equal("USSR", new Loc(Catalog, "en").Name(new DataRef("actor-SUN"), Field.Name, "gen"));   // English has no cases
    }

    [Fact]
    public void ActorShortAndAdjective_AreReadFromTheTerm()
    {
        var ru = new Loc(Catalog, "ru");
        var en = new Loc(Catalog, "en");
        var warnings = new List<string>();
        var checkedLoc = new Loc(Catalog, "ru", warnings.Add);
        Assert.Equal("СССР", checkedLoc.Name(new DataRef("actor-SUN"), Field.Short));
        Assert.Equal("советский", checkedLoc.Name(new DataRef("actor-SUN"), Field.Adj));
        Assert.Equal("Британия", ru.Name(new DataRef("actor-GBR"), Field.Short));
        Assert.Equal("British", en.Name(new DataRef("actor-GBR"), Field.Adj));
        Assert.Equal("USSR", en.Name(new DataRef("actor-SUN"), Field.Short));
        Assert.Empty(warnings);
        // A missing attribute is a marker, not a crash.
        Assert.Equal("[[-actor-SUN.desc]]", ru.Name(new DataRef("actor-SUN"), Field.Desc));
    }

    [Fact]
    public void Countries_HaveRussianCasesAndGender()
    {
        var ru = new Loc(Catalog, "ru");
        Assert.Equal("Китаем", ru.Name(new DataRef("country-CHN"), Field.Name, "ins"));
        Assert.Equal("Новой Зеландии", ru.Name(new DataRef("country-NZL"), Field.Name, "gen"));
        Assert.Equal("Египте", ru.Name(new DataRef("country-EGY"), Field.Name, "loc"));
        Assert.Equal("Филиппинами", ru.Name(new DataRef("country-PHL"), Field.Name, "ins"));
        Assert.Equal("Китай", ru.Name(new DataRef("country-CHN")));
        Assert.Equal("China", new Loc(Catalog, "en").Name(new DataRef("country-CHN"), Field.Name, "ins"));
        Assert.Equal("fem", Gender("country-KOR"));
        Assert.Equal("masc", Gender("country-CHN"));
        Assert.Equal("plur", Gender("country-PHL"));
    }

    static string Gender(string term) =>
        ((Linguini.Syntax.Ast.AstTerm)Catalog.Entities("ru")["-" + term].Ast).Attributes
            .Single(a => a.Id.ToString() == "gender").Value.Elements.Single().ToString()!;

    [Fact]
    public void L1_RequiresRussianCasesOfTerms()
    {
        using var kit = new TestKit();
        LocaleCatalog c = kit.Single(
            "-country-CHN = China\n    .gender = neut\n",
            "-country-CHN = { $case ->\n   *[nom] Китай\n    [gen] Китая\n}\n    .gender = masc\n");
        List<string> problems = LocaleChecks.Completeness(c, TestKit.Data);
        Assert.Contains(problems, p => p.Contains("'-country-CHN' lacks case [ins]", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => p.Contains("'-country-CHN' lacks case [gen]", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("missing '-country-AFG'", StringComparison.Ordinal));
    }

    [Fact]
    public void Months_AndNumberFormats_ComeFromCore()
    {
        var ru = new Loc(Catalog, "ru");
        Assert.Equal("9 февраля 1945", ru.Date(new GameDate(1945, 2, 9)));
        Assert.Equal("1 234,5", ru.Number(1234.5));
        Assert.Equal("February 9, 1945", new Loc(Catalog, "en").Date(new GameDate(1945, 2, 9)));
    }

    [Fact]
    public void RussianAnchors_EqualTheTask0011Csv()
    {
        // Moved from CoreNodesTests (ADR-0018 §4): the anchor text left Core data and lives in the Russian locale.
        string path = Path.Combine(TestKit.RepoRoot, "docs", "research", "data", "jet-slice", "nodes.csv");
        string[] lines = File.ReadAllLines(path);
        string[] header = lines[0].Split(',');
        int id = Array.IndexOf(header, "id"), anchor = Array.IndexOf(header, "anchor_event");
        var loc = new Loc(Catalog, "ru");
        int checkedRows = 0;
        foreach (string line in lines.Skip(1).Where(l => l.Length > 0))
        {
            string[] cells = line.Split(',');
            Assert.Equal(header.Length, cells.Length);
            Assert.Equal(cells[anchor], loc.A(FluentIds.Node(cells[id]), Field.Anchor));
            checkedRows++;
        }
        Assert.Equal(61, checkedRows);
    }

    [Fact]
    public void EveryNode_HasAShortThatFitsTheSignalField()
    {
        foreach (string id in TestKit.Data.Nodes.Id)
        {
            foreach (string lang in new[] { "en", "ru" })
            {
                string text = new Loc(Catalog, lang).A(FluentIds.Node(id), Field.Short);
                Assert.True(AstUtil.Graphemes(text) <= LengthBudgets.DataShort, $"{lang} {id}: '{text}'");
            }
        }
    }

    [Fact]
    public void NodeNames_AreNotMarkers()
    {
        foreach (string lang in new[] { "en", "ru" })
        {
            var loc = new Loc(Catalog, lang);
            foreach (string id in TestKit.Data.Nodes.Id)
                Assert.DoesNotContain("[[", loc.A(FluentIds.Node(id), Field.Name), StringComparison.Ordinal);
        }
    }

    // ---- the checks themselves detect problems ----

    [Fact]
    public void L1_ReportsMissingAttributes_AndL2Orphans()
    {
        using var kit = new TestKit();
        LocaleCatalog partial = kit.Single(
            "node-s8-fnd-boundary_layer =\n    .name = X\nnode-s9-eng-ghost =\n    .name = G\n    .anchor = A\n",
            "node-s8-fnd-boundary_layer =\n    .name = Х\n    .anchor = А\n");
        List<string> problems = LocaleChecks.Completeness(partial, TestKit.Data);
        Assert.Contains(problems, p => p.StartsWith("L1 en: 'node-s8-fnd-boundary_layer' lacks .anchor", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("L1 ru: 'node-s7-fnd-brayton_cycle'", StringComparison.Ordinal) || p.Contains("missing 'node-s7-fnd-brayton_cycle'", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("L2 en: orphan 'node-s9-eng-ghost'", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("lacks .anchor that ru has", StringComparison.Ordinal));
    }

    [Fact]
    public void L3_ReportsDifferentVariables()
    {
        using var kit = new TestKit();
        LocaleCatalog c = kit.Single("ui-a = { $x } and { $y }\n", "ui-a = { $x }\n");
        Assert.Contains(LocaleChecks.Variables(c), p => p.Contains("ui-a", StringComparison.Ordinal) && p.Contains("$y", StringComparison.Ordinal));
    }

    [Fact]
    public void L4_ReportsBadDefaultAndForeignCategories()
    {
        using var kit = new TestKit();
        LocaleCatalog c = kit.Single(
            "ui-a = { $n ->\n    [one] a\n   *[few] b\n}\n",
            "ui-a = { $n ->\n    [one] a\n    [few] b\n    [many] c\n   *[other] d\n}\n");
        var loc = new Loc(c, "en");
        List<string> problems = LocaleChecks.Plurals(c, loc);
        Assert.Contains(problems, p => p.Contains("must be *[other]", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("category [few] does not exist in en", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => p.StartsWith("L4 ru", StringComparison.Ordinal));
    }

    [Fact]
    public void L6_ButtonBudget_NeedsShortWhenTooLong()
    {
        using var kit = new TestKit();
        // 24 chars budget: ceil(1.3 * 19) = 25 > 24.
        LocaleCatalog tooLong = kit.Single("ui-x-btn = Nineteen characters\n", "ui-x-btn = Девятнадцать симв.\n");
        Assert.Contains(LocaleChecks.Budgets(tooLong), p => p.Contains("ui-x-btn", StringComparison.Ordinal) && p.Contains("no .short", StringComparison.Ordinal));
        LocaleCatalog withShort = kit.Single(
            "ui-x-btn =\n    Nineteen characters\n    .short = Short\n", "ui-x-btn =\n    Девятнадцать симв.\n    .short = Кратко\n");
        Assert.Empty(LocaleChecks.Budgets(withShort));
        LocaleCatalog fits = kit.Single("ui-x-btn = Fits\n", "ui-x-btn = Влезает\n");
        Assert.Empty(LocaleChecks.Budgets(fits));
        LocaleCatalog shortTooLong = kit.Single(
            "ui-x-tab =\n    A very long tab title\n    .short = Also very long tab\n", "ui-x-tab =\n    Очень длинное название\n    .short = Тоже очень длинное\n");
        Assert.Contains(LocaleChecks.Budgets(shortTooLong), p => p.Contains("ui-x-tab.short", StringComparison.Ordinal));
    }

    [Fact]
    public void L6_StateWords_AndDataShort()
    {
        using var kit = new TestKit();
        LocaleCatalog c = kit.Single(
            "enum-state_word-a = Exceptionally long word\nnode-s8-eng-x =\n    .name = N\n    .short = Thirteen char.\n",
            "enum-state_word-a = Чрезвычайно длинное\n");
        List<string> problems = LocaleChecks.Budgets(c);
        Assert.Contains(problems, p => p.Contains("enum-state_word-a' en", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("enum-state_word-a' ru", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("node-s8-eng-x.short", StringComparison.Ordinal));
    }

    [Fact]
    public void BudgetTable_HasEveryRowOfSection13_2()
    {
        string doc = File.ReadAllText(Path.Combine(TestKit.RepoRoot, "docs", "ui", "UI_DESIGN_SYSTEM.md"));
        int start = doc.IndexOf("### 13.2", StringComparison.Ordinal), end = doc.IndexOf("### 13.3", StringComparison.Ordinal);
        string[] rows = [.. doc[start..end].Split('\n').Where(l => l.StartsWith("| ", StringComparison.Ordinal) && !l.StartsWith("| Элемент", StringComparison.Ordinal)
            && !l.StartsWith("|---", StringComparison.Ordinal)).Select(l => l.Split('|')[1].Trim())];
        Assert.Equal(LengthBudgets.Rows.Select(r => r.DocRow).Distinct().Count(), rows.Length);   // inset title has two roles: 28 and 40
        foreach (string row in rows)
            Assert.Contains(LengthBudgets.Rows, r => row.StartsWith(r.DocRow, StringComparison.Ordinal));
    }
}
