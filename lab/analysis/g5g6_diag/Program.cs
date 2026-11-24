// TASK-0133 diagnosis harness. Usage (from the repo root):
//   dotnet build lab/analysis/g5g6_diag -c Release -o lab/results/g5g6_diag
//   dotnet lab/results/g5g6_diag/diag.dll lab/manifests 1 10 lab/results/task-0133/diag.csv
// Runs JetSlice FULL with the params of TASK-0133-g5g6-probe.json (threads 1) for 1024 months and writes, every December,
// culture_H / tooling_M / Hcap / tool_unmet per (actor, cluster, dim) and yield / ramp / proc_X / proc_v / proc_E / limiter / mastered_run
// for processes 41-43, 51, 52, 73-75, 80. Prints the final state hash: it must equal golden_hash of the matrix run of the same seed.
// The first argument is any directory under a repo that holds Singularity.sln and mods/core (a counterfactual copy works the same way).
using System.Reflection;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Kernel;
using Singularity.Lab.Models.Slice;
using Singularity.Lab.Simulation;
using Singularity.Session;

// args: manifestDir seedFrom seedTo outCsv [KEY=VALUE]
string dir = args[0]; ulong s0 = ulong.Parse(args[1]), s1 = ulong.Parse(args[2]); string outPath = args[3];
var kv = new List<KeyValuePair<string, ParamValue>> {
  new("LINE_CLOSE", new ParamValue(ParamKind.String, "YPROD")), new("TOOLING_TARGET", new ParamValue(ParamKind.String, "V5")),
  new("POOL_ORDER", new ParamValue(ParamKind.String, "CAN")), new("detail", new ParamValue(ParamKind.String, "full")) };
for (int i = 4; i < args.Length; i++) { var p = args[i].Split('='); kv.Add(new(p[0], new ParamValue(char.IsDigit(p[1][0]) ? ParamKind.Number : ParamKind.String, p[1]))); }
GameData data = SliceData.Load(dir);
using var w = new StreamWriter(outPath);
w.WriteLine("seed,month,actor,kind,key,a,b,c,d,e,f");
int[] procs = [41, 42, 43, 51, 52, 73, 75, 80, 74];
for (ulong seed = s0; seed <= s1; seed++)
{
  var sp = SliceParameters.Parse(new ParamSet(kv), seed, 1, 1, false);
  var s = SliceSession.Create(data, sp.Config);
  object world = typeof(SliceSession).GetField("_world", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
  var wt = world.GetType();
  var ind = (IndustrySystem)wt.GetProperty("Industry")!.GetValue(world)!;
  var sc = (ScenarioData)wt.GetProperty("Scenario")!.GetValue(world)!;
  var lay = (IndustryLayout)wt.GetProperty("IndustryLayout")!.GetValue(world)!;
  var snap = (ICultureSnapshot)ind;
  for (long m = 0; m < 1024; m++)
  {
    s.StepThroughMonth(m);
    if (m % 12 != 11) continue;
    for (int a = 0; a < sc.ActorCount; a++)
    {
      int site = lay.HomeSite(a);
      string act = sc.ActorId[a];
      for (int c = 0; c < lay.Clusters.Length; c++)
        for (int d = 0; d < 3; d++)
          w.WriteLine($"{seed},{m},{act},cell,{lay.Clusters[c]}.{d},{ind.CultureH(site, c, (Dimension)d).ToDouble():0.####},{ind.ToolingM(site, c, (Dimension)d).ToDouble():0.####},{snap.Hcap(site, d, m).ToDouble():0.####},{ind.ToolingUnmet(site, c, (Dimension)d).ToDouble():0.####},,");
      foreach (int pn in procs)
      {
        int r = lay.RowOf(pn); if (r < 0) continue;
        var y = ind.Yield(site, r, m);
        var lim = ind.Limiter(site, r, m);
        w.WriteLine($"{seed},{m},{act},prc,{pn},{y.Yield.ToDouble():0.####},{y.Ramp.ToDouble():0.####},{ind.ProcX(site, r).ToDouble():0.#},{ind.ProcV(site, r).ToDouble():0.#},{ind.ProcE(site, r).ToDouble():0.######},{lim.Value}/{lim.SubjectProcess}/mr{ind.MasteredRun(site, r)}");
      }
    }
  }
  Console.WriteLine($"seed {seed} done hash {s.LastHash}");
}
