using Singularity.Core.Commands;
using Singularity.Core.Products;

namespace Singularity.Session.Tests;

/// <summary>TASK-0134: the commands <c>StartMasteryProgramme</c> / <c>CancelMasteryProgramme</c>, their why_not codes and their log form.</summary>
public class MasteryCommandTests
{
    private static SliceSession Run(long throughMonth)
    {
        SliceSession s = SliceTestData.Create(HorizonConfigs.Config("Main", 1));
        for (long m = 0; m <= throughMonth; m++)
            s.StepThroughMonth(m);
        return s;
    }

    // The first (actor, process) of the programme processes whose check at the next month gives the reason, searched by month from m0.
    private static (SliceSession S, long Month, int Actor, int Number, ProgrammeVerdict Verdict) Find(long m0, bool abroad, Func<ProgrammeVerdict, bool> want)
    {
        SliceSession s = Run(m0 - 1);
        for (long m = m0; m < m0 + 300; m++)
        {
            s.StepThroughMonth(m);
            SliceWorld w = s.World;
            for (int a = 0; a < w.Scenario.ActorCount; a++)
            {
                foreach (int n in w.Products.ProgrammeProcessNumbers)
                {
                    ProgrammeVerdict v = MasteryChecks.Verdict(w, a, n, abroad, m + 1);
                    if (want(v))
                        return (s, m, a, n, v);
                }
            }
        }
        throw new InvalidOperationException("no process with the wanted verdict in 300 months");
    }

    [Fact]
    public void BothCommands_RoundTripThroughTheCommandLog()
    {
        var start = new LoggedCommand(new CommandKey(100, SourceClass.Player, 0, 0), new StartMasteryProgramme(2, 73, true));
        var cancel = new LoggedCommand(new CommandKey(100, SourceClass.Player, 0, 1), new CancelMasteryProgramme(2, 73));
        List<LoggedCommand> back = CommandCodec.DecodeLog(CommandCodec.EncodeLog([start, cancel]));
        Assert.Equal([start, cancel], back);
        Assert.Contains("\"kind\":\"start_mastery_programme\"", CommandCodec.Encode(start));
    }

    [Fact]
    public void Start_OnAProcessThatHasNoProgramme_OrBefore1978_IsRefusedWithAWhyNotCode()
    {
        SliceSession early = Run(99);
        Assert.True(early.Submit(SourceClass.Player, 0, 0, new StartMasteryProgramme(0, 73, false)).Accepted);   // the form is fine
        early.StepThroughMonth(100);
        Assert.Contains("ProgrammeRefused", early.Outcomes[^1]);
        Assert.Contains(nameof(MasteryRefusal.OutOfScope), early.Outcomes[^1]);

        SliceSession late = Run(449);
        Assert.True(late.Submit(SourceClass.Player, 0, 0, new StartMasteryProgramme(0, 38, false)).Accepted);   // process 38: a G1 process
        late.StepThroughMonth(450);
        Assert.Contains(nameof(MasteryRefusal.TierLtMin), late.Outcomes[^1]);
        Assert.False(MasteryChecks.Verdict(late.World, 0, 38, false, 451).Accepted);
        Assert.Equal(CommandRefusal.BadProcess, late.Submit(SourceClass.Player, 0, 1, new StartMasteryProgramme(0, 9999, false)).Reason);
    }

    [Fact]
    public void Start_OnAProcessAnotherProcessLeads_IsRefusedWithCellLedBy_NamingTheLeader()
    {
        var found = Find(440, abroad: false, v => v.Reason == MasteryRefusal.CellLedBy);
        Assert.True(found.Verdict.Detail > 0);
        Assert.NotEqual(found.Number, found.Verdict.Detail);
        Assert.True(found.S.Submit(SourceClass.Player, 0, 0, new StartMasteryProgramme(found.Actor, found.Number, false)).Accepted);
        found.S.StepThroughMonth(found.Month + 1);
        Assert.Contains(nameof(MasteryRefusal.CellLedBy), found.S.Outcomes[^1]);
        Assert.Equal(0, found.S.World.Products.ProgrammeStamp(found.Actor, found.Number));
    }

    [Fact]
    public void Start_AbroadWithoutADonor_IsRefusedNoDonor_AndAtHomeIsAccepted_ThenCancelled()
    {
        var found = Find(440, abroad: false, v => v.Reason == MasteryRefusal.None);
        SliceSession s = found.S;
        SliceWorld w = s.World;
        Assert.Equal(MasteryRefusal.NoDonor, MasteryChecks.Verdict(w, found.Actor, found.Number, true, found.Month + 1).Reason);

        Assert.True(s.Submit(SourceClass.Player, 0, 0, new StartMasteryProgramme(found.Actor, found.Number, false)).Accepted);
        s.StepThroughMonth(found.Month + 1);
        Assert.EndsWith("accepted", s.Outcomes[^1]);
        Assert.True(w.Products.ProgrammeStamp(found.Actor, found.Number) > 0);
        Assert.True(w.Products.ProgrammeDomestic(found.Actor, found.Number));
        Assert.Equal(MasteryRefusal.AlreadyActive, MasteryChecks.Verdict(w, found.Actor, found.Number, false, found.Month + 2).Reason);

        Assert.True(s.Submit(SourceClass.Player, 0, 1, new CancelMasteryProgramme(found.Actor, found.Number)).Accepted);
        s.StepThroughMonth(found.Month + 2);
        Assert.Equal(0, w.Products.ProgrammeStamp(found.Actor, found.Number));
    }
}
