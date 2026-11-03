namespace Singularity.Core.Industry;

/// <summary><c>report_output(site, prc, units, labour_hours)</c> (contract §5); process by stable number.</summary>
public readonly record struct OutputReport(int Site, int ProcessNumber, Kernel.Fx6 Units, Kernel.Fx6 LabourHours);

/// <summary><c>reserve_workforce(site, cat, n, requester_id)</c> (contract §4.6); whole people.</summary>
public readonly record struct WorkforceRequest(int Site, int Category, int RequesterId, long N);

/// <summary>
/// Messages to the capability layer from one sending system (изделие → способность, contract §5). The
/// sender posts in its compute of month t into the slot of its own chunk; the industry merge of the same
/// month reads every slot, sorts by the contract key and clears the box. Per-tick scratch, not state.
/// A sender must run on the industry cadence (monthly), otherwise its messages would wait for the next merge.
/// </summary>
public sealed class IndustryMailbox
{
    private readonly List<OutputReport>[] _reports;
    private readonly List<WorkforceRequest>[] _requests;

    internal IndustryMailbox(int senderChunks)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(senderChunks, 1);
        _reports = new List<OutputReport>[senderChunks];
        _requests = new List<WorkforceRequest>[senderChunks];
        for (int c = 0; c < senderChunks; c++)
        {
            _reports[c] = new();
            _requests[c] = new();
        }
    }

    public void ReportOutput(int senderChunk, OutputReport report) => _reports[senderChunk].Add(report);

    public void ReserveWorkforce(int senderChunk, WorkforceRequest request) => _requests[senderChunk].Add(request);

    internal void Drain(List<OutputReport> reports, List<WorkforceRequest> requests)
    {
        foreach (List<OutputReport> slot in _reports)
        {
            reports.AddRange(slot);
            slot.Clear();
        }
        foreach (List<WorkforceRequest> slot in _requests)
        {
            requests.AddRange(slot);
            slot.Clear();
        }
    }
}
