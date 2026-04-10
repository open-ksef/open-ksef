using OpenKSeF.Invoices.Domain.Aggregates;

namespace OpenKSeF.Api.Services;

public sealed class InvoiceAggregateMutationService
{
    public void Submit(Invoice invoice, DateTime submittedAt)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        invoice.SubmitToKsef(submittedAt);
    }

    public void RecordDuplicateIssue(Invoice invoice, DateTime issuedAt, string? issuedBy)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        invoice.RecordDuplicateIssue(issuedAt, issuedBy);
    }
}
