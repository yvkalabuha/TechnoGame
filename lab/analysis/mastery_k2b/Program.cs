// TASK-0134 step 4, K2b' harness (docs/simulation/g5-g6-probe.md §9.3). Usage (from the repo root):
//   dotnet build lab/analysis/mastery_k2b -c Release -o lab/results/mastery_k2b
//   dotnet lab/results/mastery_k2b/k2b.dll <manifest> <point> <seed> <out.csv>
// Runs the point of the manifest (JetSlice world from SliceParameters, threads 1, the manifest's months) and, after every month, at the home
// site of each actor, for every programme process (tier >= 5): proc_E, proc_v, CultureYield (raw Fx6) and the programme stamp
// ((start month + 1), 0 = none). Read-only; the last line of stdout is the final state hash, which must equal the golden_hash of the run.
using System.Reflection;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Products;
using Singularity.Lab;
using Singularity.Lab.Models.Slice;
using Singularity.Session;

string manifestPath = Path.GetFullPath(args[0]); int point = int.Parse(args[1]); ulong seed = ulong.Parse(args[2]); string outPath = args[3];
Manifest man = Manifest.Parse(File.ReadAllText(manifestPath), manifestPath);
GameData data = SliceData.Load(Path.GetDirectoryName(manifestPath)!);
var sp = SliceParameters.Parse(man.Points[point].CreateParamSet(), seed, 1, 1, false);
var s = SliceSession.Create(data, sp.Config);
object world = typeof(SliceSession).GetField("_world", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
var wt = world.GetType();
var ind = (IndustrySystem)wt.GetProperty("Industry")!.GetValue(world)!;
var prod = (ProductsSystem)wt.GetProperty("Products")!.GetValue(world)!;
var sc = (ScenarioData)wt.GetProperty("Scenario")!.GetValue(world)!;
var lay = (IndustryLayout)wt.GetProperty("IndustryLayout")!.GetValue(world)!;
var snap = (ICultureSnapshot)ind;
int[] procs = prod.ProgrammeProcessNumbers.ToArray();
// Ticks of the probe manifests (1945-09 calendar): 31168 = 1024 months, 31046 = 1020 months.
long months = man.Ticks switch { 31168 => 1024, 31046 => 1020, _ => throw new InvalidOperationException($"ticks {man.Ticks}: months unknown") };
using var w = new StreamWriter(outPath);
w.WriteLine("month,actor,prc,e,v,cy,stamp");
for (long m = 0; m < months; m++)
{
    s.StepThroughMonth(m);
    long t = m + 1;
    for (int a = 0; a < sc.ActorCount; a++)
    {
        int site = lay.HomeSite(a);
        foreach (int pn in procs)
        {
            int r = lay.RowOf(pn);
            if (r < 0)
                continue;
            long e = snap.E(site, r).Raw, v = snap.V(site, r).Raw, cy = ind.Yield(site, r, m).CultureYield.Raw, st = prod.ProgrammeStamp(a, pn);
            // Only months from 1978-01 with effort, output, a programme or a culture yield at y_start: the rest cannot start or end a K2b' span.
            if (m >= 388 && (e != 0 || v != 0 || st != 0 || cy >= 500_000))
                w.WriteLine($"{m},{sc.ActorId[a]},{pn},{e},{v},{cy},{st}");
        }
    }
}
Console.WriteLine($"months {months} point {point} seed {seed}");
Console.WriteLine(s.LastHash);
