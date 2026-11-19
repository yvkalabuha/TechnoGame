using System.Collections.Generic;
using Singularity.Client.Spike;
using Xunit;

namespace Singularity.Client.Audio.Tests;

public class ValidatorFormatTests
{
    [Fact]
    public void EmptyMeasurementSet_IsAFailureNotAPass()
    {
        // TASK-0117 review: T6 over a set with no measurements printed "PASS (0/0)" and so proved nothing
        string text = TextureValidator.Format("T6 edge band", new List<PairResult>());
        Assert.Contains("verdict: FAIL", text);
        Assert.Contains("NO MEASUREMENTS", text);
    }

    [Fact]
    public void FailingPair_IsReportedAsFail()
    {
        string text = TextureValidator.Format("t", new List<PairResult> { new("a", true, ""), new("b", false, "x") });
        Assert.Contains("FAIL b", text);
        Assert.Contains("verdict: FAIL (1/2", text);
    }
}
