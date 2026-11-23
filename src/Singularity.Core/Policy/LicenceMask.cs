namespace Singularity.Core.Policy;

/// <summary>
/// Set of node numbers 1…127 as two 64-bit words (H13 of the horizon note): word 0 holds numbers 1…63 (bit <c>n − 1</c>; bit 63 is never used,
/// so the stored <c>long</c> stays non-negative), word 1 holds 64…127 (bit <c>n − 64</c>). A value type: no allocation.
/// </summary>
public readonly record struct LicenceMask(ulong Lo, ulong Hi)
{
    public const int MaxNumber = 127;

    public bool IsEmpty => (Lo | Hi) == 0;

    /// <summary>The mask of one node number; outside 1…127 it throws.</summary>
    public static LicenceMask Of(int number)
    {
        if (number < 1 || number > MaxNumber)
            throw new ArgumentOutOfRangeException(nameof(number), number, "A licence mask holds node numbers 1…127.");
        return number <= 63 ? new LicenceMask(1UL << (number - 1), 0) : new LicenceMask(0, 1UL << (number - 64));
    }

    public bool Has(int number)
    {
        LicenceMask bit = Of(number);
        return (Lo & bit.Lo) != 0 || (Hi & bit.Hi) != 0;
    }

    public static LicenceMask operator |(LicenceMask a, LicenceMask b) => new(a.Lo | b.Lo, a.Hi | b.Hi);
}
