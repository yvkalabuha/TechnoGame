using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

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
    /// <exception cref="ArgumentException">When the spans differ in length or overlap.</exception>
    /// <exception cref="ArgumentOutOfRangeException">When a weight is negative.</exception>
    /// <exception cref="OverflowException">When the weights sum beyond long.</exception>
    public static void Apportion(Fx6 total, ReadOnlySpan<long> weights, Span<Fx6> parts)
    {
        if (parts.Length != weights.Length)
            throw new ArgumentException("parts and weights must have the same length.", nameof(parts));
        // Writing parts while weights are still unread would break the exact sum.
        if (MemoryMarshal.Cast<Fx6, long>(parts).Overlaps(weights))
            throw new ArgumentException("parts must not overlap weights.", nameof(parts));
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
    /// <exception cref="ArgumentOutOfRangeException">When Y is outside 1..366, δ &gt; 100 % (decay) or the growth per step would be 100 % or more.</exception>
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
        long low = -Rate.One, high = 0;
        while (low < high)
        {
            long mid = low + ((high - low) >> 1);
            if (BigInteger.Pow(one - mid, y) * Fx6.Scale <= target)
                high = mid;
            else
                low = mid + 1;
        }

        // r = −Q means a step of 100 % or more (O-069): no valid daily rate.
        if (low == -Rate.One)
            throw new ArgumentOutOfRangeException(nameof(annual), annual, "Growth of 100 % or more per step is not allowed.");
        return Rate.FromRaw(low);
    }

    /// <summary>
    /// One daily step of a decaying or growing stock (ADR-0027 p. 6, O-069, owner decision). The stock is
    /// <c>x</c> plus a hidden sub-raw part kept in <paramref name="remainder"/> (Q53, a caller-owned column of
    /// the same row, start 0): the true value is <c>V = x·2^53 + remainder</c>. The step changes V by the
    /// floor of <c>V·|r|</c>, decay downwards and growth (negative r) upwards, so V never moves faster than
    /// the exact rate. Then x is rounded the slower way: up for decay (<c>remainder ≤ 0</c>), down for
    /// growth (<c>remainder ≥ 0</c>). Hence after Y steps from a constant annual δ the stock is on the slow
    /// side of <c>x₀·(1−δ)</c> and within Y raw of it plus the DayRate quantization (about
    /// 4·Y·x₀·(1+|δ|)/2^53 raw: the rate error is up to Y·(1+|δ|) units of 2^-53, O-070),
    /// provided the rate came from <see cref="DayRate"/> (Y ≤ 366, growth below 100 % a step).
    /// The visible stock never moves against the sign of the rate (decay never rises, growth never falls),
    /// even when the rate changes sign on the same row (O-078). Callers use this, never <c>x −= r.MulFloor(x)</c>. The remainder is written only when the step
    /// succeeds. Preconditions: <c>stock ≥ 0</c>, <c>|remainder| &lt; 2^53</c>, <c>V ≥ 0</c>, <c>|r| ≤ 2^53</c>.
    /// </summary>
    public static Fx6 ApplyDayRate(Fx6 stock, Rate rate, ref long remainder)
    {
        Debug.Assert(stock.Raw >= 0, "ApplyDayRate needs a non-negative stock.");
        Debug.Assert(remainder > -Rate.One && remainder < Rate.One, "remainder must be within ±2^53.");
        if (rate.Raw == 0)
            return stock; // a zero rate must not round the hidden part up into the visible stock
        Int128 v = ((Int128)stock.Raw << Rate.FracBits) + remainder;
        Debug.Assert(v >= 0, "The hidden value must not be negative.");
        bool growth = rate.Raw < 0;
        Int128 abs = growth ? -(Int128)rate.Raw : rate.Raw;
        // floor(V·|r|/2^53) = x·|r| + floor(remainder·|r|/2^53); no Int128 overflow for |r| ≤ 2^53.
        Int128 change = (Int128)stock.Raw * abs + (((Int128)remainder * abs) >> Rate.FracBits);
        Int128 next = growth ? v + change : v - change;
        Debug.Assert(next >= 0, "The rate must not exceed 100 % a day.");
        // Clamp: a leftover remainder of the other sign must not move the stock against the rate (O-078).
        Int128 whole = growth
            ? Int128.Max(next >> Rate.FracBits, stock.Raw)
            : Int128.Min((next + (Rate.One - 1)) >> Rate.FracBits, stock.Raw);
        long newStock = checked((long)whole);
        remainder = (long)(next - (whole << Rate.FracBits));
        return Fx6.FromRaw(newStock);
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
