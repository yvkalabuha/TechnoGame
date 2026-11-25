using Singularity.Core.Data.Yaml;
using Singularity.Core.Intelligence;
using Singularity.Kernel;

namespace Singularity.Core.Data;

/// <summary>
/// The intelligence parameters of the loaded mods (<c>kind: intelligence</c>, one object). Not part of <see cref="GameData.DataHash"/>: the hash of a build
/// without intelligence must stay what it was, so the parameters have their own <see cref="Hash"/>, which a run with intelligence puts into its state.
/// </summary>
public sealed class IntelligenceData
{
    public required IntelParams Params { get; init; }

    /// <summary>SHA-256 (hex) of the canonical form of the parameters.</summary>
    public required string Hash { get; init; }

    internal static IntelligenceData Build(YMap map)
    {
        var m = new MapReader(map);
        m.Str("id");
        Fx6 F(string key) => m.Fixed(key);
        IntelParams p = new()
        {
            GrowthPerYear = F("growth_per_year"), DecayPerYear = F("decay_per_year"), MaturityPerYear = F("maturity_per_year"),
            NetShareRef = F("net_share_ref"), CiShare = F("ci_share"), CiShareRef = F("ci_share_ref"),
            CiGrowthCut = F("ci_growth_cut"), CiHazardGain = F("ci_hazard_gain"),
            HazardBasePerYear = F("hazard_base_per_year"), HazardCapPerYear = F("hazard_cap_per_year"),
            ActObserve = F("act_observe"), ActSteal = F("act_steal"), ActFalseTrails = F("act_false_trails"),
            TraceDecayPerYear = F("trace_decay_per_year"), TraceSome = F("trace_some"), TraceMany = F("trace_many"),
            RiskNoticeable = F("risk_noticeable"), RiskHigh = F("risk_high"),
            LossKeepN = F("loss_keep_n"), LossKeepA = F("loss_keep_a"),
            Sigma = F("sigma"), SMax = F("s_max"), SourcesMax = m.Int("sources_max"),
            ConfVisible = F("conf_visible"), RangeMin = F("range_min"), Kappa = F("kappa"), BiasMax = F("bias_max"),
            PresetLow = F("preset_low"), PresetNormal = F("preset_normal"), PresetStrong = F("preset_strong"),
            TurnOnExposure = F("turn_on_exposure"), TurnDetectPerYear = F("turn_detect_per_year"), SensLicence = F("sens_licence"),
            TraceCertain = F("trace_certain"), LeakOffDir = F("leak_off_dir"), DirChangeKeepA = F("dir_change_keep_a"),
        };
        m.End();
        Validate(p, map);
        return new IntelligenceData { Params = p, Hash = HashOf(p) };
    }

    private static void Validate(IntelParams p, YMap at)
    {
        Fx6[] unit = [p.GrowthPerYear, p.DecayPerYear, p.MaturityPerYear, p.CiShare, p.CiGrowthCut, p.HazardBasePerYear, p.HazardCapPerYear, p.TraceDecayPerYear,
            p.LossKeepN, p.LossKeepA, p.SMax, p.ConfVisible, p.RangeMin, p.Kappa, p.BiasMax, p.PresetLow, p.PresetNormal, p.PresetStrong, p.ActObserve, p.ActSteal, p.ActFalseTrails, p.TurnOnExposure, p.TurnDetectPerYear, p.SensLicence, p.LeakOffDir, p.DirChangeKeepA];
        if (unit.Any(v => v < Fx6.Zero || v > Fx6.One) || p.HazardCapPerYear >= Fx6.One || p.SMax >= Fx6.One || p.TurnDetectPerYear >= Fx6.One || p.CiHazardGain < Fx6.Zero || p.Sigma < Fx6.Zero || p.TraceSome < Fx6.Zero || p.TraceCertain <= Fx6.Zero || p.TraceMany < p.TraceSome
            || p.RiskNoticeable < Fx6.Zero || p.RiskHigh < p.RiskNoticeable)
            throw DataLoadException.At(at, "intelligence: rates and shares lie in [0, 1], the hazard cap below 1, the word thresholds in order");
        if (p.NetShareRef <= Fx6.Zero || p.CiShareRef <= Fx6.Zero || p.SourcesMax < 1 || !(p.PresetLow <= p.PresetNormal && p.PresetNormal <= p.PresetStrong))
            throw DataLoadException.At(at, "intelligence: the reference shares and sources_max are positive, the presets in order low <= normal <= strong");
    }

    private static string HashOf(IntelParams p)
    {
        using var w = new CanonicalWriter();
        w.Str("singularity-intelligence/1");
        foreach (Fx6 v in (Fx6[])[p.GrowthPerYear, p.DecayPerYear, p.MaturityPerYear, p.NetShareRef, p.CiShare, p.CiShareRef, p.CiGrowthCut, p.CiHazardGain,
            p.HazardBasePerYear, p.HazardCapPerYear, p.ActObserve, p.ActSteal, p.ActFalseTrails, p.TraceDecayPerYear, p.TraceSome, p.TraceMany, p.RiskNoticeable,
            p.RiskHigh, p.LossKeepN, p.LossKeepA, p.Sigma, p.SMax, p.ConfVisible, p.RangeMin, p.Kappa, p.BiasMax, p.PresetLow, p.PresetNormal, p.PresetStrong, p.TurnOnExposure, p.TurnDetectPerYear, p.SensLicence, p.TraceCertain, p.LeakOffDir, p.DirChangeKeepA])
            w.Fixed(v);
        w.Int(p.SourcesMax);
        return w.FinishHex();
    }
}
