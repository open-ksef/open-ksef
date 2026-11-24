using OpenKSeF.Domain.Models;

namespace OpenKSeF.Api.Models;

public record IssuedInvoiceBuyerModel(
    string Kind,
    string? Nip,
    string? Name,
    string? AddressLine1,
    string? AddressLine2,
    string? CountryCode = null,
    string? IdType = null,
    string? ForeignId = null);

public record ExemptionBasisModel(string Kind, string? Text);

public record IssuedInvoiceLineRequest(
    string? Name,
    string? Unit,
    decimal Quantity,
    decimal UnitNetPrice,
    string? VatRate);

public record IssuedInvoiceDraftRequest(
    IssuedInvoiceBuyerModel? Buyer,
    DateOnly? IssueDate,
    DateOnly? SaleDate,
    DateOnly? DueDate,
    string? PaymentMethod,
    bool SplitPayment,
    ExemptionBasisModel? ExemptionBasis,
    IReadOnlyList<IssuedInvoiceLineRequest>? Lines,
    string? Currency = null,
    decimal? ExchangeRate = null,
    DateOnly? ExchangeRateDate = null);

public record IssuedInvoiceSellerResponse(
    string Nip,
    string? Name,
    string? AddressLine1,
    string? AddressLine2,
    string? BankAccount,
    bool CashAccounting,
    string? Iban = null,
    string? Swift = null,
    string? BankName = null);

public record IssuedInvoiceLineResponse(
    int LineNo,
    string Name,
    string Unit,
    decimal Quantity,
    decimal UnitNetPrice,
    string VatRate,
    decimal NetAmount,
    decimal VatAmount,
    int? BaseLineNo = null);

public record VatSummaryResponse(string VatRate, decimal Net, decimal Vat);

public record IssuedInvoiceKsefResponse(
    string Environment,
    string? KsefNumber,
    DateTime? AcceptedAt,
    int? StatusCode,
    string? StatusMessage,
    string? StatusDetails,
    string? LastError,
    string? VerificationUrl,
    bool HasUpo,
    string? DuplicateKsefNumber,
    DateOnly? AcceptedOn,
    bool UpoUnavailable);

public record IssuedInvoiceResponse(
    Guid Id,
    string Status,
    string? Number,
    IssuedInvoiceSellerResponse Seller,
    IssuedInvoiceBuyerModel Buyer,
    DateOnly IssueDate,
    DateOnly SaleDate,
    DateOnly DueDate,
    string PaymentMethod,
    bool SplitPayment,
    ExemptionBasisModel? ExemptionBasis,
    IReadOnlyList<IssuedInvoiceLineResponse> Lines,
    IReadOnlyList<VatSummaryResponse> VatSummary,
    decimal TotalNet,
    decimal TotalVat,
    decimal TotalGross,
    string Currency,
    IssuedInvoiceKsefResponse Ksef,
    IReadOnlyList<ValidationIssue> Warnings,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? IssuedAt,
    decimal? ExchangeRate = null,
    DateOnly? ExchangeRateDate = null,
    uint Version = 0,
    uint SellerVersion = 0,
    string InvoiceType = "VAT",
    CorrectedInvoiceRefResponse? CorrectedInvoice = null,
    string? CorrectionReason = null,
    int? CorrectionType = null,
    IReadOnlyList<CorrectionRowResponse>? CorrectionRows = null,
    IssuedInvoiceBuyerModel? BuyerBefore = null,
    CurrentStateResponse? CurrentState = null,
    IReadOnlyList<CorrectionListItemResponse>? Corrections = null,
    Guid? OpenCorrectionId = null,
    Guid? OrderChainId = null,
    decimal? AdvanceAmount = null,
    decimal? AmountBefore = null,
    IReadOnlyList<AdvanceInvoiceRefResponse>? AdvanceInvoices = null,
    OrderChainResponse? Chain = null);

public record AdvanceInvoiceRefResponse(Guid Id, string? Number, string? KsefNumber);

public record OrderChainResponse(
    decimal OrderValue,
    IReadOnlyList<CorrectionListItemResponse> Advances,
    CorrectionListItemResponse? Final,
    IReadOnlyList<CorrectionListItemResponse> Corrections,
    decimal Remaining,
    Guid? OpenDocumentId);

public record AdvanceDraftRequest(
    IssuedInvoiceBuyerModel? Buyer = null,
    DateOnly? IssueDate = null,
    DateOnly? PaymentDate = null,
    decimal? AdvanceAmount = null,
    string? PaymentMethod = null,
    string? Currency = null,
    decimal? ExchangeRate = null,
    DateOnly? ExchangeRateDate = null,
    ExemptionBasisModel? ExemptionBasis = null,
    IReadOnlyList<IssuedInvoiceLineRequest>? Lines = null);

public record FinalDraftRequest(
    DateOnly? IssueDate = null,
    DateOnly? SaleDate = null,
    DateOnly? DueDate = null,
    string? PaymentMethod = null,
    bool? SplitPayment = null,
    decimal? ExchangeRate = null,
    DateOnly? ExchangeRateDate = null,
    ExemptionBasisModel? ExemptionBasis = null,
    IReadOnlyList<IssuedInvoiceLineRequest>? Lines = null,
    IssuedInvoiceBuyerModel? Buyer = null,
    string? Currency = null);

public record CorrectedInvoiceRefResponse(Guid Id, string? Number, string? KsefNumber, DateOnly IssueDate, string InvoiceType);

public record CorrectionRowResponse(
    int RowNo,
    bool IsBefore,
    int? BaseLineNo,
    string Name,
    string Unit,
    decimal Quantity,
    decimal UnitNetPrice,
    string VatRate,
    decimal NetAmount);

public record CurrentStateResponse(
    IReadOnlyList<IssuedInvoiceLineResponse> Lines,
    IReadOnlyList<VatSummaryResponse> VatSummary,
    decimal TotalNet,
    decimal TotalVat,
    decimal TotalGross,
    IssuedInvoiceBuyerModel Buyer,
    bool IsZero);

public record CorrectionListItemResponse(Guid Id, string? Number, string InvoiceType, string Status, DateOnly IssueDate, decimal TotalGross);

public record CorrectionCreateRequest(string? Mode = null);

public record CorrectionLineRequest(
    int? BaseLineNo,
    string? Name,
    string? Unit,
    decimal Quantity,
    decimal UnitNetPrice,
    string? VatRate);

public record CorrectionDraftRequest(
    DateOnly? IssueDate = null,
    DateOnly? DueDate = null,
    string? PaymentMethod = null,
    string? CorrectionReason = null,
    int? CorrectionType = null,
    IssuedInvoiceBuyerModel? Buyer = null,
    IReadOnlyList<CorrectionLineRequest>? Lines = null,
    decimal? AdvanceAmount = null);

public record ExchangeRateProposalResponse(string Currency, decimal? Rate, DateOnly? TableDate);

public record IssuedInvoiceListItemResponse(
    Guid Id,
    string Status,
    string? Number,
    string? BuyerName,
    string? BuyerNip,
    DateOnly IssueDate,
    DateOnly DueDate,
    decimal TotalNet,
    decimal TotalGross,
    string Currency,
    string? KsefNumber,
    bool NeedsAttention,
    string? BuyerCountryCode = null,
    string? BuyerIdType = null,
    string? BuyerForeignId = null,
    string InvoiceType = "VAT");

public record IssuedFeedItemResponse(
    Guid Id,
    string Source,
    string Status,
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
    string InvoiceType = "VAT");

public class IssuedFeedPageResponse : PagedResult<IssuedFeedItemResponse>
{
    public bool IssuedFetchIncomplete { get; init; }
}

public record RecentBuyerResponse(IssuedInvoiceBuyerModel Buyer, DateOnly LastIssueDate);

public record LineSuggestionResponse(string Name, string Unit, decimal UnitNetPrice, string VatRate);

public record InvoiceTemplateRequest(string? Name);

public record InvoiceTemplateResponse(
    Guid Id,
    string Name,
    IssuedInvoiceBuyerModel Buyer,
    string Currency,
    int PaymentDays,
    int LineCount,
    DateTime CreatedAt);
