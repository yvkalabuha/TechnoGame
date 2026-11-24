// TASK-0134 step (1), "Diagnosis B" harness (docs/simulation/g5-g6-probe.md §8). Usage (from the repo root):
//   dotnet build lab/analysis/g5g6_diag_b -c Release -o lab/results/g5g6_diag_b
//   dotnet lab/results/g5g6_diag_b/diagb.dll <dir with mods/core> <seed> <outPrefix>
// Same run as lab/analysis/g5g6_diag (JetSlice FULL, params of TASK-0133-g5g6-probe.json, threads 1, 1024 months), read-only.
// Every month, at the home site of each actor, after the month: for processes 38, 42, 43, 73 (MET) and 51, 52 (MECH):
// knows, proc_v, proc_E, planned, GrowthNominal (V_OC), eligible, limiter0 per active dim, EUseful; per MET/MECH cell:
// H, M, Hcap, lead (CbCultureRule internals by reflection), drive of the last step. <outPrefix>.prc.csv, .cell.csv;
// <outPrefix>.prj.csv lists every month a design whose prc_set holds 43 or 73 is in stage Project/Prototype/Series.
using System.Reflection;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Products;
using Singularity.Kernel;
using Singularity.Lab.Models.Slice;
using Singularity.Lab.Simulation;
using Singularity.Session;

string dir = args[0]; ulong seed = ulong.Parse(args[1]); string pre = args[2];
var kv = new List<KeyValuePair<string, ParamValue>> {
  new("LINE_CLOSE", new ParamValue(ParamKind.String, "YPROD")), new("TOOLING_TARGET", new ParamValue(ParamKind.String, "V5")),
  new("POOL_ORDER", new ParamValue(ParamKind.String, "CAN")), new("detail", new ParamValue(ParamKind.String, "full")) };
GameData data = SliceData.Load(dir);
var sp = SliceParameters.Parse(new ParamSet(kv), seed, 1, 1, false);
var s = SliceSession.Create(data, sp.Config);
object world = typeof(SliceSession).GetField("_world", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
var wt = world.GetType();
var ind = (IndustrySystem)wt.GetProperty("Industry")!.GetValue(world)!;
var prod = (ProductsSystem)wt.GetProperty("Products")!.GetValue(world)!;
var sc = (ScenarioData)wt.GetProperty("Scenario")!.GetValue(world)!;
var lay = (IndustryLayout)wt.GetProperty("IndustryLayout")!.GetValue(world)!;
var snap = (ICultureSnapshot)ind;
var cb = typeof(CbCultureRule);
const BindingFlags SF = BindingFlags.NonPublic | BindingFlags.Static;
var mElig = cb.GetMethod("Eligible", SF)!; var mLim0 = cb.GetMethod("Limiter0", SF)!; var mLead = cb.GetMethod("Lead", SF)!;
int nd = (int)typeof(ProductsSystem).GetField("_d", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(prod)!;
using var wp = new StreamWriter(pre + ".prc.csv"); using var wc = new StreamWriter(pre + ".cell.csv"); using var wj = new StreamWriter(pre + ".prj.csv");
wp.WriteLine("month,actor,prc,knows,v,E,planned,gn,eligible,lim0_0,lim0_1,lim0_2,euseful,yield,limiter");
wc.WriteLine("month,actor,cluster,dim,H,M,Hcap,lead,drive,sat");
wj.WriteLine("month,actor,design,stage,prcset");
int[] procs = [38, 42, 43, 73, 51, 52];
string[] cls = ["MET", "MECH"];
for (long m = 0; m < 1024; m++)
{
  s.StepThroughMonth(m);
  long t = m + 1; // the snapshot the next step reads
  for (int a = 0; a < sc.ActorCount; a++)
  {
    int site = lay.HomeSite(a); string act = sc.ActorId[a];
    foreach (int pn in procs)
    {
      int r = lay.RowOf(pn); if (r < 0) continue;
      bool knows = snap.Knows(site, r, t);
      string lim0 = string.Join(",", Enumerable.Range(0, 3).Select(d => lay.Req[r][d] > Fx6.Zero ? mLim0.Invoke(null, [snap, site, r, d, t])!.ToString() : "-"));
      wp.WriteLine($"{m},{act},{pn},{(knows ? 1 : 0)},{snap.V(site, r).ToDouble():0.###},{snap.E(site, r).ToDouble():0.###},{(snap.Planned(site, r, t) ? 1 : 0)},{snap.GrowthNominal(site, r, t).ToDouble():0.###},{((bool)mElig.Invoke(null, [snap, site, r, t])! ? 1 : 0)},{lim0},{ind.EUseful(site, r, t).ToDouble():0.###},{ind.Yield(site, r, m).Yield.ToDouble():0.####},{ind.Limiter(site, r, t).Value}");
    }
    foreach (string cn in cls)
    {
      int c = lay.Clusters.IndexOf(Enum.Parse<Cluster>(cn));
      for (int d = 0; d < 3; d++)
      {
        int lead = (int)mLead.Invoke(null, [snap, site, c, d, t])!;
        var st = ind.LastCulture(site, c, (Dimension)d);
        wc.WriteLine($"{m},{act},{cn},{d},{snap.H(site, c, d).ToDouble():0.####},{snap.M(site, c, d).ToDouble():0.####},{snap.Hcap(site, d, t).ToDouble():0.####},{(lead < 0 ? -1 : lay.Number[lead])},{st.Drive.ToDouble():0.######},{st.Sat.ToDouble():0.####}");
      }
    }
    for (int d = 0; d < nd; d++)
    {
      var stg = prod.Stage(a, d);
      if (stg is not (DesignStage.Project or DesignStage.Prototype or DesignStage.Series)) continue;
      int[] ps = prod.PrcSet(a, d);
      if (Array.IndexOf(ps, 43) < 0 && Array.IndexOf(ps, 73) < 0) continue;
      wj.WriteLine($"{m},{act},{d},{stg},{string.Join(" ", ps)}");
    }
  }
}
Console.WriteLine($"seed {seed} done hash {s.LastHash}");
