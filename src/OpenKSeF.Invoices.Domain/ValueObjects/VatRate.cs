namespace OpenKSeF.Invoices.Domain.ValueObjects;

/// <summary>
/// Discriminated union: either a percentage rate (e.g. 23%) or a tax-exemption reason.
/// Represents a resolved VAT treatment for a line or document.
/// </summary>
public sealed class VatRate : IEquatable<VatRate>
{
    public static readonly VatRate Zero = OfPercentage(new Percentage(0));

    private VatRate(Percentage? rate, TaxExemptionReason? exemption)
    {
        Rate = rate;
        ExemptionReason = exemption;
    }

    public Percentage? Rate { get; }
    public TaxExemptionReason? ExemptionReason { get; }
    public bool IsExempt => ExemptionReason is not null;

    /// <summary>Effective rate as fraction for calculation (0 when exempt).</summary>
    public decimal EffectiveFraction => Rate?.AsFraction ?? 0m;

    public static VatRate OfPercentage(Percentage rate) => new(rate, null);

    public static VatRate OfExemption(TaxExemptionReason reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        return new VatRate(null, reason);
    }

    public bool Equals(VatRate? other)
    {
        if (other is null) return false;
        return Rate == other.Rate && ExemptionReason == other.ExemptionReason;
    }

    public override bool Equals(object? obj) => obj is VatRate other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Rate, ExemptionReason);
    public override string ToString() => IsExempt ? $"Exempt({ExemptionReason!.Code})" : $"{Rate!.Value}%";
}
