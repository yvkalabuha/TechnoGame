namespace Singularity.Core.Tests.Industry;

/// <summary>Bit-for-bit equality of the C-B rule with lab/reference/jet_slice/jet_ref.py (TASK-0020 acceptance).</summary>
public class CbReferenceEqualityTests
{
    [Fact(Skip = "PENDING: jet_ref.py is being updated to C-B (culture_step, proc_E, smoothstep with one rounding, E_useful with ceiling) by simulation-engineer. Add trace comparison here once the reference trajectory is regenerated.")]
    public void CbCultureStep_AndEffortAllocation_MatchReference_BitForBit()
    {
    }
}
