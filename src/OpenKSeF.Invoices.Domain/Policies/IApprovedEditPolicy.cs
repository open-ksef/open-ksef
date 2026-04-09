using OpenKSeF.Invoices.Domain.Aggregates;

namespace OpenKSeF.Invoices.Domain.Policies;

/// <summary>
/// Governs whether an approved but not-yet-submitted invoice may be reopened (reverted to Draft).
/// Allows configuring different editable-approved behaviour per tenant or buyer type.
/// </summary>
public interface IApprovedEditPolicy
{
    /// <summary>Returns true if the invoice can be reopened to Draft.</summary>
    bool CanReopen(Invoice invoice);
}
