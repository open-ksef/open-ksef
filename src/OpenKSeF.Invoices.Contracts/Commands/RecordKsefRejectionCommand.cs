namespace OpenKSeF.Invoices.Contracts.Commands;

public sealed record RecordKsefRejectionCommand(
    Guid InvoiceId,
    string RejectionReason,
    DateTime RejectedAt);
