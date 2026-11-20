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

public record IssuedInvoiceLineInput(string? Name, string? Unit, decimal Quantity, decimal UnitNetPrice, string? VatRate, int? BaseLineNo = null);

public record IssuedInvoiceCorrectionInput(
    DateOnly? IssueDate,
    DateOnly? DueDate,
    string? PaymentMethod,
    string? CorrectionReason,
    int? CorrectionType,
    IssuedInvoiceBuyerInput? Buyer,
    string? BuyerIdType,
    IReadOnlyList<IssuedInvoiceLineInput>? Lines,
    decimal? AdvanceAmount = null);

public record IssuedInvoiceAdvanceInput(
    IssuedInvoiceBuyerInput? Buyer,
    DateOnly? IssueDate,
    DateOnly? PaymentDate,
    decimal? AdvanceAmount,
    string? PaymentMethod,
    string? Currency,
    decimal? ExchangeRate,
    DateOnly? ExchangeRateDate,
    string? ExemptionBasisKind,
    string? ExemptionBasisText,
    IReadOnlyList<IssuedInvoiceLineInput>? Lines);

public record IssuedInvoiceFinalInput(
    DateOnly? IssueDate,
    DateOnly? SaleDate,
    DateOnly? DueDate,
    string? PaymentMethod,
    bool? SplitPayment,
    decimal? ExchangeRate,
    DateOnly? ExchangeRateDate,
    string? ExemptionBasisKind,
    string? ExemptionBasisText,
    IReadOnlyList<IssuedInvoiceLineInput>? Lines,
    IssuedInvoiceBuyerInput? Buyer,
    string? Currency);

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
    DateTime SeenAt,
    string InvoiceType);

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

public record IssuedInvoiceView(IssuedInvoice Invoice, IssuedInvoiceSeller Seller, string KsefEnvironment, IReadOnlyList<InvoiceCheck> Warnings, uint Version, uint SellerVersion = 0)
{
    /// <summary>Set on a correction: the original it names in DaneFaKorygowanej.</summary>
    public IssuedInvoice? Original { get; init; }

    /// <summary>Set on a correction: the state it corrects.</summary>
    public IssuedInvoice? CorrectedState { get; init; }

    /// <summary>Set on an accepted VAT original: its corrections in any status.</summary>
    public IReadOnlyList<IssuedInvoice> Corrections { get; init; } = [];

    /// <summary>Set on an accepted VAT original with at least one accepted correction.</summary>
    public IssuedInvoice? CurrentState { get; init; }

    /// <summary>Set on KOR_ZAL and KOR_ROZ: P_15ZK.</summary>
    public decimal? AmountBefore { get; init; }

    /// <summary>Set on a ROZ and on the last ZAL: the advances listed in FakturaZaliczkowa.</summary>
    public IReadOnlyList<IssuedInvoice> AdvanceInvoices { get; init; } = [];

    /// <summary>Set on a ROZ: the split per rate of the advances it deducts.</summary>
    public IReadOnlyList<VatSummary>? Deducted { get; init; }

    /// <summary>Set on every document of an order chain.</summary>
    public OrderChainView? Chain { get; init; }
}

/// <summary>Order chain of a document: order value of the latest order, advances, ROZ, corrections, remaining value and the open document.</summary>
public record OrderChainView(
    decimal OrderValue,
    IReadOnlyList<IssuedInvoice> Advances,
    IssuedInvoice? Final,
    IReadOnlyList<IssuedInvoice> Corrections,
    decimal Remaining,
    Guid? OpenDocumentId);

/// <summary>Draft CRUD, issue, copy and tenant delete. Null means not found; <see cref="InvoiceStateException"/> maps to 409, <see cref="InvoiceValidationException"/> to 400/422.</summary>
public interface IIssuedInvoiceService
{
    IQueryable<IssuedInvoice> Query(Guid tenantId, IssuedInvoiceListFilter filter);

    Task<(IReadOnlyList<IssuedFeedRow> Items, int TotalCount)> FeedAsync(Guid tenantId, IssuedInvoiceListFilter filter, int page, int pageSize, CancellationToken ct = default);

    Task<IssuedInvoiceView?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task<ExchangeRateQuote?> ProposeExchangeRateAsync(string? currency, DateOnly? issueDate, DateOnly? saleDate, CancellationToken ct = default);

    Task<IssuedInvoiceView> CreateAsync(Guid tenantId, IssuedInvoiceDraftInput input, CancellationToken ct = default);

    Task<IssuedInvoiceView?> UpdateAsync(Guid tenantId, Guid id, IssuedInvoiceDraftInput input, uint? expectedVersion = null, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid tenantId, Guid id, uint? expectedVersion = null, CancellationToken ct = default);

    Task<(IssuedInvoiceView View, bool Issued)?> IssueAsync(Guid tenantId, Guid id, uint? expectedVersion = null, bool confirmPastIssueDate = false, uint? expectedSellerVersion = null, string? invoiceType = null, CancellationToken ct = default);

    Task<IssuedInvoiceView?> CopyAsync(Guid tenantId, Guid id, string? invoiceType = null, CancellationToken ct = default);

    Task<IssuedInvoiceView?> CreateCorrectionAsync(Guid tenantId, Guid originalId, string? mode, CancellationToken ct = default);

    Task<IssuedInvoiceView?> UpdateCorrectionAsync(Guid tenantId, Guid id, IssuedInvoiceCorrectionInput input, uint? expectedVersion = null, CancellationToken ct = default);

    Task<IssuedInvoiceView?> ReissueAsync(Guid tenantId, Guid originalId, CancellationToken ct = default);

    Task<IssuedInvoiceView?> CreateAdvanceAsync(Guid tenantId, IssuedInvoiceAdvanceInput? input, CancellationToken ct = default);

    Task<IssuedInvoiceView?> NextAdvanceAsync(Guid tenantId, Guid advanceId, CancellationToken ct = default);

    Task<IssuedInvoiceView?> UpdateAdvanceAsync(Guid tenantId, Guid id, IssuedInvoiceAdvanceInput input, uint? expectedVersion = null, CancellationToken ct = default);

    Task<IssuedInvoiceView?> CreateFinalAsync(Guid tenantId, Guid advanceId, CancellationToken ct = default);

    Task<IssuedInvoiceView?> UpdateFinalAsync(Guid tenantId, Guid id, IssuedInvoiceFinalInput input, uint? expectedVersion = null, CancellationToken ct = default);

    Task<bool> DeleteTenantAsync(Guid tenantId, CancellationToken ct = default);
}
