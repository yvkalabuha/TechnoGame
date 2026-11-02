using System.Globalization;
using Singularity.Core.Simulation;

namespace Singularity.Core.Tests.Simulation;

public class SimulationContractTests
{
    [Theory]
    [InlineData(0.0125)]
    [InlineData(1e-05)]
    [InlineData(-0.09999999999999998)]
    [InlineData(1.0 / 3.0)]
    [InlineData(0.0)]
    public void MetricReal_RoundTripsExactly(double value)
    {
        string text = MetricFormat.Real(value);
        Assert.Equal(BitConverter.DoubleToInt64Bits(value),
            BitConverter.DoubleToInt64Bits(double.Parse(text, CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData(1_500_000L, "1.5")]
    [InlineData(-1L, "-0.000001")]
    [InlineData(3_000_000L, "3")]
    [InlineData(long.MinValue, "-9223372036854.775808")]
    public void MetricFx6_IsExactDecimal(long raw, string expected)
    {
        Assert.Equal(expected, MetricFormat.Fx6(raw));
    }

    [Fact]
    public void ParamSet_ReadsTypedValues_AndTracksUnread()
    {
        var p = new ParamSet(
        [
            new("dt", new ParamValue(ParamKind.Number, "0.08333333333333333")),
            new("n", new ParamValue(ParamKind.Number, "42")),
            new("x", new ParamValue(ParamKind.Number, "1.25")),
            new("name", new ParamValue(ParamKind.String, "A")),
            new("unused", new ParamValue(ParamKind.Bool, "true")),
        ]);
        Assert.Equal(1.0 / 12.0, p.GetDouble("dt"));
        Assert.Equal(42, p.GetLong("n"));
        Assert.Equal(1_250_000, p.GetFx6Raw("x"));
        Assert.Equal("A", p.GetString("name"));
        Assert.Equal(7.5, p.GetDouble("missing", 7.5));
        Assert.Equal(["unused"], p.Unread());
        Assert.Throws<ParamException>(() => p.GetDouble("name"));
        Assert.Throws<ParamException>(() => p.GetLong("other"));
        Assert.Throws<ParamException>(() => ParamSet.ParseFx6Raw("0.0000001", "k"));
    }
}
