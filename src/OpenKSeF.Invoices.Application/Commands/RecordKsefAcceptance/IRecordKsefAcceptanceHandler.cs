using OpenKSeF.Invoices.Contracts.Commands;
using OpenKSeF.Invoices.Domain.Aggregates;

namespace OpenKSeF.Invoices.Application.Commands.RecordKsefAcceptance;

public interface IRecordKsefAcceptanceHandler
{
    /// <summary>
    /// Transitions the invoice from SubmittedToKsef to AcceptedByKsef,
    /// persists KSeF identifiers, and locks the aggregate.
    /// </summary>
    void Handle(Invoice invoice, RecordKsefAcceptanceCommand command);
}
