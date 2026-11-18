using System.Linq;
using Singularity.Client.Spike;
using Xunit;

namespace Singularity.Client.Audio.Tests;

public class NodeSchematicsTests
{
    static bool All(string _) => true;

    [Fact]
    public void Principle_swept_wing_uses_type_file_not_the_airframe_drawing() =>
        Assert.Equal("prn-aero", NodeSchematics.Pick("s8.prn.swept_wing", "PRN", "aero", All));

    [Fact]
    public void Airframe_swept_wing_uses_its_own_file() =>
        Assert.Equal("swept_wing", NodeSchematics.Pick("s8.eng.swept_wing_airframe", "ENG", "aero", All));

    [Theory]
    [InlineData("s8.eng.turbojet_g2", "ENG", "prop", "turbojet_g2")]
    [InlineData("s8.app.jet_fighter_g4", "APP", "aero", "jet_fighter_g4")]
    [InlineData("s5.prc.superalloy_melt_g2", "PRC", "metal", "superalloy_melt_g2")]
    [InlineData("s8.cmp.axial_compressor_assembly", "CMP", "prop", "axial_compressor")]
    public void Own_files_match_by_exact_id(string id, string type, string disc, string expected) =>
        Assert.Equal(expected, NodeSchematics.Pick(id, type, disc, All));

    [Fact]
    public void Prefix_of_an_id_is_not_a_match() =>
        Assert.Equal("eng-prop", NodeSchematics.Pick("s8.eng.turbojet_g1_extra", "ENG", "prop", All));

    [Fact]
    public void Missing_files_give_no_picture()
    {
        var present = new[] { "fnd-aero" };
        Assert.Equal("fnd-aero", NodeSchematics.Pick("s1.fnd.x", "FND", "aero", present.Contains));
        Assert.Null(NodeSchematics.Pick("s1.fnd.y", "FND", "chem", present.Contains));
    }
}
