using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Policies;

/// <summary>
/// Checks whether a proposed document number is unique within the tenant's scope.
/// </summary>
public interface IDocumentUniquenessPolicy
{
    /// <summary>Returns true if the number is already used by another document for the given tenant.</summary>
    bool IsDuplicate(TenantId tenantId, DocumentNumber number);
}
