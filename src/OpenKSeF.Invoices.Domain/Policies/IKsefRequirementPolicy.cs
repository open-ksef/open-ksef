using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;

namespace OpenKSeF.Invoices.Domain.Policies;

/// <summary>
/// Determines whether KSeF submission is required, optional, forbidden, or not applicable
/// for a given invoice, based on buyer classification, document kind, and tenant configuration.
/// </summary>
public interface IKsefRequirementPolicy
{
    KsefSubmissionRequirement Resolve(Invoice invoice);
}
