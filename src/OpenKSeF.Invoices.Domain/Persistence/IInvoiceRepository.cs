namespace OpenKSeF.Invoices.Domain.Persistence;

using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.ValueObjects;

/// <summary>
/// Persistence contract for the <see cref="Invoice"/> aggregate.
/// Implementations live in the infrastructure layer.
/// </summary>
public interface IInvoiceRepository
{
    /// <summary>Returns the invoice with the given id, or null if not found.</summary>
    Task<Invoice?> FindByIdAsync(InvoiceId id, CancellationToken ct = default);

    /// <summary>Persists a new or modified invoice.</summary>
    Task SaveAsync(Invoice invoice, CancellationToken ct = default);

    /// <summary>Returns all invoices for the given tenant.</summary>
    Task<IReadOnlyList<Invoice>> ListByTenantAsync(TenantId tenantId, CancellationToken ct = default);
}
