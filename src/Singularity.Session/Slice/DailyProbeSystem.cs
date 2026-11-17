using Singularity.Kernel;

namespace Singularity.Session;

/// <summary>
/// Execution-only probe (ADR-0016 T1a): a daily system that writes nothing, so every day is a non-empty tick
/// (the runner copies the tables and swaps). Used to measure the direct daily mode.
/// </summary>
internal sealed class DailyProbeSystem : ITickSystem
{
    public int SystemId => 900;

    public Cadence Cadence => Cadence.Daily;

    public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd)
    {
    }

    public void Merge(in TickContext context, int chunkCount)
    {
    }

    public void Post(in TickContext context)
    {
    }
}
