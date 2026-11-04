using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Core.WhyNot;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>
/// The information print of spec §6.11 (seeds 1…20, main against <c>LIC_OFF</c>): licences by pair, the month of the first fielded series, the disruptions,
/// the quality programmes by P1/P3, the training series of P3, the first link of <c>why_not</c> of FRA by year, the check E-LIC-0. Not a criterion (#150).
/// Run it with <c>SINGULARITY_T6D_PRINT=1</c>; the file is <c>SINGULARITY_T6D_PRINT_FILE</c> or <c>/tmp/t6d-print.txt</c>.
/// </summary>
public class T6dPrint
{
    private sealed class Run
    {
        public string[] Ids = [];
        public PolicyProfile[] Profiles = [];
        public int[] FirstSeries = [];                       // by actor: month of the first unit in service, −1
        public readonly List<string> Licences = [];
        public int[] Disruptions = [];
        public int[] AlloyBindMonths = [];                   // by actor: months a line was bound by the alloy
        public long SilentByBind;
        public readonly List<string> Programmes = [];
        public int TrainingMonthsP3;
        public readonly List<string> FraFirstLink = [];
        public readonly List<string> FraH = [];              // by month: the H columns of FRA
        public int FraFirstProject = -1;                     // month of the first design of FRA
        public long Bad;
        public int FirstOfferToSun = -1;
    }

    private static Run Execute(ulong seed, bool licOff)
    {
        SliceSession s = SliceTestData.Create(new SliceRunConfig { Seed = seed, LicOff = licOff, Threads = 4, ChunkSize = 2 });
        SliceWorld w = s.World;
        int n = w.Scenario.ActorCount;
        var r = new Run
        {
            Ids = [.. Enumerable.Range(0, n).Select(a => w.Scenario.ActorId[a])],
            Profiles = [.. Enumerable.Range(0, n).Select(a => w.Policy.Profile(a))],
            FirstSeries = [.. Enumerable.Repeat(-1, n)],
            Disruptions = new int[n],
            AlloyBindMonths = new int[n],
        };
        var lastEnd = Enumerable.Repeat(-1, n).ToArray();
        var lastStart = Enumerable.Repeat(-1, n).ToArray();
        var lastQp = Enumerable.Repeat(-1, n).ToArray();
        int fra = w.Scenario.ActorIndex("FRA");
        string[] hCols = [.. w.Table.Columns.Where(c => c.Name.StartsWith("ind.H.", StringComparison.Ordinal) && !c.Name.StartsWith("ind.H.unmet", StringComparison.Ordinal)).Select(c => c.Name)];
        for (int m = 0; m < 384; m++)
        {
            s.StepThroughMonth(m);
            for (int a = 0; a < n; a++)
            {
                for (int d = 0; d < w.ProductsLayout.Designs; d++)
                {
                    if (r.FirstSeries[a] < 0 && w.Products.InService(a, d) > Fx6.Zero)
                        r.FirstSeries[a] = m;
                    if (w.Products.Stage(a, d) == DesignStage.Series && w.Products.Bind(a, d) == LineBind.Alloy)
                        r.AlloyBindMonths[a]++;
                    if (a == fra && r.FraFirstProject < 0 && w.Products.Stage(a, d) != DesignStage.None)
                        r.FraFirstProject = m;
                }
                if (w.Policy.Disruption(a).End != lastEnd[a])
                {
                    r.Disruptions[a]++;
                    lastEnd[a] = w.Policy.Disruption(a).End;
                }
                if (w.Policy.LicenceStart(a) != lastStart[a])
                {
                    lastStart[a] = w.Policy.LicenceStart(a);
                    r.Licences.Add($"{r.Ids[w.Policy.LicenceSeller(a)]}->{r.Ids[a]} gen {w.Policy.LicenceGen(a)} month {lastStart[a]} {r.Profiles[a]} price {-w.Policy.LicenceFlow(a).Raw}");
                }
                if (m == 0 && r.Ids[a] == "SUN" && w.Policy.OfferMask(a) != 0)
                    r.FirstOfferToSun = 0;
                if (w.Policy.QualityProgrammeActive(a) && w.Policy.QualityProgrammeStart(a) != lastQp[a])
                {
                    lastQp[a] = w.Policy.QualityProgrammeStart(a);
                    if (lastQp[a] > 0)
                        r.Programmes.Add($"{r.Ids[a]} {r.Profiles[a]} start {lastQp[a]}");
                }
                if (r.Profiles[a] == PolicyProfile.Adaptive && w.Policy.TrainingTarget(a) >= 0)
                    r.TrainingMonthsP3++;
                for (int k = 1; k <= 4; k++)
                    r.Bad += w.Policy.BadLicenceCount(a, (BadLicence)k);
            }
            if (fra >= 0)
            {
                r.FraH.Add(string.Join(',', hCols.Select(c => ((LongColumn)w.Table.Columns.First(x => x.Name == c)).Current[fra])));
                if (m % 12 == 11)
                {
                    Link[] list = [.. new PolicyEventsAdapter(w).WhyNot(fra, m + 1)];
                    r.FraFirstLink.Add(list.Length == 0 ? $"{m + 1}:none" : $"{m + 1}:{list[0].Layer}/{list[0].Cause}/{list[0].Process}/{list[0].Dim}/eta {list[0].EtaMonths?.ToString() ?? "null"}");
                }
            }
        }
        r.SilentByBind = Enumerable.Range(0, n).Sum(a => w.Policy.FleetSilentByBindMonths(a));
        return r;
    }

    private sealed class SkipUnlessPrint : FactAttribute
    {
        public SkipUnlessPrint()
        {
            if (Environment.GetEnvironmentVariable("SINGULARITY_T6D_PRINT") != "1")
                Skip = "set SINGULARITY_T6D_PRINT=1 to run the T6d print (spec §6.11)";
        }
    }

    [SkipUnlessPrint]
    public void Print_Seeds1To20_Section611_Information()
    {
        string path = Environment.GetEnvironmentVariable("SINGULARITY_T6D_PRINT_FILE") ?? "/tmp/t6d-print.txt";
        var o = new System.Text.StringBuilder();
        int eLic0Violations = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            Run main = Execute(seed, licOff: false), off = Execute(seed, licOff: true);
            int fra = Array.IndexOf(main.Ids, "FRA");
            // E-LIC-0: the culture of FRA is equal in main and LIC_OFF until its first design.
            int until = main.FraFirstProject < 0 ? 384 : main.FraFirstProject;
            for (int m = 0; m < Math.Min(until, off.FraFirstProject < 0 ? 384 : off.FraFirstProject); m++)
            {
                if (main.FraH[m] != off.FraH[m])
                {
                    eLic0Violations++;
                    o.AppendLine($"seed {seed}: E-LIC-0 VIOLATION at month {m}");
                    break;
                }
            }
            o.AppendLine($"== seed {seed}; profiles {string.Join('/', main.Ids.Zip(main.Profiles, (i, p) => $"{i}:{p}"))}");
            o.AppendLine($"   first series main {string.Join(' ', main.Ids.Zip(main.FirstSeries, (i, m) => $"{i}={m}"))}");
            o.AppendLine($"   first series LIC_OFF {string.Join(' ', off.Ids.Zip(off.FirstSeries, (i, m) => $"{i}={m}"))}");
            o.AppendLine($"   licences main ({main.Licences.Count}): {string.Join("; ", main.Licences)}");
            o.AppendLine($"   disruptions {string.Join(' ', main.Ids.Zip(main.Disruptions, (i, c) => $"{i}={c}"))}; months bound by alloy {string.Join(' ', main.Ids.Zip(main.AlloyBindMonths, (i, c) => $"{i}={c}"))}; fleet rule silent by bind {main.SilentByBind}");
            o.AppendLine($"   programmes after t0: main [{string.Join("; ", main.Programmes)}] LIC_OFF [{string.Join("; ", off.Programmes)}]; training months P3 main {main.TrainingMonthsP3}");
            o.AppendLine($"   FRA first why_not link by year: {string.Join(" | ", main.FraFirstLink.Where((_, i) => i % 4 == 3))}");
            o.AppendLine($"   BAD_LICENCE {main.Bad}; FRA first project main {main.FraFirstProject} LIC_OFF {off.FraFirstProject}; fra index {fra}");
        }
        o.AppendLine($"E-LIC-0 violations: {eLic0Violations}");
        File.WriteAllText(path, o.ToString());
        Assert.Equal(0, eLic0Violations);
    }
}
