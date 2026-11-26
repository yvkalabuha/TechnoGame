using System.Collections;
using System.Globalization;
using System.Text;
using Singularity.Core.Commands;
using Singularity.Core.Policy;
using Singularity.Kernel;
using Singularity.Lab.Bench;
using Singularity.Session;

namespace Singularity.Lab.Tests;

/// <summary>TASK-0079 (F4-A): <c>ViewBuilder.Inputs</c> runs per actor in parallel; the view and the hash must not depend on threads or chunk size.</summary>
public sealed class ParallelViewInputsTests : IDisposable
{
    private const int Months = 6;
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "sing-pview-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch (IOException) { }
    }

    // Deterministic text of a view: records by property name, arrays in order, Fx6 as raw.
    private static void Write(StringBuilder sb, object? o)
    {
        switch (o)
        {
            case null: sb.Append("null"); break;
            case Fx6 f: sb.Append(f.Raw.ToString(CultureInfo.InvariantCulture)); break;
            case string s: sb.Append('"').Append(s).Append('"'); break;
            case IEnumerable e:
                sb.Append('[');
                foreach (object? item in e)
                {
                    Write(sb, item);
                    sb.Append(',');
                }
                sb.Append(']');
                break;
            case IConvertible c: sb.Append(c.ToString(CultureInfo.InvariantCulture)); break;
            default:
                sb.Append(o.GetType().Name).Append('{');
                foreach (var p in o.GetType().GetProperties().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    sb.Append(p.Name).Append('=');
                    Write(sb, p.GetValue(o));
                    sb.Append(';');
                }
                sb.Append('}');
                break;
        }
    }

    // Runs the months with commands in several of them; returns the dump of the view of every month and the final hash.
    private static (string Views, string Hash) Run(SynthWorld w, int threads, int chunk)
    {
        var s = SliceSession.Create(w.Data, SliceRunConfig.Legacy with { Seed = 3, ScenarioId = w.ScenarioId, Threads = threads, ChunkSize = chunk });
        var sb = new StringBuilder();
        int local = 0;
        for (int m = 0; m < Months; m++)
        {
            if (m == 1)
                Assert.True(s.Submit(SourceClass.Player, 0, local++, new SetEffortFocus(0, [47])).Accepted);
            if (m == 2)
                Assert.True(s.Submit(SourceClass.Player, 0, local++, new SetBudgetShares(3, new BudgetShares(
                    Fx6.FromRaw(200_000), Fx6.FromRaw(300_000), Fx6.FromRaw(300_000), Fx6.FromRaw(200_000), Fx6.Zero))).Accepted);
            if (m == 4)
                Assert.True(s.Submit(SourceClass.Player, 0, local++, new SetEffortFocus(5, [47])).Accepted);
            s.StepThroughMonth(m);
            Write(sb, s.View);
            sb.Append('\n');
        }
        return (sb.ToString(), s.LastHash);
    }

    [Fact]
    public void ParallelView_EqualsSequentialView_AcrossThreadsAndChunkSizes()
    {
        var spec = new SynthWorldSpec(24, 250, SynthGraph.CrossEdges);
        SynthWorld w = SynthWorldGenerator.Build(spec, Path.Combine(_tmp, spec.Name));
        (string views, string hash) = Run(w, 1, 8);
        Assert.Contains("Inputs=MonthInputsView", views);
        foreach (int t in (int[])[1, 4, 8, 16])
        {
            foreach (int c in (int[])[1, 8, 32])
            {
                (string v, string h) = Run(w, t, c);
                HashAssert.Equal(hash, h, $"threads {t}, ChunkSize {c}");
                Assert.True(views == v, $"view differs at threads {t}, ChunkSize {c}");
            }
        }
    }
}
