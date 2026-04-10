using OpenKSeF.Invoices.Domain.Enums;

namespace OpenKSeF.Invoices.Domain.ValueObjects;

public sealed record CorrectionReference(
    InvoiceId OriginalInvoiceId,
    DocumentNumber OriginalDocumentNumber,
    CorrectionReasonKind ReasonKind,
    string? ReasonDescription = null,
    InvoiceId? RootOriginalInvoiceId = null)
{
    public InvoiceId EffectiveRootOriginalInvoiceId => RootOriginalInvoiceId ?? OriginalInvoiceId;

    public static CorrectionReference NormalizeFrom(
        InvoiceId latestReferenceId,
        DocumentNumber latestReferenceNumber,
        CorrectionReasonKind reasonKind,
        string? reasonDescription = null,
        CorrectionReference? previousCorrection = null) =>
        new(
            latestReferenceId,
            latestReferenceNumber,
            reasonKind,
            reasonDescription,
            previousCorrection?.EffectiveRootOriginalInvoiceId ?? latestReferenceId);
}
