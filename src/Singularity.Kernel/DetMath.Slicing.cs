using System.Diagnostics;
using System.Numerics;

namespace Singularity.Kernel;

// Day slicing and daily rates (ADR-0027 p. 5 K0; ADR-0019 p. 5-8, 12; ADR-0003 rev 3 p. 4).
public static partial class DetMath
{
    /// <summary>Longest calendar year in days; DayRate and ApplyDayRate are specified for Y up to this.</summary>
    public const int MaxYearLength = 366;

    /// <summary>
    /// Slice <paramref name="k"/> (1..Y) of an annual amount: <c>floor(F·k/Y) − floor(F·(k−1)/Y)</c>.
    /// The Y slices of a constant F sum to F exactly; there is no remainder state (ADR-0019 p. 5).
    /// Monthly code calls it with Y = 12 (ADR-0027 p. 9).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">When Y &lt; 1 or k is outside 1..Y.</exception>
    public static Fx6 DaySlice(Fx6 annual, int k, int y)
    {
        if (y < 1)
            throw new ArgumentOutOfRangeException(nameof(y), y, "The period length must be positive.");
        if (k < 1 || k > y)
            throw new ArgumentOutOfRangeException(nameof(k), k, "The slice index must be in 1..Y.");
        Int128 f = annual.Raw;
        return Fx6.FromRaw(checked((long)(FloorDiv(f * k, y) - FloorDiv(f * (k - 1), y))));
    }

    /// <summary>
    /// Largest-prefix split of <paramref name="total"/> by weights (ADR-0019 p. 8): part i is
    /// <c>floor(F·W_i/W) − floor(F·W_(i−1)/W)</c>, W_i the running weight sum. The parts sum to F
    /// exactly, for any sign of F; the remainder goes by index order. W = 0 gives all zero parts
    /// (F stays with the source). Writes into <paramref name="parts"/>: no allocation.
    /// </summary>
    /// <exception cref="ArgumentException">When the spans differ in length.</exception>
    /// <exception cref="ArgumentOutOfRangeException">When a weight is negative.</exception>
    /// <exception cref="OverflowException">When the weights sum beyond long.</exception>
    public static void Apportion(Fx6 total, ReadOnlySpan<long> weights, Span<Fx6> parts)
    {
        if (parts.Length != weights.Length)
            throw new ArgumentException("parts and weights must have the same length.", nameof(parts));
        long sum = 0;
        foreach (long w in weights)
        {
            if (w < 0)
                throw new ArgumentOutOfRangeException(nameof(weights), w, "Weights must be non-negative.");
            sum = checked(sum + w);
        }

        if (sum == 0)
        {
            parts.Clear();
            return;
        }

        Int128 f = total.Raw;
        long running = 0;
        Int128 previous = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            running += weights[i];
            Int128 cumulative = FloorDiv(f * running, sum);
            parts[i] = Fx6.FromRaw(checked((long)(cumulative - previous)));
            previous = cumulative;
        }
    }

    /// <summary>
    /// Daily rate <c>r</c> with <c>(1 − r)^Y ≈ 1 − δ</c> from an annual rate δ (ADR-0003 rev 3 p. 4,
    /// ADR-0019 p. 12). Negative δ is growth and gives a negative r. Exact integer bisection on Q53,
    /// to be called at load, not on the tick. The error always favours slower change: for decay r is
    /// the largest Q53 value with <c>(1 − r)^Y ≥ 1 − δ</c>; for growth the smallest with <c>≤</c>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">When Y &lt; 1, δ &gt; 1, or growth per day exceeds 100 %.</exception>
    public static Rate DayRate(Fx6 annual, int y)
    {
        if (y < 1 || y > MaxYearLength)
            throw new ArgumentOutOfRangeException(nameof(y), y, "The period length must be in 1..366.");
        long d = annual.Raw;
        if (d > Fx6.Scale)
            throw new ArgumentOutOfRangeException(nameof(annual), annual, "A decay rate above 100 % is not allowed.");
        if (d == 0)
            return Rate.Zero;

        BigInteger one = Rate.One;
        BigInteger oneToY = BigInteger.One << (Rate.FracBits * y);
        // BigInteger subtraction: S − d overflows long for a huge growth rate.
        BigInteger target = (new BigInteger(Fx6.Scale) - d) * oneToY;

        // (Q − r)^Y · 10^6 against (10^6 − d) · Q^Y; the left side falls as r rises.
        bool AtLeastTarget(long r) => BigInteger.Pow(one - r, y) * Fx6.Scale >= target;

        if (d > 0)
        {
            long lo = 0, hi = Rate.One; // invariant: AtLeastTarget(lo)
            while (lo < hi)
            {
                long mid = lo + ((hi - lo + 1) >> 1);
                if (AtLeastTarget(mid))
                    lo = mid;
                else
                    hi = mid - 1;
            }

            return Rate.FromRaw(lo);
        }

        // Growth: smallest r in [−Q, 0] whose left side stays at or below the target.
        long low = -Rate.One, high = 0; // invariant: !AtLeastTarget(high) or the sides are equal
        if (BigInteger.Pow(one - low, y) * Fx6.Scale <= target)
            throw new ArgumentOutOfRangeException(nameof(annual), annual, "Daily growth above 100 % is not allowed.");
        while (low < high)
        {
            long mid = low + ((high - low) >> 1);
            if (BigInteger.Pow(one - mid, y) * Fx6.Scale <= target)
                high = mid;
            else
                low = mid + 1;
        }

        return Rate.FromRaw(low);
    }

    /// <summary>
    /// One daily step of a decaying or growing stock (ADR-0027 p. 6, O-069). Decay: <c>x − r·x</c>.
    /// Growth (negative r): <c>x + (−r)·x</c>. Both floor the change magnitude, so the stock error
    /// always goes toward slower change. Callers use this, never <c>x −= r.MulFloor(x)</c> directly.
    /// Precondition: <c>stock ≥ 0</c> (a negative stock would floor away from slower change).
    /// Error over Y steps from a constant annual δ: decay ends at or above <c>x·(1−δ)</c>; growth ends at or
    /// below it, and the floor losses are amplified by the growth that follows, so the bound is
    /// <c>Y·(1+|δ|)</c> raw (not Y), plus the DayRate quantization.
    /// </summary>
    public static Fx6 ApplyDayRate(Fx6 stock, Rate rate)
    {
        Debug.Assert(stock.Raw >= 0, "ApplyDayRate needs a non-negative stock.");
        return rate.Raw >= 0
            ? stock - rate.MulFloor(stock)
            : stock + Rate.FromRaw(checked(-rate.Raw)).MulFloor(stock);
    }

    private static Int128 FloorDiv(Int128 n, Int128 d)
    {
        Int128 q = n / d;
        // Division truncates towards zero; step down when the inexact quotient is negative (d > 0 here).
        if (n % d < 0)
            q -= 1;
        return q;
    }
}
