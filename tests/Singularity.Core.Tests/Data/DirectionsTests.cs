using Singularity.Core.Data;
using Singularity.Core.Data.Yaml;

namespace Singularity.Core.Tests.Data;

/// <summary>TASK-0189: directions of nodes and institutes (O-091): the loader, the validator and the shipped core data.</summary>
public class DirectionsTests
{
    private static readonly GameData Data = ModLoader.Load(TestMods.CoreDir);

    private static YNode Field(string yaml) => ((YMap)StrictYaml.Parse("d: " + yaml + "\n", "t.yaml")).Entries[0].Value;

    [Fact]
    public void Core_EveryNodeHasTwoToFourDistinctDirections()
    {
        NodeTable n = Data.Nodes;
        for (int r = 0; r < n.Count; r++)
        {
            Assert.InRange(n.DirectionCount(r), 2, 4);
            int[] d = [.. Enumerable.Range(0, n.DirectionCount(r)).Select(k => n.DirectionAt(r, k))];
            Assert.Equal(d.Length, d.Distinct().Count());
        }
    }

    [Fact]
    public void Core_NodeMainDirection_IsTheFirstListed()
    {
        NodeTable n = Data.Nodes;
        int r = n.IndexOf("s8.eng.turbojet_g1");
        Assert.Equal(["prop", "mfg"], Enumerable.Range(0, n.DirectionCount(r)).Select(k => Directions.Ids[n.DirectionAt(r, k)]));
    }

    [Fact]
    public void Core_EveryInstituteHasTwoToFourDirectionsWithLevels1To5()
    {
        InstituteTable t = Data.Institutes;
        Assert.Equal(27, t.Count);
        for (int i = 0; i < t.Count; i++)
        {
            Assert.InRange(t.DirectionCount(i), 2, 4);
            for (int k = 0; k < t.DirectionCount(i); k++)
                Assert.InRange(t.DirectionLevel(i, k), 1, Directions.MaxLevel);
        }
    }

    [Fact]
    public void Core_InstituteDirections_MatchTheNote()
    {
        InstituteTable t = Data.Institutes;
        int row = Enumerable.Range(0, t.Count).Single(i => t.Id(i) == "usa_naca");
        // aero 4, prop 3, struct 2, stored by direction number.
        Assert.Equal([(0, 4), (1, 3), (5, 2)], Enumerable.Range(0, t.DirectionCount(row)).Select(k => (t.DirectionAt(row, k), t.DirectionLevel(row, k))));
    }

    [Theory]
    [InlineData("[aero]")]                                  // too few
    [InlineData("[aero, prop, met, mat, chem]")]            // too many
    [InlineData("[aero, warp]")]                            // unknown id
    [InlineData("[aero, aero]")]                            // duplicate
    public void NodeEntry_Invalid_IsRejected(string yaml) =>
        Assert.Throws<DataLoadException>(() => Directions.ReadForNode(Field(yaml), "n"));

    [Fact]
    public void SpaceDirection_IsKnown_AndUsedByTheSpaceNodes()
    {
        int space = Directions.Ids.IndexOf("space");
        Assert.Equal(12, space);
        Assert.Contains(space, Data.Nodes.Direction);
        Assert.Equal([space, 0], Directions.ReadForNode(((Singularity.Core.Data.Yaml.YMap)Singularity.Core.Data.Yaml.StrictYaml.Parse("d: [space, aero]\n", "t.yaml")).Entries[0].Value, "n"));
    }

    [Fact]
    public void NodeEntry_KeepsTheFileOrder_MainFirst()
    {
        Assert.Equal([9, 0], Directions.ReadForNode(Field("[ctrl, aero]"), "n"));
        Assert.Empty(Directions.ReadForNode(null, "n"));
    }

    [Theory]
    [InlineData("{aero: 4}")]                               // too few
    [InlineData("{aero: 6, prop: 1}")]                      // level above 5
    [InlineData("{aero: 0, prop: 1}")]                      // level below 1
    [InlineData("{aero: 2, warp: 1}")]                      // unknown id
    public void Levels_Invalid_IsRejected(string yaml) =>
        Assert.Throws<DataLoadException>(() => Directions.ReadLevels(Field(yaml), "i"));

    [Fact]
    public void Levels_AreSortedByDirection_WhateverTheKeyOrder()
    {
        Assert.Equal([(0, 2), (1, 5)], Directions.ReadLevels(Field("{prop: 5, aero: 2}"), "i"));
    }

    [Fact]
    public void InstituteHash_IgnoresDirections_SoGoldensStay()
    {
        string Row(string dirs) => $"id: x\ncountry: SUN\nfounded: 1940\nref_year: 1945\nsource: s\nareas:\n  aero: 2\n{dirs}";
        string a = InstituteTable.Build([("x", (YMap)StrictYaml.Parse(Row(""), "t.yaml"))]).Hash;
        string b = InstituteTable.Build([("x", (YMap)StrictYaml.Parse(Row("directions: {aero: 2, prop: 1}\n"), "t.yaml"))]).Hash;
        Assert.Equal(a, b);
    }
}
