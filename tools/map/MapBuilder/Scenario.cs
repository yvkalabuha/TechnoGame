namespace Singularity.MapBuilder;

internal sealed class PolityEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "state";        // state | joint
    public bool Government { get; set; } = true;
    public List<string> Members { get; set; } = new();
    public string? Actor { get; set; }
    public string? NeAdm0 { get; set; }                  // set when the entity coincides with a NE ADM0_A3 code (ADR-0011 §1)
}
internal sealed class PolityFile { public List<PolityEntry> Polities { get; set; } = new(); }

internal sealed class OwnerRow
{
    public string? Region { get; set; }
    public string? Province { get; set; }
    public string? Owner { get; set; }   // null only with status unclaimed (#401)
    public string Controller { get; set; } = "";
    public string Status { get; set; } = "";
    public string Source { get; set; } = "";
    public string Confidence { get; set; } = "";       // H | M | L (owners-1945-world.md)
    public string? Note { get; set; }
}
internal sealed class OwnerFile
{
    public string AsOf { get; set; } = "";
    public List<OwnerRow> Defaults { get; set; } = new();
    public List<OwnerRow> Exceptions { get; set; } = new();
}

internal sealed class Resolved
{
    public string Owner = "", Controller = "", Status = "";
}

/// <summary>Named exception to the 5-15 core quota (ADR-0022 §3 variant A, #406); the reason is kept in the data.</summary>
internal sealed class GreatPowerException
{
    public string Id { get; set; } = "";
    public int MaxCore { get; set; }
    public string Reason { get; set; } = "";
}
/// <summary>Build-time list of great powers for the quota check (ADR-0022 §2.1); not game data, not part of scenario_hash.</summary>
internal sealed class GreatPowerFile
{
    public string Scenario { get; set; } = "";
    public string Source { get; set; } = "";
    public List<string> Ids { get; set; } = new();
    public List<GreatPowerException> Exceptions { get; set; } = new();
}
