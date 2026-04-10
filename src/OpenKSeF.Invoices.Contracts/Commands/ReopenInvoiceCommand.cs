namespace OpenKSeF.Invoices.Contracts.Commands;

public sealed record ReopenInvoiceCommand(
    Guid InvoiceId,
    DateTime ReopenedAt);
