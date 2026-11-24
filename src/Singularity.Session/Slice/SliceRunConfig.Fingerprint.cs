using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace Singularity.Session;

public sealed partial record SliceRunConfig
{
    /// <summary>
    /// SHA-256 of every field that decides the trajectory but is not (or not fully) a column of the table: the product AI and the arms of the
    /// rules (they select code paths and parameters), the manifest numbers («К», grid, tooling target) and the seed, scenario and start variant.
    /// Left out on purpose: the execution options (Threads, ChunkSize, ViewMode, ViewScope, DailyTickProbe, HashCheckpoints, HashEveryTick), which never change the
    /// result. A snapshot may be loaded at another thread count. The control tables of the layers L_K, L_C and L_P are not covered, so a snapshot is not taken of them.
    /// </summary>
    internal byte[] SnapshotFingerprint()
    {
        if (LkTables is not null || CapabilityTables is not null)
            throw new NotSupportedException("Snapshots of the layer controls with tables (L_K, L_C, L_P) are not supported.");
        string Raw(Kernel.Fx6? v) => v is { } x ? x.Raw.ToString(CultureInfo.InvariantCulture) : "-";
        var t = ToolingTarget;
        string[] fields =
        [
            Seed.ToString(CultureInfo.InvariantCulture), ScenarioId, FixK.ToString(), FixE.ToString(), FixA.ToString(), ((int)Start).ToString(CultureInfo.InvariantCulture),
            ProfileCell?.ToString(CultureInfo.InvariantCulture) ?? "-", ((int)Channels).ToString(CultureInfo.InvariantCulture), ProductAi.ToString(), Derive.ToString(),
            ((int)FleetArm).ToString(CultureInfo.InvariantCulture), ((int)FleetLineCond).ToString(CultureInfo.InvariantCulture), ((int)LineClose).ToString(CultureInfo.InvariantCulture),
            ((int)BudgetRule).ToString(CultureInfo.InvariantCulture), ((int)PilotForm).ToString(CultureInfo.InvariantCulture), ((int)PilotProfiles).ToString(CultureInfo.InvariantCulture),
            LicOff.ToString(), T6dOff.ToString(), LicKnows.ToString(), ((int)SaleArm).ToString(CultureInfo.InvariantCulture),
            ((int)t.Variant).ToString(CultureInfo.InvariantCulture), Raw(t.RhoMax), Raw(t.HLo), Raw(t.HHi), Raw(t.MLead), Raw(t.HHiControl),
            ((int)t.PoolOrder).ToString(CultureInfo.InvariantCulture), Raw(t.KCap),
            SkipStartValidation.ToString(), ((int)Layer).ToString(CultureInfo.InvariantCulture),
            Raw(CostDivisor), Raw(ThetaStart), Raw(HMax), KappaQ10?.ToString(CultureInfo.InvariantCulture) ?? "-", Raw(Alpha), Raw(CostMultG5), Raw(CostMultG6),
            Raw(AheadCostK), AheadCostPow?.ToString(CultureInfo.InvariantCulture) ?? "-", Raw(AheadCostMax), Raw(HcapGrowthPerYear), Raw(HcapGrowthMax), Raw(MasteryXProg), Raw(MasteryUpkeepMult), ((int)MasteryRules).ToString(CultureInfo.InvariantCulture),
            XHalfByTier.IsDefault ? "-" : string.Join(',', XHalfByTier.Select(x => x.Raw.ToString(CultureInfo.InvariantCulture))),
            ResearchWeightByDiscipline.IsDefault ? "-" : string.Join(',', ResearchWeightByDiscipline.OrderBy(w => w.Discipline, StringComparer.Ordinal).Select(w => w.Discipline + ":" + w.Weight.Raw.ToString(CultureInfo.InvariantCulture))),
            Raw(DeltaH), Raw(CM), Grid.Code.ToString(CultureInfo.InvariantCulture), ((int)EffortShare).ToString(CultureInfo.InvariantCulture),
            .. Reserve ? (string[])["reserve"] : [],
            .. Institutes ? (string[])["institutes"] : [],
        ];
        // Length-prefixed fields written as UTF-16 code units, integers little-endian (the encoding of TableSnapshot.Fingerprint): no value can run into its neighbour.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string field in fields)
        {
            var bytes = new byte[sizeof(int) + field.Length * sizeof(char)];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, field.Length);
            for (int i = 0; i < field.Length; i++)
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(sizeof(int) + i * sizeof(char)), field[i]);
            hash.AppendData(bytes);
        }
        return hash.GetHashAndReset();
    }
}
