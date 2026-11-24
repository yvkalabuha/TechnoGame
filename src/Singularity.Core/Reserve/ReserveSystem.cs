using Singularity.Kernel;

namespace Singularity.Core.Reserve;

/// <summary>Parameters of the budget reserve (owner decision #475).</summary>
public sealed record ReserveParams
{
    /// <summary>Share of the balance lost in a year (#475, "К": calibrated later, then moved to the mod data). The month is only the call frequency.</summary>
    public Fx6 LeakPerYear { get; init; } = Fx6.FromRaw(100_000);

    /// <summary>
    /// The monthly share that compounds to <see cref="LeakPerYear"/>: the smallest <c>m</c> with <c>(1 − m)^12 ≤ 1 − LeakPerYear</c>, found by integer bisection
    /// (no Math.Pow or Exp in the core; ADR-0002). The one place the yearly rate becomes a monthly factor; 0.1 a year gives 0.008742.
    /// </summary>
    public Fx6 LeakPerMonth
    {
        get
        {
            if (LeakPerYear <= Fx6.Zero)
                return Fx6.Zero;
            Fx6 keep = Fx6.One - LeakPerYear;
            long lo = 0, hi = Fx6.Scale;   // (1 − hi/Scale)^12 = 0 ≤ keep; the answer lies in (lo, hi]
            while (hi - lo > 1)
            {
                long mid = lo + (hi - lo) / 2;
                Fx6 f = Fx6.One - Fx6.FromRaw(mid), p = Fx6.One;
                for (int i = 0; i < 12; i++)
                    p *= f;
                if (p <= keep)
                    hi = mid;
                else
                    lo = mid;
            }
            return Fx6.FromRaw(hi);
        }
    }
}

/// <summary>A broken reserve invariant (PLAYER_LEVERS_WAVE1 §A2, production-lines-and-licence-market §4 I3, I4).</summary>
public sealed class ReserveInvariantException(long month, int actorRow, string message)
    : Exception($"reserve invariant violated at month {month}, actor row {actorRow}: {message}");

/// <summary>What the reserve reads from the other layers in the compute of month t: pure functions of the snapshot t−1.</summary>
public interface IReserveInputs
{
    /// <summary>Money that enters the account in month t: the share left out of the five budget items times the budget.</summary>
    Fx6 Inflow(int actorRow, long month);

    /// <summary>Money that leaves the account in month t (a funded programme); never above the balance of the snapshot.</summary>
    Fx6 Spend(int actorRow, long month);
}

/// <summary>
/// The budget reserve: one <c>res.balance</c> column (Fx6 raw) per actor. Monthly, after the layers that read the same snapshot:
/// <c>R' = (R + inflow − spend)·(1 − leak)</c> (A2 of PLAYER_LEVERS_WAVE1: add, then leak). Part B I3 writes <c>R·(1 − λ/12) + inflow − spend</c>,
/// the leak on the old balance only; the two differ by the leak of one month's flows (under 1 % of them) and A2 is the owner-facing text. The balance never goes below zero (I4). Own column; nothing else writes it.
/// </summary>
public sealed class ReserveSystem : ITickSystem
{
    public const int DefaultSystemId = 500;

    private readonly ReserveParams _p;
    private readonly Fx6 _leak;
    private readonly IReserveInputs _inputs;
    private readonly LongColumn _balance;

    public ReserveSystem(Table table, ReserveParams parameters, IReserveInputs inputs, int systemId = DefaultSystemId)
    {
        if (parameters.LeakPerYear < Fx6.Zero || parameters.LeakPerYear >= Fx6.One)
            throw new ArgumentOutOfRangeException(nameof(parameters), parameters.LeakPerYear, "The yearly leak lies in [0, 1).");
        _p = parameters;
        _leak = parameters.LeakPerMonth;
        _inputs = inputs;
        _balance = table.AddLong("res.balance");
        SystemId = systemId;
    }

    public int SystemId { get; }

    public Cadence Cadence => Cadence.Monthly;

    public ReserveParams Parameters => _p;

    /// <summary>The balance in the snapshot.</summary>
    public Fx6 Balance(int actorRow) => Fx6.FromRaw(_balance.Current[actorRow]);

    /// <summary>What the next monthly step takes from the current balance as the leak (before the new inflow): a pure read for the views.</summary>
    public Fx6 LeakOf(Fx6 balance) => balance * _leak;

    public void Compute(in TickContext context, int chunk, int rowStart, int rowEnd)
    {
        long t = (context.Calendar ?? throw new InvalidOperationException("The reserve is monthly and needs a runner calendar.")).MonthIndex(context.Tick);
        for (int a = rowStart; a < rowEnd; a++)
        {
            Fx6 balance = Balance(a);
            Fx6 spend = _inputs.Spend(a, t);
            if (spend > balance)
                throw new ReserveInvariantException(t, a, $"spend {spend} exceeds the balance {balance} (I3)");
            Fx6 after = balance + _inputs.Inflow(a, t) - spend;
            _balance.Next[a] = (after - after * _leak).Raw;
        }
    }

    public void Merge(in TickContext context, int chunkCount)
    {
    }

    public void Post(in TickContext context)
    {
        long month = context.Calendar!.MonthIndex(context.Tick - 1);
        ReadOnlySpan<long> now = _balance.Current;
        for (int a = 0; a < now.Length; a++)
        {
            if (now[a] < 0)
                throw new ReserveInvariantException(month, a, "the balance is negative (I4)");
        }
    }
}
