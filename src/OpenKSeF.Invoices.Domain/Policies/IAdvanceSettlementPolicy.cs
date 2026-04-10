using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Policies;

/// <summary>
/// Governs how advance invoices may be settled in a final invoice.
/// Validates that allocations are consistent and do not exceed the final invoice gross amount.
/// </summary>
public interface IAdvanceSettlementPolicy
{
    /// <summary>Returns true if the given advance allocations are valid for the final invoice.</summary>
    bool AreAllocationsValid(Invoice finalInvoice, IReadOnlyList<AdvanceAllocation> allocations);
}
