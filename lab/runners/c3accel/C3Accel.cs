// TASK-0034: the C3 stand of smoke-t4s (docs/simulation/smoke-t4s.md §2; spec slice-jet-aviation.md §8.2 C3) under
// accelerated knowledge, by two levers at the same nominal factor k:
//   cn — c_n / k (KnowledgeParams.CostByType divided by k; S = S_ref);
//   S  — S_ref · k (the R&D money that reaches the node; c_n unchanged).
// The stand is a copy of SmokeT4s.C3Stand (private there); k = 1 rows must equal the smoke-t4s rows byte for byte
// (checked by the script). Lab observer only: nothing is written into the core; doubles only in the statistics.
using System.Collections.Immutable;
using System.Globalization;
using Singularity.Core;
using Singularity.Core.Data;
using Singularity.Core.Knowledge;
using Singularity.Kernel;

internal static class C3Accel
{
    private const int Horizon = 1200;
    private const ulong SeedBase = 50001;
    private const int ActorNumber = 1;
    private static readonly int[] KeyNodes = [17, 19, 23, 27, 28, 30, 40, 41, 49, 51, 52];
    private static readonly long[] Ks = [1_000_000, 1_500_000, 2_000_000, 3_000_000];
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const long SRef = 100_000;

    public static int Main(string[] args)
    {
        string modDir = args[0], outDir = args[1];
        int seeds = int.Parse(args[2], Inv);
        GameData data = ModLoader.Load(modDir);
        ScenarioData sc = data.Scenario("jet_slice_1945");
        Directory.CreateDirectory(outDir);
        // (label, θ_start, h_max): base = spec §8.1 values, then the smoke-t4s grid at S_ref.
        var points = new List<(string Label, long Theta, long HMax)> { ("base", 600_000, 80_000) };
        foreach (long th in new long[] { 300_000, 600_000, 900_000 })
            foreach (long hm in new long[] { 40_000, 80_000, 160_000 })
                if (th != 600_000 || hm != 80_000)
                    points.Add(("grid", th, hm));
        using var csv = new StreamWriter(Path.Combine(outDir, "c3accel_runs.csv"));
        csv.WriteLine("lever,k,label,node,S,theta_start,h_max,seed,months_to_discovery,months_to_theta");
        using var sum = new StreamWriter(Path.Combine(outDir, "c3accel_summary.csv"));
        sum.WriteLine("lever,k,label,node,type,gen,c_n,S,theta_start,h_max,n,censored,mean_months,cv,p10,p50,p90,months_to_theta");
        Console.WriteLine($"c3accel scenario_hash={sc.Hash} seeds={seeds} (from {SeedBase})");
        foreach (string lever in new[] { "cn", "S" })
        foreach (long k in Ks)
        foreach ((string label, long th, long hm) in points)
        {
            Fx6 kf = Fx6.FromRaw(k);
            var def = new KnowledgeParams();
            var kp = new KnowledgeParams
            {
                ThetaStart = Fx6.FromRaw(th),
                HMax = Fx6.FromRaw(hm),
                CostByType = lever == "cn" ? [.. def.CostByType.Select(c => c / kf)] : def.CostByType,
            };
            Fx6 s = lever == "S" ? Fx6.FromRaw(SRef) * kf : Fx6.FromRaw(SRef);
            KnowledgeGraph g = KnowledgeGraph.FromData(data.Nodes, sc.Disciplines, kp);
            var cvs = new List<double>();
            foreach (int node in KeyNodes)
            {
                var times = new List<double>();
                int censored = 0;
                long toTheta = -1;
                for (int i = 0; i < seeds; i++)
                {
                    var stand = new Stand(g, sc, kp, node, s, SeedBase + (ulong)i);
                    (long t, long th0) = stand.Run(Horizon);
                    toTheta = th0;
                    csv.WriteLine(string.Join(',', lever, kf, label, node, s, Fx6.FromRaw(th), Fx6.FromRaw(hm), SeedBase + (ulong)i, t, th0));
                    if (t < 0) censored++;
                    else times.Add(t);
                }
                (double mean, double cv, double p10, double p50, double p90) = Stats(times);
                cvs.Add(cv);
                int row = g.RowOf(node);
                sum.WriteLine(string.Join(',', lever, kf, label, node, g.Type[row], g.Gen[row], g.CostN[row], s, Fx6.FromRaw(th), Fx6.FromRaw(hm),
                    times.Count, censored, D(mean), D(cv), D(p10), D(p50), D(p90), toTheta));
            }
            cvs.Sort();
            Console.WriteLine($"{lever} k={kf} {label} θ={Fx6.FromRaw(th)} h={Fx6.FromRaw(hm)} | CV min {D(cvs[0])} med {D(cvs[cvs.Count / 2])} max {D(cvs[^1])} in[0.3,0.5] {cvs.Count(c => c >= 0.3 && c <= 0.5)}");
        }
        return 0;
    }

    private static (double, double, double, double, double) Stats(List<double> xs)
    {
        double mean = xs.Average();
        double var = xs.Sum(x => (x - mean) * (x - mean)) / Math.Max(1, xs.Count - 1);
        double[] s = [.. xs.Order()];
        double Q(double q) => s[(int)Math.Floor(q * (s.Length - 1))];
        return (mean, Math.Sqrt(var) / mean, Q(0.1), Q(0.5), Q(0.9));
    }

    private static string D(double x) => double.IsNaN(x) ? "" : x.ToString("0.####", Inv);

    /// <summary>Copy of SmokeT4s.C3Stand: one actor (number 1, GBR's K_d), K_ind = 0, requires-closure known, all S on the node,
    /// j ≡ 1, V_d = 0, known_elsewhere = 0.</summary>
    private sealed class Stand : IKnowledgeInputs
    {
        public ReadOnlySpan<int> LicensedEngines(int actorRow, long month) => ReadOnlySpan<int>.Empty;
        private readonly int _target;
        private readonly Fx6 _s;
        private readonly KnowledgeSystem _k;
        private readonly TickRunner _runner;
        private readonly TickCalendar _cal;

        public Stand(KnowledgeGraph g, ScenarioData sc, KnowledgeParams kp, int node, Fx6 s, ulong seed)
        {
            _target = g.RowOf(node);
            _s = s;
            var start = new KnowledgeStart([ActorNumber], g.Count, g.DisciplineNames.Length);
            foreach (int r in Closure(g, _target))
                start.Known[r] = true;
            start.Known[_target] = false;
            for (int d = 0; d < g.DisciplineNames.Length; d++)
                start.KD[d] = sc.KD[ActorNumber * sc.Disciplines.Length + d];
            var table = new Table(1);
            _cal = new TickCalendar(new CivilDate(sc.StartYear, sc.StartMonth, 1));
            var rng = new CounterRng(seed);
            _k = new KnowledgeSystem(table, g, start, kp, this, rng, 1);
            _runner = new TickRunner(table, seed, [_k], 1, 1, _cal);
        }

        public (long Months, long ToTheta) Run(int horizon)
        {
            long toTheta = -1;
            for (long t = 0; t < horizon; t++)
            {
                long last = _cal.TickOfMonth(t);
                while (_runner.Tick <= last)
                    _runner.RunTick();
                if (toTheta < 0 && _k.LastStep(0, _target).HazardActive)
                    toTheta = t + 1;
                if (_k.IsKnown(0, _target))
                    return (t + 1, toTheta);
            }
            return (-1, toTheta);
        }

        public Fx6 ResearchBudget(int actorRow, long month) => _s;
        public Fx6 Priority(int actorRow, int nodeRow, long month) => nodeRow == _target ? Fx6.One : Fx6.Zero;
        public Fx6 J(int actorRow, int nodeRow, long month) => Fx6.One;
        public Fx6 DisciplineVolume(int actorRow, int discipline, long month) => Fx6.Zero;
    }

    private static SortedSet<int> Closure(KnowledgeGraph g, int row)
    {
        var seen = new SortedSet<int>();
        var stack = new Stack<int>();
        stack.Push(row);
        while (stack.Count > 0)
        {
            int r = stack.Pop();
            if (!seen.Add(r))
                continue;
            RequirementLists req = g.Requires;
            for (int k = req.GroupStart[r]; k < req.GroupStart[r + 1]; k++)
                stack.Push(req.GroupMembers(k)[0]);
        }
        return seen;
    }
}
