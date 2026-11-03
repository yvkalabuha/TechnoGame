namespace Singularity.Kernel;

/// <summary>
/// Deterministic math for behavioural models (ADR-0003). Only IEEE-754 operations that are
/// correctly rounded (+, -, *, /, sqrt) are used, so results are bit-identical on every
/// platform. Math.Pow/Exp/Log are not, which is why they are banned in world state.
/// None of these methods contains a*b+c, so FMA contraction cannot change their results.
/// </summary>
public static class DetMath
{
    /// <summary>Exponent scale of <see cref="PowFrac"/>: q10 = y * 1024 (ADR-0012).</summary>
    public const int FracOne = 1024;

    private const int FracBits = 10;

    public static double Sqrt(double x)
    {
        if (!double.IsFinite(x) || x < 0)
            throw new ArgumentOutOfRangeException(nameof(x), x, "Sqrt needs a finite non-negative value.");
        return Math.Sqrt(x);
    }

    /// <summary>
    /// x^(eighths/8) for a finite x &gt;= 0. Same as <c>PowFrac(x, eighths * 128)</c>, bit for bit
    /// (ADR-0012: one multiplication order for both).
    /// </summary>
    /// <exception cref="OverflowException">When x^|eighths/8| or the result is not finite.</exception>
    public static double PowDyadic(double x, int eighths)
    {
        CheckArguments(x, eighths);
        int k = Math.Abs(eighths);
        // eighths * 128 can overflow int, so pass integer and fraction parts separately.
        return Finish(PowAbs(x, k >> 3, (k & 7) << 7), x, eighths, 8);
    }

    /// <summary>
    /// x^(q10/1024) for a finite x &gt;= 0 (ADR-0012). The multiplication order is part of the
    /// contract: acc = 1, then acc *= x^(2^-i) for the set fraction bits by ascending i,
    /// then acc *= x^int. A negative exponent is one IEEE division: 1 / x^|q10/1024|.
    /// </summary>
    /// <exception cref="OverflowException">When x^|q10/1024| or the result is not finite.</exception>
    public static double PowFrac(double x, int q10)
    {
        CheckArguments(x, q10);
        int k = Math.Abs(q10);
        return Finish(PowAbs(x, k >> FracBits, k & (FracOne - 1)), x, q10, FracOne);
    }

    /// <summary>
    /// Fixed-point wrapper of <see cref="PowFrac"/> for Core, which has no double (ADR-0003 R2):
    /// <c>FromDouble(PowFrac(x.ToDouble(), q10))</c>, rounded down to 10^-6.
    /// The double round trip has two visible effects: x^1 (q10 = 1024) may return x minus one raw
    /// unit (10^-6), and x = Fx6.FromRaw(long.MaxValue) throws <see cref="OverflowException"/> even
    /// for q10 = 1024, because ToDouble rounds it up to 2^63/10^6, which no longer fits into Fx6.
    /// </summary>
    /// <exception cref="OverflowException">When the result does not fit into Fx6 or double.</exception>
    public static Fx6 PowFracFx(Fx6 x, int q10) => Fx6.FromDouble(PowFrac(x.ToDouble(), q10));

    /// <summary>
    /// Smoothstep on [0; 1] with ONE rounding (contract v2.4 §4.0 p. 2): <c>floor(X²·(3S − 2X) / S²)</c> raw,
    /// X = x.Raw, S = 10^6, all in <see cref="Int128"/>. Two chained Fx6 products would round twice.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">When x is outside [0; 1].</exception>
    public static Fx6 SmoothStep01(Fx6 x)
    {
        if (x.Raw < 0 || x.Raw > Fx6.Scale)
            throw new ArgumentOutOfRangeException(nameof(x), x, "SmoothStep01 needs 0 <= x <= 1.");
        Int128 big = x.Raw;
        const long s = Fx6.Scale;
        // The numerator is non-negative, so integer division is the floor.
        return Fx6.FromRaw((long)(big * big * (3 * s - 2 * big) / ((Int128)s * s)));
    }

    /// <summary>
    /// Product rounded UP (towards +infinity) with one rounding: <c>ceil(a.Raw·b.Raw / S)</c> raw in
    /// <see cref="Int128"/> (contract v2.4 §4.0 p. 2). For a, b &gt;= 0.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">When a or b is negative.</exception>
    public static Fx6 MulCeil(Fx6 a, Fx6 b)
    {
        if (a.Raw < 0 || b.Raw < 0)
            throw new ArgumentOutOfRangeException(a.Raw < 0 ? nameof(a) : nameof(b), "MulCeil needs non-negative factors.");
        Int128 n = (Int128)a.Raw * b.Raw;
        Int128 q = n / Fx6.Scale;
        if (n % Fx6.Scale != 0)
            q += 1;
        return Fx6.FromRaw(checked((long)q));
    }

    private static void CheckArguments(double x, int exponent)
    {
        if (!double.IsFinite(x) || x < 0)
            throw new ArgumentOutOfRangeException(nameof(x), x, "The base must be finite and non-negative.");
        if (x == 0 && exponent < 0)
            throw new ArgumentOutOfRangeException(nameof(exponent), exponent, "Zero cannot be raised to a negative power.");
        if (exponent == int.MinValue)
            throw new ArgumentOutOfRangeException(nameof(exponent), exponent, "Exponent is out of range.");
    }

    /// <summary>x^(whole + frac/1024) for whole, frac &gt;= 0, frac &lt; 1024.</summary>
    private static double PowAbs(double x, int whole, int frac)
    {
        double acc = 1.0;
        double root = x;
        // Bit 2^(10-i) of frac selects root_i = x^(2^-i). Stopping after the lowest set bit
        // skips only roots that are never multiplied, so the result equals the full 10-step loop.
        for (int bit = FracOne >> 1; frac != 0; bit >>= 1)
        {
            root = Math.Sqrt(root);
            if ((frac & bit) != 0)
            {
                acc *= root;
                frac &= ~bit;
            }
        }
        return acc * PowInt(x, whole);
    }

    private static double Finish(double magnitude, double x, int exponent, int denominator)
    {
        // x^|e| overflowing would silently give infinity, or 0 after the reciprocal.
        CheckFinite(magnitude, x, exponent, denominator);
        if (exponent >= 0)
            return magnitude;
        double reciprocal = 1.0 / magnitude;
        CheckFinite(reciprocal, x, exponent, denominator);
        return reciprocal;
    }

    private static void CheckFinite(double value, double x, int exponent, int denominator)
    {
        if (!double.IsFinite(value))
            throw new OverflowException($"{x}^({exponent}/{denominator}) is out of the double range.");
    }

    private static double PowInt(double x, int n)
    {
        double result = 1.0;
        double b = x;
        while (n > 0)
        {
            if ((n & 1) != 0)
                result *= b;
            b *= b;
            n >>= 1;
        }
        return result;
    }
}
