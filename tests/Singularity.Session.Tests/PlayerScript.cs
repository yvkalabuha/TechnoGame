using Singularity.Core.Commands;
using Singularity.Core.Products;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>
/// A scripted player for the linked runs: through <see cref="SliceSession.Submit"/> only, like any client. Each month, per actor: a
/// project for the newest known APP generation without one (even actors «superior», odd «producible»), the series at the gate with
/// 30 units/month, close the line of an older generation once a newer one is in series and produces. It reads the snapshot through the world
/// internals (a test peeks; a real client would read the published views).
/// </summary>
internal sealed class PlayerScript(SliceSession session)
{
    private readonly int[] _local = new int[16];
    public int Accepted, Refused;

    public void Issue(long month)
    {
        SliceWorld w = session.World;
        ProductsLayout l = w.ProductsLayout;
        for (int a = 0; a < w.Scenario.ActorCount; a++)
        {
            bool Knows(int n) { int r = w.Graph.RowOf(n); return r >= 0 && w.Knowledge.IsKnown(a, r); }
            Fx6 YieldOf(int p) => Knows(p) ? w.Industry.Yield(a, w.IndustryLayout.RowOf(p), month).Yield : Fx6.Zero;

            int g = 0;
            for (int i = 1; i <= l.Generations; i++)
            {
                if (Knows(l.GenApp[i - 1]))
                    g = i;
            }
            bool has = false;
            for (int d = 0; d < l.Designs; d++)
                has |= w.Products.Stage(a, d) is DesignStage.Project or DesignStage.Prototype or DesignStage.Series && w.Products.PlatformGen(a, d) == g;
            if (g > 0 && !has)
            {
                var alts = new int[l.SlotCount];
                bool ok = a % 2 == 0 && DesignRules.Superior(l, g, Knows, alts)
                    || a % 2 != 0 && DesignRules.Producible(l, g, Knows, YieldOf, new ProductsParams().YProd, null, alts)
                    || DesignRules.Superior(l, g, Knows, alts);
                if (ok)
                    Send(a, new ComposeDesign(a, g, [.. alts]));
            }
            for (int d = 0; d < l.Designs; d++)
            {
                // The player may force an early launch (A36). The old line is closed only once the newer one actually produces: a new
                // generation can start with processes not admitted yet, and closing the working line then would stop all output.
                if (w.Products.Stage(a, d) == DesignStage.Prototype)
                    Send(a, new StartSeries(a, d, Fx6.FromInt(30), !w.Products.GateOpen(a, d, month)));
                if (w.Products.Stage(a, d) == DesignStage.Series)
                {
                    for (int e = 0; e < l.Designs; e++)
                    {
                        if (w.Products.Stage(a, e) == DesignStage.Series && w.Products.Gen(a, e) > w.Products.Gen(a, d) && w.Products.Rate(a, e) > Fx6.Zero)
                        {
                            Send(a, new CloseLine(a, d));
                            break;
                        }
                    }
                }
            }
        }
    }

    private void Send(int actor, SliceCommand c)
    {
        CommandVerdict v = session.Submit(SourceClass.Player, 0, _local[0]++, c);
        if (v.Accepted)
            Accepted++;
        else
            Refused++;
    }
}
