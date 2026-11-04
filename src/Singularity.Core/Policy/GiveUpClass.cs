namespace Singularity.Core.Policy;

/// <summary>Cause class of a stage-О give-up (spec A40 v0.15, L-O0): hidden defects keep the project from launching, or its processes are not admitted.</summary>
public enum GiveUpClass
{
    Defects = 0,
    Processes = 1,
}
