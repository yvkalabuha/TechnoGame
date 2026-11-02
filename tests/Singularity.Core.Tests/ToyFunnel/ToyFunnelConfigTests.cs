using Singularity.Core.Science.ToyFunnel;
using Singularity.Core.Simulation;
using Singularity.Kernel;

namespace Singularity.Core.Tests.ToyFunnel;

public class ToyFunnelConfigTests
{
    [Fact]
    public void FromParams_ReadsEveryKey_AndExactValues()
    {
        ParamSet p = ToyRun.Params("C", 0.25);
        ToyFunnelConfig c = ToyFunnelConfig.FromParams(p);
        Assert.Empty(p.Unread());
        Assert.Equal(ToyFunnelVariant.C, c.Variant);
        Assert.Equal(1.0 / 12.0, c.Dt); // same double as the prototype's 1.0 / 12.0
        Assert.Equal(5, c.BetaEighths);
        Assert.Equal(8, c.GammaEighths);
        Assert.Equal(100 * Fx6.Scale, c.MilestoneQ0Raw);
        Assert.Equal(6, c.Archetypes.Count);
        Assert.Equal(new ToyArchetype("A1", 1.6, 0.6, 1.0, 1.0, EducationLevel.Fixed(1.6), EducationLevel.Fixed(0.6), 0.6), c.Archetypes[0]);
        Assert.True(c.Archetypes[5].EduR.Scheduled);
        Assert.Equal(0.96, c.Archetypes[5].Iota);
        Assert.Equal((0.6, 1.6, 360), (c.EduScheduleFrom, c.EduScheduleTo, c.EduScheduleMonths));
    }

    [Theory]
    [InlineData("nu")]
    [InlineData("model")]
    [InlineData("archetypes")]
    [InlineData("C_psi")]
    public void MissingKey_IsAnError(string key)
    {
        var values = ToyRun.Values("A", 0.25);
        values.RemoveAll(kv => kv.Key == key);
        var e = Assert.Throws<ParamException>(() => ToyFunnelConfig.FromParams(new ParamSet(values)));
        Assert.Contains(key, e.Message);
    }

    [Theory]
    [InlineData("beta", "0.6")]     // not on the 1/8 grid
    [InlineData("gamma", "-0.125")]
    [InlineData("beta", "1.125")]   // beta > 1
    [InlineData("nu", "-1")]
    [InlineData("C_lag_months", "0")]
    [InlineData("countries", "0")]
    public void OutOfRange_IsAnError(string key, string text)
    {
        var values = ToyRun.Values("A", 0.25);
        int i = values.FindIndex(kv => kv.Key == key);
        values[i] = new(key, new ParamValue(ParamKind.Number, text));
        Assert.Throws<ParamException>(() => ToyFunnelConfig.FromParams(new ParamSet(values)));
    }

    [Fact]
    public void UnknownVariant_OrLevel_IsAnError()
    {
        var values = ToyRun.Values("D", 0.25);
        Assert.Throws<ParamException>(() => ToyFunnelConfig.FromParams(new ParamSet(values)));

        values = ToyRun.Values("A", 0.25);
        int i = values.FindIndex(kv => kv.Key == "levels");
        values[i] = new("levels", new ParamValue(ParamKind.Json, """{"l":0.6,"m":1}"""));
        var e = Assert.Throws<ParamException>(() => ToyFunnelConfig.FromParams(new ParamSet(values)));
        Assert.Contains("'h'", e.Message);
    }

    [Fact]
    public void Factory_CreatesModelWithMetricTables()
    {
        var factory = new ToyFunnelModelFactory();
        Assert.Equal("toy-funnel", factory.Id);
        Assert.False(factory.IsTestModel);
        ILabModel model = factory.Create(new ModelSetup(ToyRun.Params("A", 1.0, countries: 13), 1, 64, 492));
        Assert.Equal(13, model.Table.RowCount);
        Assert.Equal(ToyFunnelModel.SchemaFunnel, model.SchemaVersion);
        Assert.Equal(["yearly", "countries", "milestones"], model.MetricTables.Select(t => t.Name));
        Assert.Equal("A1.2", ((ToyFunnelModel)model).CountryId(12)); // archetypes cycle, ids stay unique
    }

    [Fact]
    public void ControlC_KeepsLagHistoryInTheTable()
    {
        ILabModel model = new ToyFunnelModelFactory().Create(new ModelSetup(ToyRun.Params("C", 1.0), 1, 64, 492));
        Assert.Equal(ToyFunnelModel.SchemaLagControl, model.SchemaVersion);
        Assert.Equal(8 + 120, model.Table.Columns.Count);
    }
}
