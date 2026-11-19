using Singularity.Core.Commands;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session;

/// <summary>
/// The arm <c>FORCE</c> (spec §6.8, contract §4.4c): a player proxy, not the AI. After the step of month t it reads the snapshot and, for every
/// project of every actor that is in stage О with the gate still closed, sends <c>start_series(…, force = true)</c> once, with the rate
/// <c>⌊r_AI/divisor⌋</c> (divisor 1 or 4, the «предсерия»), and closes the actor's other lines like an ordinary launch (the parent of a derived
/// design is moved by the layer). The command goes through <see cref="SliceSession.Submit"/> as <see cref="SourceClass.Harness"/>, so it is in
/// <c>commands.log</c>; it acts from t+1, the delay of the AI's own decisions. Diagnostic: it changes no rule of main. The set of designs already
/// forced is the proxy's own bookkeeping, not world state: it is not hashed or saved, a new proxy on a restored session starts empty.
/// </summary>
public sealed class ForceProxy(SliceSession session, int rateDivisor = 1)
{
    // Keyed by the handle: a project in a reused slot is a new entity and gets its own FORCE (ADR-0005 p. 3).
    private readonly HashSet<(int Actor, DesignHandle Design)> _sent = new();
    private int _local;

    public int Sent { get; private set; }

    /// <summary>Call after the step of <paramref name="month"/>; the commands are applied at the next tick boundary.</summary>
    public void Issue(long month)
    {
        SliceWorld w = session.World;
        ProductsSystem p = w.Products;
        for (int a = 0; a < w.Scenario.ActorCount; a++)
        {
            for (int d = 0; d < w.ProductsLayout.Designs; d++)
            {
                if (p.Stage(a, d) != DesignStage.Prototype || p.GateOpen(a, d, month + 1))
                    continue;
                var handle = new DesignHandle(d, p.Generation(a, d));
                if (!_sent.Add((a, handle)))
                    continue;
                Fx6 rate = PolicyRules.LineRate(w.ProcurementBudget(a), p.UnitCostFloor(a, d)) / Fx6.FromInt(rateDivisor);
                Send(new StartSeries(a, handle, rate, Force: true));
                Sent++;
                for (int e = 0; e < w.ProductsLayout.Designs; e++)
                {
                    if (p.Stage(a, e) == DesignStage.Series && e != p.LiveParent(a, d))
                        Send(new CloseLine(a, new DesignHandle(e, p.Generation(a, e))));
                }
            }
        }
    }

    private void Send(SliceCommand command)
    {
        CommandVerdict v = session.Submit(SourceClass.Harness, 0, _local++, command);
        if (!v.Accepted)
            throw new InvalidOperationException($"the FORCE proxy was refused: {v.Message}");
    }
}
