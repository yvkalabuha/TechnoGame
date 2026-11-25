namespace Singularity.Kernel;

/// <summary>
/// Rate or probability in Q53 fixed point: <c>long Raw = value * 2^53</c> (ADR-0003 rev 3 p. 4).
/// Range about ±1024. Daily decay and growth rates (negative = growth) and probability thresholds
/// live here, because their values (1e-5..1e-9 per day) are far below the Fx6 step.
/// Core gets a Rate only from Kernel functions (<see cref="DetMath.DayRate"/>, <see cref="FromFx6"/>);
/// <see cref="FromRaw"/> is for reading a stored column.
/// </summary>
public readonly struct Rate : IEquatable<Rate>, IComparable<Rate>
{
    public const int FracBits = 53;

    /// <summary>One in Q53: 2^53.</summary>
    public const long One = 1L << FracBits;

    /// <summary>Raw value: <c>rate * 2^53</c>.</summary>
    public readonly long Raw;

    private Rate(long raw) => Raw = raw;

    public static Rate Zero => default;

    public static Rate FromRaw(long raw) => new(raw);

    /// <summary>Fx6 value to Q53, rounded down: <c>floor(x.Raw * 2^53 / 10^6)</c>.</summary>
    /// <exception cref="OverflowException">When the value is outside the Q53 range.</exception>
    public static Rate FromFx6(Fx6 x)
    {
        Int128 n = (Int128)x.Raw << FracBits;
        Int128 q = n / Fx6.Scale;
        // Integer division truncates towards zero; step down for a negative inexact quotient.
        if (n % Fx6.Scale < 0)
            q -= 1;
        return new(checked((long)q));
    }

    /// <summary>
    /// <c>floor(x.Raw * Raw / 2^53)</c> with one rounding in <see cref="Int128"/>. The shift is
    /// an arithmetic one, so the result is the floor for a negative rate or stock as well.
    /// </summary>
    /// <exception cref="OverflowException">When the result does not fit into Fx6.</exception>
    public Fx6 MulFloor(Fx6 x) => Fx6.FromRaw(checked((long)(((Int128)x.Raw * Raw) >> FracBits)));

    public static bool operator ==(Rate a, Rate b) => a.Raw == b.Raw;
    public static bool operator !=(Rate a, Rate b) => a.Raw != b.Raw;
    public static bool operator <(Rate a, Rate b) => a.Raw < b.Raw;
    public static bool operator >(Rate a, Rate b) => a.Raw > b.Raw;
    public static bool operator <=(Rate a, Rate b) => a.Raw <= b.Raw;
    public static bool operator >=(Rate a, Rate b) => a.Raw >= b.Raw;

    public bool Equals(Rate other) => Raw == other.Raw;
    public override bool Equals(object? obj) => obj is Rate other && Equals(other);
    public override int GetHashCode() => Raw.GetHashCode();
    public int CompareTo(Rate other) => Raw.CompareTo(other.Raw);
    public override string ToString() => $"rate:{Raw}";
}
