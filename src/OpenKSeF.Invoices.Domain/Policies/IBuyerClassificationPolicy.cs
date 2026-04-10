using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Snapshots;

namespace OpenKSeF.Invoices.Domain.Policies;

/// <summary>
/// Classifies the buyer kind (B2B / B2C / Unknown) based on buyer data and issuer policy.
/// Determines whether KSeF submission is required based on buyer classification.
/// </summary>
public interface IBuyerClassificationPolicy
{
    /// <summary>Resolves the buyer kind from the given buyer snapshot.</summary>
    BuyerKind Classify(BuyerSnapshot buyer);
}
