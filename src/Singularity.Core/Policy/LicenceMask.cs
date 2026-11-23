namespace Singularity.Core.Policy;

/// <summary>
/// Set of node numbers as two 64-bit words (H13 of the horizon note): word 0 holds numbers 1…63 (bit <c>n − 1</c>; bit 63 is never used,
/// so the stored <c>long</c> stays non-negative), word 1 holds 64…127 (bit <c>n − 64</c>). A value type: no allocation.
/// The width is part of the value: a mask read from a policy without the second word is narrow (<see cref="Capacity"/> 63), and
/// <see cref="Has"/> throws above its capacity instead of answering <c>false</c>. Equality compares the bits only.
/// </summary>
public readonly struct LicenceMask : IEquatable<LicenceMask>
{
    public const int MaxNumber = 127;
    public const int NarrowMaxNumber = 63;

    private readonly bool _narrow;

    public LicenceMask(ulong lo, ulong hi) : this(lo, hi, false) { }

    public LicenceMask(ulong lo, ulong hi, bool narrow)
    {
        if (narrow && hi != 0)
            throw new ArgumentException("A narrow licence mask has no second word.", nameof(hi));
        Lo = lo;
        Hi = hi;
        _narrow = narrow;
    }

    public ulong Lo { get; }

    public ulong Hi { get; }

    /// <summary>Highest node number this mask can hold: 63 for a narrow one, else 127.</summary>
    public int Capacity => _narrow ? NarrowMaxNumber : MaxNumber;

    public bool IsEmpty => (Lo | Hi) == 0;

    /// <summary>The mask of one node number (full width); outside 1…127 it throws.</summary>
    public static LicenceMask Of(int number)
    {
        if (number < 1 || number > MaxNumber)
            throw new ArgumentOutOfRangeException(nameof(number), number, "A licence mask holds node numbers 1…127.");
        return number <= 63 ? new LicenceMask(1UL << (number - 1), 0) : new LicenceMask(0, 1UL << (number - 64));
    }

    /// <summary>Whether the mask holds the node; a number above the capacity of this mask (or below 1) throws.</summary>
    public bool Has(int number)
    {
        if (number < 1 || number > Capacity)
            throw new ArgumentOutOfRangeException(nameof(number), number, $"This licence mask holds node numbers 1…{Capacity}.");
        LicenceMask bit = Of(number);
        return (Lo & bit.Lo) != 0 || (Hi & bit.Hi) != 0;
    }

    public static LicenceMask operator |(LicenceMask a, LicenceMask b) => new(a.Lo | b.Lo, a.Hi | b.Hi, a._narrow && b._narrow);

    public bool Equals(LicenceMask other) => Lo == other.Lo && Hi == other.Hi;

    public override bool Equals(object? obj) => obj is LicenceMask m && Equals(m);

    public override int GetHashCode() => HashCode.Combine(Lo, Hi);

    public static bool operator ==(LicenceMask a, LicenceMask b) => a.Equals(b);

    public static bool operator !=(LicenceMask a, LicenceMask b) => !a.Equals(b);

    public override string ToString() => $"{Hi:x16}{Lo:x16}";
}
