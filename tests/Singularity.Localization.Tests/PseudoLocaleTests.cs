using Linguini.Syntax.Ast;

namespace Singularity.Localization.Tests;

/// <summary>ADR-0018 §3.6: the pseudo-locale xx.</summary>
public class PseudoLocaleTests
{
    static LocaleCatalog Pseudo()
    {
        LocaleCatalog c = LocaleCatalog.Load(TestKit.Data);
        PseudoLocale.AddTo(c);
        return c;
    }

    [Fact]
    public void XxHasEveryEntityOfEnAndRu()
    {
        LocaleCatalog c = Pseudo();
        var union = new SortedSet<string>(c.Entities("en").Keys.Concat(c.Entities("ru").Keys), StringComparer.Ordinal);
        Assert.Equal(union, c.Entities("xx").Keys);
        Assert.Contains("xx", c.Languages);
    }

    [Fact]
    public void EveryVisibleStringIsBracketed_NoTextWithoutBrackets()
    {
        var loc = new Loc(Pseudo(), "xx");
        foreach (string id in TestKit.Data.Nodes.Id)
        {
            foreach (Field f in new[] { Field.Name, Field.Short, Field.Anchor })
            {
                string text = loc.A(FluentIds.Node(id), f);
                Assert.StartsWith(PseudoLocale.Open, text, StringComparison.Ordinal);
                Assert.EndsWith(PseudoLocale.Close, text, StringComparison.Ordinal);
            }
        }
        string actor = loc.Name(new DataRef("actor-FRA"), Field.Name, "gen");
        Assert.StartsWith(PseudoLocale.Open, actor, StringComparison.Ordinal);
        Assert.StartsWith(PseudoLocale.Open, loc.T("enum-hazard-key"), StringComparison.Ordinal);
    }

    [Fact]
    public void TextGrowsByAtLeast30Percent_OverTheLongerVersion()
    {
        LocaleCatalog c = Pseudo();
        var en = new Loc(c, "en");
        var ru = new Loc(c, "ru");
        var xx = new Loc(c, "xx");
        foreach (string id in TestKit.Data.Nodes.Id)
        {
            string key = FluentIds.Node(id);
            int longer = Math.Max(AstUtil.Graphemes(en.A(key, Field.Name)), AstUtil.Graphemes(ru.A(key, Field.Name)));
            int pseudo = AstUtil.Graphemes(xx.A(key, Field.Name)) - PseudoLocale.Open.Length - PseudoLocale.Close.Length;
            Assert.True(pseudo * 10 >= longer * 13, $"{id}: {pseudo} vs {longer}");
        }
    }

    [Fact]
    public void VariablesFunctionsAndSelectors_AreUntouched()
    {
        using var kit = new TestKit();
        LocaleCatalog c = kit.Single(
            "ui-a = { NUMBER($n) ->\n    [one] { $n } item\n   *[other] { $n } items\n}\n",
            "ui-a = { NUMBER($n) ->\n    [one] { $n } предмет\n    [few] { $n } предмета\n    [many] { $n } предметов\n   *[other] { $n } предмета\n}\n");
        PseudoLocale.AddTo(c);
        // The longer (RU) version is used: its selector keys and variables stay.
        var vars = new SortedSet<string>();
        var funcs = new SortedSet<string>();
        AstUtil.CollectUsage(((AstMessage)c.Entities("xx")["ui-a"].Ast).Value!, vars, funcs);
        Assert.Equal(["n"], vars);
        var select = AstUtil.Selects(((AstMessage)c.Entities("xx")["ui-a"].Ast).Value!).Single();
        Assert.Equal(["one", "few", "many", "other"], select.Variants.Select(v => v.Key.ToString()));
        Assert.Equal("other", select.Variants.Single(v => v.IsDefault).Key.ToString());
    }

    [Fact]
    public void FormatsAndGender_AreCopiedUnchanged()
    {
        LocaleCatalog c = Pseudo();
        Assert.Equal(c.Entities("en")["fmt-number-decimal"].Text, c.Entities("xx")["fmt-number-decimal"].Text);
        Assert.Equal("1,234.5", new Loc(c, "xx").Number(1234.5));
        // ru has the longer text for actors; their gender attribute is not rewritten.
        var term = (AstTerm)c.Entities("xx")["-actor-FRA"].Ast;
        Assert.Equal("fem", term.Attributes.Single(a => a.Id.ToString() == "gender").Value.Elements.Single().ToString());
    }

    [Fact]
    public void XxPluralSelection_DoesNotFail()
    {
        using var kit = new TestKit();
        LocaleCatalog c = kit.Single(
            "ui-a = { $n ->\n    [one] one\n   *[other] other\n}\n",
            "ui-a = { $n ->\n    [one] один\n    [few] два\n   *[other] много\n}\n");
        PseudoLocale.AddTo(c);
        var loc = new Loc(c, "xx");
        for (int n = 0; n < 30; n++)
            Assert.DoesNotContain("[[", loc.T("ui-a", ("n", n)), StringComparison.Ordinal);
    }

    [Fact]
    public void PseudoLocaleCoreLoc_SwitchesLikeAnyLanguage()
    {
        var loc = new Loc(Pseudo(), "en");
        loc.SetLanguage("xx");
        Assert.StartsWith(PseudoLocale.Open, loc.A("node-s8-fnd-boundary_layer", Field.Name), StringComparison.Ordinal);
    }
}
