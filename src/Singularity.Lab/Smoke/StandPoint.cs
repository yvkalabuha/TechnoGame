using Singularity.Core.Industry;
using Singularity.Core.Knowledge;
using Singularity.Lab.Models.Slice;
using Singularity.Session;

namespace Singularity.Lab.Smoke;

/// <summary>
/// A calibration point of a manifest for the single-layer stands of T12 (C1, C3; spec §8.2): the point's parameters go through the same
/// <see cref="SliceParameters.Parse"/> as the calibration build (keys K_DIV, THETA_START, H_MAX, ALPHA, X_SCALE, DELTA_H, C_M, KAPPA_Q10;
/// unknown or forbidden keys are errors there), and the parsed «К» values of the <see cref="SliceRunConfig"/> are put into the stand's
/// parameter records in the same way <c>SliceWorld.ApplyCalibration</c> (internal to the Session) does for the world: an unset key keeps the
/// §8.1 value, so a point without K keys gives the default records. KAPPA_Q10 moves only the product layer and no stand reads it.
/// </summary>
internal sealed record StandPoint(int Index, string Series, SliceRunConfig Config, IReadOnlyList<ulong> Seeds)
{
    public static IReadOnlyList<StandPoint> FromManifest(string path, int? only)
    {
        Manifest m = Manifest.Load(path);
        var list = new List<StandPoint>();
        foreach (MatrixPoint p in m.Points)
        {
            if (only is int i && p.Index != i)
                continue;
            SliceParameters sp = SliceParameters.Parse(p.CreateParamSet(), 0, 1, 1, calibration: true);
            list.Add(new StandPoint(p.Index, p.Series, sp.Config, p.Seeds));
        }
        if (list.Count == 0)
            throw new UsageException($"No point {only} in {path} ({m.Points.Count} points).");
        return list;
    }

    public IndustryParams Industry(IndustryParams i)
    {
        // Range checks are those of SliceWorld.ApplyCalibration (DEBT-0036), so a stand rejects every point the calibration build rejects.
        if (Config.Alpha is { } alpha)
        {
            if (alpha < Kernel.Fx6.Zero)
                throw new ParamException("ALPHA must not be negative.");
            i = i with { Alpha = alpha };
        }
        if (!Config.XHalfByTier.IsDefault)
        {
            if (Config.XHalfByTier.Length != i.XHalfByTier.Length)
                throw new ParamException($"X½ needs {i.XHalfByTier.Length} values (tiers 3, 4, 5).");
            i = i with { XHalfByTier = Config.XHalfByTier };
        }
        if (Config.DeltaH is { } deltaH)
        {
            if (deltaH < Kernel.Fx6.Zero || deltaH > Kernel.Fx6.One)
                throw new ParamException("DELTA_H must be in [0; 1].");
            i = i with { DeltaH = deltaH };
        }
        if (Config.CM is { } cm)
            i = i with { CM = cm };
        CheckKappa();
        i.Validate();
        return i;
    }

    // KAPPA_Q10 moves only the product layer (no stand reads it) but is range-checked like the calibration build does.
    private void CheckKappa()
    {
        if (Config.KappaQ10 is { } kappa && kappa is < 0 or > 1024)
            throw new ParamException("KAPPA_Q10 must be in 0…1024.");
    }

    public KnowledgeParams Knowledge(KnowledgeParams k)
    {
        if (Config.CostDivisor <= Kernel.Fx6.Zero)
            throw new ParamException("K_DIV must be positive.");
        if (Config.CostDivisor != Kernel.Fx6.One)
            k = k with { CostByType = [.. k.CostByType.Select(x => x / Config.CostDivisor)] };
        if (Config.ThetaStart is { } theta)
        {
            if (theta < Kernel.Fx6.Zero || theta > Kernel.Fx6.One)
                throw new ParamException("THETA_START must be in [0; 1].");
            k = k with { ThetaStart = theta };
        }
        if (Config.HMax is { } hMax)
        {
            if (hMax < Kernel.Fx6.Zero || hMax > Kernel.Fx6.One)
                throw new ParamException("H_MAX must be in [0; 1].");
            k = k with { HMax = hMax };
        }
        CheckKappa();
        return k;
    }

    /// <summary>The K keys of the point as text, for logs and output names.</summary>
    public string Label =>
        $"p{Index:00} {Series}: K_DIV={Config.CostDivisor} THETA_START={Config.ThetaStart?.ToString() ?? "-"} H_MAX={Config.HMax?.ToString() ?? "-"} " +
        $"ALPHA={Config.Alpha?.ToString() ?? "-"} XHALF3={(Config.XHalfByTier.IsDefault ? "-" : Config.XHalfByTier[0].ToString())} " +
        $"DELTA_H={Config.DeltaH?.ToString() ?? "-"} C_M={Config.CM?.ToString() ?? "-"} KAPPA_Q10={Config.KappaQ10?.ToString() ?? "-"}";
}
