using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Policies;

/// <summary>
/// Governs which VAT rates and exemption codes are permitted,
/// and how rounding should be applied within legally allowed bounds.
/// </summary>
public interface IVatPolicy
{
    /// <summary>Returns the set of allowed percentage VAT rates (e.g. 23, 8, 5, 0).</summary>
    IReadOnlySet<decimal> AllowedRates { get; }

    /// <summary>Returns true if the given exemption code is valid.</summary>
    bool IsExemptionCodeValid(string code);

    /// <summary>Rounds a monetary amount according to the configured rounding strategy.</summary>
    decimal Round(decimal amount);
}
