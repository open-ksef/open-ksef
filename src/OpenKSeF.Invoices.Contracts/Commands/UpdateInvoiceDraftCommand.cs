namespace OpenKSeF.Invoices.Contracts.Commands;

public sealed record UpdateInvoiceDraftCommand(
    Guid InvoiceId,
    DateTime? IssueDate = null,
    DateTime? SaleDate = null,
    DateTime? DueDate = null,
    string? DocumentNumber = null,
    string? ExternalReference = null,
    string? PaymentMethod = null,
    string? PublicNotes = null,
    string? InternalNotes = null,
    IReadOnlyList<UpdateInvoiceDraftLineCommand>? Lines = null);

public sealed record UpdateInvoiceDraftLineCommand(
    int LineNumber,
    string Description,
    decimal Quantity,
    string? UnitOfMeasure,
    Domain.Enums.PricingMode PricingMode,
    decimal UnitPrice,
    decimal? DiscountPercent,
    string VatRate);
