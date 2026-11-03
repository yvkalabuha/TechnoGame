namespace Singularity.Core.Data;

/// <summary>Knowledge node types (RR-0008 §3).</summary>
public enum NodeType { FND, PRN, ENG, CMP, PRC, APP }

/// <summary>Production clusters (LAYER_CONTRACT §2: 8 clusters).</summary>
public enum Cluster { MET, CHEM, MECH, ELEC, AERO, NUC, ENER, BASE }

/// <summary>Knowledge disciplines of the jet slice (slice spec §1.2).</summary>
public enum Discipline { Aero, Prop, Metal, Chem, Mech }

/// <summary>Capability dimensions (decision #29).</summary>
public enum Dimension { Precision, Purity, QualityControl }

/// <summary>Discovery mode: deterministic, hazard (hz) or key hazard node (★, decision #32).</summary>
public enum Hazard { None, Hz, Key }

public enum ActorClass { Great, Middle, Small }

public enum DesignVariant { Superior, Producible }

/// <summary>Data spellings of the enums. Explicit tables: no reflection, no culture rules.</summary>
public static class DataNames
{
    public static readonly (string Name, NodeType Value)[] NodeTypes =
        [("FND", NodeType.FND), ("PRN", NodeType.PRN), ("ENG", NodeType.ENG), ("CMP", NodeType.CMP), ("PRC", NodeType.PRC), ("APP", NodeType.APP)];

    public static readonly (string Name, Cluster Value)[] Clusters =
    [
        ("MET", Cluster.MET), ("CHEM", Cluster.CHEM), ("MECH", Cluster.MECH), ("ELEC", Cluster.ELEC),
        ("AERO", Cluster.AERO), ("NUC", Cluster.NUC), ("ENER", Cluster.ENER), ("BASE", Cluster.BASE),
    ];

    public static readonly (string Name, Discipline Value)[] Disciplines =
        [("aero", Discipline.Aero), ("prop", Discipline.Prop), ("metal", Discipline.Metal), ("chem", Discipline.Chem), ("mech", Discipline.Mech)];

    public static readonly (string Name, Dimension Value)[] Dimensions =
        [("precision", Dimension.Precision), ("purity", Dimension.Purity), ("quality_control", Dimension.QualityControl)];

    public static readonly (string Name, Hazard Value)[] Hazards =
        [("none", Hazard.None), ("hz", Hazard.Hz), ("key", Hazard.Key)];

    public static readonly (string Name, ActorClass Value)[] ActorClasses =
        [("great", ActorClass.Great), ("middle", ActorClass.Middle), ("small", ActorClass.Small)];

    public static readonly (string Name, DesignVariant Value)[] Variants =
        [("superior", DesignVariant.Superior), ("producible", DesignVariant.Producible)];

    public static string NameOf<T>((string Name, T Value)[] table, T value) where T : struct, Enum
    {
        foreach ((string name, T v) in table)
        {
            if (EqualityComparer<T>.Default.Equals(v, value))
                return name;
        }
        throw new ArgumentOutOfRangeException(nameof(value), value, "No data name.");
    }
}
