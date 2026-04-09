using OpenKSeF.Invoices.Domain.Enums;

namespace OpenKSeF.Invoices.Domain.ValueObjects;

public sealed record CorrectionReference(
    InvoiceId OriginalInvoiceId,
    DocumentNumber OriginalDocumentNumber,
    CorrectionReasonKind ReasonKind,
    string? ReasonDescription = null);
