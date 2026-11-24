namespace Singularity.Localization.Tests;

/// <summary>ADR-0018 §1: data id and Fluent id map both ways, with dotted node ids and product ids.</summary>
public class IdMappingTests
{
    [Fact]
    public void NodeIds_RoundTrip_AllCoreNodes()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in TestKit.Data.Nodes.Id)
        {
            string key = FluentIds.Node(id);
            Assert.DoesNotContain('.', key);
            Assert.True(seen.Add(key), key);                       // injective
            ParsedKey parsed = FluentIds.Parse(key);
            Assert.Equal(KeyKind.Node, parsed.Kind);
            Assert.Equal(id.Split('.'), parsed.Segments);           // segments are the dotted parts
            Assert.Equal(id, FluentIds.NodeIdOf(key));              // and back
        }
        Assert.Equal(81, seen.Count);
    }

    [Fact]
    public void DottedNodeId_BecomesDashes()
    {
        Assert.Equal("node-s8-eng-turbojet_g1", FluentIds.Node("s8.eng.turbojet_g1"));
        Assert.Equal("s8.eng.turbojet_g1", FluentIds.NodeIdOf("node-s8-eng-turbojet_g1"));
    }

    [Fact]
    public void ProductKeys_HaveFixedPositions_AndNeverMeetNodeKeys()
    {
        Assert.Equal(KeyKind.ProductClass, FluentIds.Parse(FluentIds.ProductClass("jet_fighter")).Kind);
        ParsedKey slot = FluentIds.Parse(FluentIds.Slot("jet_fighter", "hot_section"));
        Assert.Equal(KeyKind.Slot, slot.Kind);
        Assert.Equal(["jet_fighter", "slot", "hot_section"], slot.Segments);
        ParsedKey alt = FluentIds.Parse(FluentIds.Alternative("jet_fighter", "engine", "eng_g1"));
        Assert.Equal(KeyKind.Alternative, alt.Kind);
        Assert.Equal(["jet_fighter", "alt", "engine", "eng_g1"], alt.Segments);
        Assert.DoesNotContain(TestKit.Data.Nodes.Id, id => FluentIds.Node(id).StartsWith("pc-", StringComparison.Ordinal));
    }

    [Fact]
    public void AllExpectedKeys_AreValidGrammar_AndParseToTheirKind()
    {
        foreach ((string id, KeyKind kind) in ExpectedKeys.Of(TestKit.Data))
            Assert.Equal(kind, FluentIds.Parse(id).Kind);
    }

    [Theory]
    [InlineData("s8.eng.turbo-jet")]   // '-' in a data id would make the mapping ambiguous
    [InlineData("s8..x")]
    [InlineData("8s.eng.x")]
    [InlineData("")]
    [InlineData("s8.eng.тРД")]
    public void BadDataIds_AreRejectedLoudly(string id) =>
        Assert.Throws<LocaleLoadException>(() => FluentIds.Node(id));

    [Theory]
    [InlineData("foo-bar")]            // unknown kind
    [InlineData("node")]
    [InlineData("pc-a-slot")]          // wrong shape
    [InlineData("pc-a-b-c")]
    [InlineData("node-a b")]
    public void BadKeys_DoNotParse(string key) => Assert.False(FluentIds.TryParse(key, out _));

    [Fact]
    public void TermIds_KeepTheirDashOnlyInFiles()
    {
        Assert.Equal("actor-SUN", FluentIds.Actor("SUN"));
        Assert.Equal("-actor-SUN", FluentIds.FileId("actor-SUN"));
        Assert.Equal("node-s8-eng-x", FluentIds.FileId("node-s8-eng-x"));
        Assert.Equal(KeyKind.Country, FluentIds.Parse("-country-RUS").Kind);
    }

    [Fact]
    public void FieldTable_RequiresNameAndAnchorForNodes()
    {
        Assert.Equal(["name", "anchor"], FluentIds.RequiredAttributes(KeyKind.Node));
        Assert.True(FluentIds.IsAllowedAttribute(KeyKind.Event, "opt-accept"));
        Assert.False(FluentIds.IsAllowedAttribute(KeyKind.Node, "title"));
    }
}
