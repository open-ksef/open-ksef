namespace OpenKSeF.Invoices.Contracts.Dtos;

/// <summary>
/// API-friendly read DTO projected from the <c>Invoice</c> aggregate.
/// No aggregate internals are exposed directly — always project via <c>IInvoiceReadModelProjector</c>.
/// </summary>
public sealed record InvoiceReadDto(
    Guid Id,
    Guid TenantId,
    string Kind,
    string Status,
    string BuyerKind,
    string KsefSubmissionRequirement,
    string KsefSubmissionState,
    PartyReadDto Seller,
    PartyReadDto Buyer,
    DateTime IssueDate,
    DateTime? SaleDate,
    DateTime? DueDate,
    DateTime? ApprovedAt,
    DateTime? SubmittedToKsefAt,
    DateTime? AcceptedByKsefAt,
    string Currency,
    MoneyReadDto TotalNet,
    MoneyReadDto TotalVat,
    MoneyReadDto TotalGross,
    string? DocumentNumber,
    string? ExternalReference,
    string? PaymentMethod,
    string? PublicNotes,
    string? KsefDocumentNumber,
    string? KsefReferenceNumber,
    string? KsefRejectionReason,
    CorrectionReferenceReadDto? CorrectionReference,
    IReadOnlyList<InvoiceLineReadDto> Lines,
    IReadOnlyList<string> AdvanceDocumentIds,
    IReadOnlyList<AdvanceAllocationReadDto> SettledAdvanceAllocations,
    IReadOnlyList<DuplicateIssuanceReadDto> DuplicateIssuances);

public sealed record PartyReadDto(string Name, string? Nip);

public sealed record MoneyReadDto(decimal Amount, string Currency);

public sealed record InvoiceLineReadDto(
    int LineNumber,
    string Description,
    decimal Quantity,
    string? UnitOfMeasure,
    string PricingMode,
    MoneyReadDto UnitPrice,
    decimal? DiscountPercent,
    string VatRate,
    MoneyReadDto NetAmount,
    MoneyReadDto VatAmount,
    MoneyReadDto GrossAmount,
    string? CorrectionRole);

public sealed record CorrectionReferenceReadDto(
    Guid OriginalInvoiceId,
    string OriginalDocumentNumber,
    string ReasonKind,
    string? ReasonDescription);

public sealed record AdvanceAllocationReadDto(
    Guid AdvanceInvoiceId,
    string AdvanceDocumentNumber,
    MoneyReadDto SettledAmount);

public sealed record DuplicateIssuanceReadDto(
    DateTime IssuedAt,
    string? IssuedBy);
