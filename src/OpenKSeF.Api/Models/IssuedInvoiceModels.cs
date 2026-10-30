namespace OpenKSeF.Api.Models;

public record IssuedInvoiceBuyerModel(
    string Kind,
    string? Nip,
    string? Name,
    string? AddressLine1,
    string? AddressLine2);

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
    IReadOnlyList<IssuedInvoiceLineRequest>? Lines);

public record IssuedInvoiceSellerResponse(
    string Nip,
    string? Name,
    string? AddressLine1,
    string? AddressLine2,
    string? BankAccount,
    bool CashAccounting);

public record IssuedInvoiceLineResponse(
    int LineNo,
    string Name,
    string Unit,
    decimal Quantity,
    decimal UnitNetPrice,
    string VatRate,
    decimal NetAmount,
    decimal VatAmount);

public record VatSummaryResponse(string VatRate, decimal Net, decimal Vat);

public record IssuedInvoiceKsefResponse(
    string Environment,
    string? KsefNumber,
    DateTime? AcceptedAt,
    int? StatusCode,
    string? StatusMessage,
    string? LastError,
    string? VerificationUrl,
    bool HasUpo);

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
    DateTime? IssuedAt);

public record IssuedInvoiceListItemResponse(
    Guid Id,
    string Status,
    string? Number,
    string? BuyerName,
    string? BuyerNip,
    DateOnly IssueDate,
    decimal TotalGross,
    string Currency,
    string? KsefNumber);
