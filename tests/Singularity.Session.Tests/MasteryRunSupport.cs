using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>Observations of a run for the invariants of the mastery rules (TASK-0134): taken after every month from the public views of the world.</summary>
internal sealed class MasteryRun
{
    public int GateViolations, GateStarts, MaxMonthsBelow, ProgrammeStarts, ProgrammeReady, ProgrammeTimeouts, BadProgramme;
    public string Hash = "";

    public static MasteryRun Execute(SliceRunConfig config, int months)
    {
        SliceSession s = SliceTestData.Create(config);
        SliceWorld w = s.World;
        int actors = w.Scenario.ActorCount, designs = w.ProductsLayout.Designs;
        var run = new MasteryRun();
        int first = (int)w.ProductsParameters.Mastery.FromMonth;
        Fx6 yProd = w.ProductsParameters.YProd;
        int tAb = w.PolicyParameters.TAb;
        // culture min yield of every tier >= 5 design, by month (ring of three), stage of the month before, months below y_prod per actor
        var cm = new Fx6[3][];
        for (int i = 0; i < 3; i++)
            cm[i] = new Fx6[actors * designs];
        var stage = new DesignStage[actors * designs];
        var below = new int[actors];
        var candidateOf = new (int Design, int Generation)[actors];
        var progStart = new Dictionary<(int, int), long>();
        for (long t = 0; t < months; t++)
        {
            s.StepThroughMonth(t);
            Fx6[] now = cm[t % 3];
            for (int a = 0; a < actors; a++)
            {
                for (int d = 0; d < designs; d++)
                {
                    now[a * designs + d] = Fx6.One;
                    if (!Tier5(w, a, d, out int[] prc))
                        continue;
                    Fx6 min = Fx6.One;
                    foreach (int n in prc)
                    {
                        Fx6 y = w.ProductsInputs.CultureYield(a, n, t + 1);
                        if (y < min)
                            min = y;
                    }
                    now[a * designs + d] = min;
                    DesignStage st = w.Products.Stage(a, d);
                    if (t >= first + 2 && st == DesignStage.Series && stage[a * designs + d] == DesignStage.Prototype)
                    {
                        run.GateStarts++;
                        if (cm[(t - 2) % 3][a * designs + d] < yProd)
                            run.GateViolations++;
                    }
                }
                for (int d = 0; d < designs; d++)
                    stage[a * designs + d] = w.Products.Stage(a, d);
                // the candidate line: two lines, tier >= 5, real y_min below y_prod
                int c = Candidate(w, a);
                // The same candidate (slot and its generation) month after month: a new candidate starts the count again.
                var id = c < 0 ? (-1, 0) : (c, w.Products.Generation(a, c));
                if (id != candidateOf[a])
                    below[a] = 0;
                candidateOf[a] = id;
                // Before the rules (1978-01) a candidate may stand below y_prod for as long as it likes: the count starts with the rules.
                if (t >= first && c >= 0 && Tier5(w, a, c, out int[] cprc) && cprc.Min(n => w.ProductsInputs.AdmittedYield(a, n, t + 1).Raw) < yProd.Raw)
                    below[a]++;
                else
                    below[a] = 0;
                run.MaxMonthsBelow = Math.Max(run.MaxMonthsBelow, below[a]);
                foreach (int n in w.Products.ProgrammeProcessNumbers)
                {
                    long stamp = w.Products.ProgrammeStamp(a, n);
                    bool had = progStart.TryGetValue((a, n), out long since);
                    if (stamp > 0 && !had)
                    {
                        progStart[(a, n)] = stamp;
                        run.ProgrammeStarts++;
                    }
                    else if (stamp == 0 && had)
                    {
                        progStart.Remove((a, n));
                        if (w.ProductsInputs.CultureYield(a, n, t + 1) >= yProd)
                            run.ProgrammeReady++;
                        else
                            run.ProgrammeTimeouts++;
                    }
                }
            }
        }
        for (int a = 0; a < actors; a++)
            run.BadProgramme += (int)w.Products.RejectionCount(a, CommandRejection.BadProgramme);
        run.Hash = s.LastHash;
        return run;
    }

    private static bool Tier5(SliceWorld w, int a, int d, out int[] prc)
    {
        prc = [];
        if (w.Products.Stage(a, d) is DesignStage.None or DesignStage.Withdrawn)
            return false;
        prc = w.Products.PrcSet(a, d);
        return prc.Any(n => w.ProductsLayout.ProcTier[w.ProductsLayout.ProcIndex(n)] >= MasteryParams.MinTier);
    }

    private static int Candidate(SliceWorld w, int a)
    {
        int one = -1, two = -1;
        for (int d = 0; d < w.ProductsLayout.Designs; d++)
        {
            if (w.Products.Stage(a, d) != DesignStage.Series)
                continue;
            if (one < 0) one = d;
            else if (two < 0) two = d;
            else return -1;
        }
        return two < 0 ? -1 : PolicySystem.Candidate(one, w.Products.PlatformGen(a, one), w.Products.StageOMonth(a, one), two, w.Products.PlatformGen(a, two), w.Products.StageOMonth(a, two));
    }
}
