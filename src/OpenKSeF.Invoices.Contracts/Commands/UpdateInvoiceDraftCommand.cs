namespace OpenKSeF.Invoices.Contracts.Commands;

public sealed record UpdateInvoiceDraftCommand(
    Guid InvoiceId,
    DateTime IssueDate,
    DateTime? SaleDate = null,
    DateTime? DueDate = null,
    string? DocumentNumber = null,
    string? PaymentMethod = null,
    string? PublicNotes = null,
    string? InternalNotes = null);
