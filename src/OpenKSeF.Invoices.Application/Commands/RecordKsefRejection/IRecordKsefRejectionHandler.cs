using OpenKSeF.Invoices.Contracts.Commands;
using OpenKSeF.Invoices.Domain.Aggregates;

namespace OpenKSeF.Invoices.Application.Commands.RecordKsefRejection;

public interface IRecordKsefRejectionHandler
{
    /// <summary>
    /// Transitions the invoice from SubmittedToKsef to RejectedByKsef,
    /// stores the rejection reason, and allows the document to be corrected and resubmitted.
    /// </summary>
    void Handle(Invoice invoice, RecordKsefRejectionCommand command);
}
