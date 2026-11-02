using System.Globalization;

namespace Singularity.Kernel;

/// <summary>
/// Fixed-point number with 6 decimal digits stored in a <see cref="long"/> (ADR-0003).
/// Used for money, stocks and population. All arithmetic is checked: an overflow
/// throws instead of silently wrapping and corrupting world state.
/// Rounding policy everywhere is floor (towards negative infinity).
/// </summary>
public readonly struct Fx6 : IEquatable<Fx6>, IComparable<Fx6>
{
    public const long Scale = 1_000_000;

    /// <summary>Raw scaled value: <c>value * 10^6</c>.</summary>
    public readonly long Raw;

    private Fx6(long raw) => Raw = raw;

    public static Fx6 Zero => default;
    public static Fx6 One => new(Scale);

    public static Fx6 FromRaw(long raw) => new(raw);

    public static Fx6 FromInt(long value) => new(checked(value * Scale));

    /// <summary>
    /// The only way from <see cref="double"/> into fixed-point: floor(value * 10^6).
    /// NaN and infinities are rejected so behavioural-model errors cannot leak into state.
    /// </summary>
    public static Fx6 FromDouble(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "Value must be finite.");

        double scaled = Math.Floor(value * Scale);
        // 2^63 is exactly representable; long.MaxValue is not, so compare against 2^63.
        if (scaled < -9_223_372_036_854_775_808.0 || scaled >= 9_223_372_036_854_775_808.0)
            throw new OverflowException($"Value {value} does not fit into Fx6.");
        return new((long)scaled);
    }

    public double ToDouble() => (double)Raw / Scale;

    public static Fx6 operator +(Fx6 a, Fx6 b) => new(checked(a.Raw + b.Raw));
    public static Fx6 operator -(Fx6 a, Fx6 b) => new(checked(a.Raw - b.Raw));
    public static Fx6 operator -(Fx6 a) => new(checked(-a.Raw));

    // Products are computed in 128 bits, so only the final result can overflow.
    public static Fx6 operator *(Fx6 a, Fx6 b) =>
        new(checked((long)FloorDiv((Int128)a.Raw * b.Raw, Scale)));

    /// <exception cref="DivideByZeroException">When <paramref name="b"/> is zero.</exception>
    public static Fx6 operator /(Fx6 a, Fx6 b) =>
        new(checked((long)FloorDiv((Int128)a.Raw * Scale, b.Raw)));

    public static bool operator ==(Fx6 a, Fx6 b) => a.Raw == b.Raw;
    public static bool operator !=(Fx6 a, Fx6 b) => a.Raw != b.Raw;
    public static bool operator <(Fx6 a, Fx6 b) => a.Raw < b.Raw;
    public static bool operator >(Fx6 a, Fx6 b) => a.Raw > b.Raw;
    public static bool operator <=(Fx6 a, Fx6 b) => a.Raw <= b.Raw;
    public static bool operator >=(Fx6 a, Fx6 b) => a.Raw >= b.Raw;

    public bool Equals(Fx6 other) => Raw == other.Raw;
    public override bool Equals(object? obj) => obj is Fx6 other && Equals(other);
    public override int GetHashCode() => Raw.GetHashCode();
    public int CompareTo(Fx6 other) => Raw.CompareTo(other.Raw);

    /// <summary>Culture-independent text, always 6 fractional digits, e.g. "-1.500000".</summary>
    public override string ToString() =>
        ((decimal)Raw / Scale).ToString("0.000000", CultureInfo.InvariantCulture);

    private static Int128 FloorDiv(Int128 n, Int128 d)
    {
        Int128 q = n / d;
        Int128 r = n - q * d;
        // Integer division truncates towards zero; step down when the signs differ.
        if (r != 0 && (r < 0) != (d < 0))
            q -= 1;
        return q;
    }
}
