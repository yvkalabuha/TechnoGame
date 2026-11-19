namespace Singularity.Core.Products;

/// <summary>
/// The reference to a design that every layer carries (ADR-0005 p. 3): the slot and its generation (+1 each time the slot is taken, so
/// <c>Generation ≥ 1</c> for any occupied slot). There is no "not supplied" value: <c>default</c> (0, 0) never resolves. The field name in
/// commands is <c>Design</c>/<c>Parent</c>, not <c>Generation</c>, which is the technology generation <c>g*</c> elsewhere.
/// </summary>
public readonly record struct DesignHandle(int Slot, int Generation)
{
    /// <summary>The form check every sender and the validator share: a slot index and a generation of an occupied slot.</summary>
    public bool IsWellFormed(int designs) => Slot >= 0 && Slot < designs && Generation >= 1;
}
