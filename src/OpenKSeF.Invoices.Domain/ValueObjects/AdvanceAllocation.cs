namespace OpenKSeF.Invoices.Domain.ValueObjects;

public sealed record AdvanceAllocation(
    InvoiceId AdvanceInvoiceId,
    DocumentNumber AdvanceDocumentNumber,
    Money SettledAmount);
