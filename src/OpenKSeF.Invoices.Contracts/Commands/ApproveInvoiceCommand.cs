namespace OpenKSeF.Invoices.Contracts.Commands;

public sealed record ApproveInvoiceCommand(
    Guid InvoiceId,
    DateTime ApprovedAt);
