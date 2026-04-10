namespace OpenKSeF.Invoices.Contracts.Commands;

public sealed record RecordKsefAcceptanceCommand(
    Guid InvoiceId,
    string KsefDocumentNumber,
    string KsefReferenceNumber,
    DateTime AcceptedAt);
