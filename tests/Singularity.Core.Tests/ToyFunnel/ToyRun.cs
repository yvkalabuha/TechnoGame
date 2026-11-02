using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Singularity.Core.Science.ToyFunnel;
using Singularity.Core.Simulation;
using Singularity.Kernel;

namespace Singularity.Core.Tests.ToyFunnel;

/// <summary>Builds EXP-0001 parameter sets and runs the model on the Kernel TickRunner, like the Lab does.</summary>
internal static class ToyRun
{
    public const long Ticks = 492;

    /// <summary>Per-nu calibration of control B from the draft manifest lab/manifests/EXP-0001.json.</summary>
    public static (string CTau0, string DeltaTau) CalibratedB(double nu) =>
        nu == 1.0 ? ("0.4562846855958923", "0.10798051790334284") : ("0.45605548915918925", "0.1080050200689584");

    // Archetypes and levels as in the draft manifest (HYP-0001 rev 2), canonical JSON.
    public const string ArchetypesJson =
        """[{"E":"l","Nl":"m","Pl":"m","S":"h","edu":["h","l"],"id":"A1","iota":0.6},""" +
        """{"E":"m","Nl":"m","Pl":"h","S":"l","edu":["m","m"],"id":"A2","iota":0.6},""" +
        """{"E":"h","Nl":"l","Pl":"m","S":"m","edu":["l","h"],"id":"A3","iota":0.6},""" +
        """{"E":"m","Nl":"h","Pl":"m","S":"l","edu":["l","m"],"id":"A4","iota":0.6},""" +
        """{"E":"m","Nl":"m","Pl":"m","S":"m","edu":["m","m"],"id":"A5","iota":0.6},""" +
        """{"E":"l","Nl":"m","Pl":"l","S":"l","edu":["sched","sched"],"id":"A6","iota":0.96}]""";

    public const string LevelsJson = """{"h":1.6,"l":0.6,"m":1}""";

    /// <summary>Manifest "params" of EXP-0001 (number texts as canonical decimals) plus model/nu/countries.</summary>
    public static List<KeyValuePair<string, ParamValue>> Values(
        string model, double nu, double beta = 0.625, double gamma = 1.0,
        string cTau0 = "1", string deltaTau = "0.25", int countries = 6)
    {
        var list = new List<KeyValuePair<string, ParamValue>>();
        void Num(string key, string text) => list.Add(new(key, new ParamValue(ParamKind.Number, text)));
        Num("B_c_tau0", cTau0);
        Num("B_delta_tau", deltaTau);
        Num("C_lag_months", "120");
        Num("C_psi", "0.45");
        Num("beta", R(beta));
        Num("countries", countries.ToString(CultureInfo.InvariantCulture));
        Num("delta_A", "0.02");
        Num("delta_K", "0.25");
        Num("delta_P", "0.05");
        Num("dt", "0.08333333333333333");
        Num("eta", "4");
        Num("g0", "0.04");
        Num("gamma", R(gamma));
        Num("kappa", "0.05");
        Num("milestone_count", "24");
        Num("milestone_q0", "100");
        Num("mu", "0.03");
        Num("nu", R(nu));
        Num("p0", "0.006");
        Num("rho0", "0.12");
        Num("s_E0", "0.02");
        Num("s_P0", "0.12");
        Num("s_R0", "0.02");
        Num("setup_mult_lo", "0.85");
        Num("setup_mult_span", "0.3");
        Num("sigma0", "0.1");
        Num("start_year", "1910");
        Num("t_ref", "100");
        Num("theta", "0.25");
        Num("w", "0.5");
        Num("z", "1");
        list.Add(new("archetypes", new ParamValue(ParamKind.Json, ArchetypesJson)));
        list.Add(new("edu_sched", new ParamValue(ParamKind.Json, "[0.6,1.6,360]")));
        list.Add(new("levels", new ParamValue(ParamKind.Json, LevelsJson)));
        list.Add(new("model", new ParamValue(ParamKind.String, model)));
        return list;
    }

    public static ParamSet Params(string model, double nu, double beta = 0.625, double gamma = 1.0,
        string cTau0 = "1", string deltaTau = "0.25", int countries = 6) =>
        new(Values(model, nu, beta, gamma, cTau0, deltaTau, countries));

    /// <summary>Params for main-series runs: control B gets its calibrated values.</summary>
    public static ParamSet MainSeries(string model, double nu, int countries = 6)
    {
        if (model != "B")
            return Params(model, nu, countries: countries);
        (string c, string d) = CalibratedB(nu);
        return Params(model, nu, cTau0: c, deltaTau: d, countries: countries);
    }

    private static string R(double x) => x.ToString("R", CultureInfo.InvariantCulture);

    public sealed class Sink : IMetricSink
    {
        public List<string[]>[] Tables { get; } = [new(), new(), new()];

        public void Row(int table, params string[] cells) => Tables[table].Add(cells);
    }

    public sealed record Result(ToyFunnelModel Model, Sink Metrics, List<string> TickHashes, long FinalTick)
    {
        public string GoldenHash => StateHash.ComputeHex(Model.Table, Model.SchemaVersion, FinalTick);
    }

    public static Result Run(ParamSet p, ulong seed, int threads = 1, int chunkSize = 64, long ticks = Ticks,
        bool hashEveryTick = false, Action<ToyFunnelModel, long>? afterTick = null)
    {
        var model = (ToyFunnelModel)new ToyFunnelModelFactory().Create(new ModelSetup(p, seed, chunkSize, ticks));
        var runner = new TickRunner(model.Table, seed, model.Systems, chunkSize, threads);
        var sink = new Sink();
        var hashes = new List<string>();
        model.Observe(0, sink);
        for (long t = 0; t < ticks; t++)
        {
            runner.RunTick();
            model.Observe(runner.Tick, sink);
            if (hashEveryTick)
                hashes.Add(StateHash.ComputeHex(model.Table, model.SchemaVersion, runner.Tick));
            afterTick?.Invoke(model, runner.Tick);
        }
        return new Result(model, sink, hashes, runner.Tick);
    }

    /// <summary>Digests in the format of Reference/toy_funnel_refs.py (raw integers, double bits).</summary>
    public static (string Yearly, string Mults, string Milestones) Digests(Result r, int countries, int milestoneCount = 24)
    {
        var yearly = new StringBuilder();
        List<string[]> rows = r.Metrics.Tables[ToyFunnelModel.YearlyTable];
        for (int k = 0; k < rows.Count; k++)
        {
            string[] c = rows[k];
            yearly.Append(c[0]).Append(',').Append(k % countries);
            for (int col = 2; col <= 9; col++)
                yearly.Append(',').Append(Raw(c[col]));
            yearly.Append(',').Append(c[10]).Append(',').Append(c[11]).Append(',').Append(Bits(c[12])).Append('\n');
        }

        var mults = new StringBuilder();
        List<string[]> countryRows = r.Metrics.Tables[ToyFunnelModel.CountriesTable];
        for (int i = 0; i < countryRows.Count; i++)
        {
            mults.Append(i);
            for (int col = 1; col <= 4; col++)
                mults.Append(',').Append(Bits(countryRows[i][col]));
            mults.Append('\n');
        }

        var ms = new StringBuilder();
        List<string[]> msRows = r.Metrics.Tables[ToyFunnelModel.MilestonesTable];
        for (int k = 0; k < msRows.Count; k++)
        {
            string[] c = msRows[k];
            ms.Append(k / milestoneCount).Append(',').Append(c[1]).Append(',').Append(Raw(c[2]))
              .Append(',').Append(c[3]).Append(',').Append(c[4]).Append(',').Append(c[5]).Append('\n');
        }
        return (Sha(yearly), Sha(mults), Sha(ms));
    }

    public static long Raw(string fx6Text) => ParamSet.ParseFx6Raw(fx6Text, "csv");

    private static long Bits(string realText) =>
        BitConverter.DoubleToInt64Bits(double.Parse(realText, NumberStyles.Float, CultureInfo.InvariantCulture));

    private static string Sha(StringBuilder sb) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
}
