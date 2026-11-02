namespace Singularity.Kernel;

/// <summary>
/// Deterministic math for behavioural models (ADR-0003). Only IEEE-754 operations that are
/// correctly rounded (+, -, *, /, sqrt) are used, so results are bit-identical on every
/// platform. Math.Pow/Exp/Log are not, which is why they are banned in world state.
/// </summary>
public static class DetMath
{
    public static double Sqrt(double x)
    {
        if (!double.IsFinite(x) || x < 0)
            throw new ArgumentOutOfRangeException(nameof(x), x, "Sqrt needs a finite non-negative value.");
        return Math.Sqrt(x);
    }

    /// <summary>
    /// x^(eighths/8) for a finite x &gt;= 0. The fractional part uses x^(1/2), x^(1/4), x^(1/8)
    /// from repeated square roots, the integer part uses exponentiation by squaring.
    /// The result may differ from the exact power by a few ulps, but it is always the same.
    /// </summary>
    /// <exception cref="OverflowException">When x^|eighths/8| or the result is not finite.</exception>
    public static double PowDyadic(double x, int eighths)
    {
        if (!double.IsFinite(x) || x < 0)
            throw new ArgumentOutOfRangeException(nameof(x), x, "PowDyadic needs a finite non-negative base.");
        if (x == 0 && eighths < 0)
            throw new ArgumentOutOfRangeException(nameof(eighths), eighths, "Zero cannot be raised to a negative power.");
        if (eighths == int.MinValue)
            throw new ArgumentOutOfRangeException(nameof(eighths), eighths, "Exponent is out of range.");

        int k = Math.Abs(eighths);
        int frac = k & 7;

        double result = PowInt(x, k >> 3);
        double root = x;
        // Bits of frac, from 4/8 (sqrt) down to 1/8 (third sqrt).
        for (int bit = 4; bit >= 1; bit >>= 1)
        {
            root = Math.Sqrt(root);
            if ((frac & bit) != 0)
                result *= root;
        }

        // x^|e| overflowing would silently give infinity, or 0 after the reciprocal.
        CheckFinite(result, x, eighths);
        if (eighths >= 0)
            return result;
        double reciprocal = 1.0 / result;
        CheckFinite(reciprocal, x, eighths);
        return reciprocal;
    }

    private static void CheckFinite(double value, double x, int eighths)
    {
        if (!double.IsFinite(value))
            throw new OverflowException($"PowDyadic({x}, {eighths}/8) is out of the double range.");
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
