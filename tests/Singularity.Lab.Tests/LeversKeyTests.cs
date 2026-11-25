using Singularity.Lab.Models.Slice;
using Singularity.Lab.Simulation;
using Singularity.Session;

namespace Singularity.Lab.Tests;

/// <summary>TASK-0176: the lever keys of the Lab after TASK-0171 turned the levers of wave 1 on in SliceRunConfig.</summary>
public sealed class LeversKeyTests
{
    private static ParamSet Params(params (string Key, ParamValue Value)[] kv) =>
        new(kv.Select(x => new KeyValuePair<string, ParamValue>(x.Key, x.Value)));

    private static ParamValue S(string v) => new(ParamKind.String, v);

    private static ParamValue B(bool v) => new(ParamKind.Bool, v ? "true" : "false");

    private static Singularity.Session.SliceRunConfig Parse(ParamSet p) => SliceParameters.Parse(p, 1, 1, 1, calibration: false).Config;

    [Fact]
    public void No_key_is_the_legacy_build_so_old_manifests_keep_their_goldens()
    {
        var c = Parse(Params());
        Assert.False(c.Institutes);
        Assert.False(c.LicenceMarket);
        Assert.False(c.Intelligence);
        Assert.False(c.Reserve);
        Assert.False(c.AdviserForPlayer);
        var legacy = SliceRunConfig.Legacy;
        Assert.Equal((legacy.Institutes, legacy.LicenceMarket, legacy.Intelligence, legacy.Reserve, legacy.AdviserForPlayer),
            (c.Institutes, c.LicenceMarket, c.Intelligence, c.Reserve, c.AdviserForPlayer));
    }

    [Fact]
    public void Levers_on_takes_the_defaults_of_main_and_each_flag_can_be_overridden()
    {
        var on = Parse(Params(("LEVERS", S("ON"))));
        var main = new SliceRunConfig();
        Assert.Equal((main.Institutes, main.LicenceMarket, main.Intelligence, main.Reserve, main.AdviserForPlayer),
            (on.Institutes, on.LicenceMarket, on.Intelligence, on.Reserve, on.AdviserForPlayer));
        Assert.True(on.Institutes && on.LicenceMarket && on.Intelligence && on.Reserve);

        var noLic = Parse(Params(("LEVERS", S("ON")), ("LIC_MARKET", B(false))));
        Assert.False(noLic.LicenceMarket);
        Assert.True(noLic.Institutes);
        var one = Parse(Params(("INTEL", B(true)), ("RESERVE_ACCT", B(true))));
        Assert.True(one.Intelligence && one.Reserve);
        Assert.False(one.Institutes || one.LicenceMarket);
        Assert.Throws<ParamException>(() => Parse(Params(("LEVERS", S("ALL")))));
    }
}
