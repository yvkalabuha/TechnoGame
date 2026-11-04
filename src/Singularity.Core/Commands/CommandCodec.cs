using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Singularity.Core.Policy;
using Singularity.Kernel;

namespace Singularity.Core.Commands;

/// <summary>
/// The one codec of <c>commands.log</c> and of the command queue in a save (ADR-0013 п. 5, ADR-0014): JSON Lines, one command per
/// line, keys in a fixed order, identifiers as JSON integers, <see cref="Fx6"/> raw values as exact decimal strings (no <c>double</c>).
/// </summary>
public static class CommandCodec
{
    public static string Encode(in LoggedCommand logged)
    {
        var sb = new StringBuilder(128);
        CommandKey k = logged.Key;
        sb.Append("{\"apply_tick\":").Append(I(k.ApplyTick))
          .Append(",\"source\":").Append(I((int)k.Source))
          .Append(",\"source_id\":").Append(I(k.SourceId))
          .Append(",\"local_n\":").Append(I(k.LocalN));
        SliceCommand c = logged.Command;
        sb.Append(",\"kind\":\"").Append(Name(c.Kind)).Append("\",\"actor\":").Append(I(c.Actor));
        switch (c)
        {
            case SetBudgetShares s:
                sb.Append(",\"shares\":[");
                Raw(sb, s.Shares.Research).Append(',');
                Raw(sb, s.Shares.Capability).Append(',');
                Raw(sb, s.Shares.Effort).Append(',');
                Raw(sb, s.Shares.Procurement).Append(',');
                Raw(sb, s.Shares.Quality).Append(']');
                break;
            case SetEffortFocus f:
                sb.Append(",\"processes\":");
                Ints(sb, f.Processes);
                break;
            case ComposeDesign d:
                sb.Append(",\"generation\":").Append(I(d.Generation)).Append(",\"alts\":");
                Ints(sb, d.Alts);
                break;
            case DeriveDesign v:
                sb.Append(",\"parent\":").Append(I(v.Parent)).Append(",\"alts\":");
                Ints(sb, v.Alts);
                break;
            case StartSeries s:
                sb.Append(",\"design\":").Append(I(s.Design)).Append(",\"rate\":");
                Raw(sb, s.Rate).Append(",\"force\":").Append(s.Force ? "true" : "false");
                break;
            case CloseLine l:
                sb.Append(",\"design\":").Append(I(l.Design));
                break;
            case CancelDesign x:
                sb.Append(",\"design\":").Append(I(x.Design));
                break;
            case RetireCohort r:
                sb.Append(",\"design\":").Append(I(r.Design)).Append(",\"n\":").Append(I(r.N));
                break;
            case SellCohort v:
                sb.Append(",\"design\":").Append(I(v.Design)).Append(",\"n\":").Append(I(v.N));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(logged), c.GetType(), "Unknown command type.");
        }
        return sb.Append('}').ToString();
    }

    public static string EncodeLog(IEnumerable<LoggedCommand> commands)
    {
        var sb = new StringBuilder();
        foreach (LoggedCommand c in commands)
            sb.Append(Encode(c)).Append('\n');
        return sb.ToString();
    }

    public static LoggedCommand Decode(string line)
    {
        using JsonDocument doc = JsonDocument.Parse(line);
        JsonElement r = doc.RootElement;
        var key = new CommandKey(r.GetProperty("apply_tick").GetInt64(), (SourceClass)r.GetProperty("source").GetInt32(),
            r.GetProperty("source_id").GetInt32(), r.GetProperty("local_n").GetInt32());
        int actor = r.GetProperty("actor").GetInt32();
        string kind = r.GetProperty("kind").GetString() ?? "";
        SliceCommand command = kind switch
        {
            "set_budget_shares" => ReadShares(actor, r.GetProperty("shares")),
            "set_effort_focus" => new SetEffortFocus(actor, ReadInts(r.GetProperty("processes"))),
            "compose_design" => new ComposeDesign(actor, r.GetProperty("generation").GetInt32(), ReadInts(r.GetProperty("alts"))),
            "derive_design" => new DeriveDesign(actor, r.GetProperty("parent").GetInt32(), ReadInts(r.GetProperty("alts"))),
            "start_series" => new StartSeries(actor, r.GetProperty("design").GetInt32(), ReadRaw(r.GetProperty("rate")), r.GetProperty("force").GetBoolean()),
            "close_line" => new CloseLine(actor, r.GetProperty("design").GetInt32()),
            "cancel_design" => new CancelDesign(actor, r.GetProperty("design").GetInt32()),
            "retire_cohort" => new RetireCohort(actor, r.GetProperty("design").GetInt32(), r.GetProperty("n").GetInt32()),
            "sell_cohort" => new SellCohort(actor, r.GetProperty("design").GetInt32(), r.GetProperty("n").GetInt32()),
            _ => throw new FormatException($"Unknown command kind '{kind}'."),
        };
        return new LoggedCommand(key, command);
    }

    public static List<LoggedCommand> DecodeLog(string text)
    {
        var list = new List<LoggedCommand>();
        foreach (string line in text.Split('\n'))
        {
            if (line.Length > 0)
                list.Add(Decode(line));
        }
        return list;
    }

    private static string Name(CommandKind kind) => kind switch
    {
        CommandKind.SetBudgetShares => "set_budget_shares",
        CommandKind.SetEffortFocus => "set_effort_focus",
        CommandKind.ComposeDesign => "compose_design",
        CommandKind.DeriveDesign => "derive_design",
        CommandKind.StartSeries => "start_series",
        CommandKind.CloseLine => "close_line",
        CommandKind.CancelDesign => "cancel_design",
        CommandKind.RetireCohort => "retire_cohort",
        CommandKind.SellCohort => "sell_cohort",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static string I(long v) => v.ToString(CultureInfo.InvariantCulture);

    private static StringBuilder Raw(StringBuilder sb, Fx6 v) => sb.Append('"').Append(I(v.Raw)).Append('"');

    private static void Ints(StringBuilder sb, ImmutableArray<int> values)
    {
        sb.Append('[');
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0)
                sb.Append(',');
            sb.Append(I(values[i]));
        }
        sb.Append(']');
    }

    private static ImmutableArray<int> ReadInts(JsonElement e)
    {
        var b = ImmutableArray.CreateBuilder<int>();
        foreach (JsonElement x in e.EnumerateArray())
            b.Add(x.GetInt32());
        return b.ToImmutable();
    }

    private static Fx6 ReadRaw(JsonElement e) =>
        Fx6.FromRaw(long.Parse(e.GetString() ?? throw new FormatException("Fx6 value must be a string."), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture));

    private static SetBudgetShares ReadShares(int actor, JsonElement e)
    {
        Fx6[] v = [.. e.EnumerateArray().Select(ReadRaw)];
        if (v.Length != 5)
            throw new FormatException("shares needs 5 values.");
        return new SetBudgetShares(actor, new BudgetShares(v[0], v[1], v[2], v[3], v[4]));
    }
}
