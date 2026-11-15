// HYP-0004 E1/E2 diagnostic host (introduction-channel-sketch.md §7). Runs the slice session in the calibration view mode (the world of
// JetSliceCalib) with the Lab sketch rule CULTURE_VARIANT, and writes (1) the design lifecycle with its prc_set per actor (Session views
// only) and (2) the yield parts of the tier-4 star PRC 40/49 every month (read-only reflection into the internal world, as the sketch hook).
// Usage: dotnet dump_host.dll SEED0 N OUT_PREFIX [KEY=VALUE ...]   -> OUT_PREFIX_designs.csv, OUT_PREFIX_yield.csv
// Deterministic: one session per seed, rows sorted by seed. Not part of any build; diagnosis only.
using System.Globalization;
using System.Reflection;
using Singularity.Core.Industry;
using Singularity.Core.Products;
using Singularity.Core.Simulation;
using Singularity.Kernel;
using Singularity.Lab.Models.Slice;
using Singularity.Session;

var kv = new List<KeyValuePair<string, ParamValue>>();
ulong seed0 = ulong.Parse(args[0], CultureInfo.InvariantCulture);
int n = int.Parse(args[1], CultureInfo.InvariantCulture);
string prefix = args[2];
for (int i = 3; i < args.Length; i++)
{
    string[] parts = args[i].Split('=');
    kv.Add(new(parts[0], new ParamValue(char.IsDigit(parts[1][0]) ? ParamKind.Number : ParamKind.String, parts[1])));
}
string repo = AppContext.BaseDirectory;
while (!File.Exists(Path.Combine(repo, "Singularity.sln")))
    repo = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(repo)) ?? throw new InvalidOperationException("repository root not found");
GameDataHolder.Data = SliceData.Load(Path.Combine(repo, "lab", "manifests"));
int[] watch = [40, 49];
var results = new (List<string> Des, List<string> Yld)[n];
Parallel.For(0, n, new ParallelOptions { MaxDegreeOfParallelism = 16 }, k =>
{
    ulong seed = seed0 + (ulong)k;
    SliceParameters p = SliceParameters.Parse(new ParamSet(kv), seed, 1, 1, true);
    SliceSession s = SliceSession.Create(GameDataHolder.Data!, p.Config);
    IntroCultureRule.Install(s, p.CultureVariant);
    object world = typeof(SliceSession).GetProperty("World", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
    var ind = (IndustrySystem)world.GetType().GetProperty("Industry")!.GetValue(world)!;
    var cal = new TickCalendar(s.Date);
    var des = new List<string>();
    var yld = new List<string>();
    // design slot lifecycle: a slot is a new design when it reappears in stage Project after a later stage or after a gap
    var open = new Dictionary<(int, int), long[]>();
    var info = new Dictionary<(int, int), string>();
    var seen = new HashSet<(int, int)>();
    void Close((int, int) key)
    {
        long[] f = open[key];
        des.Add($"{seed},{key.Item1},{key.Item2},{info[key]},{f[0]},{f[1]},{f[2]},{f[3]},{f[4]}");
        open.Remove(key);
    }
    for (long t = 0; t < 384; t++)
    {
        s.StepTo(cal.TickOfMonth(t));
        SliceView v = s.View;
        var now = new HashSet<(int, int)>();
        foreach (var av in v.Actors)
        {
            foreach (var d in av.Designs)
            {
                if (d.Stage == DesignStage.None)
                    continue;
                var key = (av.Actor, d.Design);
                bool fresh = !open.TryGetValue(key, out long[]? f) || !seen.Contains(key) || (d.Stage == DesignStage.Project && (f[1] >= 0 || f[2] >= 0 || f[4] >= 0));
                if (fresh)
                {
                    if (f is not null)
                        Close(key);
                    f = [t, -1, -1, -1, -1];
                    open[key] = f;
                    info[key] = $"{d.Gen},{string.Join(' ', d.PrcSet)}";
                }
                now.Add(key);
                int si = d.Stage switch { DesignStage.Prototype => 1, DesignStage.Series => 2, DesignStage.InService => 3, _ => 0 };
                if (si > 0 && f![si] < 0)
                    f[si] = t;
                if (d.Cancelled && f![4] < 0)
                    f[4] = t;
            }
            foreach (int num in watch)
            {
                int r = ind.Layout.RowOf(num);
                ProcessYield y = ind.Yield(av.Actor, r, t);
                Fx6 g = y.G[0] * y.G[1] * y.G[2];
                if (y.ActiveMask != 7)
                {
                    g = Fx6.One;
                    for (int dd = 0; dd < 3; dd++)
                        if ((y.ActiveMask & (1 << dd)) != 0)
                            g *= y.G[dd];
                }
                // the series line that uses the process (largest design id in stage С), its rate, unit cost and what binds the rate
                DesignView? line = av.Designs.Where(x => x.Stage == DesignStage.Series && x.PrcSet.Contains(num)).OrderBy(x => x.Design).LastOrDefault();
                string ls = line is null ? ",,," : $"{line.Design},{line.Rate},{line.UnitCost},{(int)line.Bind}";
                yld.Add($"{seed},{t},{av.Actor},{num},{y.Yield},{y.Ramp},{g},{ind.ProcX(av.Actor, r)},{ind.ProcV(av.Actor, r)},{y.XHalfEff},{ls},{av.Quality.TrainingTarget},{y.ActiveMask},{y.MSourceMask},{y.C[0]},{y.C[1]},{y.C[2]},{ind.Layout.Req[r][0]},{ind.Layout.Req[r][1]},{ind.Layout.Req[r][2]},{y.G[0]},{y.G[1]},{y.G[2]}");
            }
        }
        foreach (var key in seen.Where(x => !now.Contains(x) && open.ContainsKey(x)).OrderBy(x => x).ToList())
            Close(key);
        seen = now;
        s.Step(1);
    }
    foreach (var key in open.Keys.OrderBy(x => x).ToList())
        Close(key);
    results[k] = (des, yld);
});
using (var w = new StreamWriter(prefix + "_designs.csv"))
{
    w.WriteLine("seed,actor,design,gen,prc,t_created,t_proto,t_series,t_inservice,t_cancel");
    foreach (var r in results)
        foreach (string line in r.Des)
            w.WriteLine(line);
}
using (var w = new StreamWriter(prefix + "_yield.csv"))
{
    w.WriteLine("seed,t,actor,node,yield,ramp,g,x,v,x_half,line,rate,unit_cost,bind,training_target,active,m_src,c0,c1,c2,req0,req1,req2,g0,g1,g2");
    foreach (var r in results)
        foreach (string line in r.Yld)
            w.WriteLine(line);
}

static class GameDataHolder
{
    public static Singularity.Core.Data.GameData? Data;
}
