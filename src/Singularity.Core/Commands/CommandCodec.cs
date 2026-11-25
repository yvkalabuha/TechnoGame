using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Singularity.Core.Intelligence;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Core.Commands;

/// <summary>
/// The one codec of <c>commands.log</c> and of the command queue in a save (ADR-0013 п. 5, ADR-0014): JSON Lines, one command per
/// line, keys in a fixed order, identifiers as JSON integers, <see cref="Fx6"/> raw values as exact decimal strings (no <c>double</c>).
/// A design handle is two keys: <c>design</c> + <c>design_gen</c> (<c>parent</c> + <c>parent_gen</c> for a derive). The log starts with a header line
/// <c>{"commands_log":2}</c>; a log without it is version 1 (bare design indices) and is rejected, never migrated (ADR-0005 p. 3, ORCH_DECISIONS O-003).
/// </summary>
public static class CommandCodec
{
    /// <summary>Version of <c>commands.log</c> written and read by this build.</summary>
    public const int LogVersion = 2;

    private static readonly string Header = "{\"commands_log\":" + LogVersion.ToString(CultureInfo.InvariantCulture) + "}";

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
                // Optional keys: a run without intelligence writes exactly the old line.
                if (s.Shares.Security != Fx6.Zero)
                {
                    sb.Append(",\"security\":");
                    Raw(sb, s.Shares.Security);
                }
                if (!s.Targets.IsDefault)
                {
                    sb.Append(",\"targets\":[");
                    for (int i = 0; i < s.Targets.Length; i++)
                        sb.Append(i > 0 ? "," : "").Append('[').Append(I(s.Targets[i].Target)).Append(',').Append(I((int)s.Targets[i].Mode)).Append(']');
                    sb.Append(']');
                }
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
                sb.Append(",\"parent\":").Append(I(v.Parent.Slot)).Append(",\"parent_gen\":").Append(I(v.Parent.Generation)).Append(",\"alts\":");
                Ints(sb, v.Alts);
                break;
            case StartSeries s:
                Handle(sb, s.Design).Append(",\"rate\":");
                Raw(sb, s.Rate).Append(",\"force\":").Append(s.Force ? "true" : "false");
                break;
            case CloseLine l:
                Handle(sb, l.Design);
                break;
            case CancelDesign x:
                Handle(sb, x.Design);
                break;
            case RetireCohort r:
                Handle(sb, r.Design).Append(",\"n\":").Append(I(r.N));
                if (r.Acknowledged)
                    sb.Append(",\"ack\":true");
                break;
            case SellCohort v:
                Handle(sb, v.Design).Append(",\"n\":").Append(I(v.N));
                break;
            case StartMasteryProgramme p:
                sb.Append(",\"process\":").Append(I(p.ProcessNumber)).Append(",\"abroad\":").Append(p.Abroad ? "true" : "false");
                break;
            case CancelMasteryProgramme q:
                sb.Append(",\"process\":").Append(I(q.ProcessNumber));
                break;
            case RefuseProposal rp:
                Handle(sb, rp.Design).Append(",\"close\":").Append(rp.Close ? "true" : "false");
                break;
            case BuyLicence b:
                sb.Append(",\"seller\":").Append(I(b.Seller)).Append(",\"gen\":").Append(I(b.Gen));
                break;
            case OfferLicence o:
                sb.Append(",\"gen\":").Append(I(o.Gen)).Append(",\"m\":");
                Raw(sb, o.M).Append(",\"circle\":").Append(I((int)o.Circle));
                break;
            case FundProgramme fp:
                sb.Append(",\"node\":").Append(I(fp.Node)).Append(",\"amount\":");
                Raw(sb, fp.Amount);
                break;
            case SetPriorityNode pn:
                sb.Append(",\"slot\":").Append(I(pn.Slot)).Append(",\"node\":").Append(I(pn.Node));
                break;
            case BuildInstitute bi:
                sb.Append(",\"slot\":").Append(I(bi.Slot));
                break;
            case AssignNode an:
                sb.Append(",\"slot\":").Append(I(an.Slot)).Append(",\"node\":").Append(I(an.Node));
                if (an.Acknowledged)
                    sb.Append(",\"ack\":true");
                break;
            case CancelBuild cb:
                sb.Append(",\"slot\":").Append(I(cb.Slot));
                break;
            case ApplyQueues aq:
                sb.Append(",\"plan\":[");
                for (int i = 0; i < aq.Plan.Length; i++)
                {
                    if (i > 0)
                        sb.Append(',');
                    sb.Append("{\"slot\":").Append(I(aq.Plan[i].Slot)).Append(",\"nodes\":");
                    Ints(sb, aq.Plan[i].Nodes);
                    sb.Append('}');
                }
                sb.Append(']');
                break;
            case Unassign un:
                sb.Append(",\"slot\":").Append(I(un.Slot)).Append(",\"node\":").Append(I(un.Node));
                break;
            case ReorderQueue rq:
                sb.Append(",\"slot\":").Append(I(rq.Slot)).Append(",\"order\":");
                Ints(sb, rq.Order);
                break;
            case SetFocus sf:
                sb.Append(",\"slot\":").Append(I(sf.Slot)).Append(",\"level\":").Append(I(sf.Level)).Append(",\"reserve\":").Append(sf.FromReserve ? "true" : "false");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(logged), c.GetType(), "Unknown command type.");
        }
        return sb.Append('}').ToString();
    }

    public static string EncodeLog(IEnumerable<LoggedCommand> commands)
    {
        var sb = new StringBuilder(Header).Append('\n');
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
            "set_budget_shares" => ReadShares(actor, r),
            "set_effort_focus" => new SetEffortFocus(actor, ReadInts(r.GetProperty("processes"))),
            "compose_design" => new ComposeDesign(actor, r.GetProperty("generation").GetInt32(), ReadInts(r.GetProperty("alts"))),
            "derive_design" => new DeriveDesign(actor, ReadHandle(r, "parent"), ReadInts(r.GetProperty("alts"))),
            "start_series" => new StartSeries(actor, ReadHandle(r, "design"), ReadRaw(r.GetProperty("rate")), r.GetProperty("force").GetBoolean()),
            "close_line" => new CloseLine(actor, ReadHandle(r, "design")),
            "cancel_design" => new CancelDesign(actor, ReadHandle(r, "design")),
            "retire_cohort" => new RetireCohort(actor, ReadHandle(r, "design"), r.GetProperty("n").GetInt32(), r.TryGetProperty("ack", out JsonElement ack) && ack.GetBoolean()),
            "sell_cohort" => new SellCohort(actor, ReadHandle(r, "design"), r.GetProperty("n").GetInt32()),
            "start_mastery_programme" => new StartMasteryProgramme(actor, r.GetProperty("process").GetInt32(), r.GetProperty("abroad").GetBoolean()),
            "cancel_mastery_programme" => new CancelMasteryProgramme(actor, r.GetProperty("process").GetInt32()),
            "refuse_proposal" => new RefuseProposal(actor, ReadHandle(r, "design"), r.GetProperty("close").GetBoolean()),
            "buy_licence" => new BuyLicence(actor, r.GetProperty("seller").GetInt32(), r.GetProperty("gen").GetInt32()),
            "offer_licence" => new OfferLicence(actor, r.GetProperty("gen").GetInt32(), ReadRaw(r.GetProperty("m")), (SaleCircle)r.GetProperty("circle").GetInt32()),
            "fund_programme" => new FundProgramme(actor, r.GetProperty("node").GetInt32(), ReadRaw(r.GetProperty("amount"))),
            "set_priority_node" => new SetPriorityNode(actor, r.GetProperty("slot").GetInt32(), r.GetProperty("node").GetInt32()),
            "build_institute" => new BuildInstitute(actor, r.GetProperty("slot").GetInt32()),
            "assign_node" => new AssignNode(actor, r.GetProperty("slot").GetInt32(), r.GetProperty("node").GetInt32(), r.TryGetProperty("ack", out JsonElement ackA) && ackA.GetBoolean()),
            "cancel_build" => new CancelBuild(actor, r.GetProperty("slot").GetInt32()),
            "apply_queues" => new ApplyQueues(actor, [.. r.GetProperty("plan").EnumerateArray().Select(p => new QueuePlan(p.GetProperty("slot").GetInt32(), ReadInts(p.GetProperty("nodes"))))]),
            "unassign" => new Unassign(actor, r.GetProperty("slot").GetInt32(), r.GetProperty("node").GetInt32()),
            "reorder_queue" => new ReorderQueue(actor, r.GetProperty("slot").GetInt32(), ReadInts(r.GetProperty("order"))),
            "set_focus" => new SetFocus(actor, r.GetProperty("slot").GetInt32(), r.GetProperty("level").GetInt32(), r.GetProperty("reserve").GetBoolean()),
            _ => throw new FormatException($"Unknown command kind '{kind}'."),
        };
        return new LoggedCommand(key, command);
    }

    public static List<LoggedCommand> DecodeLog(string text)
    {
        var list = new List<LoggedCommand>();
        bool header = false;
        foreach (string line in text.Split('\n'))
        {
            if (!header)
            {
                // The header is the first line, not the first non-empty one.
                if (line.Length > 0)
                    CheckHeader(line);
                else
                    break;
                header = true;
                continue;
            }
            if (line.Length > 0)
                list.Add(Decode(line));
        }
        if (!header)
            throw new FormatException("commands.log has no header line {\"commands_log\":2}: it is version 1 (no design handles, ADR-0005 p. 3); re-record the run");
        return list;
    }

    private static void CheckHeader(string line)
    {
        using JsonDocument doc = JsonDocument.Parse(line);
        JsonElement r = doc.RootElement;
        if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("commands_log", out JsonElement v))
            throw new FormatException("commands.log v1 has no design handles (ADR-0005 p. 3); re-record the run");
        int version = v.GetInt32();
        if (version != LogVersion)
            throw new FormatException($"commands.log version {version} is not supported (this build reads {LogVersion})");
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
        CommandKind.StartMasteryProgramme => "start_mastery_programme",
        CommandKind.CancelMasteryProgramme => "cancel_mastery_programme",
        CommandKind.RefuseProposal => "refuse_proposal",
        CommandKind.BuyLicence => "buy_licence",
        CommandKind.OfferLicence => "offer_licence",
        CommandKind.FundProgramme => "fund_programme",
        CommandKind.SetPriorityNode => "set_priority_node",
        CommandKind.BuildInstitute => "build_institute",
        CommandKind.AssignNode => "assign_node",
        CommandKind.CancelBuild => "cancel_build",
        CommandKind.ApplyQueues => "apply_queues",
        CommandKind.Unassign => "unassign",
        CommandKind.ReorderQueue => "reorder_queue",
        CommandKind.SetFocus => "set_focus",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    // The handle's second key is mandatory: a missing one is a format error, never defaulted (a defaulted generation would make the check always pass).
    private static DesignHandle ReadHandle(JsonElement r, string key) =>
        new(r.GetProperty(key).GetInt32(), r.TryGetProperty(key + "_gen", out JsonElement g) ? g.GetInt32() : throw new FormatException($"'{key}' needs '{key}_gen' (design handle, ADR-0005 p. 3)"));

    private static StringBuilder Handle(StringBuilder sb, DesignHandle h) =>
        sb.Append(",\"design\":").Append(I(h.Slot)).Append(",\"design_gen\":").Append(I(h.Generation));

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

    private static SetBudgetShares ReadShares(int actor, JsonElement r)
    {
        Fx6[] v = [.. r.GetProperty("shares").EnumerateArray().Select(ReadRaw)];
        if (v.Length != 5)
            throw new FormatException("shares needs 5 values.");
        Fx6 security = r.TryGetProperty("security", out JsonElement sec) ? ReadRaw(sec) : Fx6.Zero;
        ImmutableArray<IntelTarget> targets = default;
        if (r.TryGetProperty("targets", out JsonElement tg))
        {
            var b = ImmutableArray.CreateBuilder<IntelTarget>();
            foreach (JsonElement pair in tg.EnumerateArray())
                b.Add(new IntelTarget(pair[0].GetInt32(), (IntelMode)pair[1].GetInt32()));
            targets = b.ToImmutable();
        }
        return new SetBudgetShares(actor, new BudgetShares(v[0], v[1], v[2], v[3], v[4], security), targets);
    }
}
