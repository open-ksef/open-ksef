using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Invoicing;

namespace OpenKSeF.Domain.Services;

public record IssuedInvoiceBuyerInput(
    string? Kind,
    string? Nip,
    string? Name,
    string? AddressLine1,
    string? AddressLine2,
    string? CountryCode = null,
    string? ForeignId = null);

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
    IReadOnlyList<IssuedInvoiceLineInput>? Lines,
    string? Currency = null,
    decimal? ExchangeRate = null,
    DateOnly? ExchangeRateDate = null);

public record IssuedInvoiceListFilter(string? Status, string? Month, string? Query);

public record IssuedFeedRow(
    Guid Id,
    bool IsExternal,
    IssuedInvoiceStatus Status,
    string? Number,
    string? BuyerName,
    string? BuyerNip,
    string? BuyerIdType,
    string? BuyerForeignId,
    string? BuyerCountryCode,
    DateOnly IssueDate,
    DateOnly? DueDate,
    decimal TotalNet,
    decimal TotalGross,
    string Currency,
    string? KsefNumber,
    bool NeedsAttention,
    DateTime SortDate,
    DateTime SeenAt);

public record IssuedInvoiceSeller(
    string Nip,
    string? Name,
    string? AddressLine1,
    string? AddressLine2,
    string? BankAccount,
    bool CashAccounting,
    string? Iban,
    string? Swift,
    string? BankName);

public record IssuedInvoiceView(IssuedInvoice Invoice, IssuedInvoiceSeller Seller, string KsefEnvironment, IReadOnlyList<InvoiceCheck> Warnings);

/// <summary>Draft CRUD, issue, copy and tenant delete. Null means not found; <see cref="InvoiceStateException"/> maps to 409, <see cref="InvoiceValidationException"/> to 400/422.</summary>
public interface IIssuedInvoiceService
{
    IQueryable<IssuedInvoice> Query(Guid tenantId, IssuedInvoiceListFilter filter);

    Task<(IReadOnlyList<IssuedFeedRow> Items, int TotalCount)> FeedAsync(Guid tenantId, IssuedInvoiceListFilter filter, int page, int pageSize, CancellationToken ct = default);

    Task<IssuedInvoiceView?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task<ExchangeRateQuote?> ProposeExchangeRateAsync(string? currency, DateOnly? issueDate, DateOnly? saleDate, CancellationToken ct = default);

    Task<IssuedInvoiceView> CreateAsync(Guid tenantId, IssuedInvoiceDraftInput input, CancellationToken ct = default);

    Task<IssuedInvoiceView?> UpdateAsync(Guid tenantId, Guid id, IssuedInvoiceDraftInput input, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task<(IssuedInvoiceView View, bool Issued)?> IssueAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task<IssuedInvoiceView?> CopyAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task<bool> DeleteTenantAsync(Guid tenantId, CancellationToken ct = default);
}
