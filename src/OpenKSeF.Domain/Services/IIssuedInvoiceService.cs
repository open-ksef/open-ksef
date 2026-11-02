using OpenKSeF.Domain.Invoicing;

namespace OpenKSeF.Domain.Services;

public record IssuedInvoiceBuyerInput(string? Kind, string? Nip, string? Name, string? AddressLine1, string? AddressLine2);

public record IssuedInvoiceLineInput(string? Name, string? Unit, decimal Quantity, decimal UnitNetPrice, string? VatRate);

public record IssuedInvoiceDraftInput(
    IssuedInvoiceBuyerInput? Buyer,
    DateOnly? IssueDate,
    DateOnly? SaleDate,
    DateOnly? DueDate,
    string? PaymentMethod,
    bool SplitPayment,
    string? ExemptionBasisKind,
    string? ExemptionBasisText,
    IReadOnlyList<IssuedInvoiceLineInput>? Lines);

public record IssuedInvoiceListFilter(string? Status, string? Month, string? Query);

public record IssuedInvoiceSeller(string Nip, string? Name, string? AddressLine1, string? AddressLine2, string? BankAccount, bool CashAccounting);

public record IssuedInvoiceView(IssuedInvoice Invoice, IssuedInvoiceSeller Seller, string KsefEnvironment, IReadOnlyList<InvoiceCheck> Warnings);

/// <summary>Draft CRUD, issue, copy and tenant delete. Null means not found; <see cref="InvoiceStateException"/> maps to 409, <see cref="InvoiceValidationException"/> to 400/422.</summary>
public interface IIssuedInvoiceService
{
    IQueryable<IssuedInvoice> Query(Guid tenantId, IssuedInvoiceListFilter filter);

    Task<IssuedInvoiceView?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task<IssuedInvoiceView> CreateAsync(Guid tenantId, IssuedInvoiceDraftInput input, CancellationToken ct = default);

    Task<IssuedInvoiceView?> UpdateAsync(Guid tenantId, Guid id, IssuedInvoiceDraftInput input, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task<(IssuedInvoiceView View, bool Issued)?> IssueAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task<IssuedInvoiceView?> CopyAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task<bool> DeleteTenantAsync(Guid tenantId, CancellationToken ct = default);
}
