using Singularity.Kernel;

namespace Singularity.Core.Products;

/// <summary>
/// The fleet state of one actor that the commands <c>retire_cohort</c> and <c>sell_cohort</c> change (contract r6 §4.4f), with the rules of
/// the validator and the execution. One implementation: the product layer runs the commands of a month on a book built from its columns,
/// and the AI runs the same commands on a book built from the snapshot to see the state in which its own commands will be executed
/// (the projection of §4.4f «Сторона ИИ»). No RNG, no hidden state; every <c>·</c> and <c>/</c> is an <see cref="Fx6"/> floor.
/// </summary>
public sealed class FleetBook
{
    private readonly ProductsParams _p;
    private readonly int[] _m0;
    private readonly int _years;

    public FleetBook(ProductsParams parameters, ReadOnlySpan<int> midyears, int designs)
    {
        _p = parameters;
        _m0 = midyears.ToArray();
        _years = _m0.Length;
        Designs = designs;
        Counts = new Fx6[designs][];
        for (int d = 0; d < designs; d++)
            Counts[d] = new Fx6[_years];
        Stage = new DesignStage[designs];
        Gen = new int[designs];
        Perf = new Fx6[designs];
        Upkeep = new Fx6[designs];
        UnitCostFloor = new Fx6[designs];
        Bind = new LineBind[designs];
        Excess = new bool[designs];
        InDefK = new bool[designs];
        Retired = new Fx6[designs];
        Sold = new Fx6[designs];
        QuotaBase = new int[designs];
        SoldY = new int[designs];
        SellSlot = new bool[designs];
        RetireSlot = new bool[designs];
    }

    public int Designs { get; }
    public ProductsParams Parameters => _p;

    /// <summary>Cohort counts by calendar-year index, per design.</summary>
    public Fx6[][] Counts { get; }
    public DesignStage[] Stage { get; }
    public int[] Gen { get; }
    public Fx6[] Perf { get; }
    public Fx6[] Upkeep { get; }
    public Fx6[] UnitCostFloor { get; }
    public LineBind[] Bind { get; }

    /// <summary>The design has hidden defects above its gate level (<c>X &gt; 0</c>): it may have accidents the AI cannot foresee.</summary>
    public bool[] Excess { get; }

    /// <summary>The design is in <c>Def(K)</c> of the snapshot.</summary>
    public bool[] InDefK { get; }

    public Fx6[] Retired { get; }
    public Fx6[] Sold { get; }
    public int[] QuotaBase { get; }
    public int[] SoldY { get; }
    public Fx6 SaleY { get; set; }
    public Fx6 SaleCap { get; set; }

    /// <summary>Revenue of the sales executed on this book since <see cref="NewMonth"/>.</summary>
    public Fx6 SaleIncome { get; private set; }

    public bool[] SellSlot { get; }
    public bool[] RetireSlot { get; }

    /// <summary>Cohort years are written off at <c>age ≥ service_life</c> (step 0): the cohorts of age at <paramref name="month"/> at the limit go.</summary>
    public void AgeOut(long month)
    {
        for (int y = 0; y < _years; y++)
        {
            if (month - _m0[y] < _p.ServiceLife)
                continue;
            for (int d = 0; d < Designs; d++)
                Counts[d][y] = Fx6.Zero;
        }
    }

    /// <summary>The start of a policy year (step 2a): the quota base from the cohorts as they are, the counters at 0, the ceiling from the base budget.</summary>
    public void ResetYear(Fx6 baseBudget)
    {
        for (int d = 0; d < Designs; d++)
        {
            QuotaBase[d] = checked((int)FleetRules.Whole(Counts[d]));
            SoldY[d] = 0;
        }
        SaleCap = FleetRules.SaleCap(_p.SaleChi, baseBudget);
        SaleY = Fx6.Zero;
    }

    /// <summary>A new month of execution: the slots are free and the revenue of the month starts at 0 (step 2a).</summary>
    public void NewMonth()
    {
        Array.Clear(SellSlot);
        Array.Clear(RetireSlot);
        SaleIncome = Fx6.Zero;
    }

    public long Whole(int design) => FleetRules.Whole(Counts[design]);

    /// <summary><c>sell_cohort</c> by the validator of the contract; null on success, else the rejection. State changes only on success.</summary>
    /// <param name="month">The month of execution (the age of the cohorts and the price).</param>
    /// <param name="gFront"><c>G_front</c> of the snapshot the executing step reads.</param>
    public CommandRejection? Sell(int design, long n, long month, int gFront) => Sell(design, n, month, gFront, out _);

    public CommandRejection? Sell(int design, long n, long month, int gFront, out Fx6 income)
    {
        income = Fx6.Zero;
        bool own = design >= 0 && design < Designs;
        if (own && SellSlot[design])
            return CommandRejection.BadSellSlot;
        if (!own || Stage[design] != DesignStage.InService)
            return CommandRejection.BadSellStage;
        if (n < 1 || n > Whole(design))
            return CommandRejection.BadSellCount;
        SaleQuote q = FleetRules.Quote(Counts[design], n, month, _m0, UnitCostFloor[design], gFront - Gen[design], _p, QuotaBase[design],
            SoldY[design], SaleY, SaleCap);
        if (q.Reject != SaleReject.None)
        {
            return q.Reject switch
            {
                SaleReject.NotSurplus => CommandRejection.BadSellNotSurplus,
                SaleReject.Quota => CommandRejection.BadSellQuota,
                _ => CommandRejection.BadSellRevenueCap,
            };
        }
        Fx6 dust = TakeUnits(design, n);
        Sold[design] += Fx6.FromInt(n) + dust;
        SoldY[design] = checked((int)(SoldY[design] + n));
        SaleY += q.Income;
        SaleIncome += q.Income;
        SellSlot[design] = true;
        income = q.Income;
        return null;
    }

    /// <summary><c>retire_cohort</c> by the validator of the contract; null on success.</summary>
    public CommandRejection? Retire(int design, long n)
    {
        bool own = design >= 0 && design < Designs;
        if (own && RetireSlot[design])
            return CommandRejection.BadRetireSlot;
        if (!own || Stage[design] is not (DesignStage.Series or DesignStage.InService))
            return CommandRejection.BadRetireStage;
        if (n < 1 || n > Whole(design))
            return CommandRejection.BadRetireCount;
        Fx6 dust = TakeUnits(design, n);
        Retired[design] += Fx6.FromInt(n) + dust;
        RetireSlot[design] = true;
        return null;
    }

    // Oldest first, then the dust rule (contract r6 §4.4f).
    private Fx6 TakeUnits(int design, long n)
    {
        Span<Fx6> taken = stackalloc Fx6[_years];
        FleetRules.Take(Counts[design], Fx6.FromInt(n), taken);
        return FleetRules.SweepDust(Counts[design]);
    }

    /// <summary><c>⌊Σ count_i⌋</c> over the cohorts of age ≥ <paramref name="minAge"/> at <paramref name="month"/> (and below the service life): <c>avail_d</c> of spec A42.4.</summary>
    public long Aged(int design, long month, int minAge)
    {
        Fx6 sum = Fx6.Zero;
        for (int y = 0; y < _years; y++)
        {
            long age = month - _m0[y];
            if (age >= minAge && age < _p.ServiceLife)
                sum += Counts[design][y];
        }
        return sum.Raw / Fx6.Scale;
    }

    /// <summary><c>SaleMax(d, n)</c> on this book (spec A42.4 п. 5).</summary>
    public long SaleMax(int design, long n, long month, int gFront)
    {
        if (Stage[design] != DesignStage.InService || SellSlot[design])
            return 0;
        return FleetRules.SaleMax(Counts[design], n, month, _m0, UnitCostFloor[design], gFront - Gen[design], _p, QuotaBase[design], SoldY[design], SaleY, SaleCap);
    }

    /// <summary>Σ over the fielding stages (С, В) of <c>⌊Σcount_d⌋·upkeep_d</c>: the <c>need</c> of the AI rule (spec A42.4 п. 2).</summary>
    public Fx6 Need()
    {
        Fx6 need = Fx6.Zero;
        for (int d = 0; d < Designs; d++)
        {
            if (Stage[d] is DesignStage.Series or DesignStage.InService)
                need += Fx6.FromInt(Whole(d)) * Upkeep[d];
        }
        return need;
    }
}
