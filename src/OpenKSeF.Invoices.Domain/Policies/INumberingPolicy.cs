using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Policies;

/// <summary>
/// Assigns a <see cref="DocumentNumber"/> to an invoice.
/// Implementations may assign numbers at Draft or at Approve time depending on business rules.
/// </summary>
public interface INumberingPolicy
{
    /// <summary>Returns true if a number should be assigned on approval (rather than on draft).</summary>
    bool AssignOnApproval { get; }

    /// <summary>Generates the next document number for the given invoice context.</summary>
    DocumentNumber AssignNumber(Invoice invoice);
}
